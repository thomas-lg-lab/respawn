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

- **Layout.** `backend/`, .NET 10, split by responsibility: `Respawn.Host` (composition only: `Program.cs`, the
  HTTP pipeline, the front's files served from `wwwroot/`, which only the image fills), `Respawn.Persistence` (the database connection, opened and
  migrated on first use), `Respawn.SharedKernel` (the error format,
  the database contracts; no package), and one hexagonal module per responsibility under `Modules/`. Today
  `Modules/Accounts`: `Respawn.Accounts` (the core: use cases and ports, no package), `.Infrastructure` (EF Core
  and its migrations, ASP.NET Core Identity, ID token validation), `.Api` (the controllers, the session cookie);
  and `Modules/Games`, the same three projects: the catalogue (a game as one record whatever the store), the
  linked store accounts, the sources' answers kept untouched, ownership and playtime.
  One test project per module (`Modules/<Name>/Respawn.<Name>.Tests`), `Respawn.Tests` for the host and the
  architecture rules, `Respawn.Testing` for what they share; they start the real app against a real PostgreSQL.
- **Architecture is checked.** A module never references another module, its infrastructure or its tables; an inbound
  adapter never references its module's infrastructure; a core references no framework; each module owns one
  PostgreSQL schema and its migrations; no empty project. `ArchitectureTests` fails otherwise; CONTRIBUTING.md
  says how to add a module.
- **The public API is a contract.** Routes live under `/api/v1`; a breaking change means a new version. The front
  uses only these routes. Errors are always `{"error": {"code", "message"}}`.
- **Runtime rules.** The app starts and `/healthz` answers with no database and no secret; nothing is written
  outside `/tmp`; logs go to stdout. Keep it that way: the image is tested like this in CI.
- **Sign-in code.** Accounts, password checks and lockout go through ASP.NET Core Identity; cryptography comes
  from libraries (libsodium through Sodium.Core for Argon2id, Microsoft.IdentityModel for ID tokens); never write
  a hash or a signature check by hand. A request header such as `Remote-User` never identifies anyone.
- **Warnings are errors** in a Release build, and `dotnet format --verify-no-changes` is a required check.
- **Front.** `frontend/`, Angular 22 with signals and no Zone.js, Tailwind 4, Transloco, Vitest, in packages
  under `packages/`: `app` (the shell, its screens in `pages/`), `ui` (the tokens and the base components),
  `util`, `data` (the API's types). A package imports another through its `@respawn/x` barrel only, and only
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
- **No inline style.** The content security policy is `default-src 'self'`: a component has no `styles`, the
  document no inline script or style. Classes and `[style.x]` bindings are fine.
- **Design tokens.** Every token is a `--rs-*` custom property in `packages/ui/src/styles/tokens.css`, switched
  at run time by `data-theme` and `data-character` on the root element. Components use the semantic Tailwind
  names mapped in `theme.css` (`bg-surface`, `rounded-card`, `type-body`), never a hex value; the default
  palette is removed, so a palette step produces no style. One layout breakpoint: `wide:`.
- **No text in a template.** Every sentence is a key of `i18n/fr.json` and `i18n/en.json`, which keep the same
  keys; lint refuses a literal text node, and a literal in an attribute that is read or heard (`aria-label`,
  `placeholder`, `title`, `alt`...). A language is shown and remembered only once its translations loaded.
- **API types are generated.** `frontend/openapi.json` is the description the API serves and `api-types.ts`
  comes from it (`npm run gen:api`); the CI fails when either is stale. Never edit them by hand.
- **Checks and dependencies**: CONTRIBUTING.md.
