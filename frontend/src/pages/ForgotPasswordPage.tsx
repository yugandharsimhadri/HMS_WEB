import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { ForgotPasswordResponse } from '../api/types';

/**
 * "I forgot my password", in two steps on one screen.
 *
 * Step one asks for the username and sends a code to the phone already on
 * that account. Step two takes the code and the new password.
 *
 * The username is asked for rather than the phone number on purpose: the
 * phone is what we send *to*, and letting somebody type a number and have a
 * code sent to it would let anyone aim codes at any phone. The account
 * already knows its own number.
 *
 * Nothing on this page reveals whether an account exists — step one says the
 * same thing either way, because it is reachable by anyone.
 */
export function ForgotPasswordPage() {
  const navigate = useNavigate();

  const [step, setStep] = useState<'ask' | 'verify'>('ask');
  const [username, setUsername] = useState('');
  const [notice, setNotice] = useState<string | null>(null);

  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');

  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const mismatch = confirm.length > 0 && password !== confirm;

  const requestCode = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const result = await api.post<ForgotPasswordResponse>('/api/auth/forgot-password', {
        username: username.trim(),
      });
      setNotice(result.message);
      setStep('verify');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not send a code. Try again.');
    } finally {
      setBusy(false);
    }
  };

  const resetPassword = async (e: FormEvent) => {
    e.preventDefault();

    if (password !== confirm) {
      setError('The two passwords do not match.');
      return;
    }

    setError(null);
    setBusy(true);
    try {
      await api.post('/api/auth/reset-password', {
        username: username.trim(),
        code: code.trim(),
        newPassword: password,
      });
      // Straight to sign-in rather than signing them in here: proving you
      // hold a code is not the same as proving you know the password, and
      // typing the new one once more is the cheapest confirmation there is.
      navigate('/login?reset=1');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not reset the password. Try again.');
    } finally {
      setBusy(false);
    }
  };

  if (step === 'ask') {
    return (
      <div className="auth-page">
        <form className="auth-card" onSubmit={requestCode}>
          <h1>Reset your password</h1>
          <p className="auth-subtitle">
            We will send a one-time code to the mobile number on your account
          </p>

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
            <span className="hint">The same username you sign in with, including the clinic code.</span>
          </label>

          {error && <p className="auth-error">{error}</p>}

          <button type="submit" className="primary" disabled={busy}>
            {busy ? 'Sending…' : 'Send code'}
          </button>

          <p className="auth-footer">
            Remembered it? <Link to="/login">Sign in</Link>
          </p>
        </form>
      </div>
    );
  }

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={resetPassword}>
        <h1>Enter your code</h1>
        {notice && <p className="status-line">{notice}</p>}
        <p className="auth-subtitle">Check the mobile number on your account</p>

        <label>
          Six-digit code
          <input
            value={code}
            onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
            inputMode="numeric"
            autoComplete="one-time-code"
            placeholder="123456"
            required
            autoFocus
          />
        </label>

        <label>
          New password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
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

        {mismatch && <p className="hint warn">The two passwords do not match yet.</p>}
        {error && <p className="auth-error">{error}</p>}

        <button type="submit" className="primary" disabled={busy || code.length < 6}>
          {busy ? 'Saving…' : 'Set new password'}
        </button>

        {/* Going back re-sends, which the server rate-limits. Offered anyway:
            a code that never arrived is the commonest thing to go wrong, and
            the alternative is re-typing the username from a dead end. */}
        <p className="auth-footer">
          Did not get it? <button type="button" className="ghost" onClick={() => setStep('ask')}>Ask again</button>
        </p>
      </form>
    </div>
  );
}
