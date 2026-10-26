# Review: P0-5

- Reviewer: `reviewer` agent (Claude Opus 5.5), read-only, two passes. Verdict: approve (low and info findings only); follow-ups applied.
- Verifier (GPT-6 Astra): AC1-AC7 proved (build 0 warnings, 74 tests, format style and whitespace, OpenAPI unchanged, floor guard, actionlint, Design excluded from publish). AC8 recorded as FAIL only because the read-only verifier cannot reproduce deliberate breaks.

## Supervisor and reviewer evidence for AC8 (deliberate breaks, throwaway copies)

| Probe | Result |
|---|---|
| `public interface DisputeReader { int count(); }` in Application | build failed: `IDE1006` missing prefix `I`; `IDE1006` `count` must begin upper case |
| `public sealed class Shared` outside Domain | build failed: `CA1716` |
| Temporary unmigrated entity | `dotnet ef migrations has-pending-model-changes` exit 1 (reviewer) |
| `var` probe | `dotnet format style --verify-no-changes` (CI step) reported IDE0007/IDE0008; the build ignores these IDE-only rules |

## Review findings

| # | Severity | Finding | Resolution |
|---|---|---|---|
| 1 | Low | Conventions doc said private statics are `_camelCase`; the config makes static readonly fields PascalCase | Doc corrected |
| 2 | Low | `var` rules were suggestion-only | IDE0007/IDE0008 at warning, enforced by the CI code-style step |
| 3 | Low | `ResultTests.cs` held two top-level classes | `ErrorTests.cs` split out; AD-3 set check made order-insensitive |
| 4 | Info | Status assertion repeated in a pure test and the end-to-end theory | Kept (the fast test pinpoints the cause) |
| 5 | Info | Whitespace step lacked `--no-restore` | Added |
