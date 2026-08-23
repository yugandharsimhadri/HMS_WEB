import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { LabPackageMaster, LabReport } from '../api/types';

interface Props {
  existing: LabPackageMaster | null;
  onClose: () => void;
  onSaved: (message: string) => void;
}

/**
 * A health-check package: a bundle of reports at one price.
 *
 * As with a dental package the price is quoted rather than summed — the
 * discount against ordering the reports separately is the reason to offer
 * one — so the separate total is shown alongside purely to make that
 * discount visible while it is being set.
 */
export function LabPackageEditorDialog({ existing, onClose, onSaved }: Props) {
  const [name, setName] = useState(existing?.name ?? '');
  const [packagePrice, setPackagePrice] = useState(existing ? String(existing.packagePrice) : '');
  const [active, setActive] = useState(existing?.active ?? true);

  const [allReports, setAllReports] = useState<LabReport[]>([]);
  const [chosen, setChosen] = useState<LabReport[]>([]);
  const [picked, setPicked] = useState('');

  const [nameMissing, setNameMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void api.get<LabReport[]>('/api/lab/reports').then(setAllReports).catch(() => {});
  }, []);

  useEffect(() => {
    if (!existing) return;
    void api.get<LabReport[]>(`/api/lab/packages/${existing.id}/reports`).then(setChosen).catch(() => {});
  }, [existing]);

  const listPrice = useMemo(() => chosen.reduce((sum, r) => sum + r.price, 0), [chosen]);
  const price = Number(packagePrice) || 0;

  const add = () => {
    const report = allReports.find((r) => r.id === picked);
    if (report && !chosen.some((c) => c.id === report.id)) setChosen([...chosen, report]);
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
      const saved = await api.post<LabPackageMaster>('/api/lab/packages', {
        id: existing?.id ?? null,
        name: name.trim(),
        packagePrice: price,
        active,
        reportIds: chosen.map((c) => c.id),
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
      await api.post(`/api/lab/packages/${existing.id}/remove`);
      onSaved(`${existing.name} deleted.`);
    } catch (err) {
      // "Already ordered, deactivate instead" arrives here.
      setError(err instanceof ApiError ? err.message : 'Could not delete the package.');
      setBusy(false);
    }
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Lab package">
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
              placeholder="Master Health Check"
              autoFocus
            />
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
                Ordered separately these come to <strong>{listPrice.toFixed(2)}</strong>
                {price > 0 && price < listPrice && ` — a discount of ${(listPrice - price).toFixed(2)}`}
                {price > listPrice && ' — which is more than ordering them separately'}.
              </p>
            )}
          </div>

          <h3 className="sub-heading">Reports in this package</h3>

          {allReports.length === 0 && (
            <p className="hint warn">No reports exist yet. Add them on the Reports tab first.</p>
          )}

          <div className="inline-form">
            <select value={picked} onChange={(e) => setPicked(e.target.value)}>
              <option value="">Add a report…</option>
              {allReports
                .filter((r) => !chosen.some((c) => c.id === r.id))
                .map((r) => <option key={r.id} value={r.id}>{r.name} · {r.price.toFixed(2)}</option>)}
            </select>
            <button type="button" className="ghost" disabled={!picked} onClick={add}>Add</button>
          </div>

          <table>
            <thead><tr><th>Report</th><th>Separately</th><th></th></tr></thead>
            <tbody>
              {chosen.map((r) => (
                <tr key={r.id}>
                  <td>{r.name}</td>
                  <td>{r.price.toFixed(2)}</td>
                  <td>
                    <button
                      type="button"
                      className="ghost"
                      onClick={() => setChosen(chosen.filter((c) => c.id !== r.id))}
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
              {chosen.length === 0 && <tr><td colSpan={3}>Nothing in this package yet.</td></tr>}
            </tbody>
          </table>

          <label className="checkbox-label">
            <input type="checkbox" checked={active} onChange={() => setActive(!active)} />
            <span>Active — an inactive package stays on past orders but leaves ordering</span>
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
