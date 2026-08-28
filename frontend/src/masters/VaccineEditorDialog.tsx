import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { VaccineMaster } from '../api/types';
import { useModalBehaviour } from '../shell/useModalBehaviour';

interface Props {
  existing: VaccineMaster | null;
  onClose: () => void;
  onSaved: (message: string) => void;
}

/**
 * Common ages, offered as a picker because the schedule is in weeks and the
 * field is in days. Asking a nurse to convert "10 weeks" to 70 by hand is
 * how a dose ends up due a fortnight late.
 */
const AGE_PRESETS: { label: string; days: number }[] = [
  { label: 'At birth', days: 0 },
  { label: '6 weeks', days: 42 },
  { label: '10 weeks', days: 70 },
  { label: '14 weeks', days: 98 },
  { label: '6 months', days: 180 },
  { label: '9 months', days: 270 },
  { label: '12 months', days: 365 },
  { label: '15 months', days: 456 },
  { label: '18 months', days: 547 },
  { label: '2 years', days: 730 },
  { label: '5 years', days: 1825 },
  { label: '10 years', days: 3650 },
];

/** Days back into something readable, for the hint under the box. */
export function describeAgeDays(days: number): string {
  if (days <= 0) return 'at birth';
  if (days < 70) return `${Math.round(days / 7)} weeks`;
  if (days < 730) return `${Math.round(days / 30.44)} months`;
  return `${(days / 365).toFixed(days % 365 === 0 ? 0 : 1)} years`;
}

export function VaccineEditorDialog({ existing, onClose, onSaved }: Props) {
  const [name, setName] = useState(existing?.name ?? '');
  const [doseNumber, setDoseNumber] = useState(String(existing?.doseNumber ?? 1));
  const [ageDays, setAgeDays] = useState(String(existing?.recommendedAgeDays ?? 0));
  const [category, setCategory] = useState(existing?.category ?? '');
  const [sequenceOrder, setSequenceOrder] = useState(String(existing?.sequenceOrder ?? 0));
  const [active, setActive] = useState(existing?.active ?? true);

  const [categories, setCategories] = useState<string[]>([]);
  const [nameMissing, setNameMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void api.get<string[]>('/api/pediatrics/vaccines/categories').then(setCategories).catch(() => {
      // Optional enrichment: an empty list here costs a little typing, not
      // correctness, and no screen states anything about it being empty.
    });
  }, []);

  const save = async (e: FormEvent) => {
    e.preventDefault();

    if (!name.trim()) {
      setNameMissing(true);
      setError('Vaccine name is required.');
      return;
    }

    setNameMissing(false);
    setError(null);
    setBusy(true);
    try {
      const saved = await api.post<VaccineMaster>('/api/pediatrics/vaccines', {
        id: existing?.id ?? null,
        name: name.trim(),
        doseNumber: Number(doseNumber) || 1,
        recommendedAgeDays: Number(ageDays) || 0,
        category: category.trim(),
        sequenceOrder: Number(sequenceOrder) || 0,
        active,
      });
      onSaved(`${saved.name} dose ${saved.doseNumber} saved.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the vaccine.');
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!existing) return;
    if (!window.confirm(`Delete ${existing.name} dose ${existing.doseNumber}?`)) return;

    setError(null);
    setBusy(true);
    try {
      await api.post(`/api/pediatrics/vaccines/${existing.id}/remove`);
      onSaved(`${existing.name} deleted.`);
    } catch (err) {
      // The "already given, deactivate instead" refusal lands here, and is
      // the whole reason this is shown rather than swallowed.
      setError(err instanceof ApiError ? err.message : 'Could not delete the vaccine.');
      setBusy(false);
    }
  };

  const cardRef = useModalBehaviour(onClose);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Vaccine">
      <div className="overlay-card" ref={cardRef}>
        <div className="overlay-head">
          <h2>{existing ? `${existing.name} · dose ${existing.doseNumber}` : 'New vaccine'}</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body settings-form" onSubmit={save}>
          {error && <p className="auth-error">{error}</p>}

          <div className="settings-row">
            <label>Vaccine name</label>
            <input
              value={name}
              onChange={(e) => { setName(e.target.value); if (e.target.value.trim()) setNameMissing(false); }}
              className={nameMissing ? 'field-missing' : undefined}
              autoFocus
            />
          </div>

          <div className="settings-row">
            <label>Dose number</label>
            <input type="number" min={1} value={doseNumber} onChange={(e) => setDoseNumber(e.target.value)} />
            <p className="hint">
              Each dose is its own row — OPV 1, 2 and 3 are three rows, which is what lets the card show
              which one is due next.
            </p>
          </div>

          <div className="settings-row">
            <label>Due at</label>
            <div className="inline-form">
              <select
                value={AGE_PRESETS.some((p) => p.days === Number(ageDays)) ? ageDays : ''}
                onChange={(e) => e.target.value && setAgeDays(e.target.value)}
              >
                <option value="">Choose…</option>
                {AGE_PRESETS.map((p) => <option key={p.days} value={p.days}>{p.label}</option>)}
              </select>
              <input
                type="number"
                min={0}
                value={ageDays}
                onChange={(e) => setAgeDays(e.target.value)}
                aria-label="Age in days"
              />
              <span className="hint">days</span>
            </div>
            <p className="hint">
              Age from birth, in days — {describeAgeDays(Number(ageDays) || 0)}. Days rather than months
              because the early schedule runs in weeks.
            </p>
          </div>

          <div className="settings-row">
            <label>Category</label>
            <input value={category} onChange={(e) => setCategory(e.target.value)} list="vaccine-categories" />
            <datalist id="vaccine-categories">
              {categories.map((c) => <option key={c} value={c} />)}
            </datalist>
            <p className="hint">Left blank, this becomes “Others”.</p>
          </div>

          <div className="settings-row">
            <label>Sort order</label>
            <input type="number" value={sequenceOrder} onChange={(e) => setSequenceOrder(e.target.value)} />
            <p className="hint">Where it sits on the immunisation card when two doses fall due together.</p>
          </div>

          <label className="checkbox-label">
            <input type="checkbox" checked={active} onChange={() => setActive(!active)} />
            <span>Active — an inactive vaccine stays on old cards but leaves the “due” list</span>
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
