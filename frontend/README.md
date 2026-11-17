# Frontend

Angular 22 (signals, zoneless and OnPush as the framework now defaults), Tailwind 4, Transloco for French and English, Vitest.
Split into packages under `packages/`: `app` (the shell and its screens), `ui` (the design tokens and the base
components), `util` (the display settings) and `data` (the API: its types, its client, and the queries of each
feature).

[CONTRIBUTING.md](../CONTRIBUTING.md) has the checks and how to run them with Docker alone;
[AGENTS.md](../AGENTS.md) has the rules.

```bash
npm start           # dev server on :4200
npm run build       # production bundle, in dist/app/browser
npm test            # Vitest, every package's specs in one run
npm run lint
npm run format      # Prettier; format:check only checks
npm run gen:api     # the API's types, from openapi.json
```

## Screens

The image builds this front and the API process serves it; the router decides the screen from the address.

| Address        | Screen                                                     | Needs      |
| -------------- | ---------------------------------------------------------- | ---------- |
| `/sign-in`     | sign in, or create an account where the instance allows it | no session |
| `/`            | who is signed in, under the header and its account menu    | a session  |
| `/foundations` | the type styles and the base components                    | nothing    |

A screen is where the API, the router and the settings are injected (`pages/x/x.ts`, and `layout/shell.ts` for
the header). What it draws is made of components that only take inputs and emit outputs: its own in
`pages/x/components/`, the shared ones in `components/`, and the ones that know nothing of the app in
`packages/ui`, whose words are all inputs.

## Server state

What the API answered is kept by TanStack Query (`@tanstack/angular-query`, pinned to an exact version: it is a
trial, to be judged on the first screens that read real data). A feature's queries and mutations are the
methods of one service in `packages/data` (`account/account.queries.ts`); a component passes them to
`injectQuery` or `injectMutation`, a route guard to the `QueryClient`. The signed-in account is one such query:
signing in writes it, signing out empties the cache.

Every refusal is an `ApiFailure` carrying the API's `code`; a screen shows its own sentence for the code
(`errors.*` in the translation files), never the API's English message.

## Forms

Signal forms (`@angular/forms/signals`). A field is `rs-text-field`, bound with `[formField]`: the form gives
it its value and its state, the screen its label, its hint and the sentence of its error. A form component
checks what can be checked in the browser and hands the values over; the screen sends them.

## Content security policy

Every answer of the app carries `default-src 'self'; frame-ancestors 'none'`. The document carries one thing
more, `style-src 'self' 'nonce-…'`: Angular inserts the styles of a component as a `<style>` element when the
component is first drawn, and the policy lets through only the ones that carry the nonce of the response.

- The app writes the document itself, with a new nonce each time (sixteen random bytes), in place of the
  word `RESPAWN_CSP_NONCE` that `index.html` holds on the root element (`ngCspNonce`, which Angular reads).
  The document is sent `no-store`: a kept copy would meet the nonce of another response.
- Scripts stay on `'self'`: no inline script anywhere. A `style` attribute written in a template, or a style
  element made by hand, is refused; a `[style.x]` binding goes through the DOM and is fine.
- The dev server sends no policy, so the word stays as it is there and nothing is blocked.

Two files are shipped as they are, from `packages/app/public/`: the icon, and `theme.js`, which the document
head loads before the style sheet so a reload paints the chosen theme at once. A file there is never kept by
browsers; a script there must not be named like a hashed one (`name-XXXXXXXX.js`), which the app serves as
immutable.

## Design tokens

`packages/ui/src/styles/tokens.css` holds every token as a CSS custom property (`--rs-*`), read at run time.
Two attributes on the root element switch them, and nothing else changes:

| Attribute        | Values           | Default                         |
| ---------------- | ---------------- | ------------------------------- |
| `data-theme`     | `dark`, `light`  | absent: the system's preference |
| `data-character` | `pixel`, `plain` | `pixel`                         |

`theme.css` maps Tailwind's names onto them with `@theme inline`, so a utility carries the variable itself
(`.bg-surface { background-color: var(--rs-surface) }`) and follows a switch. Tailwind's default palette, radii
and type scale are removed: a component names a semantic token (`bg-surface`, `rounded-card`, `type-body`),
never a hex value or a palette step.

A base component of `packages/ui` has its own stylesheet beside it (`button.css` for `button.ts`), plain CSS
on the tokens: a variant or a state is a selector on the host (`:host([data-variant='primary'])`,
`:host([aria-pressed='true'])`), so its whole look reads in one place. `@reference` gives it the `type-*`
styles (`@apply type-control`) and the `wide` variant without emitting the theme again. Every rule sits in
`@layer components`, so a utility a screen puts on the component (a width, a weight) still wins. Tailwind
utilities are for laying out a screen, in its template.

Colours that differ by theme are written once, with `light-dark()`, which needs a browser from 2024 or later
(Chrome 123, Firefox 120, Safari 17.5).

The layout has one breakpoint, 640px of the app's own width (a container query, not the window's): styles are
written for the narrow layout first, and `wide:` applies from 640px up.

## Fonts

Inter, Bricolage Grotesque and Pixelify Sans, each under the SIL Open Font License 1.1, are served by the app
itself from the `@fontsource-variable` packages: no request leaves for a font host. A browser fetches only the
subsets a page uses, about 138 kB for the three Latin files.

## Translations

`packages/app/src/app/i18n/fr.json` and `en.json`, with the same keys (a test checks it). A component of `ui`
holds no sentence: its words are inputs. No sentence is
written in a template: lint refuses a text node that is not an expression, and a literal in an attribute a person
reads or hears (`aria-label`, `placeholder`, `title`, `alt`...). A language whose file cannot be fetched gives
way to English, and is only remembered once it has loaded. Before sign-in the language
is the browser's, or the one chosen on the page, which the browser keeps.

## API types

`openapi.json` is a copy of what the API serves at `GET /api/v1/openapi.json`, and
`packages/data/src/lib/models/api-types.ts` is generated from it. The CI fails when either is stale. After a
change to the API, with the app running on port 8080:

```bash
curl -fsS http://localhost:8080/api/v1/openapi.json -o openapi.json
npm run gen:api
```
