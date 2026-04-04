import { LogLevel } from "./level.cjs";
import { LogRecord } from "./record.cjs";
import { FilterLike } from "./filter.cjs";
import { ConsoleFormatter, TextFormatter } from "./formatter.cjs";

//#region src/sink.d.ts

/**
 * A sink is a function that accepts a log record and prints it somewhere.
 * Thrown exceptions will be suppressed and then logged to the meta logger,
 * a {@link Logger} with the category `["logtape", "meta"]`.  (In that case,
 * the meta log record will not be passed to the sink to avoid infinite
 * recursion.)
 *
 * @param record The log record to sink.
 */
type Sink = (record: LogRecord) => void;
/**
 * An async sink is a function that accepts a log record and asynchronously
 * processes it. This type is used with {@link fromAsyncSink} to create
 * a regular sink that properly handles asynchronous operations.
 *
 * @param record The log record to process asynchronously.
 * @returns A promise that resolves when the record has been processed.
 * @since 1.0.0
 */
type AsyncSink = (record: LogRecord) => Promise<void>;
/**
 * Turns a sink into a filtered sink.  The returned sink only logs records that
 * pass the filter.
 *
 * @example Filter a console sink to only log records with the info level
 * ```typescript
 * const sink = withFilter(getConsoleSink(), "info");
 * ```
 *
 * @param sink A sink to be filtered.
 * @param filter A filter to apply to the sink.  It can be either a filter
 *               function or a {@link LogLevel} string.
 * @returns A sink that only logs records that pass the filter.
 */
declare function withFilter(sink: Sink, filter: FilterLike): Sink;
/**
 * Options for the {@link getStreamSink} function.
 */
interface StreamSinkOptions {
  /**
   * The text formatter to use.  Defaults to {@link defaultTextFormatter}.
   */
  formatter?: TextFormatter;
  /**
   * The text encoder to use.  Defaults to an instance of {@link TextEncoder}.
   */
  encoder?: {
    encode(text: string): Uint8Array;
  };
  /**
   * Enable non-blocking mode with optional buffer configuration.
   * When enabled, log records are buffered and flushed in the background.
   *
   * @example Simple non-blocking mode
   * ```typescript
   * getStreamSink(stream, { nonBlocking: true });
   * ```
   *
   * @example Custom buffer configuration
   * ```typescript
   * getStreamSink(stream, {
   *   nonBlocking: {
   *     bufferSize: 1000,
   *     flushInterval: 50
   *   }
   * });
   * ```
   *
   * @default `false`
   * @since 1.0.0
   */
  nonBlocking?: boolean | {
    /**
     * Maximum number of records to buffer before flushing.
     * @default `100`
     */
    bufferSize?: number;
    /**
     * Interval in milliseconds between automatic flushes.
     * @default `100`
     */
    flushInterval?: number;
  };
}
/**
 * A factory that returns a sink that writes to a {@link WritableStream}.
 *
 * Note that the `stream` is of Web Streams API, which is different from
 * Node.js streams.  You can convert a Node.js stream to a Web Streams API
 * stream using [`stream.Writable.toWeb()`] method.
 *
 * [`stream.Writable.toWeb()`]: https://nodejs.org/api/stream.html#streamwritabletowebstreamwritable
 *
 * @example Sink to the standard error in Deno
 * ```typescript
 * const stderrSink = getStreamSink(Deno.stderr.writable);
 * ```
 *
 * @example Sink to the standard error in Node.js
 * ```typescript
 * import stream from "node:stream";
 * const stderrSink = getStreamSink(stream.Writable.toWeb(process.stderr));
 * ```
 *
 * @param stream The stream to write to.
 * @param options The options for the sink.
 * @returns A sink that writes to the stream.
 */
declare function getStreamSink(stream: WritableStream, options?: StreamSinkOptions): Sink & AsyncDisposable;
type ConsoleMethod = "debug" | "info" | "log" | "warn" | "error";
/**
 * Options for the {@link getConsoleSink} function.
 */
