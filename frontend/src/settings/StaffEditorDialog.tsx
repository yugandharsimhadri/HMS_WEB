import { useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { ClinicUser, TemporaryPasswordResponse, UserRole } from '../api/types';
import { useModalBehaviour } from '../shell/useModalBehaviour';

interface Props {
  existing: ClinicUser | null;
  /** Shown beside the username box so it is obvious what will be appended. */
  clinicCode: string;
  onClose: () => void;
  onSaved: (message: string, temporary?: TemporaryPasswordResponse) => void;
}

const ROLES: { value: UserRole; label: string; what: string }[] = [
  { value: 'Admin', label: 'Admin', what: 'Everything, including staff logins and settings' },
  { value: 'Doctor', label: 'Doctor', what: 'Consultations, prescriptions and clinical records' },
  { value: 'Pharmacy', label: 'Pharmacy', what: 'The counter, medicines and stock' },
  { value: 'Diagnosis', label: 'Diagnosis', what: 'Diagnostics and the pathology lab' },
];

/**
 * One member of staff.
 *
 * The username box takes only the part before the clinic. The clinic half is
 * appended by the server from the tenant the request is already scoped to —
 * shown here as a suffix so nobody types it twice, and so nobody imagines
 * they could put someone into a different clinic by changing it.
 */
export function StaffEditorDialog({ existing, clinicCode, onClose, onSaved }: Props) {
  const initialLocal = existing ? existing.username.slice(0, existing.username.lastIndexOf('@')) : '';

  const [localPart, setLocalPart] = useState(initialLocal);
  const [displayName, setDisplayName] = useState(existing?.displayName ?? '');
  const [role, setRole] = useState<UserRole>(existing?.role ?? 'Pharmacy');
  const [isActive, setIsActive] = useState(existing?.isActive ?? true);
  const [phone, setPhone] = useState(existing?.phone ?? '');
  const [password, setPassword] = useState('');

  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const save = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const result = await api.post<TemporaryPasswordResponse>('/api/users', {
        id: existing?.id ?? null,
        localPart,
        displayName,
        role,
        isActive,
        phone: phone.trim(),
        password: password.trim() ? password : null,
      });
      onSaved(`${result.username} saved.`, result);
    } catch (err) {
      // The self-lockout refusals ("you cannot remove your own Admin role")
      // land here, and are the reason this is shown rather than swallowed.
      setError(err instanceof ApiError ? err.message : 'Could not save this person.');
      setBusy(false);
    }
  };

  const cardRef = useModalBehaviour(onClose);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Staff login">
      <div className="overlay-card" ref={cardRef}>
        <div className="overlay-head">
          <h2>{existing ? existing.username : 'Add someone'}</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body settings-form" onSubmit={save}>
          {error && <p className="auth-error">{error}</p>}

          <div className="settings-row">
            <label>Username</label>
            <div className="inline-form">
              <input
                value={localPart}
                onChange={(e) => setLocalPart(e.target.value)}
                placeholder="reception"
                required
                autoFocus={!existing}
              />
              <span className="hint">@{clinicCode}</span>
            </div>
            <p className="hint">This is what they type to sign in. Lowercase, no spaces.</p>
          </div>

          <div className="settings-row">
            <label>Name</label>
            <input
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="Meera Rao"
              required
            />
            <p className="hint">Shown to the rest of the clinic, and on what they record.</p>
          </div>

          <div className="settings-row">
            <label>Role</label>
            <select value={role} onChange={(e) => setRole(e.target.value as UserRole)}>
              {ROLES.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
            </select>
            <p className="hint">{ROLES.find((r) => r.value === role)?.what}</p>
          </div>

          {/* Optional here, unlike at signup. Without a number this person
              cannot use Forgot password and has to ask an Admin — which is
              exactly the position everyone was in before, so it is a
              nudge rather than a blocker. */}
          <div className="settings-row">
            <label>Mobile number</label>
            <input
              type="tel"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              placeholder="+91 98765 43210"
              autoComplete="tel"
            />
            <p className="hint">
              {phone.trim()
                ? 'Used only to send password-reset codes. Include the country code.'
                : 'Optional — but without it they cannot reset their own password and will have to ask you.'}
            </p>
          </div>

          <div className="settings-row">
            <label>{existing ? 'New password' : 'Password'}</label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="new-password"
              minLength={8}
              placeholder={existing ? 'Leave blank to keep their current one' : 'Leave blank to generate one'}
            />
            <p className="hint">
              {existing
                ? 'Leave this blank unless you are changing it. Either way they choose their own at next sign-in.'
                : 'Leave blank and one is generated for you — safer than inventing one, and shown once after saving.'}
            </p>
          </div>

          <label className="checkbox-label">
            <input type="checkbox" checked={isActive} onChange={() => setIsActive(!isActive)} />
            <span>Active — turn this off when somebody leaves, rather than deleting them</span>
          </label>
          <p className="hint">
            Deactivating keeps everything they recorded intact and attributed. There is deliberately no
            delete.
          </p>

          <div className="overlay-actions">
            <button type="submit" className="primary" disabled={busy}>Save</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
