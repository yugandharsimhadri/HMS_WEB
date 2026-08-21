import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Doctor, Patient, Visit, VisitStatus } from '../api/types';

const NEXT_STATUS: Partial<Record<VisitStatus, VisitStatus>> = {
  Booked: 'Waiting',
  Waiting: 'InConsultation',
  InConsultation: 'Completed',
};

export function OpdQueuePage() {
  const [visits, setVisits] = useState<Visit[]>([]);
  const [doctors, setDoctors] = useState<Doctor[]>([]);
  const [error, setError] = useState<string | null>(null);

  const [patientTerm, setPatientTerm] = useState('');
  const [patientResults, setPatientResults] = useState<Patient[]>([]);
  const [selectedPatient, setSelectedPatient] = useState<Patient | null>(null);
  const [doctorId, setDoctorId] = useState('');
  const [complaint, setComplaint] = useState('');
  const [fee, setFee] = useState('');
  const [booking, setBooking] = useState(false);

  const loadQueue = async () => {
    try {
      setVisits(await api.get<Visit[]>('/api/visits'));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load today’s queue.');
    }
  };

  useEffect(() => {
    void loadQueue();
    void api.get<Doctor[]>('/api/doctors').then(setDoctors).catch(() => {});
  }, []);

  const onSearchPatient = async (e: FormEvent) => {
    e.preventDefault();
    if (!patientTerm.trim()) return;
    setPatientResults(await api.get<Patient[]>(`/api/patients?term=${encodeURIComponent(patientTerm)}`));
  };

  const pickPatient = (p: Patient) => {
    setSelectedPatient(p);
    setPatientResults([]);
    setPatientTerm(p.name);
  };

  const onDoctorChange = (id: string) => {
    setDoctorId(id);
    const doctor = doctors.find((d) => d.id === id);
    if (doctor) setFee(String(doctor.consultationFee));
  };

  const onBook = async (e: FormEvent) => {
    e.preventDefault();
    if (!selectedPatient || !doctorId) return;
    setBooking(true);
    setError(null);
    try {
      await api.post('/api/visits', {
        patientId: selectedPatient.id,
        doctorId,
        scheduledOn: new Date().toISOString(),
        complaint: complaint || null,
        fee: Number(fee) || 0,
      });
      setSelectedPatient(null);
      setPatientTerm('');
      setComplaint('');
      setFee('');
      await loadQueue();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not book the visit.');
    } finally {
      setBooking(false);
    }
  };

  const advance = async (visit: Visit) => {
    const next = NEXT_STATUS[visit.status];
    if (!next) return;
    try {
      await api.post(`/api/visits/${visit.id}/status`, { status: next });
      await loadQueue();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not update the visit.');
    }
  };

  const cancel = async (visit: Visit) => {
    try {
      await api.post(`/api/visits/${visit.id}/status`, { status: 'Cancelled' });
      await loadQueue();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not cancel the visit.');
    }
  };

  const collectFee = async (visit: Visit) => {
    try {
      await api.post(`/api/visits/${visit.id}/collect-fee`, { mode: 'Cash', amount: null, transactionNo: null });
      await loadQueue();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not collect the fee.');
    }
  };

  return (
    <div className="page">
      <h1>OPD Queue</h1>

      <section className="card">
        <h2>Book a visit</h2>
        <form className="inline-form" onSubmit={onBook}>
          <div className="patient-picker">
            <input
              placeholder="Search patient by name or phone"
              value={patientTerm}
              onChange={(e) => {
                setPatientTerm(e.target.value);
                setSelectedPatient(null);
              }}
            />
            <button type="button" onClick={onSearchPatient}>
              Find
            </button>
            {patientResults.length > 0 && (
              <ul className="picker-results">
                {patientResults.map((p) => (
                  <li key={p.id}>
                    <button type="button" onClick={() => pickPatient(p)}>
                      {p.name} · {p.phone || 'no phone'}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <select value={doctorId} onChange={(e) => onDoctorChange(e.target.value)} required>
            <option value="">Doctor…</option>
            {doctors.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </select>

          <input placeholder="Complaint" value={complaint} onChange={(e) => setComplaint(e.target.value)} />
          <input
            placeholder="Fee"
            type="number"
            min="0"
            step="0.01"
            value={fee}
            onChange={(e) => setFee(e.target.value)}
          />

          <button type="submit" disabled={booking || !selectedPatient || !doctorId}>
            {booking ? 'Booking…' : 'Book visit'}
          </button>
        </form>
        {selectedPatient && <p className="hint">Booking for {selectedPatient.name}.</p>}
      </section>

      <section className="card">
        <h2>Today</h2>
        {error && <p className="auth-error">{error}</p>}

        <table>
          <thead>
            <tr>
              <th>Token</th>
              <th>Patient</th>
              <th>Doctor</th>
              <th>Status</th>
              <th>Fee</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {visits.map((v) => (
              <tr key={v.id}>
                <td>{v.tokenNo}</td>
                <td>{v.patient.name}</td>
                <td>{v.doctor.name}</td>
                <td>{v.status}</td>
                <td>
                  {v.fee.toFixed(2)} {v.feePaid ? '(paid)' : ''}
                </td>
                <td className="row-actions">
                  {NEXT_STATUS[v.status] && (
                    <button type="button" onClick={() => advance(v)}>
                      {NEXT_STATUS[v.status]}
                    </button>
                  )}
                  {!v.feePaid && v.status !== 'Cancelled' && (
                    <button type="button" onClick={() => collectFee(v)}>
                      Collect fee
                    </button>
                  )}
                  {v.status === 'Booked' && (
                    <button type="button" className="danger" onClick={() => cancel(v)}>
                      Cancel
                    </button>
                  )}
                </td>
              </tr>
            ))}
            {visits.length === 0 && (
              <tr>
                <td colSpan={6}>No visits booked today.</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </div>
  );
}
