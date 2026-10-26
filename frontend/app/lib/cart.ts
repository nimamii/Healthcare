"use client";

export type CartItem = {
  productId: string;
  productName: string;
  slug: string;
  skuId: string;
  skuCode: string;
  supplierId: string;
  unitPrice: number;
  currency: string;
  quantity: number;
  requiresPrescription: boolean;
};

export type SavedOrder = {
  id: string;
  supplierId: string;
  totalAmount: number;
  currency: string;
  status: string;
  paymentId?: string;
  createdAt: string;
  items: CartItem[];
};

export type AuthSession = {
  accessToken: string;
  userId: string | null;
  email: string | null;
  roles: string[];
};

const CART_KEY = "hc_cart_items";
const ORDERS_KEY = "hc_saved_orders";

export function getCart(): CartItem[] {
  if (typeof window === "undefined") return [];
  try {
    const raw = localStorage.getItem(CART_KEY);
    return raw ? (JSON.parse(raw) as CartItem[]) : [];
  } catch {
    return [];
  }
}

export function saveCart(items: CartItem[]): void {
  if (typeof window === "undefined") return;
  localStorage.setItem(CART_KEY, JSON.stringify(items));
  window.dispatchEvent(new Event("hc-cart-updated"));
}

export function addToCart(item: Omit<CartItem, "quantity">, quantity = 1): CartItem[] {
  const current = getCart();
  const existingIndex = current.findIndex((x) => x.skuId === item.skuId);
  if (existingIndex >= 0) {
    current[existingIndex] = {
      ...current[existingIndex],
      quantity: current[existingIndex].quantity + quantity,
    };
  } else {
    current.push({ ...item, quantity });
  }
  saveCart(current);
  return current;
}

export function updateCartQuantity(skuId: string, quantity: number): CartItem[] {
  const current = getCart();
  const next =
    quantity <= 0
      ? current.filter((x) => x.skuId !== skuId)
      : current.map((x) => (x.skuId === skuId ? { ...x, quantity } : x));
  saveCart(next);
  return next;
}

export function clearCart(): void {
  saveCart([]);
}

export function getSavedOrders(): SavedOrder[] {
  if (typeof window === "undefined") return [];
  try {
    const raw = localStorage.getItem(ORDERS_KEY);
    return raw ? (JSON.parse(raw) as SavedOrder[]) : [];
  } catch {
    return [];
  }
}

export function saveOrderRecord(order: SavedOrder): void {
  if (typeof window === "undefined") return;
  const current = getSavedOrders();
  localStorage.setItem(ORDERS_KEY, JSON.stringify([order, ...current]));
}

export function getAuthSession(): AuthSession | null {
  if (typeof window === "undefined") return null;
  const accessToken = localStorage.getItem("hc_access_token");
  if (!accessToken) return null;

  try {
    const parts = accessToken.split(".");
    if (parts.length < 2) {
      return { accessToken, userId: null, email: null, roles: [] };
    }
    const base64 = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const json = decodeURIComponent(
      atob(base64)
        .split("")
        .map((c) => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2))
        .join(""),
    );
    const payload = JSON.parse(json) as Record<string, unknown>;

    const userId =
      (payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"] as string) ??
      (payload["sub"] as string) ??
      (payload["nameid"] as string) ??
      null;

    const email =
      (payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"] as string) ??
      (payload["email"] as string) ??
      null;

    const rawRole =
      payload["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] ??
      payload["role"];

    const roles = Array.isArray(rawRole)
      ? (rawRole as string[])
      : typeof rawRole === "string"
        ? [rawRole]
        : [];

    return { accessToken, userId, email, roles };
  } catch {
    return { accessToken, userId: null, email: null, roles: [] };
  }
}

export function clearAuthSession(): void {
  if (typeof window === "undefined") return;
  localStorage.removeItem("hc_access_token");
  localStorage.removeItem("hc_refresh_token");
  localStorage.removeItem("hc_expires_at");
}
