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
import { PathologyLabPage } from './pages/PathologyLabPage';
import { DashboardPage } from './pages/DashboardPage';
import { ReportsPage } from './pages/ReportsPage';
import { PlatformConsolePage } from './pages/PlatformConsolePage';
import { ChangePasswordPage } from './pages/ChangePasswordPage';
import { MastersPage } from './pages/MastersPage';

/**
 * The clinic application. A support session is bounced to its own console
 * rather than shown an empty shell — its token carries no clinic, so every
 * screen behind here would load nothing and report errors nobody can act on.
 */
function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, isPlatformAdmin, session } = useAuth();
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  if (isPlatformAdmin) return <Navigate to="/platform" replace />;

  // Wraps the whole clinic shell, so typing a URL does not get round it.
  // Someone holding a support-issued temporary password has a password their
  // support agent also knows; nothing else in the application should happen
  // until that stops being true.
  if (session?.mustChangePassword) return <Navigate to="/change-password" replace />;

  return <>{children}</>;
}

/**
 * Signed in as a clinic user, and that is all this screen needs. Platform
 * support has no clinic password to change, so it goes back to its console.
 */
function ChangePasswordGate({ children }: { children: ReactNode }) {
  const { isAuthenticated, isPlatformAdmin } = useAuth();
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  if (isPlatformAdmin) return <Navigate to="/platform" replace />;
  return <>{children}</>;
}

/**
 * The support console. Guarded both ways — a clinic's own users have no
 * business here either, and land back in their clinic.
 *
 * Neither guard is a security boundary. Both exist to show people the screen
 * that will actually work; the API decides what is permitted from the
 * token's own claims, on every request.
 */
function PlatformRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, isPlatformAdmin } = useAuth();
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  if (!isPlatformAdmin) return <Navigate to="/" replace />;
  return <>{children}</>;
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      {/* Outside ProtectedRoute deliberately — it is the one clinic screen a
          must-change-password session is allowed to reach, and putting it
          inside would bounce it to itself forever. */}
      <Route
        path="/change-password"
        element={
          <ChangePasswordGate>
            <ChangePasswordPage />
          </ChangePasswordGate>
        }
      />
      <Route
        path="/platform"
        element={
          <PlatformRoute>
            <PlatformConsolePage />
          </PlatformRoute>
        }
      />
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
        <Route path="lab" element={<PathologyLabPage />} />
        <Route path="dashboard" element={<DashboardPage />} />
        <Route path="reports" element={<ReportsPage />} />
        <Route path="pharmacy" element={<PharmacyCounterPage />} />
        <Route path="masters" element={<MastersPage />} />
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
