import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Gender, LabAnalyte, LabAnalyteReferenceRange } from '../api/types';

interface Props {
  existing: LabAnalyte | null;
  onClose: () => void;
  onSaved: (message: string) => void;
}

const BLANK_RANGE = {
  gender: '' as Gender | '',
  minAgeYears: '',
  maxAgeYears: '',
  lowValue: '',
  highValue: '',
  textRange: '',
  label: '',
};

/** Empty box → null, so "no lower bound" is distinct from "zero". A
 * haemoglobin range with a low bound of 0 flags nothing. */
function num(value: string): number | null {
  return value.trim() === '' ? null : Number(value);
}

export function describeRange(r: LabAnalyteReferenceRange): string {
  if (r.textRange) return r.textRange;
  if (r.lowValue !== null && r.highValue !== null) return `${r.lowValue} – ${r.highValue}`;
  if (r.lowValue !== null) return `> ${r.lowValue}`;
  if (r.highValue !== null) return `< ${r.highValue}`;
  return '—';
}

export function describeApplies(r: LabAnalyteReferenceRange): string {
  const parts: string[] = [];
  parts.push(r.gender ?? 'Any sex');
  if (r.minAgeYears !== null && r.maxAgeYears !== null) parts.push(`${r.minAgeYears}–${r.maxAgeYears}y`);
  else if (r.minAgeYears !== null) parts.push(`${r.minAgeYears}y+`);
  else if (r.maxAgeYears !== null) parts.push(`under ${r.maxAgeYears}y`);
  else parts.push('any age');
  return parts.join(' · ');
}

/**
 * One analyte and the reference ranges that decide whether a result reads as
 * normal.
 *
 * The ranges are saved individually and immediately, not with the analyte —
 * a new analyte has no id to hang them off until it is saved once, and
 * pretending otherwise leads to ranges silently lost on a first save. So the
 * range section only appears for an analyte that already exists, and the
 * dialog says so rather than showing dead controls.
 */
