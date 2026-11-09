import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { useI18n } from "../i18n/i18n";
import { IconClose } from "./icons";

export type ToastTone = "info" | "success" | "error";

interface ToastItem {
  id: number;
  message: string;
  tone: ToastTone;
}

interface ToastContextValue {
  showToast: (message: string, tone?: ToastTone) => void;
}

const AUTO_DISMISS_MS = 4500;
const ToastContext = createContext<ToastContextValue | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const nextId = useRef(0);
  const timers = useRef(new Map<number, number>());

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((toast) => toast.id !== id));
    const timer = timers.current.get(id);

    if (timer !== undefined) {
      window.clearTimeout(timer);
      timers.current.delete(id);
    }
  }, []);

  const showToast = useCallback(
    (message: string, tone: ToastTone = "info") => {
      const id = (nextId.current += 1);
      setToasts((current) => [...current, { id, message, tone }]);
      const timer = window.setTimeout(() => dismiss(id), AUTO_DISMISS_MS);
      timers.current.set(id, timer);
    },
    [dismiss],
  );

  useEffect(() => {
    const pending = timers.current;

    return () => {
      for (const timer of pending.values()) {
        window.clearTimeout(timer);
      }

      pending.clear();
    };
  }, []);

  const value = useMemo<ToastContextValue>(() => ({ showToast }), [showToast]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toast-region" role="status" aria-live="polite" aria-atomic="false">
        {toasts.map((toast) => (
          <div key={toast.id} className={`toast toast--${toast.tone}`}>
            <p className="toast__message">{toast.message}</p>
            <button
              type="button"
              className="toast__close"
              onClick={() => dismiss(toast.id)}
              aria-label={t("action.close")}
            >
              <IconClose size={14} />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);

  if (!context) {
    throw new Error("useToast must be used inside ToastProvider.");
  }

  return context;
}
