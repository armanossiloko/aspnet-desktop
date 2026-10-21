import { HttpClient, HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { type EnvironmentInjector, InjectionToken, createEnvironmentInjector, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { afterEach, describe, expect, it } from 'vitest';
import { DesktopHttpClient, provideDesktopHttpClient, type DesktopHttpClientConfig } from '../src/angular/index.js';
import { json, mockFetch } from './fetch-mock.js';

const injectors: EnvironmentInjector[] = [];

function createInjector(config: DesktopHttpClientConfig, extra: Parameters<typeof createEnvironmentInjector>[0] = []) {
  const injector = createEnvironmentInjector(
    [provideDesktopHttpClient(config), ...extra],
    null as unknown as EnvironmentInjector,
  );
  injectors.push(injector);
  return injector;
}

afterEach(() => {
  injectors.splice(0).forEach((injector) => injector.destroy());
});

describe('DesktopHttpClient (Angular)', () => {
  it('is a separate HttpClient that targets ipc://localhost', async () => {
    const { fetch, requests } = mockFetch(() => json({ name: 'DesktopApp' }));
    const injector = createInjector({ fetch });

    const desktop = injector.get(DesktopHttpClient);
    const metadata = await firstValueFrom(desktop.get<{ name: string }>('/api/metadata', { params: { v: 1 } }));

    expect(desktop).toBeInstanceOf(HttpClient);
    // Providing the desktop client neither registers nor replaces the app's HttpClient.
    expect(injector.get(HttpClient, null)).toBeNull();
    expect(metadata.name).toBe('DesktopApp');
    expect(requests[0]!.url).toBe('ipc://localhost/api/metadata?v=1');
  });

  it('serializes JSON bodies with the right content type', async () => {
    const { fetch, requests } = mockFetch(() => json({ id: 42 }, { status: 201 }));
    const desktop = createInjector({ fetch }).get(DesktopHttpClient);

    const response = await firstValueFrom(desktop.post<{ id: number }>('/api/orders', { product: 'Tea' }, { observe: 'response' }));

    expect(response.status).toBe(201);
    expect(response.body?.id).toBe(42);
    expect(requests[0]!.method).toBe('POST');
    expect(requests[0]!.headers.get('Content-Type')).toBe('application/json');
    expect(JSON.parse(requests[0]!.body!)).toEqual({ product: 'Tea' });
  });

  it('maps error statuses to HttpErrorResponse with the parsed body', async () => {
    const { fetch } = mockFetch(() =>
      new Response(JSON.stringify({ title: 'Not Found' }), {
        status: 404,
        statusText: 'Not Found',
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    );
    const desktop = createInjector({ fetch }).get(DesktopHttpClient);

    const error = await firstValueFrom(desktop.get('/api/orders/404')).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect((error as HttpErrorResponse).status).toBe(404);
    expect((error as HttpErrorResponse).error).toEqual({ title: 'Not Found' });
  });

  it('runs desktop interceptors in an injection context', async () => {
    const TENANT = new InjectionToken<string>('TENANT');
    const addTenant: HttpInterceptorFn = (req, next) => next(req.clone({ setHeaders: { 'X-Tenant': inject(TENANT) } }));
    const { fetch, requests } = mockFetch(() => json({}));
    const desktop = createInjector({ fetch, interceptors: [addTenant] }, [{ provide: TENANT, useValue: 'acme' }]).get(
      DesktopHttpClient,
    );

    await firstValueFrom(desktop.get('/api/x'));

    expect(requests[0]!.headers.get('X-Tenant')).toBe('acme');
  });

  it('rejects absolute URLs off the API origin', async () => {
    const { fetch, requests } = mockFetch(() => json({}));
    const desktop = createInjector({ fetch }).get(DesktopHttpClient);

    const error = (await firstValueFrom(desktop.get('https://api.example.com/')).catch((e: unknown) => e)) as HttpErrorResponse;

    expect(error.status).toBe(0);
    expect(requests).toHaveLength(0);
  });
});
