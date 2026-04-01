import { retryLogger } from "./logger.ts";

export const DOMAIN_RELOAD_CANCELLED = -32001;
const GENERIC_INTERNAL_ERROR = -32603;
const PROBE_MESSAGE = { jsonrpc: "2.0", id: "__bridge_probe__", method: "ping" } as const;

type ToolCallMessage = {
  method?: string;
  params?: {
    name?: string;
    arguments?: {
      action?: unknown;
      testMode?: unknown;
      test_mode?: unknown;
    };
  };
};

function normalizeMode(value: unknown): string {
  if (typeof value !== "string") return "";
  return value.replace(/_/g, "").toLowerCase();
}

/**
 * Retry scope guard: only PlayMode unity_test run requests are retried automatically.
 * This avoids replaying arbitrary side-effectful tool calls.
 */
export function isPlayModeUnityTestRunRequest(message: unknown): boolean {
  const msg = message as ToolCallMessage;
  if (msg?.method !== "tools/call") return false;
  if (msg?.params?.name !== "unity_test") return false;

  const args = msg.params?.arguments ?? {};
  const action = typeof args.action === "string" ? args.action.toLowerCase() : "";
  if (action !== "run") return false;

  const mode = normalizeMode(args.testMode ?? args.test_mode);
  return mode === "playmode";
}

/**
 * Connection-error retries should be limited to idempotent requests (safe to replay)
 * and the specific PlayMode unity_test run flow.
 */
export function shouldRetryOnConnectionError(message: unknown): boolean {
  const msg = message as ToolCallMessage;
  switch (msg?.method) {
    case "initialize":
    case "ping":
    case "tools/list":
    case "resources/list":
    case "resources/templates/list":
    case "prompts/list":
      return true;
    case "tools/call": {
      if (msg?.params?.name !== "unity_test") return false;
      const args = msg.params?.arguments ?? {};
      const action = typeof args.action === "string" ? args.action.toLowerCase() : "";
      // list is always idempotent; run is only retried for PlayMode
      if (action === "list") return true;
      return isPlayModeUnityTestRunRequest(message);
    }
    default:
      return false;
  }
}

/**
 * Precise detection: new server with McpCancellationContext returns -32001 +
 * data.reason == "domain_reload".
 */
export function isCancelledByReload(body: string | null): boolean {
  if (!body) return false;
  try {
    const parsed = JSON.parse(body);
    return (
      parsed?.error?.code === DOMAIN_RELOAD_CANCELLED &&
      parsed?.error?.data?.reason === "domain_reload"
    );
  } catch {
    return false;
  }
}

/**
 * Legacy fallback: old Unity package (pre-McpCancellationContext) returns -32603 +
 * a message containing "cancelled". Narrowed to PlayMode unity_test run calls only
 * to avoid false positives on other -32603 cancellations.
 */
export function isCancelledByReloadLegacy(
  body: string | null,
  message: unknown
): boolean {
  if (!body) return false;
  if (!isPlayModeUnityTestRunRequest(message)) return false;
  try {
    const parsed = JSON.parse(body);
    return (
      parsed?.error?.code === -32603 &&
      typeof parsed?.error?.message === "string" &&
      parsed.error.message.toLowerCase().includes("cancelled")
    );
  } catch {
    return false;
  }
}

/**
 * Detects a syntactically empty HTTP response body.
 * Null means "no body by design" (e.g. HTTP 202 notification response), so only
 * non-null whitespace-only bodies are treated as empty.
 */
export function isBlankJsonBody(body: string | null): boolean {
  return body !== null && body.trim().length === 0;
}

export type ForwardFn = (
  message: unknown,
  port: number
) => Promise<{ body: string | null; headers: Headers; status: number }>;

export type ReadPortFn = () => number;

