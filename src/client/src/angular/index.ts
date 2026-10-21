/**
 * Angular integration for `aspnetdesktop-client`.
 *
 * `DesktopHttpClient` is a second `HttpClient` whose backend talks to `ipc://localhost` instead of the network.
 * Register it next to (not instead of) `provideHttpClient()`:
 *
 * ```ts
 * providers: [provideHttpClient(), provideDesktopHttpClient()]
 *
 * constructor(private http: HttpClient, private desktop: DesktopHttpClient) {}
 * this.http.get('https://api.example.com/...');   // real HTTP, untouched
 * this.desktop.get<Metadata>('/api/metadata');     // in-process ASP.NET Core
 * ```
 */
import {
  HttpBackend,
  HttpClient,
  HttpErrorResponse,
  HttpEventType,
  HttpHandler,
  HttpHeaders,
  HttpResponse,
  type HttpEvent,
  type HttpHandlerFn,
  type HttpInterceptorFn,
  type HttpRequest,
} from '@angular/common/http';
import {
  EnvironmentInjector,
  InjectionToken,
  inject,
  makeEnvironmentProviders,
  runInInjectionContext,
  type EnvironmentProviders,
} from '@angular/core';
import { Observable } from 'rxjs';
import { applyRequestToken, resolveDesktopUrl, resolveIpcBase } from '../index.js';

export {
  DEFAULT_IPC_BASE,
  REQUEST_TOKEN_HEADER,
  getDesktopHostConfig,
  isDesktopHost,
  type DesktopHostConfig,
} from '../index.js';

/** Configuration for {@link provideDesktopHttpClient}. */
export interface DesktopHttpClientConfig {
  /** Overrides the API origin. Defaults to `window.__ASPNETDESKTOP__.ipcBase`, then `ipc://localhost`. */
  baseUrl?: string;
  /**
   * Interceptors for desktop requests only. The app's `HttpClient` interceptors never run here,
   * and these never run for real HTTP.
   */
  interceptors?: HttpInterceptorFn[];
  /** The `fetch` implementation to use (for tests). Defaults to the global `fetch`. */
  fetch?: typeof fetch;
}

/** Injection token holding the {@link DesktopHttpClientConfig}. */
export const DESKTOP_HTTP_CLIENT_CONFIG = new InjectionToken<DesktopHttpClientConfig>('DESKTOP_HTTP_CLIENT_CONFIG');

/**
 * An `HttpBackend` that sends requests to the in-process API over `ipc://` using `fetch`, producing the same
 * `HttpResponse` / `HttpErrorResponse` objects as Angular's own backends.
 */
export class DesktopHttpBackend implements HttpBackend {
  constructor(private readonly config: DesktopHttpClientConfig = {}) {}

  handle(req: HttpRequest<unknown>): Observable<HttpEvent<unknown>> {
    return new Observable<HttpEvent<unknown>>((observer) => {
      let url: string;
      try {
        url = resolveDesktopUrl(req.urlWithParams, resolveIpcBase(this.config.baseUrl));
      } catch (error) {
        observer.error(new HttpErrorResponse({ error, status: 0, statusText: 'Unknown Error', url: req.urlWithParams }));
        return undefined;
      }

      const headers = new Headers();
      for (const name of req.headers.keys()) {
        for (const value of req.headers.getAll(name) ?? []) {
          headers.append(name, value);
        }
      }

      if (!headers.has('Accept')) {
        headers.set('Accept', 'application/json, text/plain, */*');
      }

      const body = req.serializeBody();
      if (body !== null && !headers.has('Content-Type')) {
        const contentType = req.detectContentTypeHeader();
        if (contentType !== null) {
          headers.set('Content-Type', contentType);
        }
      }

      applyRequestToken(headers);

      const controller = new AbortController();
      const doFetch = this.config.fetch ?? globalThis.fetch.bind(globalThis);
      observer.next({ type: HttpEventType.Sent });

      doFetch(url, {
        method: req.method,
        headers,
        body: body as BodyInit | null,
        signal: controller.signal,
        credentials: req.withCredentials ? 'include' : 'same-origin',
      })
        .then(async (response) => {
          const responseHeaders = toHttpHeaders(response.headers);
          const responseUrl = response.url || url;
          const ok = response.status >= 200 && response.status < 300;
          const text = req.responseType === 'json' || req.responseType === 'text' || !ok ? await response.text() : null;

          if (!ok) {
            observer.error(
              new HttpErrorResponse({
                error: parseErrorBody(text, response.headers),
                headers: responseHeaders,
                status: response.status,
                statusText: response.statusText,
                url: responseUrl,
              }),
            );
            return;
          }

          let parsed: unknown;
          try {
            parsed = await readBody(req, response, text);
          } catch (error) {
            // Same shape Angular uses for "Http failure during parsing".
            observer.error(
              new HttpErrorResponse({
                error: { error, text },
                headers: responseHeaders,
                status: response.status,
                statusText: response.statusText,
                url: responseUrl,
              }),
            );
            return;
          }

          observer.next(
            new HttpResponse({
              body: parsed,
              headers: responseHeaders,
              status: response.status,
              statusText: response.statusText,
              url: responseUrl,
            }),
          );
          observer.complete();
        })
        .catch((error: unknown) => {
          if (controller.signal.aborted) {
            return;
          }

          observer.error(new HttpErrorResponse({ error, status: 0, statusText: 'Unknown Error', url }));
        });

      return () => controller.abort();
    });
  }
}

