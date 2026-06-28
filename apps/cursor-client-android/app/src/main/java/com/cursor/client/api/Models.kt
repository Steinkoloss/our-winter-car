package com.cursor.client.api

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement

@Serializable
data class MeResponse(
    val apiKeyName: String,
    val createdAt: String,
    val userId: Int? = null,
    val userEmail: String? = null,
    val userFirstName: String? = null,
    val userLastName: String? = null,
    val teamId: Int? = null,
)

@Serializable
data class AgentSummary(
    val id: String,
    val name: String,
    val status: String,
    val url: String,
    val createdAt: String,
    val updatedAt: String,
    val latestRunId: String? = null,
)

@Serializable
data class AgentListResponse(
    val items: List<AgentSummary>,
    val nextCursor: String? = null,
)

@Serializable
data class RepoConfig(
    val url: String,
    val startingRef: String? = null,
    val prUrl: String? = null,
)

@Serializable
data class AgentDetail(
    val id: String,
    val name: String,
    val status: String,
    val url: String,
    val createdAt: String,
    val updatedAt: String,
    val latestRunId: String? = null,
    val repos: List<RepoConfig> = emptyList(),
    val workOnCurrentBranch: Boolean = false,
    val autoCreatePR: Boolean = false,
)

@Serializable
data class RunSummary(
    val id: String,
    val agentId: String,
    val status: String,
    val createdAt: String,
    val updatedAt: String,
)

@Serializable
data class RunListResponse(
    val items: List<RunSummary>,
    val nextCursor: String? = null,
)

@Serializable
data class GitBranch(
    val repoUrl: String,
    val branch: String? = null,
    val prUrl: String? = null,
)

@Serializable
data class GitInfo(
    val branches: List<GitBranch> = emptyList(),
)

@Serializable
data class RunDetail(
    val id: String,
    val agentId: String,
    val status: String,
    val createdAt: String,
    val updatedAt: String,
    val durationMs: Long? = null,
    val result: String? = null,
    val git: GitInfo? = null,
)

@Serializable
data class PromptBody(
    val text: String,
)

@Serializable
data class ModelSelection(
    val id: String,
)

@Serializable
data class CreateAgentRequest(
    val prompt: PromptBody,
    val model: ModelSelection? = null,
    val repos: List<RepoConfig>? = null,
    val autoCreatePR: Boolean? = null,
    val mode: String? = null,
)

@Serializable
data class CreateRunRequest(
    val prompt: PromptBody,
    val mode: String? = null,
)

@Serializable
data class CreateAgentResponse(
    val agent: AgentDetail,
    val run: RunSummary,
)

@Serializable
data class CreateRunResponse(
    val run: RunSummary,
)

@Serializable
data class ModelItem(
    val id: String,
    val displayName: String,
    val aliases: List<String> = emptyList(),
)

@Serializable
data class ModelListResponse(
    val items: List<ModelItem>,
)

@Serializable
data class RepositoryItem(
    val url: String,
)

@Serializable
data class RepositoryListResponse(
    val items: List<RepositoryItem>,
)

@Serializable
data class ApiErrorBody(
    val error: String? = null,
    val message: String? = null,
)

@Serializable
data class StreamStatusEvent(
    val runId: String,
    val status: String,
)

@Serializable
data class StreamTextEvent(
    val text: String,
)

@Serializable
data class StreamToolCallEvent(
    val callId: String,
    val name: String,
    val status: String,
    val args: JsonElement? = null,
    val result: JsonElement? = null,
)

@Serializable
data class StreamResultEvent(
    val runId: String,
    val status: String,
    val text: String? = null,
    val durationMs: Long? = null,
    val git: GitInfo? = null,
)

@Serializable
data class StreamErrorEvent(
    val code: String? = null,
    val message: String? = null,
)

sealed class StreamEvent {
    data class Status(val data: StreamStatusEvent) : StreamEvent()
    data class Assistant(val text: String) : StreamEvent()
    data class Thinking(val text: String) : StreamEvent()
    data class ToolCall(val data: StreamToolCallEvent) : StreamEvent()
    data class Result(val data: StreamResultEvent) : StreamEvent()
    data class Error(val data: StreamErrorEvent) : StreamEvent()
    data object Done : StreamEvent()
    data object Heartbeat : StreamEvent()
}

class CursorApiException(
    val statusCode: Int,
    message: String,
) : Exception(message)
