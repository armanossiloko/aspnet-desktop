/**
 * aspnetdesktop-client
 *
 * `DesktopHttpClient` sends HTTP-shaped requests to the in-process ASP.NET Core API over `ipc://localhost`.
 * It is a separate client: it never wraps or replaces the global `fetch` or Angular's `HttpClient`, which keep
 * doing real HTTP over the network.
 */

/** Configuration the desktop host injects into every UI page as `window.__ASPNETDESKTOP__`. */
export interface DesktopHostConfig {
  /** Base URL of the isolated API origin, normally `ipc://localhost`. */
  readonly ipcBase: string;
  /** Base URL of the UI origin, normally `app://localhost`. */
  readonly appBase: string;
  /** `windows`, `macos` or `linux`. */
  readonly platform: string;
  /** Per-process request token, present when the host requires one. */
  readonly token?: string;
}

declare global {
  // eslint-disable-next-line no-var
  var __ASPNETDESKTOP__: DesktopHostConfig | undefined;
}

/** The API origin used when no host configuration has been injected. */
export const DEFAULT_IPC_BASE = 'ipc://localhost';

/** Header carrying the optional per-process request token. */
export const REQUEST_TOKEN_HEADER = 'X-AspNetDesktop-Token';

/** Returns the configuration injected by the desktop host, or `undefined` outside the desktop shell. */
export function getDesktopHostConfig(): DesktopHostConfig | undefined {
  return typeof globalThis === 'undefined' ? undefined : globalThis.__ASPNETDESKTOP__;
}

/** Whether the page is running inside the AspNetDesktop shell. */
export function isDesktopHost(): boolean {
  return getDesktopHostConfig() !== undefined;
}

/** Query parameters appended to the request URL. `null` and `undefined` values are skipped. */
export type DesktopQueryParams =
  | URLSearchParams
  | Record<string, string | number | boolean | ReadonlyArray<string | number | boolean> | null | undefined>;

/** How the response body is read. Defaults to `json`. */
export type DesktopResponseType = 'json' | 'text' | 'blob' | 'arraybuffer';

/** Per-request options, mirroring the familiar `HttpClient` options. */
export interface DesktopRequestOptions {
  headers?: HeadersInit;
  params?: DesktopQueryParams;
  responseType?: DesktopResponseType;
  signal?: AbortSignal;
  credentials?: RequestCredentials;
}

/** A full response, returned by {@link DesktopHttpClient.request}. */
export interface DesktopHttpResponse<T> {
  readonly ok: true;
  readonly status: number;
  readonly statusText: string;
  readonly url: string;
  readonly headers: Headers;
  readonly body: T;
}

/**
 * Thrown for non-2xx responses and transport failures, shaped like Angular's `HttpErrorResponse`:
 * `status` is `0` when the request never produced a response.
 */
export class DesktopHttpError<E = unknown> extends Error {
  override readonly name = 'DesktopHttpError';
  readonly ok = false;
  readonly status: number;
  readonly statusText: string;
  readonly url: string;
  readonly headers: Headers;
  /** The parsed error body (JSON when the server sent JSON, e.g. ProblemDetails), or the underlying failure. */
  readonly error: E;

  constructor(init: { status: number; statusText: string; url: string; headers?: Headers; error: E; message?: string }) {
    super(
      init.message ??
        (init.status === 0
          ? `Desktop request to ${init.url} failed: ${describe(init.error)}`
          : `Desktop request to ${init.url} failed: ${init.status} ${init.statusText}`),
    );
    this.status = init.status;
    this.statusText = init.statusText;
    this.url = init.url;
    this.headers = init.headers ?? new Headers();
    this.error = init.error;
  }
}

/** Options for {@link createDesktopHttpClient}. */
export interface DesktopHttpClientOptions {
  /** Overrides the API origin. Defaults to `window.__ASPNETDESKTOP__.ipcBase`, then `ipc://localhost`. */
  baseUrl?: string;
  /** Headers sent with every request. */
  headers?: HeadersInit;
  /** The `fetch` implementation to use (for tests). Defaults to the global `fetch`. */
  fetch?: typeof fetch;
}

