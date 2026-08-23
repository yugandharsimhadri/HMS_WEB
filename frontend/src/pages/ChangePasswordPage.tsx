import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';

/**
 * Choosing your own password.
 *
 * Reached two ways, and the wording changes between them. Normally it is a
 * deliberate visit. But the important path is the forced one: somebody has
 * just signed in with a temporary password that a support agent read to them
 * over the phone, and until this screen is finished that agent still knows
 * their password. That is why it cannot be dismissed — see ProtectedRoute.
 */
export function ChangePasswordPage() {
  const { session, logout, passwordChanged } = useAuth();
  const navigate = useNavigate();
  const forced = session?.mustChangePassword ?? false;

  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const mismatch = confirm.length > 0 && next !== confirm;

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();

    if (next !== confirm) {
      setError('The two new passwords do not match.');
      return;
    }

    setError(null);
    setBusy(true);
    try {
      await api.post('/api/auth/change-password', {
        currentPassword: current,
        newPassword: next,
      });
      passwordChanged();
      navigate('/');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change your password. Try again.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={onSubmit}>
        <h1>{forced ? 'Choose your password' : 'Change your password'}</h1>
        <p className="auth-subtitle">
          {forced
            ? 'You signed in with a temporary password. Choose your own before going on — whoever gave it to you knows it.'
            : `Signed in as ${session?.username}`}
        </p>

        <label>
          {forced ? 'Temporary password' : 'Current password'}
          <input
            type="password"
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
            autoComplete="current-password"
            required
            autoFocus
          />
        </label>

        <label>
          New password
          <input
            type="password"
            value={next}
            onChange={(e) => setNext(e.target.value)}
            autoComplete="new-password"
            required
            minLength={8}
          />
        </label>

        <label>
          Retype new password
          <input
            type="password"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            autoComplete="new-password"
            required
            minLength={8}
          />
        </label>

        {mismatch && <p className="hint warn">The two new passwords do not match yet.</p>}
        {error && <p className="auth-error">{error}</p>}

        <button type="submit" disabled={busy}>
          {busy ? 'Saving…' : 'Save password'}
        </button>

        {/* No "skip" when forced — that is the whole point. Signing out is
            offered instead, so nobody is trapped on a screen they cannot
            complete (a wrong temporary password, say). */}
        <p className="auth-footer">
          {forced ? (
            <button type="button" className="ghost" onClick={logout}>Sign out instead</button>
          ) : (
            <button type="button" className="ghost" onClick={() => navigate('/')}>Cancel</button>
          )}
        </p>
      </form>
    </div>
  );
}
