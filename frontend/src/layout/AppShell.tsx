import { useEffect, useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { api } from '../api/client';
import type { GeneralSettings } from '../api/types';

export function AppShell() {
  const { session, logout } = useAuth();
  const navigate = useNavigate();
  const [general, setGeneral] = useState<GeneralSettings | null>(null);

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

  const onLogout = () => {
    logout();
    navigate('/login');
  };

  return (
    <div className="shell">
      <aside className="shell-nav">
        <div className="shell-brand">
          Sivayaan HMS
          {/* Which clinic this session belongs to. A person who works at two
              of them has two usernames and two sessions, and needs to be able
              to tell at a glance which one they are looking at. */}
          {session?.clinicName && <div className="shell-clinic">{session.clinicName}</div>}
        </div>
        <nav>
          {/* The landing screen: nothing here is a destination in its own
              right, so it sits above the modules rather than among them. */}
          <NavLink to="/dashboard">Dashboard</NavLink>
          {general?.opdEnabled !== false && (
            <NavLink to="/" end>
              OPD Queue
            </NavLink>
          )}
          <NavLink to="/patients">Patients</NavLink>
          {/* Off by default — a clinic that only takes walk-ins never turns
              advance booking on, and an empty screen behind a permanent nav
              item reads as a broken feature. */}
          {general?.appointmentsEnabled && <NavLink to="/appointments">Appointments</NavLink>}
          {general?.diagnosticsEnabled && <NavLink to="/diagnostics">Diagnostics</NavLink>}
          {general?.pediatricsEnabled && <NavLink to="/pediatrics">Pediatrics</NavLink>}
          {general?.dentistEnabled && <NavLink to="/dentist">Dentist</NavLink>}
          {general?.pathologyLabEnabled && <NavLink to="/lab">Pathology Lab</NavLink>}
          {general?.pharmacyEnabled !== false && (
            <>
              <NavLink to="/pharmacy">Pharmacy</NavLink>
              <NavLink to="/medicines">Medicines</NavLink>
              <NavLink to="/inventory">Inventory</NavLink>
            </>
          )}
          <NavLink to="/reports">Reports</NavLink>
          <NavLink to="/settings">Settings</NavLink>
        </nav>
        <div className="shell-user">
          <span>{session?.username}</span>
          <button onClick={onLogout}>Sign out</button>
        </div>
      </aside>
      <main className="shell-content">
        <Outlet />
      </main>
    </div>
  );
}
