import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { DentalPackageItem, DentalPackageMaster, Procedure } from '../api/types';

interface Props {
  existing: DentalPackageMaster | null;
  onClose: () => void;
  onSaved: (message: string) => void;
}

interface ItemDraft {
  procedureId: string | null;
  procedureName: string;
  quantity: number;
}

/**
 * A package quotes a course of treatment at one price.
 *
 * The price is typed, not summed from the lines — quoting a discount against
 * the à-la-carte total is the whole reason a clinic offers a package. What
 * the lines do is say what is included, so the à-la-carte total is shown
 * beside the price purely so whoever sets it can see the discount they are
 * actually giving.
 */
export function DentalPackageEditorDialog({ existing, onClose, onSaved }: Props) {
  const [name, setName] = useState(existing?.name ?? '');
  const [description, setDescription] = useState(existing?.description ?? '');
  const [packagePrice, setPackagePrice] = useState(existing ? String(existing.packagePrice) : '');
  const [active, setActive] = useState(existing?.active ?? true);
  const [items, setItems] = useState<ItemDraft[]>([]);

  const [procedures, setProcedures] = useState<Procedure[]>([]);
  const [picked, setPicked] = useState('');
  const [nameMissing, setNameMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void api.get<Procedure[]>('/api/pediatrics/procedures?department=Dentist&activeOnly=true')
      .then(setProcedures).catch(() => {});
  }, []);

  // Loaded only when editing — the list screen carries the price, not the
  // contents, so opening a package is the first time they are needed.
  useEffect(() => {
    if (!existing) return;
    void api.get<DentalPackageItem[]>(`/api/dentist/packages/${existing.id}/items`)
      .then((rows) => setItems(rows.map((r) => ({
        procedureId: r.procedureId,
        procedureName: r.procedureName,
        quantity: r.quantity,
      }))))
      .catch(() => {});
  }, [existing]);

  const listPrice = useMemo(
    () => items.reduce((sum, i) => {
      const p = procedures.find((x) => x.id === i.procedureId);
      return sum + (p ? p.price * i.quantity : 0);
    }, 0),
    [items, procedures],
  );

  const addPicked = () => {
    const procedure = procedures.find((p) => p.id === picked);
    if (!procedure) return;

    // Already there: bump the quantity rather than adding a second line for
    // the same procedure, which would print twice and total the same.
    const at = items.findIndex((i) => i.procedureId === procedure.id);
    if (at >= 0) {
      const next = [...items];
      next[at] = { ...next[at], quantity: next[at].quantity + 1 };
      setItems(next);
    } else {
      setItems([...items, { procedureId: procedure.id, procedureName: procedure.name, quantity: 1 }]);
    }
    setPicked('');
  };

  const save = async (e: FormEvent) => {
    e.preventDefault();

    if (!name.trim()) {
      setNameMissing(true);
      setError('Package name is required.');
      return;
    }

    setNameMissing(false);
    setError(null);
    setBusy(true);
    try {
      const saved = await api.post<DentalPackageMaster>('/api/dentist/packages', {
        id: existing?.id ?? null,
        name: name.trim(),
        description: description.trim() || null,
        packagePrice: Number(packagePrice) || 0,
        active,
        items,
      });
      onSaved(`${saved.name} saved.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the package.');
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!existing) return;
    if (!window.confirm(`Delete ${existing.name}?`)) return;

    setError(null);
    setBusy(true);
    try {
      await api.post(`/api/dentist/packages/${existing.id}/remove`);
      onSaved(`${existing.name} deleted.`);
    } catch (err) {
      // "Already used by a case, deactivate instead" arrives here.
      setError(err instanceof ApiError ? err.message : 'Could not delete the package.');
      setBusy(false);
    }
  };

  const price = Number(packagePrice) || 0;

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Dental package">
      <div className="overlay-card wide">
        <div className="overlay-head">
          <h2>{existing ? existing.name : 'New package'}</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body settings-form" onSubmit={save}>
          {error && <p className="auth-error">{error}</p>}

          <div className="settings-row">
            <label>Package name</label>
            <input
              value={name}
              onChange={(e) => { setName(e.target.value); if (e.target.value.trim()) setNameMissing(false); }}
              className={nameMissing ? 'field-missing' : undefined}
              autoFocus
            />
          </div>

          <div className="settings-row">
            <label>Description</label>
            <input value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>

          <div className="settings-row">
            <label>Package price</label>
            <input
              type="number"
              min={0}
              step="0.01"
              value={packagePrice}
              onChange={(e) => setPackagePrice(e.target.value)}
            />
            {listPrice > 0 && (
              <p className="hint">
                Separately these come to <strong>{listPrice.toFixed(2)}</strong>
                {price > 0 && price < listPrice && ` — a discount of ${(listPrice - price).toFixed(2)}`}
                {price > listPrice && ' — which is less than this package charges'}.
              </p>
            )}
          </div>

          <h3 className="sub-heading">What is included</h3>
          <div className="inline-form">
            <select value={picked} onChange={(e) => setPicked(e.target.value)}>
              <option value="">Add a procedure…</option>
              {procedures.map((p) => (
                <option key={p.id} value={p.id}>{p.name} · {p.price.toFixed(2)}</option>
              ))}
            </select>
            <button type="button" className="ghost" disabled={!picked} onClick={addPicked}>Add</button>
          </div>

          {procedures.length === 0 && (
            <p className="hint warn">
              No dentist procedures exist yet. Add them on the Procedures tab first — a package with
              nothing in it quotes a price for nothing.
            </p>
          )}

          <table>
            <thead><tr><th>Procedure</th><th>Qty</th><th>Each</th><th></th></tr></thead>
            <tbody>
              {items.map((item, index) => {
                const p = procedures.find((x) => x.id === item.procedureId);
                return (
                  <tr key={item.procedureId ?? index}>
                    <td>{item.procedureName}</td>
                    <td>
                      <input
                        type="number"
                        min={1}
                        value={item.quantity}
                        style={{ width: '5rem' }}
                        onChange={(e) => {
                          const next = [...items];
                          next[index] = { ...item, quantity: Math.max(1, Number(e.target.value) || 1) };
                          setItems(next);
                        }}
                      />
                    </td>
                    <td>{p ? p.price.toFixed(2) : '—'}</td>
                    <td>
                      <button
                        type="button"
                        className="ghost"
                        onClick={() => setItems(items.filter((_, i) => i !== index))}
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                );
              })}
              {items.length === 0 && <tr><td colSpan={4}>Nothing included yet.</td></tr>}
            </tbody>
          </table>

          <label className="checkbox-label">
            <input type="checkbox" checked={active} onChange={() => setActive(!active)} />
            <span>Active — an inactive package stays on open cases but leaves the picker</span>
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
