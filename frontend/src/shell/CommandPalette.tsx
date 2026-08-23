import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { Patient } from '../api/types';
import { describeCombo } from './hotkeys';

/**
 * Ctrl K.
 *
 * The point of this screen is that the front desk never has to navigate to
 * find something. A receptionist with a patient in front of her types the
 * name and presses Enter — she does not first go to Patients, then search,
 * then click. Modules, actions and the patient register are all one list.
 *
 * Patients are fetched live because a clinic has thousands of them and there
 * is nothing to pre-load; modules and actions are matched locally.
 */

export interface Destination {
  label: string;
  to: string;
  group: string;
  /** Extra words that should match this row without being displayed. */
  keywords?: string;
}

export interface PaletteAction {
  label: string;
  group: string;
  run: () => void;
  combo?: string;
}

interface Row {
  key: string;
  label: string;
  sub?: string;
  group: string;
  combo?: string;
  run: () => void;
}

const MIN_PATIENT_QUERY = 2;

export function CommandPalette({
  open,
  onClose,
  destinations,
  actions,
}: {
  open: boolean;
  onClose: () => void;
  destinations: Destination[];
  actions: PaletteAction[];
}) {
  const navigate = useNavigate();
  const [query, setQuery] = useState('');
  const [index, setIndex] = useState(0);
  const [patients, setPatients] = useState<Patient[]>([]);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);

  // Reopening should always start clean: a stale query from twenty minutes
  // ago is never what the next person wants.
  useEffect(() => {
    if (open) {
      setQuery('');
      setIndex(0);
      setPatients([]);
      // Focus after paint, or the browser hands it back to whatever had it.
      requestAnimationFrame(() => inputRef.current?.focus());
    }
  }, [open]);

  // Live patient search, debounced. The guard on length keeps a single
  // keystroke from asking the server for a fifth of the register.
  useEffect(() => {
    if (!open) return;

    const term = query.trim();
    if (term.length < MIN_PATIENT_QUERY) {
      setPatients([]);
      return;
    }

    let cancelled = false;
    const timer = window.setTimeout(() => {
      void api
        .get<Patient[]>(`/api/patients?term=${encodeURIComponent(term)}&take=6`)
        .then((found) => {
          if (!cancelled) setPatients(found);
        })
        .catch(() => {
          // A failed lookup must not empty the modules underneath it.
          if (!cancelled) setPatients([]);
        });
    }, 180);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [query, open]);

  const rows = useMemo<Row[]>(() => {
    const q = query.trim().toLowerCase();
    const match = (text: string) => text.toLowerCase().includes(q);

    const out: Row[] = [];

    for (const d of destinations) {
      if (q && !match(d.label) && !match(d.keywords ?? '')) continue;
      out.push({
        key: `nav:${d.to}`,
        label: d.label,
        group: d.group,
        run: () => navigate(d.to),
      });
    }

    for (const a of actions) {
      if (q && !match(a.label)) continue;
      out.push({
        key: `act:${a.label}`,
        label: a.label,
        group: a.group,
        combo: a.combo,
        run: a.run,
      });
    }

    for (const p of patients) {
      out.push({
        key: `pat:${p.id}`,
        label: p.name,
        sub: `${p.patientNo} · ${p.age}${p.gender.charAt(0)}${p.phone ? ` · ${p.phone}` : ''}`,
        group: 'Patients',
        run: () => navigate(`/patients?focus=${p.id}`),
      });
    }

    return out;
  }, [query, destinations, actions, patients, navigate]);

  // Clamp rather than reset: results change on every keystroke, and sending
  // the highlight back to the top each time makes Enter unpredictable.
  useEffect(() => {
    setIndex((i) => (rows.length === 0 ? 0 : Math.min(i, rows.length - 1)));
  }, [rows.length]);

  const choose = useCallback(
    (row: Row | undefined) => {
      if (!row) return;
      onClose();
      row.run();
    },
    [onClose],
  );

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setIndex((i) => (rows.length ? (i + 1) % rows.length : 0));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setIndex((i) => (rows.length ? (i - 1 + rows.length) % rows.length : 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      choose(rows[index]);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      onClose();
    }
  };

  // Keep the highlighted row in view when arrowing past the fold.
  useEffect(() => {
    const el = listRef.current?.querySelector<HTMLElement>('[aria-selected="true"]');
    el?.scrollIntoView({ block: 'nearest' });
  }, [index]);

  if (!open) return null;

  let lastGroup = '';

  return (
    <div className="cmdk-backdrop" onMouseDown={onClose} role="presentation">
      <div
        className="cmdk"
        role="dialog"
        aria-modal="true"
        aria-label="Command palette"
        onMouseDown={(e) => e.stopPropagation()}
      >
        <input
          ref={inputRef}
          className="cmdk-input"
          placeholder="Search patients, or jump to a screen…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={onKeyDown}
          aria-label="Search"
        />

        <div className="cmdk-list" ref={listRef} role="listbox">
          {rows.length === 0 && (
            <p className="cmdk-empty">
              {query.trim().length >= MIN_PATIENT_QUERY
                ? `Nothing matches "${query.trim()}".`
                : 'Type to search.'}
            </p>
          )}

          {rows.map((row, i) => {
            const header = row.group !== lastGroup ? row.group : null;
            lastGroup = row.group;
            return (
              <div key={row.key}>
                {header && <div className="cmdk-group">{header}</div>}
                <div
                  className="cmdk-item"
                  role="option"
                  aria-selected={i === index}
                  onMouseEnter={() => setIndex(i)}
                  onClick={() => choose(row)}
                >
                  <span>{row.label}</span>
                  {row.sub && <span className="cmdk-sub">{row.sub}</span>}
                  {row.combo && <span className="kbd">{describeCombo(row.combo)}</span>}
                </div>
              </div>
            );
          })}
        </div>

        <div className="cmdk-foot">
          <span><span className="kbd">Up</span><span className="kbd">Down</span> move</span>
          <span><span className="kbd">Enter</span> open</span>
          <span><span className="kbd">Esc</span> close</span>
        </div>
      </div>
    </div>
  );
}
