import type { SessionResponse } from "./generated";
export async function login(username: string, password: string): Promise<SessionResponse> {
  const csrf = await fetch("/api/auth/csrf", { credentials: "include" }).then((r) => r.json());
  const response = await fetch("/api/auth/login", {
    method: "POST",
    headers: { "content-type": "application/json", "X-CSRF-TOKEN": csrf.token },
    credentials: "include",
    body: JSON.stringify({ username, password }),
  });
  if (!response.ok) throw new Error("Sign in failed.");
  return response.json();
}
