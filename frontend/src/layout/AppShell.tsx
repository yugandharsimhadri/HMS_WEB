import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export function AppShell() {
  const { session, logout } = useAuth();
  const navigate = useNavigate();

  const onLogout = () => {
    logout();
    navigate('/login');
  };

  return (
    <div className="shell">
      <aside className="shell-nav">
        <div className="shell-brand">Sivayaan HMS</div>
        <nav>
          <NavLink to="/" end>
            OPD
          </NavLink>
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
