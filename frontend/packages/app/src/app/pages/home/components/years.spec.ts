import { yearRuns } from './years';

describe('the years named in a caption', () => {
  it('joins the years that follow each other into a run', () => {
    expect(yearRuns([2024])).toBe('2024');
    expect(yearRuns([2022, 2023, 2024, 2025])).toBe('2022–2025');
    expect(yearRuns([2022, 2024, 2025, 2027])).toBe('2022, 2024–2025, 2027');
  });

  it('puts them in order first', () => {
    expect(yearRuns([2025, 2022, 2023])).toBe('2022–2023, 2025');
  });

  it('says nothing of no year', () => {
    expect(yearRuns([])).toBe('');
  });
});
