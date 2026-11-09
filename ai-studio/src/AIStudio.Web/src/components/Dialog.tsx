import {
  useEffect,
  useId,
  useRef,
  type KeyboardEvent,
  type MouseEvent as ReactMouseEvent,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";
import { useI18n } from "../i18n/i18n";
import { IconClose } from "./icons";

const FOCUSABLE_SELECTOR = [
  "a[href]",
  "button:not([disabled])",
  "textarea:not([disabled])",
  "input:not([disabled])",
  "select:not([disabled])",
  '[tabindex]:not([tabindex="-1"])',
].join(",");

export interface DialogProps {
  open: boolean;
  title: string;
  description?: string;
  onClose: () => void;
  dismissible?: boolean;
  children: ReactNode;
}

export function Dialog({
  open,
  title,
  description,
  onClose,
  dismissible = true,
  children,
}: DialogProps) {
  const { t } = useI18n();
  const titleId = useId();
  const descriptionId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);
  const onCloseRef = useRef(onClose);
  const dismissibleRef = useRef(dismissible);

  onCloseRef.current = onClose;
  dismissibleRef.current = dismissible;

  useEffect(() => {
    if (!open) {
      return;
    }

    const previous =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const originalOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    const node = dialogRef.current;
    const autofocus = node?.querySelector<HTMLElement>("[data-autofocus]");
    const first = node?.querySelector<HTMLElement>(FOCUSABLE_SELECTOR);
    (autofocus ?? first ?? node)?.focus();

    return () => {
      document.body.style.overflow = originalOverflow;
      previous?.focus();
    };
  }, [open]);

  if (!open) {
    return null;
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape") {
      if (!dismissibleRef.current) {
        return;
      }

      event.stopPropagation();
      onCloseRef.current();
      return;
    }

    if (event.key !== "Tab") {
      return;
    }

    const node = dialogRef.current;

    if (!node) {
      return;
    }

    const focusables = Array.from(
      node.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
    ).filter((element) => !element.hasAttribute("disabled"));

    if (focusables.length === 0) {
      event.preventDefault();
      node.focus();
      return;
    }

    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    const active = document.activeElement;

    if (event.shiftKey) {
      if (active === first || !node.contains(active)) {
        event.preventDefault();
        last.focus();
      }
      return;
    }

    if (active === last || !node.contains(active)) {
      event.preventDefault();
      first.focus();
    }
  };

  const handleBackdropMouseDown = (event: ReactMouseEvent<HTMLDivElement>) => {
    if (event.target === event.currentTarget && dismissibleRef.current) {
      onCloseRef.current();
    }
  };

  return createPortal(
    <div className="dialog-backdrop" onMouseDown={handleBackdropMouseDown}>
      <div
        ref={dialogRef}
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        tabIndex={-1}
        onKeyDown={handleKeyDown}
      >
        <header className="dialog__header">
          <div className="dialog__heading">
            <h2 className="dialog__title" id={titleId}>
              {title}
            </h2>
            {description ? (
              <p className="dialog__description" id={descriptionId}>
                {description}
              </p>
            ) : null}
          </div>
          <button
            type="button"
            className="dialog__close"
            onClick={() => onCloseRef.current()}
            aria-label={t("action.close")}
            disabled={!dismissible}
          >
            <IconClose size={16} />
          </button>
        </header>
        <div className="dialog__content">{children}</div>
      </div>
    </div>,
    document.body,
  );
}
