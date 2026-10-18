# طراحی دیتابیس

سند مرجع: `MASTER_PROMPT___HEALTHCARE_MARKETPLACE.md` §14

---

## اصل حاکم: مالکیت schema

هر ماژول schema جدا دارد و **تنها مالک** آن است.

| ماژول | schema | وضعیت |
|---|---|---|
| Identity | `identity` | ✅ ساخته شده |
| Users | `users` | ⬜ فاز ۱ |
| Catalog | `catalog` | ⬜ فاز ۲ |
| Suppliers | `suppliers` | ⬜ فاز ۳ |
| Inventory | `inventory` | ⬜ فاز ۳ |
| Geography | `geography` | ⬜ فاز ۴ |
| Orders | `orders` | ⬜ فاز ۵ |
| Payments | `payments` | ⬜ فاز ۵ |
| Logistics | `logistics` | ⬜ فاز ۷ |
| Audit | `audit` | ⬜ فاز ۱ |
| Notifications | `notifications` | ⬜ |
| Administration | — | بدون جدول |
| Customers | `customers` | ⬜ فاز ۱ |
| Pharmacies | `pharmacies` | ⬜ فاز ۶ |
| Prescriptions | `prescriptions` | ⬜ فاز ۶ |
| Fulfillment | `fulfillment` | ⬜ فاز ۴ |

> Pharmacy و Prescription **جدول طراحی می‌شوند ولی پیاده‌سازی نمی‌شوند** — فاز ۶ عمداً تعویق افتاده. مدل چندداروخانه‌ای از روز اول در نظر گرفته می‌شود تا مهاجرت لازم نشود (§3).

---

## قاعده: بدون FK بین‌ماژولی

```sql
-- ❌ ممنوع
ALTER TABLE inventory.stock_items
  ADD CONSTRAINT fk_product FOREIGN KEY (product_id)
  REFERENCES catalog.products(id);

-- ✅ درست: شناسه بدون قید
CREATE TABLE inventory.stock_items (
  product_id uuid NOT NULL,     -- بدون REFERENCES
  ...
);
```

**چرا:** FK بین دو schema یعنی پیوند فیزیکی. اگر `catalog.products` حذف یا مهاجرت کند، `inventory` می‌شکند — و این دقیقاً همان چیزی است که §13 («Do not allow random cross-module database access») منع می‌کند.

به‌جایش داده از Service Layer خوانده می‌شود:

```csharp
public interface IProductCatalog
{
    Task<ProductSummary?> FindAsync(Guid productId, CancellationToken ct);
}
```

هزینه: یک join در برابر یک تماس شبکه‌ای درون‌فرایندی. برای MVP قابل قبول است، و در عوض مرز واقعاً enforce می‌شود.

---

## schema هویت (فاز ۱)

```sql
CREATE SCHEMA identity;

CREATE TABLE identity.users (
    id                  uuid PRIMARY KEY,
    email               varchar(320) NOT NULL,
    password_hash       varchar(512) NOT NULL,
    is_active           boolean      NOT NULL DEFAULT true,
    created_at          timestamptz  NOT NULL,
    updated_at          timestamptz  NOT NULL,
    deactivated_at      timestamptz  NULL,
    last_login_at       timestamptz  NULL,
    password_changed_at timestamptz  NULL,

    CONSTRAINT ck_users_email_lowercase CHECK (email = lower(email))
);

-- ایمیل نرمال‌سازی می‌شود، پس index ساده کافی است
CREATE UNIQUE INDEX ux_users_email ON identity.users (email);

CREATE TABLE identity.user_roles (
    user_id     uuid        NOT NULL,
    role        varchar(64) NOT NULL,
    granted_at  timestamptz NOT NULL,

    PRIMARY KEY (user_id, role),
    CONSTRAINT fk_user_roles_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE
);

CREATE TABLE identity.refresh_tokens (
    id          uuid PRIMARY KEY,
    user_id     uuid        NOT NULL,
    token_hash  varchar(128) NOT NULL,
    expires_at  timestamptz NOT NULL,
    created_at  timestamptz NOT NULL,
    revoked_at  timestamptz NULL,
    reused_at   timestamptz NULL,

    CONSTRAINT fk_refresh_tokens_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE
);

-- فقط hash ذخیره می‌شود: dump دیتابیس نباید توکن قابل‌استفاده بدهد
CREATE UNIQUE INDEX ux_refresh_tokens_hash ON identity.refresh_tokens (token_hash);
CREATE INDEX ix_refresh_tokens_user ON identity.refresh_tokens (user_id);
CREATE INDEX ix_refresh_tokens_expiry ON identity.refresh_tokens (expires_at);
```

