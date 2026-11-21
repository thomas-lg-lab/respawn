import {
  type LinkedAccount,
  type SyncRun,
  TEST_LINKED_ACCOUNT,
  TEST_SYNC_RUN,
} from '@respawn/data';
import {
  STALE_AFTER,
  accountInView,
  syncErrorKey,
  syncNotice,
  syncView,
  syncsWord,
} from './sync-view';

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

const JUST_LINKED = {
  ...account({ last: null, last_succeeded_at: null }),
  linked_at: '2026-11-21T11:59:00Z',
};
/** Linked days ago and never read: its first reading left nothing behind. */
const NEVER_READ = account({ last: null, last_succeeded_at: null }, 'never-read');
const FIRST_FAILED = account({
  last: run({ outcome: 'failed', error: 'refused' }),
  last_succeeded_at: null,
});
const FIRST_EMPTY = account({ last: run({ outcome: 'nothing_visible' }), last_succeeded_at: null });

describe('syncView', () => {
  it('leads to the link while nothing is linked', () => {
    expect(syncView([], false, NOW)).toBe('no-account');
  });

  it('shows the library once a reading succeeded, whatever happened since', () => {
    expect(syncView([account()], false, NOW)).toBe('ready');
    expect(syncView([account({ running: true })], false, NOW)).toBe('ready');
    expect(
      syncView([account({ last: run({ outcome: 'failed', error: 'unavailable' }) })], false, NOW),
    ).toBe('ready');
    expect(syncView([account()], true, NOW)).toBe('ready');
  });

  it('waits for the first reading of an account just linked', () => {
    expect(syncView([JUST_LINKED], false, NOW)).toBe('first-sync');
    expect(
      syncView([{ ...JUST_LINKED, sync: { ...JUST_LINKED.sync, running: true } }], false, NOW),
    ).toBe('first-sync');
  });

  it('stops waiting for a first reading that left nothing behind', () => {
    expect(syncView([NEVER_READ], false, NOW)).toBe('first-sync-late');
    // Asked from here, or running after all: it waits again.
    expect(syncView([NEVER_READ], true, NOW)).toBe('first-sync');
    expect(
      syncView([{ ...NEVER_READ, sync: { ...NEVER_READ.sync, running: true } }], false, NOW),
    ).toBe('first-sync');
  });

  it('waits again while a first reading is tried once more', () => {
    expect(syncView([FIRST_FAILED], true, NOW)).toBe('first-sync');
  });

  it('tells a first reading that failed from one that showed nothing', () => {
    expect(syncView([FIRST_FAILED], false, NOW)).toBe('sync-failed');
    expect(syncView([FIRST_EMPTY], false, NOW)).toBe('nothing-visible');
  });
});

describe('syncsWord', () => {
  it('changes when a sync starts or ends, and only then', () => {
    const before = syncsWord([account()]);

    expect(syncsWord([account()])).toBe(before);
    expect(syncsWord([account({ running: true })])).not.toBe(before);
    expect(syncsWord([account({ last: run({ id: 'another-run' }) })])).not.toBe(before);
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

  it('says the first reading of an account just linked is on its way, while it is awaited', () => {
    expect(syncNotice(JUST_LINKED, false, NOW)).toBe('running');
    expect(syncNotice(NEVER_READ, false, NOW)).toBeNull();
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

  it('is then an account never read', () => {
    const empty = account({ last: run({ outcome: 'nothing_visible' }) }, 'empty');

    expect(accountInView([account(), empty, NEVER_READ])?.id).toBe('never-read');
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
