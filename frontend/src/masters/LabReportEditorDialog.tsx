import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { LabAnalyte, LabReport } from '../api/types';
import { useModalBehaviour } from '../shell/useModalBehaviour';

interface Props {
  existing: LabReport | null;
  onClose: () => void;
  onSaved: (message: string) => void;
}

/**
 * A report is a panel of analytes at a price — "Complete Blood Count" holds a
 * dozen.
 *
 * The chosen analytes are an ordered list, not a set: that order is the order
 * results are entered and printed in, so moving a row up is a real edit, not
 * cosmetics.
 */
export function LabReportEditorDialog({ existing, onClose, onSaved }: Props) {
  const [name, setName] = useState(existing?.name ?? '');
  const [category, setCategory] = useState(existing?.category ?? '');
  const [price, setPrice] = useState(existing ? String(existing.price) : '');
  const [sequenceOrder, setSequenceOrder] = useState(String(existing?.sequenceOrder ?? 0));
  const [active, setActive] = useState(existing?.active ?? true);

  const [allAnalytes, setAllAnalytes] = useState<LabAnalyte[]>([]);
  const [chosen, setChosen] = useState<LabAnalyte[]>([]);
  const [picked, setPicked] = useState('');

  const [nameMissing, setNameMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void api.get<LabAnalyte[]>('/api/lab/analytes?activeOnly=true').then(setAllAnalytes).catch(() => {
      // Optional enrichment: an empty list here costs a little typing, not
      // correctness, and no screen states anything about it being empty.
    });
  }, []);

  useEffect(() => {
    if (!existing) return;
    void api.get<LabAnalyte[]>(`/api/lab/reports/${existing.id}/analytes`).then(setChosen).catch(() => {
      // Optional enrichment: an empty list here costs a little typing, not
      // correctness, and no screen states anything about it being empty.
    });
  }, [existing]);

  const add = () => {
    const analyte = allAnalytes.find((a) => a.id === picked);
    // Silently ignoring a duplicate rather than adding it: the same analyte
    // twice in one panel would be entered twice and printed twice.
    if (analyte && !chosen.some((c) => c.id === analyte.id)) setChosen([...chosen, analyte]);
    setPicked('');
  };

  const move = (index: number, by: number) => {
    const to = index + by;
    if (to < 0 || to >= chosen.length) return;
    const next = [...chosen];
    [next[index], next[to]] = [next[to], next[index]];
    setChosen(next);
  };

  const save = async (e: FormEvent) => {
    e.preventDefault();

    if (!name.trim()) {
      setNameMissing(true);
      setError('Report name is required.');
      return;
    }

    setNameMissing(false);
    setError(null);
    setBusy(true);
    try {
      const saved = await api.post<LabReport>('/api/lab/reports', {
        id: existing?.id ?? null,
        name: name.trim(),
        category: category.trim(),
        price: Number(price) || 0,
        sequenceOrder: Number(sequenceOrder) || 0,
        active,
        analyteIds: chosen.map((c) => c.id),
      });
      onSaved(`${saved.name} saved.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the report.');
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!existing) return;
    if (!window.confirm(`Delete ${existing.name}?`)) return;

    setError(null);
    setBusy(true);
    try {
      await api.post(`/api/lab/reports/${existing.id}/remove`);
      onSaved(`${existing.name} deleted.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not delete the report.');
      setBusy(false);
    }
  };

  const cardRef = useModalBehaviour(onClose);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Lab report">
      <div className="overlay-card wide" ref={cardRef}>
        <div className="overlay-head">
          <h2>{existing ? existing.name : 'New report'}</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body settings-form" onSubmit={save}>
          {error && <p className="auth-error">{error}</p>}

          <div className="settings-row">
            <label>Report name</label>
            <input
              value={name}
              onChange={(e) => { setName(e.target.value); if (e.target.value.trim()) setNameMissing(false); }}
              className={nameMissing ? 'field-missing' : undefined}
              placeholder="Complete Blood Count"
              autoFocus
            />
          </div>

          <div className="settings-row">
            <label>Category</label>
            <input value={category} onChange={(e) => setCategory(e.target.value)} />
            <p className="hint">Left blank, this becomes “Others”.</p>
          </div>

          <div className="settings-row">
            <label>Price</label>
            <input type="number" min={0} step="0.01" value={price} onChange={(e) => setPrice(e.target.value)} />
          </div>

          <div className="settings-row">
            <label>Sort order</label>
            <input type="number" value={sequenceOrder} onChange={(e) => setSequenceOrder(e.target.value)} />
          </div>

          <h3 className="sub-heading">Analytes on this report</h3>

          {allAnalytes.length === 0 && (
            <p className="hint warn">
              No analytes exist yet. Add them on the Analytes tab first — a report with none has nothing
              to enter results against.
            </p>
          )}

          <div className="inline-form">
            <select value={picked} onChange={(e) => setPicked(e.target.value)}>
              <option value="">Add an analyte…</option>
              {allAnalytes
                .filter((a) => !chosen.some((c) => c.id === a.id))
                .map((a) => (
                  <option key={a.id} value={a.id}>{a.name}{a.units ? ` (${a.units})` : ''}</option>
                ))}
            </select>
            <button type="button" className="ghost" disabled={!picked} onClick={add}>Add</button>
          </div>

          <table>
            <thead><tr><th>#</th><th>Analyte</th><th>Units</th><th>Order</th><th></th></tr></thead>
            <tbody>
              {chosen.map((a, index) => (
                <tr key={a.id}>
                  <td>{index + 1}</td>
                  <td>{a.name}</td>
                  <td>{a.units || '—'}</td>
                  <td className="row-actions">
                    <button type="button" className="ghost" disabled={index === 0} onClick={() => move(index, -1)}>↑</button>
                    <button
                      type="button"
                      className="ghost"
                      disabled={index === chosen.length - 1}
                      onClick={() => move(index, 1)}
                    >
                      ↓
                    </button>
                  </td>
                  <td>
                    <button
                      type="button"
                      className="ghost"
                      onClick={() => setChosen(chosen.filter((c) => c.id !== a.id))}
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
              {chosen.length === 0 && <tr><td colSpan={5}>Nothing on this report yet.</td></tr>}
            </tbody>
          </table>

          <label className="checkbox-label">
            <input type="checkbox" checked={active} onChange={() => setActive(!active)} />
            <span>Active — an inactive report stays on past orders but leaves ordering</span>
          </label>

          <div className="overlay-actions">
            <button type="submit" disabled={busy}>Save</button>
            {existing && (
              <button type="button" className="ghost" disabled={busy} onClick={() => void remove()}>
                Delete
              </button>
            )}
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
