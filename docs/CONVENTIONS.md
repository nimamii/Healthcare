# قراردادهای توسعه

این سند قوانینی است که کد (انسان یا AI) باید رعایت کند.

---

## ۱. ساختار پروژه

```
src/
├─ Healthcare.Api/
├─ BuildingBlocks/
│  ├─ SharedKernel/
│  └─ Contracts/          پروژه به‌ازای هر ماژول، فقط interface
└─ Modules/
   └─ {Module}/
      ├─ {Module}.Domain/
      ├─ {Module}.Application/
      ├─ {Module}.Infrastructure/
      └─ {Module}.Api/
```

قاعده وابستگی — هرگز برنمی‌گردد:

```
Domain  →  هیچ
Application  →  Domain + Contracts
Infrastructure  →  Application
Api  →  Application
```

---

## ۲. مرز ماژول

**مجاز:** ماژول A به `B.Contracts` ارجاع می‌دهد و interface را صدا می‌زند.

**ممنوع:**
- `using B.Domain` یا `using B.Application` از ماژول A
- Join مستقیم بین دو schema
- دسترسی به `DbContext` ماژول دیگر
- ثبت مستقیم implementation یک ماژول در ماژول دیگر

پیاده‌سازی interfaceها فقط در **composition root** (`Healthcare.Api`) تزریق می‌شود.

---

## ۳. نام‌گذاری

| چیز | قرارداد | نمونه |
|---|---|---|
| ماژول | PascalCase | `Inventory` |
| پروژه | `{Module}.{Layer}` | `Inventory.Domain` |
| جدول | snake_case، جمع | `inventory.stock_items` |
| ستون | snake_case | `available_quantity` |
| اینترفیس `I` + Pascal | | `IProductCatalog` |
| DTO پایانی `Response` | | `ProductResponse` |
| Command | فعل + اسم | `ReserveStockCommand` |

---

## ۴. API

- REST، JSON naming در camelCase
- قرارداد فقط با **DTO** (§21) — ساختار دیتابیس هرگز مستقیم expose نمی‌شود
- پاسخ خطا یک شکل ثابت دارد
- pagination، filter، sort یکدست
- OpenAPI/Swagger

### شکل خطا

```json
{
  "type": "https://errors.example.com/validation",
  "title": "یک یا چند خطای اعتبارسنجی رخ داد",
  "status": 400,
  "correlationId": "01JC8X...",
  "errors": { "email": ["ایمیل نامعتبر است"] }
}
```

`correlationId` برای ردیابی (§22). **هرگز** stack trace یا خطای دیتابیس به کاربر نشان داده نمی‌شود.

---

## ۵. دیتابیس

- EF Core، **بدون** auto-migration در محیط‌های مشترک
- migration با Flyway
- هر ماژول schema خودش
- FK بین ماژول‌ها **ممنوع** — به‌جایش شناسه بدون قید، و داده از Contracts خوانده می‌شود
- هر جدول ستون `created_at` / `updated_at` دارد

### چرا FK بین ماژولی ممنوع است

FK بین دو schema یعنی پیوند فیزیکی بین ماژول‌ها. `Inventory` می‌خواهد جدول `catalog.products` را ارجاع دهد. اگر بعداً محصول حذف یا مهاجرت کند، Inventory شکسته می‌شود — و این دقیقاً همان چیزی است که §13 منع می‌کند.

به‌جایش: `product_id` به‌صورت UUID بدون قید، و داده از `Catalog.Contracts` خوانده می‌شود.

---

## ۶. امنیت

- Authorization همیشه سمت سرور (§15)
- پیش‌فرض: **بسته**. هر endpoint صریحاً `[Authorize]` دارد
- هیچ secret در کد. فقط env var (§24)
- `[Authorize]` در frontend هیچ معنایی ندارد
- validation همیشه سمت سرور

---

## ۷. تست

| نوع | چه چیزی |
|---|---|
| Unit | قواعد دامنه — state machine، رزرو موجودی، محاسبه فاصله، قیمت‌گذاری |
| Integration | دیتابیس، API، auth، جریان پرداخت |
| E2E | مسیر کامل: مشتری → سبد → پرداخت → سفارش |

**مناطق پرریسک** (§39) تست واحد اجباری دارند — بدون استثناء:

Payment · Prescription · Inventory · Fulfillment · Logistics · Authentication

---

## ۸. قوانین AI Agent

این بخش برای AI agent است که روی این مخزن کار می‌کند.

### NEVER ASSUME (§29)

ابهام را مخفی نکن. به‌جای ساختن بی‌صدای یک قاعده کسب‌وکار:

1. ابهام را مشخص کن
2. اگر باید ادامه دادی، **امن‌ترین فرض موقت** را بگیر
3. آن را **مستند کن** (به جدول فرض‌ها در `ARCHITECTURE.md` اضافه کن)
4. کد را طوری بنویس که تغییر بعدی آسان باشد

### MVP FIRST (§30)

قبل از ساختن هر چیز بپرس: «برای اولین سفارش واقعی موفق لازم است؟» اگر نه، نساز.

### چه چیزی نساز

abstraction بدون provider واقعی · میکروسرویس · زیرساخت غیرضروری · قابلیت speculative · بهینه‌سازی پیچیده · pattern اضافه

### قبل از commit (§35)

تست پاس شود · diff بازبینی شود · secret چک شود · فایل ناخواسته چک شود · format چک شود

### محدوده

تغییر غیرمربوط نکن. کد کارکردی را بی‌دلیل بازنویسی نکن.
