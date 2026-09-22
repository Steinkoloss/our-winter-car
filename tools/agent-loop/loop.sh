#!/usr/bin/env bash
# Unattended improvement loop: one agent run = one slice. The script, not the agent,
# enforces the rules: gates, independent review, commit on a wip/ branch, stop points.
# Usage and knobs: tools/agent-loop/README.md
set -uo pipefail

ROOT="$(git rev-parse --show-toplevel)"; cd "$ROOT" || exit 1
HERE="tools/agent-loop"
STATE=".agent-loop"                                  # agent-visible: STOP, summary, log, prompts
PRIVATE="$(git rev-parse --absolute-git-dir)/agent-loop" # script-only counters
mkdir -p "$STATE" "$PRIVATE"

AGENT="${AGENT:-claude}"                 # claude | codex | hermes | custom (uses AGENT_CMD)
MODEL="${MODEL:-}"
REVIEW_AGENT="${REVIEW_AGENT:-$AGENT}"
REVIEW_MODEL="${REVIEW_MODEL:-$MODEL}"
EFFORT="${EFFORT:-xhigh}"                # claude only
MAX_ITERS="${MAX_ITERS:-20}"             # hard stop per run
PLAYTEST_EVERY="${PLAYTEST_EVERY:-5}"    # commits before the loop halts for a human playtest
FIX_ROUNDS="${FIX_ROUNDS:-2}"            # repair attempts after a failed gate/review
MAX_FAILS="${MAX_FAILS:-2}"              # consecutive failed slices before giving up
ITER_TIMEOUT="${ITER_TIMEOUT:-3h}"
MAX_ADDED="${MAX_ADDED:-1500}"           # added lines per slice
MAX_DOC_GROWTH="${MAX_DOC_GROWTH:-120}"  # net *.md lines per slice
MAX_BUILD_GB="${MAX_BUILD_GB:-12}"
MAX_AGENT_RUNS="${MAX_AGENT_RUNS:-40}"  # every implementer/fixer/reviewer run counts (owner's hard cap)
AGENT_RUNS=0

export MSBuildEnableWorkloadResolver=false DOTNET_CLI_UI_LANGUAGE=en DOTNET_NOLOGO=1

exec 3>&1  # the terminal, even while an agent's output is redirected to a file
log()  { printf '%s %s\n' "$(date '+%F %T')" "$*" | tee -a "$STATE/log.md" >&3; }
die()  { log "STOP: $1"; exit "${2:-1}"; }

# --- agents -------------------------------------------------------------------------
# $1 agent  $2 model  $3 prompt file  $4 role (impl|review). Output goes to stdout.
run_agent() {
  local agent="$1" model="$2" prompt="$3" role="$4"
  AGENT_RUNS=$((AGENT_RUNS + 1))
  [ "$AGENT_RUNS" -le "$MAX_AGENT_RUNS" ] || die "agent-run budget $MAX_AGENT_RUNS used up (uncommitted work, if any, is left in the tree)" 0
  local deny=("Bash(git commit:*)" "Bash(git push:*)" "Bash(git reset:*)" "Bash(git checkout:*)"
              "Bash(git switch:*)" "Bash(git stash:*)" "Bash(git tag:*)" "Bash(git rebase:*)"
              "Bash(git clean:*)" "Bash(gh:*)")
  [ "$role" = review ] && deny+=("Edit" "Write" "NotebookEdit")
  # Push/gh block for every CLI (codex/hermes have no deny list): bogus pushurl, bogus token.
  # pushInsteadOf rewrites every push URL (any remote, explicit URLs) to an unusable scheme.
  export GIT_CONFIG_COUNT=3 \
         GIT_CONFIG_KEY_0=url.agent-loop-no-push://.pushInsteadOf GIT_CONFIG_VALUE_0=https:// \
         GIT_CONFIG_KEY_1=url.agent-loop-no-push://.pushInsteadOf GIT_CONFIG_VALUE_1=git@ \
         GIT_CONFIG_KEY_2=url.agent-loop-no-push://.pushInsteadOf GIT_CONFIG_VALUE_2=ssh:// \
         GH_TOKEN=agent-loop-disabled GITHUB_TOKEN=agent-loop-disabled
  case "$agent" in
    claude) timeout "$ITER_TIMEOUT" claude -p ${model:+--model "$model"} --effort "$EFFORT" \
              --permission-mode "${PERM_MODE:-auto}" --disallowedTools "${deny[@]}" < "$prompt" ;;
    codex)  timeout "$ITER_TIMEOUT" codex exec ${model:+-m "$model"} --sandbox workspace-write - < "$prompt" ;;
    hermes) timeout "$ITER_TIMEOUT" hermes ${model:+-m "$model"} ${HERMES_FLAGS:---yolo} -z "$(cat "$prompt")" ;;
    custom) timeout "$ITER_TIMEOUT" bash -c "$AGENT_CMD" < "$prompt" ;;
    *) die "unknown agent '$agent'" ;;
  esac
  local rc=$?
  unset GIT_CONFIG_COUNT GIT_CONFIG_KEY_{0,1,2} GIT_CONFIG_VALUE_{0,1,2} GH_TOKEN GITHUB_TOKEN
  return $rc
}

