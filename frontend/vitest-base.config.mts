import { type Plugin, defineConfig } from 'vitest/config';

/* What the unit-test builder's Vitest run takes from here (angular.json, `runnerConfig`): the
   builder still compiles the specs and decides which files are tests. */

const ICONS = /[\\/]node_modules[\\/]@lucide[\\/]angular[\\/]/;

/** An icon's component, as the package declares it: `static ɵcmp = i0.ɵɵngDeclareComponent({…});`. */
const DECLARED = /static ɵcmp = (i0\.ɵɵngDeclareComponent\(\{[\s\S]*?\n\t\}\));/g;
const TYPE = /\btype: (\w+),/;

/**
 * Compiles an icon when it is first drawn, not when the package is imported.
 *
 * The icon package is one module of some two thousand components, published half compiled: the
 * app's build finishes them and keeps the ones it uses, but a spec imports the package as it is,
 * and each declaration is compiled as the module loads. Every worker that starts the app paid
 * that, about nine seconds on a free core, for the handful of icons the screens draw.
 *
 * Here each declaration becomes a getter that compiles on first read and then is the value,
 * which is how a component written in a spec behaves. Should the package change its shape, this
 * stops with an error rather than let the run become slow again without a word.
 */
function lazyIcons(): Plugin {
  return {
    name: 'respawn:lazy-icons',
    transform(code, id) {
      if (!ICONS.test(id)) return null;

      let icons = 0;
      const lazy = code.replace(DECLARED, (whole, declared: string) => {
        const type = TYPE.exec(declared)?.[1];
        if (!type) return whole;
        icons++;
        return (
          `static get ɵcmp() { const def = ${declared}; ` +
          `Object.defineProperty(${type}, 'ɵcmp', ` +
          `{ value: def, writable: true, enumerable: true, configurable: true }); return def; }`
        );
      });
      if (!icons) {
        throw new Error(
          `No icon declaration found in ${id}: the icon package no longer has the shape ` +
            'vitest-base.config.mts expects. Adapt lazyIcons() to it, or remove it.',
        );
      }
      return { code: lazy, map: null };
    },
  };
}

export default defineConfig({
  plugins: [lazyIcons()],
  test: {
    // A package of node_modules is loaded by Node, past the plugins, unless it is named here.
    server: { deps: { inline: [ICONS] } },
  },
});
