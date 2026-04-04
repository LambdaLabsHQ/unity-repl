//#region src/util.d.ts
interface InspectOptions {
  colors?: boolean;
  depth?: number | null;
  compact?: boolean;
  [key: string]: unknown;
}
declare function inspect(obj: unknown, options?: InspectOptions): string;
//# sourceMappingURL=util.d.ts.map
//#endregion
export { InspectOptions, inspect };
//# sourceMappingURL=util.d.ts.map