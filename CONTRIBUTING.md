# Contributing

The checks the CI runs, locally. They need Docker only: the tools run in the .NET SDK image of the Dockerfile,
against the database server of `docker-compose.yml`. With the .NET 11 SDK installed, the same `dotnet` commands
run directly (the tests then reach the database on `localhost`, their default).

```bash
docker compose up -d db
# The network is named after the checkout's directory: <directory>_default.
docker run --rm --network respawn_default -v "$PWD:/work" -w /work \
  -e RESPAWN_TEST_DATABASE_URL=postgresql://respawn:respawn@db:5432/respawn_test \
  mcr.microsoft.com/dotnet/sdk:11.0 sh -c '
    dotnet restore Respawn.slnx --locked-mode &&
    dotnet format Respawn.slnx --verify-no-changes --no-restore &&
    dotnet build Respawn.slnx --no-restore --configuration Release &&
    dotnet test Respawn.slnx --no-build --configuration Release'
docker build -t respawn .
```

Each test project runs in a database of its own, beside the `respawn` database the compose app uses: the
projects run side by side, and each empties its tables. `RESPAWN_TEST_DATABASE_URL` gives the server and a base
name ending in `_test` (anything else is refused); a project puts its own name in it, so `respawn_test` gives
`respawn_accounts_test`, `respawn_games_test` and `respawn_host_test`. The tests create these databases when they are missing (and
three more, also ending in `_test`: two for the connection tests, with a role, and one for the test of an
upgrade of the accounts), so the account needs the right to create databases and roles.

## The tools, kept running

The commands above and below start from an empty container each time, so each one installs the packages again
before it checks anything. `docker-compose.yml` also has the two tools as services that stay up, `sdk` and
`node` (profile `tools`): the packages stay installed and the build stays warm between two checks. They mount
the sources, `sdk` reaches the database by its name and already has `RESPAWN_TEST_DATABASE_URL`.

```bash
docker compose --profile tools up -d node sdk   # once; the database comes with sdk
docker compose exec sdk dotnet test Respawn.slnx
docker compose exec node npm ci                 # once, then after a change to package-lock.json
docker compose exec node npm test
```

Any command of this page runs the same way, without its `docker run` and without `npm ci`. The dev server too,
on http://localhost:4200, with the app beside it (`docker compose up -d --build app`); `--poll` because a
change made on Windows or macOS does not reach a watcher inside the container:

```bash
docker compose exec node npm start -- --host 0.0.0.0 --poll 2000
```

`docker compose --profile tools down` stops them with the rest.

What to know before leaning on them:

- **They check the checkout that started them.** The services mount the directory `docker compose up` ran in,
  and the compose project is named after it. From another checkout or worktree, `docker compose exec` finds no
  service, and reaching the first one's (`-p`) would check its sources, not yours. A second checkout cannot
  start its own beside the first either: ports 5432 and 4200 are already published.
- **One set of tools per checkout.** What `sdk` builds lands in the sources: `bin/` and `obj/` hold the
  container's paths. An editor reads them fine; `dotnet` run on the machine itself against the same checkout
  would break on them, or rebuild them and break the service in turn. Pick one side; to change, delete `bin/`
  and `obj/`. The `node` service writes the front's build cache and output there too (`frontend/.angular`,
  `frontend/dist`).
- **The front's packages are a volume of the `node` service**, not the checkout's `node_modules` (reading them
  through a disk shared from Windows or macOS is slow): an editor on the machine needs its own `npm ci`. The
  volume starts empty, and `npm ci` in the service empties it before it fills it. The checkout's folder is
  still the one the `docker run` commands of this page fill, with Linux binaries: after one of them, or on a
  checkout whose service filled that folder before it had a volume, `npm ci` on the machine again.
- **Port 4200 is the `node` service's** as long as it is up, dev server running or not: the `docker run` dev
  server of the next section, or one started on the machine, then finds the port taken.
