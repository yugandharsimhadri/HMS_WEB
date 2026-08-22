import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { DiagnosticTest } from '../api/types';

interface Props {
  existing: DiagnosticTest | null;
  onClose: () => void;
  onSaved: (message: string) => void;
}

/**
 * One diagnostic test, added or edited over the screen — the same shape the
 * Medicines screen uses for a product, rather than a permanent side panel
 * that would sit half-empty beside the grid whenever nothing is selected.
 */
export function TestEditorDialog({ existing, onClose, onSaved }: Props) {
  const [name, setName] = useState(existing?.name ?? '');
  const [category, setCategory] = useState(existing?.category ?? '');
  const [price, setPrice] = useState(existing ? String(existing.price) : '');
  const [active, setActive] = useState(existing?.active ?? true);

  const [categories, setCategories] = useState<string[]>([]);
  const [nameMissing, setNameMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void api.get<string[]>('/api/diagnostics/tests/categories').then(setCategories).catch(() => {});
  }, []);

  const save = async (e: FormEvent) => {
    e.preventDefault();

    if (!name.trim()) {
      // Marked as well as said: the field itself is what has to change.
      setNameMissing(true);
      setError('Test name is required.');
      return;
    }

    setNameMissing(false);
    setError(null);
    setBusy(true);
    try {
      const saved = await api.post<DiagnosticTest>('/api/diagnostics/tests', {
        id: existing?.id ?? null,
        name: name.trim(),
        category: category.trim(),
        price: Number(price) || 0,
        active,
      });
      onSaved(`${saved.name} saved.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the test.');
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!existing) return;
    if (!window.confirm(`Delete ${existing.name}?`)) return;

    setError(null);
    setBusy(true);
    try {
      await api.post(`/api/diagnostics/tests/${existing.id}/remove`);
      onSaved(`${existing.name} deleted.`);
    } catch (err) {
      // The "already billed, deactivate instead" refusal lands here, and is
      // the whole reason this message is shown rather than swallowed.
      setError(err instanceof ApiError ? err.message : 'Could not delete the test.');
      setBusy(false);
    }
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Test">
      <div className="overlay-card">
        <div className="overlay-head">
          <h2>{existing ? existing.name : 'New test'}</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body settings-form" onSubmit={save}>
          {error && <p className="auth-error">{error}</p>}

          <div className="settings-row">
            <label>Test name</label>
            <input
              value={name}
              // Never left red once it is right.
              onChange={(e) => { setName(e.target.value); if (e.target.value.trim()) setNameMissing(false); }}
              className={nameMissing ? 'field-missing' : undefined}
              autoFocus
            />
          </div>

          <div className="settings-row">
            <label>Category</label>
            {/* A suggest list, not a closed set — the clinic's own vocabulary
                matters, but a bare box invites eight spellings of one word. */}
            <input value={category} onChange={(e) => setCategory(e.target.value)} list="test-categories" />
            <datalist id="test-categories">
              {categories.map((c) => <option key={c} value={c} />)}
            </datalist>
            <p className="hint">Left blank, this becomes “Others”.</p>
          </div>

          <div className="settings-row">
            <label>Price</label>
            <input type="number" min={0} step="0.01" value={price} onChange={(e) => setPrice(e.target.value)} />
          </div>

          <label className="checkbox-label">
            <input type="checkbox" checked={active} onChange={() => setActive(!active)} />
            <span>Active — an inactive test stays in the master but leaves billing</span>
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
