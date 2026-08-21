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
