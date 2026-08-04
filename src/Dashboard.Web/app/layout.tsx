import type { Metadata } from "next";
import { Inter } from "next/font/google";
import InitColorSchemeScript from "@mui/material/InitColorSchemeScript";
import Providers from "./providers";
import AppShell from "@/components/AppShell";
import "./globals.css";

// Self-hosted, so no request to a third party on first paint. Mono stays the native
// stack — one download instead of two, and mono metrics barely differ across platforms.
const inter = Inter({ subsets: ["latin"], display: "swap", variable: "--font-sans" });

export const metadata: Metadata = {
  title: "OpenBao Secrets",
  description: "Internal OpenBao developer secrets dashboard",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" className={inter.variable} suppressHydrationWarning>
      <body>
        {/*
          Must be the first child of body, and `attribute` must equal the theme's
          colorSchemeSelector. It runs before paint, reads the stored preference or
          the OS setting, and sets the attribute — so neither scheme flashes.
        */}
        <InitColorSchemeScript attribute="data-color-scheme" defaultMode="system" />
        <Providers>
          <AppShell>{children}</AppShell>
        </Providers>
      </body>
    </html>
  );
}