FK به `identity.users` **داخل همان ماژول** است — مجاز. قاعده فقط درباره مرز بین ماژول‌هاست.

---

## قواعد مشترک

| قاعده | دلیل |
|---|---|
| کلید اصلی `uuid` | داده بین محیط‌ها sync می‌شود؛ ترتیبی لو می‌دهد چند رکورد وجود دارد |
| ستون `created_at` / `updated_at` | §16 پنل مدیریت به تاریخچه نیاز دارد |
| `ON DELETE RESTRICT` پیش‌فرض | حذف تصادفی داده تاریخی خطرناک است |
| حذف نرم (`is_active`) برای کاربران | §10 نیاز به حفظ تاریخچه نسخه و audit دارد |
| نام snake_case، جمع | تطابق با قرارداد `CONVENTIONS.md` |
| `numeric(18,4)` برای پول | `float` برای مبلغ خطای گرد کردن دارد |

---

## موجودی (فاز ۳ — طراحی اولیه)

طبق §6، هنگام پیاده‌سازی نهایی می‌شود ولی اسکلتش مشخص است:

```sql
CREATE TABLE inventory.stock_items (
    id                 uuid PRIMARY KEY,
    supplier_id        uuid        NOT NULL,   -- بدون FK به suppliers
    product_id         uuid        NOT NULL,   -- بدون FK به catalog
    sku                varchar(128) NOT NULL,
    available_quantity int         NOT NULL DEFAULT 0,
    reserved_quantity  int         NOT NULL DEFAULT 0,
    price              numeric(18,4) NOT NULL,
    status             varchar(32) NOT NULL,
    last_updated_at    timestamptz  NOT NULL,
    source_system      varchar(64) NOT NULL,

    CONSTRAINT ck_stock_no_oversell
        CHECK (available_quantity >= 0 AND reserved_quantity >= 0)
);

CREATE UNIQUE INDEX ux_stock_supplier_sku
    ON inventory.stock_items (supplier_id, sku);
```

`CHECK` روی مقادیر منفی آخرین خط دفاع است. لایه بالاتر با transaction و `SELECT ... FOR UPDATE` هم قفل می‌کند — constraint تنها کافی نیست چون رقابت در سطح read رخ می‌دهد نه write.

---

## PostGIS (فاز ۴)

> **توجه:** تصویر فعلی `postgres:16` است و PostGIS ندارد، چون `postgis/postgis` فقط روی Docker Hub منتشر می‌شود و این شبکه آن را مسدود کرده. فاز ۱ به PostGIS نیازی ندارد. در فاز ۴ یا از `Dockerfile.postgres` استفاده کنید یا تصویر را تغییر دهید.

```sql
CREATE SCHEMA geography;

CREATE TABLE geography.service_areas (
    id           uuid PRIMARY KEY,
    owner_id     uuid NOT NULL,
    polygon      geometry(MultiPolygon, 4326) NOT NULL,
    ...
);

CREATE INDEX ix_service_areas_polygon
    ON geography.service_areas USING GIST (polygon);
```

SRID 4326 (WGS84) — استاندارد ذخیره‌سازی. تبدیل به متر برای محاسبه فاصله در زمان query انجام می‌شود، نه در ذخیره.

---

## مهاجرت

`dotnet ef` در پروژه‌ای با schema جدای متعدد مناسب نیست — هر ماژول context جدا دارد و یک خط فرمان نمی‌تواند همه را هماهنگ کند.

**فعلاً:** `EnsureCreated()` در حالت Development. برای MVP کافی است.

**بعداً (Phase 9):** Flyway. دلیل: نسخه‌بندی صریح، امکان review در PR، و اجرای یکسان در همه محیط‌ها.

این یک بدهی آگاهانه است — ثبت شده در `ARCHITECTURE.md` §8.
