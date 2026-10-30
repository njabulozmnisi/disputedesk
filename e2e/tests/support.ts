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

/**
 * The e2e data budget. A transaction can be disputed once and the data is not reset between specs, so each spec that
 * raises a dispute takes its transactions from its own reserved list here, never "the first disputable row". Lists never
 * overlap and never hold a transaction that fixtures/local.json disputes at seed time (story 5.4: seven on ACC-A-1, one
 * on ACC-B-1). A spec raises on the first still-disputable transaction of its list, in list order.
 * TXN-A-0031 to TXN-A-0048 are the card debits the fixture
 * must add to ACC-A-1 (see docs/verification/e2e-data-budget.md).
 */
const transactionIds = (customer: 'A' | 'B', numbers: number[]) => numbers.map((number) => `TXN-${customer}-${String(number).padStart(4, '0')}`);
const seededDisputes = [...transactionIds('A', [11, 14, 15, 18, 19, 22, 26]), ...transactionIds('B', [25])];

export const reservedTransactions = {
  // Customer A. The raise screens are clicked in the SPA, so their five are among the newest 20 rows (the first page).
  'dispute-raise': { a: transactionIds('A', [29, 28, 27, 13, 12]) },
  'dispute-withdraw': { a: transactionIds('A', [3, 4, 5, 6, 7, 46, 47, 48]), b: transactionIds('B', [11]) },
  'dispute-stored-events': { a: transactionIds('A', [8, 9]), b: transactionIds('B', [26]) },
  // B 2, 12, 21 and 23 are the story 3.12 detail-screen tests' (the first of 2, 21, 23 is withdrawn for real; 12 stays open).
  'dispute-detail': { a: transactionIds('A', [31]), b: transactionIds('B', [13, 2, 12, 21, 23]) },
  'dispute-list': { a: transactionIds('A', [32, 33, 34]), b: transactionIds('B', [14]) },
  'operations-read': { a: transactionIds('A', [35]), b: transactionIds('B', [17]) },
  'operations-actions': { b: transactionIds('B', [4, 5, 6, 7, 8, 9, 10]) },
  'outbox-relay-failure-paths': { a: transactionIds('A', [36, 37, 38, 39]), b: transactionIds('B', [18, 19]) },
  'outbox-relay': { a: transactionIds('A', [40, 41]) },
  'scalar-reference': { a: transactionIds('A', [42]) },
  'spa-dispute-status-badges': { b: transactionIds('B', [27]) },
  // Raises only when run alone (it tops customer A up to three disputes). TXN-A-0010 is also what transactions.spec.ts
  // and spa-transactions.spec.ts find disputable at the end of a full run, which raises nothing there.
  'spa-disputes-history': { a: transactionIds('A', [10, 43, 44, 45]) },
};

const everyReserved = Object.values(reservedTransactions).flatMap((lists) => Object.values(lists).flat());
const problems = [
  ...everyReserved.filter((id, index) => everyReserved.indexOf(id) !== index).map((id) => `${id} is reserved twice`),
  ...everyReserved.filter((id) => seededDisputes.includes(id)).map((id) => `${id} is disputed by the fixture`),
];
if (problems.length > 0) throw new Error(`e2e data budget: ${problems.join('; ')}`);

type DisputableRow = { transactionId: string; disputable: boolean };

/** The spec's reserved transactions that are still disputable, in reserved order (the list a spec raises from). */
export function ownDisputable<T extends DisputableRow>(rows: T[], spec: keyof typeof reservedTransactions, customer: string): T[] {
  const lists: Record<string, string[] | undefined> = reservedTransactions[spec];
  const ids = lists[customer] ?? [];
  return ids.flatMap((id) => rows.filter((row) => row.transactionId === id && row.disputable));
}
