"use client";

import { useState } from "react";
import Link from "next/link";

export default function LoginPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError(null);

    try {
      const res = await fetch("/api/identity/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });

      if (!res.ok) {
        const body = await res.json().catch(() => null);
        setError(body?.detail ?? "ورود ناموفق بود. ایمیل یا رمز عبور اشتباه است.");
        return;
      }

      setSuccess(true);
      // Redirect to dashboard on success.
      window.location.href = "/app";
    } catch {
      setError("خطا در ارتباط با سرور. دوباره تلاش کن.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main
      style={{
        minHeight: "100vh",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: 24,
      }}
    >
      <div style={{ width: "100%", maxWidth: 400 }}>
        <div style={{ textAlign: "center", marginBottom: 24 }}>
          <Link href="/" style={{ fontSize: "1.4rem", fontWeight: "bold" }}>
            یا علی
          </Link>
          <p style={{ color: "var(--ink-soft)", marginTop: 4 }}>ورود به حساب کاربری</p>
        </div>

        <div className="card">
          {success ? (
            <div style={{ textAlign: "center", color: "var(--primary)" }}>
              ورود موفق! در حال انتقال...
            </div>
          ) : (
            <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 16 }}>
              <div>
                <label style={{ display: "block", marginBottom: 6, fontSize: "0.9rem", fontWeight: 600 }}>
                  ایمیل
                </label>
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  required
                  style={{
                    width: "100%",
                    padding: "10px 14px",
                    borderRadius: 8,
                    border: "1px solid var(--line)",
                    fontSize: "1rem",
                    fontFamily: "inherit",
                  }}
                />
              </div>
              <div>
                <label style={{ display: "block", marginBottom: 6, fontSize: "0.9rem", fontWeight: 600 }}>
                  رمز عبور
                </label>
                <input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                  style={{
                    width: "100%",
                    padding: "10px 14px",
                    borderRadius: 8,
                    border: "1px solid var(--line)",
                    fontSize: "1rem",
                    fontFamily: "inherit",
                  }}
                />
              </div>

              {error && (
                <div
                  style={{
                    color: "var(--pulse)",
                    fontSize: "0.88rem",
                    padding: "8px 12px",
                    borderRadius: 6,
                    background: "var(--surface-alt)",
                  }}
                >
                  {error}
                </div>
              )}

              <button
                type="submit"
                className="btn btn-primary"
                disabled={loading}
                style={{ justifyContent: "center" }}
              >
                {loading ? "در حال ورود..." : "ورود"}
              </button>

              <p style={{ textAlign: "center", fontSize: "0.88rem", color: "var(--ink-soft)", margin: 0 }}>
                حساب نداری؟{" "}
                <Link href="/register" style={{ color: "var(--primary)", fontWeight: 600 }}>
                  ثبت‌نام
                </Link>
              </p>
            </form>
          )}
        </div>
      </div>
    </main>
  );
}
