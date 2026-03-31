#!/usr/bin/env bun

import { getPortFilePath, readPort } from "./port-discovery.ts";
import { createInterface } from "readline";

let sessionId: string | null = null;
let currentPort: number;

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

async function handleLine(line: string): Promise<void> {
  const trimmed = line.trim();
  if (!trimmed) return;

  let message: { id?: unknown; method?: string };
  try {
    message = JSON.parse(trimmed);
  } catch {
    sendError(null, -32700, "Parse error: invalid JSON");
    return;
  }

  const requestId = message.id;

  try {
    const result = await forwardToUnity(message, currentPort);

    const mcpSessionId = result.headers.get("Mcp-Session-Id");
    if (mcpSessionId) {
      sessionId = mcpSessionId;
    }

    if (result.body !== null) {
      process.stdout.write(result.body + "\n");
    }
  } catch (err: unknown) {
    const errorMessage =
      err instanceof Error ? err.message : "Unknown error";

    // On connection failure, try re-reading the port file (Unity may have reloaded)
    if (isConnectionError(err)) {
      try {
        await Bun.sleep(500);
        currentPort = readPort();

        const retryResult = await forwardToUnity(message, currentPort);
        const mcpSessionId = retryResult.headers.get("Mcp-Session-Id");
        if (mcpSessionId) {
          sessionId = mcpSessionId;
        }
        if (retryResult.body !== null) {
          process.stdout.write(retryResult.body + "\n");
        }
        return;
      } catch {
        // Retry also failed
      }
    }

    if (requestId !== undefined) {
      sendError(
        requestId,
        -32000,
        `Failed to reach Unity MCP server on port ${currentPort}: ${errorMessage}`
      );
    } else {
      process.stderr.write(
        `[unity-mcp-bridge] Error forwarding notification: ${errorMessage}\n`
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
  process.stderr.write(`[unity-mcp-bridge] ${message}\n`);
  process.exit(1);
}

process.stderr.write(
  `[unity-mcp-bridge] Connected to Unity MCP server on port ${currentPort}\n`
);

const rl = createInterface({ input: process.stdin, terminal: false });

rl.on("line", (line) => {
  handleLine(line).catch((err) => {
    process.stderr.write(`[unity-mcp-bridge] Unhandled error: ${err}\n`);
  });
});

rl.on("close", () => process.exit(0));

process.on("SIGTERM", () => process.exit(0));
process.on("SIGINT", () => process.exit(0));
