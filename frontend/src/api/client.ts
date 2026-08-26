/**
 * Where the backend lives. Supplied at *build* time, not run time — Vite
 * substitutes it into the bundle — so a hosted build takes it from the build
 * environment (on Cloudflare Pages, a Pages environment variable), not from a
 * file on the server.
 *
 * Thrown rather than defaulted. There is no sensible default: guessing the
 * current origin would be wrong for every split deployment, and leaving it
 * undefined sends every request to `undefined/api/...`, which fails as a
 * confusing network error on each screen instead of once, loudly, at the
 * moment the mistake was actually made.
 */
export const API_URL = import.meta.env.VITE_API_URL as string;

if (!API_URL) {
  throw new Error(
    'VITE_API_URL was not set when this build was made. Set it to the backend origin ' +
      '(for example https://api.your-clinic.example) and rebuild.',
  );
}
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
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body === undefined ? undefined : JSON.stringify(body) }),
};

/**
 * Downloads a server-generated file (a workbook, say) under its own name.
 *
 * Not `window.open`: these endpoints are behind `[Authorize]` and a plain
 * navigation carries no Authorization header. Not `openPdf` either — that
 * hands the bytes to the browser's viewer, which is right for a PDF and
 * useless for an .xlsx. This fetches with the token and drives a download,
 * taking the filename from the server's Content-Disposition so the file is
 * named the same way the desktop names it.
 */
export async function downloadFile(path: string, fallbackName: string): Promise<void> {
  const token = getToken();
  const headers = new Headers();
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const response = await fetch(`${API_URL}${path}`, { headers });

  if (!response.ok) {
    const text = await response.text();
    throw new ApiError(text || response.statusText, response.status);
  }

  const disposition = response.headers.get('content-disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const name = match ? decodeURIComponent(match[1]) : fallbackName;

  const url = URL.createObjectURL(await response.blob());
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = name;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();

  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
