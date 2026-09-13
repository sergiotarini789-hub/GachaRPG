# AI TASK

Status: NONE

There is no active task.

To start one, replace the body of this file below the heading (keep the
`# AI TASK` heading and set `Status: OPEN`), commit to main, then tell
the Arena agent to do the AI task. Task format, statuses and the full
operating manual: AI_AGENT.md.

Template:

```
# AI TASK

Status: OPEN            # NONE | OPEN | IN_PROGRESS | DONE | FAILED
Priority: normal        # optional: low | normal | high

## Objective
One or two sentences describing the change.

## Requirements
- ...

## Constraints
- ...

## Acceptance Criteria
- Fast CI passes.
- ...

## Result
(Filled in by the AI agent on completion: outcome, final commit SHA,
CI run id, notes for the human.)
```
