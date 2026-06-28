package com.cursor.client.viewmodel

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.cursor.client.CursorClientApp
import com.cursor.client.api.AgentSummary
import com.cursor.client.api.CreateAgentRequest
import com.cursor.client.api.CreateRunRequest
import com.cursor.client.api.CursorApiException
import com.cursor.client.api.MeResponse
import com.cursor.client.api.ModelItem
import com.cursor.client.api.PromptBody
import com.cursor.client.api.RepoConfig
import com.cursor.client.api.RunDetail
import com.cursor.client.api.StreamEvent
import com.cursor.client.data.SessionStore
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class ChatMessage(
    val id: String,
    val role: String,
    val text: String,
    val isStreaming: Boolean = false,
)

data class AppUiState(
    val isAuthenticated: Boolean = false,
    val isLoading: Boolean = false,
    val error: String? = null,
    val me: MeResponse? = null,
    val agents: List<AgentSummary> = emptyList(),
    val models: List<ModelItem> = emptyList(),
    val repositories: List<String> = emptyList(),
    val selectedAgentId: String? = null,
    val selectedAgentName: String? = null,
    val activeRunId: String? = null,
    val runStatus: String? = null,
    val messages: List<ChatMessage> = emptyList(),
    val isStreaming: Boolean = false,
    val toolActivity: String? = null,
)

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val sessionStore: SessionStore =
        (application as CursorClientApp).sessionStore

    private val _uiState = MutableStateFlow(AppUiState())
    val uiState: StateFlow<AppUiState> = _uiState.asStateFlow()

    private var streamJob: Job? = null

    init {
        val hasKey = sessionStore.getApiKey() != null
        _uiState.update { it.copy(isAuthenticated = hasKey) }
        if (hasKey) {
            refreshAll()
        }
    }

    fun login(apiKey: String) {
        viewModelScope.launch {
            _uiState.update { it.copy(isLoading = true, error = null) }
            runCatching {
                sessionStore.saveApiKey(apiKey)
                val client = sessionStore.createClient() ?: error("Missing API key")
                val me = client.getMe()
                _uiState.update {
                    it.copy(
                        isAuthenticated = true,
                        isLoading = false,
                        me = me,
                    )
                }
                refreshAll()
            }.onFailure { error ->
                sessionStore.clear()
                _uiState.update {
                    it.copy(
                        isAuthenticated = false,
                        isLoading = false,
                        error = error.message ?: "Login failed",
                    )
                }
            }
        }
    }

    fun logout() {
        streamJob?.cancel()
        sessionStore.clear()
        _uiState.value = AppUiState()
    }

    fun clearError() {
        _uiState.update { it.copy(error = null) }
    }

    fun refreshAll() {
        viewModelScope.launch {
            val client = sessionStore.createClient() ?: return@launch
            _uiState.update { it.copy(isLoading = true, error = null) }
            runCatching {
                val me = client.getMe()
                val agents = client.listAgents().items
                val models = client.listModels().items
                _uiState.update {
                    it.copy(
                        isLoading = false,
                        me = me,
                        agents = agents,
                        models = models,
                    )
                }
            }.onFailure { error ->
                _uiState.update {
                    it.copy(
                        isLoading = false,
                        error = error.message ?: "Failed to refresh",
                    )
                }
            }
        }
    }

    fun loadRepositories() {
        viewModelScope.launch {
            val client = sessionStore.createClient() ?: return@launch
            runCatching {
                val repos = client.listRepositories().items.map { it.url }
                _uiState.update { it.copy(repositories = repos) }
            }.onFailure { error ->
                _uiState.update {
                    it.copy(error = error.message ?: "Failed to load repositories")
                }
            }
        }
    }

    fun openAgent(agent: AgentSummary) {
        streamJob?.cancel()
        _uiState.update {
            it.copy(
                selectedAgentId = agent.id,
                selectedAgentName = agent.name,
                activeRunId = agent.latestRunId,
                runStatus = null,
                messages = emptyList(),
                isStreaming = false,
                toolActivity = null,
                error = null,
            )
        }
        viewModelScope.launch {
            val client = sessionStore.createClient() ?: return@launch
            val runId = agent.latestRunId ?: return@launch
            runCatching {
                val run = client.getRun(agent.id, runId)
                if (!run.result.isNullOrBlank()) {
                    appendAssistantMessage(run.result, streaming = false)
                }
                _uiState.update { it.copy(runStatus = run.status) }
                if (run.status == "RUNNING" || run.status == "CREATING") {
                    startStreaming(agent.id, runId)
                }
            }.onFailure { error ->
                _uiState.update { it.copy(error = error.message) }
            }
        }
    }

    fun closeAgent() {
        streamJob?.cancel()
        _uiState.update {
            it.copy(
                selectedAgentId = null,
                selectedAgentName = null,
                activeRunId = null,
                runStatus = null,
                messages = emptyList(),
                isStreaming = false,
                toolActivity = null,
            )
        }
    }

    fun createAgent(
        prompt: String,
        repoUrl: String?,
        branch: String?,
        modelId: String?,
        autoCreatePr: Boolean,
    ) {
        viewModelScope.launch {
            val client = sessionStore.createClient() ?: return@launch
            _uiState.update { it.copy(isLoading = true, error = null) }
            runCatching {
                val repos = repoUrl?.takeIf { it.isNotBlank() }?.let { url ->
                    listOf(
                        RepoConfig(
                            url = url,
                            startingRef = branch?.takeIf { it.isNotBlank() } ?: "main",
                        ),
                    )
                }
                val response = client.createAgent(
                    CreateAgentRequest(
                        prompt = PromptBody(prompt),
                        model = modelId?.takeIf { it.isNotBlank() }?.let {
                            com.cursor.client.api.ModelSelection(it)
                        },
                        repos = repos,
                        autoCreatePR = if (repos != null) autoCreatePr else null,
                        mode = "agent",
                    ),
                )
                val agents = client.listAgents().items
                _uiState.update {
                    it.copy(
                        isLoading = false,
                        agents = agents,
                        selectedAgentId = response.agent.id,
                        selectedAgentName = response.agent.name,
                        activeRunId = response.run.id,
                        runStatus = response.run.status,
                        messages = listOf(
                            ChatMessage(
                                id = "user-${response.run.id}",
                                role = "user",
                                text = prompt,
                            ),
                        ),
                    )
                }
                startStreaming(response.agent.id, response.run.id)
            }.onFailure { error ->
                _uiState.update {
                    it.copy(
                        isLoading = false,
                        error = error.message ?: "Failed to create agent",
                    )
                }
            }
        }
    }

    fun sendFollowUp(prompt: String) {
        val agentId = _uiState.value.selectedAgentId ?: return
        viewModelScope.launch {
            val client = sessionStore.createClient() ?: return@launch
            _uiState.update {
                it.copy(
                    error = null,
                    messages = it.messages + ChatMessage(
                        id = "user-${System.currentTimeMillis()}",
                        role = "user",
                        text = prompt,
                    ),
                )
            }
            runCatching {
                val response = client.createRun(
                    agentId,
                    CreateRunRequest(prompt = PromptBody(prompt)),
                )
                _uiState.update {
                    it.copy(
                        activeRunId = response.run.id,
                        runStatus = response.run.status,
                    )
                }
                startStreaming(agentId, response.run.id)
            }.onFailure { error ->
                if (error is CursorApiException && error.statusCode == 409) {
                    _uiState.update { it.copy(error = "Agent is busy. Wait for the current run to finish.") }
                } else {
                    _uiState.update { it.copy(error = error.message ?: "Failed to send message") }
                }
            }
        }
    }

    fun cancelActiveRun() {
        val agentId = _uiState.value.selectedAgentId ?: return
        val runId = _uiState.value.activeRunId ?: return
        viewModelScope.launch {
            val client = sessionStore.createClient() ?: return@launch
            runCatching {
                client.cancelRun(agentId, runId)
                streamJob?.cancel()
                _uiState.update { it.copy(isStreaming = false, runStatus = "CANCELLED") }
            }.onFailure { error ->
                _uiState.update { it.copy(error = error.message) }
            }
        }
    }

    private fun startStreaming(agentId: String, runId: String) {
        streamJob?.cancel()
        val client = sessionStore.createClient() ?: return
        streamJob = viewModelScope.launch {
            _uiState.update {
                it.copy(
                    isStreaming = true,
                    toolActivity = null,
                    messages = it.messages + ChatMessage(
                        id = "assistant-$runId",
                        role = "assistant",
                        text = "",
                        isStreaming = true,
                    ),
                )
            }
            runCatching {
                client.streamRun(agentId, runId).collect { event ->
                    when (event) {
                        is StreamEvent.Status -> {
                            _uiState.update { it.copy(runStatus = event.data.status) }
                        }
                        is StreamEvent.Assistant -> appendAssistantDelta(event.text)
                        is StreamEvent.Thinking -> {
                            _uiState.update { it.copy(toolActivity = "Thinking…") }
                        }
                        is StreamEvent.ToolCall -> {
                            val label = when (event.data.status) {
                                "running" -> "Running ${event.data.name}…"
                                else -> "Completed ${event.data.name}"
                            }
                            _uiState.update { it.copy(toolActivity = label) }
                        }
                        is StreamEvent.Result -> {
                            if (!event.data.text.isNullOrBlank()) {
                                setAssistantText(event.data.text)
                            }
                            _uiState.update {
                                it.copy(
                                    runStatus = event.data.status,
                                    isStreaming = false,
                                    toolActivity = null,
                                )
                            }
                        }
                        is StreamEvent.Error -> {
                            _uiState.update {
                                it.copy(
                                    error = event.data.message ?: "Stream error",
                                    isStreaming = false,
                                )
                            }
                        }
                        StreamEvent.Done -> {
                            _uiState.update { it.copy(isStreaming = false, toolActivity = null) }
                        }
                        StreamEvent.Heartbeat -> Unit
                    }
                }
            }.onFailure { error ->
                if (_uiState.value.isStreaming) {
                    runCatching {
                        val run: RunDetail = client.getRun(agentId, runId)
                        if (!run.result.isNullOrBlank()) {
                            appendAssistantMessage(run.result, streaming = false)
                        }
                        _uiState.update {
                            it.copy(
                                runStatus = run.status,
                                isStreaming = false,
                                error = if (run.result.isNullOrBlank()) error.message else null,
                            )
                        }
                    }.onFailure {
                        _uiState.update {
                            it.copy(
                                isStreaming = false,
                                error = error.message ?: "Stream failed",
                            )
                        }
                    }
                }
            }
        }
    }

    private fun appendAssistantDelta(delta: String) {
        _uiState.update { state ->
            val messages = state.messages.toMutableList()
            val index = messages.indexOfLast { it.role == "assistant" && it.isStreaming }
            if (index >= 0) {
                val current = messages[index]
                messages[index] = current.copy(text = current.text + delta)
            } else {
                messages.add(
                    ChatMessage(
                        id = "assistant-${System.currentTimeMillis()}",
                        role = "assistant",
                        text = delta,
                        isStreaming = true,
                    ),
                )
            }
            state.copy(messages = messages, toolActivity = null)
        }
    }

    private fun setAssistantText(text: String) {
        _uiState.update { state ->
            val messages = state.messages.toMutableList()
            val index = messages.indexOfLast { it.role == "assistant" }
            if (index >= 0) {
                messages[index] = messages[index].copy(text = text, isStreaming = false)
            } else {
                messages.add(
                    ChatMessage(
                        id = "assistant-${System.currentTimeMillis()}",
                        role = "assistant",
                        text = text,
                        isStreaming = false,
                    ),
                )
            }
            state.copy(messages = messages)
        }
    }

    private fun appendAssistantMessage(text: String, streaming: Boolean) {
        _uiState.update { state ->
            state.copy(
                messages = state.messages + ChatMessage(
                    id = "assistant-${System.currentTimeMillis()}",
                    role = "assistant",
                    text = text,
                    isStreaming = streaming,
                ),
            )
        }
    }
}
