import type { NextConfig } from "next";

// ponytail: dev-only proxy, replacing the old Vite one. The browser keeps calling
// same-origin /api so the auth cookie and antiforgery token stay first-party.
// Production serves this UI behind the API, where no rewrite is needed.
const apiOrigin = process.env.API_ORIGIN ?? "http://localhost:5000";

const nextConfig: NextConfig = {
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiOrigin}/api/:path*` }];
  },
};

export default nextConfig;
