import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Doctor, FeeCover, Gender, Patient, Visit } from '../api/types';
import { useModalBehaviour } from '../shell/useModalBehaviour';
import { agePartsFromDob, parseMonths } from './PatientEditorDialog';

/** Digits and phone punctuation only, and enough of them to be a number —
 * the desktop's OpdService.LooksLikePhone, which decides whether a search
 * term goes into the name box or the phone box of the new-patient form. */
function looksLikePhone(term: string): boolean {
  const trimmed = term.trim();
  if (!trimmed) return false;
  if (!/^[\d\s\-+()]+$/.test(trimmed)) return false;
  return (trimmed.match(/\d/g) ?? []).length >= 6;
}

interface Props {
  doctors: Doctor[];
  preferredDoctorId: string | null;
  date: string; // yyyy-MM-dd
  onClose: () => void;
  onBooked: (message: string) => void;
}

export function BookVisitDialog({ doctors, preferredDoctorId, date, onClose, onBooked }: Props) {
  const [search, setSearch] = useState('');
  const [matches, setMatches] = useState<Patient[]>([]);
  const [searched, setSearched] = useState(false);
  const [selectedPatient, setSelectedPatient] = useState<Patient | null>(null);

  const [addingPatient, setAddingPatient] = useState(false);
  const [newName, setNewName] = useState('');
  const [newPhone, setNewPhone] = useState('');
  const [newAge, setNewAge] = useState('');
  const [newMonths, setNewMonths] = useState('');
  const [newDob, setNewDob] = useState('');
  const [newGender, setNewGender] = useState<Gender>('Male');

  // A date of birth always wins over a typed age, same as the full patient
  // editor — walk-in booking is quick on purpose, but a bare "Age" box with
  // no unit on it is how an infant brought in for their first visit ends up
  // on the register as 6 years old instead of 6 months, because a clerk who
  // types what they were told has no way to say which unit it was in.
  const newAgeIsEditable = !newDob;

  const [doctorId, setDoctorId] = useState(preferredDoctorId ?? doctors[0]?.id ?? '');
  const [time, setTime] = useState(() => new Date().toTimeString().slice(0, 5));
  const [complaint, setComplaint] = useState('');
  const [fee, setFee] = useState('');
  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // The earlier paid visit that still covers this patient with this doctor,
  // when the doctor runs an OPD validity window and the patient is inside it.
  // Null the rest of the time, which is every booking until a doctor turns the
  // scheme on in Settings.
  const [cover, setCover] = useState<FeeCover | null>(null);

  // Booking from a doctor's tab defaults to that doctor, and the fee follows
  // whichever doctor is chosen — and, when the patient is still inside that
  // doctor's validity window, drops to nothing.
  //
  // A default, not a lock. The fee box stays editable: a return inside the
  // window for a genuinely new complaint is usually still chargeable, and that
  // is a judgement no rule here can make. Whether the visit is recorded as a
  // free review is decided by the server from what the fee says when Book is
  // pressed, not by what this suggested.
  const patientId = selectedPatient?.id ?? null;
  useEffect(() => {
    const doctor = doctors.find((d) => d.id === doctorId);
    if (!doctor) return;

    if (!patientId || doctor.opdValidDays <= 0) {
      setCover(null);
      setFee(String(doctor.consultationFee));
      return;
    }

    let stale = false;
    api
      .get<{ cover: FeeCover | null }>(
        `/api/visits/fee-cover?patientId=${patientId}&doctorId=${doctor.id}&date=${date}`,
      )
      .then((r) => {
        if (stale) return;
        setCover(r.cover);
        setFee(r.cover ? '0' : String(doctor.consultationFee));
      })
      .catch(() => {
        // Not being able to ask must never stop a booking: fall back to the
        // ordinary fee and let the server decide when the visit is booked.
        if (stale) return;
        setCover(null);
        setFee(String(doctor.consultationFee));
      });

    return () => {
      stale = true;
    };
  }, [doctorId, doctors, patientId, date]);

  const short = (iso: string | null) =>
    iso ? new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' }) : '';

  // Why this booking is free, in the terms the desk would use to explain it to
  // the person standing in front of them.
  const coverNote = cover
    ? `Review — ₹${cover.fee.toFixed(0)} paid on ${short(cover.paidOn)}` +
      (cover.receiptNo ? ` (receipt ${cover.receiptNo})` : '') +
      `, free with this doctor until ${cover.freeUntil
        ? new Date(cover.freeUntil).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })
        : ''}.`
    : '';

  useEffect(() => {
    if (!newDob) return;
    const parts = agePartsFromDob(newDob);
    setNewAge(String(parts.years));
    setNewMonths(parts.months > 0 ? String(parts.months) : '');
  }, [newDob]);

  const onFind = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    const found = await api.get<Patient[]>(`/api/patients?term=${encodeURIComponent(search)}&take=25`);
    setMatches(found);
    setSearched(true);
    setSelectedPatient(null);

    if (found.length === 0 && search.trim()) {
      // Nobody matches, so offer to add them — pre-filling whichever box the
      // search term actually looks like, so it isn't retyped.
      setAddingPatient(true);
      if (looksLikePhone(search)) setNewPhone(search.trim());
      else setNewName(search.trim());
      setStatus(`No one matches '${search}'. Add them as a new patient.`);
      return;
    }

    setAddingPatient(false);

    // Siblings share a phone number, so say how many came back — the operator
    // has to pick, and auto-picking sends the wrong child in to the doctor.
    setStatus(
      found.length === 0
        ? ''
        : found.length === 1
          ? `${found[0].name} found. Select and book.`
          : looksLikePhone(search)
            ? `${found.length} people are registered on this number. Select which one is here.`
            : `${found.length} matches. Select which one is here.`,
    );
  };

  const onBook = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);

    // Matches exist but none was picked: creating a new record here would
    // quietly duplicate a child who is already on the register.
    if (!selectedPatient && !addingPatient && matches.length > 0) {
      setError(
        matches.length === 1
          ? `Select ${matches[0].name} from the list, or choose + New patient.`
          : `${matches.length} people match that. Select which one, or choose + New patient.`,
      );
      return;
    }

    if (!doctorId) {
      setError('Add a doctor under Settings → Doctors before booking a visit.');
      return;
    }

    setBusy(true);
    try {
      let patient = selectedPatient;

      if (addingPatient || !patient) {
        if (!newName.trim()) {
          setError("Enter the patient's name, or pick an existing patient from the list.");
          setBusy(false);
          return;
        }
        patient = await api.post<Patient>('/api/patients', {
          name: newName.trim(),
          phone: newPhone.trim(),
          age: newDob ? agePartsFromDob(newDob).years : Number(newAge) || 0,
          ageMonths: newDob ? agePartsFromDob(newDob).months : parseMonths(newMonths),
          dateOfBirth: newDob || null,
          gender: newGender,
        });
      }

      // Sent as a naive local date-time, deliberately NOT .toISOString():
      // that converts to UTC, and the server stores and returns clinic-local
      // times with no offset (the desktop's DateTime, ported as-is). Round-
      // tripping through UTC shifted every booking by the timezone offset —
      // a 08:58 visit came back reading 03:28. The real fix is a per-tenant
      // timezone (SAAS_MIGRATION.md finding 4); until that exists, client and
      // server have to agree on "local", and this is that agreement.
      const scheduledOn = `${date}T${time || '00:00'}:00`;

      const visit = await api.post<Visit>('/api/visits', {
        patientId: patient.id,
        doctorId,
        scheduledOn,
        complaint: complaint.trim() || null,
        fee: Number(fee) || 0,
      });

      onBooked(
        visit.isReview
          ? `Token ${visit.tokenNo} booked for ${patient.name} — review, no fee due.`
          : `Token ${visit.tokenNo} booked for ${patient.name}.`,
      );
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not book the visit.');
    } finally {
      setBusy(false);
    }
  };

  const clear = () => {
    setSearch('');
    setMatches([]);
    setSearched(false);
    setSelectedPatient(null);
    setAddingPatient(false);
    setNewName('');
    setNewPhone('');
    setNewAge('');
    setNewMonths('');
    setNewDob('');
    setComplaint('');
    setTime(new Date().toTimeString().slice(0, 5));
    setStatus('');
    setError(null);
  };

  const cardRef = useModalBehaviour(onClose);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Book a visit">
      <div className="overlay-card wide" ref={cardRef}>
        <div className="overlay-head">
          <h2>Book a visit — {new Date(`${date}T00:00:00`).toLocaleDateString(undefined, {
            weekday: 'short', day: '2-digit', month: 'short',
          })}</h2>
          <button type="button" className="ghost" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body" onSubmit={onBook}>
          <section>
            <h3>1 · Who is here</h3>
            <div className="inline-form">
              <input
                placeholder="Name or phone number"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                // Enter here means "find this person", not "book the visit".
                // The box sits inside the booking form, so without this the
                // browser's implicit submit runs onBook against a patient
                // nobody has chosen yet, and the operator gets told to enter
                // a name they just typed. A nested form would be invalid
                // HTML, so the key is claimed on the field itself.
                onKeyDown={(e) => {
                  if (e.key !== 'Enter') return;
                  e.preventDefault();
                  void onFind(e);
                }}
                autoFocus
              />
              <button type="button" onClick={onFind}>Find</button>
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  setAddingPatient(true);
                  setSelectedPatient(null);
                }}
              >
                + New patient
              </button>
            </div>

            {status && <p className="hint">{status}</p>}

            {matches.length > 0 && (
              <ul className="pick-list">
                {matches.map((p) => (
                  <li key={p.id}>
                    <button
                      type="button"
                      className={selectedPatient?.id === p.id ? 'pick selected' : 'pick'}
                      onClick={() => {
                        setSelectedPatient(p);
                        setAddingPatient(false);
                      }}
                    >
                      <strong>{p.name}</strong>
                      <span className="hint">
                        {' '}· {p.patientNo} · {`${p.ageText} `}
                        {p.gender.charAt(0)} · {p.phone || 'no phone'}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}

            {searched && matches.length === 0 && !addingPatient && (
              <p className="hint">No matches.</p>
            )}

            {addingPatient && (
              <div className="sub-card">
                <div className="inline-form">
                  <input placeholder="Name" value={newName} onChange={(e) => setNewName(e.target.value)} />
                  <input placeholder="Phone" value={newPhone} onChange={(e) => setNewPhone(e.target.value)} />
                  <label>
                    Date of birth
                    <input type="date" value={newDob} onChange={(e) => setNewDob(e.target.value)} />
                  </label>
                  <input
                    placeholder="Age (years)"
                    type="number"
                    min="0"
                    value={newAge}
                    onChange={(e) => setNewAge(e.target.value)}
                    disabled={!newAgeIsEditable}
                  />
                  {/* Optional - for infants and small children. */}
                  <input
                    placeholder="Months (optional)"
                    type="number"
                    min="0"
                    max="11"
                    value={newMonths}
                    onChange={(e) => setNewMonths(e.target.value)}
                    disabled={!newAgeIsEditable}
                  />
                  <select value={newGender} onChange={(e) => setNewGender(e.target.value as Gender)}>
                    <option value="Male">Male</option>
                    <option value="Female">Female</option>
                    <option value="Other">Other</option>
                  </select>
                  <button type="button" className="ghost" onClick={() => setAddingPatient(false)}>
                    Cancel
                  </button>
                </div>
                {!newAgeIsEditable && (
                  <p className="hint">Age is worked out from the date of birth, so it never drifts.</p>
                )}
              </div>
            )}
          </section>

          <section>
            <h3>2 · Doctor and time</h3>
            <div className="inline-form">
              <select value={doctorId} onChange={(e) => setDoctorId(e.target.value)}>
                <option value="">Doctor…</option>
                {doctors.map((d) => (
                  <option key={d.id} value={d.id}>{d.name}</option>
                ))}
              </select>
              <input type="time" value={time} onChange={(e) => setTime(e.target.value)} />
              <input
                placeholder="Fee"
                type="number"
                min="0"
                step="0.01"
                value={fee}
                onChange={(e) => setFee(e.target.value)}
              />
            </div>

            {/* Only ever seen when the doctor runs an OPD validity window and
                this patient is inside it. Says which payment covers them and
                until when, so the desk can answer the parent without opening
                another screen — and the fee box above stays editable, because
                a return for a new complaint is usually still chargeable. */}
            {cover && (
              <p className="hint cover-note" role="status" data-testid="opd-fee-cover-note">
                {coverNote}
              </p>
            )}
          </section>

          <section>
            <h3>3 · What is wrong</h3>
            <input
              className="full"
              placeholder="Complaint"
              value={complaint}
              onChange={(e) => setComplaint(e.target.value)}
            />
          </section>

          {error && <p className="auth-error">{error}</p>}

          <div className="overlay-actions">
            <button type="submit" disabled={busy}>{busy ? 'Booking…' : 'Book visit'}</button>
            <button type="button" className="ghost" onClick={clear}>Clear</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
