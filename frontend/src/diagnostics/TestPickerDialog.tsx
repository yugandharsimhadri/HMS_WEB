import { useCallback, useEffect, useState } from 'react';
import { api } from '../api/client';
import type { DiagnosticTest } from '../api/types';
import { useModalBehaviour } from '../shell/useModalBehaviour';

interface Props {
  patientName: string;
  /** Ids already on the bill. Those tests are left out of the results
   * entirely rather than shown with a spent button. */
  billedTestIds: string[];
  /** Running figures, read off the bill behind this dialog rather than
   * counted here, so they can never disagree with it once this closes. */
  addedCount: number;
  addedTotal: number;
  onAdd: (test: DiagnosticTest) => void;
  onDone: () => void;
}

/**
 * Every active test, searchable, added straight to the bill behind this as
 * each one is picked. Deliberately stays open across several picks — a bill
 * is rarely just one test, so closing it is its own "Done" rather than
 * something that happens the moment the first test lands.
 */
export function TestPickerDialog({
  patientName, billedTestIds, addedCount, addedTotal, onAdd, onDone,
}: Props) {
  const [search, setSearch] = useState('');
  const [tests, setTests] = useState<DiagnosticTest[]>([]);

  const find = useCallback(async (term: string) => {
    try {
      // activeOnly: a retired test is found again on the master, never here.
      const found = await api.get<DiagnosticTest[]>(
        `/api/diagnostics/tests?activeOnly=true&term=${encodeURIComponent(term.trim())}`,
      );
      setTests(found);
    } catch {
      setTests([]);
    }
  }, []);

  // Loaded before anything is typed, so this opens already listing every
  // active test rather than an empty box waiting for input.
  useEffect(() => {
    const handle = setTimeout(() => void find(search), 200);
    return () => clearTimeout(handle);
  }, [search, find]);

  const visible = tests.filter((t) => !billedTestIds.includes(t.id));

  const cardRef = useModalBehaviour(onDone);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Add tests">
      <div className="overlay-card wide" ref={cardRef}>
        <div className="overlay-head">
          <h2>Add tests{patientName && ` — ${patientName}`}</h2>
          <button type="button" onClick={onDone}>Close</button>
        </div>

        <div className="overlay-body">
          <div className="inline-form">
            <input
              placeholder="Search tests"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              autoFocus
            />
          </div>

          <table>
            <thead><tr><th>Test</th><th>Category</th><th>Price</th><th></th></tr></thead>
            <tbody>
              {visible.map((t) => (
                <tr key={t.id}>
                  <td>{t.name}</td>
                  <td>{t.category}</td>
                  <td>{t.price.toFixed(2)}</td>
                  <td className="row-actions">
                    {/* The row leaving the list is the confirmation it landed
                        on the bill. */}
                    <button type="button" onClick={() => onAdd(t)}>Add</button>
                  </td>
                </tr>
              ))}
              {visible.length === 0 && (
                <tr><td colSpan={4}>No tests left to add.</td></tr>
              )}
            </tbody>
          </table>

          <div className="overlay-actions">
            <span className="hint">
              {addedCount === 0
                ? 'Nothing on the bill yet.'
                : `${addedCount} test(s) on this bill · ₹${addedTotal.toFixed(2)}`}
            </span>
            <button type="button" onClick={onDone}>Done</button>
          </div>
        </div>
      </div>
    </div>
  );
}
