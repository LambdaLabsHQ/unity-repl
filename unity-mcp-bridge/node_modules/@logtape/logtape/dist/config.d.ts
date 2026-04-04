import { ContextLocalStorage } from "./context.js";
import { LogLevel } from "./level.js";
import { FilterLike } from "./filter.js";
import { Sink } from "./sink.js";

//#region src/config.d.ts

/**
 * A configuration for the loggers.
 */
interface Config<TSinkId extends string, TFilterId extends string> {
  /**
   * The sinks to use.  The keys are the sink identifiers, and the values are
   * {@link Sink}s.
   */
  sinks: Record<TSinkId, Sink>;
  /**
   * The filters to use.  The keys are the filter identifiers, and the values
   * are either {@link Filter}s or {@link LogLevel}s.
   */
  filters?: Record<TFilterId, FilterLike>;
  /**
   * The loggers to configure.
   */
  loggers: LoggerConfig<TSinkId, TFilterId>[];
  /**
   * The context-local storage to use for implicit contexts.
   * @since 0.7.0
   */
  contextLocalStorage?: ContextLocalStorage<Record<string, unknown>>;
  /**
   * Whether to reset the configuration before applying this one.
   */
  reset?: boolean;
}
/**
 * A logger configuration.
 */
interface LoggerConfig<TSinkId extends string, TFilterId extends string> {
  /**
   * The category of the logger.  If a string, it is equivalent to an array
   * with one element.
   */
  category: string | string[];
  /**
   * The sink identifiers to use.
   */
  sinks?: TSinkId[];
  /**
   * Whether to inherit the parent's sinks.  If `inherit`, the parent's sinks
   * are used along with the specified sinks.  If `override`, the parent's
   * sinks are not used, and only the specified sinks are used.
   *
   * The default is `inherit`.
   * @default `"inherit"
   * @since 0.6.0
   */
  parentSinks?: "inherit" | "override";
  /**
   * The filter identifiers to use.
   */
  filters?: TFilterId[];
  /**
   * The lowest log level to accept.  If `null`, the logger will reject all
   * records.
   * @since 0.8.0
   */
  lowestLevel?: LogLevel | null;
}
/**
 * Configure the loggers with the specified configuration.
 *
 * Note that if the given sinks or filters are disposable, they will be
 * disposed when the configuration is reset, or when the process exits.
 *
 * @example
 * ```typescript
 * await configure({
 *   sinks: {
 *     console: getConsoleSink(),
 *   },
 *   filters: {
 *     slow: (log) =>
 *       "duration" in log.properties &&
 *       log.properties.duration as number > 1000,
 *   },
 *   loggers: [
 *     {
 *       category: "my-app",
 *       sinks: ["console"],
 *       lowestLevel: "info",
 *     },
 *     {
 *       category: ["my-app", "sql"],
 *       filters: ["slow"],
 *       lowestLevel: "debug",
 *     },
 *     {
 *       category: "logtape",
 *       sinks: ["console"],
 *       lowestLevel: "error",
 *     },
 *   ],
 * });
 * ```
 *
 * @param config The configuration.
 */
declare function configure<TSinkId extends string, TFilterId extends string>(config: Config<TSinkId, TFilterId>): Promise<void>;
/**
 * Configure sync loggers with the specified configuration.
 *
 * Note that if the given sinks or filters are disposable, they will be
 * disposed when the configuration is reset, or when the process exits.
 *
 * Also note that passing async sinks or filters will throw. If
 * necessary use {@link resetSync} or {@link disposeSync}.
 *
 * @example
 * ```typescript
 * configureSync({
 *   sinks: {
 *     console: getConsoleSink(),
 *   },
 *   loggers: [
 *     {
 *       category: "my-app",
 *       sinks: ["console"],
 *       lowestLevel: "info",
 *     },
 *     {
 *       category: "logtape",
 *       sinks: ["console"],
 *       lowestLevel: "error",
 *     },
 *   ],
 * });
 * ```
 *
 * @param config The configuration.
 * @since 0.9.0
 */
declare function configureSync<TSinkId extends string, TFilterId extends string>(config: Config<TSinkId, TFilterId>): void;
/**
 * Get the current configuration, if any.  Otherwise, `null`.
 * @returns The current configuration, if any.  Otherwise, `null`.
 */
declare function getConfig(): Config<string, string> | null;
/**
 * Reset the configuration.  Mostly for testing purposes.
 */
declare function reset(): Promise<void>;
/**
 * Reset the configuration.  Mostly for testing purposes. Will not clear async
 * sinks, only use with sync sinks. Use {@link reset} if you have async sinks.
 * @since 0.9.0
 */
declare function resetSync(): void;
/**
 * Dispose of the disposables.
 */
declare function dispose(): Promise<void>;
/**
 * Dispose of the sync disposables. Async disposables will be untouched,
 * use {@link dispose} if you have async sinks.
 * @since 0.9.0
 */
declare function disposeSync(): void;
/**
 * A configuration error.
 */
declare class ConfigError extends Error {
  /**
   * Constructs a new configuration error.
   * @param message The error message.
   */
  constructor(message: string);
}
//# sourceMappingURL=config.d.ts.map
//#endregion
export { Config, ConfigError, LoggerConfig, configure, configureSync, dispose, disposeSync, getConfig, reset, resetSync };
//# sourceMappingURL=config.d.ts.map