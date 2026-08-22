const API_URL = import.meta.env.VITE_API_URL as string;
const TOKEN_KEY = 'sivayaanhms.token';

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string | null): void {
  if (token) localStorage.setItem(TOKEN_KEY, token);
  else localStorage.removeItem(TOKEN_KEY);
}

export class ApiError extends Error {
  status: number;

  constructor(message: string, status: number) {
    super(message);
    this.status = status;
  }
}

/** One fetch wrapper for every call: attaches the bearer token when present,
 * and turns a non-2xx response into a thrown ApiError with whatever message
 * the API sent (ASP.NET Core's ProblemDetails "title", a plain string body,
 * or the status text as a last resort) — callers show one message, not a
 * three-way branch on response shape. */
async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = getToken();
  const headers = new Headers(options.headers);
  headers.set('Content-Type', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const response = await fetch(`${API_URL}${path}`, { ...options, headers });

  if (!response.ok) {
    const text = await response.text();
    let message = response.statusText;
    try {
      const parsed = JSON.parse(text);
      message = parsed.title ?? parsed.message ?? (text || message);
    } catch {
      if (text) message = text;
    }
    throw new ApiError(message, response.status);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

/**
 * Opens a server-generated PDF in a new tab.
 *
 * Not `window.open(url)`: these endpoints are behind `[Authorize]`, and a
 * plain navigation carries no Authorization header, so the tab would just
 * show a 401. Fetching it as a blob keeps the bearer token on the request
 * and hands the browser's own PDF viewer the bytes — which is what stands
 * in for the desktop's print-preview window, print and save included.
 */
export async function openPdf(path: string): Promise<void> {
  const token = getToken();
  const headers = new Headers();
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const response = await fetch(`${API_URL}${path}`, { headers });

  if (!response.ok) {
    const text = await response.text();
    throw new ApiError(text || response.statusText, response.status);
  }

  const url = URL.createObjectURL(await response.blob());
  const opened = window.open(url, '_blank');

  // Revoked on a delay rather than immediately: the new tab has to finish
  // reading the blob first, and a popup blocker may have returned null, in
  // which case nothing is reading it at all.
  setTimeout(() => URL.revokeObjectURL(url), 60_000);

  if (!opened) throw new ApiError('Allow pop-ups for this site to open the printable document.', 0);
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) }),
};