- **On a Linux host, what they write belongs to root** (as with the `docker run` commands): removing `bin/` or
  `obj/` then takes `sudo`, or an `rm` through the service. So does the folder the volume is laid over: on a
  new checkout, `mkdir frontend/node_modules` before the first `up`, or Docker makes it, as root, and `npm ci`
  on the machine is refused.

## The front

The front's checks, in the Node image, or with Node 24 installed the same `npm` commands in `frontend/`:

```bash
docker run --rm -v "$PWD/frontend:/web" -w /web node:24-slim sh -c '
  npm ci &&
  npm run format:check &&
  npm run lint &&
  npm run build &&
  npm test'
```

The dev server, on http://localhost:4200. It hands `/api` and `/healthz` to the app, started beside it
(`docker compose up --build`, on port 8080); `RESPAWN_API` says where that is when the dev server runs in a
container. With the `node` service up, start it there instead (above): the service holds port 4200.

```bash
docker run --rm -it -p 4200:4200 -e RESPAWN_API=http://host.docker.internal:8080 -v "$PWD/frontend:/web" -w /web node:24-slim sh -c '
  npm ci && npm start -- --host 0.0.0.0'
```

The app itself serves the front only in the image, which builds it: `docker compose up --build` is the way to
see the two together. Run from the sources, the app has no front and its addresses answer 404; the API works.

The API's types are generated: `frontend/openapi.json` is a copy of what the API serves, and
`packages/data/src/lib/models/api-types.ts` is what `openapi-typescript` makes of it. After a change to a
route or to a body, with the app running (`docker compose up --build`), save the description again and
regenerate; the CI compares both and fails when one is stale.

```bash
curl -fsS http://localhost:8080/api/v1/openapi.json -o frontend/openapi.json
docker run --rm -v "$PWD/frontend:/web" -w /web node:24-slim sh -c 'npm ci && npm run gen:api'
```

[frontend/README.md](frontend/README.md) describes the design tokens, the fonts and the translations.

## Layout

The app is split by responsibility, not by layer. Each module under `backend/Modules/` is hexagonal: a core,
and adapters around it.

