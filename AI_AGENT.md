# AI Agent Operating Manual (GachaRPG)

Persistent operating manual and project context for the AI coding agent
(Arena). A new agent session reads this file, the current AI_TASK.md and
the recent git log before doing anything else. The human never has to
re-explain the project: it is all in the repository.

## The loop

    human writes AI_TASK.md (Status: OPEN) and tells Arena "do the AI task"
      -> Arena implements the change
      -> Arena commits and pushes to main
      -> Fast CI validates (seconds, no Unity)
      -> PASS: Arena marks the task done in AI_TASK.md and reports
      -> FAIL: Arena reads AI_CI_STATUS, fixes, pushes again, re-checks

Termination rules (hard):

- CI never writes to the repository, so a CI run can never trigger
  another CI run. Only the agent pushes, and only while a task is open.
- If a task's CI is still red after 3 fix attempts, the agent stops,
  marks the task Status: FAILED with a diagnosis in the Result section,
  and reports to the human. No endless retry loops.
- A task is DONE only when its acceptance criteria hold and Fast CI is
  green on the final commit. That is the completion condition.

## Project context

- Unity 6000.3.24f1, URP 2D, ugui 2.0.0, no asmdefs (all scripts compile
  into the default Assembly-CSharp). Scene: Assets/Scenes/SampleScene.unity.
- Code: Assets/Scripts/Combat (BattleTestRunner, CombatManager, TeamBattle,
  BattleHud), Assets/Scripts/Data (HeroData, SkillData, Equipment*,
  HeroProgression, PlayerSaveSystem, PlayerWallet, SummonService),
  Assets/Scripts/UI.
- Gameplay data: Unity .asset files under Assets/Data (heroes, skills).
  Equipment system is save-format v4 (PlayerSaveSystem migrates v3 -> v4).
- CI runs on a self-hosted Windows runner; workflow shells are
  powershell (Windows PowerShell 5.1). Keep step scripts PS 5.1-safe.

Hard conventions:

- Source files ASCII-only (Fast CI enforces this for C# files).
- Every new file and folder under Assets/ needs its .meta committed
  (Fast CI enforces this).
- Never delete the Unity Library folder (the manual build stays warm
  only because it is preserved); never force-push main; do not modify
  .github/workflows unless the task explicitly requires it.

## CI

Two workflows, deliberately split:

- Fast CI (.github/workflows/ci.yml) - triggers on every push to main.
  Four checks, ~15 seconds, NEVER launches Unity: git integrity, project
  structure, meta/GUID integrity, C# sanity. It also publishes the
  machine-readable status described below.
- Unity Build (.github/workflows/build.yml) - workflow_dispatch ONLY
  (manual, from the Actions tab). Full Unity import/validation plus the
  Windows x64 player build (~2 minutes warm, produces the
  GachaRPG-Windows-x64-build artifact). Run it only when the human asks
  for a build or when a gameplay change explicitly needs Unity
  verification - never as part of the ordinary loop.

## Reading a CI result (machine-readable)

Every Fast CI run publishes one annotation titled AI_CI_STATUS whose
message is compact JSON:

    {
      "schema": 1,
      "result": "pass" | "fail",
      "category": "none" | "project-validation" | "infrastructure",
      "failed_checks": [ "git" | "structure" | "meta" | "scripts" ],
      "checks": { "git": "...", "structure": "...", "meta": "...", "scripts": "..." },
      "suggested": "next action for the agent",
      "commit": "<full sha of the checked commit>",
      "run_id": "<run id>",
      "unity_launched": false,
      "timestamp": "<UTC>"
    }

Fetch it (verified working from the Arena sandbox):

    RUN=$(gh run list --branch main --limit 5 --json databaseId,headSha --jq '[.[] | select(.headSha | startswith("<sha-prefix>"))][0].databaseId')
    gh run view $RUN --json status,conclusion,jobs
    JOB=$(gh api repos/sergiotarini789-hub/GachaRPG/actions/runs/$RUN/jobs --jq '.jobs[0].id')
    gh api repos/sergiotarini789-hub/GachaRPG/check-runs/$JOB/annotations

The annotation list also carries the per-step ::error:: messages with
the exact files and lines - that is the relevant error for any failure.
(gh run view --log and artifact downloads are blocked in the Arena
sandbox; the annotations API is the reliable channel.)

Category semantics:

- project-validation - the project content is broken. Fix the project
  as described in the error annotations and the playbook below.
- infrastructure - the runner or CI itself had a problem (cancellation,
  timeout, report step crash). Re-run the workflow first; only inspect
  CI config if it repeats. Do not change the project for these.

## AI_CI_RESULT.json

The agent (not CI - CI must never write to the repo) mirrors the last
verified Fast CI result into AI_CI_RESULT.json in the repository root.
Update it (a) after fixing a failed run and (b) when completing a task.
Fields: schema, timestamp, commit, workflow, run_id, result, category,
failed_checks, error (one short line - never full logs), suggested,
unity_launched. The GitHub run itself is always authoritative; if the
file looks stale, trust the run.

## Failure playbook

- failed_checks contains "git": merge-conflict markers, fsck trouble or
  HEAD mismatch. Resolve the markers / inspect the workspace. Never
  force-push to "fix" this.
- "structure": a broken ProjectVersion.txt, ProjectSettings, package
  manifest, a scene missing from disk, or a Unity YAML file with a bad
  header. The annotation names the exact file.
- "meta": missing .meta files (commit them - never let Unity regenerate
  GUIDs), a .meta without a guid line, duplicate GUIDs, or duplicate
  scene object ids. The annotation lists the exact paths.
- "scripts": non-ASCII characters or unbalanced braces/parentheses/
  brackets in a .cs file. The annotation names the files.
- category "infrastructure": re-run the workflow once (gh api
  .../actions/runs is read-only from Arena; the human can also re-run
  from the Actions tab). If it repeats, report instead of changing CI.

## Committing

1. Work on the session branch arena/01a09200-gacharpg.
2. Commit with a clear, ASCII-only message.
3. Push both refs and verify they match:
   git push origin HEAD:refs/heads/arena/01a09200-gacharpg
   git push origin HEAD:refs/heads/main
   git fetch origin main:main && git rev-parse HEAD main origin/main
   If a push is rejected (shallow clone), run
   git fetch --unshallow origin and retry.
4. Poll the CI result with the commands above. Green -> proceed to
   completion. Red -> playbook, fix, push again.

## Completing a task

1. Verify every acceptance criterion in AI_TASK.md holds and Fast CI is
   green on the final commit.
2. Update AI_TASK.md: Status: DONE (or FAILED) and fill the Result
   section - outcome summary, final commit SHA, CI run id, and anything
   the human should double-check (e.g. "run Unity Build to see it in
   game").
3. Update AI_CI_RESULT.json with that final verified run.
4. Commit ("Complete AI task: <objective>"), push, confirm Fast CI is
   green on that commit too, then report to the human and stop. The
   loop ends here - never start new work that was not tasked.

## Task file format (AI_TASK.md)

One active task at a time:

    # AI TASK
    Status: OPEN        # NONE | OPEN | IN_PROGRESS | DONE | FAILED
    Priority: normal    # optional: low | normal | high
    ## Objective        - what the human wants
    ## Requirements     - concrete, verifiable demands
    ## Constraints      - what must NOT change
    ## Acceptance Criteria - how DONE is judged (always includes
                            "Fast CI passes")
    ## Result           - filled in by the agent at the end

The human creates the task by editing AI_TASK.md on GitHub (or a local
commit) and mentions it to Arena. The agent moves the status forward
and fills Result. Git history of AI_TASK.md is the task archive.
