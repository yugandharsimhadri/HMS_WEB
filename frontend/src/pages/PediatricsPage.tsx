import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, ApiError, openPdf } from '../api/client';
import type {
  GrowthMeasurement,
  ImmunizationCardRow,
  ImmunizationStatus,
  PaymentMode,
  Patient,
  Procedure,
  ProcedureBillResult,
  VaccinationRecord,
  VaccineMaster,
} from '../api/types';
import type { GrowthMetric } from '../clinical/growthReference';
import { PatientEditorDialog } from '../opd/PatientEditorDialog';
import { GrowthChart } from '../pediatrics/GrowthChart';
import { RecordGrowthDialog, type GrowthDraft } from '../pediatrics/RecordGrowthDialog';
import { RecordVaccinationDialog, type VaccinationDraft } from '../pediatrics/RecordVaccinationDialog';

/** One line on the bill being built. A vaccine line carries the draft dose
 * it will record once the bill saves — removing the line drops both, which
 * is the whole point: a dose is on record only once it is on a saved bill. */
interface BillRow {
  key: string;
  procedureId: string | null;
  procedureName: string;
  kind: 'Procedure' | 'Vaccine';
  price: number;
  quantity: number;
  vaccination?: VaccinationDraft;
}

const PAYMENT_MODES: PaymentMode[] = ['Cash', 'Upi', 'Card'];

const METRICS: { id: GrowthMetric; label: string }[] = [
  { id: 'Weight', label: 'Weight' },
  { id: 'Height', label: 'Height' },
  { id: 'HeadCircumference', label: 'Head circumference' },
];

/** The recommended age as the schedule states it — "Birth", "6 weeks",
 * "9 months" — rather than a raw day count nobody reads a schedule in. */
function scheduleAge(days: number): string {
  if (days <= 0) return 'Birth';
  if (days < 60) return `${Math.round(days / 7)} weeks`;
  if (days < 730) return `${Math.round(days / 30.4368)} months`;
  return `${(days / 365.25).toFixed(days % 365 === 0 ? 0 : 1)} years`;
}

const STATUS_LABELS: Record<ImmunizationStatus, string> = {
  Given: 'Given',
  Overdue: 'Overdue',
  DueSoon: 'Due soon',
  Upcoming: 'Upcoming',
};

type Tab = 'growth' | 'care' | 'immunization';

