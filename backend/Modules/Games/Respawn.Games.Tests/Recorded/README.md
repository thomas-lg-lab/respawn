# Recorded answers

What the sources answer, played back by the tests in place of the sources: no test calls one.

A file holds an answer in the source's own shape, byte for byte what a connector would receive, with the
data made anonymous: nobody's library, account id or key. The games are a few well-known ones, and the
figures are made up.

| File | Source's call | Says |
| --- | --- | --- |
| `steam/owned-games.json` | `IPlayerService/GetOwnedGames/v1` | four games: one played lately, one played long ago, one never launched, one Steam gives no name for |
| `steam/owned-games-private.json` | the same | a profile whose game details are private: an empty `response` |
