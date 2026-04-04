//#region src/util.deno.d.ts
interface InspectOptions {
  colors?: boolean;
  depth?: number | null;
  compact?: boolean;
  [key: string]: unknown;
}
declare function inspect(obj: unknown, options?: InspectOptions): string;
//# sourceMappingURL=util.deno.d.ts.map
//#endregion
export { InspectOptions, inspect };
//# sourceMappingURL=util.deno.d.ts.map