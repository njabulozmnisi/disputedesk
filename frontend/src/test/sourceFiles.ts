import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const sourceRoot = join(import.meta.dirname, '..');

/** The SPA's own source: no tests, no generated client, no vendored fonts. Used by the rules that scan the code. */
export function appSourceFiles(extensions: string[]): { path: string; text: string }[] {
  return readdirSync(sourceRoot, { recursive: true, encoding: 'utf8' })
    .filter((path) => extensions.some((extension) => path.endsWith(extension)))
    .filter(
      (path) =>
        !path.includes('.test.') && !path.startsWith('test') && !path.startsWith('api/generated'),
    )
    .map((path) => ({ path, text: readFileSync(join(sourceRoot, path), 'utf8') }));
}
