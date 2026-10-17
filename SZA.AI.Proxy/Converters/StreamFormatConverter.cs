using System.Runtime.CompilerServices;
using System.Text;

namespace SZA.AI.Proxy.Converters
{
    public static class StreamFormatConverter
    {
        // SSE (provider) -> NDJSON lines, one at a time (lazy).
        public static async IAsyncEnumerable<string> SseToNdjsonLinesAsync(
            Stream sseStream,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            using var reader = new StreamReader(sseStream, Encoding.UTF8, leaveOpen: true);
            var sb = new StringBuilder();

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;

                if (line.Length == 0)
                {
                    if (sb.Length > 0)
                    {
                        yield return sb.ToString();
                        sb.Clear();
                    }
                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                var data = line.AsSpan(5).Trim().ToString();
                if (data == "[DONE]") yield break;

                if (sb.Length > 0) sb.Append('\n');
                sb.Append(data);
            }

            if (sb.Length > 0) yield return sb.ToString();
        }

        // NDJSON (provider) -> SSE events, one at a time (lazy).
        public static async IAsyncEnumerable<string> NdjsonToSseEventsAsync(
            Stream ndjsonStream,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            using var reader = new StreamReader(ndjsonStream, Encoding.UTF8, leaveOpen: true);

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                yield return $"data: {line}\n\n";
            }

            yield return "data: [DONE]\n\n";
        }
    }
}