interface ConsoleSinkOptions {
  /**
   * The console formatter or text formatter to use.
   * Defaults to {@link defaultConsoleFormatter}.
   */
  formatter?: ConsoleFormatter | TextFormatter;
  /**
   * The mapping from log levels to console methods.  Defaults to:
   *
   * ```typescript
   * {
   *   trace: "trace",
   *   debug: "debug",
   *   info: "info",
   *   warning: "warn",
   *   error: "error",
   *   fatal: "error",
   * }
   * ```
   * @since 0.9.0
   */
  levelMap?: Record<LogLevel, ConsoleMethod>;
  /**
   * The console to log to.  Defaults to {@link console}.
   */
  console?: Console;
  /**
   * Enable non-blocking mode with optional buffer configuration.
   * When enabled, log records are buffered and flushed in the background.
   *
   * @example Simple non-blocking mode
   * ```typescript
   * getConsoleSink({ nonBlocking: true });
   * ```
   *
   * @example Custom buffer configuration
   * ```typescript
   * getConsoleSink({
   *   nonBlocking: {
   *     bufferSize: 1000,
   *     flushInterval: 50
   *   }
   * });
   * ```
   *
   * @default `false`
   * @since 1.0.0
   */
  nonBlocking?: boolean | {
    /**
     * Maximum number of records to buffer before flushing.
     * @default `100`
     */
    bufferSize?: number;
    /**
     * Interval in milliseconds between automatic flushes.
     * @default `100`
     */
    flushInterval?: number;
  };
}
/**
 * A console sink factory that returns a sink that logs to the console.
 *
 * @param options The options for the sink.
 * @returns A sink that logs to the console. If `nonBlocking` is enabled,
 *          returns a sink that also implements {@link Disposable}.
 */
declare function getConsoleSink(options?: ConsoleSinkOptions): Sink | (Sink & Disposable);
/**
 * Converts an async sink into a regular sink with proper async handling.
 * The returned sink chains async operations to ensure proper ordering and
 * implements AsyncDisposable to wait for all pending operations on disposal.
 *
 * @example Create a sink that asynchronously posts to a webhook
 * ```typescript
 * const asyncSink: AsyncSink = async (record) => {
 *   await fetch("https://example.com/logs", {
 *     method: "POST",
 *     body: JSON.stringify(record),
 *   });
 * };
 * const sink = fromAsyncSink(asyncSink);
 * ```
 *
 * @param asyncSink The async sink function to convert.
 * @returns A sink that properly handles async operations and disposal.
 * @since 1.0.0
 */
declare function fromAsyncSink(asyncSink: AsyncSink): Sink & AsyncDisposable;
/**
 * Options for the {@link fingersCrossed} function.
 * @since 1.1.0
 */
