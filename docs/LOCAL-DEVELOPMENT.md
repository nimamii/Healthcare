# راهنمای توسعه محلی

## پیش‌نیاز

فقط **Docker Desktop**. نیازی به نصب .NET SDK یا PostgreSQL روی سیستم نیست — SDK داخل تصویر Docker قرار دارد.

```bash
docker version
docker compose version
```

## فقط build (بدون بالا آوردن هیچ سرویسی)

اگر فقط می‌خواهید بدانید کد کامپایل می‌شود یا نه و نمی‌خواهید کل استک بالا بیاید:

```bash
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet build
```

این یک دستور SDK را در یک کانتینر موقت می‌آورد، پروژه را build می‌کند و کانتینر را پاک می‌کند. هیچ چیز روی سیستم شما نصب نمی‌شود.

### build و تست

```bash
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test
```

> **PowerShell:** اگر `${PWD}` کار نکرد، از مسیر کامل استفاده کنید:
> ```powershell
> docker run --rm -v "C:\path\to\healthcare:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet build
> ```

## راه‌اندازی

```bash
# ۱. تنظیمات محلی
cp .env.example .env

# ۲. کلید امضای JWT را بساز
#    (یک‌بار — در .env بگذار و نگه دار)
openssl rand -base64 48
# خروجی را در JWT_SIGNINGKey= قرار بده
```

بدون `JWT_SIGNING_KEY` برنامه بالا نمی‌آید — این عمدی است. کلید امضای ضعیف نباید به‌راحتی رد شود.

```bash
# ۳. بالا بیاور
docker compose up -d

# ۴. لاگ‌ها
docker compose logs -f api
```

| سرویس | آدرس |
|---|---|
| API | http://localhost:8090 |
| Swagger | http://localhost:8090/swagger |
| Health | http://localhost:8090/health |
| pgAdmin (اختیاری) | افزودنی |

## اولین تست

```bash
# ثبت‌نام
curl -X POST http://localhost:8090/api/identity/register \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"Str0ngPassw0rd!"}'

# ورود
curl -X POST http://localhost:8090/api/identity/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"Str0ngPassw0rd!"}'
```

در Swagger می‌توانید Authorize بزنید و توکن را وارد کنید.

## اجرای تست

تست‌ها روی ماشین اجرا می‌شوند (نه داخل کانتینر) — سریع‌ترند:

```bash
docker run --rm -v "$PWD:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test
```

## دستورهای مفید

```bash
docker compose up -d --watch      # rebuild خودکار
docker compose logs -f postgres   # لاگ دیتابیس
docker compose down               # توقف
docker compose down -v            # توقف + حذف داده‌ها
docker compose exec postgres psql -U healthcare -d healthcare
```

## ساختار دیتابیس

هر ماژول schema خودش را دارد:

```sql
\dn
-- identity
\dt identity.*
```

## بدون Docker

اگر ترجیح می‌دهید SDK را مستقیم نصب کنید:

```bash
dotnet run --project src/Healthcare.Api
```

```bash
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=healthcare;Username=healthcare;Password=local_dev_only"
export Jwt__Issuer=healthcare-api
export Jwt__Audience=healthcare-web
export Jwt__SigningKey="$(openssl rand -base64 48)"
dotnet test
```

## افزودن ماژول جدید

۱. پوشه بساز: `src/Modules/{Name}/{Name}.{Domain,Application,Infrastructure,Api,Contracts}`
۲. `Domain` فقط به `SharedKernel` ارجاع دهد — هیچ وابستگی دیگری
۳. `Contracts` فقط interface، بدون هیچ `PackageReference`
۴. کلاس `IModule` بساز و `Register` / `Configure` را پیاده کن
۵. **یک خط** به لیست `modules` در `Program.cs` اضافه کن
۶. پروژه‌ها را به `Healthcare.sln` اضافه کن

مرزها را `CONVENTIONS.md` بخوان قبل از اینکه شروع کنی.

## خطاهای رایج

**«Jwt:SigningKey must be at least 32 characters»**
کلید در `.env` نیست یا کوتاه است. `openssl rand -base64 48` دوباره بساز.

**«ConnectionStrings:Default is required»**
متغیر محیطی تنظیم نشده. `.env` را بساز یا دستی export کن.

**API بالا نمی‌آید ولی Postgres سالم است**
`docker compose logs api` را ببین. معمولاً خطای پیکربندی است نه دیتابیس.

**تغییرات کد اعمال نمی‌شود**
مطمئن شوید کانتینر API دارد اجرا می‌شود (نه فقط سرویس‌های دیگر) و مسیر `/src` mount شده.

**پورت اشغال است**
`API_PORT` را در `.env` عوض کنید.

## چیزی که هنوز وجود ندارد

موارد زیر عمداً ساخته نشده‌اند چون در فاز ۱ لازم نیستند (قانون MVP FIRST):

- Redis کانفیگ‌شده اما هنوز استفاده‌نشده — جایی که ارزش روشن داشته باشد اضافه می‌شود
- RabbitMQ — فاز ۷
- S3 برای فایل نسخه — فاز ۶
- Audit واقعی — `ISecurityEventRecorder` فعلاً no-op است
