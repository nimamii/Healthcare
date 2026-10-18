# رفع خطای 403 در دانلود تصویر Docker

## خلاصه وضعیت

| تلاش | نتیجه |
|---|---|
| `docker pull redis:7-alpine` | ❌ 403 از CloudFront |
| `mcr.microsoft.com/dotnet/sdk:8.0` | ✅ موفق (پس `mcr.microsoft.com` باز است) |
| `mcr.microsoft.com/mirror/docker-library/redis` | ❌ چنین مسیری وجود ندارد |
| `registry-mirrors` در daemon.json | ⏳ در انتظار تست |

**نتیجه:** فقط `docker.io` مسدود است. راه‌حل: تنظیم registry mirror.

---

## گام ۱ — فایل تنظیمات را بسازید

در PowerShell:

```powershell
mkdir $env:USERPROFILE\.docker -Force
notepad $env:USERPROFILE\.docker\daemon.json
```

## گام ۲ — محتوا را جای‌گذاری کنید

اگر فایل خالی است یا وجود ندارد، این را کامل جایگزین کنید:

```json
{
  "registry-mirrors": [
    "https://docker.m.daocloud.io",
    "https://dockerproxy.com"
  ]
}
```

> **اگر فایل از قبل محتوایی دارد** (مثلاً تنظیمات پروکسی)، فقط کلید `registry-mirrors` را به آن اضافه کنید و بقیه را دست نزنید. پاک کردن تنظیمات موجود می‌تواند مشکل دیگری بسازد.

آدرس‌های آینه را از تیم شبکه‌تان بپرسید — اگر سازمان شما فایروال دارد، ممکن است فقط یک آینه خاص مجاز باشد.

## گام ۳ — Docker Desktop را restart کنید

از منوی tray روی آیکون Docker راست‌کلیک کنید → **Restart Docker Desktop**

یا برنامه را ببندید و دوباره باز کنید.

## گام ۴ — تست کنید

```powershell
docker pull redis:7-alpine
docker pull postgis/postgis:16-3.4
```

اگر هر دو موفق شدند:

```powershell
.\build.ps1 -Up
```

---

## روش جایگزین (بدون notepad)

```powershell
@'
{
  "registry-mirrors": ["https://docker.m.daocloud.io", "https://dockerproxy.com"]
}
'@ | Set-Content "$env:USERPROFILE\.docker\daemon.json"
```

**هشدار:** این دستور فایل را **بازنویسی** می‌کند. اگر تنظیمات دیگری در آن دارید، اول پشتیبان بگیرید:

```powershell
copy "$env:USERPROFILE\.docker\daemon.json" "$env:USERPROFILE\.docker\daemon.json.bak"
```

---

## اگر mirror جواب نداد

**تست ۱ — آیا DNS کار می‌کند؟**

```powershell
Resolve-DnsName registry-1.docker.io
```

اگر خطا داد، مشکل DNS است و mirror هم کار نخواهد کرد.

**تست ۲ — آیا خود mirror در دسترس است؟**

```powershell
curl.exe -I https://docker.m.daocloud.io/v2/
```

پاسخ `401 Unauthorized` یعنی mirror زنده است و کار می‌کند (این رفتار طبیعی registry است).
`403` یعنی فایروال شما آینه را هم بسته.

**تست ۳ — آیا پروکسی سازمانی دارید؟**

اگر بله، در Docker Desktop:
**Settings** → **Resources** → **Proxies** → Manual proxy → آدرس و پورت را وارد کنید.

---

## گزینه آخر — بدون دیتابیس

اگر هیچ راهی برای دسترسی به registry نیست، می‌توانیم فاز ۲ (Catalog) را بنویسیم بدون اینکه اجرا کنیم. ولی:

- هیچ integration test ممکن نیست
- طبق §36 سند مرجع، قابلیت بدون تست «انجام‌شده» نیست
- ریسک انباشت خطا بالا می‌رود

**توصیه:** حداقل یک راه برای registry پیدا کنید. ساخت و اجرای هر فاز بعدی به آن وابسته است.

---

## چرا MCR جواب داد ولی Docker Hub نه

سیاست فایروال سازمان شما احتمالاً فقط `docker.io` و `production.cloudfront.docker.com` را بسته. مخازن مایکروسافت (`mcr.microsoft.com`) فهرست متفاوتی دارند و باز مانده‌اند. این را از تیم شبکه بپرسید — شاید لیست سفید مشخصی وجود دارد.
