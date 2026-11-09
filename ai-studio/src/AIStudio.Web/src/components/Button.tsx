import type { ButtonHTMLAttributes, ReactNode } from "react";

export type ButtonVariant = "primary" | "secondary" | "ghost" | "danger";
export type ButtonSize = "md" | "sm";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
  loadingLabel?: string;
  children: ReactNode;
}

export function Button({
  variant = "secondary",
  size = "md",
  loading = false,
  loadingLabel,
  children,
  className,
  disabled,
  type = "button",
  ...props
}: ButtonProps) {
  const classes = [
    "button",
    `button--${variant}`,
    size === "sm" ? "button--sm" : "",
    className ?? "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <button
      {...props}
      type={type}
      className={classes}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
    >
      {loading ? <span className="button__spinner" aria-hidden="true" /> : null}
      {loading && loadingLabel ? loadingLabel : children}
    </button>
  );
}
