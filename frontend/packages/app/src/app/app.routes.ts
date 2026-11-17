import type { Routes } from '@angular/router';
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
  {
    // The proof of the design tokens and of the base components: no data, so no session.
    path: 'foundations',
    loadComponent: () => import('./pages/foundations/foundations').then((m) => m.Foundations),
  },
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
    ],
  },
  {
    path: '**',
    loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFound),
  },
];
