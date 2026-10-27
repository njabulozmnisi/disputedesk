# Frontend conventions

React with TypeScript, shadcn/ui on Tailwind, TanStack Query, TanStack Router.

- **Generated client only.** Hooks come from Orval, generated from the committed `openapi.json`. No hand-written `fetch`.
- **The browser never sees a token.** Calls go to same-origin `/api` with the session cookie; unsafe methods send the antiforgery header. The Orval mutator does both.
- **TanStack Query patterns.** Query keys come from the generated hooks; mutations invalidate the queries they change; server state never lives in component state.
- **Routes with TanStack Router.** Every route, including the not-found route, is defined with TanStack Router (architecture AD-31).
- **Forms with shadcn/ui's Form component.** Forms use the documented shadcn/ui Form component, built on React Hook Form with a zod schema. Each package is verified and pinned exactly when first added (AGENTS.md, Dependency verification).
- **Theme tokens only.** Colour, type, spacing and radius come from the one theme file. No raw colours or pixel values in components. The brand is swappable in that file.
- **WCAG 2.2 AA** (NFR-CODE-6), largely through Radix (via shadcn/ui): keyboard access, visible focus, labelled form errors, reflow at 320px, contrast as computed in DESIGN.md, one restrained accent. The axe scan in the Playwright E2E tests checks it.
- **Plain-language copy.** Short sentences, no jargon, status visible at a glance. Mobile first.
- **Tests.** Vitest against Orval-generated mocks, `tsc --noEmit`, lint. Feature folders: `features/{linking,transactions,disputes}`.
- **Strict TypeScript.** `tsconfig` has `strict: true`; no `any` (lint rule).