export function PediatricsPage() {
  const [tab, setTab] = useState<Tab>('growth');

  // ── The one patient, chosen above the tabs ─────────────────────────────
  const [search, setSearch] = useState('');
  const [matches, setMatches] = useState<Patient[]>([]);
  const [patient, setPatient] = useState<Patient | null>(null);
  const [addingPatient, setAddingPatient] = useState(false);

  // ── Per-patient data ───────────────────────────────────────────────────
  const [growth, setGrowth] = useState<GrowthMeasurement[]>([]);
  const [history, setHistory] = useState<VaccinationRecord[]>([]);
  const [card, setCard] = useState<ImmunizationCardRow[]>([]);
  const [metric, setMetric] = useState<GrowthMetric>('Weight');

  // ── Catalogues ─────────────────────────────────────────────────────────
  const [procedures, setProcedures] = useState<Procedure[]>([]);
  const [vaccines, setVaccines] = useState<VaccineMaster[]>([]);

  // ── The running bill ───────────────────────────────────────────────────
  const [lines, setLines] = useState<BillRow[]>([]);
  const [discount, setDiscount] = useState('0');
  const [paymentMode, setPaymentMode] = useState<PaymentMode>('Cash');
  const [transactionNo, setTransactionNo] = useState('');
  const [referredBy, setReferredBy] = useState('');

  const [addingProcedure, setAddingProcedure] = useState(false);
  const [procedureId, setProcedureId] = useState('');
  const [procedureQty, setProcedureQty] = useState('1');
  const [procedureMissing, setProcedureMissing] = useState(false);

  const [recordingVaccine, setRecordingVaccine] = useState(false);
  const [preselectVaccineId, setPreselectVaccineId] = useState<string | null>(null);

  const [recordingGrowth, setRecordingGrowth] = useState(false);

  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // ── Loading ────────────────────────────────────────────────────────────

  useEffect(() => {
    void api.get<Procedure[]>('/api/pediatrics/procedures?department=Pediatrics&activeOnly=true')
      .then(setProcedures).catch(() => {});
    void api.get<VaccineMaster[]>('/api/pediatrics/vaccines?activeOnly=true')
      .then(setVaccines).catch(() => {});
  }, []);

  /** Everything about this child, in one go — the desktop loads all four
   * the moment a patient is picked, and the tabs each read from them. */
  const loadPatientData = useCallback(async (id: string) => {
    const [g, h, c] = await Promise.all([
      api.get<GrowthMeasurement[]>(`/api/pediatrics/patients/${id}/growth`).catch(() => []),
      api.get<VaccinationRecord[]>(`/api/pediatrics/patients/${id}/vaccinations`).catch(() => []),
      api.get<ImmunizationCardRow[]>(`/api/pediatrics/patients/${id}/immunization-card`).catch(() => []),
    ]);
    setGrowth(g);
    setHistory(h);
    setCard(c);
  }, []);

  const selectPatient = useCallback((p: Patient) => {
    setPatient(p);
    setSearch('');
    setMatches([]);
    setError(null);
    // Whatever metric was showing for the last child may not exist for this
    // one; weight is the reading most likely on file, so it beats landing
    // on a blank chart.
    setMetric('Weight');
    void loadPatientData(p.id);
  }, [loadPatientData]);

  const findPatients = useCallback(async (term: string) => {
    if (!term.trim()) {
      setMatches([]);
      return;
    }
    try {
      const found = await api.get<Patient[]>(`/api/patients?term=${encodeURIComponent(term.trim())}&take=20`);
      setMatches(found);
      if (found.length === 1) selectPatient(found[0]);
    } catch {
      setMatches([]);
    }
  }, [selectPatient]);

  useEffect(() => {
    if (patient) return;
    const handle = setTimeout(() => void findPatients(search), 250);
    return () => clearTimeout(handle);
  }, [search, findPatients, patient]);

  // ── The bill ───────────────────────────────────────────────────────────

  const total = useMemo(() => lines.reduce((sum, l) => sum + l.price * l.quantity, 0), [lines]);
  const finalAmount = Math.max(0, total - (Number(discount) || 0));

  const newBill = () => {
    setLines([]);
    setDiscount('0');
    setPaymentMode('Cash');
    setTransactionNo('');
    setReferredBy('');
    setError(null);
  };

  const setLine = (key: string, patch: Partial<BillRow>) =>
    setLines((current) => current.map((l) => (l.key === key ? { ...l, ...patch } : l)));

  /** Removing a vaccine line drops its draft dose with it — nothing was
   * written, so there is nothing to undo. */
  const removeLine = (key: string) => setLines((current) => current.filter((l) => l.key !== key));

  const addProcedure = () => {
    const procedure = procedures.find((p) => p.id === procedureId);
    if (!procedure) {
      setProcedureMissing(true);
      return;
    }
    if (lines.some((l) => l.procedureId === procedure.id)) {
      setError(`${procedure.name} is already on this bill.`);
      setAddingProcedure(false);
      return;
    }

    setLines((current) => [
      ...current,
      {
        key: crypto.randomUUID(),
        procedureId: procedure.id,
        procedureName: procedure.name,
        kind: 'Procedure',
        price: procedure.price,
        quantity: Math.max(1, Number(procedureQty) || 1),
      },
    ]);
    setAddingProcedure(false);
    setProcedureId('');
    setProcedureQty('1');
    setError(null);
  };

  const addVaccination = (draft: VaccinationDraft) => {
    setLines((current) => [
      ...current,
      {
        key: crypto.randomUUID(),
        procedureId: null,
        procedureName: `${draft.vaccine.name} (dose ${draft.vaccine.doseNumber})`,
        kind: 'Vaccine',
        price: draft.price,
        quantity: 1,
        vaccination: draft,
      },
    ]);
    setRecordingVaccine(false);
    setPreselectVaccineId(null);
    setStatus(
      `${draft.vaccine.name} added to the bill at ₹${draft.price.toFixed(2)} — recorded once the bill is saved.`,
    );
  };

  const saveBill = async (print: boolean) => {
    setError(null);

    if (!patient) {
      setError('Select a patient first.');
      return;
    }
    if (lines.length === 0) {
      setError('Add at least one procedure to the bill.');
      return;
    }

    setBusy(true);
    try {
      const result = await api.post<ProcedureBillResult>('/api/pediatrics/bills', {
        id: null,
        patientId: patient.id,
        paymentMode,
        transactionNo: transactionNo.trim() || null,
        discount: Number(discount) || 0,
        visitId: null,
        referredBy: referredBy.trim() || null,
        lines: lines.map((l) => ({
          procedureId: l.procedureId,
          procedureName: l.procedureName,
          price: l.price,
          quantity: l.quantity,
        })),
        // Sent with the bill, not before it: the dose follows the money.
        vaccinations: lines
          .filter((l) => l.kind === 'Vaccine' && l.vaccination)
          .map((l) => ({
            vaccineId: l.vaccination!.vaccine.id,
            givenOn: `${l.vaccination!.givenOn}T00:00:00`,
            site: l.vaccination!.site,
            administeredBy: l.vaccination!.administeredBy,
            productId: l.vaccination!.productId,
            batchId: l.vaccination!.batchId,
          })),
      });

      setStatus(
        `Bill ${result.billNo} saved · ₹${result.finalAmount.toFixed(2)}` +
        (result.vaccinationsRecorded > 0 ? ` · ${result.vaccinationsRecorded} dose(s) recorded` : ''),
      );
      // A dose that could not be recorded is not a failed bill, but it must
      // not pass quietly — the card is now short a dose the bill charged for.
      if (result.warning) setError(result.warning);

      if (print) await openPdf(`/api/print/procedure-bill/${result.id}`);

      newBill();
      await loadPatientData(patient.id);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the bill.');
    } finally {
      setBusy(false);
    }
  };

  const recordGrowth = async (draft: GrowthDraft) => {
    if (!patient) return;
    setError(null);
    try {
      await api.post(`/api/pediatrics/patients/${patient.id}/growth`, {
        measuredOn: `${draft.measuredOn}T00:00:00`,
        weightKg: draft.weightKg,
        heightCm: draft.heightCm,
        headCircumferenceCm: draft.headCircumferenceCm,
      });
      setRecordingGrowth(false);
      setStatus('Measurement recorded.');
      await loadPatientData(patient.id);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not record the measurement.');
    }
  };

  const counts = useMemo(() => ({
    given: card.filter((r) => r.status === 'Given').length,
    overdue: card.filter((r) => r.status === 'Overdue').length,
    dueSoon: card.filter((r) => r.status === 'DueSoon').length,
    upcoming: card.filter((r) => r.status === 'Upcoming').length,
  }), [card]);

  const subtitle = lines.length === 0
    ? 'No procedures on this bill'
    : `${lines.length} item(s) · ₹${finalAmount.toFixed(2)}`;

  const requirePatient = (then: () => void) => {
    if (!patient) {
      setError('Select a patient first.');
      return;
    }
    then();
  };

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Pediatrics</h1>
          <p className="hint">{subtitle}</p>
        </div>
      </div>

      {/* One patient, above the tabs — billing and vaccination treat the
          same child on the same visit, so asking twice is a chance to get
          it wrong. */}
      <section className="card">
        {patient ? (
          <div className="counter-selected">
            <strong>{patient.name}</strong>
            <span className="hint">
              {' · '}{patient.patientNo}{' · '}{patient.age}{patient.gender.charAt(0)}
              {patient.dateOfBirth
                ? ` · DOB ${new Date(patient.dateOfBirth).toLocaleDateString()}`
                : ' · no date of birth on file'}
            </span>
            <button
              type="button"
              className="ghost"
              onClick={() => { setPatient(null); setSearch(''); setMatches([]); setGrowth([]); setHistory([]); setCard([]); }}
            >
              Change patient
            </button>
            <button
              type="button"
              className="ghost"
              onClick={() => void openPdf(`/api/print/vaccination-history/${patient.id}`)}
            >
              Print vaccination record
            </button>
          </div>
        ) : (
          <div className="patient-picker">
            <div className="inline-form">
              <input
                placeholder="Name or phone number"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
              <button type="button" onClick={() => setAddingPatient(true)}>+ New patient</button>
            </div>
            {matches.length > 0 && (
              <ul className="pick-list">
                {matches.map((p) => (
                  <li key={p.id}>
                    <button type="button" onClick={() => selectPatient(p)}>
                      {p.name} · {p.patientNo} · {p.age}{p.gender.charAt(0)}{p.phone && ` · ${p.phone}`}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </section>

      <div className="tabs">
        <button type="button" className={tab === 'growth' ? 'tab active' : 'tab'} onClick={() => setTab('growth')}>
          Growth
        </button>
        <button type="button" className={tab === 'care' ? 'tab active' : 'tab'} onClick={() => setTab('care')}>
          Care
        </button>
        <button type="button" className={tab === 'immunization' ? 'tab active' : 'tab'} onClick={() => setTab('immunization')}>
          Immunization
        </button>
      </div>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      {tab === 'growth' && (
        <>
          <section className="card">
            <h2>Growth chart</h2>
            <div className="inline-form">
              {METRICS.map((m) => (
                <button
                  key={m.id}
                  type="button"
                  className={metric === m.id ? 'tab active' : 'tab'}
                  onClick={() => setMetric(m.id)}
                >
                  {m.label}
                </button>
              ))}
              <button type="button" onClick={() => requirePatient(() => setRecordingGrowth(true))}>
                + Record growth
              </button>
            </div>

            {patient
              ? <GrowthChart patient={patient} history={growth} metric={metric} />
              : <p className="hint">Choose a patient to see their growth.</p>}
          </section>

          <section className="card">
            <h2>Measurements</h2>
            <table>
              <thead>
                <tr>
                  <th>Measured on</th><th>Weight (kg)</th><th>Height (cm)</th>
                  <th>Head (cm)</th><th>BMI</th>
                </tr>
              </thead>
              <tbody>
                {[...growth].sort((a, b) => b.measuredOn.localeCompare(a.measuredOn)).map((g) => (
                  <tr key={g.id}>
                    <td>{new Date(g.measuredOn).toLocaleDateString()}</td>
                    <td>{g.weightKg ?? '—'}</td>
                    <td>{g.heightCm ?? '—'}</td>
                    <td>{g.headCircumferenceCm ?? '—'}</td>
                    {/* Blank unless both weight and height were taken on the
                        same visit — a BMI from one of them would be invented. */}
                    <td>{g.bmiValue ?? '—'}</td>
                  </tr>
                ))}
                {growth.length === 0 && <tr><td colSpan={5}>Nothing recorded yet.</td></tr>}
              </tbody>
            </table>
          </section>
        </>
      )}

      {tab === 'care' && (
        <>
          <section className="card">
            <h2>The bill</h2>
            <div className="inline-form">
              <button type="button" onClick={() => requirePatient(() => setAddingProcedure(true))}>
                + Add procedure
              </button>
              <button
                type="button"
                onClick={() => requirePatient(() => { setPreselectVaccineId(null); setRecordingVaccine(true); })}
              >
                + Record vaccination
              </button>
            </div>

            {procedures.length === 0 && (
              <p className="hint">
                No Pediatrics procedures are set up yet — they are created on Procedure Master,
                which arrives with the Masters module.
              </p>
            )}

            <table>
              <thead>
                <tr><th>Item</th><th>Type</th><th>Price</th><th>Qty</th><th>Amount</th><th></th></tr>
              </thead>
              <tbody>
                {lines.map((l) => (
                  <tr key={l.key}>
                    <td>{l.procedureName}</td>
                    {/* A combined procedure-and-vaccine bill still has to
                        read clearly at a glance. */}
                    <td><span className="badge">{l.kind}</span></td>
                    <td>
                      <input
                        type="number"
                        min={0}
                        step="0.01"
                        value={l.price}
                        onChange={(e) => setLine(l.key, { price: Number(e.target.value) })}
                        style={{ width: '6rem' }}
                      />
                    </td>
                    <td>
                      <input
                        type="number"
                        min={1}
                        value={l.quantity}
                        onChange={(e) => setLine(l.key, { quantity: Number(e.target.value) })}
                        style={{ width: '4rem' }}
                      />
                    </td>
                    <td>{(l.price * l.quantity).toFixed(2)}</td>
                    <td className="row-actions">
                      <button type="button" className="ghost" onClick={() => removeLine(l.key)}>Remove</button>
                    </td>
                  </tr>
                ))}
                {lines.length === 0 && <tr><td colSpan={6}>No procedures on this bill.</td></tr>}
              </tbody>
            </table>

            <div className="totals">
              <div><span>Total</span><span>{total.toFixed(2)}</span></div>
              <div>
                <span>Discount</span>
                <input
                  type="number"
                  min={0}
                  step="0.01"
                  value={discount}
                  onChange={(e) => setDiscount(e.target.value)}
                  style={{ width: '7rem' }}
                />
              </div>
              <div><strong>Payable</strong><strong>₹{finalAmount.toFixed(2)}</strong></div>
            </div>
          </section>

          <section className="card">
            <h2>Payment</h2>
            <div className="settings-form">
              <div className="settings-row">
                <label>Mode</label>
                <select value={paymentMode} onChange={(e) => setPaymentMode(e.target.value as PaymentMode)}>
                  {PAYMENT_MODES.map((m) => <option key={m} value={m}>{m}</option>)}
                </select>
              </div>
              {(paymentMode === 'Upi' || paymentMode === 'Card') && (
                <div className="settings-row">
                  <label>Reference no.</label>
                  <input value={transactionNo} onChange={(e) => setTransactionNo(e.target.value)} />
                </div>
              )}
              <div className="settings-row">
                <label>Referred by</label>
                <input value={referredBy} onChange={(e) => setReferredBy(e.target.value)} />
              </div>
            </div>

            <div className="settings-actions">
              <button type="button" disabled={busy} onClick={() => void saveBill(false)}>Save bill</button>
              <button type="button" disabled={busy} onClick={() => void saveBill(true)}>Save &amp; print</button>
              <button type="button" className="ghost" onClick={newBill}>New bill</button>
            </div>
          </section>

          <section className="card">
            <h2>Vaccinations given</h2>
            <table>
              <thead><tr><th>Given on</th><th>Vaccine</th><th>Dose</th><th>Brand</th><th>Batch</th><th>Site</th></tr></thead>
              <tbody>
                {[...history].sort((a, b) => b.givenOn.localeCompare(a.givenOn)).map((r) => (
                  <tr key={r.id}>
                    <td>{new Date(r.givenOn).toLocaleDateString()}</td>
                    <td>{r.vaccineName}</td>
                    <td>{r.doseNumber}</td>
                    <td>{r.productName ?? '—'}{r.manufacturer && <div className="hint">{r.manufacturer}</div>}</td>
                    <td>{r.batchNo ?? '—'}</td>
                    <td>{r.siteOfInjection ?? '—'}</td>
                  </tr>
                ))}
                {history.length === 0 && <tr><td colSpan={6}>No doses recorded yet.</td></tr>}
              </tbody>
            </table>
          </section>
        </>
      )}

      {tab === 'immunization' && (
        <section className="card">
          <h2>
            Immunization card
            <span className="hint">
              {' · '}{counts.given} given · {counts.overdue} overdue · {counts.dueSoon} due soon ·{' '}
              {counts.upcoming} upcoming · {card.length} total
            </span>
          </h2>

          {patient && !patient.dateOfBirth && (
            <p className="hint">
              No date of birth on file for {patient.name}, so nothing can be compared against the
              recommended ages — every dose not yet given reads Upcoming.
            </p>
          )}

          <table>
            <thead>
              <tr>
                <th>Age</th><th>Vaccine</th><th>Dose</th><th>Recommended</th>
                <th>Status</th><th>Given on</th><th></th>
              </tr>
            </thead>
            <tbody>
              {card.map((r) => (
                <tr key={`${r.vaccineId}`}>
                  {/* The schedule age, which is what makes the card read as a
                      schedule rather than a list — and it is the only column
                      that means anything for a child with no date of birth on
                      file, since every dated column is blank for them. */}
                  <td>{scheduleAge(r.recommendedAgeDays)}</td>
                  <td>{r.vaccineName}</td>
                  <td>{r.doseNumber}</td>
                  <td>{r.recommendedOn ? new Date(r.recommendedOn).toLocaleDateString() : '—'}</td>
                  <td><span className="badge">{STATUS_LABELS[r.status]}</span></td>
                  <td>{r.given ? new Date(r.given.givenOn).toLocaleDateString() : '—'}</td>
                  <td className="row-actions">
                    {r.status !== 'Given' && (
                      // Opens the same popup billing runs through — recording
                      // from the card never bypasses the bill.
                      <button
                        type="button"
                        onClick={() => requirePatient(() => {
                          setPreselectVaccineId(r.vaccineId);
                          setRecordingVaccine(true);
                          setTab('care');
                        })}
                      >
                        Record
                      </button>
                    )}
                  </td>
                </tr>
              ))}
              {card.length === 0 && <tr><td colSpan={7}>Choose a patient to see their card.</td></tr>}
            </tbody>
          </table>
        </section>
      )}

      {addingProcedure && (
        <div className="overlay" role="dialog" aria-modal="true" aria-label="Add procedure">
          <div className="overlay-card">
            <div className="overlay-head">
              <h2>Add procedure</h2>
              <button type="button" onClick={() => setAddingProcedure(false)}>Close</button>
            </div>
            <div className="overlay-body settings-form">
              <div className="settings-row">
                <label>Procedure</label>
                <select
                  value={procedureId}
                  className={procedureMissing ? 'field-missing' : undefined}
                  onChange={(e) => { setProcedureId(e.target.value); if (e.target.value) setProcedureMissing(false); }}
                >
                  <option value="">Procedure…</option>
                  {procedures.map((p) => (
                    <option key={p.id} value={p.id}>{p.name} — ₹{p.price.toFixed(2)}</option>
                  ))}
                </select>
              </div>
              <div className="settings-row">
                <label>Quantity</label>
                <input type="number" min={1} value={procedureQty} onChange={(e) => setProcedureQty(e.target.value)} />
              </div>
              {procedureMissing && <p className="auth-error">Pick a procedure.</p>}
              <div className="overlay-actions">
                <button type="button" onClick={addProcedure}>Add</button>
                <button type="button" className="ghost" onClick={() => setAddingProcedure(false)}>Cancel</button>
              </div>
            </div>
          </div>
        </div>
      )}

      {recordingVaccine && (
        <RecordVaccinationDialog
          vaccines={vaccines}
          preselectVaccineId={preselectVaccineId}
          onClose={() => { setRecordingVaccine(false); setPreselectVaccineId(null); }}
          onAdd={addVaccination}
        />
      )}

      {recordingGrowth && (
        <RecordGrowthDialog
          onClose={() => setRecordingGrowth(false)}
          onSave={(draft) => void recordGrowth(draft)}
        />
      )}

      {addingPatient && (
        <PatientEditorDialog
          existing={null}
          onClose={() => setAddingPatient(false)}
          onSaved={(_message, saved) => {
            if (saved) selectPatient(saved);
            setAddingPatient(false);
          }}
        />
      )}
    </div>
  );
}
