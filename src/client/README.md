# aspnetdesktop-client

`DesktopHttpClient` for [AspNetDesktop](https://github.com/armanossiloko/aspnet-desktop#readme) apps: HTTP-shaped calls from the WebView UI to the in-process ASP.NET Core API over `ipc://localhost`.

It is a **separate client**. It never wraps or replaces the global `fetch` or Angular's `HttpClient`; those keep doing real HTTP.

| Import | For |
|---|---|
| `aspnetdesktop-client` | Any framework or none: `createDesktopHttpClient()` |
| `aspnetdesktop-client/angular` | Angular: `DesktopHttpClient` (an `HttpClient`) and `provideDesktopHttpClient()` |
| `aspnetdesktop-client/react` | React 18+: `DesktopHttpProvider` and `useDesktopHttpClient()` |
| `aspnetdesktop-client/vue` | Vue 3.3+: the `createDesktopHttp()` plugin and `useDesktopHttpClient()` |

Each framework is an optional peer dependency; install only the one you use.

## Vanilla (and Svelte, Solid, ...)

```ts
import { createDesktopHttpClient, DesktopHttpError } from 'aspnetdesktop-client';

const desktop = createDesktopHttpClient();

const metadata = await desktop.get<Metadata>('/api/metadata');
const order = await desktop.post<Order>('/api/orders', { product: 'Tea', quantity: 1 });
await desktop.delete(`/api/orders/${order.id}`);

// Full response (status, headers):
const response = await desktop.request<Order>('POST', '/api/orders', { body: { product: 'Tea' } });
response.headers.get('Location'); // ipc://localhost/api/orders/42

try {
  await desktop.get('/api/orders/404');
} catch (e) {
  if (e instanceof DesktopHttpError) {
    e.status; // 404 (0 = the request never got a response)
    e.error;  // parsed body, e.g. ProblemDetails
  }
}
```

Options per request: `headers`, `params`, `responseType` (`json` | `text` | `blob` | `arraybuffer`), `signal`, `credentials`.
Objects are sent as JSON; strings, `FormData`, `Blob`, `URLSearchParams` and buffers are sent as-is.

## React

```tsx
import { DesktopHttpProvider, useDesktopHttpClient } from 'aspnetdesktop-client/react';

createRoot(document.getElementById('root')!).render(
  <DesktopHttpProvider>
    <App />
  </DesktopHttpProvider>,
);

function Orders() {
  const desktop = useDesktopHttpClient();
  const [orders, setOrders] = useState<Order[]>([]);

  useEffect(() => {
    const abort = new AbortController();
    desktop.get<Order[]>('/api/orders', { signal: abort.signal }).then(setOrders);
    return () => abort.abort();
  }, [desktop]);
  // ...
}
```

- `DesktopHttpProvider` takes `options` (`baseUrl`, `headers`, `fetch`) or a ready-made `client`, e.g. a fake in tests.
- Without a provider, `useDesktopHttpClient()` returns a shared default client.
- The client's promises work as-is as a TanStack Query `queryFn`: `({ signal }) => desktop.get('/api/orders', { signal })`.
- `aspnetdesktop-client/react` re-exports the core API (`DesktopHttpError`, `getDesktopHostConfig`, ...).

## Vue

```ts
// main.ts
import { createDesktopHttp } from 'aspnetdesktop-client/vue';

createApp(App).use(createDesktopHttp()).mount('#app');
```

```vue
<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useDesktopHttpClient } from 'aspnetdesktop-client/vue';

const desktop = useDesktopHttpClient();
const orders = ref<Order[]>([]);
onMounted(async () => (orders.value = await desktop.get<Order[]>('/api/orders')));
</script>
```

- `createDesktopHttp()` takes the client options (`baseUrl`, `headers`, `fetch`) or a ready-made `client`.
- The plugin exposes the client as `plugin.client` for code outside components, such as Pinia stores and router guards, and as `this.$desktop` in the Options API.
- Without the plugin, `useDesktopHttpClient()` returns a shared default client.
- `aspnetdesktop-client/vue` re-exports the core API.

## Angular

```ts
import { provideHttpClient } from '@angular/common/http';
import { DesktopHttpClient, provideDesktopHttpClient } from 'aspnetdesktop-client/angular';

export const appConfig: ApplicationConfig = {
  providers: [provideHttpClient(), provideDesktopHttpClient()],
};

export class App {
  private http = inject(HttpClient);            // real HTTP
  private desktop = inject(DesktopHttpClient);  // in-process API

  metadata$ = this.desktop.get<Metadata>('/api/metadata');
}
```

`DesktopHttpClient` extends `HttpClient`, so it has the same API and errors (`HttpErrorResponse`). The app's interceptors do not run on it; desktop-only interceptors go in `provideDesktopHttpClient({ interceptors: [...] })`.

## Resolution rules

- Relative URLs resolve against `window.__ASPNETDESKTOP__.ipcBase` (injected by the host), falling back to `ipc://localhost`. Override with `baseUrl`.
- Absolute URLs must be on the API origin; anything else (e.g. `https://...`) is rejected so real HTTP can't be routed here by mistake.
- When the host enables `RequireRequestToken`, the injected token is sent as `X-AspNetDesktop-Token` automatically.
