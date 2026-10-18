# معماری سیستم

سند مرجع: `MASTER_PROMPT___HEALTHCARE_MARKETPLACE.md`
وضعیت: نسخه ۰.۱ — مربوط به Phase 1 (Foundation)

---

## ۱. سبک معماری

**Modular Monolith.**

چرا، نه میکروسرویس: تیم سه نفره است (§37) و پیچیدگی عملیاتی میکروسرویس (شبکه، deployment جدا، ردیابی توزیع‌شده، سرویس‌کشفی) زمان تیم را می‌بلعد بدون آنکه برای MVP ارزشی بیفزاید. یک deployable، یک دیتابیس، مرزهای داخلی صریح.

سند مرجع هم صریحاً همین را می‌گوید (§13) و هم تأکید می‌کند که مرزها باید واقعی باشند.

---

## ۲. مرزبندی ماژول‌ها

هر ماژول یک **پروژه C# مستقل** است. این یک تصمیم آگاهانه است:

**مزیت اصلی:** کامپایلر مرزها را enforce می‌کند. اگر ماژول Catalog بخواهد مستقیم به کلاس داخلی Inventory دسترسی بگیرد، پروژه build نمی‌شود. در مدل تک‌پروژه‌ای، این فقط یک قرارداد شفاهی است و به‌مرور زمان می‌شکند.

**هزینه:** برای ۱۶ ماژول، تعداد پروژه‌ها بالا می‌رود. این هزینه در فاز ۱ کم است چون طبق قانون MVP FIRST (§30) فقط ماژول‌های لازم ساخته می‌شوند.

### ساختار

```
src/
├─ Healthcare.Api/                    host، composition root، DI
├─ BuildingBlocks/
│  ├─ SharedKernel/                   Result، DomainException، IModule
│  └─ Contracts/                      interfaceهای بین‌ماژولی
│     ├─ Identity.Contracts/
│     ├─ Catalog.Contracts/
│     └─ ...
└─ Modules/
   ├─ Identity/
   │  ├─ Identity.Domain/
   │  ├─ Identity.Application/
   │  ├─ Identity.Infrastructure/
   │  └─ Identity.Api/
   └─ ...
```

### ۱۶ ماژول

Identity, Users, Customers, Catalog, Suppliers, Pharmacies, Inventory, Orders, Payments, Prescriptions, Fulfillment, Geography, Logistics, Notifications, Administration, Audit

### قاعده وابستگی

```
Domain  →  هیچ‌چیز (نه Contracts، نه Infrastructure)
Application  →  Domain + Contracts ماژول‌های دیگر
Infrastructure  →  Application + Domain
Api  →  Application
```

جهت وابستگی هرگز برنمی‌گردد. `Domain` تنها لایه‌ای است که هیچ وابستگی بیرونی ندارد.

### ارتباط بین ماژول‌ها

فقط از راه `Contracts` — پروژه‌های interface خالی. مثال:

```csharp
// Catalog.Contracts  — ماژول Catalog این را اعلام می‌کند
public interface IProductCatalog
{
    Task<ProductSummary?> FindAsync(string sku, CancellationToken ct);
}
```

ماژول Inventory به `Catalog.Contracts` **ارجاع** می‌کند و `Catalog` را **ارجاع نمی‌کند**. پیاده‌سازی در composition root تزریق می‌شود.

---

## ۳. مرز دیتابیس

هر ماژول **schema جدا** در PostgreSQL دارد.

```sql
CREATE SCHEMA identity;
CREATE SCHEMA catalog;
CREATE SCHEMA inventory;
```

| ماژول | schema | مالک جدول |
|---|---|---|
| Identity | `identity` | users, roles, refresh_tokens |
| Catalog | `catalog` | categories, products, skus |
| Inventory | `inventory` | stock_items, reservations |

چرا schema جدا، نه پیشوند در `public`: مالکیت جدول با schema جدا **واقعاً** جداست. یک ماژول می‌تواند با GRANT محدود شود که به schema دیگر دسترسی ندارد. با پیشوند، این فقط یک قرارداد نانوشته است.

هزینه: مهاجرت‌ها باید جدا مدیریت شوند (dbup یا Flyway، نه `dotnet ef` خودکار). این هزینه پذیرفته شده است.

**قید: هیچ ماژولی حق Join مستقیم بین دو schema را ندارد.** اگر داده لازم است، از Service Layer ماژول صاحب داده پرسیده می‌شود. §13 سند مرجع: "Do not allow random cross-module database access."

---

## ۴. جریان داده — نمونه: ثبت سفارش

```
Customer (Next.js)
   │  POST /api/orders
   ▼
Orders.Api ──► Orders.Application
   │
   ├─► Inventory.Contracts  ──► رزرو موجودی (transaction + row lock)
   │                              ◄── تأیید رزرو
   ├─► Fulfillment.Contracts ──► رتبه‌بندی تأمین‌کننده
   │                              (موجودی + واجد شرایط جغرافیایی + فاصله/هزینه)
   │                              ◄── تأمین‌کننده + هزینه ارسال + خارج از محدوده؟
   ├─► Geography.Contracts  ──► محاسبه فاصله و هزینه اضافه
   │
   └─► Orders.Domain       ──► Order state machine
                                  CREATED → PAYMENT_PENDING
```

