import { afterEach, describe, expect, it } from 'vitest';
import {
  DesktopHttpError,
  REQUEST_TOKEN_HEADER,
  createDesktopHttpClient,
  resolveDesktopUrl,
  type DesktopHostConfig,
} from '../src/index.js';
import { json, mockFetch } from './fetch-mock.js';

function setHost(config: Partial<DesktopHostConfig> | undefined) {
  globalThis.__ASPNETDESKTOP__ = config as DesktopHostConfig | undefined;
}

afterEach(() => setHost(undefined));

describe('createDesktopHttpClient', () => {
  it('targets ipc://localhost by default and parses JSON', async () => {
    const { fetch, requests } = mockFetch(() => json({ name: 'DesktopApp' }));
    const desktop = createDesktopHttpClient({ fetch });

    const metadata = await desktop.get<{ name: string }>('/api/metadata');

    expect(metadata.name).toBe('DesktopApp');
    expect(requests[0]!.url).toBe('ipc://localhost/api/metadata');
    expect(requests[0]!.method).toBe('GET');
    expect(requests[0]!.headers.get('Accept')).toContain('application/json');
  });

  it('uses the ipcBase injected by the host', async () => {
    setHost({ ipcBase: 'ipc://custom/', appBase: 'app://localhost', platform: 'windows' });
    const { fetch, requests } = mockFetch(() => json({}));

    await createDesktopHttpClient({ fetch }).get('api/metadata');

    expect(requests[0]!.url).toBe('ipc://custom/api/metadata');
  });

  it('serializes objects as JSON and sends the verb', async () => {
    const { fetch, requests } = mockFetch(() => json({ id: 42 }, { status: 201 }));
    const desktop = createDesktopHttpClient({ fetch });

    const created = await desktop.post<{ id: number }>('/api/orders', { product: 'Tea', quantity: 2 });
    await desktop.put('/api/orders/42', { product: 'Tea', quantity: 3 });
    await desktop.patch('/api/orders/42', { quantity: 4 });

    expect(created.id).toBe(42);
    expect(requests.map((r) => r.method)).toEqual(['POST', 'PUT', 'PATCH']);
    expect(requests[0]!.headers.get('Content-Type')).toBe('application/json');
    expect(JSON.parse(requests[0]!.body!)).toEqual({ product: 'Tea', quantity: 2 });
  });

  it('passes strings and FormData through untouched', async () => {
    const { fetch, requests } = mockFetch(() => new Response(null, { status: 204 }));
    const desktop = createDesktopHttpClient({ fetch });

    await desktop.post('/api/raw', 'plain text', { headers: { 'Content-Type': 'text/plain' } });

    expect(requests[0]!.body).toBe('plain text');
    expect(requests[0]!.headers.get('Content-Type')).toBe('text/plain');
  });

  it('appends query params, skipping null and expanding arrays', async () => {
    const { fetch, requests } = mockFetch(() => json([]));
    const desktop = createDesktopHttpClient({ fetch });

    await desktop.get('/api/search?sort=asc', { params: { q: 'café', page: 2, tag: ['a', 'b'], empty: null } });

    expect(requests[0]!.url).toBe('ipc://localhost/api/search?sort=asc&q=caf%C3%A9&page=2&tag=a&tag=b');
  });

  it('returns null for 204 and empty bodies', async () => {
    const { fetch } = mockFetch(() => new Response(null, { status: 204 }));

    await expect(createDesktopHttpClient({ fetch }).delete('/api/orders/1')).resolves.toBeNull();
  });

  it('supports text, blob and arraybuffer responses', async () => {
    const { fetch } = mockFetch(() => new Response('hello'));
    const desktop = createDesktopHttpClient({ fetch });

    expect(await desktop.get('/a', { responseType: 'text' })).toBe('hello');
    expect(await (await desktop.get<Blob>('/b', { responseType: 'blob' })).text()).toBe('hello');
    expect((await desktop.get<ArrayBuffer>('/c', { responseType: 'arraybuffer' })).byteLength).toBe(5);
  });

  it('request() exposes status and headers', async () => {
    const { fetch } = mockFetch(() => json({ id: 1 }, { status: 201, headers: { Location: 'ipc://localhost/api/orders/1' } }));

    const response = await createDesktopHttpClient({ fetch }).request<{ id: number }>('POST', '/api/orders', { body: {} });

    expect(response.status).toBe(201);
    expect(response.headers.get('Location')).toBe('ipc://localhost/api/orders/1');
    expect(response.body.id).toBe(1);
  });

  it('throws DesktopHttpError with the parsed ProblemDetails for non-2xx responses', async () => {
    const problem = { title: 'One or more validation errors occurred.', status: 400, errors: { Product: ['Required'] } };
    const { fetch } = mockFetch(() =>
      new Response(JSON.stringify(problem), {
        status: 400,
        statusText: 'Bad Request',
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    );

    const error = await createDesktopHttpClient({ fetch }).post('/api/orders', {}).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(DesktopHttpError);
    const httpError = error as DesktopHttpError<typeof problem>;
    expect(httpError.status).toBe(400);
    expect(httpError.statusText).toBe('Bad Request');
    expect(httpError.ok).toBe(false);
    expect(httpError.error.errors.Product).toEqual(['Required']);
  });

  it('reports transport failures as status 0', async () => {
    const { fetch } = mockFetch(() => {
      throw new TypeError('Failed to fetch');
    });

    const error = (await createDesktopHttpClient({ fetch }).get('/api/x').catch((e: unknown) => e)) as DesktopHttpError;

    expect(error.status).toBe(0);
    expect(error.error).toBeInstanceOf(TypeError);
  });

  it('reports invalid JSON on a 2xx response as a parse failure', async () => {
    const { fetch } = mockFetch(() => new Response('<html>', { status: 200 }));

    const error = (await createDesktopHttpClient({ fetch }).get('/api/x').catch((e: unknown) => e)) as DesktopHttpError;

    expect(error.status).toBe(200);
    expect(error.message).toContain('parsing');
  });

  it('rethrows aborts unchanged', async () => {
    const controller = new AbortController();
    const { fetch } = mockFetch(() => {
      throw new DOMException('aborted', 'AbortError');
    });
    controller.abort();

    const error = await createDesktopHttpClient({ fetch }).get('/api/x', { signal: controller.signal }).catch((e: unknown) => e);

    expect((error as Error).name).toBe('AbortError');
  });

  it('sends the injected request token', async () => {
    setHost({ ipcBase: 'ipc://localhost', appBase: 'app://localhost', platform: 'linux', token: 'secret' });
    const { fetch, requests } = mockFetch(() => json({}));

    await createDesktopHttpClient({ fetch }).get('/api/x');

    expect(requests[0]!.headers.get(REQUEST_TOKEN_HEADER)).toBe('secret');
  });

  it('refuses to send real HTTP through the desktop client', async () => {
    const { fetch, requests } = mockFetch(() => json({}));

    await expect(createDesktopHttpClient({ fetch }).get('https://api.example.com/forecast')).rejects.toThrow(TypeError);
    expect(requests).toHaveLength(0);
  });

  it('never touches the global fetch', async () => {
    const original = globalThis.fetch;
    const { fetch } = mockFetch(() => json({}));

    await createDesktopHttpClient({ fetch }).get('/api/x');

    expect(globalThis.fetch).toBe(original);
  });
});

describe('resolveDesktopUrl', () => {
  it('accepts absolute URLs on the API origin only', () => {
    expect(resolveDesktopUrl('ipc://localhost/api/x', 'ipc://localhost')).toBe('ipc://localhost/api/x');
    expect(() => resolveDesktopUrl('ipc://localhost.evil/api', 'ipc://localhost')).toThrow(TypeError);
    expect(() => resolveDesktopUrl('app://localhost/api', 'ipc://localhost')).toThrow(TypeError);
  });
});
