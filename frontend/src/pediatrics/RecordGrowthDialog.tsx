import { useState, type FormEvent } from 'react';

export interface GrowthDraft {
  measuredOn: string;
  weightKg: number | null;
  heightCm: number | null;
  headCircumferenceCm: number | null;
}

interface Props {
  onClose: () => void;
  onSave: (draft: GrowthDraft) => void;
}

const today = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

const num = (v: string): number | null => (v.trim() === '' ? null : Number(v));

/** Growth is never billed, so this only ever records — nothing joins a bill,
 * unlike the vaccination form. */
export function RecordGrowthDialog({ onClose, onSave }: Props) {
  const [measuredOn, setMeasuredOn] = useState(today);
  const [weight, setWeight] = useState('');
  const [height, setHeight] = useState('');
  const [head, setHead] = useState('');
  const [error, setError] = useState<string | null>(null);

  const submit = (e: FormEvent) => {
    e.preventDefault();

    const draft = {
      measuredOn,
      weightKg: num(weight),
      heightCm: num(height),
      headCircumferenceCm: num(head),
    };

    // Each of the three is optional on its own; none of them together is
    // not a measurement.
    if (draft.weightKg === null && draft.heightCm === null && draft.headCircumferenceCm === null) {
      setError('Enter at least one measurement.');
      return;
    }

    onSave(draft);
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Record growth">
      <div className="overlay-card">
        <div className="overlay-head">
          <h2>Record growth measurement</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body settings-form" onSubmit={submit}>
          {error && <p className="auth-error">{error}</p>}

          <div className="settings-row">
            <label>Measured on</label>
            <input type="date" value={measuredOn} onChange={(e) => setMeasuredOn(e.target.value)} />
          </div>
          <div className="settings-row">
            <label>Weight (kg)</label>
            <input type="number" min={0} step="0.01" value={weight} onChange={(e) => setWeight(e.target.value)} />
          </div>
          <div className="settings-row">
            <label>Height / length (cm)</label>
            <input type="number" min={0} step="0.1" value={height} onChange={(e) => setHeight(e.target.value)} />
          </div>
          <div className="settings-row">
            <label>Head circumference (cm)</label>
            <input type="number" min={0} step="0.1" value={head} onChange={(e) => setHead(e.target.value)} />
          </div>

          <div className="overlay-actions">
            <button type="submit">Save</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
