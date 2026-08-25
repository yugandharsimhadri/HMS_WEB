import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type {
  ClinicProfile, ClinicUser, Doctor, DocumentTheme, GeneralSettings,
  PharmacyProfile, TemporaryPasswordResponse,
} from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { StaffEditorDialog } from '../settings/StaffEditorDialog';
import { DataHealthTab } from '../settings/DataHealthTab';

type Tab = 'clinic' | 'pharmacy' | 'doctors' | 'staff' | 'branding' | 'modules' | 'health';

const TABS: { id: Tab; label: string }[] = [
  { id: 'clinic', label: 'Clinic' },
  { id: 'pharmacy', label: 'Pharmacy' },
  { id: 'doctors', label: 'Doctors' },
  // Distinct from Doctors on purpose: a doctor is someone a visit is booked
  // against, a user is someone who signs in. Most clinics have people who
  // are one and not the other.
  { id: 'staff', label: 'Staff logins' },
  { id: 'branding', label: 'Document branding' },
  { id: 'modules', label: 'Features' },
  { id: 'health', label: 'Data health' },
];

/**
 * Print size as a few named steps rather than a free number box.
 *
 * The underlying field is a delta in points, and a box would invite "20",
 * which produces a prescription with three words on it. These are the range
 * that still lays out.
 */
const FONT_SIZE_STEPS: { delta: number; label: string }[] = [
  { delta: -1, label: 'Smaller' },
  { delta: 0, label: 'Normal' },
  { delta: 1, label: 'Larger' },
  { delta: 2, label: 'Largest' },
];

/**
 * The roles that repeat across all nine printed documents, in the order they
 * appear down a page. Anything not listed here — the "Rx" mark, a section
 * heading — is a proportion within one document rather than a house style,
 * and still moves with the print size adjustment.
 */
const ELEMENT_SIZES: { key: keyof typeof DESKTOP_SIZES; label: string }[] = [
  { key: 'letterheadNameSize', label: 'Clinic name' },
  { key: 'contactLineSize', label: 'Address & phone' },
  { key: 'documentKindSize', label: 'Document title' },
  { key: 'bodyTextSize', label: 'Body text' },
  { key: 'tableHeaderSize', label: 'Table headings' },
  { key: 'tableRowSize', label: 'Table rows' },
  { key: 'totalsSize', label: 'Amount total' },
  { key: 'footerSize', label: 'Footer & notes' },
];

/** The desktop's own sizes, which are also the server's defaults. */
const DESKTOP_SIZES = {
  letterheadNameSize: 15,
  contactLineSize: 8,
  documentKindSize: 8.6,
  bodyTextSize: 9,
  tableHeaderSize: 7.5,
  tableRowSize: 8.5,
  totalsSize: 13,
  footerSize: 7.4,
};

function SavedNotice({ shown }: { shown: boolean }) {
  return shown ? <p className="hint">Saved.</p> : null;
}

function useSavedFlash(): [boolean, () => void] {
  const [saved, setSaved] = useState(false);
  const flash = () => {
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  };
  return [saved, flash];
}

export function SettingsPage() {
  const [tab, setTab] = useState<Tab>('clinic');

  return (
    <div className="page">
      <h1>Settings</h1>

      <div className="tabs">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            className={t.id === tab ? 'tab active' : 'tab'}
            onClick={() => setTab(t.id)}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'clinic' && <ClinicTab />}
      {tab === 'pharmacy' && <PharmacyTab />}
      {tab === 'doctors' && <DoctorsTab />}
      {tab === 'staff' && <StaffTab />}
      {tab === 'branding' && <BrandingTab />}
      {tab === 'modules' && <ModulesTab />}
      {tab === 'health' && <DataHealthTab />}
    </div>
  );
}

