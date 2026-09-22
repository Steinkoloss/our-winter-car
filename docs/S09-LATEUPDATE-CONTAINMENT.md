# S09: bounded LateUpdate failure containment

This slice isolates the three existing WorldSyncManager LateUpdate calls. It is
not a claim that every Update, FixedUpdate, message handler or native subsystem
is contained, nor that S09 or multiplayer acceptance is complete.

## Runtime behavior

`src/WinterMP.Core/Sync/WorldSyncManager.Callbacks.cs` contains the production
Update/LateUpdate entry points and their shared error handling. The subsequent
[vehicle Update slice](S09-UPDATE-CONTAINMENT.md) extracts UpdateWorldSync into
WorldSyncManager.Update.cs and isolates its ten vehicle callbacks. Discovery
and native cleanup still live in WorldSyncManager.cs.

- Admission is unchanged: sync must be initialized, the session previously
  active, the scene GAME, and the global fallback neither disabled nor cooling
  down. Vehicle presentation runs before trailer presentation; Ventti still
  obtains the current SessionManager after the trailer call and skips a null
  session. The same path applies to host and guest; no authority check, intent,
  result, ownership, wire format, persistence or guest suppression rule changes.
- Each escaping exception is attributed to `LateUpdate.VehicleWorldSync`,
  `LateUpdate.TractorTrailerSync` or `LateUpdate.VenttiSync`. The original
  `SyncEventLog.Record("error", ...)` category, aggregate error ordinal and full
  exception (including stack/inner cause) remain. Warnings explain local retry
  or quarantine, rather than claiming that the whole world is backing off.
- The first failure delays only that callback for 1 unscaled second; a second
  failure delays it for 2 seconds; a third quarantines that callback until world
  or session cleanup. These are cumulative failures for the current world,
  not consecutive failures: occasional successes cannot give a flapping
  callback unlimited retries/log spam. A successful admitted retry resumes
  normal per-frame execution, without an additional cooldown.
- Healthy callbacks and ordinary Update continue. Already-admitted independent
  LateUpdate calls finish in order even if a sibling crosses the global error
  threshold during that frame. There is no rollback of a partially applied
  presentation update and no replay of captured intents/events.
- Trailer/Ventti's own internal catch/disable/restore behavior is unchanged;
  this coordinator boundary handles only exceptions that escape those methods.

## Global fallback and cleanup

Every actually caught error still contributes to the original session-wide
8-error budget. A single repeatedly failing LateUpdate callback is capped at
3 errors and cannot exhaust a fresh budget by itself. Multiple failing callbacks,
other Update failures, or failures across world changes can still exhaust it.
At the threshold, the original error log and sync-log dump disable subsequent
world callbacks. If all three late callbacks fail on their third admitted frame,
errors 8 and 9 are both recorded/dumped rather than hiding the final exception.
Ordinary unscoped Update errors retain the original 1-second global backoff.

Global disable no longer prevents session restoration: after the session becomes
null/Idle/Connecting/Failed, Update can reach the existing ReleaseEverything path
without resuming gameplay. Cleanup failure remains logged and retries at most
once per second. The existing global cooldown may delay teardown for up to its
remaining second; local LateUpdate cooldowns do not delay teardown.

Session release resets all local counts/deadlines/quarantine, the global budget,
disable/backoff and the ring log (as before); the not-yet-initialized path also
resets error state. Scene change and OnDestroy reset local LateUpdate gates.
Scene change does not refund the session-global budget. OnDestroy does not erase
the diagnostic ring: its existing destruction/diagnostic behavior is preserved.
All original subsystem release/Clear calls are retained. The new gates contain
only counts and float deadlines: no delegates, subscriptions, hooks, leases,
coroutines or timer resources are allocated by them.

## Verification and limits

Portable tests link the actual production callback source, replacing only Unity,
SessionManager, subsystem implementations and diagnostic sinks with doubles:

    dotnet test tools/WorldSyncCallbacks.Tests/WorldSyncCallbacks.Tests.csproj -c Release -p:DeployToGame=false --logger 'console;verbosity=normal'
    python3 -B -m unittest discover -s tools/tests -v
    dotnet test src/WinterMP.Net.Tests -c Release -p:DeployToGame=false
    dotnet test src/WinterMP.Launcher.Tests -c Release -p:DeployToGame=false
    dotnet build src/WinterMP.Core/WinterMP.Core.csproj -c Release -p:DeployToGame=false '-p:MwcGamePath=/home/jaimep/.steam/root/steamapps/common/My Winter Car'

RUN receipts: `/home/jaimep/our-winter-car-agent-env/autonomous/rounds/000051-work/`.
`callbacks-red` records behavioral failures against the unchanged extracted
callback implementation; `callbacks-green` uses identical regression tests.
Expanded tests cover exact retry deadlines, quarantine, intermittent failures,
independent clocks, global fallback, original errors, admission/order, session
reset and bounded failed-cleanup retries. Python checks are static lifecycle
wiring assertions, not evidence of native hook/physics/save restoration.

Core builds target net35 using installed DLLs only as read-only compiler
references, never as deployment targets. No native launch is authorized by this
slice: protected-input provenance remains BLOCKED. Native discovery, injected
state, ordinary input, host/guest native convergence, Steam/two-PC, different
saves, native late-join/rejoin/save-reload and four-player soak are NOT_TESTED.