function isCancelledResponse(body: string | null): boolean {
  if (!body) return false;
  try {
    const parsed = JSON.parse(body);
    if (Array.isArray(parsed)) return false;

    const code = parsed?.error?.code;
    if (code === DOMAIN_RELOAD_CANCELLED) return true;
    if (code !== GENERIC_INTERNAL_ERROR) return false;

    return (
      typeof parsed?.error?.message === "string" &&
      parsed.error.message.toLowerCase().includes("cancelled")
    );
  } catch {
    return false;
  }
}

async function withTimeout<T>(promise: Promise<T>, timeoutMs: number): Promise<T> {
  let timeoutHandle: ReturnType<typeof setTimeout> | null = null;
  const timeoutPromise = new Promise<never>((_, reject) => {
    timeoutHandle = setTimeout(
      () => reject(new Error(`Probe timeout after ${timeoutMs}ms`)),
      timeoutMs
    );
  });

  try {
    return await Promise.race([promise, timeoutPromise]);
  } finally {
    if (timeoutHandle !== null) clearTimeout(timeoutHandle);
  }
}

async function isServerReady(
  forwardToUnity: ForwardFn,
  port: number,
  probeTimeoutMs: number
): Promise<boolean> {
  try {
    const probeResult = await withTimeout(
      forwardToUnity(PROBE_MESSAGE, port),
      probeTimeoutMs
    );
    return !isCancelledResponse(probeResult.body);
  } catch {
    return false;
  }
}

/**
 * Poll until the server is back up, then retry the request once.
 * Used after a domain-reload cancellation where recovery is expected.
 * Flat 1s interval × maxAttempts (default 10s total sleep budget).
 */
export async function waitForServerAndRetry(
  message: unknown,
  forwardToUnity: ForwardFn,
  readPort: ReadPortFn,
  setCurrentPort: (port: number) => void,
  maxAttempts = 10,
  intervalMs = 1000,
  probeTimeoutMs = 3000
): Promise<{ body: string | null; headers: Headers; status: number } | null> {
  for (let i = 0; i < maxAttempts; i++) {
    await sleep(intervalMs);
    let probePort: number;
    try {
      probePort = readPort();
    } catch {
      continue; // Port file not yet written, keep waiting
    }
    const ready = await isServerReady(forwardToUnity, probePort, probeTimeoutMs);
    if (!ready) continue;

    try {
      const result = await forwardToUnity(message, probePort);
      setCurrentPort(probePort); // Only promote on confirmed success
      retryLogger.info(
        "Server back up after {attempt} attempt(s), retrying request.",
        { attempt: i + 1 }
      );
      return result;
    } catch {
      // Request replay failed, keep polling
    }
  }
  retryLogger.warning("Server did not restart within timeout, giving up.");
  return null;
}

/**
 * Exponential-backoff poll for connection errors.
 * Starts at 500ms, doubles each attempt up to 8s per attempt.
 * Sleep budget: 500+1000+2000+4000+8000+8000 ≈ 23.5s (+ request time).
 * Fails faster for permanent outages while still recovering from slow reloads.
 */
export async function waitForConnectionRecovery(
  message: unknown,
  forwardToUnity: ForwardFn,
  readPort: ReadPortFn,
  setCurrentPort: (port: number) => void,
  maxAttempts = 6,
  probeTimeoutMs = 3000
): Promise<{ body: string | null; headers: Headers; status: number } | null> {
  let intervalMs = 500;
  for (let i = 0; i < maxAttempts; i++) {
    await sleep(intervalMs);
    intervalMs = Math.min(intervalMs * 2, 8000);
    let probePort: number;
    try {
      probePort = readPort();
    } catch {
      continue;
    }
    const ready = await isServerReady(forwardToUnity, probePort, probeTimeoutMs);
    if (!ready) continue;

    try {
      const result = await forwardToUnity(message, probePort);
      setCurrentPort(probePort);
      retryLogger.info(
        "Connection restored after {attempt} attempt(s), retrying request.",
        { attempt: i + 1 }
      );
      return result;
    } catch {
      // Request replay failed, keep polling
    }
  }
  return null;
}

function sleep(ms: number): Promise<void> {
  return Bun.sleep(ms);
}
