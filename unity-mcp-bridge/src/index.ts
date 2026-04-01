#!/usr/bin/env bun

import { readPort } from "./port-discovery.ts";
import {
  isPlayModeUnityTestRunRequest,
  shouldRetryOnConnectionError,
  isCancelledByReload,
  isCancelledByReloadLegacy,
  isBlankJsonBody,
  waitForServerAndRetry,
  waitForConnectionRecovery,
} from "./retry-helpers.ts";
import { flushLogs, logger, runtimeLogFilePath } from "./logger.ts";
import { createInterface } from "readline";

let sessionId: string | null = null;
let currentPort: number;
let isShuttingDown = false;
const inFlightRequests = new Set<Promise<void>>();
const PLAYMODE_RESTART_MAX_ATTEMPTS = 60;
const PLAYMODE_RECOVERY_MAX_ATTEMPTS = 10;

type BridgeMessage = {
  id?: unknown;
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

type UnityTestContext = {
  requestId: unknown;
  action: string;
  testMode: string;
  isPlayMode: boolean;
};

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function shutdown(exitCode: number, reason: string): Promise<void> {
  if (isShuttingDown) return;
  isShuttingDown = true;
  logger.info(
    "Bridge shutdown started: reason={reason}, inFlightRequests={inFlightRequests}",
    { reason, inFlightRequests: inFlightRequests.size }
  );

  // Best-effort drain so final request logs are less likely to be lost.
  if (inFlightRequests.size > 0) {
    await Promise.race([
      Promise.allSettled(Array.from(inFlightRequests)),
      sleep(2000),
    ]);
  }

  try {
    await flushLogs();
  } catch {
    // Best effort
  }
  logger.info("Bridge shutdown complete.");
  process.exit(exitCode);
}

async function forwardToUnity(
  message: unknown,
  port: number
): Promise<{ body: string | null; headers: Headers; status: number }> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "application/json",
  };
  if (sessionId) {
    headers["Mcp-Session-Id"] = sessionId;
  }

  const response = await fetch(`http://localhost:${port}/mcp`, {
    method: "POST",
    headers,
    body: JSON.stringify(message),
  });

  return {
    body: response.status === 202 ? null : await response.text(),
    headers: response.headers,
    status: response.status,
  };
}

function sendError(id: unknown, code: number, message: string): void {
  const response = {
    jsonrpc: "2.0",
    error: { code, message },
    id: id ?? null,
  };
  process.stdout.write(JSON.stringify(response) + "\n");
}

function emitJsonRpcBody(
  body: string,
  requestId: unknown,
  context: "initial" | "retry" | "recovery"
): boolean {
  let normalizedBody = body;

  try {
    const parsed = JSON.parse(body) as unknown;
    normalizedBody = JSON.stringify(parsed);

    if (requestId !== undefined && !Array.isArray(parsed)) {
      const responseId =
        parsed != null &&
        typeof parsed === "object" &&
        "id" in parsed
          ? (parsed as { id?: unknown }).id
          : undefined;
      if (responseId === undefined) {
        logger.warning(
          "Unity JSON-RPC response has no id: context={context}, requestId={requestId}",
          { context, requestId }
        );
      }
    }
  } catch {
    const sample = body.length > 300 ? `${body.slice(0, 300)}...` : body;
    logger.error(
      "Invalid JSON response from Unity: context={context}, requestId={requestId}, bytes={bytes}, sample={sample}",
      {
        context,
        requestId: requestId ?? null,
        bytes: body.length,
        sample,
      }
    );

    if (requestId !== undefined) {
      sendError(
        requestId,
        -32000,
        "Invalid JSON response from Unity MCP server"
      );
      return true;
    }
    return false;
  }

  process.stdout.write(normalizedBody + "\n");
  logger.debug(
    "Wrote JSON-RPC response to stdout: context={context}, requestId={requestId}, bytes={bytes}",
    {
      context,
      requestId: requestId ?? null,
      bytes: normalizedBody.length,
    }
  );
  return true;
}

function normalizeMode(value: unknown): string {
  if (typeof value !== "string") return "";
  return value.replace(/_/g, "").toLowerCase();
}

