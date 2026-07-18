package com.cursor.client.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.cursor.client.api.AgentSummary
import com.cursor.client.api.MeResponse

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AgentsScreen(
    me: MeResponse?,
    agents: List<AgentSummary>,
    isLoading: Boolean,
    onRefresh: () -> Unit,
    onOpenAgent: (AgentSummary) -> Unit,
    onCreateAgent: () -> Unit,
    onOpenSettings: () -> Unit,
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text("Cursor Agents")
                        me?.userEmail?.let { email ->
                            Text(
                                text = email,
                                style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.secondary,
                            )
                        }
                    }
                },
                actions = {
                    IconButton(onClick = onRefresh) {
                        Icon(Icons.Default.Refresh, contentDescription = "Refresh")
                    }
                    IconButton(onClick = onOpenSettings) {
                        Icon(Icons.Default.Settings, contentDescription = "Settings")
                    }
                },
            )
        },
        floatingActionButton = {
            FloatingActionButton(onClick = onCreateAgent) {
                Icon(Icons.Default.Add, contentDescription = "New agent")
            }
        },
    ) { padding ->
        if (isLoading && agents.isEmpty()) {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                verticalArrangement = Arrangement.Center,
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                CircularProgressIndicator()
            }
        } else if (agents.isEmpty()) {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding)
                    .padding(24.dp),
                verticalArrangement = Arrangement.Center,
            ) {
                Text("No agents yet", style = MaterialTheme.typography.headlineSmall)
                Spacer(Modifier.height(8.dp))
                Text(
                    "Start a Cloud Agent from your phone. Tap + to create one.",
                    color = MaterialTheme.colorScheme.secondary,
                )
            }
        } else {
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentPadding = PaddingValues(vertical = 8.dp),
            ) {
                items(agents, key = { it.id }) { agent ->
                    ListItem(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { onOpenAgent(agent) },
                        headlineContent = {
                            Text(
                                text = agent.name,
                                maxLines = 2,
                                overflow = TextOverflow.Ellipsis,
                            )
                        },
                        supportingContent = {
                            Text("${agent.status} · ${formatRelative(agent.updatedAt)}")
                        },
                    )
                }
            }
        }
    }
}

private fun formatRelative(iso: String): String {
    return iso.replace('T', ' ').take(16)
}
