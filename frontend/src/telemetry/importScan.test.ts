import { appSourceFiles } from '@/test/sourceFiles';

// Criterion 1: the only browser-instrumentation entry point is the fetch one. The others record page URLs (the
// transaction ID), element text, console output or errors.
describe('the telemetry imports', () => {
  it('import only the fetch subpath of @opentelemetry/browser-instrumentation', () => {
    const imports = appSourceFiles(['.ts', '.tsx']).flatMap(({ path, text }) =>
      [...text.matchAll(/from '(@opentelemetry\/browser-instrumentation[^']*)'/g)].map((match) => ({
        path,
        module: match[1],
      })),
    );

    expect(imports.length).toBeGreaterThan(0);
    for (const { module } of imports) {
      expect(module).toBe('@opentelemetry/browser-instrumentation/experimental/fetch');
    }
  });
});
