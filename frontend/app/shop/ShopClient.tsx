"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { addToCart, getCart } from "../lib/cart";

const FALLBACK_DEFAULTS: Record<
  string,
  { skuId: string; skuCode: string; supplierId: string; price: number }
> = {
  "portable-patient-monitor": {
    skuId: "bbbbbbbb-0000-0000-0000-000000000001",
    skuCode: "MON-001",
    supplierId: "dddddddd-0000-0000-0000-000000000001",
    price: 485_000_000,
  },
  "digital-blood-pressure": {
    skuId: "bbbbbbbb-0000-0000-0000-000000000003",
    skuCode: "BP-001",
    supplierId: "dddddddd-0000-0000-0000-000000000001",
    price: 18_500_000,
  },
  "fingertip-pulse-oximeter": {
    skuId: "bbbbbbbb-0000-0000-0000-000000000004",
    skuCode: "OX-001",
    supplierId: "dddddddd-0000-0000-0000-000000000001",
    price: 6_500_000,
  },
  "home-ventilator": {
    skuId: "bbbbbbbb-0000-0000-0000-000000000005",
    skuCode: "VENT-001",
    supplierId: "dddddddd-0000-0000-0000-000000000001",
    price: 390_000_000,
  },
  "portable-ultrasound": {
    skuId: "bbbbbbbb-0000-0000-0000-000000000006",
    skuCode: "US-001",
    supplierId: "dddddddd-0000-0000-0000-000000000001",
    price: 950_000_000,
  },
};