function getUnityTestContext(message: BridgeMessage): UnityTestContext | null {
  if (message.method !== "tools/call") return null;
  if (message.params?.name !== "unity_test") return null;

  const args = message.params.arguments ?? {};
  const action = typeof args.action === "string" ? args.action : "unknown";
  const modeRaw =
    typeof args.testMode === "string"
      ? args.testMode
      : typeof args.test_mode === "string"
        ? args.test_mode
        : "unspecified";

  return {
    requestId: message.id ?? null,
    action,
    testMode: modeRaw,
    isPlayMode: normalizeMode(modeRaw) === "playmode",
  };
}

type ForwardResult = { body: string | null; headers: Headers; status: number };

function hasNonEmptyBody(
  result: ForwardResult | null
): result is ForwardResult & { body: string } {
  return result !== null && result.body !== null && !isBlankJsonBody(result.body);
}

function acceptResponse(
  result: ForwardResult,
  requestId: unknown,
  context: "initial" | "retry" | "recovery",
  unityTest: UnityTestContext | null,
  startedAt: number,
  extraLogFields?: Record<string, unknown>
): void {
  const sid = result.headers.get("Mcp-Session-Id");
  if (sid) sessionId = sid;
  emitJsonRpcBody(result.body!, requestId, context);
  if (unityTest) {
    logger.info(
      `unity_test ${context} completed: id={requestId}, status={status}, durationMs={durationMs}`,
      {
        requestId: unityTest.requestId,
        status: result.status,
        durationMs: Date.now() - startedAt,
        ...extraLogFields,
      }
    );
  }
}

async function handleLine(line: string): Promise<void> {
  const trimmed = line.trim();
  if (!trimmed) return;

  let message: BridgeMessage;
  try {
    message = JSON.parse(trimmed);
  } catch {
    sendError(null, -32700, "Parse error: invalid JSON");
    return;
  }

  const requestId = message.id;
  const startedAt = Date.now();
  const unityTest = getUnityTestContext(message);
  const isPlayModeRunRequest = isPlayModeUnityTestRunRequest(message);

  if (unityTest) {
    logger.info(
      "unity_test request started: id={requestId}, action={action}, testMode={testMode}, isPlayMode={isPlayMode}",
      {
        requestId: unityTest.requestId,
        action: unityTest.action,
        testMode: unityTest.testMode,
        isPlayMode: unityTest.isPlayMode,
      }
    );
  }

  const setPort = (p: number) => { currentPort = p; };

  try {
    const result = await forwardToUnity(message, currentPort);

    const mcpSessionId = result.headers.get("Mcp-Session-Id");
    if (mcpSessionId) {
      sessionId = mcpSessionId;
    }

    // Domain reload retry: only for PlayMode unity_test run requests.
    // Unity cancels in-flight requests during assembly reload; we poll until the
    // server restarts, then replay once. Non-PlayMode requests are forwarded as-is.
    const cancelledByReload =
      isCancelledByReload(result.body) || isCancelledByReloadLegacy(result.body, message);
    const emptyBody = isBlankJsonBody(result.body);

    if (unityTest) {
      logger.info(
        "unity_test initial response: id={requestId}, status={status}, cancelledByReload={cancelledByReload}, emptyBody={emptyBody}, bodyBytes={bodyBytes}, durationMs={durationMs}",
        {
          requestId: unityTest.requestId,
          status: result.status,
          cancelledByReload,
          emptyBody,
          bodyBytes: result.body?.length ?? -1,
          durationMs: Date.now() - startedAt,
        }
      );
    }

    if (isPlayModeRunRequest && (cancelledByReload || emptyBody)) {
      const retryReason = cancelledByReload
        ? "domain_reload_cancelled"
        : "empty_response_body";

      logger.warning(
        "PlayMode unity_test response is transient ({retryReason}), waiting for server restart before replay.",
        { retryReason }
      );

      // Step 1: Wait for server to restart, then replay the request.
      // Unity-side recovery (SessionState + TestResults.xml) handles returning
      // cached results so the replay completes quickly without re-running tests.
      const retryResult = await waitForServerAndRetry(
        message, forwardToUnity, readPort, setPort, PLAYMODE_RESTART_MAX_ATTEMPTS
      );

      if (hasNonEmptyBody(retryResult)) {
        acceptResponse(retryResult, requestId, "retry", unityTest, startedAt, { retryReason });
        return;
      }

      // Step 2: Restart replay failed or returned empty — try connection recovery
      // as a final fallback (e.g. server restarted on a different port).
      if (retryResult !== null) {
        logger.warning(
          "Retry response body empty for PlayMode unity_test; falling back to connection recovery."
        );
      }

      const recoveredResult = await waitForConnectionRecovery(
        message, forwardToUnity, readPort, setPort, PLAYMODE_RECOVERY_MAX_ATTEMPTS
      );

      if (hasNonEmptyBody(recoveredResult)) {
        acceptResponse(recoveredResult, requestId, "recovery", unityTest, startedAt, { retryReason });
        return;
      }

      // All recovery attempts exhausted
      if (unityTest) {
        logger.error(
          "unity_test retry exhausted: id={requestId}, retryReason={retryReason}, durationMs={durationMs}",
          {
            requestId: unityTest.requestId,
            retryReason,
            durationMs: Date.now() - startedAt,
          }
        );
      }

      if (requestId !== undefined) {
        sendError(
          requestId,
          -32000,
          "Unity MCP server did not recover in time after PlayMode domain reload"
        );
        return;
      }
    }

    if (result.body !== null) {
      emitJsonRpcBody(result.body, requestId, "initial");
    }
    if (unityTest) {
      logger.info(
        "unity_test request finished: id={requestId}, status={status}, durationMs={durationMs}",
        {
          requestId: unityTest.requestId,
          status: result.status,
          durationMs: Date.now() - startedAt,
        }
      );
    }
  } catch (err: unknown) {
    const errorMessage =
      err instanceof Error ? err.message : "Unknown error";

    // On connection failure, poll with exponential backoff (Unity may be reloading)
    if (isConnectionError(err) && shouldRetryOnConnectionError(message)) {
      logger.warning("Connection error, polling for recovery.");
      const recoveryAttempts = isPlayModeRunRequest
        ? PLAYMODE_RECOVERY_MAX_ATTEMPTS
        : undefined;
      const retryResult = await waitForConnectionRecovery(
        message, forwardToUnity, readPort, setPort, recoveryAttempts
      );
      if (hasNonEmptyBody(retryResult)) {
        acceptResponse(retryResult, requestId, "recovery", unityTest, startedAt);
        return;
      }
      if (unityTest) {
        logger.error(
          "unity_test connection recovery exhausted: id={requestId}, durationMs={durationMs}, error={errorMessage}",
          {
            requestId: unityTest.requestId,
            durationMs: Date.now() - startedAt,
            errorMessage,
          }
        );
      }
    }

    if (requestId !== undefined) {
      sendError(
        requestId,
        -32000,
        `Failed to reach Unity MCP server on port ${currentPort}: ${errorMessage}`
      );
    } else {
      if (isShuttingDown && isConnectionError(err)) {
        logger.info(
          "Ignoring notification forwarding error during shutdown: {errorMessage}",
          { errorMessage }
        );
      } else {
        logger.error("Error forwarding notification: {errorMessage}", { errorMessage });
      }
    }
    if (unityTest) {
      logger.error(
        "unity_test request failed: id={requestId}, durationMs={durationMs}, error={errorMessage}",
        {
          requestId: unityTest.requestId,
          durationMs: Date.now() - startedAt,
          errorMessage,
        }
      );
    }
  }
}

