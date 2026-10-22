import { createSSRApp, defineComponent, getCurrentInstance, h, type App } from 'vue';
import { renderToString } from 'vue/server-renderer';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DesktopHttpError as CoreDesktopHttpError } from '../src/index.js';
import {
  DesktopHttpError,
  createDesktopHttp,
  createDesktopHttpClient,
  useDesktopHttpClient,
  type DesktopHttpClient,
} from '../src/vue/index.js';
import { json, mockFetch } from './fetch-mock.js';

/** Renders a component that captures what useDesktopHttpClient() and this.$desktop return. */
async function captureClient(setup: (app: App) => void = () => {}) {
  let fromComposable: DesktopHttpClient | undefined;
  let fromGlobal: DesktopHttpClient | undefined;
  const Probe = defineComponent({
    setup() {
      fromComposable = useDesktopHttpClient();
      fromGlobal = getCurrentInstance()!.proxy!.$desktop;
      return () => h('div');
    },
  });

  const app = createSSRApp(Probe);
  setup(app);
  await renderToString(app);
  return { fromComposable: fromComposable!, fromGlobal };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('useDesktopHttpClient (Vue)', () => {
  it('returns the client installed by the plugin, also as this.$desktop', async () => {
    const client = createDesktopHttpClient();

    const { fromComposable, fromGlobal } = await captureClient((app) => app.use(createDesktopHttp({ client })));

    expect(fromComposable).toBe(client);
    expect(fromGlobal).toBe(client);
  });

  it('creates the provided client from options and exposes it on the plugin', async () => {
    const { fetch, requests } = mockFetch(() => json({ name: 'DesktopApp' }));
    const plugin = createDesktopHttp({ baseUrl: 'ipc://custom', fetch });

    const { fromComposable } = await captureClient((app) => app.use(plugin));
    const metadata = await fromComposable.get<{ name: string }>('/api/metadata');

    expect(fromComposable).toBe(plugin.client);
    expect(metadata.name).toBe('DesktopApp');
    expect(requests[0]!.url).toBe('ipc://custom/api/metadata');
  });

  it('works in app.runWithContext, e.g. in router guards', () => {
    const plugin = createDesktopHttp();
    const app = createSSRApp({ render: () => null }).use(plugin);

    expect(app.runWithContext(() => useDesktopHttpClient())).toBe(plugin.client);
  });

  it('falls back to one shared ipc://localhost client without the plugin or outside components', async () => {
    const warn = vi.spyOn(console, 'warn');

    const { fromComposable } = await captureClient();
    const outside = useDesktopHttpClient();

    expect(fromComposable.baseUrl).toBe('ipc://localhost');
    expect(outside).toBe(fromComposable);
    expect(warn).not.toHaveBeenCalled();
  });

  it('re-exports the core client API', () => {
    expect(DesktopHttpError).toBe(CoreDesktopHttpError);
  });
});
