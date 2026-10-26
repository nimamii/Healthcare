"use client";

import { useState } from "react";
import Link from "next/link";

type DebugLogEntry = {
  id: string;
  timestamp: string;
  method: "GET" | "POST";
  endpoint: string;
  status: number;
  mode: "LIVE" | "SIMULATED";
  durationMs: number;
  requestBody?: unknown;
  responseBody: unknown;
};

type DemoSku = {
  skuId: string;
  skuCode: string;
  productId: string;
  productName: string;
  slug: string;
  category: string;
  price: number;
  availableStock: number;
  reservedStock: number;
  version: number;
  requiresPrescription: boolean;
};

const INITIAL_SKUS: DemoSku[] = [
  {
    skuId: "bbbbbbbb-0000-0000-0000-000000000001",
    skuCode: "MON-001",
    productId: "aaaaaaaa-0000-0000-0000-000000000001",
    productName: "مانیتور بیمار قابل حمل (۱۲ اینچ)",
    slug: "portable-patient-monitor",
    category: "تجهیزات تصویربرداری و مانیتورینگ",
    price: 485_000_000,
    availableStock: 15,
    reservedStock: 0,
    version: 1,
    requiresPrescription: false,
  },
  {
    skuId: "bbbbbbbb-0000-0000-0000-000000000003",
    skuCode: "BP-001",
    productId: "aaaaaaaa-0000-0000-0000-000000000002",
    productName: "دستگاه فشارسنج دیجیتال بازویی",
    slug: "digital-blood-pressure",
    category: "تجهیزات آزمایشگاهی و تشخیصی",
    price: 18_500_000,
    availableStock: 40,
    reservedStock: 0,
    version: 1,
    requiresPrescription: false,
  },
  {
    skuId: "bbbbbbbb-0000-0000-0000-000000000004",
    skuCode: "OX-001",
    productId: "aaaaaaaa-0000-0000-0000-000000000003",
    productName: "پالس اکسیمتر انگشتی OLED",
    slug: "fingertip-pulse-oximeter",
    category: "تجهیزات آزمایشگاهی و تشخیصی",
    price: 6_500_000,
    availableStock: 60,
    reservedStock: 0,
    version: 1,
    requiresPrescription: false,
  },
  {
    skuId: "bbbbbbbb-0000-0000-0000-000000000005",
    skuCode: "VENT-001",
    productId: "aaaaaaaa-0000-0000-0000-000000000004",
    productName: "کمک تنفسی خانگی (CPAP / BiPAP)",
    slug: "home-ventilator",
    category: "تجهیزات تصویربرداری و مانیتورینگ",
    price: 390_000_000,
    availableStock: 6,
    reservedStock: 0,
    version: 1,
    requiresPrescription: true,
  },
  {
    skuId: "bbbbbbbb-0000-0000-0000-000000000006",
    skuCode: "US-001",
    productId: "aaaaaaaa-0000-0000-0000-000000000005",
    productName: "دستگاه سونوگرافی قابل حمل پروب خطی",
    slug: "portable-ultrasound",
    category: "تجهیزات تصویربرداری و مانیتورینگ",
    price: 950_000_000,
    availableStock: 4,
    reservedStock: 0,
    version: 1,
    requiresPrescription: true,
  },
];

const ORDER_STATES = [
  { code: 0, name: "Created", label: "۱. ایجاد شده (Created)" },
  { code: 1, name: "PaymentPending", label: "۲. در انتظار پرداخت (PaymentPending)" },
  { code: 2, name: "Paid", label: "۳. پرداخت شده (Paid)" },
  { code: 3, name: "FulfillmentPending", label: "۴. در انتظار تأمین (FulfillmentPending)" },
  { code: 4, name: "Confirmed", label: "۵. تأیید نهایی (Confirmed)" },
  { code: 5, name: "Cancelled", label: "لغو شده (Cancelled)" },
];

