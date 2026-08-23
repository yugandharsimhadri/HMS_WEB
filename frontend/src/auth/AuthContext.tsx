import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { api } from '../api/client';
import { getToken, setToken as persistToken } from '../api/client';
import type { LoginResponse } from '../api/types';

export interface Session {
  username: string;
  role: string;
  mustChangePassword: boolean;
  /** Shown in the shell so a person working across two clinics can see at a
   * glance which one they are signed into. */
  clinicName: string;
}

/**
 * The support identity, which belongs to no clinic.
 *
 * This constant only decides which screen the browser shows. It grants
 * nothing: a support token carries no tenant claim, so the API's default
 * authorization policy turns it away from every clinic route regardless of
 * what the browser believes. Editing this value in devtools changes the
 * menu, not the data.
 */
export const PLATFORM_ADMIN_ROLE = 'EnterpriseAdmin';

const SESSION_KEY = 'sivayaanhms.session';

function loadSession(): Session | null {
  const raw = localStorage.getItem(SESSION_KEY);
  return raw ? (JSON.parse(raw) as Session) : null;
}

interface AuthContextValue {
  session: Session | null;
  isAuthenticated: boolean;
  /** True when signed in as platform support rather than as a clinic's user.
   * Drives routing only — see PLATFORM_ADMIN_ROLE. */
  isPlatformAdmin: boolean;
  /** Username carries its own clinic ("reception@twinkle"), so there is no
   * separate clinic argument — it is asked for at registration only.
   * Returns the new session so the caller can route on it without waiting
   * for a re-render. */
  login: (username: string, password: string) => Promise<Session>;
  /** Clears the must-change flag after the user has actually chosen a new
   * password, so the forced-change gate lets them through. The server has
   * already cleared its own copy; this keeps the browser in step without a
   * round trip. */
  passwordChanged: () => void;
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
    return next;
  };

  const passwordChanged = () => {
    setSession((current) => {
      if (!current) return current;
      const next = { ...current, mustChangePassword: false };
      localStorage.setItem(SESSION_KEY, JSON.stringify(next));
      return next;
    });
  };

  const logout = () => {
    persistToken(null);
    localStorage.removeItem(SESSION_KEY);
    setSession(null);
  };

  const value = useMemo(
    () => ({
      session,
      isAuthenticated: session !== null,
      isPlatformAdmin: session?.role === PLATFORM_ADMIN_ROLE,
      login,
      passwordChanged,
      logout,
    }),
    [session],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
