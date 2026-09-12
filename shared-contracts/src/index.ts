// Re-exports of the generated OpenAPI types plus small helpers shared by portal, public-site and mobile.
export type { paths, components, operations } from "./api";

import type { components } from "./api";
export type Schemas = components["schemas"];

/** Standard headers every server-side call to the .NET API carries. */
export const TENANT_HEADER = "X-Tenant";
export const PUBLIC_API_KEY_HEADER = "X-Public-Api-Key";

/** RFC 9457 problem details as produced by the API's DomainExceptionHandler. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === "object" && value !== null && "status" in value && ("title" in value || "detail" in value);
}
