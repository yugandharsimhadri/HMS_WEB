import type { ClinicProfile, ClinicSession } from '../api/types';

/** "10:00:00" (how a TimeSpan serialises) or "10:00" → minutes since midnight. */
function toMinutes(time: string): number {
  const [h, m] = time.split(':');
  return Number(h) * 60 + Number(m);
}

function windowFor(clinic: ClinicProfile, session: ClinicSession): [number, number] | null {
  if (session === 'Morning') return [toMinutes(clinic.morningFrom), toMinutes(clinic.morningTo)];
  if (session === 'Evening') return [toMinutes(clinic.eveningFrom), toMinutes(clinic.eveningTo)];
  return null; // Full day covers everything.
}

/**
 * Whether a visit at this time belongs to the chosen sitting. The end is
 * exclusive, so a morning ending at 13:00 does not also claim the one
 * o'clock patient — same rule as the desktop's ClinicProfile.IsIn.
 */
export function isInSession(clinic: ClinicProfile, session: ClinicSession, scheduledOn: string): boolean {
  const w = windowFor(clinic, session);
  if (!w) return true;

  const at = new Date(scheduledOn);
  const minutes = at.getHours() * 60 + at.getMinutes();
  return minutes >= w[0] && minutes < w[1];
}

/** "10:00 to 13:00", for the line under the OPD heading. */
export function describeSession(clinic: ClinicProfile, session: ClinicSession): string {
  const w = windowFor(clinic, session);
  if (!w) return 'the whole day';

  const hhmm = (mins: number) =>
    `${String(Math.floor(mins / 60)).padStart(2, '0')}:${String(mins % 60).padStart(2, '0')}`;

  return `${hhmm(w[0])} to ${hhmm(w[1])}`;
}

export const SESSIONS: { id: ClinicSession; label: string }[] = [
  { id: 'FullDay', label: 'Full day' },
  { id: 'Morning', label: 'Morning' },
  { id: 'Evening', label: 'Evening' },
];
