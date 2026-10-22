/**
 * React integration for `aspnetdesktop-client`.
 *
 * `useDesktopHttpClient()` returns the promise-based client for the in-process API over `ipc://localhost`.
 * Wrap the app in `DesktopHttpProvider` to configure it once, or to hand components a fake in tests:
 *
 * ```tsx
 * createRoot(root).render(
 *   <DesktopHttpProvider>
 *     <App />
 *   </DesktopHttpProvider>,
 * );
 *
 * const desktop = useDesktopHttpClient();
 * const metadata = await desktop.get<Metadata>('/api/metadata');  // in-process ASP.NET Core
 * await fetch('https://api.example.com/...');                      // real HTTP, untouched
 * ```
 */
import { createContext, createElement, useContext, useState, type ReactNode } from 'react';
import { createDesktopHttpClient, type DesktopHttpClient, type DesktopHttpClientOptions } from '../index.js';

export * from '../index.js';

const DesktopHttpContext = createContext<DesktopHttpClient | null>(null);
DesktopHttpContext.displayName = 'DesktopHttpContext';

let defaultClient: DesktopHttpClient | undefined;

/** Props for {@link DesktopHttpProvider}. */
export interface DesktopHttpProviderProps {
  /** The client to provide, e.g. a fake in tests. Defaults to one created from `options`. */
  client?: DesktopHttpClient;
  /** Options for the client created when `client` is not set. Read on the first render only. */
  options?: DesktopHttpClientOptions;
  children?: ReactNode;
}

/** Provides a `DesktopHttpClient` to {@link useDesktopHttpClient} in the components below it. */
export function DesktopHttpProvider({ client, options, children }: DesktopHttpProviderProps) {
  const [created] = useState(() => (client ? undefined : createDesktopHttpClient(options)));
  return createElement(DesktopHttpContext.Provider, { value: client ?? created ?? null }, children);
}

/**
 * Returns the `DesktopHttpClient` from the nearest {@link DesktopHttpProvider}, or a shared default client
 * (`ipc://localhost`, no extra headers) when there is no provider.
 */
export function useDesktopHttpClient(): DesktopHttpClient {
  return useContext(DesktopHttpContext) ?? (defaultClient ??= createDesktopHttpClient());
}
