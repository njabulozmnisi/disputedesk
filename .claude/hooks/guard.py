#!/usr/bin/env python3
"""PreToolUse guard: turns the repo's critical rules into hard checks.

Reads the hook JSON on stdin and prints a permission decision when a rule fires.
Prose rules get violated; these can't be. See AGENTS.md for the why.
"""
import json
import re
import shlex
import sys
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
FORBIDDEN_TERMS_FILE = PROJECT / ".claude" / "forbidden-terms.local"

# Which paths each agent may write. The main session (no agent_type) is the supervisor.
OWNERSHIP = {
    "backend-builder": ("backend/", "infra/", ".github/", "docker-compose.yml", "contracts/", ".zap/", ".nvmrc",
                        "scripts/", "docs/verification/"),
    "frontend-builder": ("frontend/", "docs/verification/"),
    "qa": ("e2e/", "docs/verification/"),
    "verifier": ("docs/verification/",),
    "reviewer": ("docs/verification/",),
    "security": ("docs/verification/",),
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

# Read on the command text and on each simple command's words, so quoting cannot hide them.
BASH_DENY = [
    (r"--no-verify\b|core\.hooksPath[\s=]+(?!\.githooks\b)\S", "Never bypass git hooks (gitleaks runs there)."),
    (r"git\s+reset\s+--hard|git\s+clean\s+-[a-z]*f", "No destructive resets; return `blocked` instead."),
    (r"docker\s+(system|volume)\s+prune", "No Docker prunes; other lanes share the daemon."),
    (rf"dotnet\s+add\s+\S*\s*package\s+({BANNED_PACKAGES})\b", "Banned package (see AGENTS.md)."),
]
# Only actions that change a deployed environment ask the human (decision 2026-11-05); gh_ask_reasons adds the gh ones.
DEPLOY_REASON = "Deploying to an environment requires explicit approval."
BASH_ASK = [
    (r"\bterraform\s+(apply|destroy|import)\b", "Infrastructure changes are irreversible: requires explicit approval."),
    (r"(^|[\s;&|])aws\s", "AWS calls require explicit approval (the target account may not exist yet)."),
    (r"/actions/workflows/\S*deploy\S*/dispatches|\bdeploy\.sh\b", DEPLOY_REASON),
]
DEPLOY_ENABLED_REASON = "DEPLOY_ENABLED switches deploy on merge: requires explicit approval."
DEPLOY_ENABLED = r"(?i)\bDEPLOY_ENABLED\b"  # GitHub variable names are case-insensitive

# Commands are split into words the way a POSIX shell does, with Python's shlex (docs.python.org/3/library/shlex.html):
# quotes and backslashes are honoured, and ( ) ; & | and a newline end a simple command. A form shlex does not model
# makes the command unreadable: ANSI-C or locale quoting ($'…', $"…"), a separator written as a quoted or escaped word,
# or brace expansion. An unreadable command that names git or gh fails closed.
UNREADABLE = r"\$['\"]|(['\"])[();&|<>\n]+\1|\\[();&|<>]"
BRACE_EXPANSION = r"\{[^{}\s]*(,|\.\.)[^{}\s]*\}"
GIT_OR_GH = r"\b(git|gh)\b"
# A command or process substitution runs a command the rules cannot read: it is denied to a pipeline agent, and asks
# the main session when the command names git or gh. The two the flow writes run only a read and are taken out first:
# the owner's token for gh (AGENTS.md, Commits) and the repository root (build-story's full run).
SUBSTITUTION = r"\$\(|`|[<>]\("
KNOWN_SUBSTITUTION = r"\$\(gh auth token --user [A-Za-z0-9-]+\)|\$\(git rev-parse --show-toplevel\)"
# Bash ends a comment at the newline even after a backslash (bash manual, Comments), where it otherwise joins the lines.
COMMENT_CONTINUATION = r"(^|\s)#[^\n]*\\\n"
# A redirection operator (POSIX Shell Command Language 2.7: < > >> >| <> <& >& << <<-, and bash's &> &>> <<<).
REDIRECTION = r"&?[<>]+[&|]?"
# These run a word as shell text, so the words after them are read again as commands.
SHELLS = ("sh", "bash", "zsh", "dash", "ksh", "$SHELL", "${SHELL}")
SHELL_RUNNERS = SHELLS + ("eval", "ssh", "watch")
# Long options of bash that take the next word as their value (bash manual, Invoking Bash).
SHELL_VALUE_OPTIONS = ("--rcfile", "--init-file")
# `.` and `source` given one of these files read their commands from standard input (bash manual, Bourne Shell
# Builtins); `builtin` and `command` and leading assignments may come before them.
STANDARD_INPUT_FILES = ("/dev/stdin", "/dev/fd/0", "/proc/self/fd/0")
ASSIGNMENT = r"[A-Za-z_][A-Za-z0-9_]*=.*"

# git's global options that take the next word as their value (git help git).
GIT_VALUE_OPTIONS = ("-C", "-c", "--git-dir", "--work-tree", "--namespace", "--config-env", "--attr-source")
# git push forces with a short-option cluster holding f (-f, -uf, -4f), any prefix of these long options (git takes an
# unambiguous prefix), or a + refspec (git help push).
FORCE_OPTIONS = ("force", "force-with-lease", "force-if-includes", "mirror")
# The one force push that runs: a stacked story's push after its rebase onto main (AGENTS.md, Commits). It must be the
# whole command, and it gets no decision, never "allow".
STACKED_PUSH = ["push", "--force-with-lease", "--force-if-includes", "origin"]
STACKED_PATH = r"/[A-Za-z0-9._/-]+"
STACKED_BRANCH = r"feature/[0-9]+\.[0-9]+[a-z]?"
# The supervisor commits; pipeline agents never commit, push or merge.
AGENT_GIT_DENY = ("commit", "push", "merge")
# The closed list of gh writes that change the flow, the settings or the secrets, by the call's first positional
# words, with gh's own aliases for them (gh help <command>, ALIASES); the supervisor runs them.
AGENT_GH_DENY = {
    "pr create", "pr new", "pr edit", "pr merge", "pr update-branch", "pr close", "pr reopen", "pr ready",
    "repo edit", "repo delete", "repo rename", "repo archive", "repo unarchive", "repo sync",
    "repo deploy-key add", "repo deploy-key delete",
    "secret set", "secret delete", "secret remove", "variable set", "variable delete", "variable remove",
    "alias set", "alias import",
    "workflow run", "workflow enable", "workflow disable",
}
STRICTNESS = {"ask": 1, "deny": 2}


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


def shell_tokens(command: str, punctuation: str = "();<>|&\n") -> list[str] | None:
    """The command's words and separators, or None when shlex cannot read it (an unbalanced quote)."""
    lexer = shlex.shlex(command, posix=True, punctuation_chars=punctuation)
    lexer.whitespace_split = True
    lexer.whitespace = " \t"  # a newline separates commands; bash reads \r as part of a word
    lexer.commenters = ""  # shlex starts a comment inside a word, bash only at a word's start: comments stay words
    try:
        return list(lexer)
    except ValueError:
        return None


def outside_single_quotes(command: str) -> str:
    """The command without its single-quoted text, which bash reads literally (bash manual, Single Quotes)."""
    return re.sub(r"""(\\.|"(?:[^"\\]|\\.)*")|'[^']*'""", lambda match: match.group(1) or "''", command)


def is_separator(token: str) -> bool:
    """An operator that ends a simple command; a redirection such as 2>&1, &>log or >| is part of it."""
    if re.fullmatch(REDIRECTION, token):
        return False
    is_operator = token != "" and all(character in "();<>|&\n`" for character in token)
    return is_operator and (any(character in "();|\n`" for character in token) or token in ("&", "&&"))


def simple_commands(tokens: list[str]) -> list[list[str]]:
    """The tokens of each simple command, split at the separators."""
    commands, current = [], []
    for token in tokens + ["\n"]:
        if is_separator(token):
            if current:
                commands.append(current)
            current = []
        else:
            current.append(token)
    return commands


def without_redirections(tokens: list[str]) -> list[str]:
    """A simple command's words. A redirection is not one of them (POSIX Shell Command Language 2.7): its operator, its
    target and the file descriptor number before it are dropped."""
    words = []
    rest = iter(tokens)
    for token in rest:
        if not re.fullmatch(REDIRECTION, token):
            words.append(token)
            continue
        next(rest, None)
        if words and re.fullmatch(r"[0-9]+", words[-1]):
            # shortcut: shlex keeps no spacing, so a number just before a redirection is dropped as its file descriptor
            # even when a space separates them (`echo 2 > x`); a shell parser would tell them apart.
            words.pop()
    return words


def shell_reads_standard_input(arguments: list[str]) -> bool:
    """A shell reads its commands from standard input with -s, or when no operand follows its options (POSIX sh;
    bash manual, Invoking Bash). -o and -O take the next word as their value."""
    rest = iter(arguments)
    for word in rest:
        if not word.startswith(("-", "+")):
            return False
        if re.fullmatch(r"-[A-Za-z]*s[A-Za-z]*", word):
            return True
        if word in SHELL_VALUE_OPTIONS or re.fullmatch(r"[-+][A-Za-z]*[oO]", word):
            next(rest, None)
    return True


def sources_standard_input(words: list[str]) -> bool:
    """`.` or `source` of the standard input (bash manual, Bourne Shell Builtins)."""
    index = 0
    while index < len(words) and (re.fullmatch(ASSIGNMENT, words[index]) or words[index] in ("builtin", "command")):
        index += 1
    if index + 1 >= len(words):
        return False
    return words[index] in (".", "source") and words[index + 1] in STANDARD_INPUT_FILES


def reads_standard_input(words: list[str]) -> bool:
    """A shell, `.` or `source` that reads its commands from a pipe, a here-string or a heredoc (bash manual,
    Pipelines, Here Strings): they are not among the command's words."""
    for index, word in enumerate(words):
        if word.rsplit("/", 1)[-1] in SHELLS:
            return shell_reads_standard_input(words[index + 1:])
    return sources_standard_input(words)


def programs(words: list[str]) -> list[tuple[str, list[str]]]:
    """Each word named git or gh, with the words from it on, so `env X=1 git push` and `timeout 5 gh pr merge` are
    read too. Every such word is read as a possible program, since a wrapper's option value can carry the name
    (`env -u gh git push`, `sudo -u git git push`)."""
    found = []
    for index, word in enumerate(words):
        name = word.rsplit("/", 1)[-1]
        if name in ("git", "gh"):
            found.append((name, words[index:]))
    return found


def read_commands(command: str) -> tuple[list[list[str]], bool, bool]:
    """Each simple command's words, whether the command holds a substitution, and whether shlex could read it."""
    bare = outside_single_quotes(command)
    substitution = re.search(SUBSTITUTION, bare) is not None
    readable = not substitution and not re.search(UNREADABLE, command) and not re.search(BRACE_EXPANSION, bare)
    tokens = shell_tokens(command) if readable else None
    readable = tokens is not None
    if not readable:
        # Read roughly instead, so a force push or a hard reset is still denied: quotes and backslashes removed, once
        # with ( ) and backticks ending a command and once with them kept inside words.
        rough = re.sub(r"\$?['\"]|\\", "", command)
        tokens = shell_tokens(rough, "();<>|&\n`") + ["\n"] + shell_tokens(rough, ";<>|&\n")
    commands, texts = [], []
    for command_tokens in simple_commands(tokens):
        words = without_redirections(command_tokens)
        if words:
            commands.append(words)
        # A runner's text is read with its redirections, so a here-string fed to eval is read too.
        runners = [index for index, token in enumerate(command_tokens) if token.rsplit("/", 1)[-1] in SHELL_RUNNERS]
        if runners:
            texts += command_tokens[runners[0] + 1:]
    if any(reads_standard_input(words) for words in commands):
        # What the shell reads is not among its words, so the command fails closed, and every word of the whole
        # command is read again instead: a force push piped or fed into the shell is still seen.
        readable = False
        texts = [token for token in tokens if not is_separator(token)]
    for text in texts:
        if text == command:
            continue  # the whole command is one word, already read
        inner_commands, inner_substitution, inner_readable = read_commands(text)
        commands += inner_commands
        substitution = substitution or inner_substitution
        readable = readable and inner_readable
    return commands, substitution, readable


def git_subcommand(words: list[str]) -> tuple[str, list[str]]:
    """A git command's subcommand and its arguments, after git's own options."""
    rest = iter(words[1:])
    for word in rest:
        if word in GIT_VALUE_OPTIONS:
            next(rest, None)
        elif not word.startswith("-"):
            return word, list(rest)
    return "", []


def is_force_argument(word: str) -> bool:
    if word.startswith("+"):
        return True
    if word.startswith("--"):
        name = word[2:].split("=")[0]
        return name != "" and any(option.startswith(name) for option in FORCE_OPTIONS)
    return word.startswith("-") and "f" in word


def is_force_push(words: list[str]) -> bool:
    for name, git in programs(words):
        subcommand, arguments = git_subcommand(git)
        if name == "git" and subcommand == "push" and any(is_force_argument(word) for word in arguments):
            return True
    return False


def is_stacked_push(command: str) -> bool:
    """The whole command is the stacked form, read before any substitution is taken out."""
    words = shell_tokens(command) or []
    if words[:1] != ["git"]:
        return False
    words = words[1:]
    if words[:1] == ["-C"] and len(words) > 1 and re.fullmatch(STACKED_PATH, words[1]):
        words = words[2:]
    return len(words) == 5 and words[:4] == STACKED_PUSH and re.fullmatch(STACKED_BRANCH, words[4]) is not None


def gh_positional(arguments: list[str]) -> list[str]:
    """The positional words of a gh call (`pr merge 5`), read the way gh's command parser finds a subcommand: an
    option written `--repo o/r` or `-R o/r` takes the next word as its value; `--repo=o/r` or `-Ro/r` does not,
    and every word after `--` is positional."""
    words = []
    rest = iter(arguments)
    for word in rest:
        if word == "--":
            return words + list(rest)
        if not word.startswith("-"):
            words.append(word)
        elif "=" not in word and (word.startswith("--") or len(word) == 2):
            next(rest, None)
    return words


def is_gh_api_write(arguments: list[str]) -> bool:
    """Any method other than GET (-X PUT, -XPOST, -iXPOST, -X=PUT, --method=patch), or with no method a field or
    --input, which makes gh send a POST (gh help api)."""
    methods = []
    for index, word in enumerate(arguments):
        following = arguments[index + 1] if index + 1 < len(arguments) else ""
        if word == "--method":
            methods.append(following)
        elif word.startswith("--method="):
            methods.append(word.partition("=")[2])
        elif re.fullmatch(r"-[A-Za-z]*X.*", word):
            methods.append(word.partition("X")[2].removeprefix("=") or following)
    if methods:
        return any(method.upper() != "GET" for method in methods)
    return any(word.startswith(("--field", "--raw-field", "--input")) or re.fullmatch(r"-[A-Za-z]*[fF].*", word)
               for word in arguments)


def is_agent_gh_write(arguments: list[str]) -> bool:
    words = gh_positional(arguments)
    if " ".join(words[:2]) in AGENT_GH_DENY or " ".join(words[:3]) in AGENT_GH_DENY:
        return True
    return words[:1] == ["api"] and is_gh_api_write(arguments)


def gh_ask_reasons(arguments: list[str]) -> list[str]:
    """A deploy dispatch, or a change to DEPLOY_ENABLED through gh variable or a gh api write."""
    words = gh_positional(arguments)
    text = " ".join(arguments)
    reasons = []
    if words[:2] == ["workflow", "run"] and "deploy" in text.lower():
        reasons.append(DEPLOY_REASON)
    if words[:2] in (["variable", "set"], ["variable", "delete"], ["variable", "remove"]) \
            and re.search(DEPLOY_ENABLED, text):
        reasons.append(DEPLOY_ENABLED_REASON)
    if words[:1] == ["api"] and is_gh_api_write(arguments) and "actions/variables" in text \
            and re.search(DEPLOY_ENABLED, text):
        reasons.append(DEPLOY_ENABLED_REASON)
    return reasons


def bash_decision(command: str, agent: str | None) -> tuple[str, str] | None:
    stacked = is_stacked_push(command)
    command = re.sub(KNOWN_SUBSTITUTION, "known-substitution", command)
    commands, substitution, readable = read_commands(command)
    texts = [command] + [" ".join(words) for words in commands]
    for pattern, reason in BASH_DENY:
        if any(re.search(pattern, text) for text in texts):
            return "deny", reason
    if any(is_force_push(words) for words in commands) and not stacked:
        return "deny", "No force pushes; the one exception is a stacked story's lease-checked push (AGENTS.md, Commits)."
    if agent and substitution:
        return "deny", "Pipeline agents may not run a command substitution; write the value out instead."
    # The name is looked for with quotes and backslashes removed, as bash passes the words on (g"h" reads as gh).
    if not readable and re.search(GIT_OR_GH, re.sub(r"[\\'\"]", "", command)):
        if agent:
            return "deny", "Pipeline agents may not run git or gh in a command the guard cannot read."
        return "ask", "A substitution, unusual quoting or a shell reading its input hides what git or gh will do: confirm."
    calls = [call for words in commands for call in programs(words)]
    gh_calls = [words[1:] for name, words in calls if name == "gh"]
    if agent and any(git_subcommand(words)[0] in AGENT_GIT_DENY for name, words in calls if name == "git"):
        return "deny", "Pipeline agents never commit, push or merge; the supervisor does."
    if agent and any(is_agent_gh_write(arguments) for arguments in gh_calls):
        return "deny", "Pipeline agents never change pull requests, settings, secrets or workflows; the supervisor does."
    ask_reasons = [reason for pattern, reason in BASH_ASK if any(re.search(pattern, text) for text in texts)]
    ask_reasons += [reason for arguments in gh_calls for reason in gh_ask_reasons(arguments)]
    if ask_reasons:
        # Subagents never touch a deployed environment; an "ask" can be
        # auto-resolved in a background run, so only the main session gets the prompt.
        return ("deny" if agent else "ask"), ask_reasons[0]
    return None


def check_bash(command: str, agent: str | None) -> None:
    check_terms(command)
    # Bash removes each backslash-newline pair before it reads words (bash manual, Escape Character). Only an
    # unescaped backslash joins: an odd run of them before the newline.
    joined = re.sub(r"(?<!\\)((?:\\\\)*)\\\n", r"\1", command)
    readings = [joined]
    if re.search(COMMENT_CONTINUATION, command):
        # Whether that backslash joins depends on whether the # starts a comment, so both readings are decided.
        readings.append(re.sub(r"(?<!\\)((?:\\\\)*)\\\n", "\\1\n", command))
    decisions = [decision for decision in (bash_decision(text, agent) for text in readings) if decision]
    if decisions:
        decide(*max(decisions, key=lambda decision: STRICTNESS[decision[0]]))


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
