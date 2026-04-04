//#region src/util.node.d.ts
interface InspectOptions {
  colors?: boolean;
  depth?: number | null;
  compact?: boolean;
  [key: string]: unknown;
}
declare function inspect(obj: unknown, options?: InspectOptions): string;
//# sourceMappingURL=util.node.d.ts.map
//#endregion
export { InspectOptions, inspect };
//# sourceMappingURL=util.node.d.cts.map