/** yyyy-MM-dd in the clinic's own time - what a date input holds. */
export const localDate = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

export const todayInput = () => localDate(new Date());

export interface Range { from: string; to: string }

/** "31 Oct 2026", or "1 Oct – 31 Oct 2026" for a range. */
export function rangeLabel({ from, to }: Range) {
  const f = new Date(`${from}T00:00`);
  const t = new Date(`${to}T00:00`);
  const long: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short', year: 'numeric' };
  if (from === to) return f.toLocaleDateString(undefined, long);
  const short: Intl.DateTimeFormatOptions = f.getFullYear() === t.getFullYear()
    ? { day: 'numeric', month: 'short' } : long;
  return `${f.toLocaleDateString(undefined, short)} – ${t.toLocaleDateString(undefined, long)}`;
}
