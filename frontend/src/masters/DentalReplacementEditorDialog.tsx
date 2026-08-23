import { useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { DentalReplacementMaster } from '../api/types';

interface Props {
  existing: DentalReplacementMaster | null;
  onClose: () => void;
  onSaved: (message: string) => void;
}

/** A crown, a bridge, an implant — priced per unit, because one case may
 * need several of the same thing. */
export function DentalReplacementEditorDialog({ existing, onClose, onSaved }: Props) {
  const [name, setName] = useState(existing?.name ?? '');
  const [category, setCategory] = useState(existing?.category ?? '');
  const [unitCost, setUnitCost] = useState(existing ? String(existing.unitCost) : '');
  const [active, setActive] = useState(existing?.active ?? true);

  const [nameMissing, setNameMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const save = async (e: FormEvent) => {
    e.preventDefault();

    if (!name.trim()) {
      setNameMissing(true);
      setError('Replacement name is required.');
      return;
    }

    setNameMissing(false);
    setError(null);
    setBusy(true);
    try {
      const saved = await api.post<DentalReplacementMaster>('/api/dentist/replacements', {
        id: existing?.id ?? null,
        name: name.trim(),
        category: category.trim(),
        unitCost: Number(unitCost) || 0,
        active,
      });
      onSaved(`${saved.name} saved.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the replacement.');
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!existing) return;
    if (!window.confirm(`Delete ${existing.name}?`)) return;

    setError(null);
    setBusy(true);
    try {
      await api.post(`/api/dentist/replacements/${existing.id}/remove`);
      onSaved(`${existing.name} deleted.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not delete the replacement.');
      setBusy(false);
    }
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Replacement">
      <div className="overlay-card">
        <div className="overlay-head">
          <h2>{existing ? existing.name : 'New replacement'}</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body settings-form" onSubmit={save}>
          {error && <p className="auth-error">{error}</p>}

          <div className="settings-row">
            <label>Name</label>
            <input
              value={name}
              onChange={(e) => { setName(e.target.value); if (e.target.value.trim()) setNameMissing(false); }}
              className={nameMissing ? 'field-missing' : undefined}
              autoFocus
            />
          </div>

          <div className="settings-row">
            <label>Category</label>
            <input value={category} onChange={(e) => setCategory(e.target.value)} placeholder="Crown, Bridge, Implant" />
            <p className="hint">Left blank, this becomes “Others”.</p>
          </div>

          <div className="settings-row">
            <label>Cost per unit</label>
            <input type="number" min={0} step="0.01" value={unitCost} onChange={(e) => setUnitCost(e.target.value)} />
            <p className="hint">A case adds these by quantity, so this is one unit, not the job.</p>
          </div>

          <label className="checkbox-label">
            <input type="checkbox" checked={active} onChange={() => setActive(!active)} />
            <span>Active — an inactive item stays on open cases but leaves the picker</span>
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
