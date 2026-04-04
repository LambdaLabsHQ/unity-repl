#!/usr/bin/env bun
/**
 * UnityREPL — persistent stdin/stdout C# REPL for Unity Editor.
 *
 * Input: raw C# expressions/statements, one per line.
 * Output: plain text evaluation result, followed by ---END--- sentinel.
 *
 * Examples:
 *   EditorApplication.isPlaying
 *   AssetDatabase.Refresh()
 *   Selection.activeGameObject?.name ?? "(none)"
 *   var go = new GameObject("Test"); go.name
 */
import { resolve, join } from "path";
import { readFileSync, writeFileSync, existsSync, unlinkSync, mkdirSync, renameSync } from "fs";
import { randomUUID } from "crypto";
import { createInterface } from "readline";

const projectRoot = resolve(__dirname, "../../../../");
const ipcDir = join(projectRoot, "Temp", "UnityMcpIpc");
const reqDir = join(ipcDir, "Requests");
const resDir = join(ipcDir, "Responses");

mkdirSync(reqDir, { recursive: true });
mkdirSync(resDir, { recursive: true });

const SENTINEL = "---END---";
const TIMEOUT_MS = 60_000;

async function evaluate(code: string): Promise<string> {
  const uuid = randomUUID();
  const reqTmp = join(reqDir, `${uuid}.tmp`);
  const reqFile = join(reqDir, `${uuid}.req`);
  const resFile = join(resDir, `${uuid}.res`);

  writeFileSync(reqTmp, code);
  renameSync(reqTmp, reqFile);

  let waited = 0;
  while (!existsSync(resFile)) {
    await new Promise(r => setTimeout(r, 10));
    waited += 10;
    if (waited > TIMEOUT_MS) {
      return "ERROR: timeout (60s) — is Unity Editor running?";
    }
  }

  const result = readFileSync(resFile, "utf-8");
  unlinkSync(resFile);
  return result;
}

const rl = createInterface({ input: process.stdin, terminal: false });

console.log("UnityREPL ready. Type C# expressions:");
process.stdout.write("> ");

rl.on("line", async (line: string) => {
  const code = line.trim();
  if (!code) { process.stdout.write("> "); return; }
  if (code === "exit" || code === "quit") process.exit(0);

  const result = await evaluate(code);
  console.log(result);
  console.log(SENTINEL);
  process.stdout.write("> ");
});

rl.on("close", () => process.exit(0));
