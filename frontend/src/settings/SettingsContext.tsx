import {
  createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode,
} from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { GeneralSettings } from '../api/types';

/**
 * The clinic's module switches, fetched once per session instead of once per
 * screen.
 *
 * Seven components each loaded `/api/settings/general` on mount — the shell
 * and six pages — so opening Patients issued the same request twice at the
 * same moment, and every navigation issued it again. It is a handful of
 * booleans that change when an admin edits Features, which is a few times a
 * year, and it decides what the navigation rail shows; refetching it on every
 * mount bought nothing and cost a round trip each time. Over a tunnel to a
 * clinic's own server that round trip is visible.
 *
 * Kept as a plain context rather than a caching library: this is the only
 * piece of genuinely global, slow-moving state in the application, and one
 * provider is a smaller thing to own than a dependency.
 */
interface SettingsContextValue {
  /** Null until the first load finishes, and after a failure. Consumers
   *  already treat null as "assume the default", which is what the module
   *  flags' `!== false` checks do. */
  general: GeneralSettings | null;
  /** True while the first load is in flight, so a caller can tell "not
   *  loaded yet" from "loaded, and this module really is off". */
  loading: boolean;
  /** Set when the load failed, so a screen can say so instead of silently
   *  rendering as though every optional module were switched off. */
  error: string | null;
  /** Re-read from the server. Called after Features is saved. */
  refresh: () => void;
}

const SettingsContext = createContext<SettingsContextValue | null>(null);

/** Fired by the Features tab after a save. Kept as a window event because the
 *  emitter and this provider have no other relationship, and inverting that
 *  would mean threading a callback through the whole settings screen. */
export const SETTINGS_CHANGED_EVENT = 'sivayaanhms:general-settings-changed';

export function SettingsProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  const [general, setGeneral] = useState<GeneralSettings | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setGeneral(await api.get<GeneralSettings>('/api/settings/general'));
      setError(null);
    } catch {
      // Deliberately keeps whatever was loaded before rather than blanking
      // the nav: a failed refresh should not make modules disappear from
      // under someone mid-shift.
      setError('Could not read the clinic settings.');
    } finally {
      setLoading(false);
    }
  }, []);

  // Only once signed in. Asking before that guarantees a 401, which the API
  // client now treats as an expired session and redirects on - so an
  // unauthenticated fetch here would bounce the login screen to itself.
  useEffect(() => {
    if (!isAuthenticated) {
      setGeneral(null);
      return;
    }
    void load();
  }, [isAuthenticated, load]);

  useEffect(() => {
    const onChanged = () => void load();
    window.addEventListener(SETTINGS_CHANGED_EVENT, onChanged);
    return () => window.removeEventListener(SETTINGS_CHANGED_EVENT, onChanged);
  }, [load]);

  const value = useMemo(
    () => ({ general, loading, error, refresh: () => void load() }),
    [general, loading, error, load],
  );

  return <SettingsContext.Provider value={value}>{children}</SettingsContext.Provider>;
}

export function useSettings(): SettingsContextValue {
  const ctx = useContext(SettingsContext);
  if (!ctx) throw new Error('useSettings must be used within SettingsProvider');
  return ctx;
}

/** The common case: just the flags, for a screen that only wants to know
 *  which modules are on. */
export function useGeneralSettings(): GeneralSettings | null {
  return useSettings().general;
}
