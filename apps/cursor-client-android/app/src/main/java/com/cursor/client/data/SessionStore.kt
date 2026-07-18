package com.cursor.client.data

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import com.cursor.client.api.CursorApiClient

class SessionStore(context: Context) {
    private val prefs = EncryptedSharedPreferences.create(
        context,
        PREFS_NAME,
        MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build(),
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
    )

    fun getApiKey(): String? = prefs.getString(KEY_API_KEY, null)?.takeIf { it.isNotBlank() }

    fun saveApiKey(apiKey: String) {
        prefs.edit().putString(KEY_API_KEY, apiKey.trim()).apply()
    }

    fun clear() {
        prefs.edit().clear().apply()
    }

    fun createClient(): CursorApiClient? {
        val key = getApiKey() ?: return null
        return CursorApiClient(key)
    }

    companion object {
        private const val PREFS_NAME = "cursor_client_session"
        private const val KEY_API_KEY = "api_key"
    }
}
