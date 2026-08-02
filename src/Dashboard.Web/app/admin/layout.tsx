"use client";

import type { ReactNode } from "react";
import { AccessDenied, isAdmin, useSession } from "@/components/AppShell";

/** One guard for every administrator page underneath. */
export default function AdminLayout({ children }: { children: ReactNode }) {
  const session = useSession();
  if (!isAdmin(session)) return <AccessDenied what="administrator pages" />;
  return <>{children}</>;
}
