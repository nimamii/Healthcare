import Link from "next/link";
import ShopClient from "./ShopClient";

type Product = {
  id: string;
  name: string;
  slug: string;
  categoryId: string;
  requiresPrescription: boolean;
  isActive: boolean;
};

const DEMO_PRODUCTS: Product[] = [
  {
    id: "aaaaaaaa-0000-0000-0000-000000000001",
    name: "مانیتور بیمار قابل حمل",
    slug: "portable-patient-monitor",
    categoryId: "33333333-3333-3333-3333-333333333333",
    requiresPrescription: false,
    isActive: true,
  },
  {
    id: "aaaaaaaa-0000-0000-0000-000000000002",
    name: "دستگاه فشارسنج دیجیتال",
    slug: "digital-blood-pressure",
    categoryId: "22222222-2222-2222-2222-222222222222",
    requiresPrescription: false,
    isActive: true,
  },
  {
    id: "aaaaaaaa-0000-0000-0000-000000000003",
    name: "پالس اکسیمتر انگشتی",
    slug: "fingertip-pulse-oximeter",
    categoryId: "22222222-2222-2222-2222-222222222222",
    requiresPrescription: false,
    isActive: true,
  },
  {
    id: "aaaaaaaa-0000-0000-0000-000000000004",
    name: "کمک تنفسی خانگی",
    slug: "home-ventilator",
    categoryId: "33333333-3333-3333-3333-333333333333",
    requiresPrescription: true,
    isActive: true,
  },
  {
    id: "aaaaaaaa-0000-0000-0000-000000000005",
    name: "دستگاه سونوگرافی قابل حمل",
    slug: "portable-ultrasound",
    categoryId: "33333333-3333-3333-3333-333333333333",
    requiresPrescription: true,
    isActive: true,
  },
];

async function getProducts(): Promise<{ products: Product[]; isDemoFallback: boolean }> {
  try {
    const res = await fetch(
      `${process.env.API_URL ?? "http://localhost:8090"}/api/catalog/products`,
      { cache: "no-store" },
    );
    if (!res.ok) return { products: DEMO_PRODUCTS, isDemoFallback: true };
    const data = (await res.json()) as Product[];
    return data.length > 0
      ? { products: data, isDemoFallback: false }
      : { products: DEMO_PRODUCTS, isDemoFallback: true };
  } catch {
    return { products: DEMO_PRODUCTS, isDemoFallback: true };
  }
}

export default async function ShopPage() {
  const { products, isDemoFallback } = await getProducts();

  return (
    <main>
      <header className="container" style={{ paddingBlock: 20 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <Link href="/" style={{ fontSize: "1.2rem", fontWeight: "bold", color: "var(--primary)" }}>
            یا علی
          </Link>
          <nav style={{ display: "flex", gap: 16, fontSize: "0.95rem", alignItems: "center" }}>
            <Link href="/about">درباره ما</Link>
            <Link href="/contact">تماس با ما</Link>
            <Link href="/checkout">سبد خرید</Link>
            <Link href="/login">ورود</Link>
            <Link href="/app" className="btn btn-primary" style={{ padding: "6px 14px", fontSize: "0.88rem" }}>
              ورود به پنل
            </Link>
          </nav>
        </div>
      </header>

      <section className="container" style={{ paddingBlock: "36px 80px" }}>
        <ShopClient products={products} isDemoFallback={isDemoFallback} />
      </section>
    </main>
  );
}