/** HttpClient-like methods for the in-process API. Each verb resolves with the response body. */
export interface DesktopHttpClient {
  /** The API origin requests are sent to. */
  readonly baseUrl: string;
  request<T = unknown>(
    method: string,
    url: string,
    options?: DesktopRequestOptions & { body?: unknown },
  ): Promise<DesktopHttpResponse<T>>;
  get<T = unknown>(url: string, options?: DesktopRequestOptions): Promise<T>;
  delete<T = unknown>(url: string, options?: DesktopRequestOptions): Promise<T>;
  post<T = unknown>(url: string, body?: unknown, options?: DesktopRequestOptions): Promise<T>;
  put<T = unknown>(url: string, body?: unknown, options?: DesktopRequestOptions): Promise<T>;
  patch<T = unknown>(url: string, body?: unknown, options?: DesktopRequestOptions): Promise<T>;
}

/**
 * Creates a promise-based `DesktopHttpClient` for React, Vue, Svelte or vanilla JS.
 *
 * ```ts
 * const desktop = createDesktopHttpClient();
 * const metadata = await desktop.get<Metadata>('/api/metadata');
 * const order = await desktop.post<Order>('/api/orders', { product: 'Tea', quantity: 1 });
 * ```
 */
export function createDesktopHttpClient(options: DesktopHttpClientOptions = {}): DesktopHttpClient {
  const defaultHeaders = options.headers;
  const getBase = () => resolveIpcBase(options.baseUrl);
  const getFetch = () => options.fetch ?? globalThis.fetch.bind(globalThis);

  async function request<T>(
    method: string,
    url: string,
    requestOptions: DesktopRequestOptions & { body?: unknown } = {},
  ): Promise<DesktopHttpResponse<T>> {
    const target = resolveDesktopUrl(url, getBase(), requestOptions.params);
    const headers = new Headers(defaultHeaders);
    new Headers(requestOptions.headers).forEach((value, name) => headers.set(name, value));
    if (!headers.has('Accept')) {
      headers.set('Accept', 'application/json, text/plain, */*');
    }

    applyRequestToken(headers);
    const body = await serializeBody(requestOptions.body, headers);

    let response: Response;
    try {
      response = await getFetch()(target, {
        method: method.toUpperCase(),
        headers,
        body,
        signal: requestOptions.signal,
        credentials: requestOptions.credentials ?? 'same-origin',
      });
    } catch (error) {
      if (isAbortError(error)) {
        throw error;
      }

      throw new DesktopHttpError({ status: 0, statusText: 'Unknown Error', url: target, error });
    }

    const responseUrl = response.url || target;
    if (!response.ok) {
      throw new DesktopHttpError({
        status: response.status,
        statusText: response.statusText,
        url: responseUrl,
        headers: response.headers,
        error: await readErrorBody(response),
      });
    }

    let parsed: T;
    try {
      parsed = (await readBody(response, requestOptions.responseType ?? 'json', method)) as T;
    } catch (error) {
      throw new DesktopHttpError({
        status: response.status,
        statusText: response.statusText,
        url: responseUrl,
        headers: response.headers,
        error,
        message: `Desktop request to ${responseUrl} failed while parsing the response: ${describe(error)}`,
      });
    }

    return {
      ok: true,
      status: response.status,
      statusText: response.statusText,
      url: responseUrl,
      headers: response.headers,
      body: parsed,
    };
  }

  const bodyOf = async <T>(promise: Promise<DesktopHttpResponse<T>>) => (await promise).body;

  return {
    get baseUrl() {
      return getBase();
    },
    request,
    get: (url, opts) => bodyOf(request('GET', url, opts)),
    delete: (url, opts) => bodyOf(request('DELETE', url, opts)),
    post: (url, body, opts) => bodyOf(request('POST', url, { ...opts, body })),
    put: (url, body, opts) => bodyOf(request('PUT', url, { ...opts, body })),
    patch: (url, body, opts) => bodyOf(request('PATCH', url, { ...opts, body })),
  };
}

