import { Navigate, Route, BrowserRouter, Routes, Link } from 'react-router-dom';
import { Suspense, lazy, type ReactNode } from 'react';
import { AuthProvider, useAuth } from './auth/AuthContext';
import { SettingsProvider } from './settings/SettingsContext';
import { AppShell } from './layout/AppShell';
import { LoginPage } from './pages/LoginPage';

/**
 * Everything past the front door is split out of the entry bundle.
 *
 * Loading all twenty screens eagerly meant a receptionist downloaded the
 * pathology lab, the dentist module, the growth charts and the reports engine
 * before the username box could be typed into — one 528 kB chunk for a login
 * form. Splitting at the route boundary is where the seam already is: nobody
 * is on two screens at once.
 *
 * The login screen and the shell stay eager on purpose. They are the first
 * thing every session renders, so deferring them would only add a round trip
 * before anything at all appeared.
 */
const RegisterPage = lazy(() => import('./pages/RegisterPage').then((m) => ({ default: m.RegisterPage })));
const ForgotPasswordPage = lazy(() => import('./pages/ForgotPasswordPage').then((m) => ({ default: m.ForgotPasswordPage })));
const PatientsPage = lazy(() => import('./pages/PatientsPage').then((m) => ({ default: m.PatientsPage })));
const OpdQueuePage = lazy(() => import('./pages/OpdQueuePage').then((m) => ({ default: m.OpdQueuePage })));
const PharmacyCounterPage = lazy(() => import('./pages/PharmacyCounterPage').then((m) => ({ default: m.PharmacyCounterPage })));
const SettingsPage = lazy(() => import('./pages/SettingsPage').then((m) => ({ default: m.SettingsPage })));
const ConsultationPage = lazy(() => import('./pages/ConsultationPage').then((m) => ({ default: m.ConsultationPage })));
const MedicinesPage = lazy(() => import('./pages/MedicinesPage').then((m) => ({ default: m.MedicinesPage })));
const InventoryPage = lazy(() => import('./pages/InventoryPage').then((m) => ({ default: m.InventoryPage })));
const AppointmentsPage = lazy(() => import('./pages/AppointmentsPage').then((m) => ({ default: m.AppointmentsPage })));
const DiagnosticsPage = lazy(() => import('./pages/DiagnosticsPage').then((m) => ({ default: m.DiagnosticsPage })));
const PediatricsPage = lazy(() => import('./pages/PediatricsPage').then((m) => ({ default: m.PediatricsPage })));
const DentistPage = lazy(() => import('./pages/DentistPage').then((m) => ({ default: m.DentistPage })));
const PathologyLabPage = lazy(() => import('./pages/PathologyLabPage').then((m) => ({ default: m.PathologyLabPage })));
const DashboardPage = lazy(() => import('./pages/DashboardPage').then((m) => ({ default: m.DashboardPage })));
const ReportsPage = lazy(() => import('./pages/ReportsPage').then((m) => ({ default: m.ReportsPage })));
const PlatformConsolePage = lazy(() => import('./pages/PlatformConsolePage').then((m) => ({ default: m.PlatformConsolePage })));
const ChangePasswordPage = lazy(() => import('./pages/ChangePasswordPage').then((m) => ({ default: m.ChangePasswordPage })));
const MastersPage = lazy(() => import('./pages/MastersPage').then((m) => ({ default: m.MastersPage })));

/**
 * What shows while a screen's code is on its way.
 *
 * Deliberately not a spinner: on a warm cache the chunk arrives in a few
 * milliseconds and a spinner would flash, which reads as a fault. A quiet
 * line of text that appears the same way whether it lasts 20 ms or 2 seconds
 * is calmer than something that draws the eye and then vanishes.
 */
function RouteFallback() {
  return <div className="route-loading">Loading…</div>;
}

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

/**
 * An address that matches nothing.
 *
 * Rendering nothing at all was worse than it sounds: a stale bookmark or a
 * typo produced a blank page indistinguishable from a crash, with no way
 * onward. A signed-in person is offered the queue; a signed-out one, the
 * login screen.
 */
function NotFoundPage() {
  const { isAuthenticated } = useAuth();
  return (
    <div className="auth-page">
      <div className="auth-card">
        <h1>Page not found</h1>
        <p className="auth-subtitle">That address does not match anything in this application.</p>
        <Link className="button primary" to={isAuthenticated ? '/' : '/login'}>
          {isAuthenticated ? 'Back to the queue' : 'Go to sign in'}
        </Link>
      </div>
    </div>
  );
}

function AppRoutes() {
  return (
    <Suspense fallback={<RouteFallback />}>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
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
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </Suspense>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <SettingsProvider>
          <AppRoutes />
        </SettingsProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}
