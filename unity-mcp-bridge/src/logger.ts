import { appendFile, mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { configure, dispose, fromAsyncSink, getLogger, getTextFormatter } from "@logtape/logtape";

function pad2(value: number): string {
  return value.toString().padStart(2, "0");
}

function formatRuntimeFileName(now: Date): string {
  const y = now.getFullYear();
  const m = pad2(now.getMonth() + 1);
  const d = pad2(now.getDate());
  const hh = pad2(now.getHours());
  const mm = pad2(now.getMinutes());
  const ss = pad2(now.getSeconds());
  return `${y}${m}${d}_${hh}${mm}${ss}.log`;
}

const moduleDir = dirname(fileURLToPath(import.meta.url));
const bridgeRootDir = resolve(moduleDir, "..");
const logsDir = resolve(bridgeRootDir, "logs");
const runtimeLogFileName = formatRuntimeFileName(new Date());
export const runtimeLogFilePath = resolve(logsDir, runtimeLogFileName);

await mkdir(logsDir, { recursive: true });

const formatter = getTextFormatter();
const fileSink = fromAsyncSink(async (record) => {
  const line = formatter(record);
  await appendFile(
    runtimeLogFilePath,
    line.endsWith("\n") ? line : `${line}\n`,
    "utf8"
  );
});

await configure({
  sinks: {
    file: fileSink,
  },
  loggers: [
    {
      category: "unity-mcp-bridge",
      sinks: ["file"],
      lowestLevel: "trace",
    },
    {
      category: ["logtape", "meta"],
      sinks: ["file"],
      lowestLevel: "error",
    },
  ],
});

export const logger = getLogger(["unity-mcp-bridge"]);
export const retryLogger = getLogger(["unity-mcp-bridge", "retry"]);

let disposed = false;

export async function flushLogs(): Promise<void> {
  if (disposed) return;
  disposed = true;
  await dispose();
}
