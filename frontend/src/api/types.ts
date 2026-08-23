// Mirrors the shapes SivayaanHMS.Api actually returns (JsonStringEnumConverter
// is registered server-side, so enums arrive as strings like "Male", not 1).

export interface LoginResponse {
  token: string;
  username: string;
  role: string;
  mustChangePassword: boolean;
  clinicName: string;
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

// ── Appointments ─────────────────────────────────────────────────────────
// Exactly Core's enums. Checked against Enums.cs rather than guessed — two
// invented values in the last pass compiled fine and failed only at runtime.

export type AppointmentStatus =
  | 'Scheduled' | 'CheckedIn' | 'Cancelled' | 'Rescheduled' | 'NoShow';

/** Which module an appointment was booked against — decides what checking
 * in actually creates. Only General has somewhere to route to today. */
export type AppointmentModuleContext =
  | 'General' | 'Pediatrics' | 'Dentist' | 'PathologyLab';

export type ReminderSourceKind =
  | 'FollowUp' | 'Appointment' | 'VaccineDue' | 'DentalSitting';

export interface Appointment {
  id: string;
  appointmentNo: string;
  patientId: string;
  patientName: string;
  patientPhone: string;
  doctorId: string;
  doctorName: string;
  scheduledOn: string;
  durationMinutes: number;
  moduleContext: AppointmentModuleContext;
  status: AppointmentStatus;
  reason: string | null;
  notes: string | null;
  rescheduledFromId: string | null;
  linkedRecordId: string | null;
  isPending: boolean;
}

/** One row on the shared reminder call list. A reminded row stays on the
 * list carrying isReminded rather than vanishing — the desk needs to see
 * what has been done, not only what is left. */
export interface ReminderItem {
  sourceKind: ReminderSourceKind;
  sourceId: string;
  patientId: string;
  patientName: string;
  patientPhone: string | null;
  dueOn: string;
  description: string;
  isReminded: boolean;
  statusLabel: string;
}

export interface CheckInResult {
  visitId: string;
  tokenNo: number;
  patientName: string;
}

// ── Diagnostics ──────────────────────────────────────────────────────────

/** Exactly Core's DiagnosticBillStatus — the lab workflow, in order. */
export type DiagnosticBillStatus =
  | 'Ordered' | 'SampleCollected' | 'ResultReceived' | 'Completed';

export interface DiagnosticTest {
  id: string;
  name: string;
  category: string;
  price: number;
  active: boolean;
}

export interface DiagnosticBillItem {
  id: string;
  /** Null once the test it was added from has been deleted from the master,
   * and for a test requested as free text — the line still bills on its own
   * name and price either way. */
  testId: string | null;
  testName: string;
  price: number;
  quantity: number;
  amount: number;
}

export interface DiagnosticBill {
  id: string;
  billNo: string;
  billDate: string;
  patientId: string;
  patientName: string;
  patientNo: string;
  totalAmount: number;
  discount: number;
  finalAmount: number;
  paymentMode: PaymentMode;
  transactionNo: string | null;
  status: DiagnosticBillStatus;
  remarks: string | null;
  visitId: string | null;
  referredBy: string | null;
  items: DiagnosticBillItem[];
}

// ── Pediatrics ───────────────────────────────────────────────────────────

export type ProcedureDepartment = 'Pediatrics' | 'Dentist' | 'General';

/** Deliberately simpler than DiagnosticBillStatus — a procedure bill has no
 * lab workflow to move through. */
export type ProcedureBillStatus = 'Ordered' | 'Completed';

export type ImmunizationStatus = 'Given' | 'Overdue' | 'DueSoon' | 'Upcoming';

export interface Procedure {
  id: string;
  name: string;
  category: string;
  department: ProcedureDepartment;
  price: number;
  active: boolean;
}

export interface VaccineMaster {
  id: string;
  name: string;
  doseNumber: number;
  /** Age at which due, in days from birth — "6 weeks" is 42, so every
   * schedule comparison is one integer subtraction. */
  recommendedAgeDays: number;
  category: string;
  sequenceOrder: number;
  active: boolean;
}

export interface VaccinationRecord {
  id: string;
  patientId: string;
  patientName: string;
  vaccineId: string | null;
  vaccineName: string;
  doseNumber: number;
  givenOn: string;
  batchNo: string | null;
  siteOfInjection: string | null;
  administeredBy: string | null;
  productId: string | null;
  productName: string | null;
  manufacturer: string | null;
  batchId: string | null;
  nextDueOn: string | null;
}

export interface ImmunizationCardRow {
  vaccineId: string;
  vaccineName: string;
  doseNumber: number;
  recommendedAgeDays: number;
  /** Null when the patient has no date of birth on file — in which case
   * every not-yet-given row reads Upcoming. */
  recommendedOn: string | null;
  status: ImmunizationStatus;
  given: VaccinationRecord | null;
}

export interface GrowthMeasurement {
  id: string;
  patientId: string;
  measuredOn: string;
  ageDays: number;
  weightKg: number | null;
  heightCm: number | null;
  headCircumferenceCm: number | null;
  /** Computed and stored server-side on save, not recomputed here — weight
   * and height are not always both present on the same visit, so a BMI that
   * exists is a BMI that was genuinely measurable. */
  bmiValue: number | null;
}

export interface ProcedureBillItem {
  id: string;
  procedureId: string | null;
  procedureName: string;
  price: number;
  quantity: number;
  amount: number;
}

export interface ProcedureBill {
  id: string;
  billNo: string;
  billDate: string;
  patientId: string;
  patientName: string;
  patientNo: string;
  totalAmount: number;
  discount: number;
  finalAmount: number;
  paymentMode: PaymentMode;
  transactionNo: string | null;
  status: ProcedureBillStatus;
  visitId: string | null;
  referredBy: string | null;
  items: ProcedureBillItem[];
}

export interface ProcedureBillResult {
  id: string;
  billNo: string;
  finalAmount: number;
  vaccinationsRecorded: number;
  /** Set when the bill saved but a dose did not record — the batch emptied
   * between picking the brand and saving. Not a failed bill, but the desk
   * must see it: the card is now missing a dose the bill charged for. */
  warning: string | null;
}

// ── Dentist ──────────────────────────────────────────────────────────────

export type DentalCaseStatus = 'Planned' | 'InProgress' | 'Completed' | 'Cancelled';

export interface DentalPackageMaster {
  id: string;
  name: string;
  packagePrice: number;
  active: boolean;
}

export interface DentalReplacementMaster {
  id: string;
  name: string;
  unitCost: number;
  active: boolean;
}

export interface AnesthesiaTypeMaster {
  id: string;
  name: string;
  defaultCost: number;
  active: boolean;
}

export interface DentalSitting {
  id: string;
  dentalCaseId: string;
  sittingNumber: number;
  sittingDate: string;
  workDone: string | null;
  anesthesiaTypeId: string | null;
  anesthesiaTypeName: string | null;
  anesthesiaCost: number | null;
  nextSittingOn: string | null;
}

export interface DentalCaseReplacement {
  id: string;
  dentalCaseId: string;
  replacementId: string | null;
  name: string;
  unitCost: number;
  quantity: number;
  amount: number;
}

export interface DentalPayment {
  id: string;
  dentalCaseId: string;
  receiptNo: string;
  paidOn: string;
  amount: number;
  paymentMode: PaymentMode;
  transactionNo: string | null;
}

/** The unit money is tracked against — not a bill. Dentistry runs across
 * several visits and is paid in parts, so total, paid and balance are all
 * derived from the case's own sittings, replacements and payments. */
export interface DentalCase {
  id: string;
  patientId: string;
  patientName: string;
  procedureId: string | null;
  procedureName: string | null;
  packageId: string | null;
  packageName: string | null;
  toothNumber: string | null;
  doctorId: string;
  status: DentalCaseStatus;
  startedOn: string;
  completedOn: string | null;
  notes: string | null;
  /** Snapshotted at opening, so a later master price change never moves the
   * ground under an in-progress case. */
  baseCost: number;
  sittings: DentalSitting[];
  replacements: DentalCaseReplacement[];
  payments: DentalPayment[];
  totalCost: number;
  amountPaid: number;
  balance: number;
}

// ── Pathology Lab ────────────────────────────────────────────────────────

export type LabResultType = 'Numeric' | 'Text' | 'Selection';

/** Richer than a billing status: a result has to be entered and verified
 * before it can print, which is a real pathology-lab norm. */
export type LabOrderStatus =
  | 'Ordered' | 'SampleCollected' | 'ResultEntered' | 'Verified' | 'Completed';

export type LabResultFlag = 'Normal' | 'Low' | 'High' | 'Abnormal';

export interface LabAnalyte {
  id: string;
  name: string;
  category: string;
  units: string;
  resultType: LabResultType;
  decimalPlaces: number;
  sequenceOrder: number;
  active: boolean;
}

export interface LabReport {
  id: string;
  name: string;
  category: string;
  price: number;
  sequenceOrder: number;
  active: boolean;
}

export interface LabPackageMaster {
  id: string;
  name: string;
  packagePrice: number;
  active: boolean;
}

export interface LabResult {
  id: string;
  orderReportId: string;
  analyteId: string | null;
  analyteName: string;
  units: string;
  resultValue: string;
  /** Matched server-side against this patient's gender and age — never
   * chosen by the client. */
  referenceRangeDisplay: string;
  flag: LabResultFlag;
  enteredOn: string | null;
  enteredBy: string | null;
  verifiedOn: string | null;
  verifiedBy: string | null;
}

export interface LabOrderReport {
  id: string;
  orderId: string;
  reportId: string | null;
  reportName: string;
  price: number;
  amount: number;
  notes: string | null;
  results: LabResult[];
}

export interface LabOrder {
  id: string;
  orderNo: string;
  orderDate: string;
  patientId: string;
  patientName: string;
  patientNo: string;
  packageId: string | null;
  visitId: string | null;
  referredBy: string | null;
  specimenId: string | null;
  collectedOn: string | null;
  receivedOn: string | null;
  totalAmount: number;
  discount: number;
  finalAmount: number;
  paymentMode: PaymentMode;
  transactionNo: string | null;
  status: LabOrderStatus;
  remarks: string | null;
  reports: LabOrderReport[];
}

// ── Dashboard ────────────────────────────────────────────────────────────

export interface DashboardTrendDay {
  day: string;
  opd: number;
  pharmacy: number;
  diagnostics: number;
}

export interface DashboardLowStockRow {
  productId: string;
  name: string;
  stockOnHand: number;
  reorderLevel: number;
}

export interface DashboardActivityRow {
  when: string;
  billNo: string;
  patientName: string;
  department: string;
  amount: number;
}

/** One payload, not several: the KPI tile and the donut only reliably agree
 * when they come out of the same arithmetic over the same snapshot. */
export interface DashboardResponse {
  patientsToday: number;
  patientsDeltaPercent: number;
  inQueueNow: number;
  revenueToday: number;
  revenueDeltaPercent: number;
  opdRevenueToday: number;
  pharmacyRevenueToday: number;
  diagnosticsRevenueToday: number;
  revenueTrendTotal: number;
  lowStockCount: number;
  diagnosticsEnabled: boolean;
  pediatricsEnabled: boolean;
  dentistEnabled: boolean;
  pathologyLabEnabled: boolean;
  trend: DashboardTrendDay[];
  lowStock: DashboardLowStockRow[];
  recentActivity: DashboardActivityRow[];
}

// ── Reports ──────────────────────────────────────────────────────────────

export type ReportKind =
  | 'None' | 'DayBook' | 'GstSummary' | 'OpdRegister' | 'ExpiringSoon'
  | 'LowStock' | 'StockRegister' | 'ScheduleH1';

export type ReportAlign = 'Left' | 'Right' | 'Center';
export type ReportFormat =
  | 'Text' | 'Money' | 'Number' | 'Integer' | 'Date' | 'Time' | 'DateTime';

export interface ReportColumn {
  header: string;
  align: ReportAlign;
  format: ReportFormat;
  width: number;
}

export interface ReportRow {
  cells: (string | number | null)[];
  emphasise: boolean;
  /** Why the row is emphasised, in words — so the reason does not depend on
   * seeing a colour. */
  note: string | null;
  /** The record behind the row when there is one worth acting on — a bill to
   * reprint. Null for a row that is only a figure. */
  id: string | null;
}

export interface DiagnosticsReportBill {
  id: string;
  billNo: string;
  billDate: string;
  patientName: string;
  patientNo: string;
  finalAmount: number;
  status: string;
}

export interface DiagnosticsReport {
  todayTotal: number;
  todaysBills: DiagnosticsReportBill[];
  revenue: { day: string; bills: number; amount: number }[];
  topTests: { test: string; times: number; amount: number }[];
}

export interface ReportTotal {
  label: string;
  value: string | number | null;
  format: ReportFormat;
}

/** One shape for the screen, the PDF and the workbook, so the three cannot
 * disagree about what the report says. */
export interface ReportTable {
  kind: ReportKind;
  title: string;
  dateLabel: string;
  columns: ReportColumn[];
  rows: ReportRow[];
  totals: ReportTotal[];
}

export interface DayBookSummary {
  totalCollected: number;
  cashTotal: number;
  upiTotal: number;
  taxableTotal: number;
  cgstTotal: number;
  sgstTotal: number;
  netTotal: number;
  consultationTotal: number;
  visitCount: number;
}
