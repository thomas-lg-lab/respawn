import {
  type LinkedAccount,
  type SyncRun,
  TEST_LINKED_ACCOUNT,
  TEST_SYNC_RUN,
} from '@respawn/data';
import { STALE_AFTER, accountInView, syncErrorKey, syncNotice, syncView } from './sync-view';

const NOW = Date.parse('2026-11-21T12:00:00Z');
const THIS_MORNING = '2026-11-21T04:02:05Z';

function run(changes: Partial<SyncRun> = {}): SyncRun {
  return { ...TEST_SYNC_RUN, finished_at: THIS_MORNING, ...changes };
}

function account(sync: Partial<LinkedAccount['sync']> = {}, id = TEST_LINKED_ACCOUNT.id) {
  return {
    ...TEST_LINKED_ACCOUNT,
    id,
    sync: { running: false, last: run(), last_succeeded_at: THIS_MORNING, ...sync },
  } satisfies LinkedAccount;
}

const JUST_LINKED = account({ last: null, last_succeeded_at: null });
const FIRST_FAILED = account({
  last: run({ outcome: 'failed', error: 'refused' }),
  last_succeeded_at: null,
});
const FIRST_EMPTY = account({ last: run({ outcome: 'nothing_visible' }), last_succeeded_at: null });

describe('syncView', () => {
  it('leads to the link while nothing is linked', () => {
    expect(syncView([], false)).toBe('no-account');
  });

  it('shows the library once a reading succeeded, whatever happened since', () => {
    expect(syncView([account()], false)).toBe('ready');
    expect(syncView([account({ running: true })], false)).toBe('ready');
    expect(
      syncView([account({ last: run({ outcome: 'failed', error: 'unavailable' }) })], false),
    ).toBe('ready');
    expect(syncView([account()], true)).toBe('ready');
  });

  it('waits for the first reading of an account just linked', () => {
    expect(syncView([JUST_LINKED], false)).toBe('first-sync');
    expect(
      syncView([{ ...JUST_LINKED, sync: { ...JUST_LINKED.sync, running: true } }], false),
    ).toBe('first-sync');
  });

  it('waits again while a first reading is tried once more', () => {
    expect(syncView([FIRST_FAILED], true)).toBe('first-sync');
  });

  it('tells a first reading that failed from one that showed nothing', () => {
    expect(syncView([FIRST_FAILED], false)).toBe('sync-failed');
    expect(syncView([FIRST_EMPTY], false)).toBe('nothing-visible');
  });
});

describe('syncNotice', () => {
  it('says nothing of a reading that went well this morning', () => {
    expect(syncNotice(account(), false, NOW)).toBeNull();
  });

  it('says a sync is running, asked from here or not', () => {
    expect(syncNotice(account({ running: true }), false, NOW)).toBe('running');
    expect(syncNotice(account(), true, NOW)).toBe('running');
  });

  it('says the last reading brought nothing', () => {
    expect(syncNotice(account({ last: run({ outcome: 'failed' }) }), false, NOW)).toBe('failed');
    expect(syncNotice(account({ last: run({ outcome: 'nothing_visible' }) }), false, NOW)).toBe(
      'nothing-visible',
    );
  });

  it('points out a last success older than two days', () => {
    const at = (age: number) => new Date(NOW - age).toISOString();

    expect(
      syncNotice(account({ last_succeeded_at: at(STALE_AFTER - 1000) }), false, NOW),
    ).toBeNull();
    expect(syncNotice(account({ last_succeeded_at: at(STALE_AFTER + 1000) }), false, NOW)).toBe(
      'stale',
    );
  });
});

describe('accountInView', () => {
  it('is the account whose last reading went wrong, when there is one', () => {
    const troubled = account({ last: run({ outcome: 'failed' }) }, 'troubled');

    expect(accountInView([account(), troubled])?.id).toBe('troubled');
  });

  it('is otherwise the account read the longest ago', () => {
    const older = account({ last_succeeded_at: '2026-11-19T04:02:05Z' }, 'older');

    expect(accountInView([account(), older])?.id).toBe('older');
    expect(accountInView([])).toBeUndefined();
  });
});

describe('syncErrorKey', () => {
  it('names the sentence of why a run failed', () => {
    expect(syncErrorKey(run({ outcome: 'failed', error: 'unavailable' }))).toBe(
      'home.syncErrors.unavailable',
    );
  });

  it('tells a refused key of the user from the instance’s', () => {
    const refused = run({ outcome: 'failed', error: 'refused' });

    expect(syncErrorKey(refused)).toBe('home.syncErrors.refused');
    expect(syncErrorKey({ ...refused, own_key: true })).toBe('home.syncErrors.refused_own_key');
  });

  it('gives the general sentence to a reason it does not know', () => {
    expect(syncErrorKey(run({ outcome: 'failed', error: 'something_new' }))).toBe(
      'home.syncErrors.unknown',
    );
    expect(syncErrorKey(null)).toBe('home.syncErrors.unknown');
  });
});
