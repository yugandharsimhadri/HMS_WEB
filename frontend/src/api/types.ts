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

export type DrugSchedule = 'None' | 'H' | 'H1' | 'X';

export interface Product {
  id: string;
  name: string;
  genericName: string | null;
  manufacturer: string | null;
  packSize: string | null;
  unitsPerPack: number;
  allowLooseSale: boolean;
  gstRate: number;
  hsnCode: string;
  schedule: DrugSchedule;
  isActive: boolean;
  stockOnHand: number;
}

export interface Batch {
  id: string;
  productId: string;
  batchNo: string;
  expiryDate: string;
  mrp: number;
  unitsPerPack: number;
  qtyOnHand: number;
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
  fee: number;
  feePaid: boolean;
  feeReceiptNo: string | null;
  feePaymentMode: PaymentMode | null;
}
