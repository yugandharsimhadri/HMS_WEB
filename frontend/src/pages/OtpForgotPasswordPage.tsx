import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { ForgotPasswordResponse, ResetStepResponse } from '../api/types';

/** Seconds before "send it again" is offered. The server enforces its own
 *  cooldown but deliberately never reports it (that would reveal the
 *  account), so the page keeps time itself. */
const RESEND_COOLDOWN = 60;

/**
 * "I forgot my password", in three steps on one screen — the SivaComm flow,
 * shown only when the API reports SivaComm:Enabled. With it off (the default)
 * ForgotPasswordPage shows ClassicForgotPasswordPage instead, unchanged.
 *
 *   1. ask    — the username; a code goes to the phone already on that account.
 *   2. verify — the code, and only the code. The backend checks it.
 *   3. choose — the new password, shown only after the backend said VERIFIED.
 *
 * The username is asked for rather than the phone number on purpose: the
 * phone is what we send *to*, and letting somebody type a number and have a
 * code sent to it would let anyone aim codes at any phone. The account
 * already knows its own number.
 *
 * Moving to step 3 is not this page's decision. It happens only when the API
 * answers VERIFIED, and the API refuses the new password unless its own
 * record of this reset says the code was verified — so skipping step 2 in the
 * browser gets nowhere.
 *
 * Nothing on this page reveals whether an account exists: step one says the
 * same thing either way, and a reset for an unknown username fails at step
 * two exactly as a wrong code does.
 */
export function OtpForgotPasswordPage() {
  const navigate = useNavigate();

  const [step, setStep] = useState<'ask' | 'verify' | 'choose'>('ask');
  const [username, setUsername] = useState('');
  const [resetToken, setResetToken] = useState('');
  const [notice, setNotice] = useState<string | null>(null);

  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');

  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Set when the reset itself is over (expired, too many tries) and only
  // starting again will help — the code box is then pointless.
  const [dead, setDead] = useState(false);

  const [cooldown, setCooldown] = useState(0);
  useEffect(() => {
    if (cooldown <= 0) return;
    const t = setTimeout(() => setCooldown(cooldown - 1), 1000);
    return () => clearTimeout(t);
  }, [cooldown]);

  const mismatch = confirm.length > 0 && password !== confirm;

  /** The reset-step body from either a success or an ApiError. */
  const outcome = (err: unknown): ResetStepResponse | null =>
    err instanceof ApiError && err.body && typeof err.body === 'object' && 'status' in err.body
      ? (err.body as ResetStepResponse)
      : null;

  const startAgain = () => {
    setStep('ask');
    setCode('');
    setPassword('');
    setConfirm('');
    setResetToken('');
    setDead(false);
    setError(null);
    setNotice(null);
  };

  const requestCode = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const result = await api.post<ForgotPasswordResponse>('/api/auth/forgot-password', {
        username: username.trim(),
      });
      setNotice(result.message);
      setResetToken(result.resetToken ?? '');
      setDead(false);
      setCode('');
      setCooldown(RESEND_COOLDOWN);
      setStep('verify');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not send a code. Try again.');
    } finally {
      setBusy(false);
    }
  };

  const resend = async () => {
    setError(null);
    setBusy(true);
    try {
      const result = await api.post<ForgotPasswordResponse>('/api/auth/resend-reset-code', { resetToken });
      setNotice(result.message);
      setCode('');
      setCooldown(RESEND_COOLDOWN);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not send a new code. Try again.');
    } finally {
      setBusy(false);
    }
  };

  const verifyCode = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const result = await api.post<ResetStepResponse>('/api/auth/verify-reset-code', {
        resetToken,
        code: code.trim(),
      });
      if (result.status === 'VERIFIED') {
        setNotice(result.message);
        setStep('choose');
      } else {
        setError(result.message);
      }
    } catch (err) {
      const body = outcome(err);
      if (body?.status === 'INVALID_OTP' && body.remainingAttempts != null) {
        setError(`${body.message} ${body.remainingAttempts} ${body.remainingAttempts === 1 ? 'try' : 'tries'} left.`);
      } else {
        setError(body?.message ?? (err instanceof ApiError ? err.message : 'Could not check the code. Try again.'));
      }

      // These end the reset. TEMPORARY_ERROR and RATE_LIMITED do not: the
      // same code may still work in a moment.
      if (body && ['EXPIRED', 'ATTEMPTS_EXCEEDED', 'ALREADY_USED', 'CANCELLED'].includes(body.status)) setDead(true);
      setCode('');
    } finally {
      setBusy(false);
    }
  };

  const setNewPassword = async (e: FormEvent) => {
    e.preventDefault();

    if (password !== confirm) {
      setError('The two passwords do not match.');
      return;
    }

    setError(null);
    setBusy(true);
    try {
      await api.post<ResetStepResponse>('/api/auth/reset-password', { resetToken, newPassword: password });
      // Straight to sign-in rather than signing them in here: proving you
      // hold a code is not the same as proving you know the password, and
      // typing the new one once more is the cheapest confirmation there is.
      navigate('/login?reset=1');
    } catch (err) {
      const body = outcome(err);
      setError(body?.message ?? (err instanceof ApiError ? err.message : 'Could not reset the password. Try again.'));

      // Anything but a bad password or a passing outage means this reset is
      // finished and only a new code will do.
      if (body && !['INVALID_PASSWORD', 'TEMPORARY_ERROR', 'RATE_LIMITED'].includes(body.status)) {
        setDead(true);
        setStep('verify');
      }
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

  if (step === 'verify') {
    return (
      <div className="auth-page">
        <form className="auth-card" onSubmit={verifyCode}>
          <h1>Enter your code</h1>
          {notice && !dead && <p className="status-line">{notice}</p>}
          <p className="auth-subtitle">Check WhatsApp on the mobile number on your account</p>

          {!dead && (
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
          )}

          {error && <p className="auth-error">{error}</p>}

          {dead ? (
            <button type="button" className="primary" onClick={startAgain}>Start again</button>
          ) : (
            <button type="submit" className="primary" disabled={busy || code.length < 6}>
              {busy ? 'Checking…' : 'Verify code'}
            </button>
          )}

          {!dead && (
            <p className="auth-footer">
              Did not get it?{' '}
              <button type="button" className="ghost" onClick={resend} disabled={busy || cooldown > 0}>
                {cooldown > 0 ? `Send again in ${cooldown}s` : 'Send it again'}
              </button>
              {' · '}
              <button type="button" className="ghost" onClick={startAgain}>Use a different username</button>
            </p>
          )}
        </form>
      </div>
    );
  }

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={setNewPassword}>
        <h1>Choose a new password</h1>
        {notice && <p className="status-line">{notice}</p>}

        <label>
          New password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="new-password"
            required
            minLength={8}
            autoFocus
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

        <button type="submit" className="primary" disabled={busy || mismatch}>
          {busy ? 'Saving…' : 'Set new password'}
        </button>
      </form>
    </div>
  );
}
