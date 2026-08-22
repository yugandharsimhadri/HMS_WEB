import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, openPdf } from '../api/client';
import type { Patient, Sale, Visit } from '../api/types';
import { PatientEditorDialog } from '../opd/PatientEditorDialog';

/**
 * Patient register: search, the visit and bill history behind each patient,
 * and reprinting any of it however long ago it was.
 *
 * Booking still happens on the OPD screen — this is the record, not the queue.
 */
export function PatientsPage() {
  const [search, setSearch] = useState('');
  const [patients, setPatients] = useState<Patient[]>([]);
  const [selected, setSelected] = useState<Patient | null>(null);

  const [history, setHistory] = useState<Visit[]>([]);
  const [bills, setBills] = useState<Sale[]>([]);

  const [editing, setEditing] = useState<Patient | null | undefined>(undefined);
  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);

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

  useEffect(() => {
    if (!selected) { setHistory([]); setBills([]); return; }
    void api.get<Visit[]>(`/api/visits/by-patient/${selected.id}`).then(setHistory).catch(() => {});
    void api.get<Sale[]>(`/api/pharmacy/sales/by-patient/${selected.id}`).then(setBills).catch(() => setBills([]));
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
              placeholder="Name, phone or patient no."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <button type="submit" className="ghost">Search</button>
          </form>
          <button type="button" disabled={!selected} onClick={() => selected && setEditing(selected)}>
            Edit
          </button>
          <button type="button" onClick={() => setEditing(null)}>+ New patient</button>
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
            </>
          )}
        </section>
      </div>

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
