import createClient, { type Middleware } from "openapi-fetch";
import type { components, paths } from "./schema";

export type Schemas = components["schemas"];
export type { paths };
export type { Middleware };

export interface Problem {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem: Problem;

  constructor(status: number, problem: Problem) {
    super(problem.detail ?? problem.title ?? `The request failed (${status})`);
    this.status = status;
    this.problem = problem;
  }
}

const correlate: Middleware = {
  onRequest({ request }) {
    if (!request.headers.has("X-Correlation-ID")) {
      request.headers.set("X-Correlation-ID", crypto.randomUUID().replaceAll("-", ""));
    }
    return request;
  },
};

export function createApi(baseUrl = "") {
  const client = createClient<paths>({ baseUrl });
  client.use(correlate);
  return client;
}

export type Api = ReturnType<typeof createApi>;

export async function unwrap<T>(promise: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await promise;
  if (error !== undefined || !response.ok) {
    const problem = (error && typeof error === "object" ? error : {}) as Problem;
    throw new ApiError(response.status, problem);
  }
  return data as T;
}
