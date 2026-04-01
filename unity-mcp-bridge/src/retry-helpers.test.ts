import { test, expect, describe } from "bun:test";
import {
  isPlayModeUnityTestRunRequest,
  shouldRetryOnConnectionError,
  isCancelledByReload,
  isCancelledByReloadLegacy,
  isBlankJsonBody,
  waitForServerAndRetry,
  waitForConnectionRecovery,
  DOMAIN_RELOAD_CANCELLED,
} from "./retry-helpers.ts";

// ---------------------------------------------------------------------------
// isPlayModeUnityTestRunRequest
// ---------------------------------------------------------------------------

describe("isPlayModeUnityTestRunRequest", () => {
  const playModeMsg = {
    method: "tools/call",
    params: { name: "unity_test", arguments: { action: "run", testMode: "PlayMode" } },
  };

  test("returns true for unity_test + run + PlayMode", () => {
    expect(isPlayModeUnityTestRunRequest(playModeMsg)).toBe(true);
  });

  test("accepts snake_case test_mode", () => {
    const msg = {
      method: "tools/call",
      params: { name: "unity_test", arguments: { action: "run", test_mode: "play_mode" } },
    };
    expect(isPlayModeUnityTestRunRequest(msg)).toBe(true);
  });

  test("returns false for EditMode", () => {
    const msg = {
      method: "tools/call",
      params: { name: "unity_test", arguments: { action: "run", testMode: "EditMode" } },
    };
    expect(isPlayModeUnityTestRunRequest(msg)).toBe(false);
  });

  test("returns false when action is not run", () => {
    const msg = {
      method: "tools/call",
      params: { name: "unity_test", arguments: { action: "list", testMode: "PlayMode" } },
    };
    expect(isPlayModeUnityTestRunRequest(msg)).toBe(false);
  });

  test("returns false for wrong tool name", () => {
    const msg = {
      method: "tools/call",
      params: { name: "run_tests", arguments: { action: "run", testMode: "PlayMode" } },
    };
    expect(isPlayModeUnityTestRunRequest(msg)).toBe(false);
  });

  test("returns false for non-tools/call method", () => {
    const msg = {
      method: "tools/list",
      params: { name: "unity_test", arguments: { action: "run", testMode: "PlayMode" } },
    };
    expect(isPlayModeUnityTestRunRequest(msg)).toBe(false);
  });

  test("returns false for null/undefined", () => {
    expect(isPlayModeUnityTestRunRequest(null)).toBe(false);
    expect(isPlayModeUnityTestRunRequest(undefined)).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// shouldRetryOnConnectionError
// ---------------------------------------------------------------------------

describe("shouldRetryOnConnectionError", () => {
  test("returns true for idempotent non-tool-call methods", () => {
    expect(shouldRetryOnConnectionError({ method: "initialize" })).toBe(true);
    expect(shouldRetryOnConnectionError({ method: "ping" })).toBe(true);
    expect(shouldRetryOnConnectionError({ method: "tools/list" })).toBe(true);
    expect(shouldRetryOnConnectionError({ method: "resources/list" })).toBe(true);
    expect(shouldRetryOnConnectionError({ method: "resources/templates/list" })).toBe(true);
    expect(shouldRetryOnConnectionError({ method: "prompts/list" })).toBe(true);
  });

  test("returns true for PlayMode unity_test run tools/call", () => {
    const msg = {
      method: "tools/call",
      params: { name: "unity_test", arguments: { action: "run", testMode: "PlayMode" } },
    };
    expect(shouldRetryOnConnectionError(msg)).toBe(true);
  });

  test("returns false for non-PlayMode unity_test run tools/call", () => {
    const msg = {
      method: "tools/call",
      params: { name: "unity_test", arguments: { action: "run", testMode: "EditMode" } },
    };
    expect(shouldRetryOnConnectionError(msg)).toBe(false);
  });

  test("returns true for unity_test list (PlayMode)", () => {
    const msg = {
      method: "tools/call",
      params: { name: "unity_test", arguments: { action: "list", testMode: "PlayMode" } },
    };
    expect(shouldRetryOnConnectionError(msg)).toBe(true);
  });

  test("returns true for unity_test list (EditMode)", () => {
    const msg = {
      method: "tools/call",
      params: { name: "unity_test", arguments: { action: "list", testMode: "EditMode" } },
    };
    expect(shouldRetryOnConnectionError(msg)).toBe(true);
  });

  test("returns false for side-effectful non-whitelisted tools/call", () => {
    const msg = {
      method: "tools/call",
      params: { name: "unity_scene", arguments: { action: "create" } },
    };
    expect(shouldRetryOnConnectionError(msg)).toBe(false);
  });

  test("returns false for unknown method", () => {
    expect(shouldRetryOnConnectionError({ method: "custom/method" })).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// isCancelledByReload
// ---------------------------------------------------------------------------

describe("isCancelledByReload", () => {
  test("returns true for -32001 + reason domain_reload", () => {
    const body = JSON.stringify({
      error: { code: DOMAIN_RELOAD_CANCELLED, message: "Request cancelled", data: { reason: "domain_reload" } },
    });
    expect(isCancelledByReload(body)).toBe(true);
  });

  test("returns false when reason is missing", () => {
    const body = JSON.stringify({
      error: { code: DOMAIN_RELOAD_CANCELLED, message: "Request cancelled" },
    });
    expect(isCancelledByReload(body)).toBe(false);
  });

  test("returns false when reason is not domain_reload", () => {
    const body = JSON.stringify({
      error: { code: DOMAIN_RELOAD_CANCELLED, message: "Request cancelled", data: { reason: "other" } },
    });
    expect(isCancelledByReload(body)).toBe(false);
  });

  test("returns false for -32603 (generic internal error)", () => {
    const body = JSON.stringify({
      error: { code: -32603, message: "Request cancelled" },
    });
    expect(isCancelledByReload(body)).toBe(false);
  });

  test("returns false for array response (batch)", () => {
    const body = JSON.stringify([
      { error: { code: DOMAIN_RELOAD_CANCELLED, data: { reason: "domain_reload" } } },
    ]);
    expect(isCancelledByReload(body)).toBe(false);
  });

  test("returns false for null body", () => {
    expect(isCancelledByReload(null)).toBe(false);
  });

  test("returns false for invalid JSON", () => {
    expect(isCancelledByReload("not json")).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// isCancelledByReloadLegacy
// ---------------------------------------------------------------------------

describe("isCancelledByReloadLegacy", () => {
  const playModeMsg = {
    method: "tools/call",
    params: { name: "unity_test", arguments: { action: "run", testMode: "PlayMode" } },
  };
  const editModeMsg = {
    method: "tools/call",
    params: { name: "unity_test", arguments: { action: "run", testMode: "EditMode" } },
  };
  const otherToolMsg = {
    method: "tools/call",
    params: { name: "unity_scene", arguments: {} },
  };

  const cancelledBody = JSON.stringify({
    error: { code: -32603, message: "Request cancelled" },
  });

  test("returns true for -32603 + cancelled + PlayMode unity_test run", () => {
    expect(isCancelledByReloadLegacy(cancelledBody, playModeMsg)).toBe(true);
  });

  test("returns false for non-PlayMode request", () => {
    expect(isCancelledByReloadLegacy(cancelledBody, editModeMsg)).toBe(false);
  });

  test("returns false for different tool", () => {
    expect(isCancelledByReloadLegacy(cancelledBody, otherToolMsg)).toBe(false);
  });

  test("returns false when message is not 'cancelled'", () => {
    const body = JSON.stringify({ error: { code: -32603, message: "Internal error" } });
    expect(isCancelledByReloadLegacy(body, playModeMsg)).toBe(false);
  });

  test("returns false for -32001 (should use isCancelledByReload instead)", () => {
    const body = JSON.stringify({
      error: { code: DOMAIN_RELOAD_CANCELLED, message: "Request cancelled", data: { reason: "domain_reload" } },
    });
    expect(isCancelledByReloadLegacy(body, playModeMsg)).toBe(false);
  });

  test("returns false for null body", () => {
    expect(isCancelledByReloadLegacy(null, playModeMsg)).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// isBlankJsonBody
// ---------------------------------------------------------------------------

describe("isBlankJsonBody", () => {
  test("returns true for empty string", () => {
    expect(isBlankJsonBody("")).toBe(true);
  });

  test("returns true for whitespace-only body", () => {
    expect(isBlankJsonBody("   \n\t")).toBe(true);
  });

  test("returns false for null body", () => {
    expect(isBlankJsonBody(null)).toBe(false);
  });

  test("returns false for non-empty JSON body", () => {
    expect(isBlankJsonBody('{"jsonrpc":"2.0","id":1,"result":{}}')).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// waitForServerAndRetry
// ---------------------------------------------------------------------------

describe("waitForServerAndRetry", () => {
  const message = { method: "tools/call", params: { name: "unity_test" } };
  const okResult = { body: '{"result":"ok"}', headers: new Headers(), status: 200 };
  const pingOk = { body: '{"jsonrpc":"2.0","result":{},"id":"__bridge_probe__"}', headers: new Headers(), status: 200 };

  test("retries probe until ready, then replays original request once", async () => {
    let probeCalls = 0;
    let requestCalls = 0;
    const forward = async (msg: unknown, _port: number) => {
      const method = (msg as { method?: string }).method;
      if (method === "ping") {
        probeCalls++;
        if (probeCalls < 3) throw new Error("ECONNREFUSED");
        return pingOk;
      }
      requestCalls++;
      return okResult;
    };
    const readPort = () => 9000;
    let port = 0;
    const result = await waitForServerAndRetry(
      message,
      forward,
      readPort,
      (p) => { port = p; },
      5,
      1,
      20
    );
    expect(result).toBe(okResult);
    expect(port).toBe(9000);
    expect(probeCalls).toBe(3);
    expect(requestCalls).toBe(1);
  });

  test("returns null when all attempts fail", async () => {
    let requestCalls = 0;
    const forward = async (msg: unknown) => {
      if ((msg as { method?: string }).method !== "ping") requestCalls++;
      throw new Error("ECONNREFUSED");
    };
    const readPort = () => 9000;
    const result = await waitForServerAndRetry(message, forward, readPort, () => {}, 3, 1, 20);
    expect(result).toBeNull();
    expect(requestCalls).toBe(0);
  });

  test("skips iteration when readPort throws", async () => {
    let readCalls = 0;
    let forwardCalls = 0;
    const forward = async (_msg: unknown, _port: number) => {
      forwardCalls++;
      return okResult;
    };
    const readPort = () => {
      readCalls++;
      if (readCalls < 3) throw new Error("port file not found");
      return 9000;
    };
    const result = await waitForServerAndRetry(message, forward, readPort, () => {}, 5, 1, 20);
    expect(result).toBe(okResult);
    // One probe + one replay once readPort starts succeeding
    expect(forwardCalls).toBe(2);
  });

  test("does not update port on failure", async () => {
    let port = 8000;
    const forward = async (msg: unknown) => {
      if ((msg as { method?: string }).method === "ping") return pingOk;
      throw new Error("ECONNREFUSED");
    };
    const readPort = () => 9999;
    await waitForServerAndRetry(message, forward, readPort, (p) => { port = p; }, 2, 1, 20);
    expect(port).toBe(8000); // unchanged
  });

  test("probe timeout does not hang the retry loop", async () => {
    let probeCalls = 0;
    let requestCalls = 0;
    const forward = async (msg: unknown) => {
      const method = (msg as { method?: string }).method;
      if (method === "ping") {
        probeCalls++;
        if (probeCalls === 1) {
          // Simulate a hung fetch/promise: should be cut off by probe timeout
          return new Promise<never>(() => {});
        }
        return pingOk;
      }
      requestCalls++;
      return okResult;
    };
    const result = await waitForServerAndRetry(message, forward, () => 9000, () => {}, 3, 1, 5);
    expect(result).toBe(okResult);
    expect(probeCalls).toBe(2);
    expect(requestCalls).toBe(1);
  });
});

// ---------------------------------------------------------------------------
// waitForConnectionRecovery
// ---------------------------------------------------------------------------

describe("waitForConnectionRecovery", () => {
  const message = { method: "tools/call" };
  const okResult = { body: '{"result":"ok"}', headers: new Headers(), status: 200 };
  const pingOk = { body: '{"jsonrpc":"2.0","result":{},"id":"__bridge_probe__"}', headers: new Headers(), status: 200 };

  test("returns result when connection recovers", async () => {
    let probeCalls = 0;
    let requestCalls = 0;
    const forward = async (msg: unknown) => {
      const method = (msg as { method?: string }).method;
      if (method === "ping") {
        probeCalls++;
        if (probeCalls < 2) throw new Error("ECONNREFUSED");
        return pingOk;
      }
      requestCalls++;
      return okResult;
    };
    const result = await waitForConnectionRecovery(message, forward, () => 9000, () => {}, 4, 20);
    expect(result).toBe(okResult);
    expect(probeCalls).toBe(2);
    expect(requestCalls).toBe(1);
  });

  test("returns null when all attempts exhausted", async () => {
    let requestCalls = 0;
    const forward = async (msg: unknown) => {
      if ((msg as { method?: string }).method !== "ping") requestCalls++;
      throw new Error("ECONNREFUSED");
    };
    const result = await waitForConnectionRecovery(message, forward, () => 9000, () => {}, 3, 20);
    expect(result).toBeNull();
    expect(requestCalls).toBe(0);
  });

  test("uses exponential backoff intervals", async () => {
    // We can't easily measure actual sleep time, but we can verify the function
    // performs multiple probe attempts before replaying the original request.
    let probeAttempts = 0;
    let requestCalls = 0;
    const forward = async (msg: unknown) => {
      const method = (msg as { method?: string }).method;
      if (method === "ping") {
        probeAttempts++;
        if (probeAttempts < 3) throw new Error("ECONNREFUSED");
        return pingOk;
      }
      requestCalls++;
      return okResult;
    };
    await waitForConnectionRecovery(message, forward, () => 9000, () => {}, 5, 20);
    expect(probeAttempts).toBe(3);
    expect(requestCalls).toBe(1);
  });
});
