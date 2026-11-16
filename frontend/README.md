# Frontend

Angular 22 (signals, zoneless and OnPush as the framework now defaults), Tailwind 4, Transloco for French and English, Vitest.
Split into packages under `packages/`: `app` (the shell and its screens), `ui` (the design tokens and the base
components), `util` (the display settings) and `data` (the API's types).

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

## Design tokens

`packages/ui/src/styles/tokens.css` holds every token as a CSS custom property (`--rs-*`), read at run time.
Two attributes on the root element switch them, and nothing else changes:

| Attribute        | Values            | Default                         |
| ---------------- | ----------------- | ------------------------------- |
| `data-theme`     | `sombre`, `clair` | absent: the system's preference |
| `data-character` | `pixel`, `sobre`  | `pixel`                         |

`theme.css` maps Tailwind's names onto them with `@theme inline`, so a utility carries the variable itself
(`.bg-surface { background-color: var(--rs-surface) }`) and follows a switch. Tailwind's default palette, radii
and type scale are removed: a component names a semantic token (`bg-surface`, `rounded-card`, `type-body`),
never a hex value or a palette step.

Colours that differ by theme are written once, with `light-dark()`, which needs a browser from 2024 or later
(Chrome 123, Firefox 120, Safari 17.5).

The layout has one breakpoint, 640px of the app's own width (a container query, not the window's): styles are
written for the narrow layout first, and `wide:` applies from 640px up.

## Fonts

Inter, Bricolage Grotesque and Pixelify Sans, each under the SIL Open Font License 1.1, are served by the app
itself from the `@fontsource-variable` packages: no request leaves for a font host. A browser fetches only the
subsets a page uses, about 138 kB for the three Latin files.

## Translations

`packages/app/src/app/i18n/fr.json` and `en.json`, with the same keys (a test checks it). No sentence is
written in a template: a lint rule refuses a text node that is not an expression. Before sign-in the language
is the browser's, or the one chosen on the page, which the browser keeps.

## API types

`openapi.json` is a copy of what the API serves at `GET /api/v1/openapi.json`, and
`packages/data/src/lib/models/api-types.ts` is generated from it. The CI fails when either is stale. After a
change to the API, with the app running on port 8080:

```bash
curl -fsS http://localhost:8080/api/v1/openapi.json -o openapi.json
npm run gen:api
```