interface FingersCrossedOptions {
  /**
   * Minimum log level that triggers buffer flush.
   * When a log record at or above this level is received, all buffered
   * records are flushed to the wrapped sink.
   * @default `"error"`
   */
  readonly triggerLevel?: LogLevel;
  /**
   * Maximum log level that will be buffered.
   * Log records at or below this level are buffered, while records above
   * this level (but below {@link triggerLevel}) pass through immediately
   * without buffering.
   *
   * When `undefined` (default), all records below {@link triggerLevel} are
   * buffered (equivalent to setting this to the level just below triggerLevel).
   *
   * When `null`, all records below {@link triggerLevel} are buffered
   * (same as `undefined`, but explicit).
   *
   * @example Buffer only trace and debug, pass through info immediately
   * ```typescript
   * fingersCrossed(sink, {
   *   bufferLevel: "debug",     // trace, debug → buffered
   *   triggerLevel: "warning",  // warning+ → trigger flush
   *   // info → passes through immediately (not buffered, not trigger)
   * })
   * ```
   *
   * @default `undefined` (buffer all levels below triggerLevel)
   * @since 2.0.0
   */
  readonly bufferLevel?: LogLevel | null;
  /**
   * Maximum buffer size before oldest records are dropped.
   * When the buffer exceeds this size, the oldest records are removed
   * to prevent unbounded memory growth.
   * @default `1000`
   */
  readonly maxBufferSize?: number;
  /**
   * Category isolation mode or custom matcher function.
   *
   * When `undefined` (default), all log records share a single buffer.
   *
   * When set to a mode string:
   *
   * - `"descendant"`: Flush child category buffers when parent triggers
   * - `"ancestor"`: Flush parent category buffers when child triggers
   * - `"both"`: Flush both parent and child category buffers
   *
   * When set to a function, it receives the trigger category and buffered
   * category and should return true if the buffered category should be flushed.
   *
   * @default `undefined` (no isolation, single global buffer)
   */
  readonly isolateByCategory?: "descendant" | "ancestor" | "both" | ((triggerCategory: readonly string[], bufferedCategory: readonly string[]) => boolean);
  /**
   * Enable context-based buffer isolation.
   * When enabled, buffers are isolated based on specified context keys.
   * This is useful for scenarios like HTTP request tracing where logs
   * should be isolated per request.
   *
   * @example
   * ```typescript
   * fingersCrossed(sink, {
   *   isolateByContext: { keys: ['requestId'] }
   * })
   * ```
   *
   * @example Combined with category isolation
   * ```typescript
   * fingersCrossed(sink, {
   *   isolateByCategory: 'descendant',
   *   isolateByContext: { keys: ['requestId', 'sessionId'] }
   * })
   * ```
   *
   * @example With TTL-based buffer cleanup
   * ```typescript
   * fingersCrossed(sink, {
   *   isolateByContext: {
   *     keys: ['requestId'],
   *     bufferTtlMs: 30000,        // 30 seconds
   *     cleanupIntervalMs: 10000   // cleanup every 10 seconds
   *   }
   * })
   * ```
   *
   * @default `undefined` (no context isolation)
   * @since 1.2.0
   */
  readonly isolateByContext?: {
    /**
     * Context keys to use for isolation.
     * Buffers will be separate for different combinations of these context values.
     */
    readonly keys: readonly string[];
    /**
     * Maximum number of context buffers to maintain simultaneously.
     * When this limit is exceeded, the least recently used (LRU) buffers
     * will be evicted to make room for new ones.
     *
     * This provides memory protection in high-concurrency scenarios where
     * many different context values might be active simultaneously.
     *
     * When set to 0 or undefined, no limit is enforced.
     *
     * @default `undefined` (no limit)
     * @since 1.2.0
     */
    readonly maxContexts?: number;
    /**
     * Time-to-live for context buffers in milliseconds.
     * Buffers that haven't been accessed for this duration will be automatically
     * cleaned up to prevent memory leaks in long-running applications.
     *
     * When set to 0 or undefined, buffers will never expire based on time.
     *
     * @default `undefined` (no TTL)
     * @since 1.2.0
     */
    readonly bufferTtlMs?: number;
    /**
     * Interval in milliseconds for running cleanup operations.
     * The cleanup process removes expired buffers based on {@link bufferTtlMs}.
     *
     * This option is ignored if {@link bufferTtlMs} is not set.
     *
     * @default `30000` (30 seconds)
     * @since 1.2.0
     */
    readonly cleanupIntervalMs?: number;
  };
}
/**
 * Creates a sink that buffers log records until a trigger level is reached.
 * This pattern, known as "fingers crossed" logging, keeps detailed debug logs
 * in memory and only outputs them when an error or other significant event occurs.
 *
 * @example Basic usage with default settings
 * ```typescript
 * const sink = fingersCrossed(getConsoleSink());
 * // Debug and info logs are buffered
 * // When an error occurs, all buffered logs + the error are output
 * ```
 *
 * @example Custom trigger level and buffer size
 * ```typescript
 * const sink = fingersCrossed(getConsoleSink(), {
 *   triggerLevel: "warning",  // Trigger on warning or higher
 *   maxBufferSize: 500        // Keep last 500 records
 * });
 * ```
 *
 * @example Category isolation
 * ```typescript
 * const sink = fingersCrossed(getConsoleSink(), {
 *   isolateByCategory: "descendant"  // Separate buffers per category
 * });
 * // Error in ["app"] triggers flush of ["app"] and ["app", "module"] buffers
 * // But not ["other"] buffer
 * ```
 *
 * @param sink The sink to wrap. Buffered records are sent to this sink when
 *             triggered.
 * @param options Configuration options for the fingers crossed behavior.
 * @returns A sink that buffers records until the trigger level is reached.
 * @since 1.1.0
 */
declare function fingersCrossed(sink: Sink, options?: FingersCrossedOptions): Sink | (Sink & Disposable);
//#endregion
export { AsyncSink, ConsoleSinkOptions, FingersCrossedOptions, Sink, StreamSinkOptions, fingersCrossed, fromAsyncSink, getConsoleSink, getStreamSink, withFilter };
//# sourceMappingURL=sink.d.cts.map