// @ts-check
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

/**
 * Each package may import the *public* surface of the packages below it, and nothing else.
 * Anything absent from a list is an error, so adding an edge is a deliberate edit here.
 */
const MAY_IMPORT = {
  util: [],
  data: [],
  ui: ['util'],
  app: ['ui', 'util', 'data'],
};

/** Packages that hold screens: only these have a `pages/` to protect from their `components/`. */
const HAS_PAGES = ['app'];

const PACKAGES = Object.keys(MAY_IMPORT);

function restrictedImports(pkg, extra = []) {
  const foreign = PACKAGES.filter((p) => p !== pkg && !MAY_IMPORT[pkg].includes(p));
  return {
    '@typescript-eslint/no-restricted-imports': [
      'error',
      {
        patterns: [
          {
            group: [...foreign.map((p) => `@respawn/${p}`), `@respawn/${pkg}`],
            message:
              `packages/${pkg} may import only ${MAY_IMPORT[pkg].join(', ') || 'nothing'}. ` +
              'Its own package is reached through the @' +
              pkg +
              '/ alias, not through its own barrel.',
          },
          {
            group: PACKAGES.filter((p) => p !== pkg).map((p) => `@${p}/*`),
            message:
              "The @x/ aliases are one package's own inside. Another package is reached through " +
              'its @respawn/x barrel, which is what decides what it exposes.',
          },
          {
            group: ['**/packages/*/src/**'],
            message:
              'A relative path into another package walks past its public-api.ts. Import the ' +
              '@respawn/x barrel instead.',
          },
          ...extra,
        ],
      },
    ],
  };
}

/** Pages compose components. The reverse makes the two indistinguishable again. */
const NO_PAGE_FROM_COMPONENT = {
  group: ['**/pages/**', '@*/pages/*'],
  message: 'A component must not import a page; pages compose components, never the reverse.',
};

module.exports = tseslint.config(
  {
    // Build outputs, generated code and this file are not linted.
    ignores: [
      'dist/**',
      'out-tsc/**',
      '.angular/**',
      'coverage/**',
      'packages/data/src/lib/models/api-types.ts',
      // Not covered by any tsconfig, so the type-aware parser cannot resolve it.
      'eslint.config.js',
    ],
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      // Type-aware: floating promises, misused promises and unsafe `any` need the type
      // checker. projectService resolves each file's owning tsconfig.
      ...tseslint.configs.recommendedTypeChecked,
      ...angular.configs.tsRecommended,
    ],
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: __dirname,
      },
    },
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'rs', style: 'camelCase' },
      ],
      // One prefix. A component is an element (`rs-card`) or, when it must stay the native
      // element, an attribute on it (`button[rsButton]`).
      '@angular-eslint/component-selector': [
        'error',
        [
          { type: 'element', prefix: 'rs', style: 'kebab-case' },
          { type: 'attribute', prefix: 'rs', style: 'camelCase' },
        ],
      ],
      // Underscore-prefixed names are intentionally unused.
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrorsIgnorePattern: '^_' },
      ],
    },
  },
  {
    // Specs drive Angular test APIs that are typed `any` at the boundary
    // (fixture.nativeElement, JSON files). Source code carries the type-aware rules;
    // asserting them here would mean a cast at every call into the framework.
    files: ['**/*.spec.ts'],
    rules: {
      '@typescript-eslint/no-unsafe-assignment': 'off',
      '@typescript-eslint/no-unsafe-member-access': 'off',
      '@typescript-eslint/no-unsafe-call': 'off',
      '@typescript-eslint/no-unsafe-argument': 'off',
      '@typescript-eslint/await-thenable': 'off',
    },
  },
  ...PACKAGES.map((pkg) => ({
    files: [`packages/${pkg}/**/*.ts`],
    rules: restrictedImports(pkg),
  })),
  // Repeats each package's patterns rather than adding to them: in flat config a later block
  // setting the same rule replaces it outright for the files it matches.
  ...HAS_PAGES.map((pkg) => ({
    files: [`packages/${pkg}/**/components/**/*.ts`],
    rules: restrictedImports(pkg, [NO_PAGE_FROM_COMPONENT]),
  })),
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    rules: {
      // `x != null` is the intentional "neither null nor undefined" check in templates.
      '@angular-eslint/template/eqeqeq': ['error', { allowNullOrUndefined: true }],
      // No text written in a template: every sentence comes from the translation files.
      // This is Angular's own i18n rule with its id and attribute checks turned off, which
      // leaves the part that matters with Transloco: a text node that is not an expression.
      '@angular-eslint/template/i18n': [
        'error',
        { checkId: false, checkText: true, checkAttributes: false },
      ],
    },
  },
  {
    // Not screens: the document the app is mounted in, whose text is read before any
    // translation can load, and the small host templates the specs write for themselves.
    files: ['packages/app/src/index.html', '**/*.spec.ts/*.html'],
    rules: {
      '@angular-eslint/template/i18n': 'off',
    },
  },
);
