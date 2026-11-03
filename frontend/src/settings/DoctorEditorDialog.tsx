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
  const [qualification, setQualification] = useState(existing?.qualification ?? '');
  const [opdValidDays, setOpdValidDays] = useState(existing ? String(existing.opdValidDays ?? 0) : '0');
  const [isActive, setIsActive] = useState(existing?.isActive ?? true);

  // Says back what the number means, because "7" on its own does not tell the
  // person typing it whether they have just made reviews free for a week or
  // charged them for seven days.
  const validDays = Math.max(0, Math.floor(Number(opdValidDays) || 0));
  const validDaysNote = validDays <= 0
    ? '0 — every visit is charged.'
    : `One fee covers this doctor for ${validDays} day${validDays === 1 ? '' : 's'}; a return inside that is a free review.`;

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
        // Always sent, even when blank: an empty string clears the degrees and
        // a 0 turns the window off, which the server treats differently from
        // the fields being absent (absent leaves them alone).
        qualification: qualification.trim(),
        opdValidDays: validDays,
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
            <label>Qualification (optional)</label>
            <input
              value={qualification}
              onChange={(e) => setQualification(e.target.value)}
              placeholder="MBBS, MD (Paediatrics)"
              aria-describedby="qualification-hint"
            />
            <p className="hint" id="qualification-hint">
              Degrees as they should print beside the name on receipts, e.g. MBBS, MD (Paediatrics).
            </p>
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

          <div className="settings-row">
            <label>OPD valid for (days)</label>
            <input
              type="number"
              min={0}
              step="1"
              value={opdValidDays}
              onChange={(e) => setOpdValidDays(e.target.value)}
              aria-describedby="opd-valid-hint"
              title="0 charges every visit. 7 means one fee covers this doctor for seven days."
            />
            <p className="hint" id="opd-valid-hint">{validDaysNote}</p>
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
