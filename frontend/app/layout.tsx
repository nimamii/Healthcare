import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "یا علی — مارکت‌پلیس تجهیزات پزشکی",
  description:
    "بازار دوطرفه تجهیزات پزشکی؛ تأمین‌کننده و خریدر، با همگام‌سازی لحظه‌ای موجودی.",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="fa" dir="rtl">
      <body>{children}</body>
    </html>
  );
}
