import Link from "next/link";

const stats = [
  { value: "۱۶۰+", label: "تست خودکار پاس‌شده" },
  { value: "۴", label: "ماژول فعال بک‌اند" },
  { value: "لحظه‌ای", label: "همگام‌سازی موجودی" },
];

const modules = [
  {
    name: "کاتالوگ",
    desc: "محصولات، SKU و دسته‌بندی‌ها؛ منبع واحد حقیقت برای هر چیزی که فروخته می‌شود.",
  },
  {
    name: "موجودی",
    desc: "موجودی لحظه‌ای تأمین‌کننده، با رزرو و کنترل هم‌زمانی برای جلوگیری از فروش بیش از موجودی.",
  },
  {
    name: "هویت",
    desc: "احراز هویت و نقش‌ها؛ دسترسی همیشه سمت سرور تصمیم می‌گیرد.",
  },
  {
    name: "جغرافیا",
    desc: "محدوده خدمات با PostGIS؛ آدرس خارج از محدوده مشتری را بلاک نمی‌کند، هزینه اضافه را پرسش می‌کند.",
  },
];

export default function Home() {
  return (
    <main>
      <header className="container" style={{ paddingBlock: 20 }}>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
          }}
        >
          <strong style={{ fontSize: "1.2rem" }}>یا علی</strong>
          <nav style={{ display: "flex", gap: 16, fontSize: "0.95rem" }}>
            <Link href="#modules">ماژول‌ها</Link>
            <Link href="/app">ورود به پنل</Link>
          </nav>
        </div>
      </header>

      <section className="container" style={{ paddingBlock: "72px 48px" }}>
        <span className="tag">مارکت‌پلیس دوطرفه حوزه سلامت</span>
        <h1 style={{ fontSize: "2.6rem", marginBlock: "20px 12px", maxWidth: 720 }}>
          تجهیزات پزشکی، از تأمین‌کننده تا خریدار؛ موجودی لحظه‌ای.
        </h1>
        <p style={{ color: "var(--ink-soft)", maxWidth: 620, fontSize: "1.1rem" }}>
          تأمین‌کننده موجودی را در پنل ثبت می‌کند یا ERP خودش را وصل می‌کند؛ در هر دو
          حالت، موجودی لحظه‌ای در بازار منعکس می‌شود.
        </p>
        <div style={{ display: "flex", gap: 12, marginTop: 28 }}>
          <Link href="/app" className="btn btn-primary">
            ورود به پنل
          </Link>
          <a href="#modules" className="btn btn-ghost">
            دیدن ماژول‌ها
          </a>
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
            gap: 16,
            marginTop: 56,
          }}
        >
          {stats.map((s) => (
            <div key={s.label} className="card" style={{ textAlign: "center" }}>
              <div className="mono" style={{ fontSize: "1.6rem", color: "var(--teal)" }}>
                {s.value}
              </div>
              <div style={{ color: "var(--ink-soft)", fontSize: "0.9rem" }}>{s.label}</div>
            </div>
          ))}
        </div>
      </section>

      <section id="modules" className="container" style={{ paddingBlock: "48px 80px" }}>
        <h2 style={{ fontSize: "1.6rem", marginBottom: 8 }}>ماژول‌های فعلی</h2>
        <p style={{ color: "var(--ink-soft)", marginTop: 0 }}>
          معماری Modular Monolith؛ هر ماژول مالک داده خودش است.
        </p>
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
            gap: 16,
            marginTop: 24,
          }}
        >
          {modules.map((m) => (
            <div key={m.name} className="card">
              <h3 style={{ fontSize: "1.1rem", marginBottom: 8 }}>{m.name}</h3>
              <p style={{ color: "var(--ink-soft)", fontSize: "0.92rem", margin: 0 }}>
                {m.desc}
              </p>
            </div>
          ))}
        </div>
      </section>

      <footer
        className="container"
        style={{ paddingBlock: 32, borderTop: "1px solid var(--line)" }}
      >
        <p style={{ color: "var(--ink-soft)", fontSize: "0.85rem", margin: 0 }}>
          یا علی — ساخته‌شده با معماری Modular Monolith، ASP.NET Core و PostgreSQL/PostGIS.
        </p>
      </footer>
    </main>
  );
}
