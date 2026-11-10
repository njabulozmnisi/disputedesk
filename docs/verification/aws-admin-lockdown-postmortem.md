# Postmortem: aws-admin-lockdown's second fix round (2026-11-10)

Trigger: a second fix round (AGENTS.md, Continuous improvement). Written late: the retrospective input for
`lows-2026-11-10` found it missing.

| Round | Cause | Stage that caught it | Stage that should have | Category |
|---|---|---|---|---|
| 1 | Public organization registration was open on AWS: an anonymous visitor becomes an `INITIAL` org owner, which `DeactivateUser` refuses, so every deploy would fail; the step also failed open on an unexpected response shape and read only the first page | security (round 0) | the builder (the first build's brief named only the human admin), and the AWS environment's earlier security review | auth, input boundary |
| 2 | A merge conflict with #41, which changed `provision.sh` and `PlatformScriptTests.cs` while round 1 was in review; no behaviour change | the supervisor at push time | the supervisor's dispatch collision check | environment or tooling |

## Pattern

1. Round 1: the brief asked for "no human with admin rights can be taken over through a reset email" and named one
   path (the first-instance admin). It did not ask the builder to list every way an anonymous visitor can become an
   administrator, so the builder closed the one named path. The security agent, asked the wider question, found the
   second path at once.
2. Round 2: two changes to the same script were in flight from two sessions. The collision check (AGENTS.md,
   Dispatch rules) runs within this session's lanes; it does not see another session's open work.

## Proposed countermeasures (for the retrospective, need a human decision)

- Brief template line for an auth or identity fix: "List every path by which an anonymous or low-privilege user
  reaches the protected capability, and close or test each one", instead of naming the one path seen.
- Before dispatching a builder, list the other sessions' open pull requests and unmerged branches that touch the
  same files (`gh pr list --json files`), not only this session's lanes.
