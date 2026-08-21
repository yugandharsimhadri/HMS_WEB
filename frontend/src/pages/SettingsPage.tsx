import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { ClinicProfile, DocumentTheme, GeneralSettings, PharmacyProfile } from '../api/types';

type Tab = 'clinic' | 'pharmacy' | 'branding' | 'modules';

const TABS: { id: Tab; label: string }[] = [
  { id: 'clinic', label: 'Clinic' },
  { id: 'pharmacy', label: 'Pharmacy' },
  { id: 'branding', label: 'Document branding' },
  { id: 'modules', label: 'Features' },
];

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
      {tab === 'branding' && <BrandingTab />}
      {tab === 'modules' && <ModulesTab />}
    </div>
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

  if (!theme) return <p className="hint">Loading…</p>;

  return (
    <form className="card settings-form" onSubmit={onSave}>
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

      <label className="checkbox-label">
        <input
          type="checkbox"
          checked={general.requireLogin}
          onChange={() => setGeneral({ ...general, requireLogin: !general.requireLogin })}
        />
        Require login
      </label>

      {error && <p className="auth-error">{error}</p>}
      <div className="settings-actions">
        <button type="submit">Save</button>
        <SavedNotice shown={saved} />
      </div>
    </form>
  );
}