# All refs (branches, tags, stash, remotes) and HEAD: agents may only change the working tree.
refs_hash() { { git rev-parse HEAD; git for-each-ref --format='%(refname) %(objectname)'; } | sha1sum; }
# Working tree incl. untracked files, relative to $1.
tree_hash() { git add -N . 2>/dev/null; { git diff "$1"; git status --porcelain; } | sha1sum; }

# --- gates --------------------------------------------------------------------------
proto_at() {  # protocol version at a ref, or the working tree when $1 is empty
  local src
  if [ -n "$1" ]; then src="$(git show "$1:src/WinterMP.Net/Protocol.cs" 2>/dev/null)"; else src="$(cat src/WinterMP.Net/Protocol.cs)"; fi
  grep -oP 'const ushort Version = \K[0-9]+' <<<"$src"
}
test_count() {  # [Fact]/[Theory] count at a ref, or the working tree
  if [ -n "$1" ]; then git grep -hE '^\s*\[(Fact|Theory)' "$1" -- 'src/*Tests/*.cs' | wc -l
  else grep -rhE '^\s*\[(Fact|Theory)' --include='*.cs' src/*Tests | wc -l; fi
}

# $1 start commit  $2 output file. Returns nonzero with reasons in $2.
gates() {
  local start="$1" out="$2" rc=0
  fail() { echo "GATE FAIL: $*" >> "$out"; rc=1; }
  : > "$out"

  [ "$(git rev-parse HEAD)" = "$start" ] || fail "HEAD moved; agents must not commit/reset (script commits)"
  [ "$(git branch --show-current)" = "$BRANCH" ] || fail "branch changed"
  git add -N . 2>/dev/null
  local changed; changed="$(git diff --name-only "$start")"
  [ -n "$changed" ] || [ -n "${DRY:-}" ] || fail "no changes (write $STATE/STOP with a reason if there is nothing worth doing)"
  grep -qE "^($HERE/|AGENTS\.md|CLAUDE\.md|\.github/|\.gitignore|Directory\.Build\.props)" <<<"$changed" \
    && fail "touched protected files (loop tooling, AGENTS.md, CI, gitignore, build props): $(grep -E "^($HERE/|AGENTS|CLAUDE|\.github|\.gitignore|Directory)" <<<"$changed" | tr '\n' ' ')"
  [ -s "$STATE/summary.md" ] || [ -n "${DRY:-}" ] || fail "missing $STATE/summary.md (commit message)"

  local added; added="$(git diff --numstat "$start" -- . ':!catalog/dump-*' | awk '$1!="-"{s+=$1} END{print s+0}')"
  [ "$added" -le "$MAX_ADDED" ] || fail "slice too big: $added added lines > $MAX_ADDED; split it"
  local docnet; docnet="$(git diff --numstat "$start" -- '*.md' | awk '$1!="-"{s+=$1-$2} END{print s+0}')"
  [ "$docnet" -le "$MAX_DOC_GROWTH" ] || fail "docs grew $docnet lines > $MAX_DOC_GROWTH; docs state facts, test logs go in summary.md"
  [ "$(wc -l < PLAN.md)" -le 800 ] || fail "PLAN.md over 800 lines"
  [ "$(wc -l < docs/BUILDING.md)" -le 300 ] || fail "docs/BUILDING.md over 300 lines"
  local f
  while read -r f; do
    [ -f "$f" ] && [ "$(wc -l < "$f")" -gt 1000 ] && fail "$f over 1000 lines; split into Type.Concern.cs partials"
  done < <(grep -E '\.cs$' <<<"$changed")

  [ "$(test_count "")" -ge "$(test_count "$start")" ] || fail "test count dropped ($(test_count "$start") -> $(test_count ""))"
  git diff "$start" -- 'src/*Tests/*.cs' | grep -qE '^\+.*\bSkip *=' && fail "added a skipped test (Skip =); fix it or leave it failing and STOP"

  local tag v_tag v_head v_now; tag="$(git describe --tags --abbrev=0 --match 'v*' "$start")"
  [ -n "$tag" ] || { fail "no v* tag reachable from HEAD; protocol-bump gate needs one"; tag="$start"; }
  v_tag="$(proto_at "$tag")"; v_head="$(proto_at "$start")"; v_now="$(proto_at "")"
  if [ "$v_head" -gt "$v_tag" ]; then
    [ "$v_now" = "$v_head" ] || fail "protocol already bumped since $tag (v$v_tag -> v$v_head); extend the v$v_head CHANGELOG entry, don't bump to v$v_now"
  else
    [ "$v_now" -le $((v_head + 1)) ] || fail "protocol bumped more than once (v$v_head -> v$v_now)"
  fi
  if [ "$v_now" != "$v_head" ] || git diff --name-only "$start" | grep -q '^src/WinterMP.Net/Messages/'; then
    grep -q '^protocol/' <<<"$changed" || fail "wire code changed without protocol/PROTOCOL.md or CHANGELOG.md update"
  fi

  local gb; gb="$(du -s --block-size=1G build 2>/dev/null | cut -f1)"
  [ "${gb:-0}" -le "$MAX_BUILD_GB" ] || fail "build/ is ${gb} GB > $MAX_BUILD_GB; delete test scratch (keep build/local2p, build/release-tools)"

  local b="$STATE/build.log"; : > "$b"
  dotnet build src/WinterMP.Net/WinterMP.Net.csproj -warnaserror >> "$b" 2>&1 || fail "WinterMP.Net build (see $b)"
  dotnet test src/WinterMP.Net.Tests >> "$b" 2>&1 || fail "WinterMP.Net.Tests (see $b)"
  dotnet test src/WinterMP.Launcher.Tests >> "$b" 2>&1 || fail "WinterMP.Launcher.Tests (see $b)"
  if [ -f Directory.Build.props.user ]; then
    dotnet build src/WinterMP.Core/WinterMP.Core.csproj -c Release -warnaserror -t:Rebuild -p:DeployToGame=false >> "$b" 2>&1 || fail "WinterMP.Core Release build (see $b)"
    [ -d tools/GuestSaveProbe ] && { dotnet build tools/GuestSaveProbe/GuestSaveProbe.csproj -c Release -p:DeployToGame=false >> "$b" 2>&1 || fail "GuestSaveProbe build (see $b)"; }
  fi
  [ $rc -ne 0 ] && grep -E ' error [A-Z]+[0-9]+|Failed!|\[FAIL\]' "$b" | sort -u | head -30 >> "$out"
  return $rc
}

# --- prompts ------------------------------------------------------------------------
impl_prompt() {  # $1 iteration  $2 feedback file (optional)  -> prompt file path
  local p="$STATE/prompt-impl.md"
  {
    cat "$HERE/IMPLEMENT.md"
    printf '\n\n## Loop state\n\nIteration %s. Branch `%s`. Slices since last playtest: %s/%s.\n' \
      "$1" "$BRANCH" "$(since)" "$PLAYTEST_EVERY"
    printf '\n### Recent loop log\n\n```\n%s\n```\n' "$(tail -n 25 "$STATE/log.md" 2>/dev/null)"
    printf '\n### Recent commits\n\n```\n%s\n```\n' "$(git log --oneline -12)"
    [ -s "$STATE/playtest-notes.md" ] && printf '\n### Human playtest notes (highest priority)\n\n%s\n' "$(cat "$STATE/playtest-notes.md")"
    if [ -n "${2:-}" ] && [ -s "$2" ]; then
      printf '\n## FIX ROUND\n\nYour uncommitted slice is still in the working tree and was rejected. Fix exactly this, keep the slice otherwise unchanged, and update %s/summary.md:\n\n```\n%s\n```\n' \
        "$STATE" "$(cat "$2")"
    fi
  } > "$p"
  echo "$p"
}
review_prompt() {  # $1 start commit
  local p="$STATE/prompt-review.md"
  git add -N . 2>/dev/null
  { cat "$HERE/REVIEW.md"
    printf '\n\n## Proposed commit message\n\n%s\n\n## Diff against %s\n\n```diff\n' "$(cat "$STATE/summary.md")" "$1"
    git diff "$1" -- . ':!catalog/dump-*' | head -c 400000
    printf '\n```\n'; } > "$p"
  echo "$p"
}

# --- commands -----------------------------------------------------------------------
BRANCH="$(git branch --show-current)"
COUNTER="$PRIVATE/since-playtest.${BRANCH//\//_}"
since()   { cat "$COUNTER" 2>/dev/null || echo 0; }
case "${1:-run}" in
  gates)  # run the gates on the current tree vs HEAD without an agent: checks the setup
    DRY=1 gates "$(git rev-parse HEAD)" "$STATE/gates.out"; r=$?; cat "$STATE/gates.out"; [ $r -eq 0 ] && echo "gates: PASS"; exit $r ;;
  playtested)
    [ -s "$STATE/playtest-notes.md" ] || die "write what you tested and what broke into $STATE/playtest-notes.md first"
    echo 0 > "$COUNTER"; rm -f "$STATE/PLAYTEST.md"; log "playtest acknowledged; notes will steer the next slices"; exit 0 ;;
  status)  # read-only summary for humans and chat agents
    running=no; pgrep -f "agent-loop/loop.sh( run)?$" >/dev/null && running=yes
    echo "branch: $BRANCH   loop process running: $running   slices since playtest: $(since)/$PLAYTEST_EVERY"
    for f in STOP HALT PLAYTEST.md; do [ -f "$STATE/$f" ] && { echo; echo "== $STATE/$f"; cat "$STATE/$f"; }; done
    echo; echo "== last log lines ($STATE/log.md)"; tail -n 15 "$STATE/log.md" 2>/dev/null || echo "(no runs yet)"
    base="$(git merge-base HEAD main 2>/dev/null)"
    echo; echo "== loop commits on this branch"; git log --format='%h %ad %s' --date=short --grep='^Agent-Loop:' ${base:+"$base"..HEAD} | head -20
    echo; echo "== uncommitted work"; git status --short | head -20
    echo; echo "== failed slices set aside"; git stash list | grep 'agent-loop failed' | head -5
    last="$(ls -dt "$STATE"/iter-* 2>/dev/null | head -1)"
    [ -n "$last" ] && { echo; echo "== latest slice details: $last/ (impl.out, review-*.out, feedback-*.txt)"; }
    exit 0 ;;
  run) ;;
  *) echo "usage: $0 [run|status|gates|playtested]"; exit 2 ;;
esac

[[ "$BRANCH" == wip/* ]] || die "run only on a wip/* branch (git switch -c wip/loop-$(date +%F))" 2
[ -z "$(git status --porcelain)" ] || die "working tree not clean" 2
rm -f "$STATE/STOP" "$STATE/HALT"
fails=0
log "run start: agent=$AGENT${MODEL:+/$MODEL} reviewer=$REVIEW_AGENT${REVIEW_MODEL:+/$REVIEW_MODEL} branch=$BRANCH head=$(git rev-parse --short HEAD)"

for ((i = 1; i <= MAX_ITERS; i++)); do
  [ -f "$STATE/HALT" ] && die "halted by $STATE/HALT" 0
  if [ "$(since)" -ge "$PLAYTEST_EVERY" ]; then
    { echo "# Playtest needed"; echo; echo "Slices since the last playtest, with what each left unverified:"; echo
      git log --format='## %h %s%n%n%b' -n "$PLAYTEST_EVERY"
      echo "Play these in a real session, write findings to $STATE/playtest-notes.md,"
      echo "then run: tools/agent-loop/loop.sh playtested && tools/agent-loop/loop.sh"; } > "$STATE/PLAYTEST.md"
    die "playtest gate reached; see $STATE/PLAYTEST.md" 0
  fi
  start="$(git rev-parse HEAD)"; dir="$STATE/iter-$(date +%Y%m%d-%H%M%S)"; mkdir -p "$dir"
  rm -f "$STATE/summary.md"
  log "iter $i: implementing"
  refs="$(refs_hash)"
  run_agent "$AGENT" "$MODEL" "$(impl_prompt "$i")" impl > "$dir/impl.out" 2>&1 \
    || die "implementer exited $? (auth? timeout?); see $dir/impl.out, tree left as is"
  [ "$(refs_hash)" = "$refs" ] || die "implementer changed git refs/HEAD; inspect with git reflog"
  [ -f "$STATE/STOP" ] && die "agent asked to stop: $(cat "$STATE/STOP")  (uncommitted work, if any, is left in the tree)" 0

  ok=0
  for ((round = 0; round <= FIX_ROUNDS; round++)); do
    feedback="$dir/feedback-$round.txt"
    if gates "$start" "$feedback"; then
      before="$(tree_hash "$start")"
      run_agent "$REVIEW_AGENT" "$REVIEW_MODEL" "$(review_prompt "$start")" review > "$dir/review-$round.out" 2>&1 \
        || die "reviewer exited $?; see $dir/review-$round.out, tree left as is"
      [ "$(tree_hash "$start")" = "$before" ] && [ "$(refs_hash)" = "$refs" ] \
        || die "reviewer changed the tree or git refs; inspect $dir"
      verdict="$(grep -oE 'VERDICT: *(APPROVE|REJECT)' "$dir/review-$round.out" | tail -1)"
      if [[ "$verdict" == *APPROVE ]]; then ok=1; break; fi
      cp "$dir/review-$round.out" "$feedback"
      log "iter $i: review rejected (round $round)"
    else
      log "iter $i: gates failed (round $round): $(grep -m1 'GATE FAIL' "$feedback")"
    fi
    [ $round -lt $FIX_ROUNDS ] || break
    run_agent "$AGENT" "$MODEL" "$(impl_prompt "$i" "$feedback")" impl > "$dir/fix-$round.out" 2>&1 \
      || die "fixer exited $?; see $dir/fix-$round.out, tree left as is"
    [ "$(refs_hash)" = "$refs" ] || die "fixer changed git refs/HEAD; inspect with git reflog"
    [ -f "$STATE/STOP" ] && die "agent asked to stop: $(cat "$STATE/STOP")" 0
  done

  if [ $ok -eq 1 ]; then
    git add -A
    { cat "$STATE/summary.md"; printf '\nAgent-Loop: %s%s, reviewed by %s%s\n' "$AGENT" "${MODEL:+/$MODEL}" "$REVIEW_AGENT" "${REVIEW_MODEL:+/$REVIEW_MODEL}"; } \
      | git commit -q -F - || die "commit failed"
    echo $(( $(since) + 1 )) > "$COUNTER"
    fails=0
    log "iter $i: committed $(git log --oneline -1)"
  else
    git add -A && git stash push -q -m "agent-loop failed slice $(basename "$dir")"
    fails=$((fails + 1))
    log "iter $i: slice discarded to stash (fail $fails/$MAX_FAILS); feedback in $dir"
    [ $fails -ge "$MAX_FAILS" ] && die "$MAX_FAILS slices in a row failed; needs a human"
  fi
done
log "run end: MAX_ITERS=$MAX_ITERS reached"