/**
 * Staff logins.
 *
 * Until this existed a clinic had exactly one account, so the receptionist
 * and the pharmacist both signed in as the owner and every row's "created
 * by" named the owner regardless of who did the work. That is what this
 * fixes; the roles were always there.
 *
 * Admin-only, and the API enforces it independently — a non-admin gets 403
 * from every call here, not just a hidden tab.
 */
function StaffTab() {
  const { session } = useAuth();
  const [users, setUsers] = useState<ClinicUser[]>([]);
  const [editing, setEditing] = useState<ClinicUser | null | undefined>(undefined);
  const [issued, setIssued] = useState<TemporaryPasswordResponse | null>(null);
  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);

  // The clinic half of every username here, taken from the signed-in user's
  // own — the server appends the same thing, this only shows it.
  const clinicCode = session?.username.slice(session.username.lastIndexOf('@') + 1) ?? '';

  const load = async () => {
    try {
      setUsers(await api.get<ClinicUser[]>('/api/users'));
      setError(null);
    } catch (err) {
      setError(err instanceof ApiError && err.status === 403
        ? 'Only an Admin can manage staff logins.'
        : 'Could not load the staff list.');
    }
  };

  useEffect(() => { void load(); }, []);

  const resetPassword = async (u: ClinicUser) => {
    if (!window.confirm(
      `Reset the password for ${u.username}?\n\n` +
      'Their current password stops working immediately. You will be shown a temporary one to give them.')) return;

    setError(null);
    try {
      setIssued(await api.post<TemporaryPasswordResponse>(`/api/users/${u.id}/reset-password`, {}));
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not reset that password.');
    }
  };

  return (
    <section className="settings-form">
      <p className="hint">
        Everyone who signs in. A person's username is their name plus your clinic code
        — <strong>@{clinicCode}</strong> is added automatically.
      </p>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      {/* Shown once. It is hashed the moment it is issued and cannot be
          retrieved again — losing it means issuing another. */}
      {issued && issued.temporaryPassword && (
        <div className="card issued-password">
          <h3>Temporary password for {issued.username}</h3>
          <p className="issued-value">{issued.temporaryPassword}</p>
          <p className="hint">
            Give this to them now — it is not stored and cannot be shown again. They will be asked to
            choose their own the moment they sign in.
          </p>
          <button type="button" className="ghost" onClick={() => setIssued(null)}>Done</button>
        </div>
      )}

      <div className="inline-form">
        <button type="button" className="primary" onClick={() => setEditing(null)}>+ Add someone</button>
      </div>

      <table>
        <thead>
          <tr><th>Username</th><th>Name</th><th>Role</th><th>Last signed in</th><th></th></tr>
        </thead>
        <tbody>
          {users.map((u) => (
            <tr key={u.id} className={u.isActive ? undefined : 'row-inactive'}>
              <td>
                {u.username}
                {u.isYou && <span className="hint"> · you</span>}
                {!u.isActive && <span className="hint"> · disabled</span>}
                {u.mustChangePassword && <div className="hint">must choose a new password</div>}
              </td>
              <td>{u.displayName}</td>
              <td>{u.role}</td>
              <td>{u.lastLoginOn ? new Date(u.lastLoginOn).toLocaleString() : 'never'}</td>
              <td className="row-actions">
                <button type="button" className="ghost" onClick={() => setEditing(u)}>Edit</button>
                <button type="button" className="ghost" onClick={() => void resetPassword(u)}>
                  Reset password
                </button>
              </td>
            </tr>
          ))}
          {users.length === 0 && <tr><td colSpan={5}>Nobody yet.</td></tr>}
        </tbody>
      </table>

      {editing !== undefined && (
        <StaffEditorDialog
          existing={editing}
          clinicCode={clinicCode}
          onClose={() => setEditing(undefined)}
          onSaved={async (message, temporary) => {
            setEditing(undefined);
            await load();
            setStatus(message);
            if (temporary?.temporaryPassword) setIssued(temporary);
          }}
        />
      )}
    </section>
  );
}