| Project | Holds | May reference |
| --- | --- | --- |
| `Respawn.Host` | composition only: `Program.cs`, the HTTP pipeline, one `Add<Module>()` per module, the front's files | everything, but no database driver; nothing references it but the tests |
| `Respawn.Persistence` | the database connection: opened on first use, every module's schema migrated then, a rotated password followed | the shared kernel; no module |
| `Respawn.SharedKernel` | what modules and the host agree on: the error format, the database contracts, and what a navigation may come back to (a path of the app, never another site) | nothing, not even a package |
| `Modules/Accounts/Respawn.Accounts` | the core: use cases and the ports they need | nothing; no package |
| `Modules/Accounts/Respawn.Accounts.Infrastructure` | outbound adapters: EF Core, ASP.NET Core Identity, sessions | its core, the shared kernel |
| `Modules/Accounts/Respawn.Accounts.Api` | inbound adapter: the controllers of `/api/v1`, the session cookie, the sign-in through a provider (ASP.NET Core's OpenID Connect handler) | its core, the shared kernel |
| `Modules/Accounts/Respawn.Accounts.Tests` | the tests of the module, which start the real app in-process | its module, the test support |
| `Modules/Games/Respawn.Games` | the core: the catalogue, the libraries, the sync and what each game gained by each reading, the import of a store's yearly reviews, the covers and each account's choice among them, what IGDB says of each game, the tags the stores' players gave each game, what is read back of them (one game as its owner sees it, the time played by day, by year, by month and by game), and the ports they need | nothing; no package |
| `Modules/Games/Respawn.Games.Infrastructure` | outbound adapters: EF Core, the encryption of the store keys users give (libsodium), and the files of the kept covers in the data directory | its core, the shared kernel |
| `Modules/Games/Respawn.Games.Steam` | a connector: the Steam Web API behind the core's `ILibrarySource` and `IYearReviewSource`, and Steam's store and image host, asked with no key, behind `ICoverSource` and `IStoreTagSource` (the tags its players gave each game, and their names in English) | its core only |
| `Modules/Games/Respawn.Games.Igdb` | a connector: IGDB, with the token Twitch gives the instance, behind the core's `IGameDataSource` (which IGDB games the games of a store are, and what IGDB says of those: genres, themes, summary, cover, release day, similar games, durations), and IGDB's image host, asked with no token, for the covers | its core only |
| `Modules/Games/Respawn.Games.Api` | inbound adapter: the controllers of `/api/v1`, the schedules of the sync, of the import of the yearly reviews, of the covers' places, of what IGDB says of the games and of the stores' tags (five timers in the app's process), and "Sign in through Steam", whose exchange with Steam (the browser sent there, its answer checked with it) is a library's | its core, the shared kernel |
| `Modules/Games/Respawn.Games.Tests` | the tests of the module, which start the real app in-process | its module, the test support |
| `Respawn.Tests` | the tests of the host and of the solution as a whole: the runtime contract, the database connection, the architecture rules | everything |
| `Respawn.Testing` | what every test project shares: the app started in-process, the test database; not a test project | the host |

The rules, checked by `ArchitectureTests`:

- A module never references another module's infrastructure or tables, only its public contract. No module
  has one yet, so today a module references no other module at all, and no table points into another schema:
  another module's row is known by its id (an account, in `Games`).
- A core references no framework and none of its adapters: no ASP.NET Core, EF Core, `Microsoft.Extensions`,
  IdentityModel, Npgsql or Sodium, and no other project of the app.
- An inbound adapter never references its module's infrastructure: the host puts them together.
- Each module owns one PostgreSQL schema, one `DbContext` and its migrations (`accounts` for Accounts, `games`
  for Games).
- The host holds no database driver: the connection is `Respawn.Persistence`'s, which knows no module.
- No empty project: a module or a layer is created when it has something to hold. And every project of
  the solution is one the rules know.
- A connector to an outside source is a project of its own in its module (`Respawn.<Module>.<Source>`). It
  references the module's core and nothing else of the app: not the database, not the shared kernel, not
  another connector.

A new module adds its three projects and its test project under `backend/Modules/<Name>/`, an `Add<Name>()`
in `Respawn.Host/Modules/`, and its assemblies and its schema to `ArchitectureTests`; `Respawn.Testing`
empties every table of its schema before each test. A module's tests live in the module;
what only they need (a fake of an outside service) stays there, not in `Respawn.Testing`. A port belongs to the module that uses it;
an adapter that lives in its own project (a connector to an outside source) only references that module's core.

A connector is tested against recorded answers, never against its source: the answers are files under
`Recorded/` in the module's tests, in the source's own shape and made anonymous (nobody's library, account
or key), and a fake of the source plays them back in memory. A secret a connector needs is read each time it
is needed and never at start, from the environment or a file under `/secrets`, sent in a header rather than in an address
when the source allows it, and never written in a log or in an error. What a source gives to anyone is
asked through a client of its own, which never carries the secret. What a source says is never put in an
address as it is: an id or a path is checked for its shape first, and the host is the connector's.

## Dependencies

Versions are in each `.csproj`; the resolved graph is in the `packages.lock.json` beside it, and the CI and
the image restore in locked mode. After changing a version, write the lock files again:

```bash
dotnet restore Respawn.slnx --force-evaluate
```

Renovate keeps both current. The front's versions are in `frontend/package.json` and its resolved graph in
`frontend/package-lock.json`, which `npm ci` installs exactly.

## Database changes

A change to a module's entities or to its `DbContext` needs a migration in that module, generated by the EF
Core tools (here for Accounts):

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add <Name> --project backend/Modules/Accounts/Respawn.Accounts.Infrastructure \
  --startup-project backend/Modules/Accounts/Respawn.Accounts.Infrastructure -o Persistence/Migrations
```

Migrations run on the first connection, so they must be safe on a database that already holds data. The
generated files are left as the tools write them: `.editorconfig` marks the folder as generated code.
Some tables are derived from others: `games.playtime_gains` is written from `games.observations` with each
reading, so a migration that changes observations writes the gains of the accounts it touches again.
