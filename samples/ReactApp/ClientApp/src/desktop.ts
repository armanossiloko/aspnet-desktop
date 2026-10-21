import { createDesktopHttpClient } from 'aspnetdesktop-client';

/** The in-process ASP.NET Core API over ipc://localhost. Real HTTP keeps using fetch. */
export const desktop = createDesktopHttpClient();

export interface AppMetadata {
  name: string;
  version: string;
  environment: string;
  runtime: string;
  operatingSystem: string;
}

export interface Order {
  id: number;
  product: string;
  quantity: number;
  createdAt: string;
}

export interface GitHubRepository {
  full_name: string;
  description: string;
  stargazers_count: number;
  html_url: string;
}
