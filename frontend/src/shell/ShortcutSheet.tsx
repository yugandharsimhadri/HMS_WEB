import { describeCombo, useShortcutList } from './hotkeys';

/**
 * The sheet behind `?`.
 *
 * Generated from the live registry rather than written by hand, so it can
 * only ever show shortcuts that genuinely exist — and it changes as you move
 * between screens, because the page-level keys change with it. A shortcut
 * nobody can find is not a feature, and a printed list that has drifted out
 * of date is worse than none.
 */
export function ShortcutSheet({ onClose }: { onClose: () => void }) {
  const shortcuts = useShortcutList();

  const groups = shortcuts.reduce<Record<string, typeof shortcuts>>((acc, s) => {
    (acc[s.group] ||= []).push(s);
    return acc;
  }, {});

  return (
    <div className="overlay" onMouseDown={onClose} role="presentation">
      <div
        className="overlay-card"
        role="dialog"
        aria-modal="true"
        aria-label="Keyboard shortcuts"
        onMouseDown={(e) => e.stopPropagation()}
      >
        <div className="overlay-head">
          <h2>Keyboard shortcuts</h2>
          <button type="button" className="ghost" onClick={onClose}>Close</button>
        </div>

        <div className="overlay-body">
          {Object.keys(groups).length === 0 ? (
            <p className="hint">No shortcuts are active on this screen.</p>
          ) : (
            <div className="sheet">
              {Object.entries(groups).map(([group, items]) => (
                <section key={group}>
                  <h4>{group}</h4>
                  <dl>
                    {items.map((s) => (
                      <div className="sheet-row" key={s.combo}>
                        <span>{s.label}</span>
                        <span className="kbd">{describeCombo(s.combo)}</span>
                      </div>
                    ))}
                  </dl>
                </section>
              ))}
            </div>
          )}
          <p className="hint">
            Shortcuts stay out of the way while you are typing in a field — only
            Escape and the command palette work there.
          </p>
        </div>
      </div>
    </div>
  );
}
