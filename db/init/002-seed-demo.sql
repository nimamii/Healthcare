-- Demo catalogue data.
--
-- Idempotent: safe to run more than once. The API's write endpoints require
-- PlatformAdmin, so this file is the way to see a populated dashboard without
-- building the admin UI first.
--
-- Run against the healthcare database:
--   docker exec -i hc-postgres psql -U healthcare -d healthcare < db/init/002-seed-demo.sql

BEGIN;

-- --- Categories --------------------------------------------------------------

INSERT INTO catalog.categories (id, name, slug, parent_id, depth, is_active, created_at, updated_at)
VALUES
    ('11111111-1111-1111-1111-111111111111', 'تجهیزات پزشکی', 'medical-equipment', NULL, 1, true, now(), now()),
    ('22222222-2222-2222-2222-222222222222', 'تجهیزات آزمایشگاهی', 'lab-equipment', '11111111-1111-1111-1111-111111111111', 2, true, now(), now()),
    ('33333333-3333-3333-3333-333333333333', 'تجهیزات تصویربرداری', 'imaging', '11111111-1111-1111-1111-111111111111', 2, true, now(), now())
ON CONFLICT (id) DO NOTHING;

-- --- Products ----------------------------------------------------------------

INSERT INTO catalog.products (id, name, slug, description, category_id, requires_prescription, is_active, created_at, updated_at)
VALUES
    ('aaaaaaaa-0000-0000-0000-000000000001', 'مانیتور بیمار قابل حمل', 'portable-patient-monitor', 'مانیتور چندپارامتره با نمایشگر ۱۲ اینچ', '33333333-3333-3333-3333-333333333333', false, true, now(), now()),
    ('aaaaaaaa-0000-0000-0000-000000000002', 'دستگاه فشارسنج دیجیتال', 'digital-blood-pressure', 'فشارسنج دیجیتال بازویی با حافظه ۶۰ رکورد', '22222222-2222-2222-2222-222222222222', false, true, now(), now()),
    ('aaaaaaaa-0000-0000-0000-000000000003', 'پالس اکسیمتر انگشتی', 'fingertip-pulse-oximeter', 'پالس اکسیمتر با نمایشگر OLED', '22222222-2222-2222-2222-222222222222', false, true, now(), now()),
    ('aaaaaaaa-0000-0000-0000-000000000004', 'کمک تنفسی خانگی', 'home-ventilator', 'دستگاه کمک تنفسی خانگی با حالت‌های CPAP و BiPAP', '33333333-3333-3333-3333-333333333333', true, true, now(), now()),
    ('aaaaaaaa-0000-0000-0000-000000000005', 'دستگاه سونوگرافی قابل حمل', 'portable-ultrasound', 'سونوگرافی قابل حمل با پروب خطی', '33333333-3333-3333-3333-333333333333', true, true, now(), now())
ON CONFLICT (id) DO NOTHING;

-- --- SKUs -------------------------------------------------------------------

INSERT INTO catalog.skus (id, product_id, code, variant_description, is_active, created_at, updated_at)
VALUES
    ('bbbbbbbb-0000-0000-0000-000000000001', 'aaaaaaaa-0000-0000-0000-000000000001', 'MON-001', 'نمایشگر ۱۲ اینچ', true, now(), now()),
    ('bbbbbbbb-0000-0000-0000-000000000002', 'aaaaaaaa-0000-0000-0000-000000000001', 'MON-002', 'نمایشگر ۱۵ اینچ', true, now(), now()),
    ('bbbbbbbb-0000-0000-0000-000000000003', 'aaaaaaaa-0000-0000-0000-000000000002', 'BP-001', 'بازویی استاندارد', true, now(), now()),
    ('bbbbbbbb-0000-0000-0000-000000000004', 'aaaaaaaa-0000-0000-0000-000000000003', 'OX-001', 'انگشتی ساده', true, now(), now()),
    ('bbbbbbbb-0000-0000-0000-000000000005', 'aaaaaaaa-0000-0000-0000-000000000004', 'VENT-001', 'CPAP + BiPAP', true, now(), now()),
    ('bbbbbbbb-0000-0000-0000-000000000006', 'aaaaaaaa-0000-0000-0000-000000000005', 'US-001', 'پروب خطی', true, now(), now())
ON CONFLICT (id) DO NOTHING;

COMMIT;