/** Resolves the API origin: explicit override, then the injected host config, then `ipc://localhost`. */
export function resolveIpcBase(override?: string): string {
  const base = override ?? getDesktopHostConfig()?.ipcBase ?? DEFAULT_IPC_BASE;
  return base.replace(/\/+$/, '');
}

/**
 * Resolves `url` against the API origin and appends `params`.
 * Absolute URLs must point at the API origin: real HTTP goes through `fetch`/`HttpClient`, never through this client.
 */
export function resolveDesktopUrl(url: string, base: string, params?: DesktopQueryParams): string {
  const origin = base.replace(/\/+$/, '');
  let resolved: string;
  if (/^[a-z][a-z\d+.-]*:/i.test(url)) {
    if (!sameOrigin(url, origin)) {
      throw new TypeError(
        `DesktopHttpClient only calls ${origin}. Use fetch or HttpClient for "${url}".`,
      );
    }

    resolved = url;
  } else {
    resolved = origin + (url.startsWith('/') ? url : `/${url}`);
  }

  const query = toQueryString(params);
  if (query) {
    resolved += (resolved.includes('?') ? '&' : '?') + query;
  }

  return resolved;
}

/** Adds the host's request token, when one was injected and the caller did not set it. */
export function applyRequestToken(headers: Headers): void {
  const token = getDesktopHostConfig()?.token;
  if (token && !headers.has(REQUEST_TOKEN_HEADER)) {
    headers.set(REQUEST_TOKEN_HEADER, token);
  }
}

function sameOrigin(url: string, origin: string): boolean {
  const normalizedUrl = url.toLowerCase();
  const normalizedOrigin = origin.toLowerCase();
  return (
    normalizedUrl === normalizedOrigin ||
    normalizedUrl.startsWith(`${normalizedOrigin}/`) ||
    normalizedUrl.startsWith(`${normalizedOrigin}?`)
  );
}

function toQueryString(params?: DesktopQueryParams): string {
  if (!params) {
    return '';
  }

  if (params instanceof URLSearchParams) {
    return params.toString();
  }

  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined) {
      continue;
    }

    const values: ReadonlyArray<string | number | boolean> = Array.isArray(value) ? value : [value as string];
    for (const item of values) {
      search.append(key, String(item));
    }
  }

  return search.toString();
}

async function serializeBody(body: unknown, headers: Headers): Promise<BodyInit | undefined> {
  if (body === undefined || body === null) {
    return undefined;
  }

  if (typeof ReadableStream !== 'undefined' && body instanceof ReadableStream) {
    // fetch() rejects stream bodies without `duplex: 'half'` and only streams them over HTTP/2, which the desktop
    // schemes aren't. The adapters buffer request bodies anyway, so read the stream up front.
    return new Response(body).blob();
  }

  if (
    typeof body === 'string' ||
    body instanceof Blob ||
    body instanceof FormData ||
    body instanceof URLSearchParams ||
    body instanceof ArrayBuffer ||
    ArrayBuffer.isView(body)
  ) {
    return body as BodyInit;
  }

  if (!headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  return JSON.stringify(body);
}

async function readBody(response: Response, responseType: DesktopResponseType, method: string): Promise<unknown> {
  if (response.status === 204 || method.toUpperCase() === 'HEAD') {
    return null;
  }

  switch (responseType) {
    case 'text':
      return response.text();
    case 'blob':
      return response.blob();
    case 'arraybuffer':
      return response.arrayBuffer();
    case 'json': {
      const text = await response.text();
      return text === '' ? null : JSON.parse(text);
    }
  }
}

async function readErrorBody(response: Response): Promise<unknown> {
  const text = await response.text().catch(() => '');
  if (text === '') {
    return null;
  }

  if (/[/+]json\b/i.test(response.headers.get('Content-Type') ?? '')) {
    try {
      return JSON.parse(text);
    } catch {
      return text;
    }
  }

  return text;
}

function isAbortError(error: unknown): boolean {
  return typeof error === 'object' && error !== null && (error as { name?: unknown }).name === 'AbortError';
}

function describe(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
