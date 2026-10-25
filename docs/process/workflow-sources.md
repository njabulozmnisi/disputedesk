# Workflow sources

The agent-assisted workflow borrows from four open-source approaches and keeps only the pieces that suited a small, time-boxed project. None is installed as a plugin. BMAD's skills are a pinned dependency: `skills-lock.json` records the commit and `scripts/bmad-install.sh` restores them, so their code is not committed here. The rest are written down as practices.

| Source | Taken | Left out | Why |
|---|---|---|---|
| [BMAD Method](https://github.com/bmad-code-org/BMAD-METHOD) (v7 preview, pinned to `61e01f8`) | Product brief, PRD with stable requirement IDs, UX, the architecture spine, Sofia's slicing into epics and stories with the tracer-bullet rule, the review lenses, domain research, elicitation methods; the built-versus-done rule and a headless status contract | The persona agents (analyst, PM, UX designer, architect), its build, code-review and QA loops, brainstorming, PRFAQ, party mode, and the tooling skills; Sofia's per-epic refactor sweep and story-count norm | The persona agents only route to the same skills. Building, verification and review run through our own agents. Ideation tools that widen scope fight a fixed brief. |
| [Matt Pocock's skills](https://github.com/mattpocock/skills) (MIT) | Grilling in batched rounds with a recommended answer per question, the glossary kept current while grilling, the three-part bar for writing a decision record | `to-spec`, `to-tickets`, `wayfinder` and the plugin itself | Sofia already covers specs and tickets; the plugin assumes an issue tracker. |
| [Addy Osmani's agent skills](https://github.com/addyosmani/agent-skills) | A definition of done, safety rails, and the floor guard against new suppressions | The rest of the catalogue | Only the quality gates applied to this stack. |
| pstack (Poteto) | A non-author verifier from another model family, verdicts keyed by commit SHA, one writer per file | Its Cursor-specific primitives | The verification discipline transfers; the harness does not. |

## Original to DisputeDesk

- **A data-integrity and data-movement QA stage.** QA traces every acceptance criterion through four hops (browser, network, database through a read-only role, events on the topic) with concrete expected values, and passing walkthroughs become committed Playwright tests.
- **Developer smoke checks.** A builder proves its story works over real HTTP against a real identity provider before reporting it built.
- **Area-based builders with model roles chosen per area,** and hooks that turn the critical rules into hard checks.

## Three layers of reuse

- Running the project needs only Docker.
- Working on it needs only the .NET SDK, Node and any AI assistant (`AGENTS.md` is tool-neutral).
- The multi-agent orchestration is optional and documented in [agentic-sdlc.md](agentic-sdlc.md).