/**
 * A second `HttpClient` bound to the in-process API. Same `get`/`post`/`put`/`patch`/`delete` surface, generics,
 * headers, params and error handling as `HttpClient`; every request goes to `ipc://localhost`.
 */
export class DesktopHttpClient extends HttpClient {}

/** Runs the desktop-only interceptors in front of {@link DesktopHttpBackend}. */
class DesktopInterceptingHandler extends HttpHandler {
  private readonly chain: HttpHandlerFn;

  constructor(backend: HttpBackend, interceptors: readonly HttpInterceptorFn[], injector: EnvironmentInjector) {
    super();
    this.chain = interceptors.reduceRight<HttpHandlerFn>(
      (next, interceptor) => (req) => runInInjectionContext(injector, () => interceptor(req, next)),
      (req) => backend.handle(req),
    );
  }

  handle(req: HttpRequest<unknown>): Observable<HttpEvent<unknown>> {
    return this.chain(req);
  }
}

/**
 * Provides {@link DesktopHttpClient}. Use together with `provideHttpClient()`; neither replaces the other.
 */
export function provideDesktopHttpClient(config: DesktopHttpClientConfig = {}): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: DESKTOP_HTTP_CLIENT_CONFIG, useValue: config },
    { provide: DesktopHttpBackend, useFactory: () => new DesktopHttpBackend(inject(DESKTOP_HTTP_CLIENT_CONFIG)) },
    {
      provide: DesktopHttpClient,
      useFactory: () =>
        new DesktopHttpClient(
          new DesktopInterceptingHandler(
            inject(DesktopHttpBackend),
            inject(DESKTOP_HTTP_CLIENT_CONFIG).interceptors ?? [],
            inject(EnvironmentInjector),
          ),
        ),
    },
  ]);
}

function toHttpHeaders(headers: Headers): HttpHeaders {
  const init: Record<string, string[]> = {};
  headers.forEach((value, name) => {
    (init[name] ??= []).push(value);
  });
  return new HttpHeaders(init);
}

async function readBody(req: HttpRequest<unknown>, response: Response, text: string | null): Promise<unknown> {
  if (response.status === 204 || req.method === 'HEAD') {
    return null;
  }

  switch (req.responseType) {
    case 'json':
      return text === null || text === '' ? null : JSON.parse(text);
    case 'text':
      return text ?? '';
    case 'blob':
      return response.blob();
    case 'arraybuffer':
      return response.arrayBuffer();
  }
}

function parseErrorBody(text: string | null, headers: Headers): unknown {
  if (text === null || text === '') {
    return null;
  }

  if (/[/+]json\b/i.test(headers.get('Content-Type') ?? '')) {
    try {
      return JSON.parse(text);
    } catch {
      return text;
    }
  }

  return text;
}
