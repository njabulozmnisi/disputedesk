# Epic 8 pre-build review: 8.6 to 8.9 and the rest of 8.3 (2026-11-04)

Base `0596586`. Read-only reviewer; recorded by the supervisor. Every point is `[story]`.

## 8.6, verdict changes
1. High. `playwright/no-wait-for-timeout` flags only a receiver named `page`; `second.waitForTimeout` (`dispute-raise.spec.ts:372,376`) passes (11 of 13 flagged in a scratch run). Add ESLint core `no-restricted-properties` `{ property: 'waitForTimeout' }` and a Supervisor check on a non-`page` variable.
2. High. `flat/recommended` adds 11 errors on today's tree (7 `prefer-web-first-assertions`: `dispute-detail.spec.ts:536`, `dispute-raise.spec.ts:193,313,680`, `linking-screens.spec.ts:285,336,380`; 2 `no-unsafe-references`: `dispute-raise.spec.ts:712`, `linking-screens.spec.ts:180`; 2 `no-networkidle`: `outbox-relay-failure-paths.spec.ts:240`, `session-cookie-never-renewed.spec.ts:103`). Enable only the wait rules, or list these fixes in scope.
3. High. Criterion 3: `expect.poll(() => count).toBe(0)` passes on its immediate first poll, so no window is held. Wait for a later positive signal then assert 0, or `await expect(page.waitForRequest(matcher, { timeout })).rejects.toThrow()`.
4. Medium. Criterion 1: five identical `signIn` copies are not listed (`outbox-relay.spec.ts:18`, `outbox-relay-failure-paths.spec.ts:24`, `seeded-disputes.spec.ts:47`, `dispute-detail.spec.ts:16`, `spa-disputes-history.spec.ts:46`); `forwarded-headers.spec.ts:24` and `linking-screens.spec.ts:85` are variants; `linking-rate-limits.spec.ts` has only `signInAs` (`:94`); variants that stay: `sign-in-and-out-audit.spec.ts:33`, `spa-transactions.spec.ts:46`, `session-cookie-never-renewed.spec.ts:35`. List which go and which stay, with reasons.
5. Medium. Criterion 6 names no transaction for the new Resolved dispute (a free one from `dispute-withdraw`'s reserved list, or a new entry plus `docs/verification/e2e-data-budget.md`), and no Collision line for 8.10 to 8.14, which edit the same specs.
6. Low. `setTimeout` sleeps (`linking-rate-limits.spec.ts:355`, `sla-sweep.spec.ts:65,198,200`, `forwarded-headers.spec.ts:58`): scope criterion 3 to `waitForTimeout` with reason comments, or include them. `journeys/uj-1.spec.ts:20` has a `rows` copy but the journeys are frozen: say which wins. `eslint` 10.14.0 versus `frontend/`'s 10.13.0: align or say why. Nits: `axeTags` at `journeys/screens.ts:5`; 20 `origin` copies; `sign-in-and-out-audit.spec.ts:12` hard-codes the origin.
Package checks hold: eslint-plugin-playwright 2.12.2, eslint 10.14.0, typescript-eslint 8.72.1 (typescript `>=4.8.4 <6.1.0`, so 6.0.3), node 24.22.1.

## 8.7, verdict changes
1. Medium. Criterion 5 would make backend ArchitectureTests shell out to `docker compose` in the `backend` job, which has the runner's unpinned Compose; move the four `Compose_*` cases into `infra/tests/DisputeDesk.PlatformTests` (the `platform` job pins 5.6.1).
2. Medium. Criterion 5's render is not isolated: `docker compose config` reads a gitignored `.env` and the shell. Use `--env-file` to an empty file and a controlled `ProcessStartInfo.Environment`.
3. Medium. Criterion 3: `smoke.sh:100-130`'s Scalar checks (`servers[0].url`, no `<base>`, assets 200, each header once) need the real Api; keep them in `smoke.sh` or point at `e2e/tests/scalar-reference.spec.ts`; say what the echo returns for `/scalar/` (pitfall 7.12).
4. Medium. The `/otlp/` `Cookie` and `Authorization` case (8.9's security point) must land in 8.7's `GatewayTests`, local gateway, with an OTLP stand-in upstream, because 8.9 changes no test.
5. Low. Also list `Compose_WorkerHealthCheck_...` (`PlatformScriptTests.cs:143`) and the `ForwardedHeaders__AllowedHosts` text check (`:81`); leave `ProvisionScript_ClientsFile_MapsTheMachineUserIdToTheOperationsActor` (`infra/tests/.../PlatformScriptTests.cs:41`) and `..._CurrentCheck_AlsoRequiresTheActorMapping` (`:49`) to 8.12; state the order with 8.5 (`ApiProgram_ForwardedHeaders_...`); `smoke.sh` runs `down -v` on the shared project: name the `stack` lock.

## 8.8, verdict changes
1. Medium. Criterion 1's stub helper also passes `docker run` through to the real Docker with `--network` added (`rds-init.test.sh:229-231`) and fails an unexpected call with exit 97 naming it.
2. Medium. Criterion 6: the render checks assert only chosen environment keys (`check-compose.sh:205-242`), so a dropped `environment: !override` (for example `worker`, `infra/aws/compose.yml:173`) passes without the awk (`:168-175`). Assert each overridden service's rendered `environment` key set exactly; add a Supervisor check for a dropped tag.
3. Low. Criterion 5: both sides of every range (each edge accepted, a value just past each refused), as `check-compose.sh:70-130` does. Criterion 7's exception for `iam-names.test.sh` is removed by 8.16 (add to its Touches). Say the render is authoritative in CI (Compose 5.6.1) and locally only on the same version.

## 8.9, verdict approve with lows
1. Low. The AWS Mailpit block (`templates-aws/nginx.conf.template:281-292`) keeps its own set (no `X-Forwarded-Host`).
2. Low. Say whether `/otlp/` keeps its own `proxy_http_version 1.1`.
3. Low. Criterion 3's `/otlp/` proof depends on 8.7 point 4.

## 8.3 (the rest), verdict changes
1. Medium. Criterion 3: zod reads `globalConfig.jitless` when a schema is constructed (`zod/v4/core/schemas.js:1194`), so `z.config` in `main.tsx`'s body runs too late. Use a side-effect module (for example `src/zodConfig.ts`) imported first in `main.tsx` and in the Vitest setup; say "before any schema is constructed"; cite the zod docs page.
2. Low. Criterion 1: a route's `pendingComponent` shows only after `pendingMs` (default 1000 ms); name `pendingMs: 0` or accept the delay.
3. Low. With `beforeLoad`, a session change with no navigation no longer redirects (a focus refetch answering `linked` on `/link`); say so in Boundaries or name `router.invalidate()` on a session-state change.
4. Low. Criterion 5 names the client tests it deletes or inverts (`routing.test.tsx:202-247`).
5. Nits. `field.tsx` exports at `:63`; `Footer` keeps its `wide` prop (`RootLayout.tsx:18,24,92`).
