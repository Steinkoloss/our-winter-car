package com.cursor.client

import android.app.Application
import com.cursor.client.data.SessionStore

class CursorClientApp : Application() {
    lateinit var sessionStore: SessionStore
        private set

    override fun onCreate() {
        super.onCreate()
        sessionStore = SessionStore(this)
    }
}
