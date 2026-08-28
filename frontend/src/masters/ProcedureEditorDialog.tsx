import { useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Procedure, ProcedureDepartment } from '../api/types';
import { useModalBehaviour } from '../shell/useModalBehaviour';

interface Props {
  existing: Procedure | null;
  /** Which department the list was filtered to when "New" was pressed — a
   * new procedure belongs where the user was looking, not to a default. */
  department: ProcedureDepartment;
  onClose: () => void;
  /** The saved department comes back so the list can follow the row. Saving a
   * dentist procedure while the list is filtered to General otherwise reports
   * success over an unchanged, empty table. */
  onSaved: (message: string, department: ProcedureDepartment) => void;
}

const DEPARTMENTS: ProcedureDepartment[] = ['General', 'Pediatrics', 'Dentist'];

/**
 * One procedure. The department is editable here rather than fixed by the
 * tab, because a procedure filed under the wrong one is otherwise invisible
 * from the screen that could fix it.
 */
export function ProcedureEditorDialog({ existing, department, onClose, onSaved }: Props) {
  const [name, setName] = useState(existing?.name ?? '');
  const [dept, setDept] = useState<ProcedureDepartment>(existing?.department ?? department);
  const [category, setCategory] = useState(existing?.category ?? '');
  const [price, setPrice] = useState(existing ? String(existing.price) : '');
  const [active, setActive] = useState(existing?.active ?? true);

  const [nameMissing, setNameMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const save = async (e: FormEvent) => {
    e.preventDefault();

    if (!name.trim()) {
      setNameMissing(true);
      setError('Procedure name is required.');
      return;
    }

    setNameMissing(false);
    setError(null);
    setBusy(true);
    try {
      const saved = await api.post<Procedure>('/api/pediatrics/procedures', {
        id: existing?.id ?? null,
        name: name.trim(),
        category: category.trim(),
        department: dept,
        price: Number(price) || 0,
        active,
      });
      onSaved(`${saved.name} saved.`, saved.department);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the procedure.');
      setBusy(false);
    }
  };

  const cardRef = useModalBehaviour(onClose);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Procedure">
      <div className="overlay-card" ref={cardRef}>
        <div className="overlay-head">
          <h2>{existing ? existing.name : 'New procedure'}</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body settings-form" onSubmit={save}>
          {error && <p className="auth-error">{error}</p>}

          <div className="settings-row">
            <label>Procedure name</label>
            <input
              value={name}
              onChange={(e) => { setName(e.target.value); if (e.target.value.trim()) setNameMissing(false); }}
              className={nameMissing ? 'field-missing' : undefined}
              autoFocus
            />
          </div>

          <div className="settings-row">
            <label>Department</label>
            <select value={dept} onChange={(e) => setDept(e.target.value as ProcedureDepartment)}>
              {DEPARTMENTS.map((d) => <option key={d} value={d}>{d}</option>)}
            </select>
            <p className="hint">Which screen offers it when billing. One catalogue, filtered — not three.</p>
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

          <label className="checkbox-label">
            <input type="checkbox" checked={active} onChange={() => setActive(!active)} />
            <span>Active — an inactive procedure stays on old bills but leaves billing</span>
          </label>

          {/* No delete: a procedure is referenced by every bill line and
              dental case that used it, and the desktop offers deactivation
              here for the same reason. */}
          <div className="overlay-actions">
            <button type="submit" disabled={busy}>Save</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