function ClinicTab() {
  const [profile, setProfile] = useState<ClinicProfile | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, flash] = useSavedFlash();

  useEffect(() => {
    void api.get<ClinicProfile>('/api/settings/clinic').then(setProfile);
  }, []);

  const onSave = async (e: FormEvent) => {
    e.preventDefault();
    if (!profile) return;
    setError(null);
    try {
      await api.post('/api/settings/clinic', profile);
      flash();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save.');
    }
  };

  if (!profile) return <p className="hint">Loading…</p>;

  return (
    <form className="card settings-form" onSubmit={onSave}>
      <label>
        Clinic name
        <input value={profile.name} onChange={(e) => setProfile({ ...profile, name: e.target.value })} />
      </label>
      <label>
        Address line 1
        <input value={profile.addressLine} onChange={(e) => setProfile({ ...profile, addressLine: e.target.value })} />
      </label>
      <label>
        Address line 2
        <input
          value={profile.addressLine2}
          onChange={(e) => setProfile({ ...profile, addressLine2: e.target.value })}
        />
      </label>
      <label>
        Phone
        <input value={profile.phone} onChange={(e) => setProfile({ ...profile, phone: e.target.value })} />
      </label>
      <label className="checkbox-label">
        <input
          type="checkbox"
          checked={profile.gstRegistered}
          onChange={(e) => setProfile({ ...profile, gstRegistered: e.target.checked })}
        />
        GST registered
      </label>
      {profile.gstRegistered && (
        <label>
          GSTIN
          <input value={profile.gstin} onChange={(e) => setProfile({ ...profile, gstin: e.target.value })} />
        </label>
      )}
      <div className="settings-row">
        <label>
          Morning from
          <input
            type="time"
            value={profile.morningFrom}
            onChange={(e) => setProfile({ ...profile, morningFrom: e.target.value })}
          />
        </label>
        <label>
          Morning to
          <input
            type="time"
            value={profile.morningTo}
            onChange={(e) => setProfile({ ...profile, morningTo: e.target.value })}
          />
        </label>
        <label>
          Evening from
          <input
            type="time"
            value={profile.eveningFrom}
            onChange={(e) => setProfile({ ...profile, eveningFrom: e.target.value })}
          />
        </label>
        <label>
          Evening to
          <input
            type="time"
            value={profile.eveningTo}
            onChange={(e) => setProfile({ ...profile, eveningTo: e.target.value })}
          />
        </label>
      </div>
      <label>
        Footer text (prescription / fee receipt)
        <input value={profile.footerText} onChange={(e) => setProfile({ ...profile, footerText: e.target.value })} />
      </label>

      {error && <p className="auth-error">{error}</p>}
      <div className="settings-actions">
        <button type="submit">Save</button>
        <SavedNotice shown={saved} />
      </div>
    </form>
  );
}

