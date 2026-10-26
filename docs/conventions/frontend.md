# Frontend conventions

React with TypeScript, shadcn/ui on Tailwind, TanStack Query.

- **Generated client only.** Hooks come from Orval, generated from the committed `openapi.json`. No hand-written `fetch`.
- **The browser never sees a token.** Calls go to same-origin `/api` with the session cookie; unsafe methods send the antiforgery header. The Orval mutator does both.
- **TanStack Query patterns.** Query keys come from the generated hooks; mutations invalidate the queries they change; server state never lives in component state.
- **Theme tokens only.** Colour, type, spacing and radius come from the one theme file. No raw colours or pixel values in components. The brand is swappable in that file.
- **Accessibility via Radix** (through shadcn/ui): keyboard navigation, focus management, labels. Strong contrast with one restrained accent.
- **Plain-language copy.** Short sentences, no jargon, status visible at a glance. Mobile first.
- **Tests.** Vitest against Orval-generated mocks, `tsc --noEmit`, lint. Feature folders: `features/{linking,transactions,disputes}`.
- **Strict TypeScript.** `tsconfig` has `strict: true`; no `any` (lint rule).
