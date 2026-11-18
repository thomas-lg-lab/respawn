import { MutationCache, QueryCache, QueryClient } from '@tanstack/angular-query';
import { NOT_SIGNED_IN, closeSession } from './account/account.queries';
import { ApiFailure } from './api/api';

/**
 * The server state of the app: what the API answered, kept and shared between screens.
 *
 * It is also where the app learns that the session ended. Any request the API refuses for
 * want of a session, whatever asked it, closes the session as the front knows it, then
 * calls `sessionEnded`: the app says there where that leads (the sign-in).
 */
export function serverState(sessionEnded: () => void = () => undefined): QueryClient {
  const refused = (failure: unknown): void => {
    if (failure instanceof ApiFailure && failure.code === NOT_SIGNED_IN) {
      closeSession(client);
      sessionEnded();
    }
  };
  const client = new QueryClient({
    queryCache: new QueryCache({ onError: refused }),
    mutationCache: new MutationCache({ onError: refused }),
  });
  return client;
}
