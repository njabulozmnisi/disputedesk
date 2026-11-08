import { readFileSync } from 'node:fs';
import { join } from 'node:path';

// AGENTS.md, Dependency verification: every package is pinned exactly (npm install --save-exact), so no range
// (^, ~, >=, *, latest) reaches package.json.
const packageJson = JSON.parse(
  readFileSync(join(import.meta.dirname, '..', 'package.json'), 'utf8'),
) as {
  dependencies: Record<string, string>;
  devDependencies: Record<string, string>;
};
const exactVersion = /^\d+\.\d+\.\d+$/;

describe('package.json', () => {
  it('pins every dependency to an exact version', () => {
    const all = { ...packageJson.dependencies, ...packageJson.devDependencies };
    const ranges = Object.entries(all)
      .filter(([, version]) => !exactVersion.test(version))
      .map(([name, version]) => `${name}@${version}`);

    expect(ranges).toEqual([]);
  });

  it('lists lucide-react once, as a runtime dependency', () => {
    expect(packageJson.dependencies['lucide-react']).toMatch(exactVersion);
    expect(packageJson.devDependencies['lucide-react']).toBeUndefined();
  });
});
