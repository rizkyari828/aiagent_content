import type { CSSProperties } from "react";

export interface SkeletonProps {
  width?: number | string;
  height?: number | string;
  radius?: string;
  className?: string;
}

export function Skeleton({ width = "100%", height = 12, radius, className }: SkeletonProps) {
  const style: CSSProperties = { width, height };

  if (radius) {
    style.borderRadius = radius;
  }

  return (
    <span
      className={["skeleton", className ?? ""].filter(Boolean).join(" ")}
      style={style}
      aria-hidden="true"
    />
  );
}
