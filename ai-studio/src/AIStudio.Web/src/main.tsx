import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { ToastProvider } from "./components/Toast";
import { I18nProvider } from "./i18n/i18n";
import "./styles/tokens.css";
import "./styles/app.css";

const rootElement = document.getElementById("root");

if (!rootElement) {
  throw new Error("Root element was not found.");
}

createRoot(rootElement).render(
  <StrictMode>
    <I18nProvider>
      <ToastProvider>
        <App />
      </ToastProvider>
    </I18nProvider>
  </StrictMode>,
);
