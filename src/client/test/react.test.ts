import { createElement, type ReactNode } from 'react';
import { renderToString } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { DesktopHttpError as CoreDesktopHttpError } from '../src/index.js';
import {
  DesktopHttpError,
  DesktopHttpProvider,
  createDesktopHttpClient,
  useDesktopHttpClient,
  type DesktopHttpClient,
} from '../src/react/index.js';
import { json, mockFetch } from './fetch-mock.js';

/** Renders `tree` with a component that captures what useDesktopHttpClient() returns. */
function captureClient(wrap: (child: ReactNode) => ReactNode = (child) => child): DesktopHttpClient {
  let captured: DesktopHttpClient | undefined;
  function Probe() {
    captured = useDesktopHttpClient();
    return null;
  }

  renderToString(wrap(createElement(Probe)));
  return captured!;
}

describe('useDesktopHttpClient (React)', () => {
  it('returns the client given to DesktopHttpProvider', () => {
    const client = createDesktopHttpClient();

    const captured = captureClient((child) => createElement(DesktopHttpProvider, { client }, child));

    expect(captured).toBe(client);
  });

  it('creates the provided client from options', async () => {
    const { fetch, requests } = mockFetch(() => json({ name: 'DesktopApp' }));

    const captured = captureClient((child) =>
      createElement(DesktopHttpProvider, { options: { baseUrl: 'ipc://custom', fetch } }, child),
    );
    const metadata = await captured.get<{ name: string }>('/api/metadata');

    expect(metadata.name).toBe('DesktopApp');
    expect(requests[0]!.url).toBe('ipc://custom/api/metadata');
  });

  it('falls back to one shared ipc://localhost client without a provider', () => {
    const first = captureClient();
    const second = captureClient();

    expect(first.baseUrl).toBe('ipc://localhost');
    expect(second).toBe(first);
  });

  it('re-exports the core client API', () => {
    expect(DesktopHttpError).toBe(CoreDesktopHttpError);
  });
});
