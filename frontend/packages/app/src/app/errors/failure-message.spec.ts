import { ApiFailure } from '@respawn/data';
import en from '@app/i18n/en.json';
import fr from '@app/i18n/fr.json';
import { KNOWN_FAILURES, failureKey } from './failure-message';

describe('failureKey', () => {
  it('names the sentence of a refusal the app has words for', () => {
    expect(failureKey(new ApiFailure(401, 'invalid_credentials', ''))).toBe(
      'errors.invalid_credentials',
    );
  });

  it('gives the general sentence to anything unforeseen', () => {
    expect(failureKey(new ApiFailure(418, 'teapot', ''))).toBe('errors.unknown');
    expect(failureKey(new Error('boom'))).toBe('errors.unknown');
    expect(failureKey(null)).toBe('errors.unknown');
  });

  it('has a sentence in both languages for every refusal it names', () => {
    for (const file of [fr, en]) {
      const sentences: Record<string, string> = file.errors;
      for (const code of [...KNOWN_FAILURES, 'unknown']) {
        expect(sentences[code], code).toBeTruthy();
      }
      // And no sentence that nothing would ever show.
      expect(Object.keys(sentences).sort()).toEqual([...KNOWN_FAILURES, 'unknown'].sort());
    }
  });
});