export default function ShopClient({
  products,
  isDemoFallback,
}: {
  products: {
    id: string;
    name: string;
    slug: string;
    categoryId: string;
    requiresPrescription: boolean;
    isActive: boolean;
  }[];
  isDemoFallback: boolean;
}) {
  const [cartCount, setCartCount] = useState(0);
  const [addedSlug, setAddedSlug] = useState<string | null>(null);

  useEffect(() => {
    const sync = () => {
      const items = getCart();
      setCartCount(items.reduce((sum, x) => sum + x.quantity, 0));
    };
    sync();
    window.addEventListener("hc-cart-updated", sync);
    return () => window.removeEventListener("hc-cart-updated", sync);
  }, []);

  async function handleAdd(p: {
    id: string;
    name: string;
    slug: string;
    requiresPrescription: boolean;
  }) {
    const preset = FALLBACK_DEFAULTS[p.slug] ?? {
      skuId: "bbbbbbbb-0000-0000-0000-000000000001",
      skuCode: "SKU-DEFAULT",
      supplierId: "dddddddd-0000-0000-0000-000000000001",
      price: 25_000_000,
    };

    let skuId = preset.skuId;
    let skuCode = preset.skuCode;
    let supplierId = preset.supplierId;
    let unitPrice = preset.price;
    let currency = "IRR";

    try {
      const skusRes = await fetch(`/api/catalog/products/${p.id}/skus`);
      if (skusRes.ok) {
        const skus = (await skusRes.json()) as { id: string; code: string }[];
        if (skus.length > 0) {
          skuId = skus[0].id;
          skuCode = skus[0].code;

          const listingsRes = await fetch(`/api/catalog/skus/${skuId}/listings`);
          if (listingsRes.ok) {
            const listings = (await listingsRes.json()) as {
              supplierId: string;
              listPrice: number | null;
              currency: string;
            }[];
            if (listings.length > 0) {
              supplierId = listings[0].supplierId;
              unitPrice = listings[0].listPrice ?? preset.price;
              currency = listings[0].currency || "IRR";
            }
          }
        }
      }
    } catch {
      // Uses deterministic seeded SKU/listing defaults when API container is offline.
    }

    addToCart(
      {
        productId: p.id,
        productName: p.name,
        slug: p.slug,
        skuId,
        skuCode,
        supplierId,
        unitPrice,
        currency,
        requiresPrescription: p.requiresPrescription,
      },
      1,
    );

    setAddedSlug(p.slug);
    setTimeout(() => {
      setAddedSlug((curr) => (curr === p.slug ? null : curr));
    }, 1500);
  }

  return (
    <div>
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: 12,
          marginBottom: 24,
        }}
      >
        <div>
          <h1 style={{ fontSize: "2rem", marginBottom: 4 }}>فروشگاه تجهیزات پزشکی</h1>
          <p style={{ color: "var(--ink-soft)", margin: 0 }}>
            انتخاب مستقیم کالا، بررسی موجودی تأمین‌کننده و ثبت سفارش آنلاین.
          </p>
        </div>

        <Link href="/checkout" className="btn btn-primary">
          <span>سبد خرید و تسویه</span>
          <span
            className="mono"
            style={{
              background: "#ffffff",
              color: "var(--primary)",
              borderRadius: 999,
              padding: "2px 8px",
              fontSize: "0.82rem",
            }}
          >
            {cartCount}
          </span>
        </Link>
      </div>

      {isDemoFallback && (
        <div
          className="card"
          style={{
            marginBottom: 24,
            background: "var(--surface-alt)",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: 12,
          }}
        >
          <span style={{ fontSize: "0.92rem", color: "var(--ink)" }}>
            نمایش کاتالوگ نمونه (`002-seed-demo.sql`). برای اتصال زنده به بک‌اند، کانتینرها را با{" "}
            <code className="mono">docker compose up -d</code> اجرا کنید.
          </span>
          <span className="tag">حالت پیش‌نمایش</span>
        </div>
      )}

      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fill, minmax(260px, 1fr))",
          gap: 20,
        }}
      >
        {products.map((p) => {
          const preset = FALLBACK_DEFAULTS[p.slug];
          const price = preset?.price ?? 25_000_000;
          const isAdded = addedSlug === p.slug;

          return (
            <div key={p.id} className="card" style={{ display: "flex", flexDirection: "column" }}>
              <div
                style={{
                  width: "100%",
                  height: 120,
                  borderRadius: 8,
                  background: "var(--surface-alt)",
                  marginBottom: 12,
                  display: "flex",
                  flexDirection: "column",
                  alignItems: "center",
                  justifyContent: "center",
                  color: "var(--primary)",
                  fontSize: "0.85rem",
                  fontWeight: 600,
                }}
              >
                <span>تجهیزات پزشکی</span>
                <span className="mono" style={{ fontSize: "0.75rem", color: "var(--ink-soft)" }}>
                  {preset?.skuCode ?? p.slug}
                </span>
              </div>

              <h3 style={{ fontSize: "1.05rem", marginBlock: 4 }}>{p.name}</h3>
              <div className="mono" style={{ fontSize: "0.78rem", color: "var(--ink-soft)" }}>
                {p.slug}
              </div>

              {p.requiresPrescription && (
                <span
                  className="tag"
                  style={{ fontSize: "0.75rem", marginTop: 8, alignSelf: "flex-start" }}
                >
                  نیازمند تأیید مجوز / نسخه
                </span>
              )}

              <div
                style={{
                  marginTop: "auto",
                  paddingTop: 16,
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  gap: 8,
                }}
              >
                <div>
                  <div className="mono" style={{ color: "var(--primary)", fontWeight: 700 }}>
                    {price.toLocaleString("fa-IR")}
                  </div>
                  <div style={{ fontSize: "0.75rem", color: "var(--ink-soft)" }}>ریال (IRR)</div>
                </div>

                <button
                  type="button"
                  onClick={() => handleAdd(p)}
                  className={isAdded ? "btn btn-primary" : "btn btn-ghost"}
                  style={{ padding: "8px 14px", fontSize: "0.85rem" }}
                >
                  {isAdded ? "به سبد اضافه شد ✓" : "افزودن به سبد"}
                </button>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
