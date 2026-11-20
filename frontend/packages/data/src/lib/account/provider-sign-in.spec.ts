import { providerOutcome, providerSignInAddress } from './provider-sign-in';

describe('sign-in through a provider', () => {
  it('starts at the API, with the address to come back to', () => {
    expect(providerSignInAddress('sso', '/sign-in?returnTo=%2Flibrary')).toBe(
      '/api/v1/auth/oidc/sso?return_to=%2Fsign-in%3FreturnTo%3D%252Flibrary',
    );
    expect(providerSignInAddress('a/b', '/')).toBe('/api/v1/auth/oidc/a%2Fb?return_to=%2F');
  });

  it('reads the outcome the API wrote, and a word it does not know as a failure', () => {
    expect(providerOutcome('linked')).toBe('linked');
    expect(providerOutcome('unavailable')).toBe('unavailable');
    expect(providerOutcome('something-new')).toBe('failed');
    expect(providerOutcome('')).toBeNull();
    expect(providerOutcome(null)).toBeNull();
  });
});
