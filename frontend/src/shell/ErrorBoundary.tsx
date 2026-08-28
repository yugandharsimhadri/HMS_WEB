import { Component, type ErrorInfo, type ReactNode } from 'react';

/**
 * The last thing between a thrown render and a blank white page.
 *
 * React unmounts the entire tree when a render throws and nothing catches it,
 * which leaves no message, no navigation and no way back short of devtools —
 * on a clinic PC, indistinguishable from the application being gone. This
 * catches that and offers the two things that actually recover a front desk:
 * reload, and clear the locally stored session.
 *
 * A class component because that is still the only way to implement
 * componentDidCatch; there is no hook equivalent.
 */
interface Props {
  children: ReactNode;
}

interface State {
  error: Error | null;
}

export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // The console is where this gets read from — there is no error-reporting
    // service wired up, and inventing one here would be a bigger decision
    // than a boundary. Kept explicit so the stack is not swallowed.
    console.error('Unhandled render error:', error, info.componentStack);
  }

  private reload = () => window.location.reload();

  /** For the failure this most often follows: something unreadable in local
   *  storage. Clears only this application's keys, then reloads to the login
   *  screen. */
  private resetLocalData = () => {
    try {
      for (const key of Object.keys(localStorage)) {
        if (key.startsWith('sivayaanhms')) localStorage.removeItem(key);
      }
    } catch {
      // Nothing to do — if storage cannot be read it cannot be the cause.
    }
    window.location.href = '/login';
  };

  render() {
    if (!this.state.error) return this.props.children;

    return (
      <div className="crash">
        <div className="crash-card">
          <h1>Something went wrong</h1>
          <p>
            The screen could not be drawn. Nothing you had saved is affected — this is the
            browser, not the clinic's records.
          </p>
          <p className="crash-detail">{this.state.error.message}</p>
          <div className="crash-actions">
            <button type="button" className="primary" onClick={this.reload}>
              Reload the page
            </button>
            <button type="button" className="ghost" onClick={this.resetLocalData}>
              Sign out and start again
            </button>
          </div>
          <p className="hint">
            If it keeps happening, note what you were doing and tell whoever supports this
            clinic's system.
          </p>
        </div>
      </div>
    );
  }
}
