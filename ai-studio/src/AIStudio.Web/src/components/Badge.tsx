import type { ReactNode } from "react";
import { useI18n } from "../i18n/i18n";

export type BadgeTone = "neutral" | "accent" | "success" | "warning" | "danger" | "muted";

export interface BadgeProps {
  tone?: BadgeTone;
  children: ReactNode;
}

export function Badge({ tone = "neutral", children }: BadgeProps) {
  return <span className={`badge badge--${tone}`}>{children}</span>;
}

const STATUS_TONES: Record<string, BadgeTone> = {
  draft: "neutral",
  researching: "accent",
  idea_review: "accent",
  scripting: "accent",
  script_review: "accent",
  producing: "warning",
  final_review: "warning",
  ready_to_publish: "success",
  published: "success",
  archived: "muted",
};

export function StatusBadge({ status }: { status: string }) {
  const { t } = useI18n();
  const known = Object.hasOwn(STATUS_TONES, status);
  const tone = STATUS_TONES[status] ?? "neutral";
  const label = known
    ? t(`projects.status.${status}`)
    : t("projects.status.unknown");

  return (
    <Badge tone={tone}>
      <span className="badge__dot" aria-hidden="true" />
      {label}
    </Badge>
  );
}
