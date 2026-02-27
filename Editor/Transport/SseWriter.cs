using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace NativeMcp.Editor.Transport
{
    /// <summary>
    /// Writes Server-Sent Events (SSE) formatted data to an output stream.
    /// Each message follows the format: "event: message\ndata: {json}\n\n"
    /// </summary>
    internal class SseWriter
    {
        private readonly Stream _stream;
        private readonly object _writeLock = new object();

        public SseWriter(Stream outputStream)
        {
            _stream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));
        }

        /// <summary>
        /// Write a single SSE event containing JSON data.
        /// </summary>
        public async Task WriteEventAsync(string jsonData)
        {
            if (string.IsNullOrEmpty(jsonData))
            {
                return;
            }

            // SSE format: "event: message\ndata: {json}\n\n"
            var sb = new StringBuilder();
            sb.Append("event: message\n");
            sb.Append("data: ");
            sb.Append(jsonData);
            sb.Append("\n\n");

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());

            try
            {
                await _stream.WriteAsync(bytes, 0, bytes.Length);
                await _stream.FlushAsync();
            }
            catch (ObjectDisposedException)
            {
                // Client disconnected
            }
            catch (IOException)
            {
                // Client disconnected
            }
        }
    }
}
