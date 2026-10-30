import { execFileSync } from 'node:child_process';
import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { fixtureLogin, queryDatabase } from './support';

// Story 6.6: forwarded headers are trusted only from the Compose network, and the identity origin never logs
// the end-session id_token_hint. Four hops: browser (Zitadel), network (gateway and a direct hop to the Api),
// database (qa_readonly) and the gateway's access log. Serial, one browser; assertions compare, they do not echo
// cookies, tokens or the id_token_hint.
test.describe.configure({ mode: 'serial' });

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const project = process.env.COMPOSE_PROJECT_NAME ?? 'disputedesk';
const forgedAddress = '203.0.113.9';
const forgedHeaders = {
  'X-Forwarded-For': `${forgedAddress}, 198.51.100.4`,
  'X-Forwarded-Host': 'evil.example',
  'X-Forwarded-Proto': 'https',
  Forwarded: `for=${forgedAddress};host=evil.example;proto=https`,
};
const unmatchedDetails = JSON.stringify({ idNumber: '0000000000000', accountNumber: '1000000049' });
const sql = (statement: string) => queryDatabase(statement);
const docker = (...args: string[]) => execFileSync('docker', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }).trim();

async function signIn(browser: Browser, loginKey: string, extraHeaders: Record<string, string> = {}): Promise<BrowserContext> {
  const login = fixtureLogin(loginKey);
  const context = await browser.newContext();
  // Playwright keeps these headers on the redirect to the identity origin, which passes a client's RFC 7239 Forwarded
  // header on to Zitadel and would reject the sign-in itself (QA record, observation O1), so the browser flow forges
  // the three X-Forwarded-* headers only.
  if (Object.keys(extraHeaders).length > 0) {
    await context.route(`${origin}/**`, (route) => route.continue({ headers: { ...route.request().headers(), ...extraHeaders } }));
  }
  const page = await context.newPage();
  await page.goto('/api/v1/bff/login');
  await page.getByTestId('username-text-input').fill(login.username);
  await page.getByTestId('username-text-input').press('Enter');
  await page.getByTestId('password-text-input').fill(login.password, { timeout: 60_000 });
  await page.getByTestId('password-text-input').press('Enter');
  await page.waitForURL((url) => url.origin === origin, { timeout: 60_000 });
  await page.close();
  return context;
}

