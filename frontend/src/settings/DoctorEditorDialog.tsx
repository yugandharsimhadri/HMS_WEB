import { useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Doctor } from '../api/types';
import { useModalBehaviour } from '../shell/useModalBehaviour';

interface Props {
  existing: Doctor | null;
  onClose: () => void;
  onSaved: (message: string) => void;
}

export function DoctorEditorDialog({ existing, onClose, onSaved }: Props) {
  const [name, setName] = useState(existing?.name ?? '');
  const [speciality, setSpeciality] = useState(existing?.speciality ?? '');
  const [registrationNo, setRegistrationNo] = useState(existing?.registrationNo ?? '');
  const [consultationFee, setConsultationFee] = useState(existing ? String(existing.consultationFee) : '');
  const [isActive, setIsActive] = useState(existing?.isActive ?? true);

  const [nameMissing, setNameMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const save = async (e: FormEvent) => {
    e.preventDefault();

    if (!name.trim()) {
      setNameMissing(true);
      setError("The doctor's name is required.");
      return;
    }

    setNameMissing(false);
    setError(null);
    setBusy(true);
    try {
      const saved = await api.post<Doctor>('/api/doctors', {
        id: existing?.id ?? null,
        name: name.trim(),
        speciality: speciality.trim() || null,
        registrationNo: registrationNo.trim() || null,
        consultationFee: Number(consultationFee) || 0,
        isActive,
      });
      onSaved(`${saved.name} saved.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the doctor.');
      setBusy(false);
    }
  };

  const cardRef = useModalBehaviour(onClose);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Doctor">
      <div className="overlay-card" ref={cardRef}>
        <div className="overlay-head">
          <h2>{existing ? existing.name : 'New doctor'}</h2>
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
              placeholder="Dr. First Last"
              autoFocus
            />
          </div>

          <div className="settings-row">
            <label>Speciality</label>
            <input
              value={speciality}
              onChange={(e) => setSpeciality(e.target.value)}
              placeholder="Pediatrics, General Medicine…"
            />
          </div>

          <div className="settings-row">
            <label>Registration no.</label>
            <input
              value={registrationNo}
              onChange={(e) => setRegistrationNo(e.target.value)}
              placeholder="State medical council registration number"
            />
          </div>

          <div className="settings-row">
            <label>Consultation fee</label>
            <input
              type="number"
              min={0}
              step="0.01"
              value={consultationFee}
              onChange={(e) => setConsultationFee(e.target.value)}
            />
          </div>

          <label className="checkbox-label">
            <input type="checkbox" checked={isActive} onChange={() => setIsActive(!isActive)} />
            <span>Active — an inactive doctor stays on past visits but leaves every booking picker</span>
          </label>

          <div className="overlay-actions">
            <button type="submit" disabled={busy}>Save</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
