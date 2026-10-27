# ad-004: Model roles per agent

**Status:** accepted

**Decision.** Each pipeline agent runs a model chosen for its area, set in its own agent file:

| Agent | Model | Why |
|---|---|---|
| `backend-builder` | Claude Opus 5.5 | Backend carries the domain, data integrity and security; the strongest model where mistakes are costliest |
| `frontend-builder` | Claude Sonnet 5.5 | Generated client and design tokens keep the frontend well-constrained; faster and cheaper |
| `verifier` | GPT-6 Astra via GitHub Copilot CLI (GPT-6 Sol as backup), driven by a Sonnet agent | A non-author from another model family catches what the author's family is blind to |
| `qa` | Claude Sonnet 5.5 with Playwright MCP | Procedural, evidence-heavy work |
| `reviewer`, `security` | Claude Opus 5.5, read-only | Judgement-heavy; security is the primary quality requirement |

**Options rejected.**
- *One model everywhere:* simplest, but an author verifying its own family's output shares its blind spots, and Opus everywhere strains usage.
- *Codex as verifier:* its sandbox could not commit inside a worktree and it stalled the test host.
- *Copilot with MCP:* the organisation's policy blocks MCP in Copilot, so it reads Microsoft Learn through `npx @microsoft/learn-cli` instead.

**Consequences.** Switching a model is a one-line change in one file. Opus is slow and costly; if usage strains, the backend stays on Opus and the reviewer drops to Sonnet. Claude Code subagents can only run Claude models, so the GPT verifier is an external CLI called from a Claude agent.