export function LabAnalyteEditorDialog({ existing, onClose, onSaved }: Props) {
  const [name, setName] = useState(existing?.name ?? '');
  const [category, setCategory] = useState(existing?.category ?? '');
  const [units, setUnits] = useState(existing?.units ?? '');
  const [decimalPlaces, setDecimalPlaces] = useState(String(existing?.decimalPlaces ?? 1));
  const [sequenceOrder, setSequenceOrder] = useState(String(existing?.sequenceOrder ?? 0));
  const [active, setActive] = useState(existing?.active ?? true);

  const [categories, setCategories] = useState<string[]>([]);
  const [ranges, setRanges] = useState<LabAnalyteReferenceRange[]>([]);
  const [draft, setDraft] = useState({ ...BLANK_RANGE });
  const [editingRangeId, setEditingRangeId] = useState<string | null>(null);

  const [nameMissing, setNameMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void api.get<string[]>('/api/lab/analytes/categories').then(setCategories).catch(() => {});
  }, []);

  const loadRanges = useCallback(async () => {
    if (!existing) return;
    try {
      setRanges(await api.get<LabAnalyteReferenceRange[]>(`/api/lab/analytes/${existing.id}/ranges`));
    } catch { /* the analyte is still editable without them */ }
  }, [existing]);

  useEffect(() => { void loadRanges(); }, [loadRanges]);

  const save = async (e: FormEvent) => {
    e.preventDefault();

    if (!name.trim()) {
      setNameMissing(true);
      setError('Analyte name is required.');
      return;
    }

    setNameMissing(false);
    setError(null);
    setBusy(true);
    try {
      const saved = await api.post<LabAnalyte>('/api/lab/analytes', {
        id: existing?.id ?? null,
        name: name.trim(),
        category: category.trim(),
        units: units.trim(),
        decimalPlaces: Number(decimalPlaces) || 0,
        sequenceOrder: Number(sequenceOrder) || 0,
        active,
      });
      onSaved(`${saved.name} saved.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the analyte.');
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!existing) return;
    if (!window.confirm(`Delete ${existing.name}?`)) return;

    setError(null);
    setBusy(true);
    try {
      await api.post(`/api/lab/analytes/${existing.id}/remove`);
      onSaved(`${existing.name} deleted.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not delete the analyte.');
      setBusy(false);
    }
  };

  const saveRange = async () => {
    if (!existing) return;
    setError(null);
    try {
      await api.post(`/api/lab/analytes/${existing.id}/ranges`, {
        id: editingRangeId,
        gender: draft.gender === '' ? null : draft.gender,
        minAgeYears: num(draft.minAgeYears),
        maxAgeYears: num(draft.maxAgeYears),
        lowValue: num(draft.lowValue),
        highValue: num(draft.highValue),
        textRange: draft.textRange.trim() || null,
        label: draft.label.trim(),
      });
      setDraft({ ...BLANK_RANGE });
      setEditingRangeId(null);
      await loadRanges();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the range.');
    }
  };

  const editRange = (r: LabAnalyteReferenceRange) => {
    setEditingRangeId(r.id);
    setDraft({
      gender: r.gender ?? '',
      minAgeYears: r.minAgeYears?.toString() ?? '',
      maxAgeYears: r.maxAgeYears?.toString() ?? '',
      lowValue: r.lowValue?.toString() ?? '',
      highValue: r.highValue?.toString() ?? '',
      textRange: r.textRange ?? '',
      label: r.label ?? '',
    });
  };

  const removeRange = async (r: LabAnalyteReferenceRange) => {
    if (!window.confirm(`Delete the range “${r.label || describeApplies(r)}”?`)) return;
    try {
      await api.post(`/api/lab/ranges/${r.id}/remove`);
      await loadRanges();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not delete the range.');
    }
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Analyte">
      <div className="overlay-card wide">
        <div className="overlay-head">
          <h2>{existing ? existing.name : 'New analyte'}</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body settings-form" onSubmit={save}>
          {error && <p className="auth-error">{error}</p>}

          <div className="settings-row">
            <label>Analyte name</label>
            <input
              value={name}
              onChange={(e) => { setName(e.target.value); if (e.target.value.trim()) setNameMissing(false); }}
              className={nameMissing ? 'field-missing' : undefined}
              placeholder="Haemoglobin"
              autoFocus
            />
          </div>

          <div className="settings-row">
            <label>Category</label>
            <input value={category} onChange={(e) => setCategory(e.target.value)} list="analyte-categories" />
            <datalist id="analyte-categories">
              {categories.map((c) => <option key={c} value={c} />)}
            </datalist>
          </div>

          <div className="settings-row">
            <label>Units</label>
            <input value={units} onChange={(e) => setUnits(e.target.value)} placeholder="g/dL" />
          </div>

          <div className="settings-row">
            <label>Decimal places</label>
            <input
              type="number"
              min={0}
              max={4}
              value={decimalPlaces}
              onChange={(e) => setDecimalPlaces(e.target.value)}
            />
            <p className="hint">
              How the value prints — haemoglobin to one place, a cell count to none. Wrong here makes a
              correct result look wrong.
            </p>
          </div>

          <div className="settings-row">
            <label>Sort order</label>
            <input type="number" value={sequenceOrder} onChange={(e) => setSequenceOrder(e.target.value)} />
            <p className="hint">Where it sits within a report.</p>
          </div>

          <label className="checkbox-label">
            <input type="checkbox" checked={active} onChange={() => setActive(!active)} />
            <span>Active</span>
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

        {/* Outside the form on purpose — these save on their own, and a
            nested form would submit the analyte instead. */}
        <div className="overlay-body settings-form">
          <h3 className="sub-heading">Reference ranges</h3>

          {!existing ? (
            <p className="hint">
              Save this analyte first — a range has to belong to something before it can be recorded
              against it.
            </p>
          ) : (
            <>
              <p className="hint">
                Which range applies is matched by the patient's sex and age when a result is entered.
                Leave a bound empty for “no limit”.
              </p>

              <table>
                <thead><tr><th>Label</th><th>Applies to</th><th>Range</th><th></th></tr></thead>
                <tbody>
                  {ranges.map((r) => (
                    <tr key={r.id}>
                      <td>{r.label || '—'}</td>
                      <td>{describeApplies(r)}</td>
                      <td>{describeRange(r)}</td>
                      <td className="row-actions">
                        <button type="button" className="ghost" onClick={() => editRange(r)}>Edit</button>
                        <button type="button" className="ghost" onClick={() => void removeRange(r)}>Delete</button>
                      </td>
                    </tr>
                  ))}
                  {ranges.length === 0 && (
                    <tr><td colSpan={4}>No ranges — results will print without a normal range.</td></tr>
                  )}
                </tbody>
              </table>

              <h4 className="sub-heading">{editingRangeId ? 'Edit range' : 'Add a range'}</h4>

              <div className="settings-row">
                <label>Label</label>
                <input
                  value={draft.label}
                  onChange={(e) => setDraft({ ...draft, label: e.target.value })}
                  placeholder="Adult male"
                />
                <p className="hint">What prints on the report beside the range.</p>
              </div>

              <div className="settings-row">
                <label>Applies to</label>
                <div className="inline-form">
                  <select
                    value={draft.gender}
                    onChange={(e) => setDraft({ ...draft, gender: e.target.value as Gender | '' })}
                  >
                    <option value="">Any sex</option>
                    <option value="Male">Male</option>
                    <option value="Female">Female</option>
                    <option value="Other">Other</option>
                  </select>
                  <input
                    type="number" step="0.1" placeholder="from age"
                    value={draft.minAgeYears}
                    onChange={(e) => setDraft({ ...draft, minAgeYears: e.target.value })}
                  />
                  <input
                    type="number" step="0.1" placeholder="to age"
                    value={draft.maxAgeYears}
                    onChange={(e) => setDraft({ ...draft, maxAgeYears: e.target.value })}
                  />
                  <span className="hint">years</span>
                </div>
              </div>

              <div className="settings-row">
                <label>Normal range</label>
                <div className="inline-form">
                  <input
                    type="number" step="0.01" placeholder="low"
                    value={draft.lowValue}
                    onChange={(e) => setDraft({ ...draft, lowValue: e.target.value })}
                  />
                  <input
                    type="number" step="0.01" placeholder="high"
                    value={draft.highValue}
                    onChange={(e) => setDraft({ ...draft, highValue: e.target.value })}
                  />
                </div>
              </div>

              <div className="settings-row">
                <label>Or a worded range</label>
                <input
                  value={draft.textRange}
                  onChange={(e) => setDraft({ ...draft, textRange: e.target.value })}
                  placeholder="Non-reactive"
                />
                <p className="hint">
                  For an analyte whose result is not a number. Set, this replaces the numbers above on the
                  printed report.
                </p>
              </div>

              <div className="overlay-actions">
                <button type="button" onClick={() => void saveRange()}>
                  {editingRangeId ? 'Save range' : 'Add range'}
                </button>
                {editingRangeId && (
                  <button
                    type="button"
                    className="ghost"
                    onClick={() => { setEditingRangeId(null); setDraft({ ...BLANK_RANGE }); }}
                  >
                    Cancel edit
                  </button>
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
