#!/usr/bin/env python3
"""SubagentStop guard: a pipeline agent may only stop after reporting the status contract.

The last message must contain a JSON object with status, assumptions, open_questions
and conflicts, and a status this agent is allowed to report. Otherwise it keeps working.
"""
import json
import re
import sys

ALLOWED = {
    "backend-builder": {"built", "blocked"},
    "frontend-builder": {"built", "blocked"},
    "verifier": {"pass", "fail", "blocked"},
    "qa": {"pass", "fail", "blocked"},
    "reviewer": {"approve", "changes", "blocked"},
    "security": {"pass", "findings", "blocked"},
}
REQUIRED = ("status", "assumptions", "open_questions", "conflicts")


def contract(message: str) -> dict | None:
    for block in re.findall(r"\{.*\}", message, re.DOTALL):
        try:
            data = json.loads(block)
        except json.JSONDecodeError:
            continue
        if isinstance(data, dict) and all(k in data for k in REQUIRED):
            return data
    return None


def main() -> None:
    event = json.load(sys.stdin)
    agent = event.get("agent_type")
    if agent not in ALLOWED or event.get("stop_hook_active"):
        return
    data = contract(event.get("last_assistant_message", ""))
    if data is None:
        reason = f"End with the status contract JSON: {{{', '.join(REQUIRED)}}}."
    elif data["status"] not in ALLOWED[agent]:
        reason = f"status must be one of {sorted(ALLOWED[agent])}; got {data['status']!r}."
    elif not all(isinstance(data[k], list) for k in REQUIRED[1:]):
        reason = "assumptions, open_questions and conflicts must be lists (empty is fine)."
    else:
        return
    print(json.dumps({"decision": "block", "reason": f"[status-contract] {reason}"}))


if __name__ == "__main__":
    main()