export default function DemoDebuggerPage() {
  const [skus, setSkus] = useState<DemoSku[]>(INITIAL_SKUS);
  const [selectedSkuId, setSelectedSkuId] = useState<string>(INITIAL_SKUS[0].skuId);
  const [orderQty, setOrderQty] = useState<number>(2);
  const [latitude, setLatitude] = useState<string>("35.6892");
  const [longitude, setLongitude] = useState<string>("51.3890");

  // Order & Payment state machine simulator for debugging
  const [activeOrderId, setActiveOrderId] = useState<string | null>(null);
  const [orderState, setOrderState] = useState<number>(-1);
  const [paymentStatus, setPaymentStatus] = useState<string>("بدون تراکنش");
  const [reservationId, setReservationId] = useState<string | null>(null);
  const [geoInArea, setGeoInArea] = useState<boolean>(true);

  // Debug console logs
  const [logs, setLogs] = useState<DebugLogEntry[]>([]);
  const [selectedLog, setSelectedLog] = useState<DebugLogEntry | null>(null);

  const selectedSku = skus.find((s) => s.skuId === selectedSkuId) ?? skus[0];

  function appendLog(entry: Omit<DebugLogEntry, "id" | "timestamp">) {
    const newEntry: DebugLogEntry = {
      ...entry,
      id: `req-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
      timestamp: new Date().toLocaleTimeString("fa-IR"),
    };
    setLogs((prev) => [newEntry, ...prev]);
    setSelectedLog(newEntry);
  }

  async function callApiOrSimulate(
    method: "GET" | "POST",
    endpoint: string,
    requestBody: unknown,
    simulatedStatus: number,
    simulatedResponse: unknown,
  ) {
    const start = performance.now();
    try {
      const res = await fetch(endpoint, {
        method,
        headers: requestBody ? { "Content-Type": "application/json" } : undefined,
        body: requestBody ? JSON.stringify(requestBody) : undefined,
      });
      const durationMs = Math.round(performance.now() - start);
      if (res.ok || res.status === 400 || res.status === 401) {
        const body = await res.json().catch(() => ({ status: res.status }));
        appendLog({
          method,
          endpoint,
          status: res.status,
          mode: "LIVE",
          durationMs,
          requestBody,
          responseBody: body,
        });
        return { live: true, status: res.status, body };
      }
    } catch {
      // Fallback to deterministic simulation when Docker API container is offline
    }

    const durationMs = Math.max(4, Math.round(performance.now() - start));
    appendLog({
      method,
      endpoint,
      status: simulatedStatus,
      mode: "SIMULATED",
      durationMs,
      requestBody,
      responseBody: simulatedResponse,
    });
    return { live: false, status: simulatedStatus, body: simulatedResponse };
  }

  async function step1RankSuppliers() {
    const latNum = parseFloat(latitude);
    const lonNum = parseFloat(longitude);
    const inTehran = latNum >= 35.5 && latNum <= 35.85 && lonNum >= 51.1 && lonNum <= 51.65;
    setGeoInArea(inTehran);

    const endpoint = `/api/fulfillment/skus/${selectedSku.skuId}/suppliers?latitude=${latitude}&longitude=${longitude}`;
    const simResponse = [
      {
        supplierId: "dddddddd-0000-0000-0000-000000000001",
        availableQuantity: selectedSku.availableStock,
        listPrice: selectedSku.price,
        isInArea: inTehran,
        requiresCustomerConfirmation: !inTehran,
        outOfAreaSurcharge: inTehran ? null : 2_500_000,
      },
    ];
    await callApiOrSimulate("GET", endpoint, undefined, 200, simResponse);
  }

  async function step2CreateOrderAndReserve() {
    if (orderQty <= 0 || orderQty > selectedSku.availableStock) {
      appendLog({
        method: "POST",
        endpoint: "/api/fulfillment/reserve",
        status: 400,
        mode: "SIMULATED",
        durationMs: 5,
        requestBody: { skuId: selectedSku.skuId, quantity: orderQty },
        responseBody: {
          title: "موجودی ناکافی",
          detail: "The selected supplier does not have enough available stock.",
          errorCode: "fulfillment_insufficient_stock",
        },
      });
      return;
    }

    const newOrderId = `ord-${Math.random().toString(16).slice(2, 10)}-4000-8000`;
    const newResId = `res-${Math.random().toString(16).slice(2, 10)}`;

    await callApiOrSimulate(
      "POST",
      "/api/orders",
      {
        customerId: "99999999-0000-0000-0000-000000000001",
        supplierId: "dddddddd-0000-0000-0000-000000000001",
      },
      201,
      {
        id: newOrderId,
        customerId: "99999999-0000-0000-0000-000000000001",
        supplierId: "dddddddd-0000-0000-0000-000000000001",
        status: 0,
        statusName: "Created",
      },
    );

    await callApiOrSimulate(
      "POST",
      `/api/orders/${newOrderId}/lines`,
      {
        skuId: selectedSku.skuId,
        quantity: orderQty,
        unitPrice: selectedSku.price,
        currency: "IRR",
      },
      204,
      { message: "Line added to order" },
    );

    await callApiOrSimulate(
      "POST",
      "/api/fulfillment/reserve",
      {
        orderId: newOrderId,
        skuId: selectedSku.skuId,
        supplierId: "dddddddd-0000-0000-0000-000000000001",
        quantity: orderQty,
      },
      200,
      {
        reservationId: newResId,
        optimisticConcurrencyVersion: selectedSku.version + 1,
      },
    );

    setSkus((prev) =>
      prev.map((s) =>
        s.skuId === selectedSku.skuId
          ? {
              ...s,
              availableStock: s.availableStock - orderQty,
              reservedStock: s.reservedStock + orderQty,
              version: s.version + 1,
            }
          : s,
      ),
    );
    setActiveOrderId(newOrderId);
    setReservationId(newResId);
    setOrderState(0);
    setPaymentStatus("ایجاد نشده");
  }

  async function step3ProcessPayment() {
    if (!activeOrderId) return;

    await callApiOrSimulate(
      "POST",
      `/api/orders/${activeOrderId}/payment-pending`,
      undefined,
      204,
      { orderId: activeOrderId, previousState: "Created", newState: "PaymentPending" },
    );
    setOrderState(1);

    const paymentId = `pay-${Math.random().toString(16).slice(2, 10)}`;
    const amount = selectedSku.price * orderQty;

    await callApiOrSimulate(
      "POST",
      "/api/payments",
      { orderId: activeOrderId, amount, currency: "IRR" },
      201,
      {
        id: paymentId,
        orderId: activeOrderId,
        amount,
        currency: "IRR",
        status: 1,
        statusName: "Authorized",
        providerTransactionId: `mock_tx_${paymentId.replace(/-/g, "")}`,
      },
    );

    await callApiOrSimulate(
      "POST",
      `/api/payments/${paymentId}/refresh`,
      undefined,
      204,
      { id: paymentId, gatewayStatus: "Settled" },
    );

    await callApiOrSimulate(
      "POST",
      `/api/orders/${activeOrderId}/paid`,
      undefined,
      204,
      { orderId: activeOrderId, previousState: "PaymentPending", newState: "Paid" },
    );

    setOrderState(2);
    setPaymentStatus("تسویه‌شده (Settled)");
  }

  async function step4ConfirmOrder() {
    if (!activeOrderId) return;

    await callApiOrSimulate(
      "POST",
      `/api/orders/${activeOrderId}/fulfillment-pending`,
      undefined,
      204,
      { orderId: activeOrderId, previousState: "Paid", newState: "FulfillmentPending" },
    );
    setOrderState(3);

    await callApiOrSimulate(
      "POST",
      `/api/orders/${activeOrderId}/confirm`,
      undefined,
      204,
      { orderId: activeOrderId, previousState: "FulfillmentPending", newState: "Confirmed" },
    );
    setOrderState(4);

    setSkus((prev) =>
      prev.map((s) =>
        s.skuId === selectedSku.skuId
          ? {
              ...s,
              reservedStock: Math.max(0, s.reservedStock - orderQty),
              version: s.version + 1,
            }
          : s,
      ),
    );
  }

  async function testInvalidTransition() {
    const targetOrder = activeOrderId ?? "ord-demo-invalid";
    await callApiOrSimulate(
      "POST",
      `/api/orders/${targetOrder}/confirm`,
      undefined,
      400,
      {
        type: "https://errors.example.com/domain",
        title: "تأیید ناموفق بود",
        status: 400,
        errorCode: "order_invalid_transition",
        detail: "سفارش نمی‌تواند بدون عبور از مراحل پرداخت و آماده‌سازی مستقیماً تأیید شود.",
      },
    );
  }

  function resetDemo() {
    setSkus(INITIAL_SKUS);
    setActiveOrderId(null);
    setReservationId(null);
    setOrderState(-1);
    setPaymentStatus("بدون تراکنش");
    setLogs([]);
    setSelectedLog(null);
  }

  return (
    <main>
      <header className="container" style={{ paddingBlock: 20 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 12 }}>
          <Link href="/" style={{ fontSize: "1.2rem", fontWeight: "bold", color: "var(--primary)" }}>
            یا علی — استودیو دمو و دیباگ
          </Link>
          <nav style={{ display: "flex", gap: 14, fontSize: "0.92rem", alignItems: "center", flexWrap: "wrap" }}>
            <Link href="/">صفحه اصلی</Link>
            <Link href="/shop">فروشگاه</Link>
            <Link href="/checkout">سبد خرید</Link>
            <Link href="/login">ورود</Link>
            <Link href="/app" className="btn btn-primary" style={{ padding: "6px 14px", fontSize: "0.86rem" }}>
              داشبورد و پنل ادمین
            </Link>
          </nav>
        </div>
      </header>

      <section className="container" style={{ paddingBlock: "20px 64px" }}>
        <div
          className="card"
          style={{
            marginBottom: 24,
            background: "var(--surface-alt)",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: 16,
          }}
        >
          <div>
            <span className="tag" style={{ marginBottom: 6 }}>
              Interactive Demo & Debugger
            </span>
            <h1 style={{ fontSize: "1.65rem", marginTop: 4 }}>
              شبیه‌ساز زنده معماری، موجودی و ماشین وضعیت سفارش‌ها
            </h1>
            <p style={{ color: "var(--ink-soft)", margin: "4px 0 0", fontSize: "0.92rem" }}>
              مراحل زیر را گام‌به‌گام اجرا کنید تا جریان داده بین ماژول‌های{" "}
              <span className="mono">Catalog → Geography → Inventory → Fulfillment → Orders → Payments</span>{" "}
              و لاگ‌های JSON را دیباگ کنید.
            </p>
          </div>
          <button type="button" onClick={resetDemo} className="btn btn-ghost">
            بازنشانی دمو (Reset)
          </button>
        </div>

        {/* Live Inventory & Optimistic Concurrency Table */}
        <div className="card" style={{ marginBottom: 24 }}>
          <h2 style={{ fontSize: "1.15rem", marginBottom: 12 }}>
            ۱. وضعیت لحظه‌ای موجودی انبار و کنترل هم‌زمانی (`inventory.stock_items`)
          </h2>
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.9rem" }}>
              <thead>
                <tr style={{ borderBottom: "2px solid var(--line)", textAlign: "right" }}>
                  <th style={{ padding: "10px 8px" }}>انتخاب</th>
                  <th style={{ padding: "10px 8px" }}>کد SKU</th>
                  <th style={{ padding: "10px 8px" }}>نام محصول</th>
                  <th style={{ padding: "10px 8px" }}>قیمت واحد (IRR)</th>
                  <th style={{ padding: "10px 8px" }}>موجودی آزاد</th>
                  <th style={{ padding: "10px 8px" }}>رزرو شده</th>
                  <th style={{ padding: "10px 8px" }}>نسخه هم‌زمانی (Version)</th>
                </tr>
              </thead>
              <tbody>
                {skus.map((s) => {
                  const isSelected = s.skuId === selectedSkuId;
                  return (
                    <tr
                      key={s.skuId}
                      onClick={() => setSelectedSkuId(s.skuId)}
                      style={{
                        borderBottom: "1px solid var(--line)",
                        background: isSelected ? "var(--surface-alt)" : "transparent",
                        cursor: "pointer",
                      }}
                    >
                      <td style={{ padding: "10px 8px" }}>
                        <input
                          type="radio"
                          name="selectedSku"
                          checked={isSelected}
                          onChange={() => setSelectedSkuId(s.skuId)}
                        />
                      </td>
                      <td className="mono" style={{ padding: "10px 8px", color: "var(--primary)", fontWeight: 700 }}>
                        {s.skuCode}
                      </td>
                      <td style={{ padding: "10px 8px", fontWeight: 500 }}>
                        {s.productName}{" "}
                        {s.requiresPrescription && (
                          <span className="tag" style={{ fontSize: "0.72rem", marginRight: 6 }}>
                            نسخه‌دار
                          </span>
                        )}
                      </td>
                      <td className="mono" style={{ padding: "10px 8px" }}>
                        {s.price.toLocaleString("fa-IR")}
                      </td>
                      <td className="mono" style={{ padding: "10px 8px", fontWeight: 700, color: "var(--primary)" }}>
                        {s.availableStock}
                      </td>
                      <td className="mono" style={{ padding: "10px 8px", color: "var(--amber)", fontWeight: 700 }}>
                        {s.reservedStock}
                      </td>
                      <td className="mono" style={{ padding: "10px 8px" }}>
                        v{s.version}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>

        {/* Step-by-Step Interactive Workflow & State Machine */}
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))",
            gap: 24,
            marginBottom: 24,
          }}
        >
          <div className="card" style={{ display: "grid", gap: 14 }}>
            <h2 style={{ fontSize: "1.15rem" }}>۲. اجرای گام‌به‌گام سناریوی خرید و دیباگ</h2>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 10 }}>
              <div>
                <label style={{ display: "block", fontSize: "0.8rem", fontWeight: 600, marginBottom: 4 }}>
                  تعداد سفارش
                </label>
                <input
                  type="number"
                  min={1}
                  className="mono"
                  value={orderQty}
                  onChange={(e) => setOrderQty(Number(e.target.value))}
                  style={{
                    width: "100%",
                    padding: "8px 10px",
                    borderRadius: 8,
                    border: "1px solid var(--line)",
                  }}
                />
              </div>
              <div>
                <label style={{ display: "block", fontSize: "0.8rem", fontWeight: 600, marginBottom: 4 }}>
                  عرض جغرافیایی (Lat)
                </label>
                <input
                  type="text"
                  className="mono"
                  value={latitude}
                  onChange={(e) => setLatitude(e.target.value)}
                  style={{
                    width: "100%",
                    padding: "8px 10px",
                    borderRadius: 8,
                    border: "1px solid var(--line)",
                  }}
                />
              </div>
              <div>
                <label style={{ display: "block", fontSize: "0.8rem", fontWeight: 600, marginBottom: 4 }}>
                  طول جغرافیایی (Lon)
                </label>
                <input
                  type="text"
                  className="mono"
                  value={longitude}
                  onChange={(e) => setLongitude(e.target.value)}
                  style={{
                    width: "100%",
                    padding: "8px 10px",
                    borderRadius: 8,
                    border: "1px solid var(--line)",
                  }}
                />
              </div>
            </div>

            <div style={{ display: "grid", gap: 10, marginTop: 4 }}>
              <button type="button" onClick={step1RankSuppliers} className="btn btn-ghost" style={{ justifyContent: "space-between" }}>
                <span>گام ۱: بررسی محدوده PostGIS و رتبه‌بندی تأمین‌کننده</span>
                <span className="mono" style={{ fontSize: "0.78rem", color: "var(--primary)" }}>
                  GET /api/fulfillment/...
                </span>
              </button>

              <button type="button" onClick={step2CreateOrderAndReserve} className="btn btn-primary" style={{ justifyContent: "space-between" }}>
                <span>گام ۲: ایجاد سفارش + رزرو موجودی (Optimistic Lock)</span>
                <span className="mono" style={{ fontSize: "0.78rem" }}>
                  POST /api/orders + /reserve
                </span>
              </button>

              <button
                type="button"
                onClick={step3ProcessPayment}
                disabled={!activeOrderId || orderState >= 2}
                className="btn btn-ghost"
                style={{ justifyContent: "space-between", opacity: !activeOrderId || orderState >= 2 ? 0.55 : 1 }}
              >
                <span>گام ۳: مجوز درگاه پرداخت و تسویه (Authorized → Settled)</span>
                <span className="mono" style={{ fontSize: "0.78rem", color: "var(--primary)" }}>
                  POST /api/payments
                </span>
              </button>

              <button
                type="button"
                onClick={step4ConfirmOrder}
                disabled={!activeOrderId || orderState !== 2}
                className="btn btn-ghost"
                style={{ justifyContent: "space-between", opacity: !activeOrderId || orderState !== 2 ? 0.55 : 1 }}
              >
                <span>گام ۴: تأیید نهایی سفارش و کسر قطعی موجودی</span>
                <span className="mono" style={{ fontSize: "0.78rem", color: "var(--primary)" }}>
                  POST /api/orders/.../confirm
                </span>
              </button>

              <button
                type="button"
                onClick={testInvalidTransition}
                className="btn btn-ghost"
                style={{ justifyContent: "space-between", borderColor: "var(--pulse)", color: "var(--pulse)" }}
              >
                <span>تست خطای دامنه: پرش غیرمجاز وضعیت سفارش (State Machine Guard)</span>
                <span className="mono" style={{ fontSize: "0.78rem" }}>
                  HTTP 400
                </span>
              </button>
            </div>
          </div>

          {/* State Machine Visualizer */}
          <div className="card" style={{ display: "grid", gap: 14 }}>
            <h2 style={{ fontSize: "1.15rem" }}>۳. وضعیت فعلی ماشین حالت سفارش (`Orders.Domain`)</h2>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10, fontSize: "0.88rem" }}>
              <div style={{ padding: 10, borderRadius: 8, background: "var(--surface-alt)" }}>
                <div style={{ color: "var(--ink-soft)", fontSize: "0.78rem" }}>شناسه سفارش فعال</div>
                <div className="mono" style={{ fontWeight: 700, color: "var(--primary)", marginTop: 2 }}>
                  {activeOrderId ?? "—"}
                </div>
              </div>
              <div style={{ padding: 10, borderRadius: 8, background: "var(--surface-alt)" }}>
                <div style={{ color: "var(--ink-soft)", fontSize: "0.78rem" }}>شناسه رزرو انبار</div>
                <div className="mono" style={{ fontWeight: 700, color: "var(--primary)", marginTop: 2 }}>
                  {reservationId ?? "—"}
                </div>
              </div>
              <div style={{ padding: 10, borderRadius: 8, background: "var(--surface-alt)" }}>
                <div style={{ color: "var(--ink-soft)", fontSize: "0.78rem" }}>ارزیابی PostGIS</div>
                <div style={{ fontWeight: 700, marginTop: 2 }}>
                  {geoInArea ? "داخل محدوده (بدون هزینه اضافه)" : "خارج از محدوده (+۲,۵۰۰,۰۰۰ ریال)"}
                </div>
              </div>
              <div style={{ padding: 10, borderRadius: 8, background: "var(--surface-alt)" }}>
                <div style={{ color: "var(--ink-soft)", fontSize: "0.78rem" }}>وضعیت پرداخت</div>
                <div style={{ fontWeight: 700, marginTop: 2 }}>{paymentStatus}</div>
              </div>
            </div>

            <div style={{ display: "grid", gap: 8 }}>
              {ORDER_STATES.slice(0, 5).map((st) => {
                const isCurrent = orderState === st.code;
                const isPassed = orderState > st.code;
                return (
                  <div
                    key={st.code}
                    style={{
                      padding: "8px 12px",
                      borderRadius: 8,
                      border: `1px solid ${isCurrent ? "var(--primary)" : "var(--line)"}`,
                      background: isCurrent
                        ? "var(--primary)"
                        : isPassed
                          ? "var(--surface-alt)"
                          : "var(--surface)",
                      color: isCurrent ? "#ffffff" : "var(--ink)",
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      fontSize: "0.88rem",
                      fontWeight: isCurrent ? 700 : 500,
                    }}
                  >
                    <span>{st.label}</span>
                    <span className="mono" style={{ fontSize: "0.78rem" }}>
                      {isCurrent ? "● ACTIVE" : isPassed ? "✓ DONE" : "PENDING"}
                    </span>
                  </div>
                );
              })}
            </div>
          </div>
        </div>

        {/* Request / Response Inspector */}
        <div className="card">
          <h2 style={{ fontSize: "1.15rem", marginBottom: 12 }}>
            ۴. کنسول دیباگ و بازرسی ترافیک API (Request / Response Inspector)
          </h2>

          {logs.length === 0 ? (
            <div style={{ color: "var(--ink-soft)", fontSize: "0.92rem" }}>
              هنوز درخواستی ثبت نشده است. روی یکی از دکمه‌های گام ۱ تا ۴ در بالا کلیک کنید تا جزئیات کامل درخواست و پاسخ JSON را ببینید.
            </div>
          ) : (
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(300px, 1fr))",
                gap: 16,
              }}
            >
              <div style={{ display: "grid", gap: 8, maxHeight: 340, overflowY: "auto" }}>
                {logs.map((log) => (
                  <div
                    key={log.id}
                    onClick={() => setSelectedLog(log)}
                    style={{
                      padding: "10px 12px",
                      borderRadius: 8,
                      border: `1px solid ${selectedLog?.id === log.id ? "var(--primary)" : "var(--line)"}`,
                      background: selectedLog?.id === log.id ? "var(--surface-alt)" : "var(--surface)",
                      cursor: "pointer",
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      gap: 8,
                    }}
                  >
                    <div>
                      <span
                        className="mono"
                        style={{
                          fontWeight: 700,
                          color: log.status >= 400 ? "var(--pulse)" : "var(--primary)",
                          marginLeft: 8,
                        }}
                      >
                        {log.method}
                      </span>
                      <span className="mono" style={{ fontSize: "0.82rem" }}>
                        {log.endpoint}
                      </span>
                    </div>
                    <div className="mono" style={{ fontSize: "0.78rem", color: "var(--ink-soft)" }}>
                      HTTP {log.status} · {log.durationMs}ms
                    </div>
                  </div>
                ))}
              </div>

              {selectedLog && (
                <div
                  className="mono"
                  style={{
                    background: "#0f172a",
                    color: "#e2e8f0",
                    padding: 16,
                    borderRadius: 10,
                    fontSize: "0.8rem",
                    overflowX: "auto",
                    direction: "ltr",
                    textAlign: "left",
                  }}
                >
                  <div style={{ color: "#93c5fd", marginBottom: 8 }}>
                    {selectedLog.method} {selectedLog.endpoint} → HTTP {selectedLog.status} ({selectedLog.mode})
                  </div>
                  {selectedLog.requestBody !== undefined && (
                    <>
                      <div style={{ color: "#94a3b8", marginTop: 8 }}>// Request Payload:</div>
                      <pre style={{ margin: "4px 0 12px", whiteSpace: "pre-wrap" }}>
                        {JSON.stringify(selectedLog.requestBody, null, 2)}
                      </pre>
                    </>
                  )}
                  <div style={{ color: "#94a3b8" }}>// Response Payload:</div>
                  <pre style={{ margin: "4px 0 0", whiteSpace: "pre-wrap", color: "#86efac" }}>
                    {JSON.stringify(selectedLog.responseBody, null, 2)}
                  </pre>
                </div>
              )}
            </div>
          )}
        </div>
      </section>
    </main>
  );
}
