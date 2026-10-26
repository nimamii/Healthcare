import Link from "next/link";
import DashboardClient from "./DashboardClient";
import AdminPanelClient from "./AdminPanelClient";

type Product = {
  id: string;
  name: string;
  slug: string;
  categoryId: string;
  requiresPrescription: boolean;
  isActive: boolean;
};

type FetchResult =
  | { ok: true; products: Product[] }
  | { ok: false; reason: "unreachable" | "error" };

async function getProducts(): Promise<FetchResult> {
  try {
    const res = await fetch(
      `${process.env.API_URL ?? "http://localhost:8090"}/api/catalog/products`,
      { cache: "no-store" },
    );
    if (!res.ok) return { ok: false, reason: "error" };
    return { ok: true, products: (await res.json()) as Product[] };
  } catch {
    return { ok: false, reason: "unreachable" };
  }
}

export default async function DashboardPage() {
  const result = await getProducts();

  return (
    <main style={{ padding: 24 }}>
      <div
        className="container"
        style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}
      >
        <div>
          <Link href="/" style={{ color: "var(--primary)", fontSize: "0.9rem", fontWeight: 600 }}>
            بازگشت به سایت
          </Link>
          <h1 style={{ fontSize: "1.6rem", marginTop: 4 }}>داشبورد کاربری و وضعیت سامانه</h1>
        </div>
        <span className="tag">پنل کاربری</span>
      </div>

      <section className="container" style={{ marginTop: 24 }}>
        <DashboardClient />
        <AdminPanelClient />

        <h2 style={{ fontSize: "1.15rem", marginBottom: 12 }}>کاتالوگ زنده بک‌اند</h2>
        {result.ok ? (
          result.products.length === 0 ? (
            <div className="card" style={{ color: "var(--ink-soft)" }}>
              کاتالوگ خالی است. API پاسخ می‌دهد ولی محصولی ثبت نشده است.
            </div>
          ) : (
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fill, minmax(240px, 1fr))",
                gap: 16,
              }}
            >
              {result.products.map((p) => (
                <div key={p.id} className="card">
                  <div className="mono" style={{ fontSize: "0.8rem", color: "var(--ink-soft)" }}>
                    {p.slug}
                  </div>
                  <h3 style={{ fontSize: "1rem", marginBlock: 6 }}>{p.name}</h3>
                  {p.requiresPrescription && (
                    <span className="tag" style={{ fontSize: "0.75rem" }}>
                      نسخه‌دار
                    </span>
                  )}
                </div>
              ))}
            </div>
          )
        ) : result.reason === "unreachable" ? (
          <div className="card" style={{ color: "var(--ink-soft)" }}>
            API بک‌اند در حال حاضر اجرا نشده است. برای اتصال زنده، کانتینرها را بالا بیاورید:
            <pre className="mono" style={{ marginTop: 12, fontSize: "0.85rem" }}>
              {"docker compose up -d"}
            </pre>
          </div>
        ) : (
          <div className="card" style={{ color: "var(--pulse)" }}>
            API پاسخ با خطا داد. لاگ کانتینر را ببین:
            <pre className="mono" style={{ marginTop: 12, fontSize: "0.85rem" }}>
              {"docker logs hc-api"}
            </pre>
          </div>
        )}
      </section>
    </main>
  );
}
