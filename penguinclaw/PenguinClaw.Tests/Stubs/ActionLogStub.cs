// Stub for tests — replaces disk-based action log with in-memory list.
// PenguinClawActionLog is called by RetryPolicy in LlmProviders.cs.
// Since PenguinClawActionLog.cs is NOT included in the test project, we define
// the stub freely here in the same namespace.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace PenguinClaw
{
    internal static class PenguinClawActionLog
    {
        public static readonly List<string> TestLog = new();

        public static void RecordRetry(string provider, int statusCode, int attempt)
            => TestLog.Add($"retry:{provider}:{statusCode}:{attempt}");

        public static void RecordRetryFailure(string provider, int statusCode, int attempts)
            => TestLog.Add($"retry_fail:{provider}:{statusCode}:{attempts}");

        public static void RecordMalformedCall(string provider)
            => TestLog.Add($"malformed:{provider}");

        public static void Record(string toolName, JObject input, string resultJson) { }

        public static string? GetContextBlock() => null;
    }
}
