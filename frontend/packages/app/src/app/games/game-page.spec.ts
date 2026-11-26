import { originIn, originOf } from './game-page';

describe('the way to a game page', () => {
  it('knows the screens it leads back to, by their address', () => {
    expect(originOf('/')).toBe('home');
    expect(originOf('/library?q=hades')).toBe('library');
    expect(originOf('/time#map')).toBe('time');
    expect(originOf('/account')).toBeNull();
    expect(originOf('/games/0198')).toBeNull();
  });

  it('reads where a navigation came from, and nothing else', () => {
    expect(originIn({ from: 'time' })).toBe('time');
    expect(originIn({ from: 'account' })).toBeNull();
    expect(originIn({ from: 'toString' })).toBeNull();
    expect(originIn(null)).toBeNull();
    expect(originIn({ navigationId: 3 })).toBeNull();
  });
});
