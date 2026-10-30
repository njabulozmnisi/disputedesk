/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** `off` builds the app without browser tracing; any other value, or none, leaves it on. */
  readonly VITE_BROWSER_TELEMETRY?: string;
}
