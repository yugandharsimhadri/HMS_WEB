import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { AuthOptionsResponse } from '../api/types';

// One request per page load, shared by every screen that asks.
let pending: Promise<boolean> | null = null;

function load(): Promise<boolean> {
  pending ??= api
    .get<AuthOptionsResponse>('/api/auth/options')
    .then((o) => o.passwordResetEnabled)
    // Unreachable API: offer the link anyway. The reset page reports the
    // outage itself, which says more than a link that silently vanished.
    .catch(() => true);
  return pending;
}

/**
 * Whether this deployment offers "Forgot password" (PasswordReset:Enabled on
 * the server). Undefined until known, so the link appears once rather than
 * flashing up and disappearing.
 */
export function usePasswordResetEnabled(): boolean | undefined {
  const [enabled, setEnabled] = useState<boolean | undefined>(undefined);

  useEffect(() => {
    let live = true;
    load().then((v) => {
      if (live) setEnabled(v);
    });
    return () => {
      live = false;
    };
  }, []);

  return enabled;
}
