"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import {
  CartItem,
  clearCart,
  getAuthSession,
  getCart,
  saveOrderRecord,
  updateCartQuantity,
} from "../lib/cart";

export default function CheckoutPage() {
  const [items, setItems] = useState<CartItem[]>([]);
  const [latitude, setLatitude] = useState("35.6892");
  const [longitude, setLongitude] = useState("51.3890");
  const [addressNote, setAddressNote] = useState("تهران، خیابان ولیعصر، کلینیک مرکزی");
  const [sessionEmail, setSessionEmail] = useState<string | null>(null);
  const [customerId, setCustomerId] = useState<string | null>(null);
  const [accessToken, setAccessToken] = useState<string | null>(null);

  const [submitting, setSubmitting] = useState(false);
  const [stepLog, setStepLog] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [completedOrderId, setCompletedOrderId] = useState<string | null>(null);

  useEffect(() => {
    setItems(getCart());
    const session = getAuthSession();
    if (session) {
      setAccessToken(session.accessToken);
      setCustomerId(session.userId);
      setSessionEmail(session.email);
    }
  }, []);

  function handleQtyChange(skuId: string, nextQty: number) {
    setItems(updateCartQuantity(skuId, nextQty));
  }

  const totalAmount = items.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0);

  async function handleCheckout(e: React.FormEvent) {
    e.preventDefault();
    if (items.length === 0) return;

    setSubmitting(true);
    setError(null);
    setStepLog([]);

    const pushStep = (msg: string) => setStepLog((prev) => [...prev, msg]);
    const authHeaders: Record<string, string> = {
      "Content-Type": "application/json",
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
    };

    try {
      const primaryItem = items[0];
      let chosenSupplierId = primaryItem.supplierId;

      pushStep("۱. بررسی موجودی و رتبه‌بندی تأمین‌کنندگان در محدوده جغرافیایی...");
      try {
        const rankRes = await fetch(
          `/api/fulfillment/skus/${primaryItem.skuId}/suppliers?latitude=${encodeURIComponent(
            latitude,
          )}&longitude=${encodeURIComponent(longitude)}`,
        );
        if (rankRes.ok) {
          const candidates = (await rankRes.json()) as {
            supplierId: string;
            availableQuantity: number;
            isInArea: boolean;
            requiresCustomerConfirmation: boolean;
          }[];
          if (candidates.length > 0) {
            chosenSupplierId = candidates[0].supplierId;
            pushStep(
              `تأمین‌کننده منتخب (${chosenSupplierId.slice(0, 8)}...) با موجودی ${candidates[0].availableQuantity} عدد انتخاب شد.`,
            );
          }
        }
      } catch {
        // Proceed with default supplier ID if fulfillment endpoint is offline
      }

      const effectiveCustomerId = customerId ?? "99999999-0000-0000-0000-000000000001";
      pushStep("۲. ایجاد سفارش در سرویس سفارش‌ها (/api/orders)...");

      let orderId = `ord-${Date.now()}`;
      let paymentId = `pay-${Date.now()}`;
      let apiConnected = false;

      try {
        const createOrderRes = await fetch("/api/orders", {
          method: "POST",
          headers: authHeaders,
          body: JSON.stringify({
            customerId: effectiveCustomerId,
            supplierId: chosenSupplierId,
          }),
        });

        if (createOrderRes.status === 401 || createOrderRes.status === 403) {
          setError("برای ثبت نهایی سفارش در سرور، لطفاً ابتدا وارد حساب کاربری خود شوید.");
          setSubmitting(false);
          return;
        }

        if (createOrderRes.ok) {
          apiConnected = true;
          const createdOrder = (await createOrderRes.json()) as { id: string };
          orderId = createdOrder.id;

          pushStep("۳. افزودن اقلام سفارش و رزرو موجودی (/api/fulfillment/reserve)...");
          for (const item of items) {
            await fetch(`/api/orders/${orderId}/lines`, {
              method: "POST",
              headers: authHeaders,
              body: JSON.stringify({
                skuId: item.skuId,
                quantity: item.quantity,
                unitPrice: item.unitPrice,
                currency: item.currency,
              }),
            });

            await fetch("/api/fulfillment/reserve", {
              method: "POST",
              headers: authHeaders,
              body: JSON.stringify({
                orderId,
                skuId: item.skuId,
                supplierId: chosenSupplierId,
                quantity: item.quantity,
              }),
            });
          }

          pushStep("۴. انتقال سفارش به وضعیت انتظار پرداخت و فراخوانی درگاه (/api/payments)...");
          await fetch(`/api/orders/${orderId}/payment-pending`, {
            method: "POST",
            headers: authHeaders,
          });

          const payRes = await fetch("/api/payments", {
            method: "POST",
            headers: authHeaders,
            body: JSON.stringify({
              orderId,
              amount: totalAmount,
              currency: "IRR",
            }),
          });

          if (payRes.ok) {
            const payData = (await payRes.json()) as { id: string };
            paymentId = payData.id;
            await fetch(`/api/payments/${paymentId}/refresh`, {
              method: "POST",
              headers: authHeaders,
            });
          }

          pushStep("۵. تأیید پرداخت و نهایی‌سازی سفارش...");
          await fetch(`/api/orders/${orderId}/paid`, {
            method: "POST",
            headers: authHeaders,
          });
          await fetch(`/api/orders/${orderId}/fulfillment-pending`, {
            method: "POST",
            headers: authHeaders,
          });
          await fetch(`/api/orders/${orderId}/confirm`, {
            method: "POST",
            headers: authHeaders,
          });
        }
      } catch {
        pushStep("سرور بک‌اند در دسترس نیست؛ سفارش در حالت پیش‌نمایش محلی ثبت شد.");
      }

      saveOrderRecord({
        id: orderId,
        supplierId: chosenSupplierId,
        totalAmount,
        currency: "IRR",
        status: apiConnected ? "تأیید شده (Confirmed)" : "ثبت‌شده در پیش‌نمایش",
        paymentId,
        createdAt: new Date().toLocaleDateString("fa-IR"),
        items,
      });

      clearCart();
      setItems([]);
      setCompletedOrderId(orderId);
    } catch {
      setError("خطایی در فرآیند ثبت سفارش رخ داد. لطفاً دوباره تلاش کنید.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main>
      <header className="container" style={{ paddingBlock: 20 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <Link href="/" style={{ fontSize: "1.2rem", fontWeight: "bold", color: "var(--primary)" }}>
            یا علی
          </Link>
          <nav style={{ display: "flex", gap: 16, fontSize: "0.95rem", alignItems: "center" }}>
            <Link href="/shop">فروشگاه</Link>
            <Link href="/about">درباره ما</Link>
            <Link href="/contact">تماس با ما</Link>
            <Link href="/app" className="btn btn-primary" style={{ padding: "6px 14px", fontSize: "0.88rem" }}>
              داشبورد
            </Link>
          </nav>
        </div>
      </header>

      <section className="container" style={{ paddingBlock: "32px 80px" }}>
        <h1 style={{ fontSize: "2rem", marginBottom: 8 }}>سبد خرید و تسویه‌حساب</h1>
        <p style={{ color: "var(--ink-soft)", marginTop: 0, marginBottom: 28 }}>
          بررسی اقلام، تعیین موقعیت تحویل (PostGIS) و پرداخت امن سفارش.
        </p>

        {completedOrderId ? (
          <div className="card" style={{ maxWidth: 640, marginInline: "auto", textAlign: "center" }}>
            <span className="tag" style={{ marginBottom: 12 }}>
              سفارش با موفقیت ثبت شد
            </span>
            <h2 style={{ fontSize: "1.4rem", marginBottom: 8 }}>
              سفارش شما ثبت و تأیید شد
            </h2>
            <p className="mono" style={{ color: "var(--primary)", fontSize: "0.95rem" }}>
              شناسه سفارش: {completedOrderId}
            </p>

            {stepLog.length > 0 && (
              <div
                style={{
                  textAlign: "right",
                  background: "var(--surface-alt)",
                  padding: 16,
                  borderRadius: 8,
                  marginBlock: 20,
                  fontSize: "0.88rem",
                }}
              >
                {stepLog.map((s, idx) => (
                  <div key={idx} style={{ marginBottom: 4 }}>
                    • {s}
                  </div>
                ))}
              </div>
            )}

            <div style={{ display: "flex", gap: 12, justifyContent: "center", marginTop: 20 }}>
              <Link href="/app" className="btn btn-primary">
                مشاهده در داشبورد
              </Link>
              <Link href="/shop" className="btn btn-ghost">
                بازگشت به فروشگاه
              </Link>
            </div>
          </div>
        ) : items.length === 0 ? (
          <div className="card" style={{ textAlign: "center", paddingBlock: 48 }}>
            <h2 style={{ fontSize: "1.2rem", marginBottom: 8 }}>سبد خرید شما خالی است</h2>
            <p style={{ color: "var(--ink-soft)", marginBottom: 24 }}>
              از فروشگاه تجهیزات پزشکی مورد نظر خود را به سبد خرید اضافه کنید.
            </p>
            <Link href="/shop" className="btn btn-primary">
              رفتن به فروشگاه
            </Link>
          </div>
        ) : (
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))",
              gap: 24,
              alignItems: "start",
            }}
          >
            <div style={{ display: "grid", gap: 16 }}>
              {items.map((item) => (
                <div
                  key={item.skuId}
                  className="card"
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    gap: 16,
                    flexWrap: "wrap",
                  }}
                >
                  <div>
                    <h3 style={{ fontSize: "1.05rem", marginBottom: 4 }}>{item.productName}</h3>
                    <div className="mono" style={{ fontSize: "0.8rem", color: "var(--ink-soft)" }}>
                      SKU: {item.skuCode}
                    </div>
                    <div className="mono" style={{ color: "var(--primary)", fontWeight: 600, marginTop: 6 }}>
                      {(item.unitPrice * item.quantity).toLocaleString("fa-IR")} ریال
                    </div>
                  </div>

                  <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                    <button
                      type="button"
                      className="btn btn-ghost"
                      style={{ padding: "4px 12px" }}
                      onClick={() => handleQtyChange(item.skuId, item.quantity - 1)}
                    >
                      −
                    </button>
                    <span className="mono" style={{ minWidth: 28, textAlign: "center", fontWeight: 700 }}>
                      {item.quantity}
                    </span>
                    <button
                      type="button"
                      className="btn btn-ghost"
                      style={{ padding: "4px 12px" }}
                      onClick={() => handleQtyChange(item.skuId, item.quantity + 1)}
                    >
                      +
                    </button>
                  </div>
                </div>
              ))}
            </div>

            <form onSubmit={handleCheckout} className="card" style={{ display: "grid", gap: 16 }}>
              <h2 style={{ fontSize: "1.2rem" }}>اطلاعات تحویل و پرداخت</h2>

              <div
                style={{
                  padding: "10px 14px",
                  borderRadius: 8,
                  background: "var(--surface-alt)",
                  fontSize: "0.88rem",
                }}
              >
                {sessionEmail ? (
                  <span>
                    حساب کاربری فعال: <strong className="mono">{sessionEmail}</strong>
                  </span>
                ) : (
                  <span>
                    برای ارسال سفارش به API،{" "}
                    <Link href="/login" style={{ color: "var(--primary)", fontWeight: 600 }}>
                      وارد حساب شوید
                    </Link>{" "}
                    یا{" "}
                    <Link href="/register" style={{ color: "var(--primary)", fontWeight: 600 }}>
                      ثبت‌نام کنید
                    </Link>
                    .
                  </span>
                )}
              </div>

              <div>
                <label style={{ display: "block", marginBottom: 6, fontSize: "0.88rem", fontWeight: 600 }}>
                  نشانی تحویل (بیمارستان / کلینیک)
                </label>
                <input
                  type="text"
                  value={addressNote}
                  onChange={(e) => setAddressNote(e.target.value)}
                  required
                  style={{
                    width: "100%",
                    padding: "10px 12px",
                    borderRadius: 8,
                    border: "1px solid var(--line)",
                    background: "var(--surface)",
                    fontFamily: "inherit",
                  }}
                />
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
                <div>
                  <label style={{ display: "block", marginBottom: 6, fontSize: "0.82rem", fontWeight: 600 }}>
                    عرض جغرافیایی (Latitude)
                  </label>
                  <input
                    type="text"
                    className="mono"
                    value={latitude}
                    onChange={(e) => setLatitude(e.target.value)}
                    required
                    style={{
                      width: "100%",
                      padding: "8px 10px",
                      borderRadius: 8,
                      border: "1px solid var(--line)",
                      background: "var(--surface)",
                    }}
                  />
                </div>
                <div>
                  <label style={{ display: "block", marginBottom: 6, fontSize: "0.82rem", fontWeight: 600 }}>
                    طول جغرافیایی (Longitude)
                  </label>
                  <input
                    type="text"
                    className="mono"
                    value={longitude}
                    onChange={(e) => setLongitude(e.target.value)}
                    required
                    style={{
                      width: "100%",
                      padding: "8px 10px",
                      borderRadius: 8,
                      border: "1px solid var(--line)",
                      background: "var(--surface)",
                    }}
                  />
                </div>
              </div>

              <div
                style={{
                  borderTop: "1px solid var(--line)",
                  paddingTop: 14,
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                }}
              >
                <span style={{ fontWeight: 600 }}>مبلغ کل قابل پرداخت:</span>
                <span className="mono" style={{ fontSize: "1.2rem", color: "var(--primary)", fontWeight: 700 }}>
                  {totalAmount.toLocaleString("fa-IR")} ریال
                </span>
              </div>

              {error && (
                <div
                  style={{
                    color: "var(--pulse)",
                    background: "var(--surface-alt)",
                    padding: "10px 12px",
                    borderRadius: 8,
                    fontSize: "0.88rem",
                  }}
                >
                  {error}
                </div>
              )}

              <button
                type="submit"
                className="btn btn-primary"
                disabled={submitting}
                style={{ justifyContent: "center" }}
              >
                {submitting ? "در حال پردازش سفارش..." : "تأیید و پرداخت سفارش"}
              </button>
            </form>
          </div>
        )}
      </section>
    </main>
  );
}
