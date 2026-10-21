export interface RecordedRequest {
  url: string;
  method: string;
  headers: Headers;
  body: string | null;
}

/** A fetch stand-in that records requests and replies with a canned response. */
export function mockFetch(reply: (request: RecordedRequest) => Response | Promise<Response>) {
  const requests: RecordedRequest[] = [];
  const fetch = async (input: RequestInfo | URL, init: RequestInit = {}): Promise<Response> => {
    const request: RecordedRequest = {
      url: String(input),
      method: init.method ?? 'GET',
      headers: new Headers(init.headers),
      body: init.body === undefined || init.body === null ? null : String(init.body),
    };
    requests.push(request);
    return reply(request);
  };

  return { fetch: fetch as typeof globalThis.fetch, requests };
}

export function json(body: unknown, init: ResponseInit = {}): Response {
  return new Response(JSON.stringify(body), {
    ...init,
    headers: { 'Content-Type': 'application/json; charset=utf-8', ...(init.headers as Record<string, string>) },
  });
}
