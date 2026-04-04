//#region src/context.d.ts
/**
 * A generic interface for a context-local storage.  It resembles
 * the {@link AsyncLocalStorage} API from Node.js.
 * @template T The type of the context-local store.
 * @since 0.7.0
 */
interface ContextLocalStorage<T> {
  /**
   * Runs a callback with the given store as the context-local store.
   * @param store The store to use as the context-local store.
   * @param callback The callback to run.
   * @returns The return value of the callback.
   */
  run<R>(store: T, callback: () => R): R;
  /**
   * Returns the current context-local store.
   * @returns The current context-local store, or `undefined` if there is no
   *          store.
   */
  getStore(): T | undefined;
}
/**
 * Runs a callback with the given implicit context.  Every single log record
 * in the callback will have the given context.
 *
 * If no `contextLocalStorage` is configured, this function does nothing and
 * just returns the return value of the callback.  It also logs a warning to
 * the `["logtape", "meta"]` logger in this case.
 * @param context The context to inject.
 * @param callback The callback to run.
 * @returns The return value of the callback.
 * @since 0.7.0
 */
declare function withContext<T>(context: Record<string, unknown>, callback: () => T): T;
/**
 * Gets the current category prefix from context local storage.
 * @returns The current category prefix, or an empty array if not set.
 * @since 1.3.0
 */

/**
 * Runs a callback with the given category prefix prepended to all log
 * categories within the callback context.
 *
 * This is useful for SDKs or libraries that want to add their own category
 * as a prefix to logs from their internal dependencies.
 *
 * If no `contextLocalStorage` is configured, this function does nothing and
 * just returns the return value of the callback.  It also logs a warning to
 * the `["logtape", "meta"]` logger in this case.
 *
 * @example Basic usage
 * ```typescript
 * import { getLogger, withCategoryPrefix } from "@logtape/logtape";
 *
 * export function sdkFunction() {
 *   return withCategoryPrefix(["my-sdk"], () => {
 *     // Any logs from internal libraries within this context
 *     // will have ["my-sdk"] prepended to their category
 *     return internalLibraryFunction();
 *   });
 * }
 * ```
 *
 * @param prefix The category prefix to prepend.  Can be a string or an array
 *               of strings.
 * @param callback The callback to run.
 * @returns The return value of the callback.
 * @since 1.3.0
 */
declare function withCategoryPrefix<T>(prefix: string | readonly string[], callback: () => T): T;
//# sourceMappingURL=context.d.ts.map
//#endregion
export { ContextLocalStorage, withCategoryPrefix, withContext };
//# sourceMappingURL=context.d.cts.map