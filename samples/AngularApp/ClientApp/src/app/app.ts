import { Component, type OnInit, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { DesktopHttpClient, getDesktopHostConfig } from 'aspnetdesktop-client/angular';
import type { AppMetadata, GitHubRepository, Order } from './models';

interface ValidationProblem {
  title?: string;
  errors?: Record<string, string[]>;
}

@Component({
  selector: 'app-root',
  imports: [FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  // Real HTTP over the network.
  private readonly http = inject(HttpClient);
  // The in-process ASP.NET Core API over ipc://localhost.
  private readonly desktop = inject(DesktopHttpClient);

  protected readonly platform = getDesktopHostConfig()?.platform ?? 'browser';
  protected readonly metadata = signal<AppMetadata | null>(null);
  protected readonly metadataError = signal<string | null>(null);
  protected readonly orders = signal<Order[]>([]);
  protected readonly orderErrors = signal<string[]>([]);
  protected readonly repository = signal<GitHubRepository | null>(null);
  protected readonly repositoryError = signal<string | null>(null);

  protected product = '';
  protected quantity = 1;

  ngOnInit(): void {
    this.desktop.get<AppMetadata>('/api/metadata').subscribe({
      next: (metadata) => this.metadata.set(metadata),
      error: (error: HttpErrorResponse) => this.metadataError.set(describe(error)),
    });

    this.loadOrders();

    this.http.get<GitHubRepository>('https://api.github.com/repos/dotnet/aspnetcore').subscribe({
      next: (repository) => this.repository.set(repository),
      error: (error: HttpErrorResponse) => this.repositoryError.set(describe(error)),
    });
  }

  protected addOrder(): void {
    this.orderErrors.set([]);
    this.desktop.post<Order>('/api/orders', { product: this.product, quantity: this.quantity }).subscribe({
      next: (order) => {
        this.orders.update((orders) => [...orders, order]);
        this.product = '';
        this.quantity = 1;
      },
      error: (error: HttpErrorResponse) => this.orderErrors.set(validationMessages(error)),
    });
  }

  protected deleteOrder(order: Order): void {
    this.desktop.delete(`/api/orders/${order.id}`).subscribe({
      next: () => this.orders.update((orders) => orders.filter((o) => o.id !== order.id)),
      error: (error: HttpErrorResponse) => this.orderErrors.set([describe(error)]),
    });
  }

  private loadOrders(): void {
    this.desktop.get<Order[]>('/api/orders').subscribe({
      next: (orders) => this.orders.set(orders),
      error: (error: HttpErrorResponse) => this.orderErrors.set([describe(error)]),
    });
  }
}

function describe(error: HttpErrorResponse): string {
  return error.status === 0 ? `Network error: ${error.message}` : `${error.status} ${error.statusText}`;
}

/** Flattens ASP.NET Core ValidationProblemDetails into readable messages. */
function validationMessages(error: HttpErrorResponse): string[] {
  const problem = error.error as ValidationProblem | null;
  if (error.status === 400 && problem?.errors) {
    return Object.values(problem.errors).flat();
  }

  return [problem?.title ?? describe(error)];
}
