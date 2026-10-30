#!/usr/bin/env python3
"""PreToolUse guard: turns the repo's critical rules into hard checks.

Reads the hook JSON on stdin and prints a permission decision when a rule fires.
Prose rules get violated; these can't be. See AGENTS.md for the why.
"""
import json
import re
import sys
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
FORBIDDEN_TERMS_FILE = PROJECT / ".claude" / "forbidden-terms.local"

# Which paths each agent may write. The main session (no agent_type) is the supervisor.
OWNERSHIP = {
    "backend-builder": ("backend/", "infra/", "fixtures/", ".github/", "docker-compose.yml", "docker-compose.aws.yml",
                        "contracts/", ".zap/", ".nvmrc", ".dockerignore", "scripts/floor-guard.sh", "scripts/dev-login.sh",
                        "scripts/operations-token.sh"),
    "frontend-builder": ("frontend/",),
    "qa": ("e2e/", "docs/verification/"),
    "verifier": ("docs/verification/",),
    "reviewer": (),
    "security": (),
}
SUPERVISOR_FORBIDDEN = ("backend/", "frontend/", "e2e/")

BANNED_PACKAGES = r"MediatR|AutoMapper|Swashbuckle[\w.]*|Duende[\w.]*|Microsoft\.EntityFrameworkCore\.(InMemory|Sqlite)"

CONTENT_RULES = [
    (r"#pragma\s+warning\s+disable", "No new warning suppressions (#pragma warning disable). Fix the cause."),
    (r"SuppressMessage", "No [SuppressMessage]. Fix the cause."),
    (r"severity\s*=\s*none", "No `severity = none` in analyzer config. Fix the cause."),
    (r"UseInMemoryDatabase|UseSqlite\(", "Never mock the database: use Postgres via Testcontainers."),
    (rf'Include="({BANNED_PACKAGES})"', "Banned package (MediatR, AutoMapper, Swashbuckle, Duende, EF InMemory/Sqlite)."),
]
CLOCK_RULE = (r"\bDateTime(Offset)?\.(Now|UtcNow|Today)\b", "Inject TimeProvider; never read the system clock directly.")
# Content rules apply to .NET code and build config only, so docs can describe them.
CODE_SUFFIXES = (".cs", ".csproj", ".props", ".targets", ".editorconfig", ".globalconfig", ".ruleset")

BASH_DENY = [
    (r"--no-verify\b|core\.hooksPath[\s=]+(?!\.githooks\b)\S", "Never bypass git hooks (gitleaks runs there)."),
    (r"git\s+push\b.*(\s-f\b|--force)", "No force pushes."),
    (r"git\s+reset\s+--hard|git\s+clean\s+-[a-z]*f", "No destructive resets; return `blocked` instead."),
    (r"docker\s+(system|volume)\s+prune", "No Docker prunes; other lanes share the daemon."),
    (rf"dotnet\s+add\s+\S*\s*package\s+({BANNED_PACKAGES})\b", "Banned package (see AGENTS.md)."),
]
BASH_ASK = [
    (r"\bgit\b[^;&|\n]*\s(commit|push|merge)\b|\bgh\s+pr\s+merge\b",
     "Every commit, push and merge requires explicit approval."),
    (r"\bterraform\s+(apply|destroy|import)\b", "Infrastructure changes are irreversible: requires explicit approval."),
    (r"(^|[\s;&|])aws\s", "AWS calls require explicit approval (the target account may not exist yet)."),
]


def decide(kind: str, reason: str) -> None:
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": kind,
        "permissionDecisionReason": f"[guard] {reason}",
    }}))
    sys.exit(0)


def forbidden_terms() -> list[str]:
    if not FORBIDDEN_TERMS_FILE.exists():
        return []
    lines = FORBIDDEN_TERMS_FILE.read_text(encoding="utf-8").splitlines()
    return [t.strip() for t in lines if t.strip() and not t.startswith("#")]


def repo_relative(path: str) -> str:
    """Path relative to its worktree root, so ownership works inside .claude/worktrees too."""
    p = Path(path).resolve()
    for parent in [p, *p.parents]:
        if (parent / ".git").exists():
            return p.relative_to(parent).as_posix()
    return p.as_posix()


def new_content(tool: str, tool_input: dict) -> str:
    if tool == "Write":
        return tool_input.get("content", "")
    if tool == "Edit":
        return tool_input.get("new_string", "")
    if tool == "MultiEdit":
        return "\n".join(e.get("new_string", "") for e in tool_input.get("edits", []))
    if tool == "NotebookEdit":
        return tool_input.get("new_source", "")
    return ""


def is_production_cs(rel: str) -> bool:
    return rel.endswith(".cs") and "/tests/" not in f"/{rel}" and ".Tests" not in rel


def check_terms(text: str) -> None:
    for term in forbidden_terms():
        if re.search(rf"\b{re.escape(term)}\b", text, re.IGNORECASE):
            decide("deny", "A forbidden term (see .claude/forbidden-terms.local) is in this change.")


def check_bash(command: str, agent: str | None) -> None:
    check_terms(command)
    for pattern, reason in BASH_DENY:
        if re.search(pattern, command):
            decide("deny", reason)
    for pattern, reason in BASH_ASK:
        if re.search(pattern, command):
            # Subagents never commit, push, merge or touch infrastructure; an "ask" can be
            # auto-resolved in a background run, so only the main session gets the prompt.
            decide("deny" if agent else "ask", reason)


def check_write(tool: str, tool_input: dict, agent: str | None) -> None:
    path = tool_input.get("file_path") or tool_input.get("notebook_path") or ""
    rel = repo_relative(path) if path else ""
    if agent in OWNERSHIP:
        if not rel.startswith(OWNERSHIP[agent]):
            decide("deny", f"{agent} may only write under {', '.join(OWNERSHIP[agent]) or 'nothing (read-only)'}; got {rel}.")
    elif agent is None and rel.startswith(SUPERVISOR_FORBIDDEN):
        decide("deny", "The supervisor never writes production or test code; dispatch a builder or QA.")
    text = new_content(tool, tool_input)
    check_terms(text)
    if rel.endswith(CODE_SUFFIXES):
        for pattern, reason in CONTENT_RULES:
            if re.search(pattern, text):
                decide("deny", reason)
    if is_production_cs(rel) and re.search(CLOCK_RULE[0], text):
        decide("deny", CLOCK_RULE[1])
    if is_production_cs(rel) and re.search(r"^\s*//\s*!", text, re.MULTILINE):
        decide("deny", "No `// !` justification comments: they silence the null-forgiving analyser. Fix the nullability.")


def main() -> None:
    event = json.load(sys.stdin)
    tool = event.get("tool_name", "")
    tool_input = event.get("tool_input", {}) or {}
    agent = event.get("agent_type")
    if tool == "Bash":
        check_bash(tool_input.get("command", ""), agent)
    elif tool in ("Write", "Edit", "MultiEdit", "NotebookEdit"):
        check_write(tool, tool_input, agent)


if __name__ == "__main__":
    main()
