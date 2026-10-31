import { expect, test } from '@playwright/test';
import { holdsCode, telemetryValues } from './support';

// The span privacy scan helpers (no stack needed): a code in an attribute, event or exception value is found; the same six
// digits inside a trace id, span id, parent id or nanosecond timestamp are not.
const code = '482913';
const spanDocument = (attributeValue: string, extra: Record<string, unknown> = {}) => JSON.stringify({
  resourceSpans: [{ scopeSpans: [{ spans: [{
    traceId: `a1${code}b2c3d4e5f60718293a4b5c6d7e`, spanId: `${code}0a1b2c3d4e`, parentSpanId: `ff${code}00aa11bb22`,
    startTimeUnixNano: `17300${code}1234567`, endTimeUnixNano: 1730012345000000000,
    name: 'GET /api/v1/disputes',
    attributes: [{ key: 'url.path', value: { stringValue: attributeValue } }],
    events: [], ...extra,
  }] }] }],
});

test('hex ids and nanosecond timestamps holding the code are not a hit', () => {
  const values = telemetryValues(spanDocument('/api/v1/disputes'));
  expect(holdsCode(values, code)).toBe(false);
  // Even the raw document would hit a naive substring check.
  expect(spanDocument('/api/v1/disputes').includes(code)).toBe(true);
});

test('a code in an attribute value is a hit', () => {
  expect(holdsCode(telemetryValues(spanDocument(`/api/v1/linking/verify?code=${code}`)), code)).toBe(true);
  expect(holdsCode(telemetryValues(spanDocument(code)), code)).toBe(true);
});

test('a code in an exception event is a hit', () => {
  const event = { name: 'exception', attributes: [{ key: 'exception.message', value: { stringValue: `bad code ${code}.` } }] };
  expect(holdsCode(telemetryValues(spanDocument('/x', { events: [event] })), code)).toBe(true);
});

test('a code inside a longer hex word or number is not a hit', () => {
  expect(holdsCode(telemetryValues(spanDocument(`ab${code}cd`)), code)).toBe(false);
  expect(holdsCode(telemetryValues(spanDocument(`1${code}`)), code)).toBe(false);
  // the microsecond fraction of a timestamp
  expect(holdsCode(telemetryValues(spanDocument(`2026-10-31T12:00:00.${code}+00:00`)), code)).toBe(false);
});
