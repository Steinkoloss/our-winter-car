package com.cursor.client

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.viewmodel.compose.viewModel
import com.cursor.client.ui.screens.AgentsScreen
import com.cursor.client.ui.screens.ChatScreen
import com.cursor.client.ui.screens.CreateAgentScreen
import com.cursor.client.ui.screens.LoginScreen
import com.cursor.client.ui.screens.SettingsScreen
import com.cursor.client.ui.theme.CursorClientTheme
import com.cursor.client.viewmodel.MainViewModel

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            CursorClientTheme {
                val vm: MainViewModel = viewModel()
                val state by vm.uiState.collectAsState()
                val snackbarHostState = remember { SnackbarHostState() }
                var showCreate by rememberSaveable { mutableStateOf(false) }
                var showSettings by rememberSaveable { mutableStateOf(false) }

                LaunchedEffect(state.error) {
                    val message = state.error ?: return@LaunchedEffect
                    snackbarHostState.showSnackbar(message)
                    vm.clearError()
                }

                LaunchedEffect(state.selectedAgentId) {
                    if (state.selectedAgentId != null) {
                        showCreate = false
                        showSettings = false
                    }
                }

                Scaffold(
                    modifier = Modifier.fillMaxSize(),
                    snackbarHost = { SnackbarHost(snackbarHostState) },
                ) {
                    when {
                        !state.isAuthenticated -> {
                            LoginScreen(
                                isLoading = state.isLoading,
                                error = state.error,
                                onLogin = vm::login,
                            )
                        }
                        state.selectedAgentId != null -> {
                            ChatScreen(
                                agentName = state.selectedAgentName,
                                runStatus = state.runStatus,
                                toolActivity = state.toolActivity,
                                messages = state.messages,
                                isStreaming = state.isStreaming,
                                onBack = vm::closeAgent,
                                onSend = vm::sendFollowUp,
                                onCancel = vm::cancelActiveRun,
                            )
                        }
                        showCreate -> {
                            CreateAgentScreen(
                                models = state.models,
                                repositories = state.repositories,
                                isLoading = state.isLoading,
                                onLoadRepositories = vm::loadRepositories,
                                onBack = { showCreate = false },
                                onCreate = { prompt, repo, branch, model, autoPr ->
                                    showCreate = false
                                    vm.createAgent(prompt, repo, branch, model, autoPr)
                                },
                            )
                        }
                        showSettings -> {
                            SettingsScreen(
                                email = state.me?.userEmail,
                                apiKeyName = state.me?.apiKeyName,
                                onLogout = {
                                    showSettings = false
                                    vm.logout()
                                },
                                onBack = { showSettings = false },
                            )
                        }
                        else -> {
                            AgentsScreen(
                                me = state.me,
                                agents = state.agents,
                                isLoading = state.isLoading,
                                onRefresh = vm::refreshAll,
                                onOpenAgent = vm::openAgent,
                                onCreateAgent = { showCreate = true },
                                onOpenSettings = { showSettings = true },
                            )
                        }
                    }
                }
            }
        }
    }
}
