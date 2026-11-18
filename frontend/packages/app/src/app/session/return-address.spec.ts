import { returnAddress } from './return-address';

describe('returnAddress', () => {
  it.each(['/library', '/games/half-life.2', '/library?sort=name&page=2', '/games/42#notes'])(
    'keeps %s, an address of the app',
    (asked) => {
      expect(returnAddress(asked)).toBe(asked);
    },
  );

  // The address is read from a link anyone can write: nothing in it may lead elsewhere.
  it.each([
    ['another origin', 'https://elsewhere.example/library'],
    ['an address with no scheme', '//elsewhere.example/library'],
    ['a scheme', 'javascript:alert(1)'],
    ['a backslash, which a browser reads as a slash', '/\\elsewhere.example'],
    ['a backslash further on', '/library\\..\\elsewhere'],
    ['a tab, which a browser drops', '/\t/elsewhere.example'],
    ['a line break', '/\n/elsewhere.example'],
    ['a path that does not start at the root', 'library'],
    ['nothing', ''],
    ['no address at all', null],
  ])('falls back to the home for %s', (_what, asked) => {
    expect(returnAddress(asked)).toBe('/');
  });

  it.each(['/sign-in', '/sign-in?returnTo=%2Flibrary', '/sign-in/anything', '/sign-in#top'])(
    'does not come back to the sign-in itself (%s)',
    (asked) => {
      expect(returnAddress(asked)).toBe('/');
    },
  );

  it('keeps an address that only starts like the sign-in', () => {
    expect(returnAddress('/sign-in-help')).toBe('/sign-in-help');
  });
});