function PharmacyTab() {
  const [profile, setProfile] = useState<PharmacyProfile | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, flash] = useSavedFlash();

  useEffect(() => {
    void api.get<PharmacyProfile>('/api/settings/pharmacy').then(setProfile);
  }, []);

  const onSave = async (e: FormEvent) => {
    e.preventDefault();
    if (!profile) return;
    setError(null);
    try {
      await api.post('/api/settings/pharmacy', profile);
      flash();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save.');
    }
  };

  if (!profile) return <p className="hint">Loading…</p>;

  return (
    <form className="card settings-form" onSubmit={onSave}>
      <label>
        Pharmacy name
        <input value={profile.name} onChange={(e) => setProfile({ ...profile, name: e.target.value })} />
      </label>
      <label>
        Address line 1
        <input value={profile.addressLine} onChange={(e) => setProfile({ ...profile, addressLine: e.target.value })} />
      </label>
      <label>
        Address line 2
        <input
          value={profile.addressLine2}
          onChange={(e) => setProfile({ ...profile, addressLine2: e.target.value })}
        />
      </label>
      <label>
        Phone
        <input value={profile.phone} onChange={(e) => setProfile({ ...profile, phone: e.target.value })} />
      </label>
      <label className="checkbox-label">
        <input
          type="checkbox"
          checked={profile.gstRegistered}
          onChange={(e) => setProfile({ ...profile, gstRegistered: e.target.checked })}
        />
        GST registered
      </label>
      {profile.gstRegistered && (
        <label>
          GSTIN
          <input value={profile.gstin} onChange={(e) => setProfile({ ...profile, gstin: e.target.value })} />
        </label>
      )}
      <label>
        Drug licence no.
        <input
          value={profile.drugLicenceNo}
          onChange={(e) => setProfile({ ...profile, drugLicenceNo: e.target.value })}
        />
      </label>
      <label>
        Pharmacist name
        <input
          value={profile.pharmacistName}
          onChange={(e) => setProfile({ ...profile, pharmacistName: e.target.value })}
        />
      </label>
      <label>
        Footer text (medicine bill)
        <input value={profile.footerText} onChange={(e) => setProfile({ ...profile, footerText: e.target.value })} />
      </label>

      {error && <p className="auth-error">{error}</p>}
      <div className="settings-actions">
        <button type="submit">Save</button>
        <SavedNotice shown={saved} />
      </div>
    </form>
  );
}

const emptyDoctor = { name: '', speciality: '', registrationNo: '', consultationFee: '' };

