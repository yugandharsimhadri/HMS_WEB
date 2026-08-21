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
    void api.get<GeneralSettings>('/api/settings/general').then(setGeneral).catch(() => {});
  }, []);

  const onLogout = () => {
    logout();
    navigate('/login');
  };

  return (
    <div className="shell">
      <aside className="shell-nav">
        <div className="shell-brand">Sivayaan HMS</div>
        <nav>
          {general?.opdEnabled !== false && (
            <NavLink to="/" end>
              OPD Queue
            </NavLink>
          )}
          <NavLink to="/patients">Patients</NavLink>
          {general?.pharmacyEnabled !== false && <NavLink to="/pharmacy">Pharmacy</NavLink>}
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
