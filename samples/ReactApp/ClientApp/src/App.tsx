import { type FormEvent, useEffect, useState } from 'react';
import { DesktopHttpError, getDesktopHostConfig } from 'aspnetdesktop-client';
import { desktop, type AppMetadata, type GitHubRepository, type Order } from './desktop';

interface ValidationProblem {
  title?: string;
  errors?: Record<string, string[]>;
}

const platform = getDesktopHostConfig()?.platform ?? 'browser';

export function App() {
  const [metadata, setMetadata] = useState<AppMetadata | null>(null);
  const [metadataError, setMetadataError] = useState<string | null>(null);
  const [orders, setOrders] = useState<Order[]>([]);
  const [orderErrors, setOrderErrors] = useState<string[]>([]);
  const [repository, setRepository] = useState<GitHubRepository | null>(null);
  const [repositoryError, setRepositoryError] = useState<string | null>(null);
  const [product, setProduct] = useState('');
  const [quantity, setQuantity] = useState(1);

  useEffect(() => {
    const abort = new AbortController();

    // In-process ASP.NET Core over ipc://localhost.
    desktop
      .get<AppMetadata>('/api/metadata', { signal: abort.signal })
      .then(setMetadata)
      .catch((error: unknown) => !abort.signal.aborted && setMetadataError(describe(error)));

    desktop
      .get<Order[]>('/api/orders', { signal: abort.signal })
      .then(setOrders)
      .catch((error: unknown) => !abort.signal.aborted && setOrderErrors([describe(error)]));

    // Real HTTP over the network: plain fetch, untouched by the desktop host.
    fetch('https://api.github.com/repos/dotnet/aspnetcore', { signal: abort.signal })
      .then((response) => {
        if (!response.ok) {
          throw new Error(`${response.status} ${response.statusText}`);
        }
        return response.json() as Promise<GitHubRepository>;
      })
      .then(setRepository)
      .catch((error: unknown) => !abort.signal.aborted && setRepositoryError(describe(error)));

    return () => abort.abort();
  }, []);

  async function addOrder(event: FormEvent) {
    event.preventDefault();
    setOrderErrors([]);
    try {
      const order = await desktop.post<Order>('/api/orders', { product, quantity });
      setOrders((current) => [...current, order]);
      setProduct('');
      setQuantity(1);
    } catch (error) {
      setOrderErrors(validationMessages(error));
    }
  }

  async function deleteOrder(order: Order) {
    try {
      await desktop.delete(`/api/orders/${order.id}`);
      setOrders((current) => current.filter((o) => o.id !== order.id));
    } catch (error) {
      setOrderErrors([describe(error)]);
    }
  }

  return (
    <main className="shell">
      <header className="masthead">
        <div className="mark" aria-hidden="true" />
        <div>
          <h1>
            AspNetDesktop <span className="plus">+</span> React
          </h1>
          <p className="lede">
            An ASP.NET Core backend running in-process. No Kestrel, no port: the UI is served from{' '}
            <code>app://localhost</code> and the API answers on <code>ipc://localhost</code>.
          </p>
        </div>
        <span className="badge">{platform}</span>
      </header>

      <section className="grid">
        <article className="card">
          <header>
            <h2>Metadata</h2>
            <p className="route">
              <code>desktop.get</code> → <code>GET ipc://localhost/api/metadata</code> (minimal API)
            </p>
          </header>
          {metadata ? (
            <dl className="facts">
              <dt>Name</dt>
              <dd>{metadata.name}</dd>
              <dt>Version</dt>
              <dd>{metadata.version}</dd>
              <dt>Environment</dt>
              <dd>{metadata.environment}</dd>
              <dt>Runtime</dt>
              <dd>{metadata.runtime}</dd>
              <dt>OS</dt>
              <dd>{metadata.operatingSystem}</dd>
            </dl>
          ) : metadataError ? (
            <p className="error">{metadataError}</p>
          ) : (
            <p className="muted">Loading…</p>
          )}
        </article>

        <article className="card">
          <header>
            <h2>Orders</h2>
            <p className="route">
              <code>desktop.post / delete</code> → <code>[ApiController] /api/orders</code> (MVC, validation, 201/204)
            </p>
          </header>
          <ul className="orders">
            {orders.length === 0 && <li className="muted">No orders yet.</li>}
            {orders.map((order) => (
              <li key={order.id}>
                <span className="qty">{order.quantity}×</span>
                <span className="product">{order.product}</span>
                <button type="button" className="ghost" onClick={() => deleteOrder(order)} aria-label={`Delete ${order.product}`}>
                  ✕
                </button>
              </li>
            ))}
          </ul>
          <form className="order-form" onSubmit={addOrder}>
            <input name="product" value={product} onChange={(e) => setProduct(e.target.value)} placeholder="Product" aria-label="Product" />
            <input
              name="quantity"
              type="number"
              min={1}
              max={99}
              value={quantity}
              onChange={(e) => setQuantity(Number(e.target.value))}
              aria-label="Quantity"
            />
            <button type="submit">Add</button>
          </form>
          {orderErrors.map((message) => (
            <p key={message} className="error">
              {message}
            </p>
          ))}
        </article>

        <article className="card">
          <header>
            <h2>Real HTTP</h2>
            <p className="route">
              <code>fetch</code> → <code>GET https://api.github.com/repos/dotnet/aspnetcore</code> (network)
            </p>
          </header>
          {repository ? (
            <>
              <p className="repo">
                <a href={repository.html_url} target="_blank" rel="noopener">
                  {repository.full_name}
                </a>
              </p>
              <p>{repository.description}</p>
              <p className="stars">★ {repository.stargazers_count.toLocaleString()}</p>
              <p className="muted">Links to the web open in your browser, not in this window.</p>
            </>
          ) : repositoryError ? (
            <p className="error">{repositoryError}</p>
          ) : (
            <p className="muted">Loading…</p>
          )}
        </article>
      </section>
    </main>
  );
}

function describe(error: unknown): string {
  if (error instanceof DesktopHttpError) {
    return error.status === 0 ? `Network error: ${error.message}` : `${error.status} ${error.statusText}`;
  }

  return error instanceof Error ? error.message : String(error);
}

/** Flattens ASP.NET Core ValidationProblemDetails into readable messages. */
function validationMessages(error: unknown): string[] {
  if (error instanceof DesktopHttpError) {
    const problem = error.error as ValidationProblem | null;
    if (error.status === 400 && problem?.errors) {
      return Object.values(problem.errors).flat();
    }

    return [problem?.title ?? describe(error)];
  }

  return [describe(error)];
}
