#!/usr/bin/env python3
"""Deterministic test for the autonomous session limit.

Proves that the session limit is taken from AI_TASKS/SETTINGS.md,
currently 20, and that nothing accidentally keeps it at the old value
of 5:

  1. SETTINGS.md must parse to MAX_AUTONOMOUS_TASKS=20.
  2. select_next.py must report MAX_AUTONOMOUS_TASKS: 20 for this repo.
  3. Simulated session over a synthetic queue of 25 OPEN tasks:
     selecting and completing tasks must keep working past the old
     limit of 5 (task 6 must be offered after 5 completions) and the
     session must be able to complete exactly 20 tasks - the 20th
     selection must succeed and leave 5 OPEN tasks behind.

Run:  python3 AI_TASKS/test_session_limit.py
Exit status 0 = pass, 1 = fail. Standard library only.
"""

import os
import re
import subprocess
import sys
import tempfile

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
SELECTOR = os.path.join(SCRIPT_DIR, "select_next.py")
SETTINGS = os.path.join(SCRIPT_DIR, "SETTINGS.md")

EXPECTED_LIMIT = 20
OLD_LIMIT = 5
SYNTHETIC_QUEUE_SIZE = 25

failures = []


def check(name, condition, detail=""):
    if condition:
        print("PASS: %s" % name)
    else:
        failures.append(name)
        print("FAIL: %s%s" % (name, (" - " + detail) if detail else ""))


def read_limit_from_settings():
    with open(SETTINGS, encoding="utf-8") as handle:
        match = re.search(r"^MAX_AUTONOMOUS_TASKS\s*=\s*(\d+)\s*$", handle.read(), re.M)
        return int(match.group(1)) if match else None


def selector_output(root):
    result = subprocess.run(
        [sys.executable, SELECTOR, "--root", root],
        capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError("selector failed: %s" % result.stderr)
    return result.stdout


def selector_field(output, key):
    for line in output.splitlines():
        if line.startswith(key + ": "):
            return line[len(key) + 2:]
    return None


def write_task(root, name):
    tid = name.split("-")[0]
    with open(os.path.join(root, "AI_TASKS", name), "w") as handle:
        handle.write(
            "# Task %s - %s\n\nStatus: OPEN\n\n## Objective\nTest.\n\n"
            "## Result\n\n" % (tid, name))


def set_status(root, name, status):
    path = os.path.join(root, "AI_TASKS", name)
    with open(path) as handle:
        text = handle.read()
    with open(path, "w") as handle:
        handle.write(re.sub(r"^Status: .*$", "Status: %s" % status, text, count=1, flags=re.M))


def main():
    # 1) The configured limit is 20, not the old 5.
    limit = read_limit_from_settings()
    check("SETTINGS.md parses", limit is not None, "no MAX_AUTONOMOUS_TASKS line found")
    check("SETTINGS.md limit is %d" % EXPECTED_LIMIT, limit == EXPECTED_LIMIT,
          "got %r" % limit)
    check("SETTINGS.md limit is not the old %d" % OLD_LIMIT, limit != OLD_LIMIT)

    # 2) The selector reports the configured limit for this repository.
    reported = selector_field(selector_output(REPO_ROOT), "MAX_AUTONOMOUS_TASKS")
    check("selector reports limit %d for this repo" % EXPECTED_LIMIT,
          reported == str(EXPECTED_LIMIT), "got %r" % reported)

    # 3) Simulated session over a synthetic queue of 25 OPEN tasks.
    with tempfile.TemporaryDirectory() as root:
        os.makedirs(os.path.join(root, "AI_TASKS"))
        with open(os.path.join(root, "AI_TASK.md"), "w") as handle:
            handle.write("# AI TASK\n\nStatus: NONE\n")
        names = ["%03d-task.md" % i for i in range(1, SYNTHETIC_QUEUE_SIZE + 1)]
        for name in names:
            write_task(root, name)

        completed = 0
        session_limit = limit  # what the agent reads from SETTINGS.md
        stop_reason = None
        while True:
            if completed >= session_limit:
                stop_reason = "limit reached after %d tasks" % completed
                break
            out = selector_output(root)
            if selector_field(out, "NEXT_TASK_FILE") == "NONE":
                stop_reason = "queue empty after %d tasks" % completed
                break
            selected = selector_field(out, "NEXT_TASK_FILE")
            set_status(root, os.path.basename(selected), "DONE")
            completed += 1

        check("simulated session completed exactly %d tasks" % EXPECTED_LIMIT,
              completed == EXPECTED_LIMIT, "completed %d" % completed)
        check("session stopped because of the limit (not an empty queue)",
              stop_reason == "limit reached after %d tasks" % EXPECTED_LIMIT,
              stop_reason or "")

        # No residual 5-cap: after 5 completions task 6 must be offered.
        remaining = SYNTHETIC_QUEUE_SIZE - completed
        check("%d OPEN tasks remain after the session" % (SYNTHETIC_QUEUE_SIZE - EXPECTED_LIMIT),
              remaining == SYNTHETIC_QUEUE_SIZE - EXPECTED_LIMIT)

        # Rewind to exactly 5 completions and require task 6 to be next.
        for name in names:
            set_status(root, name, "OPEN")
        for name in names[:OLD_LIMIT]:
            set_status(root, name, "DONE")
        out = selector_output(root)
        nxt = selector_field(out, "NEXT_TASK_FILE")
        check("after %d completions the next task offered is 006 (no %d-cap)"
              % (OLD_LIMIT, OLD_LIMIT), nxt == "AI_TASKS/006-task.md", "got %r" % nxt)

    print()
    if failures:
        print("FAILED: %d check(s): %s" % (len(failures), ", ".join(failures)))
        return 1
    print("ALL SESSION-LIMIT TESTS PASSED (limit = %d)" % EXPECTED_LIMIT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
