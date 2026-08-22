import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { api } from '../api/client';
import { getToken, setToken as persistToken } from '../api/client';
import type { LoginResponse } from '../api/types';

interface Session {
  username: string;
  role: string;
  mustChangePassword: boolean;
  /** Shown in the shell so a person working across two clinics can see at a
   * glance which one they are signed into. */
  clinicName: string;
}

const SESSION_KEY = 'sivayaanhms.session';

function loadSession(): Session | null {
  const raw = localStorage.getItem(SESSION_KEY);
  return raw ? (JSON.parse(raw) as Session) : null;
}

interface AuthContextValue {
  session: Session | null;
  isAuthenticated: boolean;
  /** Username carries its own clinic ("reception@twinkle"), so there is no
   * separate clinic argument — it is asked for at registration only. */
  login: (username: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(() => (getToken() ? loadSession() : null));

  const login = async (username: string, password: string) => {
    const result = await api.post<LoginResponse>('/api/auth/login', { username, password });
    persistToken(result.token);
    const next: Session = {
      username: result.username,
      role: result.role,
      mustChangePassword: result.mustChangePassword,
      clinicName: result.clinicName,
    };
    localStorage.setItem(SESSION_KEY, JSON.stringify(next));
    setSession(next);
  };

  const logout = () => {
    persistToken(null);
    localStorage.removeItem(SESSION_KEY);
    setSession(null);
  };

  const value = useMemo(
    () => ({ session, isAuthenticated: session !== null, login, logout }),
    [session],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
