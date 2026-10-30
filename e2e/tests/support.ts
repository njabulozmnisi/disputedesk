import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const fixturePath = fileURLToPath(new URL('../../fixtures/local.json', import.meta.url));

export const sessionCookieName = 'disputedesk-session';
export const antiforgeryCookieName = 'disputedesk-antiforgery';
export const problemTypeBase = 'https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/';

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

/** Every problem body carries the resolvable page-URL type and a traceId, and no exception text. */
export function expectProblemShape(body: Record<string, unknown>, slug: string, status: number): string[] {
  const problems: string[] = [];
  if (body.type !== `${problemTypeBase}${slug}.md`) problems.push(`type was ${String(body.type)}`);
  if (body.status !== status) problems.push(`status was ${String(body.status)}`);
  if (typeof body.traceId !== 'string' || body.traceId.length === 0) problems.push('traceId missing');
  if (JSON.stringify(body).match(/Exception|StackTrace|\bat [A-Z]\w+\.\w+\(/)) problems.push('exception text leaked');
  return problems;
}

/** The CSP the gateway sends with the SPA (infra/nginx). `vite preview` sends none, so SPA tests add it to the document. */
export const gatewayContentSecurityPolicy =
  "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' http://auth.localhost:8080";

/** The records on the dispute topic, read with the broker's own consumer (rpk in the redpanda container) from the start to the current end. */
export type TopicRecord = { key: string; value: string; headers: { key: string; value: string }[]; partition: number; offset: number };
export function topicRecords(): TopicRecord[] {
  const output = execFileSync(
    'docker',
    ['exec', 'disputedesk-redpanda-1', 'rpk', 'topic', 'consume', 'disputedesk.disputes', '-X', 'brokers=redpanda:9092', '-o', ':end', '-f', 'json'],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 },
  );
  // rpk prints one pretty-printed JSON object per record; each ends with a line that is only "}".
  return output.split(/^}$/m).map((chunk) => chunk.trim()).filter((chunk) => chunk.length > 0).map((chunk) => JSON.parse(`${chunk}}`) as TopicRecord);
}
