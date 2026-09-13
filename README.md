AI BUILD TEST
AI pipeline smoke test 2026-09-13: fast CI end-to-end check.

## AI development loop

This repository supports an autonomous AI coding loop with Arena:

1. Create a task in `AI_TASK.md` (set `Status: OPEN`) and commit to
   main - the GitHub web UI is enough.
2. Tell the Arena agent to do the AI task. It reads the task and its
   project context from `AI_AGENT.md`, implements the change, and
   pushes to main.
3. Fast CI validates every push in seconds without launching Unity and
   publishes a machine-readable `AI_CI_STATUS` annotation (JSON) with
   the result, the failed check if any, and whether the failure is a
   project problem or infrastructure.
4. On failure the agent reads the annotation, fixes the problem, and
   pushes again. The last verified result is mirrored in
   `AI_CI_RESULT.json`.
5. On success the agent marks the task done in `AI_TASK.md` and stops.

Operating manual, task format, CI result schema and failure playbook:
`AI_AGENT.md`.
