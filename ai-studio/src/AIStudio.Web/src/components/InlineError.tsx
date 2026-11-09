import { useI18n } from "../i18n/i18n";
import { Button } from "./Button";
import { IconAlert } from "./icons";

export interface InlineErrorProps {
  title: string;
  detail?: string;
  code?: string;
  actionLabel?: string;
  onAction?: () => void;
}

export function InlineError({
  title,
  detail,
  code,
  actionLabel,
  onAction,
}: InlineErrorProps) {
  const { t } = useI18n();

  return (
    <div className="inline-error" role="alert" aria-live="assertive">
      <span className="inline-error__icon" aria-hidden="true">
        <IconAlert size={18} />
      </span>
      <div className="inline-error__body">
        <p className="inline-error__title">{title}</p>
        {detail ? <p className="inline-error__detail">{detail}</p> : null}
        {code ? <p className="inline-error__code">{t("error.code", { code })}</p> : null}
      </div>
      {actionLabel && onAction ? (
        <div className="inline-error__action">
          <Button size="sm" onClick={onAction}>
            {actionLabel}
          </Button>
        </div>
      ) : null}
    </div>
  );
}
