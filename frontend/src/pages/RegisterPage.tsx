import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { RegisterTenantResponse } from '../api/types';

export function RegisterPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [clinicName, setClinicName] = useState('');
  const [slug, setSlug] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const result = await api.post<RegisterTenantResponse>('/api/tenants/register', {
        clinicName,
        slug,
        adminPassword: password,
      });
      // Straight into the new clinic - no separate "now go log in" step.
      await login(result.slug, result.adminUsername, password);
      navigate('/');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not register the clinic. Try again.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={onSubmit}>
        <h1>Register your clinic</h1>
        <p className="auth-subtitle">OPD and Pharmacy are ready the moment you sign up</p>

        <label>
          Clinic name
          <input value={clinicName} onChange={(e) => setClinicName(e.target.value)} required autoFocus />
        </label>
        <label>
          Clinic URL
          <input
            value={slug}
            onChange={(e) => setSlug(e.target.value)}
            placeholder="your-clinic"
            required
            pattern="[a-z0-9\-]{3,}"
            title="Lowercase letters, numbers and hyphens, at least 3 characters"
          />
        </label>
        <label>
          Admin password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            minLength={8}
          />
        </label>

        {error && <p className="auth-error">{error}</p>}

        <button type="submit" disabled={busy}>
          {busy ? 'Setting up…' : 'Register clinic'}
        </button>

        <p className="auth-footer">
          Already registered? <Link to="/login">Sign in</Link>
        </p>
      </form>
    </div>
  );
}
