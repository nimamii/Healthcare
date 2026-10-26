"use client";

import { useState } from "react";
import { getAuthSession } from "../lib/cart";

export default function AdminPanelClient() {
  const [activeTab, setActiveTab] = useState<"product" | "inventory">("product");

  const [productName, setProductName] = useState("");
  const [productSlug, setProductSlug] = useState("");
  const [productDesc, setProductDesc] = useState("");
  const [categoryId, setCategoryId] = useState("33333333-3333-3333-3333-333333333333");
  const [requiresPrescription, setRequiresPrescription] = useState(false);

  const [skuId, setSkuId] = useState("bbbbbbbb-0000-0000-0000-000000000001");
  const [supplierId, setSupplierId] = useState("dddddddd-0000-0000-0000-000000000001");
  const [newQuantity, setNewQuantity] = useState(25);

  const [loading, setLoading] = useState(false);
  const [feedback, setFeedback] = useState<{ type: "ok" | "err"; text: string } | null>(null);

  async function handleCreateProduct(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setFeedback(null);

    const session = getAuthSession();
    try {
      const res = await fetch("/api/catalog/products", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ...(session?.accessToken ? { Authorization: `Bearer ${session.accessToken}` } : {}),
        },
        body: JSON.stringify({
          name: productName,
          slug: productSlug,
          description: productDesc || null,
          categoryId,
          requiresPrescription,
        }),
      });

      if (!res.ok) {
        const body = await res.json().catch(() => null);
        setFeedback({
          type: "err",
          text:
            body?.detail ??
            "ایجاد محصول نیازمند نقش PlatformAdmin و اتصال فعال به بک‌اند است.",
        });
        return;
      }

      const created = (await res.json()) as { id: string };
      setFeedback({
        type: "ok",
        text: `محصول جدید با شناسه ${created.id} با موفقیت ثبت شد.`,
      });
      setProductName("");
      setProductSlug("");
      setProductDesc("");
    } catch {
      setFeedback({
        type: "err",
        text: "ارتباط با سرور برقرار نشد. مطمئن شوید کانتینر hc-api روشن است.",
      });
    } finally {
      setLoading(false);
    }
  }

  async function handleAdjustStock(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setFeedback(null);

    const session = getAuthSession();
    try {
      const res = await fetch("/api/inventory/adjust", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ...(session?.accessToken ? { Authorization: `Bearer ${session.accessToken}` } : {}),
        },
        body: JSON.stringify({
          skuId,
          supplierId,
          newAvailableQuantity: Number(newQuantity),
        }),
      });

      if (!res.ok) {
        const body = await res.json().catch(() => null);
        setFeedback({
          type: "err",
          text: body?.detail ?? "به‌روزرسانی موجودی ناموفق بود. ابتدا وارد حساب شوید.",
        });
        return;
      }

      setFeedback({
        type: "ok",
        text: `موجودی SKU به ${newQuantity.toLocaleString("fa-IR")} عدد به‌روزرسانی شد.`,
      });
    } catch {
      setFeedback({
        type: "err",
        text: "ارتباط با سرور برقرار نشد. مطمئن شوید کانتینر hc-api روشن است.",
      });
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="card" style={{ marginBottom: 32 }}>
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: 12,
          marginBottom: 20,
        }}
      >
        <div>
          <h2 style={{ fontSize: "1.2rem" }}>مدیریت کاتالوگ و موجودی تأمین‌کننده</h2>
          <p style={{ color: "var(--ink-soft)", fontSize: "0.88rem", margin: 0 }}>
            ثبت محصول جدید در کاتالوگ (PlatformAdmin) و تنظیم لحظه‌ای موجودی انبار (/api/inventory/adjust).
          </p>
        </div>

        <div style={{ display: "flex", gap: 8 }}>
          <button
            type="button"
            onClick={() => {
              setActiveTab("product");
              setFeedback(null);
            }}
            className={activeTab === "product" ? "btn btn-primary" : "btn btn-ghost"}
            style={{ padding: "6px 14px", fontSize: "0.85rem" }}
          >
            افزودن محصول
          </button>
          <button
            type="button"
            onClick={() => {
              setActiveTab("inventory");
              setFeedback(null);
            }}
            className={activeTab === "inventory" ? "btn btn-primary" : "btn btn-ghost"}
            style={{ padding: "6px 14px", fontSize: "0.85rem" }}
          >
            تنظیم موجودی انبار
          </button>
        </div>
      </div>

      {activeTab === "product" ? (
        <form onSubmit={handleCreateProduct} style={{ display: "grid", gap: 14 }}>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 12 }}>
            <div>
              <label style={{ display: "block", marginBottom: 6, fontSize: "0.85rem", fontWeight: 600 }}>
                نام محصول (فارسی)
              </label>
              <input
                type="text"
                value={productName}
                onChange={(e) => setProductName(e.target.value)}
                placeholder="مثال: الکتروکاردیوگراف ۱۲ کاناله"
                required
                style={{
                  width: "100%",
                  padding: "9px 12px",
                  borderRadius: 8,
                  border: "1px solid var(--line)",
                  background: "var(--surface)",
                  fontFamily: "inherit",
                }}
              />
            </div>

            <div>
              <label style={{ display: "block", marginBottom: 6, fontSize: "0.85rem", fontWeight: 600 }}>
                شناسه یکتا (Slug انگلیسی)
              </label>
              <input
                type="text"
                className="mono"
                value={productSlug}
                onChange={(e) => setProductSlug(e.target.value)}
                placeholder="ecg-12-channel"
                required
                style={{
                  width: "100%",
                  padding: "9px 12px",
                  borderRadius: 8,
                  border: "1px solid var(--line)",
                  background: "var(--surface)",
                }}
              />
            </div>

            <div>
              <label style={{ display: "block", marginBottom: 6, fontSize: "0.85rem", fontWeight: 600 }}>
                دسته‌بندی
              </label>
              <select
                value={categoryId}
                onChange={(e) => setCategoryId(e.target.value)}
                style={{
                  width: "100%",
                  padding: "9px 12px",
                  borderRadius: 8,
                  border: "1px solid var(--line)",
                  background: "var(--surface)",
                  fontFamily: "inherit",
                }}
              >
                <option value="33333333-3333-3333-3333-333333333333">تجهیزات تصویربرداری و مانیتورینگ</option>
                <option value="22222222-2222-2222-2222-222222222222">تجهیزات آزمایشگاهی و تشخیصی</option>
                <option value="11111111-1111-1111-1111-111111111111">تجهیزات پزشکی عمومی</option>
              </select>
            </div>
          </div>

          <div>
            <label style={{ display: "block", marginBottom: 6, fontSize: "0.85rem", fontWeight: 600 }}>
              توضیحات فنی
            </label>
            <input
              type="text"
              value={productDesc}
              onChange={(e) => setProductDesc(e.target.value)}
              placeholder="مشخصات دستگاه، استانداردها و گارانتی..."
              style={{
                width: "100%",
                padding: "9px 12px",
                borderRadius: 8,
                border: "1px solid var(--line)",
                background: "var(--surface)",
                fontFamily: "inherit",
              }}
            />
          </div>

          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 12 }}>
            <label style={{ display: "inline-flex", alignItems: "center", gap: 8, fontSize: "0.88rem", cursor: "pointer" }}>
              <input
                type="checkbox"
                checked={requiresPrescription}
                onChange={(e) => setRequiresPrescription(e.target.checked)}
              />
              <span>نیازمند مجوز / نسخه پزشکی</span>
            </label>

            <button type="submit" className="btn btn-primary" disabled={loading}>
              {loading ? "در حال ثبت..." : "ثبت محصول در کاتالوگ"}
            </button>
          </div>
        </form>
      ) : (
        <form onSubmit={handleAdjustStock} style={{ display: "grid", gap: 14 }}>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 12 }}>
            <div>
              <label style={{ display: "block", marginBottom: 6, fontSize: "0.85rem", fontWeight: 600 }}>
                انتخاب کالا (SKU)
              </label>
              <select
                value={skuId}
                onChange={(e) => setSkuId(e.target.value)}
                style={{
                  width: "100%",
                  padding: "9px 12px",
                  borderRadius: 8,
                  border: "1px solid var(--line)",
                  background: "var(--surface)",
                  fontFamily: "inherit",
                }}
              >
                <option value="bbbbbbbb-0000-0000-0000-000000000001">MON-001 — مانیتور بیمار ۱۲ اینچ</option>
                <option value="bbbbbbbb-0000-0000-0000-000000000002">MON-002 — مانیتور بیمار ۱۵ اینچ</option>
                <option value="bbbbbbbb-0000-0000-0000-000000000003">BP-001 — فشارسنج دیجیتال بازویی</option>
                <option value="bbbbbbbb-0000-0000-0000-000000000004">OX-001 — پالس اکسیمتر انگشتی</option>
                <option value="bbbbbbbb-0000-0000-0000-000000000005">VENT-001 — کمک تنفسی خانگی</option>
                <option value="bbbbbbbb-0000-0000-0000-000000000006">US-001 — سونوگرافی قابل حمل</option>
              </select>
            </div>

            <div>
              <label style={{ display: "block", marginBottom: 6, fontSize: "0.85rem", fontWeight: 600 }}>
                شناسه تأمین‌کننده (Supplier ID)
              </label>
              <input
                type="text"
                className="mono"
                value={supplierId}
                onChange={(e) => setSupplierId(e.target.value)}
                required
                style={{
                  width: "100%",
                  padding: "9px 12px",
                  borderRadius: 8,
                  border: "1px solid var(--line)",
                  background: "var(--surface)",
                }}
              />
            </div>

            <div>
              <label style={{ display: "block", marginBottom: 6, fontSize: "0.85rem", fontWeight: 600 }}>
                موجودی قابل فروش جدید
              </label>
              <input
                type="number"
                min={0}
                className="mono"
                value={newQuantity}
                onChange={(e) => setNewQuantity(Number(e.target.value))}
                required
                style={{
                  width: "100%",
                  padding: "9px 12px",
                  borderRadius: 8,
                  border: "1px solid var(--line)",
                  background: "var(--surface)",
                }}
              />
            </div>
          </div>

          <div style={{ display: "flex", justifyContent: "flex-end" }}>
            <button type="submit" className="btn btn-primary" disabled={loading}>
              {loading ? "در حال ذخیره..." : "به‌روزرسانی موجودی"}
            </button>
          </div>
        </form>
      )}

      {feedback && (
        <div
          style={{
            marginTop: 14,
            padding: "10px 14px",
            borderRadius: 8,
            background: "var(--surface-alt)",
            color: feedback.type === "ok" ? "var(--primary)" : "var(--pulse)",
            fontSize: "0.88rem",
            fontWeight: 500,
          }}
        >
          {feedback.text}
        </div>
      )}
    </div>
  );
}