اگر مشتری خارج از محدوده خدمات باشد: هزینه اضافه محاسبه و **نمایش** می‌شود، سپس **تأیید مشتری** لازم است، و فقط بعد ادامه می‌یابد (§1). مشتری به‌خاطر خارج بودن از محدوده، بلاک نمی‌شود.

---

## ۵. مرزهای امنیتی

| قاعده | منبع |
|---|---|
| Authorization همیشه سمت سرور. فرانت‌اند هرگز منبع اعتماد نیست. | §15 |
| هرگز سفارش را فقط بر اساس پاسخ موفق کلاینت «پرداخت‌شده» علامت نزن | §26 |
| callback پرداخت باید idempotent باشد | §26 |
| فایل نسخه هرگز از URL عمومی سرو نمی‌شود | §25 |
| هیچ secret، connection string یا توکن در کد hard-code نشود | §24 |
| خطا به کاربر: بدون stack trace، خطای دیتابیس، یا اطلاعات زیرساخت | §22 |

### مناطق پرریسک (§39)

Payment, Prescription, Inventory, Fulfillment, Logistics, Authentication

هر کد AI در این مناطق **قبل از Production نیازمند Code Review انسانی است.**

---

## ۶. استراتژی یکپارچه‌سازی

### موجودی تأمین‌کننده (§5)

```
سیستم خارجی تأمین‌کننده
   → Integration Adapter
   → Data Normalization
   → مدل داخلی Inventory
   → Fulfillment Engine
```

اولویت: REST API → Webhook → Sync زمان‌بندی‌شده → CSV/Excel

همه به یک مدل داخلی نرمال می‌شوند: `SupplierId, ProductId, SKU, AvailableQuantity, ReservedQuantity, Price, Status, LastUpdatedAt, SourceSystem`

منطق کسب‌وکار **هرگز** مستقیم به ERP تأمین‌کننده couple نمی‌شود.

### پرداخت (§26) و لجستیک (§11)

هر دو پشت یک interface انتزاعی + adapter + mock provider.

```csharp
public interface IPaymentGateway
{
    Task<PaymentResult> InitiateAsync(PaymentRequest request, CancellationToken ct);
    Task<PaymentResult> VerifyAsync(string reference, CancellationToken ct);
    Task<RefundResult> RefundAsync(string reference, CancellationToken ct);
}
```

توسعه نباید به‌خاطر در دسترس نبودن API خارجی متوقف شود. mock provider از روز اول وجود دارد.

---

## ۷. استقرار

```
                    ┌─────────────┐
   Browser ────────►│    Nginx    │
                    │  (TLS)      │
                    └──────┬──────┘
                           │
                    ┌──────▼──────┐
                    │  Healthcare │  ASP.NET Core
                    │     .Api    │
                    └──────┬──────┘
                           │
          ┌────────────────┼────────────────┐
          │                │                │
   ┌──────▼──────┐  ┌──────▼──────┐  ┌──────▼──────┐
   │ PostgreSQL  │  │    Redis    │  │  S3 (فاز ۶) │
   │  + PostGIS  │  │             │  │   (dev)     │
   └─────────────┘  └─────────────┘  └─────────────┘
```

محیط‌ها: Development / Staging / Production — جدا. هرگز credential تولید در توسعه (§33).

Kubernetes در MVP لازم نیست (§12).

---

## ۸. فرض‌های ثبت‌شده

طبق قانون NEVER ASSUME (§29)، این‌ها را صریح ثبت می‌کنم:

| # | فرض | چرا | تغییر بعدی |
|---|---|---|---|
| 1 | .NET 8 LTS | پایدار، پشتیبانی بلندمدت | تغییر به .NET 9/10 بی‌هزینه است |
| 2 | مهاجرت با Flyway | EF Core برای schema جدای ماژول‌ها auto-migration ندارد | به dbup تغییر‌پذیر است |
| 3 | pgvector استفاده نمی‌شود | در سند به آن نیازی نشده | اگر جستجوی معنایی لازم شد |
| 4 | RabbitMQ در فاز ۱ راه‌اندازی نمی‌شود | §12: «Do not turn every operation into an event» | فاز ۷ (Logistics) |
| 5 | S3 در فاز ۶ اضافه می‌شود | هیچ کدی فعلاً object storage نمی‌خواند یا نمی‌نویسد | افزودن سرویس در فازی که واقعاً لازمش دارد |

هر فرضی که در طول کار رد شد، به این جدول اضافه یا اصلاح می‌شود.

---

## ۹. چه چیزی هنوز تصمیم‌گرفته نشده

مواردی که عمداً به تعویق افتاده‌اند تا داده واقعی داشته باشیم:

- **الگوریتم رتبه‌بندی Fulfillment** — §8 می‌گوید برای MVP ساده نگه دار. عوامل آینده (SLA، نرخ تحقق تاریخی) بدون داده تاریخی بی‌معنی‌اند.
- **ساختار ServiceArea** — Polygon در برابر چندضلعی ساده. به شکل واقعی محدوده‌ها بستگی دارد.
- **نرخ مالیات و مرجوعی** — سند مرجع درباره حسابداری چیزی نمی‌گوید.
- **مدیریت اسکالا** — چندمقیاسی یا تک‌مقیاسی.
