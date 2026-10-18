import Link from "next/link";

export default function AboutPage() {
  return (
    <main>
      <header className="container" style={{ paddingBlock: 20 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <Link href="/" style={{ fontSize: "1.2rem", fontWeight: "bold" }}>
            یا علی
          </Link>
          <nav style={{ display: "flex", gap: 16, fontSize: "0.95rem" }}>
            <Link href="/shop">فروشگاه</Link>
            <Link href="/contact">تماس با ما</Link>
            <Link href="/app">ورود به پنل</Link>
          </nav>
        </div>
      </header>

      <section className="container" style={{ paddingBlock: "48px 80px", maxWidth: 720 }}>
        <h1 style={{ fontSize: "2rem", marginBottom: 16 }}>درباره یا علی</h1>
        <p style={{ color: "var(--ink-soft)", fontSize: "1.05rem", lineHeight: 1.9 }}>
          «یا علی» یک مارکت‌پلیس دوطرفه در حوزه تجهیزات پزشکی است. هدف ما ایجاد بازاری
          است که تأمین‌کنندگان تجهیزات پزشکی و خریداران (بیمارستان‌ها، کلینیک‌ها،
          آزمایشگاه‌ها) بتوانند مستقیماً و با شفافیت کامل معامله کنند.
        </p>

        <h2 style={{ fontSize: "1.3rem", marginTop: 40, marginBottom: 12 }}>چرا یا علی؟</h2>
        <div style={{ display: "grid", gap: 16 }}>
          <div className="card">
            <h3 style={{ fontSize: "1.05rem", marginBottom: 6 }}>همگام‌سازی لحظه‌ای موجودی</h3>
            <p style={{ color: "var(--ink-soft)", margin: 0, fontSize: "0.95rem" }}>
              تأمین‌کننده موجودی را در پنل ثبت می‌کند یا ERP خودش را وصل می‌کند؛ در هر دو
              حالت، موجودی لحظه‌ای در بازار منعکس می‌شود.
            </p>
          </div>
          <div className="card">
            <h3 style={{ fontSize: "1.05rem", marginBottom: 6 }}>محدوده خدمات هوشمند</h3>
            <p style={{ color: "var(--ink-soft)", margin: 0, fontSize: "0.95rem" }}>
              با PostGIS، محدوده خدمات هر تأمین‌کننده روی نقشه تعریف می‌شود. آدرس خارج از
              محدوده مشتری را بلاک نمی‌کند، هزینه اضافه را پرسش می‌کند.
            </p>
          </div>
          <div className="card">
            <h3 style={{ fontSize: "1.05rem", marginBottom: 6 }}>امنیت و اعتماد</h3>
            <p style={{ color: "var(--ink-soft)", margin: 0, fontSize: "0.95rem" }}>
              احراز هویت و مجوزها همیشه سمت سرور تصمیم می‌گیرد. فرانت‌اند هرگز منبع اعتماد
              نیست.
            </p>
          </div>
        </div>

        <h2 style={{ fontSize: "1.3rem", marginTop: 40, marginBottom: 12 }}>تکنولوژی</h2>
        <p style={{ color: "var(--ink-soft)", fontSize: "0.95rem" }}>
          بک‌اند: ASP.NET Core (C#) با معماری Modular Monolith
          <br />
          دیتابیس: PostgreSQL + PostGIS
          <br />
          فرانت‌اند: Next.js + TypeScript + Tailwind
          <br />
          زیرساخت: Docker Compose (بدون Kubernetes)
        </p>
      </section>
    </main>
  );
}