async function antiforgeryToken(context: BrowserContext): Promise<string> {
  return (await (await context.request.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
}

/** The newest audit row for the action after the mark, as `host(ClientIp)|Result`. */
const latestAudit = (action: string, mark: string) =>
  sql(`select concat_ws('|', host("ClientIp"), "Result") from "AuditLogEntries" where "Action" = '${action}' and "OccurredAtUtc" > '${mark}' order by "OccurredAtUtc" desc limit 1`);
const auditMark = () => sql(`select coalesce(max("OccurredAtUtc")::text, '1970-01-01') from "AuditLogEntries"`);

async function untilAudit(action: string, mark: string): Promise<string> {
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    const row = latestAudit(action, mark);
    if (row) return row;
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`no ${action} audit row after ${mark}`);
}

function gatewayLog(): string {
  return execFileSync('sh', ['-c', `docker logs ${project}-gateway-1 2>&1`], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
}

/** The connection address in the gateway's own log line of the newest linking start: what the gateway saw as the client. */
function addressGatewaySawForLastStart(): string {
  const lines = gatewayLog().split('\n').filter((line) => line.includes('"POST /api/v1/linking/start '));
  return lines[lines.length - 1].split(' ')[0];
}

let unlinked: BrowserContext;

test.beforeAll(async ({ browser }) => {
  unlinked = await signIn(browser, 'customer-c');
});

test.afterAll(async () => {
  await unlinked?.close();
});

test('criterion 1: forged X-Forwarded-* from the browser and from curl through the gateway never reach the audited ClientIp', async () => {
  const token = await antiforgeryToken(unlinked);
  const post = (headers: Record<string, string>) =>
    unlinked.request.post('/api/v1/linking/start', {
      data: unmatchedDetails, maxRedirects: 0,
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token, ...headers },
    });

  const plainMark = auditMark();
  expect((await post({})).status()).toBe(202);
  const plain = (await untilAudit('LinkingStart', plainMark)).split('|')[0];
  expect(plain).toBe(addressGatewaySawForLastStart());
  expect(plain).not.toBe(forgedAddress);

  const forgedMark = auditMark();
  expect((await post(forgedHeaders)).status()).toBe(202);
  const forged = (await untilAudit('LinkingStart', forgedMark)).split('|')[0];
  expect(forged).toBe(addressGatewaySawForLastStart());
  expect(forged).toBe(plain);
  expect(forged).not.toBe(forgedAddress);
  expect(sql(`select count(*) from "AuditLogEntries" where "OccurredAtUtc" > '${forgedMark}' and (host("ClientIp") = '${forgedAddress}' or host("ClientIp") = '198.51.100.4')`)).toBe('0');
  // Not the gateway's own container address either.
  const gatewayAddresses = docker('inspect', '-f', '{{range .NetworkSettings.Networks}}{{.IPAddress}} {{end}}', `${project}-gateway-1`).split(' ');
  expect(gatewayAddresses).not.toContain(forged);
});

test('criterion 1: forged host and scheme never reach the OIDC redirect_uri; sign-in and sign-out end on the right URLs', async ({ browser, request }) => {
  const redirectUriOf = async (headers: Record<string, string>) => {
    const response = await request.get('/api/v1/bff/login', { headers, maxRedirects: 0 });
    expect(response.status()).toBe(302);
    const location = response.headers().location;
    expect(location).not.toContain('evil.example');
    return new URL(location).searchParams.get('redirect_uri');
  };
  const plainRedirect = await redirectUriOf({});
  expect(plainRedirect).toBe(`${origin}/api/signin-oidc`);
  expect(await redirectUriOf(forgedHeaders)).toBe(plainRedirect);

  // A browser that sends the forged headers on every request, including the sign-in and sign-out ones.
  const { Forwarded: _forwarded, ...forgedXForwarded } = forgedHeaders;
  const context = await signIn(browser, 'customer-a', forgedXForwarded);
  try {
    const page: Page = await context.newPage();
    await page.goto('/');
    expect(page.url()).toBe(`${origin}/`);
    expect(await (await page.request.get('/api/v1/bff/session')).json()).toEqual({ state: 'linked' });

    const { requestToken } = await (await page.request.get('/api/v1/bff/antiforgery')).json();
    const navigations: string[] = [];
    page.on('request', (navigation) => navigation.isNavigationRequest() && navigations.push(navigation.url()));
    await page.evaluate((antiforgery) => {
      const form = document.createElement('form');
      form.method = 'POST';
      form.action = '/api/v1/bff/logout';
      form.innerHTML = `<input type="hidden" name="__RequestVerificationToken" value="${antiforgery}">`;
      document.body.appendChild(form);
      form.submit();
    }, requestToken);
    await expect.poll(() => {
      const endSession = navigations.findIndex((url) => url.includes('/oidc/v1/end_session'));
      return endSession >= 0 && navigations.slice(endSession + 1).includes(`${origin}/`);
    }, { timeout: 60_000 }).toBe(true);
    await page.waitForLoadState('load');
    expect(page.url()).toBe(`${origin}/`);

    const endSession = new URL(navigations.find((url) => url.includes('/oidc/v1/end_session'))!);
    expect(endSession.searchParams.get('post_logout_redirect_uri')).toBe(`${origin}/api/signout-callback-oidc`);
    expect(endSession.searchParams.get('id_token_hint')).toMatch(/^eyJ/);
    expect(await (await page.request.get('/api/v1/bff/session')).json()).toEqual({ state: 'anonymous' });
  } finally {
    await context.close();
  }
});

test('criterion 2: the identity origin logs the end_session path with no query, no id_token_hint and no Referer, while sign-out works', async () => {
  const lines = gatewayLog().split('\n');
  const endSessionLines = lines.filter((line) => line.includes('/oidc/v1/end_session'));
  expect(endSessionLines.length).toBeGreaterThan(0);
  for (const line of endSessionLines) {
    expect(line).toMatch(/"GET \/oidc\/v1\/end_session HTTP\/1\.1" \d{3} \d+ "[^"]*"$/);
    expect(line).not.toContain('?');
    expect(line).not.toContain('id_token_hint');
  }
  // No id_token_hint on any line, and no token-shaped value on any line but the app origin's sign-in callback, whose
  // query (an encrypted one-time authorization code and the state) the app origin's combined format still logs
  // (QA record, observation O2); that line is not the identity origin's and is out of this story's criterion.
  expect(lines.filter((line) => /eyJ[A-Za-z0-9_-]{10,}/.test(line) && !line.includes('"GET /api/signin-oidc?'))).toEqual([]);
  expect(lines.filter((line) => line.includes('id_token_hint'))).toEqual([]);
});

test('container health stays green', async () => {
  const states = docker('ps', '-a', '--filter', `name=${project}-`, '--format', '{{.Names}} {{.Status}}').split('\n');
  expect(states.length).toBeGreaterThan(5);
  expect(states.filter((state) => /unhealthy|health: starting|Restarting/.test(state))).toEqual([]);
  const api = await unlinked.request.get('/api/health/ready', { maxRedirects: 0 });
  expect(api.status()).toBe(200);
});

test('criterion 1: a connection from outside the trusted network keeps its own address; one from inside takes the forwarded one', async () => {
  const outside = 'qa66-outside';
  const start = async (network: string, apiAddress: string) => {
    const cookies = (await unlinked.cookies(origin)).map((cookie) => `${cookie.name}=${cookie.value}`).join('; ');
    const mark = auditMark();
    // wget in a throwaway container on the given network posts straight to the Api, with the browser's cookies (passed
    // through the environment, never printed) and a forged X-Forwarded-For.
    const output = execFileSync('docker', ['run', '--rm', '--network', network, '-e', 'COOKIES', '-e', 'TOKEN', 'alpine:latest', 'sh', '-c',
      `wget -S -O /dev/null --header "Cookie: $COOKIES" --header "X-XSRF-TOKEN: $TOKEN" --header 'Content-Type: application/json' ` +
      `--header 'X-Forwarded-For: ${forgedAddress}' --post-data '${unmatchedDetails}' http://${apiAddress}:8080/api/v1/linking/start 2>&1 | grep -m1 HTTP/`],
      { encoding: 'utf8', env: { ...process.env, COOKIES: cookies, TOKEN: await antiforgeryToken(unlinked) } });
    expect(output).toContain('202');
    return (await untilAudit('LinkingStart', mark)).split('|')[0];
  };

  docker('network', 'create', '--subnet', '10.201.91.0/24', outside);
  try {
    docker('network', 'connect', outside, `${project}-api-1`);
    const apiAddressOn = (network: string) =>
      docker('inspect', '-f', `{{(index .NetworkSettings.Networks "${network}").IPAddress}}`, `${project}-api-1`);

    // Outside the configured range: the forwarded address is ignored and the connection's own (10.201.91.0/24) is audited.
    const untrusted = await start(outside, apiAddressOn(outside));
    expect(untrusted).not.toBe(forgedAddress);
    expect(sql(`select ('${untrusted}'::inet << '10.201.91.0/24'::inet)::text`)).toBe('true');

    // Inside it (any container on the Compose default network is trusted, story boundary): the forwarded address is taken.
    expect(await start(`${project}_default`, apiAddressOn(`${project}_default`))).toBe(forgedAddress);
  } finally {
    docker('network', 'disconnect', '-f', outside, `${project}-api-1`);
    docker('network', 'rm', outside);
  }
});