function isConnectionError(err: unknown): boolean {
  if (err instanceof TypeError && err.message.includes("fetch")) {
    return true;
  }
  if (
    err != null &&
    typeof err === "object" &&
    "code" in err
  ) {
    const code = (err as { code: string }).code;
    return code === "ECONNREFUSED" || code === "ECONNRESET" || code === "ConnectionRefused";
  }
  return false;
}

// --- Main ---

try {
  currentPort = readPort();
} catch (err) {
  const message = err instanceof Error ? err.message : String(err);
  logger.fatal(message);
  await shutdown(1, "startup_failure");
}

logger.info(
  "Connected to Unity MCP server on port {port}; log file: {logFilePath}",
  { port: currentPort, logFilePath: runtimeLogFilePath }
);

const rl = createInterface({ input: process.stdin, terminal: false });

rl.on("line", (line) => {
  if (isShuttingDown) {
    logger.debug("Skipping incoming line while shutting down.");
    return;
  }

  const task = handleLine(line).catch((err) => {
    logger.error("Unhandled error: {error}", { error: String(err) });
  });
  inFlightRequests.add(task);
  void task.finally(() => {
    inFlightRequests.delete(task);
  });
});

rl.on("close", () => {
  void shutdown(0, "stdin_closed");
});

process.on("SIGTERM", () => {
  void shutdown(0, "sigterm");
});
process.on("SIGINT", () => {
  void shutdown(0, "sigint");
});
