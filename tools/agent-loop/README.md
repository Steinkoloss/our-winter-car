# agent-loop

Unattended improvement loop. Each iteration: one agent implements one slice →
`loop.sh` runs gates → a second agent reviews the diff → the script commits to the
current `wip/*` branch. Up to `FIX_ROUNDS` repair rounds per slice; a slice that still
fails is stashed. Never pushes, never touches main.

```bash
git switch -c wip/loop-2026-09-22        # loop refuses to run elsewhere
tools/agent-loop/loop.sh gates           # check the gates pass on the current tree
tools/agent-loop/loop.sh                 # run
tools/agent-loop/loop.sh status          # what happened / what's going on (read-only)
tools/agent-loop/menu.sh                 # terminal menu: status/start/halt/playtest notes/log (desktop shortcut)
```

One run per checkout (lock in `.git/agent-loop/lock`). Don't edit files or switch
branches in this checkout while it runs; its gates would count your edits as the agent's.

It stops by itself when:

- `PLAYTEST_EVERY` slices landed → writes `.agent-loop/PLAYTEST.md`. Play them, write
  findings to `.agent-loop/playtest-notes.md`, then `loop.sh playtested && loop.sh`.
  Notes steer the next slices until you replace them.
- the agent writes `.agent-loop/STOP` (decision needed, needs real play, nothing worth doing);
- `MAX_FAILS` slices in a row fail, `MAX_ITERS` or `MAX_AGENT_RUNS` is reached;
- an agent CLI exits nonzero (auth, timeout) or touches git refs. The tree is left as is.

Stop it yourself with `touch .agent-loop/HALT` (takes effect before the next slice) or Ctrl-C.

## Gates (script-enforced, the agent can't edit them)

HEAD and all refs untouched by agents (push and `gh` disabled via env for every CLI) · no edits to loop tooling, AGENTS.md, CI, gitignore,
build props · ≤ `MAX_ADDED` added lines · net doc growth ≤ `MAX_DOC_GROWTH`, PLAN ≤ 800,
BUILDING ≤ 300 · touched `.cs` ≤ 1000 lines · test count never drops · one protocol bump
per release, wire changes update `protocol/` · `build/` ≤ `MAX_BUILD_GB` · Net build
(warnings = errors), Net + Launcher tests, Core Release (warnings = errors) and probe
when the game is installed. Loop builds pass `-p:DeployToGame=false`, so candidates never
land in your real game's `BepInEx/plugins`.

## Knobs (env vars)

| Var | Default | |
|---|---|---|
| `AGENT` / `MODEL` | `claude` / CLI default | `claude`, `codex`, `hermes`, or `custom` + `AGENT_CMD` (prompt on stdin) |
| `REVIEW_AGENT` / `REVIEW_MODEL` | same as implementer | cross-model review, e.g. implement with codex, review with claude |
| `EFFORT` | `xhigh` | claude only |
| `PERM_MODE` | `auto` | claude permission mode; git writes and `gh` are always denied |
| `HERMES_FLAGS` | `--yolo` | |
| `MAX_ITERS` `PLAYTEST_EVERY` `FIX_ROUNDS` `MAX_FAILS` | 20 · 5 · 2 · 2 | |
| `ITER_TIMEOUT` `MAX_ADDED` `MAX_DOC_GROWTH` `MAX_BUILD_GB` | 3h · 1500 · 120 · 12 | |
| `MAX_AGENT_RUNS` | 40 | hard budget per run; every implement, fix and review call counts |

Cost: every iteration is 1 implementer run + 1 review run, plus up to 2× that per
fix round. A run stops at 5 committed slices (playtest) or 40 agent runs, whichever comes first.

## Comparing models

Same loop, same rules, separate branches from the same commit:

```bash
git switch -c wip/cmp-claude <base> && AGENT=claude MODEL=claude-opus-5-5 tools/agent-loop/loop.sh
git switch -c wip/cmp-codex  <base> && AGENT=codex  tools/agent-loop/loop.sh
```

Compare the branches' `git log`, what the reviewer rejected (`.agent-loop/iter-*/feedback-*`),
and how often each wrote a STOP instead of pushing on. State lives in `.agent-loop/`
(gitignored); move it aside between runs. Playtest counters are per branch.
