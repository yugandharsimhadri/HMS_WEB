import { useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { AnesthesiaTypeMaster } from '../api/types';

interface Props {
  existing: AnesthesiaTypeMaster | null;
  onClose: () => void;
  onSaved: (message: string) => void;
}

export function AnesthesiaTypeEditorDialog({ existing, onClose, onSaved }: Props) {
  const [name, setName] = useState(existing?.name ?? '');
  const [defaultCost, setDefaultCost] = useState(existing ? String(existing.defaultCost) : '');
  const [active, setActive] = useState(existing?.active ?? true);

  const [nameMissing, setNameMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const save = async (e: FormEvent) => {
    e.preventDefault();

    if (!name.trim()) {
      setNameMissing(true);
      setError('Anesthesia type name is required.');
      return;
    }

    setNameMissing(false);
    setError(null);
    setBusy(true);
    try {
      const saved = await api.post<AnesthesiaTypeMaster>('/api/dentist/anesthesia-types', {
        id: existing?.id ?? null,
        name: name.trim(),
        defaultCost: Number(defaultCost) || 0,
        active,
      });
      onSaved(`${saved.name} saved.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the anesthesia type.');
      setBusy(false);
    }
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Anesthesia type">
      <div className="overlay-card">
        <div className="overlay-head">
          <h2>{existing ? existing.name : 'New anesthesia type'}</h2>
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
              placeholder="Local, Nerve block, General"
              autoFocus
            />
          </div>

          <div className="settings-row">
            <label>Default cost</label>
            <input
              type="number"
              min={0}
              step="0.01"
              value={defaultCost}
              onChange={(e) => setDefaultCost(e.target.value)}
            />
            <p className="hint">
              A starting figure, not a fixed price — a sitting can override it, since how much was actually
              needed is not knowable from here.
            </p>
          </div>

          <label className="checkbox-label">
            <input type="checkbox" checked={active} onChange={() => setActive(!active)} />
            <span>Active — an inactive type stays on past sittings but leaves the picker</span>
          </label>

          {/* No delete, matching the desktop: every sitting that used this
              points at it, and deactivating is what taking it out of the
              picker actually requires. */}
          <div className="overlay-actions">
            <button type="submit" disabled={busy}>Save</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
