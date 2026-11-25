# Working on this repository (humans and agents)

- **Trunk-based.** `main` is the only long-lived branch. Work on a short-lived branch and open one
  PR into `main`; it is squash-merged once the required checks pass. Never push to `main`.
- **Signed commits.** The ruleset requires a verified signature on every commit of a PR, not only
  on the squash commit: sign with an SSH or GPG key registered on GitHub as a signing key. Commits
  made through the GitHub API or web UI (Renovate, the merge button) are signed by GitHub.
- **Merging is not releasing.** `main` builds the `edge` image; only a published release (a `v*`
  tag, from the draft release) builds `latest`.
- **Shared workflows.** PR conventions, release notes, the image build and registry cleanup call the reusable
  workflows of `thomas-lg-lab/.github` at a version tag; change them there, not here.
- **PR titles follow Conventional Commits** (`type(scope): summary`, commitlint types, `!` for a breaking
  change): checked by the required `conventions / title` check; the label and the release notes come from it.
- **Issues and PR descriptions** follow the organisation's defaults in `thomas-lg-lab/.github`
  (issue forms, PR template, and how to fill them in its README); they are not in this repository.
  Fill every section that applies, delete the others, and say how the change was tested. An issue
  opened through the API or `gh` skips the forms: use the same sections and set the issue type
  (`gh api repos/OWNER/REPO/issues -f title=... -f body=... -f type=Task`, or `Bug`, `Feature`).
- **Dependencies** are updated by Renovate (shared config in `thomas-lg-lab/renovate-config`); do not
  bump them by hand in a feature PR. Security fixes arrive as `security` PRs at any time.
- **Supply chain.** Actions are pinned to commit SHAs and images to digests; keep it that way when
  adding one (Renovate keeps them current).
- **Secrets** never go in the repository, in a commit message or in a log.
- Technical files are written in English.

## This project

- **Layout.** `backend/`, .NET 11, split by responsibility: `Respawn.Host` (composition only: `Program.cs`, the
  HTTP pipeline, the front's files served from `wwwroot/`, which only the image fills), `Respawn.Persistence` (the database connection, opened and
  migrated on first use), `Respawn.SharedKernel` (the error format,
  the database contracts, what a navigation may come back to; no package), and one hexagonal module per responsibility under `Modules/`. Today
  `Modules/Accounts`: `Respawn.Accounts` (the core: use cases and ports, no package), `.Infrastructure` (EF Core
  and its migrations, ASP.NET Core Identity), `.Api` (the controllers, the session cookie, the sign-in through a provider);
  and `Modules/Games`, the same three projects: the catalogue (a game as one record whatever the store), the
  linked store accounts, the sources' answers kept untouched, ownership and playtime, and the sync (what is
  due and what a run becomes are the core's, `SyncService`; the timer that asks every few minutes is an
  inbound adapter, `SyncScheduler` in `.Api`; tests set the clock and run a turn by hand), and the import of a
  store's yearly reviews, apart from the sync and never needed by it (`YearReviewService`, and
  `YearReviewScheduler` in `.Api`); and its connectors, one
  project per outside source behind a port of the core (`Respawn.Games.Steam`: the Steam Web API), tested
  against recorded answers under `Recorded/`, never against the source.
  One test project per module (`Modules/<Name>/Respawn.<Name>.Tests`), `Respawn.Tests` for the host and the
  architecture rules, `Respawn.Testing` for what they share; they start the real app against a real PostgreSQL.
- **Architecture is checked.** A module never references another module, its infrastructure or its tables; an inbound
  adapter never references its module's infrastructure; a core references no framework; a connector references
  its module's core only; each module owns one
  PostgreSQL schema and its migrations. `ArchitectureTests` fails otherwise. No empty project either, which no
  test checks; CONTRIBUTING.md
  says how to add a module.
- **The public API is a contract.** Routes live under `/api/v1`; a breaking change means a new version. The front
  uses only these routes. Errors are always `{"error": {"code", "message"}}`.
