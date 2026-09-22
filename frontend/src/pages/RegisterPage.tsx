import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { RegisterTenantResponse } from '../api/types';

/**
 * Mirrors TenantsController.NormaliseSlug, so the username shown on this
 * page is the one the server will actually mint. Kept in step with it
 * deliberately: this is the only moment a person is told what their username
 * will be, and being told something different from what they get is worse
 * than not being told at all.
 */
function tidyCode(raw: string): string {
  return raw
    .trim()
    .toLowerCase()
    .replace(/['’"]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 24)
    .replace(/-+$/, '');
}

/**
 * A first guess at the clinic code, from the first word of the clinic's
 * name — "Twinkle Children's Hospital" suggests "twinkle". Only a
 * suggestion: it stops the moment the person edits the field themselves,
 * because the code is theirs to choose and may well already be taken.
 *
 * Words like "the" are skipped so "The City Clinic" suggests "city", not
 * "the".
 */
const SKIP_WORDS = new Set(['the', 'a', 'an', 'dr', 'sri', 'shri', 'st']);

function suggestCode(clinicName: string): string {
  const words = clinicName.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const first = words.find((w) => !SKIP_WORDS.has(w.replace(/[^a-z]/g, ''))) ?? words[0] ?? '';
  return tidyCode(first);
}

/**
 * Clinic signup.
 *
 * Two names, because they do different jobs. The full name goes on printed
 * prescriptions and bills and may duplicate freely — unrelated clinics share
 * names all the time. The clinic code is the short handle that lives inside
 * every username here ("admin@twinkle"), and it is the one thing on this
 * page that has to be unique.
 *
 * Whoever registers is the clinic's owner and its first Admin; they add
 * everyone else afterwards from Settings.
 */
export function RegisterPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [clinicName, setClinicName] = useState('');
  const [clinicCode, setClinicCode] = useState('');
  const [codeEdited, setCodeEdited] = useState(false);
  const [username, setUsername] = useState('');
  const [phone, setPhone] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Follows the clinic name until the person takes the field over. Typing
  // the clinic's name and having a sensible code appear is most of the work
  // done for the common case; overwriting it is one click away for the
  // clinic whose obvious code is already gone.
  const code = codeEdited ? tidyCode(clinicCode) : suggestCode(clinicName);
  const localPart = username.trim().toLowerCase();

  // A warning rather than a blocked button: someone halfway through typing
  // the second password has not made a mistake yet.
  const mismatch = confirm.length > 0 && password !== confirm;

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();

    // Confirmed here rather than server-side — a mistyped repeat is a typing
    // slip to catch before the request leaves, and sending the same secret
    // twice only widens where it can leak.
    if (password !== confirm) {
      setError('The two passwords do not match.');
      return;
    }

    setError(null);
    setBusy(true);
    try {
      const result = await api.post<RegisterTenantResponse>('/api/tenants/register', {
        clinicName,
        clinicCode: code,
        username,
        password,
        phone: phone.trim(),
      });
      // Straight into the new clinic — no separate "now go and log in" step.
      // adminUsername comes back fully qualified ("you@your-clinic"), which
      // is the only form sign-in accepts.
      await login(result.adminUsername, password);
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
        <p className="auth-subtitle">You will be the clinic's admin — add your staff afterwards from Settings</p>

        <label>
          Clinic name
          <input
            value={clinicName}
            onChange={(e) => setClinicName(e.target.value)}
            placeholder="Twinkle Children's Hospital"
            required
            minLength={3}
            autoFocus
          />
          <span className="hint">The full name, as it should appear on prescriptions and bills.</span>
        </label>

        <label>
          Clinic code
          <input
            value={codeEdited ? clinicCode : code}
            onChange={(e) => { setCodeEdited(true); setClinicCode(e.target.value); }}
            placeholder="twinkle"
            required
            minLength={3}
            maxLength={24}
          />
          <span className="hint">
            A short handle for your clinic — it goes inside everyone's username here, so keep it easy to
            type. Must be unique, so if it is taken, add your town.
          </span>
        </label>

        <label>
          Username
          <input
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="yourname"
            autoComplete="username"
            required
            pattern="[A-Za-z0-9][A-Za-z0-9._\-]{2,31}"
            title="3–32 characters: letters, numbers, dots, hyphens or underscores"
          />
        </label>

        {/* The clinic is asked for here and nowhere else — from now on it
            travels inside the username, so say plainly what that will be
            before they commit to it. Shows the tidied code, not the raw
            typing, so "St Mary's" is visibly "st-marys" before they find
            out at the login page tomorrow. */}
        {code && localPart && (
          <p className="hint">
            You will sign in as <strong>{localPart}@{code}</strong>
          </p>
        )}

        {/* Asked here because this account cannot be recovered by asking an
            admin — it is the admin. The number is only ever used to send a
            reset code, which the field says plainly: a signup form asking
            for a mobile number without saying why is the kind of thing
            people give a fake answer to. */}
        <label>
          Mobile number
          <input
            type="tel"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            placeholder="+91 98765 43210"
            autoComplete="tel"
            required
          />
          <span className="hint">
            Used only to send password-reset codes, and to confirm your account by SMS or WhatsApp.
            Include the country code.
          </span>
        </label>

        <label>
          Password
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
          Retype password
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
