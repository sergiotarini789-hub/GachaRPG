# AI TASK

Status: IN_PROGRESS
Priority: normal

## Objective

Add a simple main-menu button called "Heroes" to the existing main scene
(visible, clickable, existing project UI conventions).

## Requirements

- Button text must be "Heroes".
- Button must be visible on the main menu.
- Clicking it must open the heroes collection.
- Do not modify unrelated systems.

## Acceptance Criteria

- Fast CI passes on the change commit.
- No unrelated files are changed.
- Task status marked DONE after verification.

## Result

IN PROGRESS - 2026-09-13.

- Discovery: the runtime main menu (MainMenu.cs) already contains a
  Heroes navigation tab wired to BattleTestRunner.OpenHeroesCollection
  (HeroesScreen). The main scene had no object driving the menu, so on
  Play no UI appeared at all.
- Change made (commit 9f9cd64, scene only): added a BattleTestRunner
  GameObject to SampleScene.unity with startWithMainMenu enabled and the
  three prototype heroes assigned by role (attack KaelStormblade,
  defense SirRoland, support LyraLightweaver) - the harness's documented
  setup. On Play the main scene now shows the existing main menu; its
  Heroes button is visible, clickable, and opens the heroes collection.
- Local Fast CI simulation on 9f9cd64: ALL checks pass (structure,
  55 metas / unique GUIDs, scene fileIDs unique, 31 scripts ASCII and
  balanced).
- Fast CI run 34758254563 is QUEUED: the self-hosted runner has been
  offline since ~11:00 UTC. This task is marked DONE once that run
  passes; the result will be recorded here.
