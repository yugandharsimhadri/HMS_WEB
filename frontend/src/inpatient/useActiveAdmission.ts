import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { ActiveAdmission } from '../api/types';
import { useGeneralSettings } from '../settings/SettingsContext';

/**
 * The patient's current admission, when a counter may charge a bill to it: the
 * In-patients module and "counters can charge to the IP bill" are both on, and the
 * patient is in. Null otherwise, so the counter shows nothing new.
 */
export function useActiveAdmission(patientId: string | null | undefined): ActiveAdmission | null {
  const general = useGeneralSettings();
  const enabled = Boolean(general?.inpatientEnabled && general?.ipLinkedBilling);

  // Kept with the patient it was fetched for, so a changed patient reads as "not known
  // yet" straight away rather than showing the previous patient's admission.
  const [found, setFound] = useState<{ patientId: string; admission: ActiveAdmission | null } | null>(null);

  useEffect(() => {
    if (!enabled || !patientId) return;

    let stale = false;
    api.get<ActiveAdmission | undefined>(`/api/inpatient/active?patientId=${patientId}`)
      .then((a) => { if (!stale) setFound({ patientId, admission: a ?? null }); })
      .catch(() => { if (!stale) setFound({ patientId, admission: null }); });
    return () => { stale = true; };
  }, [enabled, patientId]);

  return enabled && patientId && found?.patientId === patientId ? found.admission : null;
}
