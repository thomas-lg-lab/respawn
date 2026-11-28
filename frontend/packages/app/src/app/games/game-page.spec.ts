import { gamePageOrigin, isGamePage, originIn, originOf } from './game-page';

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

  it('tells a game’s page by its address', () => {
    expect(isGamePage('/games/0198')).toBe(true);
    expect(isGamePage('/games/0198?steam_link=failed')).toBe(true);
    expect(isGamePage('/games/0198#history')).toBe(true);
    expect(isGamePage('/games')).toBe(false);
    expect(isGamePage('/games/')).toBe(false);
    expect(isGamePage('/games/0198;view=month')).toBe(true);
    expect(isGamePage('/games/0198/months')).toBe(false);
    expect(isGamePage('/games;view=grid')).toBe(false);
    expect(isGamePage('/library')).toBe(false);
  });

  it('tells a screen by its address, whatever its parameters', () => {
    expect(originOf('/library;view=grid')).toBe('library');
    expect(originOf('/time?year=2025#top')).toBe('time');
  });

  it('says where a game page stands, and whether the way back returns there', () => {
    expect(gamePageOrigin({ from: 'time' })).toEqual({ screen: 'time', returns: true });
    expect(gamePageOrigin({ from: 'time', throughGame: true })).toEqual({
      screen: 'time',
      returns: false,
    });
    expect(gamePageOrigin(null)).toEqual({ screen: 'library', returns: false });
    expect(gamePageOrigin({ from: 'account' })).toEqual({ screen: 'library', returns: false });
  });
});
