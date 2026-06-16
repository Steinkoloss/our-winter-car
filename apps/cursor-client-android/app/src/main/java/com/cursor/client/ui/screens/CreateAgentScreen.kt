package com.cursor.client.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.cursor.client.api.ModelItem

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CreateAgentScreen(
    models: List<ModelItem>,
    repositories: List<String>,
    isLoading: Boolean,
    onLoadRepositories: () -> Unit,
    onBack: () -> Unit,
    onCreate: (prompt: String, repoUrl: String?, branch: String?, modelId: String?, autoCreatePr: Boolean) -> Unit,
) {
    var prompt by rememberSaveable { mutableStateOf("") }
    var repoUrl by rememberSaveable { mutableStateOf("") }
    var branch by rememberSaveable { mutableStateOf("main") }
    var autoCreatePr by rememberSaveable { mutableStateOf(true) }
    var modelExpanded by rememberSaveable { mutableStateOf(false) }
    var selectedModel by rememberSaveable { mutableStateOf<String?>(null) }
    var repoExpanded by rememberSaveable { mutableStateOf(false) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("New agent") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .padding(16.dp)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            OutlinedTextField(
                modifier = Modifier.fillMaxWidth(),
                value = prompt,
                onValueChange = { prompt = it },
                label = { Text("Prompt") },
                placeholder = { Text("What should the agent do?") },
                minLines = 4,
            )

            ExposedDropdownMenuBox(
                expanded = modelExpanded,
                onExpandedChange = { modelExpanded = !modelExpanded },
            ) {
                OutlinedTextField(
                    modifier = Modifier
                        .menuAnchor()
                        .fillMaxWidth(),
                    value = selectedModel?.let { id ->
                        models.find { it.id == id }?.displayName ?: id
                    } ?: "Default model",
                    onValueChange = {},
                    readOnly = true,
                    label = { Text("Model") },
                    trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = modelExpanded) },
                )
                ExposedDropdownMenu(
                    expanded = modelExpanded,
                    onDismissRequest = { modelExpanded = false },
                ) {
                    DropdownMenuItem(
                        text = { Text("Default model") },
                        onClick = {
                            selectedModel = null
                            modelExpanded = false
                        },
                    )
                    models.forEach { model ->
                        DropdownMenuItem(
                            text = { Text(model.displayName) },
                            onClick = {
                                selectedModel = model.id
                                modelExpanded = false
                            },
                        )
                    }
                }
            }

            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                ExposedDropdownMenuBox(
                    expanded = repoExpanded,
                    onExpandedChange = {
                        if (!repoExpanded && repositories.isEmpty()) {
                            onLoadRepositories()
                        }
                        repoExpanded = !repoExpanded
                    },
                    modifier = Modifier.weight(1f),
                ) {
                    OutlinedTextField(
                        modifier = Modifier
                            .menuAnchor()
                            .fillMaxWidth(),
                        value = repoUrl,
                        onValueChange = { repoUrl = it },
                        label = { Text("Repository (optional)") },
                        placeholder = { Text("https://github.com/org/repo") },
                        trailingIcon = {
                            if (repositories.isNotEmpty()) {
                                ExposedDropdownMenuDefaults.TrailingIcon(expanded = repoExpanded)
                            }
                        },
                    )
                    if (repositories.isNotEmpty()) {
                        ExposedDropdownMenu(
                            expanded = repoExpanded,
                            onDismissRequest = { repoExpanded = false },
                        ) {
                            repositories.forEach { repo ->
                                DropdownMenuItem(
                                    text = { Text(repo, maxLines = 1) },
                                    onClick = {
                                        repoUrl = repo
                                        repoExpanded = false
                                    },
                                )
                            }
                        }
                    }
                }
            }

            OutlinedTextField(
                modifier = Modifier.fillMaxWidth(),
                value = branch,
                onValueChange = { branch = it },
                label = { Text("Branch") },
                enabled = repoUrl.isNotBlank(),
            )

            Row(verticalAlignment = Alignment.CenterVertically) {
                Checkbox(
                    checked = autoCreatePr,
                    onCheckedChange = { autoCreatePr = it },
                    enabled = repoUrl.isNotBlank(),
                )
                Text("Auto-create pull request")
            }

            Spacer(Modifier.height(8.dp))

            Button(
                modifier = Modifier.fillMaxWidth(),
                enabled = prompt.isNotBlank() && !isLoading,
                onClick = {
                    onCreate(
                        prompt.trim(),
                        repoUrl.takeIf { it.isNotBlank() },
                        branch.takeIf { repoUrl.isNotBlank() },
                        selectedModel,
                        autoCreatePr,
                    )
                },
            ) {
                if (isLoading) {
                    CircularProgressIndicator()
                } else {
                    Text("Start agent")
                }
            }

            Text(
                "Agents run in Cursor's cloud. Repository access requires GitHub integration in your Cursor dashboard.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.secondary,
            )
        }
    }
}
