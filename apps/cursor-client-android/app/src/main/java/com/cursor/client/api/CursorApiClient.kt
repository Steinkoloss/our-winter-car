package com.cursor.client.api

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.withContext
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.sse.EventSource
import okhttp3.sse.EventSourceListener
import okhttp3.sse.EventSources
import java.util.concurrent.TimeUnit

class CursorApiClient(
  private val apiKey: String,
  private val httpClient: OkHttpClient = defaultClient(),
) {
    private val json = Json {
        ignoreUnknownKeys = true
        isLenient = true
        encodeDefaults = false
    }

    private val jsonMediaType = "application/json; charset=utf-8".toMediaType()

    private fun authHeader(): String {
        val token = "$apiKey:".encodeToByteArray()
        val encoded = android.util.Base64.encodeToString(token, android.util.Base64.NO_WRAP)
        return "Basic $encoded"
    }

    private fun requestBuilder(path: String): Request.Builder {
        return Request.Builder()
            .url("$BASE_URL$path")
            .header("Authorization", authHeader())
            .header("Accept", "application/json")
    }

    suspend fun getMe(): MeResponse = withContext(Dispatchers.IO) {
        execute(requestBuilder("/v1/me").get().build()) { body ->
            json.decodeFromString<MeResponse>(body)
        }
    }

    suspend fun listAgents(limit: Int = 30): AgentListResponse = withContext(Dispatchers.IO) {
        execute(requestBuilder("/v1/agents?limit=$limit").get().build()) { body ->
            json.decodeFromString<AgentListResponse>(body)
        }
    }

    suspend fun getAgent(agentId: String): AgentDetail = withContext(Dispatchers.IO) {
        execute(requestBuilder("/v1/agents/$agentId").get().build()) { body ->
            json.decodeFromString<AgentDetail>(body)
        }
    }

    suspend fun listModels(): ModelListResponse = withContext(Dispatchers.IO) {
        execute(requestBuilder("/v1/models").get().build()) { body ->
            json.decodeFromString<ModelListResponse>(body)
        }
    }

    suspend fun listRepositories(): RepositoryListResponse = withContext(Dispatchers.IO) {
        execute(requestBuilder("/v1/repositories").get().build()) { body ->
            json.decodeFromString<RepositoryListResponse>(body)
        }
    }

    suspend fun createAgent(request: CreateAgentRequest): CreateAgentResponse =
        withContext(Dispatchers.IO) {
            val payload = json.encodeToString(request)
            val httpRequest = requestBuilder("/v1/agents")
                .post(payload.toRequestBody(jsonMediaType))
                .build()
            execute(httpRequest) { body ->
                json.decodeFromString<CreateAgentResponse>(body)
            }
        }

    suspend fun createRun(agentId: String, request: CreateRunRequest): CreateRunResponse =
        withContext(Dispatchers.IO) {
            val payload = json.encodeToString(request)
            val httpRequest = requestBuilder("/v1/agents/$agentId/runs")
                .post(payload.toRequestBody(jsonMediaType))
                .build()
            execute(httpRequest) { body ->
                json.decodeFromString<CreateRunResponse>(body)
            }
        }

    suspend fun getRun(agentId: String, runId: String): RunDetail = withContext(Dispatchers.IO) {
        execute(requestBuilder("/v1/agents/$agentId/runs/$runId").get().build()) { body ->
            json.decodeFromString<RunDetail>(body)
        }
    }

    suspend fun cancelRun(agentId: String, runId: String): Unit = withContext(Dispatchers.IO) {
        execute(
            requestBuilder("/v1/agents/$agentId/runs/$runId/cancel")
                .post("".toRequestBody(jsonMediaType))
                .build(),
        ) { }
    }

    fun streamRun(agentId: String, runId: String, lastEventId: String? = null): Flow<StreamEvent> =
        callbackFlow {
            val builder = requestBuilder("/v1/agents/$agentId/runs/$runId/stream")
                .header("Accept", "text/event-stream")
                .get()
            if (!lastEventId.isNullOrBlank()) {
                builder.header("Last-Event-ID", lastEventId)
            }

            val factory = EventSources.createFactory(httpClient)
            val listener = object : EventSourceListener() {
                override fun onEvent(
                    eventSource: EventSource,
                    id: String?,
                    type: String?,
                    data: String,
                ) {
                    val eventType = type ?: return
                    when (eventType) {
                        "status" -> trySend(StreamEvent.Status(json.decodeFromString(data)))
                        "assistant" -> {
                            val payload = json.decodeFromString<StreamTextEvent>(data)
                            trySend(StreamEvent.Assistant(payload.text))
                        }
                        "thinking" -> {
                            val payload = json.decodeFromString<StreamTextEvent>(data)
                            trySend(StreamEvent.Thinking(payload.text))
                        }
                        "tool_call" -> trySend(
                            StreamEvent.ToolCall(json.decodeFromString(data)),
                        )
                        "result" -> trySend(StreamEvent.Result(json.decodeFromString(data)))
                        "error" -> trySend(StreamEvent.Error(json.decodeFromString(data)))
                        "heartbeat" -> trySend(StreamEvent.Heartbeat)
                        "done" -> {
                            trySend(StreamEvent.Done)
                            close()
                        }
                    }
                }

                override fun onFailure(
                    eventSource: EventSource,
                    t: Throwable?,
                    response: Response?,
                ) {
                    if (response != null && !response.isSuccessful) {
                        val message = response.body?.string().orEmpty()
                        close(CursorApiException(response.code, message.ifBlank { "Stream failed" }))
                    } else {
                        close(t ?: CursorApiException(0, "Stream connection lost"))
                    }
                }

                override fun onClosed(eventSource: EventSource) {
                    close()
                }
            }

            val source = factory.newEventSource(builder.build(), listener)
            awaitClose { source.cancel() }
        }

    private inline fun <T> execute(request: Request, parse: (String) -> T): T {
        httpClient.newCall(request).execute().use { response ->
            val body = response.body?.string().orEmpty()
            if (!response.isSuccessful) {
                val errorMessage = runCatching {
                    json.decodeFromString<ApiErrorBody>(body).message
                }.getOrNull() ?: body.ifBlank { response.message }
                throw CursorApiException(response.code, errorMessage)
            }
            return if (body.isBlank()) {
                @Suppress("UNCHECKED_CAST")
                Unit as T
            } else {
                parse(body)
            }
        }
    }

    companion object {
        const val BASE_URL = "https://api.cursor.com"

        fun defaultClient(): OkHttpClient {
            return OkHttpClient.Builder()
                .connectTimeout(30, TimeUnit.SECONDS)
                .readTimeout(0, TimeUnit.SECONDS)
                .writeTimeout(30, TimeUnit.SECONDS)
                .callTimeout(0, TimeUnit.SECONDS)
                .build()
        }
    }
}
