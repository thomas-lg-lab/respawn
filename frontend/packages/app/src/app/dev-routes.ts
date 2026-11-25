import type { Routes } from '@angular/router';

/**
 * The screens of the development build only: the production build replaces this file with
 * `dev-routes.production.ts` (angular.json), so they are not in the image at all.
 */
export const devRoutes: Routes = [
  {
    // The proof of the design tokens and of the base components: no data, so no session.
    path: 'foundations',
    loadComponent: () => import('./pages/foundations/foundations').then((m) => m.Foundations),
  },
];
