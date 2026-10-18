import Link from "next/link";

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

export default async function ShopPage() {
  const result = await getProducts();

  return (
    <main>
      <header className="container" style={{ paddingBlock: 20 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <Link href="/" style={{ fontSize: "1.2rem", fontWeight: "bold" }}>
            یا علی
          </Link>
          <nav style={{ display: "flex", gap: 16, fontSize: "0.95rem" }}>
            <Link href="/about">درباره ما</Link>
            <Link href="/contact">تماس با ما</Link>
            <Link href="/app">ورود به پنل</Link>
          </nav>
        </div>
      </header>

      <section className="container" style={{ paddingBlock: "48px 80px" }}>
        <h1 style={{ fontSize: "2rem", marginBottom: 8 }}>فروشگاه</h1>
        <p style={{ color: "var(--ink-soft)", marginTop: 0 }}>
          تجهیزات پزشکی با کیفیت، از تأمین‌کنندگان معتبر.
        </p>

        <div style={{ marginTop: 32 }}>
          {result.ok ? (
            result.products.length === 0 ? (
              <div className="card" style={{ color: "var(--ink-soft)" }}>
                محصولی موجود نیست.
              </div>
            ) : (
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fill, minmax(260px, 1fr))",
                  gap: 20,
                }}
              >
                {result.products.map((p) => (
                  <div key={p.id} className="card" style={{ display: "flex", flexDirection: "column" }}>
                    <div
                      style={{
                        width: "100%",
                        height: 120,
                        borderRadius: 8,
                        background: "var(--surface-alt)",
                        marginBottom: 12,
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "center",
                        color: "var(--ink-soft)",
                        fontSize: "0.85rem",
                      }}
                    >
                      تصویر محصول
                    </div>
                    <h3 style={{ fontSize: "1.05rem", marginBlock: 4 }}>{p.name}</h3>
                    <div className="mono" style={{ fontSize: "0.78rem", color: "var(--ink-soft)" }}>
                      {p.slug}
                    </div>
                    {p.requiresPrescription && (
                      <span
                        className="tag"
                        style={{ fontSize: "0.75rem", marginTop: 8, alignSelf: "flex-start" }}
                      >
                        نسخه‌دار
                      </span>
                    )}
                    <div
                      style={{
                        marginTop: 16,
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                      }}
                    >
                      <span style={{ color: "var(--teal)", fontWeight: 600 }}>استعلام قیمت</span>
                      <button className="btn btn-ghost" style={{ padding: "6px 14px", fontSize: "0.85rem" }}>
                        افزودن به سبد
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )
          ) : result.reason === "unreachable" ? (
            <div className="card" style={{ color: "var(--ink-soft)" }}>
              فروشگاه در دسترس نیست. استک را بالا بیاور:
              <pre className="mono" style={{ marginTop: 12, fontSize: "0.85rem" }}>
                {".\\build.ps1 -Up"}
              </pre>
            </div>
          ) : (
            <div className="card" style={{ color: "var(--pulse)" }}>
              خطا در دریافت محصولات. لاگ کانتینر را ببین:
              <pre className="mono" style={{ marginTop: 12, fontSize: "0.85rem" }}>
                {"docker logs hc-api"}
              </pre>
            </div>
          )}
        </div>
      </section>
    </main>
  );
}
