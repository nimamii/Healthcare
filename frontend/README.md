# یا علی — فرانت‌اند

Next.js (App Router) — هم سایت بازاریابی، هم پنل وب‌اپ.

## اجرا

```powershell
cd E:\Healthcare\frontend
npm install
npm run dev
```

- سایت: <http://localhost:3000>
- پنل: <http://localhost:3000/app>

پنل به API بک‌اند (پورت 8090) وصل می‌شود. اگر استک بالا نیامده، پنل پیام می‌دهد و
صفحه سایت همچنان کار می‌کند.

## ساختار

```
app/
├─ layout.tsx      ← RTL، فونت Vazirmatن
├─ globals.css     ← توکن‌های طراحی (ink/paper/teal/amber/pulse)
├─ page.tsx        ← سایت فرود
└─ app/page.tsx    ← داشبورد نمایشی
```

## قواعد طراحی

- رنگ‌ها فقط از طریق CSS variables در `globals.css`.
- فونت فارسی: Vazirmatن با `@import` در CSS (نه `next/font`).
- اعداد و کدها: `font-family: monospace` با کلاس `.mono`.
- همه‌چیز RTL و فارسی.
