import { sanitizeUrl } from './sanitizeUrl';

describe('sanitizeUrl', () => {
  it.each([
    [
      'http://localhost:3000/api/v1/transactions?cursor=cursor-seed-8L1N',
      'http://localhost:3000/api/v1/transactions',
    ],
    ['http://localhost:3000/app#TXN-SEED-7Q4Z', 'http://localhost:3000/app'],
    [
      'http://localhost:3000/api/v1/disputes/0b9a1c7e-5d3f-4a62-9e1b-7c2d8f4a6b10?x=1',
      'http://localhost:3000/api/v1/disputes/{id}',
    ],
    ['http://localhost:3000/api/v1/disputes/D-1', 'http://localhost:3000/api/v1/disputes/D-1'],
  ])('%s becomes %s', (url, expected) => {
    expect(sanitizeUrl(url)).toBe(expected);
  });
});
