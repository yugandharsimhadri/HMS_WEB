import { describeCombo, useShortcutList } from './hotkeys';

/**
 * The row of keycaps at the foot of a screen.
 *
 * The sheet behind `?` is the complete reference; this is the reminder that
 * makes somebody look for it. A shortcut nobody can find is not a feature,
 * and the surest way to be found is to be visible where the work happens.
 *
 * Only keys that are genuinely bound right now are drawn. That matters on
 * screens where a control comes and goes: Appointments only mounts the
 * patient picker on its booking tab, so F3 is real there and not on the
 * others — and a strip that advertised it anyway would be teaching people a
 * key that does nothing. Pass combos rather than rendered text and the same
 * key is always described the same way here as in the sheet, Ctrl on
 * Windows and Cmd on a Mac, decided in one place.
 */
export function ShortcutHints({ keys }: { keys: [combo: string, label: string][] }) {
  const live = useShortcutList();
  const bound = new Set(live.map((s) => s.combo));

  const shown = keys.filter(([combo]) =>
    combo.split(' ').every((c) => bound.has(c.toLowerCase())),
  );

  if (shown.length === 0) return null;

  return (
    <p className="shortcut-hints">
      {shown.map(([combo, label]) => (
        <span key={combo + label}>
          {combo.split(' ').map((c) => (
            <span className="kbd" key={c}>{describeCombo(c)}</span>
          ))}
          {label}
        </span>
      ))}
      <span><span className="kbd">?</span>all shortcuts</span>
    </p>
  );
}
