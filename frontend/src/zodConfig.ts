import { z } from 'zod';

// zod 4 compiles object schemas with `new Function` unless told not to (zod 4.6.7, core/schemas.js and core/util.js
// `allowsEval`). The gateway's CSP has no 'unsafe-eval' (story 2.5), so zod parses without it. zod reads the setting
// when a schema is built (core/schemas.js:1194), so this module is imported first in `main.tsx` and in the Vitest
// setup file, before any schema is constructed. `z.config` is zod's global configuration (https://zod.dev/).
z.config({ jitless: true });