function DoctorsTab() {
  const [doctors, setDoctors] = useState<Doctor[]>([]);
  const [form, setForm] = useState(emptyDoctor);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const load = () => {
    void api.get<Doctor[]>('/api/doctors').then(setDoctors);
  };

  useEffect(load, []);

  const onSave = async (e: FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      await api.post('/api/doctors', {
        name: form.name,
        speciality: form.speciality || null,
        registrationNo: form.registrationNo || null,
        consultationFee: Number(form.consultationFee) || 0,
        isActive: true,
      });
      setForm(emptyDoctor);
      load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the doctor.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="card settings-form">
      <form className="inline-form" onSubmit={onSave}>
        <input
          placeholder="Name"
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
          required
        />
        <input
          placeholder="Speciality"
          value={form.speciality}
          onChange={(e) => setForm({ ...form, speciality: e.target.value })}
        />
        <input
          placeholder="Registration no."
          value={form.registrationNo}
          onChange={(e) => setForm({ ...form, registrationNo: e.target.value })}
        />
        <input
          placeholder="Consultation fee"
          type="number"
          min="0"
          step="0.01"
          value={form.consultationFee}
          onChange={(e) => setForm({ ...form, consultationFee: e.target.value })}
          required
        />
        <button type="submit" disabled={saving}>
          {saving ? 'Saving…' : 'Add doctor'}
        </button>
      </form>

      {error && <p className="auth-error">{error}</p>}

      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Speciality</th>
            <th>Registration no.</th>
            <th>Fee</th>
          </tr>
        </thead>
        <tbody>
          {doctors.map((d) => (
            <tr key={d.id}>
              <td>{d.name}</td>
              <td>{d.speciality ?? ''}</td>
              <td>{d.registrationNo ?? ''}</td>
              <td>{d.consultationFee.toFixed(2)}</td>
            </tr>
          ))}
          {doctors.length === 0 && (
            <tr>
              <td colSpan={4}>No doctors yet.</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

function BrandingTab() {
  const [theme, setTheme] = useState<DocumentTheme | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, flash] = useSavedFlash();

  useEffect(() => {
    void api.get<DocumentTheme>('/api/settings/document-theme').then(setTheme);
  }, []);

  const onSave = async (e: FormEvent) => {
    e.preventDefault();
    if (!theme) return;
    setError(null);
    try {
      await api.post('/api/settings/document-theme', theme);
      flash();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save.');
    }
  };

  /**
   * Reads the chosen image straight into the theme as a data URI.
   *
   * Bounded hard at 200 KB. The logo is embedded in every document the clinic
   * prints, so a 4 MB photograph of a signboard would be carried into every
   * prescription, receipt and invoice for as long as it is set.
   */
  const chooseLogo = (file: File) => {
    setError(null);

    if (!file.type.startsWith('image/')) {
      setError('Choose an image file — a PNG or JPG.');
      return;
    }
    if (file.size > 200 * 1024) {
      setError(`That image is ${Math.round(file.size / 1024)} KB. Logos are limited to 200 KB, because ` +
               'this one is embedded in every document the clinic prints. Scale it down and try again.');
      return;
    }

    const reader = new FileReader();
    reader.onload = () => setTheme((t) => (t ? {
      ...t,
      logoBase64: String(reader.result),
      logoContentType: file.type,
    } : t));
    reader.onerror = () => setError('That file could not be read.');
    reader.readAsDataURL(file);
  };

  if (!theme) return <p className="hint">Loading…</p>;

  return (
    <form className="card settings-form" onSubmit={onSave}>
      <div className="settings-row">
        <label>Clinic logo</label>
        <p className="hint">
          Printed on the left of the letterhead of every prescription, receipt, invoice and report.
        </p>

        {theme.logoBase64 ? (
          <div className="logo-preview">
            <img src={theme.logoBase64} alt="Clinic logo" />
            <button
              type="button"
              className="ghost"
              onClick={() => setTheme({ ...theme, logoBase64: null, logoContentType: null })}
            >
              Remove
            </button>
          </div>
        ) : (
          <p className="hint">No logo — documents print with the clinic's name alone.</p>
        )}

        <input
          type="file"
          accept="image/png,image/jpeg,image/webp"
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) chooseLogo(file);
            // Cleared so choosing the same file twice still fires.
            e.target.value = '';
          }}
        />
        <p className="hint">PNG or JPG, up to 200 KB. Remember to Save.</p>
      </div>

      <label>
        Shared footer (used when a clinic/pharmacy footer is blank)
        <input value={theme.footer} onChange={(e) => setTheme({ ...theme, footer: e.target.value })} />
      </label>
      <label>
        Print font family
        <input
          value={theme.printFontFamily ?? ''}
          placeholder="Segoe UI"
          onChange={(e) => setTheme({ ...theme, printFontFamily: e.target.value || null })}
        />
      </label>
      <label>
        Title font family
        <input
          value={theme.titleFontFamily ?? ''}
          placeholder="Same as print font"
          onChange={(e) => setTheme({ ...theme, titleFontFamily: e.target.value || null })}
        />
      </label>

      {/* An adjustment, not an absolute size. Every printed document is laid
          out around the sizes the desktop uses, and a nudge moves all of them
          together — which keeps a receipt looking like a receipt instead of
          letting one field grow out of proportion to the rest. */}
      <div className="settings-row">
        <label>Print size</label>
        <div className="inline-form">
          {FONT_SIZE_STEPS.map((s) => (
            <button
              key={s.delta}
              type="button"
              className={theme.printFontSizeDelta === s.delta ? 'pill active' : 'pill'}
              onClick={() => setTheme({ ...theme, printFontSizeDelta: s.delta })}
            >
              {s.label}
            </button>
          ))}
        </div>
        <p className="hint">
          Applies to every document — prescriptions, receipts, invoices, reports. Normal matches the
          desktop exactly; the others shift every size on the page by the same amount.
        </p>
      </div>

      <div className="settings-row">
        <label>Clinic name size</label>
        <div className="inline-form">
          {FONT_SIZE_STEPS.map((s) => (
            <button
              key={s.delta}
              type="button"
              className={theme.titleFontSizeDelta === s.delta ? 'pill active' : 'pill'}
              onClick={() => setTheme({ ...theme, titleFontSizeDelta: s.delta })}
            >
              {s.label}
            </button>
          ))}
        </div>
        <p className="hint">
          On top of the print size, for the clinic's name in the letterhead alone — the one line a clinic
          usually wants larger than everything else.
        </p>
      </div>

      {/* The absolute sizes. The two settings above move the whole page at
          once, which is what most clinics want; this is for the one that has
          a printer, a paper size and an opinion of its own. Defaults are the
          desktop's numbers, so an untouched install prints an identical page. */}
      <div className="settings-block">
        <span className="field-title">Element sizes</span>
        <div className="size-grid">
          {ELEMENT_SIZES.map((el) => (
            <label key={el.key} className="size-field">
              <span>{el.label}</span>
              <input
                type="number"
                min={4}
                max={40}
                step={0.1}
                value={theme[el.key]}
                onChange={(e) =>
                  setTheme({ ...theme, [el.key]: Number(e.target.value) })
                }
              />
              <span className="size-unit">pt</span>
            </label>
          ))}
        </div>
        <p className="hint">
          Points, before the print size adjustment above is added. The identity grid under the
          letterhead follows the table sizes — its label sits just under the table header and its
          value just under the table row, which is what makes it read as a grid.
        </p>
        <button type="button" className="pill" onClick={() => setTheme({ ...theme, ...DESKTOP_SIZES })}>
          Reset to desktop defaults
        </button>
      </div>

      {error && <p className="auth-error">{error}</p>}
      <div className="settings-actions">
        <button type="submit">Save</button>
        <SavedNotice shown={saved} />
      </div>
    </form>
  );
}

