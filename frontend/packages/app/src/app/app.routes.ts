import type { Routes } from '@angular/router';
import { devRoutes } from './dev-routes';
import { signedInOnly, signedOutOnly } from './session/session.guards';

/**
 * The screens. Each is a chunk of its own, fetched when first shown. The API process serves
 * the document for every one of these addresses, so a reload or a shared link lands on the
 * same screen.
 */
export const routes: Routes = [
  {
    path: 'sign-in',
    canActivate: [signedOutOnly],
    loadComponent: () => import('./pages/sign-in/sign-in').then((m) => m.SignIn),
  },
  ...devRoutes,
  {
    path: '',
    canActivate: [signedInOnly],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./pages/home/home').then((m) => m.Home),
      },
      {
        path: 'library',
        loadComponent: () => import('./pages/library/library').then((m) => m.Library),
      },
      {
        path: 'time',
        loadComponent: () => import('./pages/time/time').then((m) => m.Time),
      },
      {
        // A game's page: reached from a game wherever one is shown, never from a tab.
        path: 'games/:id',
        loadComponent: () => import('./pages/game/game').then((m) => m.Game),
      },
      {
        // Reached from the account menu, not from a tab.
        path: 'account',
        loadComponent: () => import('./pages/account/account').then((m) => m.Account),
      },
    ],
  },
  {
    path: '**',
    loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFound),
  },
];
