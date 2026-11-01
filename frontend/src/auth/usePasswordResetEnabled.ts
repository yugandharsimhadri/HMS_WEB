import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { AuthOptionsResponse } from '../api/types';

// One request per page load, shared by every screen that asks. Null when the
// API could not be asked — callers then behave exactly as before these
// options existed.
let pending: Promise<AuthOptionsResponse | null> | null = null;

function load(): Promise<AuthOptionsResponse | null> {
  pending ??= api.get<AuthOptionsResponse>('/api/auth/options').catch(() => null);
  return pending;
}

/**
 * GET /api/auth/options: PasswordReset:Enabled and SivaCom:Enabled on the
 * server. Undefined until known, null if it could not be fetched.
 *
 * Every caller treats "not known" as the original behaviour — the Forgot
 * password link shown, the original reset page — so with SivaCom off nothing
 * on screen waits for, or depends on, this request.
 */
export function useAuthOptions(): AuthOptionsResponse | null | undefined {
  const [options, setOptions] = useState<AuthOptionsResponse | null | undefined>(undefined);

  useEffect(() => {
    let live = true;
    load().then((o) => {
      if (live) setOptions(o);
    });
    return () => {
      live = false;
    };
  }, []);

  return options;
}

/** False only when the server explicitly says self-service reset is off. */
export function usePasswordResetEnabled(): boolean {
  return useAuthOptions()?.passwordResetEnabled !== false;
}
