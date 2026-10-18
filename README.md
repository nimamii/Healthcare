# یا علی — مارکت‌پلیس تجهیزات پزشکی

Two-sided healthcare marketplace. Phase 0 → Phase 1 (Foundation).

> **Build less. Build right. Launch fast.**

---

## سند مرجع

`MASTER_PROMPT___HEALTHCARE_MARKETPLACE.md` منبع حقیقت برای اسکوپ، استک و فازبندی است. هرجا ابهام پیش بیاید، به آن ارجاع می‌دهیم. اگر این مخزن با آن سند تعارض داشت، سند برنده است و مخزن باید اصلاح شود.

---

## استک

| لایه | فناوری |
|---|---|
| Frontend | Next.js + TypeScript + Tailwind |
| Backend | ASP.NET Core (C#) |
| Database | PostgreSQL + PostGIS |
| Cache | Redis (فقط جایی که ارزش روشن دارد) |
| Messaging | RabbitMQ (انتخابی — نه برای همه‌چیز) |
| Storage | S3-compatible |
| Infra | Docker Compose + Nginx — **بدون Kubernetes** |

---

## معماری

**Modular Monolith.** هر ماژول یک پروژه C# مستقل با **schema جدا در دیتابیس**.

```
src/
├─ Healthcare.Api/           ← host + composition root
├─ BuildingBlocks/
│  ├─ SharedKernel/          ← Result, DomainException, IModule
│  └─ Contracts/             ← interfaceهای بین‌ماژولی
└─ Modules/
   ├─ Identity/              {Domain, Application, Infrastructure, Api}
   ├─ Catalog/
   └─ ...
```

قواعد مرز:
- ماژول‌ها فقط از راه `Contracts` همدیگر را می‌شناسند
- ارتباط بین ماژول‌ها **فقط** از راه Service Layer — نه `using` مستقیم، نه Join مستقیم دیتابیس
- schema هر ماژول متعلق به خودش است

جزئیات: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)

---

## شروع سریع

نیاز: فقط **Docker Desktop**. نه .NET SDK نصب می‌خواهد، نه PostgreSQL.

```powershell
# ویندوز
.\build.ps1              # فقط build
.\build.ps1 -Test        # build + تست
.\build.ps1 -Up          # بالا آوردن کل استک
```

```bash
# لینوکس / مک
cp .env.example .env
docker compose up -d
```

سپس: `http://localhost:8090/swagger`

جزئیات: [docs/LOCAL-DEVELOPMENT.md](docs/LOCAL-DEVELOPMENT.md)

---

## مستندات

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — معماری سیستم، ماژول‌ها، جریان داده، مرزهای امنیتی
- [docs/MVP-PLAN.md](docs/MVP-PLAN.md) — فازبندی، اولویت، وابستگی، ریسک، فرض‌ها
- [docs/DATABASE.md](docs/DATABASE.md) — طرح دیتابیس و مالکیت جدول‌ها
- [docs/CONVENTIONS.md](docs/CONVENTIONS.md) — قراردادهای کدنویسی و قوانین AI Agent

---

## فاز فعلی

**Phase 1 — Foundation.** فاز ۶ (دارو/نسخه) عمداً تعویق افتاده و فقط جای آن در معماری حفظ شده.
