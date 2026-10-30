import { ApiError, type Problem } from './mutator';
import {
  isProblem,
  problemExtension,
  problemInteger,
  problemType,
  traceIdOf,
  validationFields,
} from './problems';

// Criterion 2: problems are identified by the full `type` URI (RFC 9457 section 3.1.1), compared with ===.
describe('problem types', () => {
  it('builds the full URI from the one base and a slug', () => {
    expect(problemType('linking-required')).toBe(
      'https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/linking-required.md',
    );
  });

  it('recognises a problem whose type is the whole URI', () => {
    const error = new ApiError(403, { type: problemType('linking-required') });

    expect(isProblem(error, 'linking-required')).toBe(true);
    expect(isProblem(error, 'core-banking-unavailable')).toBe(false);
  });

  it('does not match on the last path segment: another URI ending in the same slug is a different problem', () => {
    const lookalike = new ApiError(403, { type: 'https://example.com/other/linking-required.md' });
    const oldTagUri = new ApiError(403, { type: 'tag:disputedesk,2026:problem:linking-required' });

    expect(isProblem(lookalike, 'linking-required')).toBe(false);
    expect(isProblem(oldTagUri, 'linking-required')).toBe(false);
  });

  it('is false for an error with no problem body, and for anything that is not an ApiError', () => {
    expect(isProblem(new ApiError(403, undefined), 'linking-required')).toBe(false);
    expect(isProblem(new Error('x'), 'linking-required')).toBe(false);
    expect(isProblem(undefined, 'linking-required')).toBe(false);
  });
});

describe('traceIdOf', () => {
  it('gives the trace id of a problem body, and nothing for a body-less error or a network failure', () => {
    expect(traceIdOf(new ApiError(500, { traceId: 'trace-9' }))).toBe('trace-9');
    expect(traceIdOf(new ApiError(502, undefined))).toBeUndefined();
    expect(traceIdOf(new TypeError('Failed to fetch'))).toBeUndefined();
  });
});

describe('problemExtension', () => {
  it('reads a string extension member, and nothing for a missing or non-string one', () => {
    // Variables, not literals: the extension members are not in the generated ProblemDetails type.
    const alreadyDisputed = { type: problemType('already-disputed'), disputeId: 'D-1' };
    const numeric = { title: 'x', reason: 5 };

    expect(problemExtension(new ApiError(409, alreadyDisputed), 'disputeId')).toBe('D-1');
    expect(problemExtension(new ApiError(409, alreadyDisputed), 'reason')).toBeUndefined();
    expect(problemExtension(new ApiError(422, numeric), 'reason')).toBeUndefined();
    expect(problemExtension(new ApiError(502, undefined), 'reason')).toBeUndefined();
  });

  it('reads customerStatus from an action-not-allowed problem', () => {
    const notAllowed = { type: problemType('action-not-allowed'), customerStatus: 'resolved' };

    expect(isProblem(new ApiError(409, notAllowed), 'action-not-allowed')).toBe(true);
    expect(problemExtension(new ApiError(409, notAllowed), 'customerStatus')).toBe('resolved');
  });
});

// Story 6.4: the linking problem slugs and the number reader for `attemptsRemaining`.
describe('linking problems', () => {
  it.each(['already-linked', 'otp-incorrect', 'linking-locked', 'rate-limited'] as const)(
    'builds the %s type from the base and the slug',
    (slug) => {
      expect(problemType(slug)).toBe(
        `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/${slug}.md`,
      );
    },
  );

  it('reads an integer extension member, and nothing else', () => {
    const read = (value: unknown) =>
      problemInteger(
        new ApiError(422, { attemptsRemaining: value } as Problem),
        'attemptsRemaining',
      );

    expect(read(2)).toBe(2);
    expect(read(0)).toBe(0);
    expect(read(1.5)).toBeUndefined();
    expect(read('2')).toBeUndefined();
    expect(read(null)).toBeUndefined();
    expect(read(undefined)).toBeUndefined();
    expect(problemInteger(new ApiError(502, undefined), 'attemptsRemaining')).toBeUndefined();
  });
});

describe('validationFields', () => {
  it('lists the keys of the errors member, and nothing when there are none', () => {
    const named = new ApiError(400, { errors: { idNumber: ['x'], code: ['y'] } } as Problem);

    expect(validationFields(named)).toEqual(['idNumber', 'code']);
    expect(validationFields(new ApiError(400, { title: 'Bad' }))).toEqual([]);
    expect(validationFields(new ApiError(502, undefined))).toEqual([]);
  });
});
