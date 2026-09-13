# AI task queue

Development tasks for the autonomous AI development loop live in this
directory, one Markdown file per task. The file name fixes the task id:

    001-fix-hero-stats.md
    002-add-heroes-button.md
    ...

Full processing procedure, safety limits and failure handling:
`AI_AGENT.md` (section "Autonomous task queue").

## Task file format

```
# Task 001 - Short Title

Status: OPEN            # OPEN | IN_PROGRESS | DONE | FAILED | BLOCKED
Priority: NORMAL        # HIGH | NORMAL | LOW - default NORMAL
Depends:                # optional: comma-separated task ids that must be
                        # DONE before this task may start (e.g. "001, 003")

## Objective

Describe exactly what should be changed.

## Requirements

- Requirement 1
- Requirement 2

## Constraints

- Do not modify unrelated systems.
- Reuse existing project architecture.

## Acceptance Criteria

- Criterion 1
- Criterion 2
- Fast CI passes.

## Result

Filled in by the AI agent after completion (status, commit SHA,
Fast CI run id, files changed, validation notes).
```

Notes:

- The heading must be `# Task NNN - Title`; the NNN must match the file
  name prefix (a mismatch is reported as a warning).
- `README.md` and `SETTINGS.md` in this directory are not task files;
  only `NNN-slug.md` files are parsed.
- Keep task files ASCII.

## Selection order (deterministic)

1. An active `AI_TASK.md` in the repository root (Status OPEN or
   IN_PROGRESS) takes precedence over this queue.
2. A queue task left IN_PROGRESS is resumed first.
3. Otherwise: the OPEN task whose dependencies are all DONE, ordered by
   priority (HIGH, then NORMAL, then LOW), then by lowest task id.

Check what would run next (read-only, safe to run any time):

```
python3 AI_TASKS/select_next.py
```

Its output also reports OPEN tasks that are blocked by unsatisfied
dependencies (`NOT_READY:` line), a status summary, the session limit
and warnings about malformed task files.

## Session limit and automatic continuation

`SETTINGS.md` holds `MAX_AUTONOMOUS_TASKS` (currently 20; removing the
line falls back to the built-in default of 5).

One instruction - "run the autonomous queue" - processes tasks
automatically, one after another (implement -> push -> Fast CI ->
DONE -> next), with no further input needed between tasks, until the
first stop condition:

- the queue is empty or nothing is ready (unsatisfied dependencies),
- MAX_AUTONOMOUS_TASKS tasks completed this session,
- a task ends FAILED or BLOCKED (the session stops immediately;
  never continues past an unresolved failure),
- or the human says "stop the queue".

Arena cannot wake itself between sessions (no Arena API/webhook
exists and GitHub Actions cannot implement project changes): across
sessions exactly one short message ("continue the queue") resumes the
loop, because all state lives in the task files. See AI_AGENT.md,
"Can the loop run with no human message at all?".
