#!/usr/bin/env python3
"""Deterministic next-task selector for the GachaRPG AI task queue.

Read-only helper for the autonomous AI development loop: it never edits
anything, it only reports which task the agent must work on next.

Selection rules (see AI_AGENT.md, "Autonomous task queue"):
  1. If AI_TASK.md (repo root) has Status OPEN or IN_PROGRESS, that
     single task always takes precedence over the queue.
  2. Otherwise, a queue task left IN_PROGRESS is resumed first (lowest
     priority rank, then lowest id).
  3. Otherwise the next task is the OPEN task whose Depends ids are all
     DONE, with the lowest priority rank (HIGH, then NORMAL, then LOW)
     and, on ties, the lowest numeric task id.

Usage:
    python3 AI_TASKS/select_next.py             # this repository
    python3 AI_TASKS/select_next.py --root DIR  # DIR contains AI_TASK.md
                                               # (optional) and AI_TASKS/

Output: stable KEY: VALUE lines. NEXT_TASK_FILE is the relative path of
the task to select, or NONE with a REASON line. Warnings are reported as
WARNING: lines and never abort the selection.
"""

import argparse
import os
import re
import sys

PRIORITY_RANK = {"HIGH": 0, "NORMAL": 1, "LOW": 2}
ACTIVE_SINGLE_STATUSES = {"OPEN", "IN_PROGRESS"}
KNOWN_STATUSES = {"OPEN", "IN_PROGRESS", "DONE", "FAILED", "BLOCKED"}
TASK_FILE_PATTERN = re.compile(r"^(\d+)-(.+)\.md$")
HEADING_PATTERN = re.compile(r"^# Task\s+(\d+)\s*[-:\u2014]\s*(.*)$", re.M)
MAX_TASKS_PATTERN = re.compile(r"^MAX_AUTONOMOUS_TASKS\s*=\s*(\d+)\s*$", re.M)
DEFAULT_MAX_AUTONOMOUS_TASKS = 5


def parse_task_file(path):
    """Extract id, title, status, priority and dependencies from a task file."""
    with open(path, encoding="utf-8", errors="replace") as handle:
        text = handle.read()

    def field(name):
        match = re.search(r"^" + name + r":\s*(.*)$", text, re.M)
        return match.group(1).strip() if match else ""

    status = field("Status").upper()
    priority = field("Priority").upper()
    depends_raw = field("Depends")

    deps = []
    for part in re.split(r"[,\s]+", depends_raw):
        if part and part.lower() not in ("none", "-"):
            deps.append(part)

    heading = HEADING_PATTERN.search(text)
    heading_id = int(heading.group(1)) if heading else None
    title = heading.group(2).strip() if heading else ""

    return {
        "path": path,
        "id": None,  # filled by the caller from the file name
        "heading_id": heading_id,
        "title": title,
        "status": status,
        "priority": priority if priority in PRIORITY_RANK else "NORMAL",
        "priority_raw": priority,
        "depends": deps,
    }


def load_queue(tasks_dir, warnings):
    """Parse every NNN-slug.md task file in tasks_dir."""
    tasks = []
    if not os.path.isdir(tasks_dir):
        return tasks
    for name in sorted(os.listdir(tasks_dir)):
        match = TASK_FILE_PATTERN.match(name)
        if not match:
            continue
        path = os.path.join(tasks_dir, name)
        task = parse_task_file(path)
        task["id"] = int(match.group(1))
        task["slug"] = match.group(2)
        if not task["title"]:
            task["title"] = task["slug"].replace("-", " ")
        if task["status"] not in KNOWN_STATUSES:
            warnings.append(
                "%s: unknown Status '%s' (expected OPEN, IN_PROGRESS, "
                "DONE, FAILED or BLOCKED) - task is not selectable"
                % (name, task["status"] or "(missing)"))
        if task["priority_raw"] and task["priority_raw"] not in PRIORITY_RANK:
            warnings.append(
                "%s: unknown Priority '%s' - using NORMAL"
                % (name, task["priority_raw"]))
        if task["heading_id"] is not None and task["heading_id"] != task["id"]:
            warnings.append(
                "%s: heading says Task %d but the file name says %d"
                % (name, task["heading_id"], task["id"]))
        if any(dep.isdigit() and int(dep) == task["id"] for dep in task["depends"]):
            warnings.append("%s: depends on itself - never selectable" % name)
        tasks.append(task)
    return tasks


