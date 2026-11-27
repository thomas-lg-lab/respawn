# Recorded answers

What the sources answer, played back by the tests in place of the sources: no test calls one.

A file holds an answer in the source's own shape, as a connector would receive it, with the data made
anonymous: nobody's library, account id or key. The games are a few well-known ones, and the figures are
made up. The files here are written by hand in that shape, not captured from the source: they prove what a
connector makes of such an answer, not that the source still answers this way.

| File | Source's call | Says |
| --- | --- | --- |
| `steam/owned-games.json` | `IPlayerService/GetOwnedGames/v1` | four games: one played lately, one played long ago, one never launched, one Steam gives no name for |
| `steam/owned-games-private.json` | the same | a profile whose game details are private: an empty `response` |
| `steam/year-in-review-2024.json` | `ISaleFeatureService/GetUserYearInReview/v1` (not documented by Valve; the shape follows `webui/service_salefeature.proto` of the community's extracted protobufs) | a review of 2024: January (Portal 2, Half-Life 2) and March (Portal 2, and Half-Life, a game the library above does not list), 3 h 15 min in 7 sessions; each month dated by its start at midnight Pacific time |
| `steam/year-in-review-none.json` | the same | a year Steam has no review of yet: an empty `response` |
| `steam/store-items.json` | `IStoreBrowseService/GetItems/v1` with `include_assets`, which takes no key | where the covers of the four games above are: Portal 2 at the address every game had (`library_600x900.jpg`), Half-Life 2 in a folder of its own (a made-up hash), Portal with assets and no cover among them, and the fourth unknown to the store (`success` 15, no assets) |
| `steam/cover.jpg`, `steam/cover.png` | a file of Steam's image host | not Steam's artwork: a grey JPEG of 8 x 8 pixels and a PNG of 2 x 3, written byte by byte; what matters is that they are images of each kind |
| `igdb/token.json` | Twitch's `POST oauth2/token`, `grant_type=client_credentials` | a token for the instance, `expires_in` about 58 days; made up, nobody's |
| `igdb/token-wrong-secret.json` | the same | the 403 Twitch gives for a client id it knows with the wrong secret |
| `igdb/token-unknown-client.json` | the same | the 400 Twitch gives for a client id it does not know |
| `igdb/external-games.json` | IGDB's `POST v4/external_games`, `fields game,uid; where external_game_source = 1 & uid = (...)` | seven Steam games and the IGDB game each one is; asked about with an eighth that IGDB does not know, which has no row |
| `igdb/games.json` | IGDB's `POST v4/games`, `fields name,slug,url,cover.image_id,genres.name,themes.name,summary,first_release_date,similar_games,...; where id = (...)` | three of those games (Portal, Portal 2, Half-Life 2) with their page, a made-up cover id, genres, themes and a summary written for the tests; Portal and Portal 2 with the day they came out and the games IGDB lists as similar (each other, Half-Life 2, and 1020, a game none of the stores' games is), Half-Life 2 with neither |
| `igdb/game-time-to-beats.json` | IGDB's `POST v4/game_time_to_beats`, `fields game_id,hastily,normally,completely,count; where game_id = (...)` | how long two of them take, in seconds: Portal 2 with all three figures, Half-Life 2 with two and few players; Portal has no row |
