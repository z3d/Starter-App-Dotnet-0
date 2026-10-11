import { createApi, type Middleware } from "@starterapp/api-client";
import { accessToken, signOut, stepUp } from "./auth";

export { ApiError, unwrap, type Problem, type Schemas } from "@starterapp/api-client";

export const identity: Middleware = {
  onRequest({ request }) {
    const token = accessToken();
    if (token && !request.headers.has("Authorization")) request.headers.set("Authorization", `Bearer ${token}`);
    return request;
  },
  onResponse({ response }) {
    if (response.status === 401) signOut();
    if (response.status === 403) stepUp(response.headers.get("WWW-Authenticate"));
    return response;
  },
};

export const api = createApi("");
api.use(identity);
