import { Link } from 'react-router-dom';
import { useAuthOptions } from '../auth/usePasswordResetEnabled';
import { ClassicForgotPasswordPage } from './ClassicForgotPasswordPage';
import { OtpForgotPasswordPage } from './OtpForgotPasswordPage';

/**
 * /forgot-password: which reset page this deployment uses.
 *
 *   SivaCom:Enabled off (the default) — ClassicForgotPasswordPage, the page
 *     as it was before SivaCom, unchanged. It is also what shows while the
 *     options are still loading or could not be fetched, so with SivaCom off
 *     nothing about this screen changes or waits.
 *   SivaCom:Enabled on — OtpForgotPasswordPage, the three-step flow.
 *   PasswordReset:Enabled off — no reset at all; ask the clinic admin.
 */
export function ForgotPasswordPage() {
  const options = useAuthOptions();

  if (options?.passwordResetEnabled === false) {
    return (
      <div className="auth-page">
        <div className="auth-card">
          <h1>Reset your password</h1>
          <p className="auth-subtitle">
            Self-service password reset is not available. Ask your clinic admin to reset your password.
          </p>
          <p className="auth-footer">
            <Link to="/login">Back to sign in</Link>
          </p>
        </div>
      </div>
    );
  }

  return options?.sivaComEnabled ? <OtpForgotPasswordPage /> : <ClassicForgotPasswordPage />;
}
