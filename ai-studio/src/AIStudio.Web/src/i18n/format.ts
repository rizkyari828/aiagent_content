import { useMemo } from "react";
import { useI18n, type Locale } from "./i18n";

const INTL_LOCALES: Record<Locale, string> = {
  id: "id-ID",
  en: "en-US",
};

const dateTimeFormatCache = new Map<string, Intl.DateTimeFormat>();
const numberFormatCache = new Map<string, Intl.NumberFormat>();

function dateTimeFormat(locale: Locale): Intl.DateTimeFormat {
  const cacheKey = `date-time:${locale}`;
  const cached = dateTimeFormatCache.get(cacheKey);

  if (cached) {
    return cached;
  }

  const formatter = new Intl.DateTimeFormat(INTL_LOCALES[locale], {
    dateStyle: "medium",
    timeStyle: "short",
  });
  dateTimeFormatCache.set(cacheKey, formatter);
  return formatter;
}

function numberFormat(locale: Locale): Intl.NumberFormat {
  const cacheKey = `number:${locale}`;
  const cached = numberFormatCache.get(cacheKey);

  if (cached) {
    return cached;
  }

  const formatter = new Intl.NumberFormat(INTL_LOCALES[locale]);
  numberFormatCache.set(cacheKey, formatter);
  return formatter;
}

/** Formats an ISO timestamp for the active locale, or an em dash when invalid. */
export function formatDateTime(value: string | Date, locale: Locale): string {
  const date = value instanceof Date ? value : new Date(value);

  if (Number.isNaN(date.getTime())) {
    return "—";
  }

  return dateTimeFormat(locale).format(date);
}

export function formatNumber(value: number, locale: Locale): string {
  return numberFormat(locale).format(value);
}

/** Locale-aware formatters bound to the active i18n locale. */
export function useFormatters() {
  const { locale } = useI18n();

  return useMemo(
    () => ({
      locale,
      dateTime: (value: string | Date) => formatDateTime(value, locale),
      number: (value: number) => formatNumber(value, locale),
    }),
    [locale],
  );
}
