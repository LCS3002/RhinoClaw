using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PenguinClaw
{
    /// <summary>
    /// In-memory ring buffer for debug logging — captures every tool call, user message,
    /// and agent response. Served via GET /log so the UI can display a live feed.
    /// </summary>
    internal static class PenguinClawDebugLog
    {
        private static readonly Queue<JObject> _buffer = new Queue<JObject>();
        private const int MaxEntries = 500;
        private static readonly object _lock = new object();
        private static int _seq = 0;

        // ── Public write API ─────────────────────────────────────────────────────

        public static void LogUser(string message)
            => Append("user", "USER", message, null);

        public static void LogAgent(string text)
            => Append("agent", "AGENT", text?.Length > 300 ? text.Substring(0, 300) + "…" : text, null);

        public static void LogTool(string name, JObject input, string result)
        {
            // Truncate large results (e.g. base64 images) for display
            var resultDisplay = result;
            if (resultDisplay != null && resultDisplay.Length > 600)
                resultDisplay = resultDisplay.Substring(0, 600) + "…";

            // Abbreviate base64 blobs inside JSON
            try
            {
                var j = JObject.Parse(result);
                if (j["base64"] != null) { j["base64"] = "[image data]"; resultDisplay = j.ToString(Formatting.None); }
            }
            catch { }

            var inputDisplay = input?.ToString(Formatting.None) ?? "{}";
            if (inputDisplay.Length > 400) inputDisplay = inputDisplay.Substring(0, 400) + "…";

            Append("tool", name, inputDisplay, resultDisplay);
        }

        public static void LogEvent(string label, string detail)
            => Append("event", label, detail, null);

        // ── Read API ─────────────────────────────────────────────────────────────

        public static JArray GetEntries(int limit = 300, int afterSeq = -1)
        {
            lock (_lock)
            {
                var entries = afterSeq >= 0
                    ? _buffer.Where(e => e["seq"]?.ToObject<int>() > afterSeq).ToList()
                    : _buffer.ToList();

                // Return last `limit` entries
                if (entries.Count > limit)
                    entries = entries.Skip(entries.Count - limit).ToList();

                return new JArray(entries);
            }
        }

        public static int LatestSeq()
        {
            lock (_lock) { return _seq; }
        }

        // ── Internals ────────────────────────────────────────────────────────────

        private static void Append(string type, string label, string body, string detail)
        {
            lock (_lock)
            {
                _seq++;
                _buffer.Enqueue(new JObject
                {
                    ["seq"]    = _seq,
                    ["ts"]     = DateTime.Now.ToString("HH:mm:ss.fff"),
                    ["type"]   = type,   // "user" | "agent" | "tool" | "event"
                    ["label"]  = label,
                    ["body"]   = body ?? "",
                    ["detail"] = detail ?? "",
                });
                while (_buffer.Count > MaxEntries)
                    _buffer.Dequeue();
            }
        }
    }
}
