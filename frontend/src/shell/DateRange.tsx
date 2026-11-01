import { localDate, type Range } from './dates';

/** The ranges people actually ask for, so most of the time nobody types a date. */
function presets(): { label: string; range: Range }[] {
  const now = new Date();
  const day = (offset: number) => { const d = new Date(now); d.setDate(d.getDate() + offset); return d; };
  const today = localDate(now);

  // The week starts on Monday, as it does on an Indian calendar.
  const weekStart = day(-((now.getDay() + 6) % 7));
  const monthStart = new Date(now.getFullYear(), now.getMonth(), 1);
  const lastMonthStart = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  const lastMonthEnd = new Date(now.getFullYear(), now.getMonth(), 0);

  return [
    { label: 'Today', range: { from: today, to: today } },
    { label: 'Yesterday', range: { from: localDate(day(-1)), to: localDate(day(-1)) } },
    { label: 'This week', range: { from: localDate(weekStart), to: today } },
    { label: 'This month', range: { from: localDate(monthStart), to: today } },
    { label: 'Last month', range: { from: localDate(lastMonthStart), to: localDate(lastMonthEnd) } },
  ];
}

/**
 * From and To, with one-click presets. The same control on the dashboard and every
 * report, so a range reads and is chosen the same way everywhere.
 */
export function DateRange({ value, onChange }: { value: Range; onChange: (range: Range) => void }) {
  const options = presets();
  // Early in a month "Today" and "This month" can be the same dates; only the first lights up.
  const active = options.findIndex((p) => p.range.from === value.from && p.range.to === value.to);

  return (
    <span className="date-range">
      {options.map((p, i) => (
        <button
          key={p.label}
          type="button"
          className={i === active ? 'tab active' : 'tab'}
          onClick={() => onChange(p.range)}
        >
          {p.label}
        </button>
      ))}
      <label>From</label>
      <input type="date" value={value.from} max={value.to || undefined}
             onChange={(e) => onChange({ ...value, from: e.target.value })} />
      <label>To</label>
      <input type="date" value={value.to} min={value.from || undefined}
             onChange={(e) => onChange({ ...value, to: e.target.value })} />
    </span>
  );
}

