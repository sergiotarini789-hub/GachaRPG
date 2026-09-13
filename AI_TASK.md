# AI TASK

Status: DONE
Priority: normal

## Objective

Perform a small real project change to verify the complete AI development loop.

## Requirements

- Add a visible "AI PIPELINE TEST" label to the main scene.
- Place it somewhere clearly visible but unobtrusive.
- Do not remove or break existing UI.
- Do not modify unrelated gameplay systems.
- Keep the implementation simple and maintainable.

## Constraints

- Do not modify CI/CD workflows.
- Do not run a full Unity Build.
- Do not change unrelated project files.

## Acceptance Criteria

- The project remains valid.
- The change is committed to git.
- Fast CI passes.
- No unrelated files are changed.
- Task status can be marked DONE after successful verification.

## Result

DONE - 2026-09-13.

- The main scene already contained the AI PIPELINE TEST label (added by
  an earlier pipeline test), anchored dead-center at 900x140 with font
  size 56 - visible but obtrusive.
- Change made: repositioned the existing label to the top-left corner
  (400x50 rect, 20px margin, font size 28), exactly matching the task
  requirement "clearly visible but unobtrusive". The label text remains
  'AI PIPELINE TEST'; no new objects, no fileID changes, no other UI or
  gameplay files touched.
- Commit: f71b5ad1c7b3aa1bfcc81b71cdaeb0a65dfe1bb6
- Fast CI: run 34752618429, PASS (all checks green, no Unity launched).
- Note for the human: run the manual Unity Build workflow to see the
  label in-game if desired.
