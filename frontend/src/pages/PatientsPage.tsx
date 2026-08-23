import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, openPdf } from '../api/client';
import type {
  DiagnosticBill, GeneralSettings, GrowthMeasurement, Patient, Sale,
  VaccinationRecord, Visit,
} from '../api/types';
import { PatientEditorDialog } from '../opd/PatientEditorDialog';
import { useHotkey } from '../shell/hotkeys';
import { ShortcutHints } from '../shell/ShortcutHints';

/**
 * Patient register: search, everything on record behind each patient, and
 * reprinting any of it however long ago it was.
 *
 * "Everything" is the point of this screen — the desktop shows six histories
 * here, not two. A patient's diagnostic bills, doses given and growth
 * measurements belong to the person, not to the module that happened to
 * record them, and this is the one screen that reads the whole person.
 *
 * Booking still happens on the OPD screen — this is the record, not the queue.
 */
export function PatientsPage() {
  const [search, setSearch] = useState('');
  const [patients, setPatients] = useState<Patient[]>([]);
  const [selected, setSelected] = useState<Patient | null>(null);

  const [history, setHistory] = useState<Visit[]>([]);
  const [bills, setBills] = useState<Sale[]>([]);
  const [diagnosticBills, setDiagnosticBills] = useState<DiagnosticBill[]>([]);
  const [vaccinations, setVaccinations] = useState<VaccinationRecord[]>([]);
  const [growth, setGrowth] = useState<GrowthMeasurement[]>([]);
  const [general, setGeneral] = useState<GeneralSettings | null>(null);

  const [editing, setEditing] = useState<Patient | null | undefined>(undefined);
  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);

  const searchRef = useRef<HTMLInputElement>(null);

  const G = 'Patients';
  useHotkey('f2', 'Add a patient', G, () => setEditing(null));
  useHotkey('f3', 'Find a patient', G, () => {
    searchRef.current?.focus();
    searchRef.current?.select();
  }, { whileTyping: true });

  const find = useCallback(async (term: string, keepId?: string) => {
    try {
      const found = await api.get<Patient[]>(`/api/patients?term=${encodeURIComponent(term)}&take=200`);
      setPatients(found);
      if (keepId) setSelected(found.find((p) => p.id === keepId) ?? null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the register.');
    }
  }, []);

  useEffect(() => { void find(''); }, [find]);

  // Which histories are worth showing at all. A clinic that has never turned
  // Pediatrics on should not see two permanently empty tables.
  useEffect(() => {
    void api.get<GeneralSettings>('/api/settings/general').then(setGeneral).catch(() => {});
  }, []);

  useEffect(() => {
    if (!selected) {
      setHistory([]); setBills([]); setDiagnosticBills([]); setVaccinations([]); setGrowth([]);
      return;
    }
    const id = selected.id;
    void api.get<Visit[]>(`/api/visits/by-patient/${id}`).then(setHistory).catch(() => {});
    void api.get<Sale[]>(`/api/pharmacy/sales/by-patient/${id}`).then(setBills).catch(() => setBills([]));
    void api.get<DiagnosticBill[]>(`/api/diagnostics/bills/by-patient/${id}`)
      .then(setDiagnosticBills).catch(() => setDiagnosticBills([]));
    void api.get<VaccinationRecord[]>(`/api/pediatrics/patients/${id}/vaccinations`)
      .then(setVaccinations).catch(() => setVaccinations([]));
    void api.get<GrowthMeasurement[]>(`/api/pediatrics/patients/${id}/growth`)
      .then(setGrowth).catch(() => setGrowth([]));
  }, [selected]);

  const onSearch = (e: FormEvent) => {
    e.preventDefault();
    void find(search, selected?.id);
  };

  const print = async (path: string, unavailable: string) => {
    setError(null);
    try {
      await openPdf(path);
    } catch (err) {
      setStatus(err instanceof ApiError && err.status === 400 ? unavailable : String(err));
    }
  };

  return (
    <div className="page wide">
      <div className="page-head">
        <div>
          <h1>Patients</h1>
          <p className="hint">{patients.length} patient(s) listed</p>
        </div>
        <div className="inline-form">
          <form className="inline-form" onSubmit={onSearch}>
            <input
              ref={searchRef}
              placeholder="Name, phone or patient no."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <button type="submit" className="ghost">Search</button>
          </form>
          <button type="button" disabled={!selected} onClick={() => selected && setEditing(selected)}>
            Edit
          </button>
          <button type="button" className="primary" onClick={() => setEditing(null)}>+ New patient</button>
        </div>
      </div>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      <div className="queue-columns">
        <section className="card">
          <h2>Register</h2>
          <table>
            <thead><tr><th>Patient no.</th><th>Name</th><th>Age/Sex</th><th>Phone</th></tr></thead>
            <tbody>
              {patients.map((p) => (
                <tr
                  key={p.id}
                  onClick={() => setSelected(p)}
                  className={selected?.id === p.id ? 'selected-row' : undefined}
                >
                  <td>{p.patientNo}</td>
                  <td>
                    {p.name}
                    {p.allergies && <div className="hint">Allergies: {p.allergies}</div>}
                  </td>
                  <td>{p.age}{p.gender.charAt(0)}</td>
                  <td>{p.phone}</td>
                </tr>
              ))}
              {patients.length === 0 && <tr><td colSpan={4}>Nobody matches.</td></tr>}
            </tbody>
          </table>
        </section>

        <section className="card">
          <h2>History {selected && <span className="hint">· {selected.name}</span>}</h2>

          {!selected ? (
            <p className="hint">Pick a patient to see their visits and bills.</p>
          ) : (
            <>
              <h3 className="sub-heading">Visits</h3>
              <table>
                <thead><tr><th>Visit</th><th>When</th><th>Doctor</th><th>Fee</th><th>Reprint</th></tr></thead>
                <tbody>
                  {history.map((v) => (
                    <tr key={v.id}>
                      <td>{v.visitNo}</td>
                      <td>{new Date(v.scheduledOn).toLocaleDateString()}</td>
                      <td>{v.doctor?.name ?? ''}</td>
                      <td>{v.fee.toFixed(2)}{v.feePaid ? ' (paid)' : ''}</td>
                      <td className="row-actions">
                        <button
                          type="button"
                          className="ghost"
                          onClick={() => print(`/api/print/prescription/${v.id}`, `Visit ${v.visitNo} has no prescription recorded.`)}
                        >
                          Rx
                        </button>
                        {v.feePaid && (
                          <button
                            type="button"
                            className="ghost"
                            onClick={() => print(`/api/print/receipt/${v.id}?reprint=true`, 'No receipt recorded.')}
                          >
                            Receipt
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                  {history.length === 0 && <tr><td colSpan={5}>No visits recorded.</td></tr>}
                </tbody>
              </table>

              <h3 className="sub-heading">Medicine bills</h3>
              <table>
                <thead><tr><th>Bill</th><th>When</th><th>Net</th><th>Reprint</th></tr></thead>
                <tbody>
                  {bills.map((b) => (
                    <tr key={b.id}>
                      <td>{b.billNo}</td>
                      <td>{new Date(b.billDate).toLocaleDateString()}</td>
                      <td>{b.netAmount.toFixed(2)}</td>
                      <td>
                        <button
                          type="button"
                          className="ghost"
                          onClick={() => print(`/api/print/bill/${b.id}?reprint=true`, 'Bill not found.')}
                        >
                          Bill
                        </button>
                      </td>
                    </tr>
                  ))}
                  {bills.length === 0 && <tr><td colSpan={4}>No medicine bills.</td></tr>}
                </tbody>
              </table>

              {/* The three histories that belong to the person rather than to
                  the module that recorded them. Each is hidden when its own
                  module has never been switched on, so a clinic that does not
                  run a lab or see children is not shown two empty tables. */}
              {general?.diagnosticsEnabled && (
                <>
                  <h3 className="sub-heading">Diagnostic bills</h3>
                  <table>
                    <thead><tr><th>Bill</th><th>When</th><th>Tests</th><th>Amount</th><th>Status</th><th>Reprint</th></tr></thead>
                    <tbody>
                      {diagnosticBills.map((b) => (
                        <tr key={b.id}>
                          <td>{b.billNo}</td>
                          <td>{new Date(b.billDate).toLocaleDateString()}</td>
                          <td>{b.items?.length ?? 0}</td>
                          <td>{b.finalAmount.toFixed(2)}</td>
                          <td><span className="badge">{b.status}</span></td>
                          <td>
                            <button
                              type="button"
                              className="ghost"
                              onClick={() => print(`/api/print/diagnostic-bill/${b.id}?reprint=true`, 'Bill not found.')}
                            >
                              Bill
                            </button>
                          </td>
                        </tr>
                      ))}
                      {diagnosticBills.length === 0 && <tr><td colSpan={6}>No diagnostic bills.</td></tr>}
                    </tbody>
                  </table>
                </>
              )}

              {general?.pediatricsEnabled && (
                <>
                  <h3 className="sub-heading">Vaccinations</h3>
                  <table>
                    <thead><tr><th>Given on</th><th>Vaccine</th><th>Dose</th><th>Batch</th><th>Site</th></tr></thead>
                    <tbody>
                      {[...vaccinations].sort((a, b) => b.givenOn.localeCompare(a.givenOn)).map((r) => (
                        <tr key={r.id}>
                          <td>{new Date(r.givenOn).toLocaleDateString()}</td>
                          <td>{r.vaccineName}</td>
                          <td>{r.doseNumber}</td>
                          <td>{r.batchNo ?? '—'}</td>
                          <td>{r.siteOfInjection ?? '—'}</td>
                        </tr>
                      ))}
                      {vaccinations.length === 0 && <tr><td colSpan={5}>No doses recorded.</td></tr>}
                    </tbody>
                  </table>

                  <h3 className="sub-heading">Growth</h3>
                  <table>
                    <thead>
                      <tr>
                        <th>Measured on</th><th>Age (days)</th><th>Weight (kg)</th>
                        <th>Height (cm)</th><th>Head circ. (cm)</th><th>BMI</th>
                      </tr>
                    </thead>
                    <tbody>
                      {[...growth].sort((a, b) => b.measuredOn.localeCompare(a.measuredOn)).map((g) => (
                        <tr key={g.id}>
                          <td>{new Date(g.measuredOn).toLocaleDateString()}</td>
                          <td>{g.ageDays}</td>
                          <td>{g.weightKg ?? '—'}</td>
                          <td>{g.heightCm ?? '—'}</td>
                          <td>{g.headCircumferenceCm ?? '—'}</td>
                          <td>{g.bmiValue ?? '—'}</td>
                        </tr>
                      ))}
                      {growth.length === 0 && <tr><td colSpan={6}>Nothing recorded.</td></tr>}
                    </tbody>
                  </table>
                </>
              )}
            </>
          )}
        </section>
      </div>

      <ShortcutHints keys={[['f2', 'new patient'], ['f3', 'find']]} />

      {editing !== undefined && (
        <PatientEditorDialog
          existing={editing}
          onClose={() => setEditing(undefined)}
          onSaved={async (message) => {
            setEditing(undefined);
            // Everything clears, the search included. Leaving the register
            // filtered to the patient just saved reads as "ready for the
            // next one" and is not.
            setSelected(null);
            setSearch('');
            await find('');
            setStatus(`${message} The register is clear for the next patient.`);
          }}
        />
      )}
    </div>
  );
}