const MODULE_TOGGLES: { key: keyof GeneralSettings; label: string; description: string }[] = [
  { key: 'opdEnabled', label: 'OPD', description: 'Queue, consultations, prescriptions' },
  { key: 'pharmacyEnabled', label: 'Pharmacy', description: 'Counter billing and stock' },
  { key: 'diagnosticsEnabled', label: 'Diagnostics', description: 'Flat named-test billing' },
  { key: 'appointmentsEnabled', label: 'Appointments', description: 'Advance booking and check-in' },
  { key: 'pediatricsEnabled', label: 'Pediatrics', description: 'Vaccines and growth chart' },
  { key: 'dentistEnabled', label: 'Dentist', description: 'Cases, sittings and payments' },
  { key: 'pathologyLabEnabled', label: 'Pathology Lab', description: 'Analyte-level lab reports' },
];

function ModulesTab() {
  const [general, setGeneral] = useState<GeneralSettings | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, flash] = useSavedFlash();

  useEffect(() => {
    void api.get<GeneralSettings>('/api/settings/general').then(setGeneral);
  }, []);

  const toggle = (key: keyof GeneralSettings) => {
    if (!general) return;
    setGeneral({ ...general, [key]: !general[key] });
  };

  const onSave = async (e: FormEvent) => {
    e.preventDefault();
    if (!general) return;
    setError(null);
    try {
      await api.post('/api/settings/general', general);
      // The sidebar decides which modules to show from this same call, and
      // it read it once when the shell mounted. Without this it keeps
      // showing the old set until a full page reload — which nobody does,
      // so switching a module on looked like it had silently failed.
      window.dispatchEvent(new CustomEvent('sivayaanhms:general-settings-changed'));
      flash();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'At least one module must stay on.');
    }
  };

  if (!general) return <p className="hint">Loading…</p>;

  return (
    <form className="card settings-form" onSubmit={onSave}>
      <p className="hint">Switching a module off hides it everywhere - the sidebar, Reports, Patients.</p>
      {MODULE_TOGGLES.map((m) => (
        <label key={m.key} className="checkbox-label module-toggle">
          <input type="checkbox" checked={Boolean(general[m.key])} onChange={() => toggle(m.key)} />
          <span>
            <strong>{m.label}</strong>
            <span className="hint"> — {m.description}</span>
          </span>
        </label>
      ))}


      {error && <p className="auth-error">{error}</p>}
      <div className="settings-actions">
        <button type="submit">Save</button>
        <SavedNotice shown={saved} />
      </div>
    </form>
  );
}
