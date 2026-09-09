# Delivery and review

This process is for contributors and agents. It does not require the owner to
read documentation or operate a project-management system. Use the existing
project queue identified in `AGENTS.md`; an issue/PR links that item rather than
creating a second source of priority.

## A task is ready when its result can be checked

Keep one short brief in the issue, PR, or existing queue item:

- **Outcome:** what becomes possible, or which observed bug stops happening.
- **Scope:** owning subsystem and explicit non-goals. Read its code and tests.
- **Acceptance:** reproduction/scenario, expected behavior, negative/edge case,
  and the commands or manual observations that would demonstrate success.

For a bug, reproduce it before changing code and add a regression test where
possible. For a feature, trace the real entry point through rules/state to
visible behavior or persistence. A new class, passing mock, or updated checkbox
is not by itself the requested outcome. Make the smallest complete change, not
the smallest cosmetic change.

## Implement without creating a second problem

Use an isolated branch/worktree and respect the repository's existing builder
pipeline. Prefer existing subsystem owners; introduce an abstraction only when
the task needs it. Do not mix a bug fix with bulk formatting, dependency upgrades,
source moves or scheduler rewrites. Record genuinely discovered follow-up work
in the existing backlog rather than silently adding it to this change.

Small fixes do not need separate plan, retro, handoff, and architecture files.
Use the brief and PR body. Larger work needs a plan only when it coordinates
real dependencies or decisions. Update existing docs when their contract changes;
do not append a new standing rule for every incident. After repeated failed
attempts, retain the reproduction and change the hypothesis rather than retrying
the same edit or deleting the test.

## A change is done when the evidence matches the claim

Run the repository-specific checks in `AGENTS.md`. Keep commands, exit status,
test counts and report paths with the change. Distinguish automated checks from
manual/runtime verification. A missing dependency, absent report, timeout or
unavailable display is a limitation, not a pass. Do not report a failing or
unexecuted check as green because an unrelated suite passed.

Review the actual diff against each acceptance criterion. Check the real call
site, error path, lifecycle/cleanup, relevant compatibility and saved state.
Player-visible changes need actual observed evidence; test doubles cannot prove
rendering, input, Steam networking, or successful play. Explain anything left
unverified and how it can be checked. Do not merge past a required failed gate.

Use the PR template for the review handoff. A reviewer verifies evidence and
scope, rather than merely repeating the author's summary. The owner receives a
plain-language result in chat: what changed, what was checked, what remains.
The existing authorized merge path remains in force; this file does not grant
new permission to merge, deploy, release, or alter repository settings.
