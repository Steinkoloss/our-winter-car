# Cursor Android Client

Native Android client for [Cursor Cloud Agents](https://cursor.com/docs/cloud-agent/api/endpoints). Manage agents, start runs, and follow streaming responses from your phone.

## Features

- Sign in with your Cursor user API key (stored encrypted on-device)
- List and open existing Cloud Agents
- Create new agents with optional GitHub repo, branch, model, and auto-PR
- Real-time SSE streaming for assistant replies and tool activity
- Send follow-up prompts and cancel active runs

## Requirements

- Android 8.0+ (API 26)
- A Cursor account with a user API key from **Dashboard → Integrations → API Keys**
- For repo-backed agents: GitHub connected in your Cursor dashboard

## Build

```bash
export ANDROID_HOME="$HOME/android-sdk"   # or your SDK path
cd apps/cursor-client-android
./gradlew assembleRelease
```

Release APK:

`app/build/outputs/apk/release/app-release-unsigned.apk`

For a signed installable build, configure a keystore in `app/build.gradle.kts` or sign with `apksigner` after building.

Debug APK (signed, installable for testing):

```bash
./gradlew assembleDebug
```

`app/build/outputs/apk/debug/app-debug.apk`

Pre-built copies (after build) are also placed in `dist/`:

- `dist/CursorClient-1.0.0-debug.apk` — signed debug build, ready to sideload
- `dist/CursorClient-1.0.0-unsigned.apk` — release build (sign before distributing)

## API

This app talks directly to `https://api.cursor.com` using the official Cloud Agents API v1. Your API key never leaves your device except in HTTPS requests to Cursor.

## Disclaimer

Unofficial client — not affiliated with Cursor. Use at your own risk.
