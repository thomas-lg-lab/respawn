import type { LibraryEntry, LibraryGame } from '@respawn/data';

let made = 0;

/** A game of a library as the API gives one, never launched unless `values` say otherwise. */
export function game(name: string | null, values: Partial<LibraryGame> = {}): LibraryGame {
  return {
    game_id: `00000000-0000-4000-8000-${String(++made).padStart(12, '0')}`,
    name,
    playtime_seconds: 0,
    recent_playtime_seconds: 0,
    last_played_at: null,
    entries: [],
    cover_source: null,
    ...values,
  };
}

/** A copy of a game on Steam, as the API gives one: never launched, still listed. */
export function entry(values: Partial<LibraryEntry> = {}): LibraryEntry {
  return {
    store_entry_id: 'e',
    linked_account_id: 'a',
    source: 'steam',
    external_id: '620',
    playtime_seconds: 0,
    recent_playtime_seconds: 0,
    last_played_at: null,
    first_seen_at: '2026-11-18T10:05:00Z',
    as_of: '2026-11-20T04:02:05Z',
    missing_since: null,
    ...values,
  };
}

/** Noon, that many days back on the calendar: a clock change cannot move it to another day. */
export function ago(days: number): string {
  const moment = new Date();
  moment.setDate(moment.getDate() - days);
  moment.setHours(12, 0, 0, 0);
  return moment.toISOString();
}
