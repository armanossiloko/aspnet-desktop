<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue';
import { DesktopHttpError, getDesktopHostConfig, useDesktopHttpClient } from 'aspnetdesktop-client/vue';
import type { AppMetadata, GitHubRepository, Order } from './models';

interface ValidationProblem {
  title?: string;
  errors?: Record<string, string[]>;
}

const desktop = useDesktopHttpClient();
const platform = getDesktopHostConfig()?.platform ?? 'browser';

const metadata = ref<AppMetadata | null>(null);
const metadataError = ref<string | null>(null);
const orders = ref<Order[]>([]);
const orderErrors = ref<string[]>([]);
const repository = ref<GitHubRepository | null>(null);
const repositoryError = ref<string | null>(null);
const product = ref('');
const quantity = ref(1);

const abort = new AbortController();
onUnmounted(() => abort.abort());

onMounted(() => {
  // In-process ASP.NET Core over ipc://localhost.
  desktop
    .get<AppMetadata>('/api/metadata', { signal: abort.signal })
    .then((value) => (metadata.value = value))
    .catch((error: unknown) => !abort.signal.aborted && (metadataError.value = describe(error)));

  desktop
    .get<Order[]>('/api/orders', { signal: abort.signal })
    .then((value) => (orders.value = value))
    .catch((error: unknown) => !abort.signal.aborted && (orderErrors.value = [describe(error)]));

  // Real HTTP over the network: plain fetch, untouched by the desktop host.
  fetch('https://api.github.com/repos/dotnet/aspnetcore', { signal: abort.signal })
    .then((response) => {
      if (!response.ok) {
        throw new Error(`${response.status} ${response.statusText}`);
      }
      return response.json() as Promise<GitHubRepository>;
    })
    .then((value) => (repository.value = value))
    .catch((error: unknown) => !abort.signal.aborted && (repositoryError.value = describe(error)));
});

async function addOrder() {
  orderErrors.value = [];
  try {
    const order = await desktop.post<Order>('/api/orders', { product: product.value, quantity: quantity.value });
    orders.value = [...orders.value, order];
    product.value = '';
    quantity.value = 1;
  } catch (error) {
    orderErrors.value = validationMessages(error);
  }
}

async function deleteOrder(order: Order) {
  try {
    await desktop.delete(`/api/orders/${order.id}`);
    orders.value = orders.value.filter((o) => o.id !== order.id);
  } catch (error) {
    orderErrors.value = [describe(error)];
  }
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
</script>

<template>
  <main class="shell">
    <header class="masthead">
      <div class="mark" aria-hidden="true" />
      <div>
        <h1>AspNetDesktop <span class="plus">+</span> Vue</h1>
        <p class="lede">
          An ASP.NET Core backend running in-process. No Kestrel, no port: the UI is served from
          <code>app://localhost</code> and the API answers on <code>ipc://localhost</code>.
        </p>
      </div>
      <span class="badge">{{ platform }}</span>
    </header>

    <section class="grid">
      <article class="card">
        <header>
          <h2>Metadata</h2>
          <p class="route">
            <code>desktop.get</code> → <code>GET ipc://localhost/api/metadata</code> (minimal API)
          </p>
        </header>
        <dl v-if="metadata" class="facts">
          <dt>Name</dt>
          <dd>{{ metadata.name }}</dd>
          <dt>Version</dt>
          <dd>{{ metadata.version }}</dd>
          <dt>Environment</dt>
          <dd>{{ metadata.environment }}</dd>
          <dt>Runtime</dt>
          <dd>{{ metadata.runtime }}</dd>
          <dt>OS</dt>
          <dd>{{ metadata.operatingSystem }}</dd>
        </dl>
        <p v-else-if="metadataError" class="error">{{ metadataError }}</p>
        <p v-else class="muted">Loading…</p>
      </article>

      <article class="card">
        <header>
          <h2>Orders</h2>
          <p class="route">
            <code>desktop.post / delete</code> → <code>[ApiController] /api/orders</code> (MVC, validation, 201/204)
          </p>
        </header>
        <ul class="orders">
          <li v-if="orders.length === 0" class="muted">No orders yet.</li>
          <li v-for="order in orders" :key="order.id">
            <span class="qty">{{ order.quantity }}×</span>
            <span class="product">{{ order.product }}</span>
            <button type="button" class="ghost" :aria-label="`Delete ${order.product}`" @click="deleteOrder(order)">
              ✕
            </button>
          </li>
        </ul>
        <form class="order-form" @submit.prevent="addOrder">
          <input v-model="product" name="product" placeholder="Product" aria-label="Product" />
          <input v-model.number="quantity" name="quantity" type="number" min="1" max="99" aria-label="Quantity" />
          <button type="submit">Add</button>
        </form>
        <p v-for="message in orderErrors" :key="message" class="error">{{ message }}</p>
      </article>

      <article class="card">
        <header>
          <h2>Real HTTP</h2>
          <p class="route">
            <code>fetch</code> → <code>GET https://api.github.com/repos/dotnet/aspnetcore</code> (network)
          </p>
        </header>
        <template v-if="repository">
          <p class="repo">
            <a :href="repository.html_url" target="_blank" rel="noopener">{{ repository.full_name }}</a>
          </p>
          <p>{{ repository.description }}</p>
          <p class="stars">★ {{ repository.stargazers_count.toLocaleString() }}</p>
          <p class="muted">Links to the web open in your browser, not in this window.</p>
        </template>
        <p v-else-if="repositoryError" class="error">{{ repositoryError }}</p>
        <p v-else class="muted">Loading…</p>
      </article>
    </section>

    <footer class="credit">
      Built with <a href="https://github.com/armanossiloko/aspnet-desktop" target="_blank" rel="noopener">AspNetDesktop</a> by Arman Ossi Loko
    </footer>
  </main>
</template>
