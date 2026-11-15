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
  HTTP pipeline, `wwwroot/` for the page), `Respawn.Persistence` (the database connection, opened and
  migrated on first use), `Respawn.SharedKernel` (the error format,
  the database contracts; no package), and one hexagonal module per responsibility under `Modules/`. Today
  `Modules/Accounts`: `Respawn.Accounts` (the core: use cases and ports, no package), `.Infrastructure` (EF Core
  and its migrations, ASP.NET Core Identity, ID token validation), `.Api` (the controllers, the session cookie).
  One test project per module (`Modules/Accounts/Respawn.Accounts.Tests`), `Respawn.Tests` for the host and the
  architecture rules, `Respawn.Testing` for what they share; they start the real app against a real PostgreSQL.
- **Architecture is checked.** A module never references another module's infrastructure or tables; an inbound
  adapter never references its module's infrastructure; a core references no framework; each module owns one
  PostgreSQL schema and its migrations; no empty project. `ArchitectureTests` fails otherwise; CONTRIBUTING.md
  says how to add a module.
- **The public API is a contract.** Routes live under `/api/v1`; a breaking change means a new version. The page
  uses only these routes. Errors are always `{"error": {"code", "message"}}`.
- **Runtime rules.** The app starts and `/healthz` answers with no database and no secret; nothing is written
  outside `/tmp`; logs go to stdout. Keep it that way: the image is tested like this in CI.
- **Sign-in code.** Accounts, password checks and lockout go through ASP.NET Core Identity; cryptography comes
  from libraries (libsodium through Sodium.Core for Argon2id, Microsoft.IdentityModel for ID tokens); never write
  a hash or a signature check by hand. A request header such as `Remote-User` never identifies anyone.
- **Warnings are errors** in a Release build, and `dotnet format --verify-no-changes` is a required check.
- **Checks and dependencies**: CONTRIBUTING.md.
