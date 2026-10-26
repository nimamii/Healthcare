"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import {
  AuthSession,
  clearAuthSession,
  getAuthSession,
  getCart,
  getSavedOrders,
  SavedOrder,
} from "../lib/cart";

export default function DashboardClient() {
  const [session, setSession] = useState<AuthSession | null>(null);
  const [orders, setOrders] = useState<SavedOrder[]>([]);
  const [cartCount, setCartCount] = useState(0);

  useEffect(() => {
    setSession(getAuthSession());
    setOrders(getSavedOrders());
    setCartCount(getCart().reduce((s, i) => s + i.quantity, 0));
  }, []);

  async function handleLogout() {
    if (session?.accessToken) {
      try {
        await fetch("/api/identity/logout", {
          method: "POST",
          headers: { Authorization: `Bearer ${session.accessToken}` },
        });
      } catch {
        // Ignore network errors on logout
      }
    }
    clearAuthSession();
    setSession(null);
  }

  return (
    <div style={{ display: "grid", gap: 24, marginBottom: 32 }}>
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: 16,
        }}
      >
        <div>
          <div style={{ fontSize: "0.85rem", color: "var(--ink-soft)" }}>وضعیت نشست کاربری</div>
          {session ? (
            <div style={{ marginTop: 4 }}>
              <strong>{session.email ?? "کاربر احراز هویت‌شده"}</strong>{" "}
              {session.roles.length > 0 && (
                <span className="tag" style={{ marginRight: 8 }}>
                  {session.roles.join(", ")}
                </span>
              )}
            </div>
          ) : (
            <div style={{ marginTop: 4, color: "var(--ink-soft)" }}>
              شما هنوز وارد حساب کاربری نشده‌اید.
            </div>
          )}
        </div>

        <div style={{ display: "flex", gap: 10, alignItems: "center", flexWrap: "wrap" }}>
          <Link href="/demo" className="btn btn-ghost">
            دمو و دیباگ
          </Link>
          <Link href="/shop" className="btn btn-ghost">
            فروشگاه
          </Link>
          <Link href="/checkout" className="btn btn-primary">
            سبد خرید ({cartCount})
          </Link>
          {session ? (
            <button type="button" onClick={handleLogout} className="btn btn-ghost">
              خروج از حساب
            </button>
          ) : (
            <Link href="/login" className="btn btn-ghost">
              ورود / ثبت‌نام
            </Link>
          )}
        </div>
      </div>

      <div>
        <h2 style={{ fontSize: "1.15rem", marginBottom: 12 }}>سفارش‌های اخیر شما</h2>
        {orders.length === 0 ? (
          <div className="card" style={{ color: "var(--ink-soft)" }}>
            هنوز سفارشی ثبت نشده است. از{" "}
            <Link href="/shop" style={{ color: "var(--primary)", fontWeight: 600 }}>
              فروشگاه
            </Link>{" "}
            کالای مورد نظر خود را انتخاب کنید.
          </div>
        ) : (
          <div style={{ display: "grid", gap: 12 }}>
            {orders.map((ord) => (
              <div
                key={ord.id}
                className="card"
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  flexWrap: "wrap",
                  gap: 12,
                }}
              >
                <div>
                  <div className="mono" style={{ fontSize: "0.85rem", color: "var(--primary)" }}>
                    #{ord.id}
                  </div>
                  <div style={{ fontSize: "0.9rem", color: "var(--ink-soft)", marginTop: 4 }}>
                    تاریخ: {ord.createdAt} · تعداد اقلام:{" "}
                    {ord.items.reduce((s, i) => s + i.quantity, 0)}
                  </div>
                </div>
                <div style={{ textAlign: "left" }}>
                  <span className="tag">{ord.status}</span>
                  <div
                    className="mono"
                    style={{ fontWeight: 700, color: "var(--ink)", marginTop: 6 }}
                  >
                    {ord.totalAmount.toLocaleString("fa-IR")} {ord.currency}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