- **Runtime rules.** The app starts and `/healthz` answers with no database and no secret; nothing is written
  outside `/tmp`; logs go to stdout. Keep it that way: the image is tested like this in CI. What runs in the
  background (the sync's schedule) never stops the app, and says a trouble that lasts once, not at every turn.
- **Sign-in code.** Accounts, password checks and lockout go through ASP.NET Core Identity; cryptography comes
  from libraries (libsodium through Sodium.Core for Argon2id and for sealing the store keys users give,
  ASP.NET Core's OpenID Connect handler for a sign-in through a provider, which the API runs itself); never write
  a hash or a signature check by hand. A request header such as `Remote-User` never identifies anyone.
  "Sign in through Steam" links a library and never opens a session; its exchange is a library's too
  (AspNet.Security.OpenId.Steam), and what it proves is still checked to be a SteamID.
- **Warnings are errors** in a Release build, and `dotnet format --verify-no-changes` is a required check.
- **Front.** `frontend/`, Angular 22 with signals and no Zone.js, Tailwind 4, Transloco, Vitest, in packages
  under `packages/`: `app` (the shell, its screens in `pages/`), `ui` (the tokens and the base components),
  `util` (the display settings), `data` (the API: its types, its client, each feature's queries). A package imports another through its `@respawn/x` barrel only, and only
  the ones `eslint.config.js` lists for it; `@x/...` is a package's own inside.
- **Angular as it is today.** Zoneless and OnPush are the defaults since Angular 21 and 22: neither is written
  out, and `ChangeDetectionStrategy.Eager` is refused by lint. State is signals (`signal`, `computed`,
  `input()`, `model()`), a singleton is a `@Service()`, dependencies come from `inject()`, host bindings go in
  `host`. Tests use Vitest's globals and wait with `await fixture.whenStable()` rather than forcing
  `detectChanges()`. Check angular.dev before reaching for an older idiom.
- **Smart and dumb components.** A screen (`pages/x/x.ts`) or the shell is where the API, the router and the
  settings are injected; what it draws is made of components that only take inputs and emit outputs
  (`pages/x/components/`, `components/` when shared, `packages/ui` when it knows nothing of the app). A
  component of `ui` holds no sentence: its words are inputs.
- **Server state.** What the API answered lives in TanStack Query (`@tanstack/angular-query`, pinned to an exact
  version), not in a store or a service's signals. A feature's queries and mutations are the methods of a
  `@Service()` in `packages/data`, in `<feature>.queries.ts`; a component passes them to `injectQuery` or
  `injectMutation`, a guard to the `QueryClient`. A refusal is an `ApiFailure`, and a screen picks its sentence
  from the failure's `code`.
- **Forms** are signal forms (`@angular/forms/signals`); a field is `rs-text-field` bound with `[formField]`.
- **Where a look is written.** A base component of `packages/ui` carries its look in its own stylesheet
  (`x.css` beside `x.ts`, `styleUrl`), written as CSS on the `--rs-*` tokens inside `@layer components`: a
  variant or a state is a selector (`:host([data-variant='primary'])`, `[aria-pressed='true']`), never a table
  of class strings in the `.ts`. Like every component, its template is in `x.html` as soon as it is more than a slot or a value.
  Tailwind utilities are for the layout of a screen, in its template.
- **Content security policy.** `default-src 'self'`, and for the document alone the style elements that carry
  the nonce of the response: Angular brings component styles that way, and the host writes the document with
  a new nonce each time. So no inline script, no `style` attribute written in a template, and no style
  element made by hand. `[style.x]` bindings are fine. `/docs`, the page of the API's description, has a
  policy of its own (`data:` images, no nonce); do not widen the app's for a library.
- **Design tokens.** Every token is a `--rs-*` custom property in `packages/ui/src/styles/tokens.css`, switched
  at run time by `data-theme` and `data-character` on the root element. Components use the semantic Tailwind
  names mapped in `theme.css` (`bg-surface`, `rounded-card`, `type-body`), never a hex value; the default
  palette is removed, so a palette step produces no style. One layout breakpoint: `wide:`.
- **No text in a template.** Every sentence is a key of `i18n/fr.json` and `i18n/en.json`, which keep the same
  keys; lint refuses a literal text node, and a literal in an attribute that is read or heard (`aria-label`,
  `placeholder`, `title`, `alt`...). A language is shown and remembered only once its translations loaded.
- **API types are generated.** `frontend/openapi.json` is the description the API serves and `api-types.ts`
  comes from it (`npm run gen:api`); the CI fails when either is stale. Never edit them by hand.
- **Checks and dependencies**: CONTRIBUTING.md. When the `node` and `sdk` services of the compose file are up,
  run the checks in them (`docker compose exec`) rather than in a new container. They check the checkout that
  started them: `docker compose ps`, run from where you work, must list them. From another worktree, use a
  new container, never the first checkout's services.
