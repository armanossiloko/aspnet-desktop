/**
 * Vue integration for `aspnetdesktop-client`.
 *
 * `createDesktopHttp()` is a plugin that provides the promise-based client for the in-process API over
 * `ipc://localhost`; components get it from `useDesktopHttpClient()` (or `this.$desktop` in the Options API):
 *
 * ```ts
 * createApp(App).use(createDesktopHttp()).mount('#app');
 *
 * const desktop = useDesktopHttpClient();
 * const metadata = await desktop.get<Metadata>('/api/metadata');  // in-process ASP.NET Core
 * await fetch('https://api.example.com/...');                      // real HTTP, untouched
 * ```
 */
import { hasInjectionContext, inject, type App, type InjectionKey } from 'vue';
import { createDesktopHttpClient, type DesktopHttpClient, type DesktopHttpClientOptions } from '../index.js';

export * from '../index.js';

/** Injection key the plugin provides the client under. */
export const DESKTOP_HTTP_CLIENT: InjectionKey<DesktopHttpClient> = Symbol('DesktopHttpClient');

let defaultClient: DesktopHttpClient | undefined;

/** Options for {@link createDesktopHttp}. */
export interface DesktopHttpPluginOptions extends DesktopHttpClientOptions {
  /** The client to provide, e.g. a fake in tests. Defaults to one created from the other options. */
  client?: DesktopHttpClient;
}

/** The Vue plugin returned by {@link createDesktopHttp}. */
export interface DesktopHttpPlugin {
  /** The provided client, for use outside components (stores, router guards). */
  readonly client: DesktopHttpClient;
  install(app: App): void;
}

/** Creates a plugin that provides a `DesktopHttpClient` to {@link useDesktopHttpClient} and `this.$desktop`. */
export function createDesktopHttp(options: DesktopHttpPluginOptions = {}): DesktopHttpPlugin {
  const { client: provided, ...clientOptions } = options;
  const client = provided ?? createDesktopHttpClient(clientOptions);
  return {
    client,
    install(app) {
      app.provide(DESKTOP_HTTP_CLIENT, client);
      app.config.globalProperties.$desktop = client;
    },
  };
}

/**
 * Returns the `DesktopHttpClient` provided by {@link createDesktopHttp}, or a shared default client
 * (`ipc://localhost`, no extra headers) when the plugin is not installed or there is no injection context.
 */
export function useDesktopHttpClient(): DesktopHttpClient {
  const provided = hasInjectionContext() ? inject(DESKTOP_HTTP_CLIENT, null) : null;
  return provided ?? (defaultClient ??= createDesktopHttpClient());
}

declare module 'vue' {
  interface ComponentCustomProperties {
    /** The `DesktopHttpClient` installed by {@link createDesktopHttp}. */
    $desktop: DesktopHttpClient;
  }
}
