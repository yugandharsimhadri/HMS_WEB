import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Gender, Patient } from '../api/types';
import { useModalBehaviour } from '../shell/useModalBehaviour';

interface Props {
  existing: Patient | null;
  onClose: () => void;
  /** The saved row is passed alongside the message so a caller that opened
   * this to *pick* somebody — booking an appointment, say — can select the
   * patient it just created instead of making the user search for them.
   * Null on removal. Callers that only want the message ignore it. */
  onSaved: (message: string, patient: Patient | null) => void;
}

const BLOOD_GROUPS = ['', 'A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];

/** Whole years between a date of birth and today — Patient.AgeFromDob.
 * Exported so BookVisitDialog's own quick "new patient" sub-form computes
 * age the same way when it too is given a date of birth, rather than only
 * ever trusting a bare number with no unit. */
export function ageFromDob(dob: string): number {
  const birth = new Date(dob);
  const now = new Date();
  let age = now.getFullYear() - birth.getFullYear();
  const before =
    now.getMonth() < birth.getMonth() ||
    (now.getMonth() === birth.getMonth() && now.getDate() < birth.getDate());
  if (before) age -= 1;
  return Math.max(0, age);
}

export function PatientEditorDialog({ existing, onClose, onSaved }: Props) {
  const [name, setName] = useState(existing?.name ?? '');
  const [phone, setPhone] = useState(existing?.phone ?? '');
  const [age, setAge] = useState(existing && existing.age > 0 ? String(existing.age) : '');
  const [dateOfBirth, setDateOfBirth] = useState(existing?.dateOfBirth?.slice(0, 10) ?? '');
  const [bloodGroup, setBloodGroup] = useState(existing?.bloodGroup ?? '');
  const [guardianName, setGuardianName] = useState(existing?.guardianName ?? '');
  const [gender, setGender] = useState<Gender>(existing?.gender ?? 'Male');
  const [address, setAddress] = useState(existing?.address ?? '');
  const [allergies, setAllergies] = useState(existing?.allergies ?? '');

  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // A date of birth always wins over a typed age — it is the more precise of
  // the two, and re-typing an age every birthday is the reason ages on a
  // register drift.
  useEffect(() => {
    if (dateOfBirth) setAge(String(ageFromDob(dateOfBirth)));
  }, [dateOfBirth]);

  // With a DOB on file the age box has nothing left to decide, so it is
  // locked rather than left to disagree with it.
  const ageIsEditable = !dateOfBirth;

  // Guardian's name only means anything for a minor.
  const showGuardian = useMemo(() => {
    const years = Number(age);
    return Number.isFinite(years) && years > 0 && years < 18;
  }, [age]);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!name.trim()) { setError('Patient name is required.'); return; }
    if (!dateOfBirth && !age.trim()) { setError('Provide a date of birth or an age.'); return; }

    const years = dateOfBirth ? ageFromDob(dateOfBirth) : Number(age) || 0;

    setBusy(true);
    try {
      const saved = await api.post<Patient>('/api/patients', {
        id: existing?.id,
        name: name.trim(),
        phone: phone.trim(),
        age: years,
        dateOfBirth: dateOfBirth || null,
        bloodGroup: bloodGroup || null,
        // Only kept for a minor — an adult's "guardian" is a leftover.
        guardianName: years < 18 ? (guardianName.trim() || null) : null,
        gender,
        address: address.trim() || null,
        allergies: allergies.trim() || null,
      });

      onSaved(`${saved.name} saved as ${saved.patientNo}.`, saved);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the patient.');
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!existing) return;
    if (!window.confirm(`Remove ${existing.name} from the register?`)) return;

    setError(null);
    setBusy(true);
    try {
      const problem = await api.post<string | null>(`/api/patients/${existing.id}/remove`);
      if (problem) { setError(problem); setBusy(false); return; }
      onSaved(`${existing.name} removed from the register.`, null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not remove the patient.');
      setBusy(false);
    }
  };

  const cardRef = useModalBehaviour(onClose);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Patient">
      <div className="overlay-card wide" ref={cardRef}>
        <div className="overlay-head">
          <h2>{existing ? existing.name : 'New patient'}</h2>
          <button type="button" className="ghost" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body" onSubmit={onSubmit}>
          {existing && <p className="hint">Patient no. {existing.patientNo}</p>}

          <div className="settings-row">
            <label>Name<input value={name} onChange={(e) => setName(e.target.value)} required autoFocus /></label>
            <label>Phone<input value={phone} onChange={(e) => setPhone(e.target.value)} /></label>
            <label>
              Gender
              <select value={gender} onChange={(e) => setGender(e.target.value as Gender)}>
                <option value="Male">Male</option>
                <option value="Female">Female</option>
                <option value="Other">Other</option>
              </select>
            </label>
          </div>

          <div className="settings-row">
            <label>
              Date of birth
              <input type="date" value={dateOfBirth} onChange={(e) => setDateOfBirth(e.target.value)} />
            </label>
            <label>
              Age
              <input
                type="number"
                min="0"
                value={age}
                onChange={(e) => setAge(e.target.value)}
                disabled={!ageIsEditable}
              />
            </label>
            <label>
              Blood group
              <select value={bloodGroup} onChange={(e) => setBloodGroup(e.target.value)}>
                {BLOOD_GROUPS.map((g) => <option key={g} value={g}>{g || '—'}</option>)}
              </select>
            </label>
          </div>

          {!ageIsEditable && (
            <p className="hint">Age is worked out from the date of birth, so it never drifts.</p>
          )}

          {showGuardian && (
            <label>
              Guardian's name
              <input value={guardianName} onChange={(e) => setGuardianName(e.target.value)} />
            </label>
          )}

          <label>Address<input value={address} onChange={(e) => setAddress(e.target.value)} /></label>

          <label>
            Allergies
            <input
              value={allergies}
              onChange={(e) => setAllergies(e.target.value)}
              placeholder="Anything the doctor must know before prescribing"
            />
          </label>

          {error && <p className="auth-error">{error}</p>}

          <div className="overlay-actions">
            <button type="submit" disabled={busy}>{busy ? 'Saving…' : 'Save'}</button>
            {existing && (
              <button type="button" className="danger" onClick={remove} disabled={busy}>Remove</button>
            )}
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
