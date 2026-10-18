import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // The API runs in Docker on the host; the browser reaches it there.
  // In dev, Next proxies /api to the container so the app and the site
  // share one origin and no CORS configuration is needed.
  async rewrites() {
    const api = process.env.API_URL ?? "http://localhost:8090";
    return [{ source: "/api/:path*", destination: `${api}/api/:path*` }];
  },
};

export default nextConfig;
