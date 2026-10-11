import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./app";
import { ensureSignedIn } from "./lib/auth";
import "./styles.css";

void ensureSignedIn().then(() => createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
));
