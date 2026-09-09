## Outcome and task

Link the existing queue/roadmap item or issue. What concrete behavior changes?

## Scope and non-goals

Which subsystem owns this? What was deliberately not changed?

## Acceptance evidence

| Criterion / reproduction | Expected and observed result | Test / artifact |
|---|---|---|
| | | |

## Verification

Exact commands, exit codes, test counts and report paths. Distinguish portable,
engine, real-input, visual, multiplayer and save-compatibility checks as relevant.

**Not verified / blockers:** state missing prerequisites or checks explicitly.

## Risk and rollback

Compatibility, saved data, protocol, dependencies or performance impact; how to
revert safely. State “none” with a reason when these are not affected.

## Review

- [ ] The real entry point exercises the implementation; this is not unused scaffolding.
- [ ] Required checks passed; missing evidence is not represented as a pass.
- [ ] Regression/edge-case coverage and runtime evidence match the change.
- [ ] No unrelated refactor, foreign WIP, generated output or secrets are included.
- [ ] Only changed contracts/docs were updated; remaining work stays in the existing queue.
