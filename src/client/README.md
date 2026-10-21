# aspnetdesktop-client

`DesktopHttpClient` for [AspNetDesktop](https://github.com/armanossiloko/aspnet-desktop#readme) apps: HTTP-shaped calls from the WebView UI to the in-process ASP.NET Core API over `ipc://localhost`.

It is a **separate client**. It never wraps or replaces the global `fetch` or Angular's `HttpClient`; those keep doing real HTTP.

## Vanilla / React / Vue

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
