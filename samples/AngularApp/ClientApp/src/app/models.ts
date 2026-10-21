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
