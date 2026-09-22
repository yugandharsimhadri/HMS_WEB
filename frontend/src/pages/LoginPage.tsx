import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { PLATFORM_ADMIN_ROLE, useAuth } from '../auth/AuthContext';
import { ApiError } from '../api/client';

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();

  // Set by the API client when a request comes back 401 and the session is
  // cleared. Without it the user is dropped here mid-shift with no
  // explanation, which reads as the application having logged them out at
  // random rather than a token reaching its eight-hour limit.
  const expired = params.get('expired') === '1';

  // Set by the reset screen on its way here, so the person knows the new
  // password took effect rather than wondering whether it saved.
  const justReset = params.get('reset') === '1';
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      // The clinic rides along inside the username ("reception@twinkle"),
      // so there is nothing else to ask for here.
      const session = await login(username.trim(), password);

      // Support staff sign in through the same box but land somewhere else
      // entirely — they belong to no clinic, so there is no clinic shell to
      // show them. The API enforces that independently; this only picks the
      // screen.
      navigate(session.role === PLATFORM_ADMIN_ROLE ? '/platform' : '/');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not sign in. Try again.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={onSubmit}>
        <h1>Sivayaan HMS</h1>
        <p className="auth-subtitle">Sign in to your clinic</p>
        {expired && (
          <p className="status-line">Your session ended. Please sign in again.</p>
        )}
        {justReset && (
          <p className="status-line">Password changed. Sign in with your new one.</p>
        )}

        <label>
          Username
          <input
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="you@your-clinic"
            autoComplete="username"
            required
            autoFocus
          />
        </label>
        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            required
          />
        </label>

        {error && <p className="auth-error">{error}</p>}

        <button type="submit" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>

        <p className="auth-footer">
          <Link to="/forgot-password">Forgot password?</Link>
        </p>

        <p className="auth-footer">
          New clinic? <Link to="/register">Register here</Link>
        </p>
      </form>
    </div>
  );
}
