import { useCallback, useEffect, useMemo, useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { api } from '../api/client';
import type { GeneralSettings } from '../api/types';
import { CommandPalette, type Destination, type PaletteAction } from '../shell/CommandPalette';
import { ShortcutSheet } from '../shell/ShortcutSheet';
import { describeCombo, useHotkey } from '../shell/hotkeys';
import {
  IconCalendar, IconDashboard, IconDensity, IconDentist, IconDiagnostics, IconHelp,
  IconInventory, IconKey, IconLab, IconMedicines, IconMoon, IconPatients, IconPediatrics,
  IconPharmacy, IconQueue, IconReports, IconSearch, IconSettings, IconSignOut, IconSun,
} from '../shell/icons';

/** Where the local overrides live. The clinic's own default still comes from
 *  Settings; these two only record what this person chose on this machine. */
const THEME_KEY = 'sivayaanhms:theme';
const DENSITY_KEY = 'sivayaanhms:density';

type Theme = 'light' | 'dark';
type Density = 'dense' | 'comfortable';

function applyTheme(theme: Theme) {
  document.documentElement.dataset.theme = theme;
}

function applyDensity(density: Density) {
  document.documentElement.dataset.density = density;
}

export function AppShell() {
  const { session, logout } = useAuth();
  const navigate = useNavigate();
  const [general, setGeneral] = useState<GeneralSettings | null>(null);

  const [paletteOpen, setPaletteOpen] = useState(false);
  const [sheetOpen, setSheetOpen] = useState(false);

  // Dense is the default because this is a counter tool. Anyone who wants
  // room can take it, and the choice sticks.
  const [density, setDensity] = useState<Density>(
    () => (localStorage.getItem(DENSITY_KEY) as Density | null) ?? 'dense',
  );
  const [theme, setTheme] = useState<Theme | null>(
    () => localStorage.getItem(THEME_KEY) as Theme | null,
  );

  useEffect(() => {
    // Mirrors the desktop: a module switched off under Settings -> Features
    // disappears from the nav entirely, not just greyed out.
    const read = () =>
      void api.get<GeneralSettings>('/api/settings/general').then(setGeneral).catch(() => {});

    read();

    // Re-read when Features is saved. Reading only on mount meant the nav
    // kept the module set the shell started with, so turning a module on
    // appeared to do nothing at all until the next full page load.
    window.addEventListener('sivayaanhms:general-settings-changed', read);
    return () => window.removeEventListener('sivayaanhms:general-settings-changed', read);
  }, []);

  // The clinic's stored theme is the starting point; a local toggle overrides
  // it. `GeneralSettings.Theme` was carried across in the port and then read
  // by nothing at all, which is why the setting appeared to do nothing.
  const effectiveTheme: Theme = theme ?? (general?.theme === 'Dark' ? 'dark' : 'light');

  useEffect(() => { applyTheme(effectiveTheme); }, [effectiveTheme]);
  useEffect(() => { applyDensity(density); }, [density]);

  const toggleTheme = useCallback(() => {
    setTheme((current) => {
      const base = current ?? (general?.theme === 'Dark' ? 'dark' : 'light');
      const next: Theme = base === 'dark' ? 'light' : 'dark';
      localStorage.setItem(THEME_KEY, next);
      return next;
    });
  }, [general?.theme]);

  const toggleDensity = useCallback(() => {
    setDensity((current) => {
      const next: Density = current === 'dense' ? 'comfortable' : 'dense';
      localStorage.setItem(DENSITY_KEY, next);
      return next;
    });
  }, []);

  const onLogout = useCallback(() => {
    logout();
    navigate('/login');
  }, [logout, navigate]);

  // Which modules are on. Everything below reads this one list so the rail,
  // the palette and the shortcut sheet can never disagree about what exists.
  const items = useMemo(() => {
    const on = (flag: boolean | undefined, fallback = false) => flag ?? fallback;
    return [
      { to: '/dashboard', label: 'Dashboard', group: 'Go to', icon: IconDashboard, show: true, end: false },
      { to: '/', label: 'OPD Queue', group: 'Go to', icon: IconQueue, show: general?.opdEnabled !== false, end: true, keywords: 'queue token visit' },
      { to: '/patients', label: 'Patients', group: 'Go to', icon: IconPatients, show: true, end: false, keywords: 'register uhid' },
      { to: '/appointments', label: 'Appointments', group: 'Go to', icon: IconCalendar, show: on(general?.appointmentsEnabled), end: false, keywords: 'booking reminder' },
      { sep: true as const, show: true },
      { to: '/diagnostics', label: 'Diagnostics', group: 'Go to', icon: IconDiagnostics, show: on(general?.diagnosticsEnabled), end: false, keywords: 'test bill' },
      { to: '/pediatrics', label: 'Pediatrics', group: 'Go to', icon: IconPediatrics, show: on(general?.pediatricsEnabled), end: false, keywords: 'growth vaccination child' },
      { to: '/dentist', label: 'Dentist', group: 'Go to', icon: IconDentist, show: on(general?.dentistEnabled), end: false, keywords: 'dental sitting' },
      { to: '/lab', label: 'Pathology Lab', group: 'Go to', icon: IconLab, show: on(general?.pathologyLabEnabled), end: false, keywords: 'analyte report order' },
      { sep: true as const, show: general?.pharmacyEnabled !== false },
      { to: '/pharmacy', label: 'Pharmacy', group: 'Go to', icon: IconPharmacy, show: general?.pharmacyEnabled !== false, end: false, keywords: 'counter sale bill' },
      { to: '/medicines', label: 'Medicines', group: 'Go to', icon: IconMedicines, show: general?.pharmacyEnabled !== false, end: false, keywords: 'product drug' },
      { to: '/inventory', label: 'Inventory', group: 'Go to', icon: IconInventory, show: general?.pharmacyEnabled !== false, end: false, keywords: 'stock batch expiry' },
      { sep: true as const, show: true },
      { to: '/reports', label: 'Reports', group: 'Go to', icon: IconReports, show: true, end: false, keywords: 'gst export excel' },
      { to: '/settings', label: 'Settings', group: 'Go to', icon: IconSettings, show: true, end: false, keywords: 'features doctors clinic' },
    ];
  }, [general]);

  const destinations = useMemo<Destination[]>(
    () =>
      items
        .filter((i): i is Extract<typeof i, { to: string }> => 'to' in i && i.show)
        .map((i) => ({ label: i.label, to: i.to, group: i.group, keywords: i.keywords })),
    [items],
  );

  const paletteActions = useMemo<PaletteAction[]>(
    () => [
      { label: effectiveTheme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme', group: 'Actions', run: toggleTheme, combo: 'mod+j' },
      { label: density === 'dense' ? 'Use comfortable rows' : 'Use dense rows', group: 'Actions', run: toggleDensity, combo: 'mod+shift+d' },
      { label: 'Change password', group: 'Actions', run: () => navigate('/change-password') },
      { label: 'Keyboard shortcuts', group: 'Actions', run: () => setSheetOpen(true), combo: '?' },
      { label: 'Sign out', group: 'Actions', run: onLogout },
    ],
    [effectiveTheme, density, toggleTheme, toggleDensity, navigate, onLogout],
  );

  const openPalette = useCallback(() => setPaletteOpen(true), []);
  const showSheet = useCallback(() => setSheetOpen(true), []);
  const closeAll = useCallback(() => {
    setPaletteOpen(false);
    setSheetOpen(false);
  }, []);

  // The palette has to answer even from inside a field — that is the whole
  // point of it — so it is one of the two bindings marked whileTyping.
  useHotkey('mod+k', 'Search and jump', 'Anywhere', openPalette, { whileTyping: true });
  useHotkey('?', 'Keyboard shortcuts', 'Anywhere', showSheet);
  useHotkey('escape', 'Close this', 'Anywhere', closeAll, { whileTyping: true });
  useHotkey('mod+j', 'Light or dark theme', 'Anywhere', toggleTheme, { whileTyping: true });
  useHotkey('mod+shift+d', 'Dense or comfortable rows', 'Anywhere', toggleDensity, { whileTyping: true });

  const initial = (session?.clinicName ?? 'Sivayaan').trim().charAt(0).toUpperCase();

  return (
    <div className="shell">
      <aside className="shell-nav">
        {/* Which clinic this session belongs to. A person who works at two
            of them has two usernames and two sessions, and needs to be able
            to tell at a glance which one they are looking at. */}
        <div className="shell-brand" title={session?.clinicName ?? 'Sivayaan HMS'}>{initial}</div>

        <nav>
          {items.map((item, i) => {
            if (!item.show) return null;
            if ('sep' in item) return <div className="nav-sep" key={`sep-${i}`} />;
            const Icon = item.icon;
            return (
              <NavLink to={item.to} end={item.end} key={item.to} aria-label={item.label}>
                <Icon />
                <span className="nav-label">{item.label}</span>
              </NavLink>
            );
          })}
        </nav>

        <div className="shell-user">
          <span>{session?.username}</span>
          {/* Somewhere to change your password without needing to be locked
              out and phone support first. */}
          <NavLink to="/change-password" className="shell-user-link" aria-label="Change password">
            <IconKey />
            <span className="nav-label">Change password</span>
          </NavLink>
          <button type="button" onClick={onLogout} aria-label="Sign out">
            <IconSignOut />
            <span className="nav-label">Sign out</span>
          </button>
        </div>
      </aside>

      <div className="shell-content">
        <header className="topbar">
          <div className="topbar-clinic">
            {session?.clinicName ?? 'Sivayaan HMS'}
            <small>{session?.username}</small>
          </div>

          {/* Deliberately a button, not a text field: it opens the palette.
              An input here would invite typing that goes nowhere. */}
          <button type="button" className="omnibox" onClick={openPalette}>
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
              <IconSearch />
              Search patients, or jump to a screen
            </span>
            <span className="kbd">{describeCombo('mod+k')}</span>
          </button>

          <div className="topbar-right">
            <button type="button" className="icon-btn" onClick={toggleDensity}
                    title={density === 'dense' ? 'Comfortable rows' : 'Dense rows'} aria-label="Row density">
              <IconDensity />
            </button>
            <button type="button" className="icon-btn" onClick={toggleTheme}
                    title={effectiveTheme === 'dark' ? 'Light theme' : 'Dark theme'} aria-label="Theme">
              {effectiveTheme === 'dark' ? <IconSun /> : <IconMoon />}
            </button>
            <button type="button" className="icon-btn" onClick={showSheet}
                    title="Keyboard shortcuts" aria-label="Keyboard shortcuts">
              <IconHelp />
            </button>
          </div>
        </header>

        <main className="shell-content">
          <Outlet />
        </main>
      </div>

      <CommandPalette
        open={paletteOpen}
        onClose={() => setPaletteOpen(false)}
        destinations={destinations}
        actions={paletteActions}
      />

      {sheetOpen && <ShortcutSheet onClose={() => setSheetOpen(false)} />}
    </div>
  );
}
