import { MutationCache, QueryCache, QueryClient } from '@tanstack/angular-query';
import { accountKeys, closeSession, saysNoSession } from './account/account.queries';

/**
 * The server state of the app: what the API answered, kept and shared between screens.
 *
 * It is also where the app learns that the session ended. A request refused for want of a
 * session, whatever asked it, closes the session as the front knows it, then calls
 * `sessionEnded`: the app says there where that leads (the sign-in).
 *
 * Once: the requests that were on their way when the session ended are refused one after
 * the other, and the later ones must not empty what the sign-in screen has asked since.
 */
export function serverState(sessionEnded: () => void = () => undefined): QueryClient {
  const refused = (failure: unknown): void => {
    const alreadyClosed = client.getQueryData(accountKeys.me) === null;
    if (saysNoSession(failure) && !alreadyClosed) {
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