def read_max_autonomous_tasks(tasks_dir):
    settings_path = os.path.join(tasks_dir, "SETTINGS.md")
    if os.path.isfile(settings_path):
        with open(settings_path, encoding="utf-8", errors="replace") as handle:
            match = MAX_TASKS_PATTERN.search(handle.read())
            if match:
                return int(match.group(1))
    return DEFAULT_MAX_AUTONOMOUS_TASKS


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--root",
        default=None,
        help="repository root containing AI_TASK.md and AI_TASKS/ "
             "(default: the parent of this script's directory)")
    args = parser.parse_args()

    script_dir = os.path.dirname(os.path.abspath(__file__))
    root = os.path.abspath(args.root) if args.root else os.path.dirname(script_dir)
    tasks_dir = os.path.join(root, "AI_TASKS")
    single_path = os.path.join(root, "AI_TASK.md")

    warnings = []
    lines = []

    # 1) Single-task compatibility interface: an active AI_TASK.md wins.
    single_status = ""
    if os.path.isfile(single_path):
        single = parse_task_file(single_path)
        single_status = single["status"]
        if single_status in ACTIVE_SINGLE_STATUSES:
            lines.append("MODE: single")
            lines.append("SINGLE_TASK_FILE: AI_TASK.md")
            lines.append("SINGLE_TASK_STATUS: %s" % single_status)
            lines.append("NEXT_TASK_FILE: AI_TASK.md")
            lines.append("NEXT_TASK_ID: NONE")
            lines.append("NEXT_TASK_TITLE: %s" % (single["title"] or "(untitled)"))
            lines.append("NEXT_TASK_PRIORITY: %s" % single["priority"])
            lines.append("NEXT_TASK_STATUS: %s" % single_status)
            lines.append("NEXT_TASK_DEPENDS: (none)")
            lines.append(
                "REASON: AI_TASK.md is active (%s) and takes precedence "
                "over the AI_TASKS/ queue" % single_status)
            lines.append("SUMMARY: (queue not evaluated)")
            lines.append(
                "MAX_AUTONOMOUS_TASKS: %d"
                % read_max_autonomous_tasks(tasks_dir))
            for warning in warnings:
                lines.append("WARNING: %s" % warning)
            print("\n".join(lines))
            return 0

    # 2) Queue mode.
    tasks = load_queue(tasks_dir, warnings)
    by_id = {}
    for task in tasks:
        if task["id"] in by_id:
            warnings.append(
                "duplicate task id %03d (%s, %s)"
                % (task["id"], os.path.basename(by_id[task["id"]]["path"]),
                   os.path.basename(task["path"])))
        else:
            by_id[task["id"]] = task

    counts = {}
    for status in sorted(KNOWN_STATUSES):
        counts[status] = sum(1 for task in tasks if task["status"] == status)
    unknown = sum(1 for task in tasks if task["status"] not in KNOWN_STATUSES)

    def dep_state(dep):
        dep_id = int(dep) if dep.isdigit() else None
        if dep_id is None or dep_id not in by_id:
            return "UNKNOWN"
        return by_id[dep_id]["status"]

    # 2a) Resume an interrupted queue task first.
    in_progress = sorted(
        (t for t in tasks if t["status"] == "IN_PROGRESS"),
        key=lambda t: (PRIORITY_RANK[t["priority"]], t["id"]))
    selected = None
    reason = ""
    if in_progress:
        selected = in_progress[0]
        reason = "resume the unfinished task first (never start a new " \
                 "task while one is IN_PROGRESS)"
    else:
        candidates = []
        not_ready = []
        for task in tasks:
            if task["status"] != "OPEN":
                continue
            unmet = []
            for dep in task["depends"]:
                if dep_state(dep) != "DONE":
                    unmet.append("%s:%s" % (dep, dep_state(dep)))
            if unmet:
                not_ready.append("%03d(%s)" % (task["id"], ",".join(unmet)))
                continue
            candidates.append(task)
        candidates.sort(key=lambda t: (PRIORITY_RANK[t["priority"]], t["id"]))
        if candidates:
            selected = candidates[0]
            reason = "highest priority, then lowest task id, dependencies OK"
        elif counts["OPEN"] > 0:
            reason = "OPEN task(s) exist but their dependencies are not " \
                     "satisfied (or ids are unknown)"
        else:
            reason = "no OPEN task in the queue"
        if not_ready:
            lines.append("NOT_READY: %s" % " ".join(not_ready))

    lines.append("MODE: queue")
    if selected:
        deps = ", ".join(
            "%s(%s)" % (dep, dep_state(dep)) for dep in selected["depends"]
        ) or "(none)"
        rel = os.path.relpath(selected["path"], root)
        lines.append("NEXT_TASK_FILE: %s" % rel.replace(os.sep, "/"))
        lines.append("NEXT_TASK_ID: %03d" % selected["id"])
        lines.append("NEXT_TASK_TITLE: %s" % selected["title"])
        lines.append("NEXT_TASK_PRIORITY: %s" % selected["priority"])
        lines.append("NEXT_TASK_STATUS: %s" % selected["status"])
        lines.append("NEXT_TASK_DEPENDS: %s" % deps)
        lines.append("REASON: %s" % reason)
    else:
        lines.append("NEXT_TASK_FILE: NONE")
        lines.append("NEXT_TASK_ID: NONE")
        lines.append("NEXT_TASK_TITLE: NONE")
        lines.append("NEXT_TASK_PRIORITY: NONE")
        lines.append("NEXT_TASK_STATUS: NONE")
        lines.append("NEXT_TASK_DEPENDS: NONE")
        lines.append("REASON: %s" % reason)
    lines.append(
        "SUMMARY: OPEN=%d IN_PROGRESS=%d DONE=%d FAILED=%d BLOCKED=%d UNKNOWN=%d"
        % (counts["OPEN"], counts["IN_PROGRESS"], counts["DONE"],
           counts["FAILED"], counts["BLOCKED"], unknown))
    lines.append("MAX_AUTONOMOUS_TASKS: %d" % read_max_autonomous_tasks(tasks_dir))
    for warning in warnings:
        lines.append("WARNING: %s" % warning)
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main())
