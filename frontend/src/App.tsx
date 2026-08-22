import { Navigate, Route, BrowserRouter, Routes } from 'react-router-dom';
import type { ReactNode } from 'react';
import { AuthProvider, useAuth } from './auth/AuthContext';
import { AppShell } from './layout/AppShell';
import { LoginPage } from './pages/LoginPage';
import { RegisterPage } from './pages/RegisterPage';
import { PatientsPage } from './pages/PatientsPage';
import { OpdQueuePage } from './pages/OpdQueuePage';
import { PharmacyCounterPage } from './pages/PharmacyCounterPage';
import { SettingsPage } from './pages/SettingsPage';
import { ConsultationPage } from './pages/ConsultationPage';
import { MedicinesPage } from './pages/MedicinesPage';
import { InventoryPage } from './pages/InventoryPage';
import { AppointmentsPage } from './pages/AppointmentsPage';
import { DiagnosticsPage } from './pages/DiagnosticsPage';
import { PediatricsPage } from './pages/PediatricsPage';
import { DentistPage } from './pages/DentistPage';

function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  return isAuthenticated ? <>{children}</> : <Navigate to="/login" replace />;
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route
        path="/"
        element={
          <ProtectedRoute>
            <AppShell />
          </ProtectedRoute>
        }
      >
        <Route index element={<OpdQueuePage />} />
        <Route path="patients" element={<PatientsPage />} />
        <Route path="appointments" element={<AppointmentsPage />} />
        <Route path="diagnostics" element={<DiagnosticsPage />} />
        <Route path="pediatrics" element={<PediatricsPage />} />
        <Route path="dentist" element={<DentistPage />} />
        <Route path="pharmacy" element={<PharmacyCounterPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="consultation/:visitId" element={<ConsultationPage />} />
        <Route path="medicines" element={<MedicinesPage />} />
        <Route path="inventory" element={<InventoryPage />} />
      </Route>
    </Routes>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </BrowserRouter>
  );
}
