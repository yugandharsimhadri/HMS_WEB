// Mirrors the shapes SivayaanHMS.Api actually returns (JsonStringEnumConverter
// is registered server-side, so enums arrive as strings like "Male", not 1).

export interface LoginResponse {
  token: string;
  username: string;
  role: string;
  mustChangePassword: boolean;
}

export interface RegisterTenantResponse {
  tenantId: string;
  slug: string;
  adminUsername: string;
}

export type Gender = 'Male' | 'Female' | 'Other';

export interface Patient {
  id: string;
  patientNo: string;
  name: string;
  phone: string;
  gender: Gender;
  age: number;
  dateOfBirth: string | null;
  bloodGroup: string | null;
  guardianName: string | null;
  address: string | null;
  allergies: string | null;
  display: string;
}

export type VisitStatus = 'Booked' | 'Waiting' | 'InConsultation' | 'Completed' | 'Cancelled';
export type PaymentMode = 'Cash' | 'Upi' | 'Card';

export interface Doctor {
  id: string;
  name: string;
  speciality: string | null;
  registrationNo: string | null;
  consultationFee: number;
  isActive: boolean;
}

export interface ClinicProfile {
  name: string;
  addressLine: string;
  addressLine2: string;
  phone: string;
  gstRegistered: boolean;
  gstin: string;
  footerText: string;
  morningFrom: string;
  morningTo: string;
  eveningFrom: string;
  eveningTo: string;
}

export interface PharmacyProfile {
  name: string;
  addressLine: string;
  addressLine2: string;
  phone: string;
  gstRegistered: boolean;
  gstin: string;
  drugLicenceNo: string;
  pharmacistName: string;
  footerText: string;
}

export interface DocumentTheme {
  footer: string;
  logoBase64: string | null;
  logoContentType: string | null;
  printFontFamily: string | null;
  printFontSizeDelta: number;
  titleFontFamily: string | null;
  titleFontSizeDelta: number;
}

export interface GeneralSettings {
  queueLayout: 'Tiles' | 'Rows';
  theme: 'Light' | 'Dark';
  diagnosticsEnabled: boolean;
  opdEnabled: boolean;
  pharmacyEnabled: boolean;
  appointmentsEnabled: boolean;
  pediatricsEnabled: boolean;
  dentistEnabled: boolean;
  pathologyLabEnabled: boolean;
  requireLogin: boolean;
}

export type DrugSchedule = 'None' | 'H' | 'H1' | 'X';

/** Exactly Core's DispensingUnit. A children's clinic sells more than
 * tablets — syrup by the bottle, moisturiser and medicated soap over the
 * counter — which is why the list runs past the obvious four. */
export type DispensingUnit =
  | 'Tablet' | 'Capsule' | 'Bottle' | 'Sachet' | 'Tube' | 'Vial'
  | 'Piece' | 'Syrup' | 'Moisturizer' | 'Soap' | 'Others';

export interface Batch {
  id: string;
  productId: string;
  batchNo: string;
  expiryDate: string;
  mrp: number;
  purchaseRate: number;
  unitsPerPack: number;
  qtyOnHand: number;
  receivedOn: string;
  isProvisional: boolean;
  isDeleted: boolean;
}

export interface Product {
  id: string;
  name: string;
  genericName: string | null;
  manufacturer: string | null;
  composition: string | null;
  storage: string | null;
  packSize: string | null;
  unitsPerPack: number;
  allowLooseSale: boolean;
  dispensingUnit: DispensingUnit;
  gstRate: number;
  hsnCode: string;
  schedule: DrugSchedule;
  rackLocation: string | null;
  reorderLevel: number;
  isActive: boolean;
  stockOnHand: number;
  /** Included by the products endpoint — the counter reads the batch that
   * would actually be dispensed (nearest expiry) to show a unit price. */
  batches: Batch[];
}

/** What one unit costs from the batch that would actually be dispensed —
 * the desktop's SaleViewModel.UnitPriceOf. */
export function nextBatchMrp(product: Product): number {
  const next = (product.batches ?? [])
    .filter((b) => !b.isDeleted && b.qtyOnHand > 0)
    .sort((a, b) => a.expiryDate.localeCompare(b.expiryDate))[0];
  return next?.mrp ?? 0;
}

/** The price this medicine was last received at — what Quick stock
 * pre-fills, because it is nearly always right. */
export function lastReceivedMrp(product: Product): number | null {
  const latest = (product.batches ?? [])
    .filter((b) => !b.isDeleted)
    .sort((a, b) => b.receivedOn.localeCompare(a.receivedOn))[0];
  return latest?.mrp ?? null;
}

export interface CartLine {
  productId: string;
  batchId: string;
  productName: string;
  batchNo: string;
  expiryDate: string;
  quantity: number;
  unitsPerPack: number;
  packLabel: string | null;
  mrp: number;
  discountPercent: number;
  gstRate: number;
  schedule: DrugSchedule;
}

export interface Sale {
  id: string;
  billNo: string;
  billDate: string;
  customerName: string;
  doctorName: string | null;
  paymentMode: PaymentMode;
  isTaxInvoice: boolean;
  grossAmount: number;
  discountAmount: number;
  taxableAmount: number;
  cgstAmount: number;
  sgstAmount: number;
  roundOff: number;
  netAmount: number;
}

export interface PrescriptionItem {
  id: string;
  productId: string | null;
  medicineName: string;
  dosage: string | null;
  frequency: string | null;
  days: number;
  quantity: number;
  instructions: string | null;
}

export interface VisitDiagnosticRequest {
  id: string;
  testId: string | null;
  testName: string;
}

export interface Visit {
  id: string;
  visitNo: string;
  tokenNo: number;
  scheduledOn: string;
  status: VisitStatus;
  patientId: string;
  patient: Patient;
  doctorId: string;
  doctor: Doctor;
  complaint: string | null;
  diagnosis: string | null;
  notes: string | null;
  weightKg: number | null;
  bloodPressure: string | null;
  temperatureF: number | null;
  heightCm: number | null;
  heartRateBpm: number | null;
  spo2Percent: number | null;
  fee: number;
  feePaid: boolean;
  feeReceiptNo: string | null;
  feePaymentMode: PaymentMode | null;
  feeTransactionNo: string | null;
  followUpOn: string | null;
  prescription: PrescriptionItem[];
  diagnosticRequests: VisitDiagnosticRequest[];
}

/** Which sitting the OPD screen is showing. Mirrors the desktop's
 * ClinicSession — Full day hides nobody, which matters because an
 * afternoon walk-in belongs to neither sitting. */
export type ClinicSession = 'FullDay' | 'Morning' | 'Evening';
