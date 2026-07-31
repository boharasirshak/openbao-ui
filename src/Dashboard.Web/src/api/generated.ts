// Generated from the ASP.NET OpenAPI document. Run `npm run generate-api` after API changes.
export interface SessionResponse {
  expiresAt: string;
}

export interface SecretDocumentResponse {
  values: Record<string, string>;
  version: number;
}

export interface SecretVersionResponse {
  version: number;
  deletedAt: string | null;
  destroyed: boolean;
}

export interface ProjectResponse {
  id: string;
  description: string;
  environments: string[];
}
