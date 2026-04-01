using System.Threading;

namespace NativeMcp.Editor.Transport
{
    /// <summary>
    /// Bundles a CancellationTokenSource with an optional cancellation reason.
    /// Allows request handlers to distinguish domain-reload cancellation from
    /// other stop causes without coupling transport and protocol handler directly.
    /// </summary>
    internal sealed class McpCancellationContext
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public CancellationToken Token => _cts.Token;

        /// <summary>
        /// Set when Cancel() is called. Null means no specific reason (e.g. normal shutdown).
        /// </summary>
        public string CancelReason { get; private set; }

        public void Cancel(string reason = null)
        {
            CancelReason = reason;
            _cts.Cancel();
        }

        public void Dispose() => _cts.Dispose();
    }
}
