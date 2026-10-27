import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const fixturePath = fileURLToPath(new URL('../../fixtures/local.json', import.meta.url));

export const sessionCookieName = 'disputedesk-session';
export const antiforgeryCookieName = 'disputedesk-antiforgery';
export const problemTypePrefix = 'tag:disputedesk,2026:problem:';

export function fixtureLogin(key: string): { username: string; password: string } {
  const fixtures = JSON.parse(readFileSync(fixturePath, 'utf8'));
  const login = fixtures.logins.find((entry: { key: string }) => entry.key === key);
  return { username: login.username, password: login.password };
}

/** Runs one query as the read-only QA role (infra/db). The role cannot write, so this is evidence only. */
export function queryDatabase(sql: string): string {
  return execFileSync(
    'docker',
    ['exec', '-e', `PGPASSWORD=${process.env.QA_DB_PASSWORD ?? 'dev-only-qa-db'}`, 'disputedesk-postgres-1',
      'psql', '-h', 'localhost', '-U', 'qa_readonly', '-d', 'disputedesk', '-At', '-c', sql],
    { encoding: 'utf8' },
  ).trim();
}

/** Every problem body carries the tag-URI type and a traceId, and no exception text. */
export function expectProblemShape(body: Record<string, unknown>, slug: string, status: number): string[] {
  const problems: string[] = [];
  if (body.type !== `${problemTypePrefix}${slug}`) problems.push(`type was ${String(body.type)}`);
  if (body.status !== status) problems.push(`status was ${String(body.status)}`);
  if (typeof body.traceId !== 'string' || body.traceId.length === 0) problems.push('traceId missing');
  if (JSON.stringify(body).match(/Exception|StackTrace|\bat [A-Z]\w+\.\w+\(/)) problems.push('exception text leaked');
  return problems;
}
