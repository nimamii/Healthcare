import Link from "next/link";

export default function ContactPage() {
  return (
    <main>
      <header className="container" style={{ paddingBlock: 20 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <Link href="/" style={{ fontSize: "1.2rem", fontWeight: "bold" }}>
            یا علی
          </Link>
          <nav style={{ display: "flex", gap: 16, fontSize: "0.95rem" }}>
            <Link href="/about">درباره ما</Link>
            <Link href="/shop">فروشگاه</Link>
            <Link href="/app">ورود به پنل</Link>
          </nav>
        </div>
      </header>

      <section className="container" style={{ paddingBlock: "48px 80px", maxWidth: 720 }}>
        <h1 style={{ fontSize: "2rem", marginBottom: 16 }}>تماس با ما</h1>
        <p style={{ color: "var(--ink-soft)", fontSize: "1.05rem" }}>
          سوالی دارید؟ پیام بگذارید، تیم ما در اسرع وقت پاسخ می‌دهد.
        </p>

        <div style={{ display: "grid", gap: 16, marginTop: 32 }}>
          <div className="card">
            <h3 style={{ fontSize: "1rem", marginBottom: 6 }}>ایمیل</h3>
            <p style={{ color: "var(--ink-soft)", margin: 0, fontSize: "0.95rem" }}>
              info@ya-ali.example
            </p>
          </div>
          <div className="card">
            <h3 style={{ fontSize: "1rem", marginBottom: 6 }}>تلفن</h3>
            <p style={{ color: "var(--ink-soft)", margin: 0, fontSize: "0.95rem" }}>
              ۰۲۱-۱۲۳۴۵۶۷۸
            </p>
          </div>
          <div className="card">
            <h3 style={{ fontSize: "1rem", marginBottom: 6 }}>آدرس</h3>
            <p style={{ color: "var(--ink-soft)", margin: 0, fontSize: "0.95rem" }}>
              تهران، خیابان ولیعصر، پلاک ۱۲۳
            </p>
          </div>
        </div>
      </section>
    </main>
  );
}
