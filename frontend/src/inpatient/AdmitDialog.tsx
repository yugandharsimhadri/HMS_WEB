import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { BedBoardItem, Doctor, Patient, PayerType, PaymentMode } from '../api/types';
import { useModalBehaviour } from '../shell/useModalBehaviour';
import { PatientPicker } from '../shell/PatientPicker';
import { usePatientSearch } from '../shell/usePatientSearch';
import { PatientEditorDialog } from '../opd/PatientEditorDialog';
import { nowInput } from './ip';

interface Props {
  /** A bed chosen on the board, or none. */
  bedId?: string | null;
  /** Filled in when admitting from an OPD visit. */
  patient?: Patient | null;
  doctorId?: string | null;
  visitId?: string | null;
  reason?: string | null;
  onClose: () => void;
  onAdmitted: (admissionId: string) => void;
}

/**
 * Admit a patient: one form, top to bottom in the order the desk asks for things.
 * Only the patient, the bed and the doctor are required; everything else can be
 * filled in later from the IP file.
 */
export function AdmitDialog({ bedId, patient: initialPatient, doctorId, visitId, reason: initialReason, onClose, onAdmitted }: Props) {
  const [patient, setPatient] = useState<Patient | null>(initialPatient ?? null);
  const [addingPatient, setAddingPatient] = useState(false);
  const { search, setSearch, matches, reset } = usePatientSearch({ onSingleMatch: setPatient });

  const [beds, setBeds] = useState<BedBoardItem[]>([]);
  const [doctors, setDoctors] = useState<Doctor[]>([]);

  const [bed, setBed] = useState(bedId ?? '');
  const [doctor, setDoctor] = useState(doctorId ?? '');
  const [admittedOn, setAdmittedOn] = useState(nowInput());
  const [reason, setReason] = useState(initialReason ?? '');
  const [isMlc, setIsMlc] = useState(false);

  const [attendantName, setAttendantName] = useState('');
  const [attendantRelation, setAttendantRelation] = useState('');
  const [attendantPhone, setAttendantPhone] = useState('');

  const [payer, setPayer] = useState<PayerType>('Self');
  const [insurer, setInsurer] = useState('');
  const [policyNo, setPolicyNo] = useState('');

  const [advance, setAdvance] = useState('');
  const [mode, setMode] = useState<PaymentMode>('Cash');
  const [reference, setReference] = useState('');

  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void api.get<BedBoardItem[]>('/api/inpatient/board').then((all) => {
      setBeds(all.filter((b) => b.status === 'Available'));
    });
    void api.get<Doctor[]>('/api/doctors').then((all) => {
      const active = all.filter((d) => d.isActive);
      setDoctors(active);
      if (!doctorId && active.length === 1) setDoctor(active[0].id);
    });
  }, [doctorId]);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!patient) { setError('Choose the patient.'); return; }
    if (!bed) { setError('Choose a bed.'); return; }
    if (!doctor) { setError('Choose the treating doctor.'); return; }

    const amount = Number(advance) || 0;

    setBusy(true);
    try {
      const result = await api.post<{ id: string }>('/api/inpatient/admissions', {
        patientId: patient.id,
        doctorId: doctor,
        bedId: bed,
        admittedOn,
        reason: reason.trim() || null,
        attendantName: attendantName.trim() || null,
        attendantRelation: attendantRelation.trim() || null,
        attendantPhone: attendantPhone.trim() || null,
        payerType: payer,
        insurerName: payer === 'Insurance' ? insurer.trim() || null : null,
        policyNo: payer === 'Insurance' ? policyNo.trim() || null : null,
        isMlc,
        visitId: visitId ?? null,
        advanceAmount: amount,
        advanceMode: amount > 0 ? mode : null,
        advanceReference: amount > 0 ? reference.trim() || null : null,
      });
      onAdmitted(result.id);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not admit the patient.');
      setBusy(false);
    }
  };

  const cardRef = useModalBehaviour(onClose);

  // Beds grouped by ward, the way the board shows them.
  const wards = [...new Set(beds.map((b) => b.ward))];

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Admit patient">
      <div className="overlay-card wide" ref={cardRef}>
        <div className="overlay-head">
          <h2>Admit patient</h2>
          <button type="button" className="ghost" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body ip-form" onSubmit={onSubmit}>
          <fieldset>
            <legend>Patient</legend>
            {patient ? (
              <div className="ip-picked">
                <strong>{patient.name}</strong>
                <span className="hint"> · {patient.patientNo} · {patient.ageText} {patient.gender.charAt(0)}{patient.phone && ` · ${patient.phone}`}</span>
                {!initialPatient && (
                  <button type="button" className="ghost" onClick={() => { setPatient(null); reset(); }}>Change</button>
                )}
              </div>
            ) : (
              <PatientPicker
                group="Admit"
                search={search}
                onSearchChange={setSearch}
                matches={matches}
                onPick={(p) => { setPatient(p); reset(); }}
                onNewPatient={() => setAddingPatient(true)}
              />
            )}
          </fieldset>

          <fieldset>
            <legend>Admission</legend>
            <div className="ip-grid">
              <label>Bed
                <select id="admit-bed" value={bed} onChange={(e) => setBed(e.target.value)} required>
                  <option value="">Choose a bed</option>
                  {wards.map((w) => (
                    <optgroup key={w} label={w}>
                      {beds.filter((b) => b.ward === w).map((b) => (
                        <option key={b.bedId} value={b.bedId}>{b.bedNo} · {b.roomType} · ₹{b.dailyRent}/day</option>
                      ))}
                    </optgroup>
                  ))}
                </select>
              </label>
              <label>Treating doctor
                <select id="admit-doctor" value={doctor} onChange={(e) => setDoctor(e.target.value)} required>
                  <option value="">Choose</option>
                  {doctors.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
                </select>
              </label>
              <label>Admitted on
                <input id="admit-on" type="datetime-local" value={admittedOn} onChange={(e) => setAdmittedOn(e.target.value)} required />
              </label>
              <label className="ip-span">Reason / provisional diagnosis
                <input id="admit-reason" value={reason} onChange={(e) => setReason(e.target.value)} />
              </label>
              <label className="checkbox-label">
                <input id="admit-mlc" type="checkbox" checked={isMlc} onChange={() => setIsMlc(!isMlc)} />
                <span>Medico-legal case (MLC)</span>
              </label>
            </div>
            {beds.length === 0 && <p className="hint">No bed is free. Mark a cleaned bed ready on the bed board.</p>}
          </fieldset>

          <fieldset>
            <legend>Attendant</legend>
            <div className="ip-grid">
              <label>Name<input id="admit-att-name" value={attendantName} onChange={(e) => setAttendantName(e.target.value)} /></label>
              <label>Relation<input id="admit-att-rel" value={attendantRelation} onChange={(e) => setAttendantRelation(e.target.value)} placeholder="Mother, son…" /></label>
              <label>Phone<input id="admit-att-phone" value={attendantPhone} onChange={(e) => setAttendantPhone(e.target.value)} inputMode="tel" /></label>
            </div>
          </fieldset>

          <fieldset>
            <legend>Payer</legend>
            <div className="ip-grid">
              <label>Paid by
                <select id="admit-payer" value={payer} onChange={(e) => setPayer(e.target.value as PayerType)}>
                  <option value="Self">Self (patient)</option>
                  <option value="Insurance">Insurance / TPA</option>
                </select>
              </label>
              {payer === 'Insurance' && (
                <>
                  <label>Insurance / TPA<input id="admit-insurer" value={insurer} onChange={(e) => setInsurer(e.target.value)} /></label>
                  <label>Policy / card no.<input id="admit-policy" value={policyNo} onChange={(e) => setPolicyNo(e.target.value)} /></label>
                </>
              )}
            </div>
          </fieldset>

          <fieldset>
            <legend>Advance <span className="hint">optional</span></legend>
            <div className="ip-grid">
              <label>Amount (₹)<input id="admit-advance" type="number" min="0" step="1" value={advance} onChange={(e) => setAdvance(e.target.value)} /></label>
              <label>Paid by
                <select id="admit-mode" value={mode} onChange={(e) => setMode(e.target.value as PaymentMode)} disabled={!Number(advance)}>
                  <option value="Cash">Cash</option>
                  <option value="Upi">UPI</option>
                  <option value="Card">Card</option>
                </select>
              </label>
              {mode !== 'Cash' && Number(advance) > 0 && (
                <label>Transaction no.<input id="admit-ref" value={reference} onChange={(e) => setReference(e.target.value)} /></label>
              )}
            </div>
          </fieldset>

          {error && <p className="auth-error">{error}</p>}
          <div className="settings-actions">
            <button type="submit" disabled={busy}>{busy ? 'Admitting…' : 'Admit'}</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>

      {addingPatient && (
        <PatientEditorDialog
          existing={null}
          onClose={() => setAddingPatient(false)}
          onSaved={(_message, saved) => {
            if (saved) setPatient(saved);
            setAddingPatient(false);
            reset();
          }}
        />
      )}
    </div>
  );
}
