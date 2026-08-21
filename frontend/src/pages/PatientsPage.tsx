import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Patient, Gender } from '../api/types';

const emptyForm = { name: '', phone: '', gender: 'Male' as Gender };

export function PatientsPage() {
  const [term, setTerm] = useState('');
  const [patients, setPatients] = useState<Patient[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [form, setForm] = useState(emptyForm);
  const [saving, setSaving] = useState(false);

  const search = async (q: string) => {
    setLoading(true);
    setError(null);
    try {
      const params = q ? `?term=${encodeURIComponent(q)}` : '';
      setPatients(await api.get<Patient[]>(`/api/patients${params}`));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load patients.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void search('');
  }, []);

  const onSearchSubmit = (e: FormEvent) => {
    e.preventDefault();
    void search(term);
  };

  const onSave = async (e: FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      await api.post<Patient>('/api/patients', form);
      setForm(emptyForm);
      await search(term);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the patient.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="page">
      <h1>Patients</h1>

      <section className="card">
        <h2>Register a patient</h2>
        <form className="inline-form" onSubmit={onSave}>
          <input
            placeholder="Name"
            value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
            required
          />
          <input
            placeholder="Phone"
            value={form.phone}
            onChange={(e) => setForm({ ...form, phone: e.target.value })}
          />
          <select value={form.gender} onChange={(e) => setForm({ ...form, gender: e.target.value as Gender })}>
            <option value="Male">Male</option>
            <option value="Female">Female</option>
            <option value="Other">Other</option>
          </select>
          <button type="submit" disabled={saving}>
            {saving ? 'Saving…' : 'Add patient'}
          </button>
        </form>
      </section>

      <section className="card">
        <h2>Search</h2>
        <form className="inline-form" onSubmit={onSearchSubmit}>
          <input placeholder="Name, phone or patient no." value={term} onChange={(e) => setTerm(e.target.value)} />
          <button type="submit">Search</button>
        </form>

        {error && <p className="auth-error">{error}</p>}

        <table>
          <thead>
            <tr>
              <th>Patient No.</th>
              <th>Name</th>
              <th>Phone</th>
              <th>Gender</th>
            </tr>
          </thead>
          <tbody>
            {patients.map((p) => (
              <tr key={p.id}>
                <td>{p.patientNo}</td>
                <td>{p.name}</td>
                <td>{p.phone}</td>
                <td>{p.gender}</td>
              </tr>
            ))}
            {!loading && patients.length === 0 && (
              <tr>
                <td colSpan={4}>No patients yet.</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </div>
  );
}
