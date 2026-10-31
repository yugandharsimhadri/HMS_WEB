import type { DischargeType, IpChargeCategory, IpPaymentKind } from '../api/types';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const two = (n: number) => String(n).padStart(2, '0');

/** ₹1,23,456.00 - the Indian grouping the bills print. */
export const money = (n: number) =>
  `₹${n.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

/** 31 Oct 2026, 11:40 */
export const dateTime = (iso: string | null | undefined) => {
  if (!iso) return '';
  const d = new Date(iso);
  return `${two(d.getDate())} ${MONTHS[d.getMonth()]} ${d.getFullYear()}, ${two(d.getHours())}:${two(d.getMinutes())}`;
};

/** 31 Oct */
export const shortDate = (iso: string | null | undefined) => {
  if (!iso) return '';
  const d = new Date(iso);
  return `${two(d.getDate())} ${MONTHS[d.getMonth()]}`;
};

/** The value a datetime-local input wants: 2026-10-31T11:40, in the clinic's own time. */
export const toLocalInput = (d: Date) =>
  `${d.getFullYear()}-${two(d.getMonth() + 1)}-${two(d.getDate())}T${two(d.getHours())}:${two(d.getMinutes())}`;

export const nowInput = () => toLocalInput(new Date());

/** The value a date input wants: 2026-10-31. */
export const todayInput = () => {
  const d = new Date();
  return `${d.getFullYear()}-${two(d.getMonth() + 1)}-${two(d.getDate())}`;
};

/** Whole days since admission, counting the admission day as day 1. */
export const dayOfStay = (admittedOn: string, until?: string | null) => {
  const start = new Date(admittedOn);
  const end = until ? new Date(until) : new Date();
  const days = Math.floor((new Date(end.toDateString()).getTime() - new Date(start.toDateString()).getTime()) / 86_400_000);
  return Math.max(1, days + 1);
};

export const CATEGORY_LABEL: Record<IpChargeCategory, string> = {
  Room: 'Room charges',
  Nursing: 'Nursing',
  DoctorVisit: 'Doctor visits',
  Procedure: 'Procedures',
  Investigation: 'Investigations',
  Medicine: 'Medicines & consumables',
  Other: 'Other',
};

/** The categories a person can add a charge under - room and nursing come from the bed. */
export const MANUAL_CATEGORIES: IpChargeCategory[] = ['DoctorVisit', 'Procedure', 'Investigation', 'Medicine', 'Other'];

export const PAYMENT_KIND_LABEL: Record<IpPaymentKind, string> = {
  Advance: 'Advance',
  Settlement: 'Settlement',
  Refund: 'Refund',
  Insurance: 'Insurance',
};

export const DISCHARGE_LABEL: Record<DischargeType, string> = {
  Normal: 'Normal',
  Dama: 'DAMA (against medical advice)',
  Lama: 'LAMA (left against advice)',
  Referred: 'Referred elsewhere',
  Death: 'Death',
};
