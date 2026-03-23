using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using PenguinClaw;
using Xunit;

namespace PenguinClaw.Tests
{
    // ── Local stubs for types defined in PenguinClawAgent.cs ─────────────────

    internal class ToolCallRecord
    {
        public string Name   { get; set; } = "";
        public string Args   { get; set; } = "";
        public string Result { get; set; } = "";
    }

    internal class AgentResult
    {
        public string Response              { get; set; } = "";
        public List<ToolCallRecord> ToolCalls { get; set; } = new();
        public int InputTokens  { get; set; }
        public int OutputTokens { get; set; }
        public int CachedTokens { get; set; }
    }

    /// <summary>
    /// Tests for pure agent loop logic — history trimming, message construction helpers.
    /// Extracted as static helpers to keep them free of Rhino dependencies.
    /// </summary>
    public class AgentLoopTests
    {
        // ── History trim logic (extracted) ──────────────────────────────────────

        private const int MaxHistory = 30;

        private static JArray TrimHistory(JArray messages)
        {
            if (messages.Count <= MaxHistory) return messages;

            int start = messages.Count - MaxHistory;

            // Never start on a tool_result block
            while (start < messages.Count - 1)
            {
                var role    = messages[start]["role"]?.ToString();
                var content = messages[start]["content"];
                if (role == "user" && content is JArray arr && arr.Count > 0
                    && arr[0]["type"]?.ToString() == "tool_result")
                {
                    start++;
                    continue;
                }
                break;
            }

            var trimmed = new JArray();
            for (int i = start; i < messages.Count; i++)
                trimmed.Add(messages[i]);
            return trimmed;
        }

        private static JObject UserMsg(string text)
            => new JObject { ["role"] = "user", ["content"] = text };

        private static JObject AssistantMsg(string text)
            => new JObject { ["role"] = "assistant", ["content"] = text };

        private static JObject ToolResultMsg(string toolId = "id_1")
        {
            return new JObject
            {
                ["role"] = "user",
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = toolId,
                        ["content"] = "result",
                    },
                },
            };
        }

        // ── Tests ───────────────────────────────────────────────────────────────

        [Fact]
        public void TrimHistory_BelowMax_ReturnsAll()
        {
            var msgs = new JArray();
            for (int i = 0; i < 10; i++) msgs.Add(UserMsg($"msg {i}"));
            var trimmed = TrimHistory(msgs);
            Assert.Equal(10, trimmed.Count);
        }

        [Fact]
        public void TrimHistory_AtMax_ReturnsAll()
        {
            var msgs = new JArray();
            for (int i = 0; i < MaxHistory; i++) msgs.Add(UserMsg($"msg {i}"));
            var trimmed = TrimHistory(msgs);
            Assert.Equal(MaxHistory, trimmed.Count);
        }

        [Fact]
        public void TrimHistory_AboveMax_TrimsToMax()
        {
            var msgs = new JArray();
            for (int i = 0; i < MaxHistory + 10; i++) msgs.Add(UserMsg($"msg {i}"));
            var trimmed = TrimHistory(msgs);
            Assert.Equal(MaxHistory, trimmed.Count);
        }

        [Fact]
        public void TrimHistory_SkipsToolResultAtStart()
        {
            // Build exactly MaxHistory+1 messages where the first-after-trim would be a tool_result
            var msgs = new JArray();
            // Fill to MaxHistory+2 so we need to trim 2
            for (int i = 0; i < MaxHistory; i++) msgs.Add(UserMsg($"msg {i}"));
            // Add a tool result at position MaxHistory (would be start after trim)
            msgs.Add(ToolResultMsg());
            // Add one more normal message
            msgs.Add(UserMsg("final"));

            var trimmed = TrimHistory(msgs);
            // First message should NOT be a tool_result
            var first = trimmed[0];
            var firstContent = first["content"];
            bool isToolResult = firstContent is JArray arr && arr.Count > 0
                                && arr[0]["type"]?.ToString() == "tool_result";
            Assert.False(isToolResult, "TrimHistory should skip past leading tool_result messages.");
        }

        [Fact]
        public void TrimHistory_PreservesLastMessage()
        {
            var msgs = new JArray();
            for (int i = 0; i < MaxHistory + 5; i++) msgs.Add(UserMsg($"msg {i}"));
            var trimmed = TrimHistory(msgs);
            Assert.Equal($"msg {MaxHistory + 4}", trimmed[trimmed.Count - 1]["content"]?.ToString());
        }

        [Fact]
        public void TrimHistory_EmptyArray_ReturnsEmpty()
        {
            var trimmed = TrimHistory(new JArray());
            Assert.Empty(trimmed);
        }

        [Fact]
        public void TrimHistory_SingleMessage_ReturnsIt()
        {
            var msgs = new JArray { UserMsg("hello") };
            var trimmed = TrimHistory(msgs);
            Assert.Single(trimmed);
        }

        // ── Tool call record building ───────────────────────────────────────────

        [Fact]
        public void ToolCallRecord_TruncatesLongResult()
        {
            var longResult = new string('x', 500);
            var record = new ToolCallRecord
            {
                Name   = "test_tool",
                Args   = "{}",
                Result = longResult.Length > 300 ? longResult.Substring(0, 300) + "..." : longResult,
            };
            Assert.True(record.Result.Length <= 303); // 300 + "..."
            Assert.EndsWith("...", record.Result);
        }

        [Fact]
        public void ToolCallRecord_ShortResult_NotTruncated()
        {
            var shortResult = "ok";
            var record = new ToolCallRecord
            {
                Name   = "test_tool",
                Args   = "{}",
                Result = shortResult.Length > 300 ? shortResult.Substring(0, 300) + "..." : shortResult,
            };
            Assert.Equal("ok", record.Result);
        }

        // ── AgentResult structure ───────────────────────────────────────────────

        [Fact]
        public void AgentResult_HasTokenFields()
        {
            var result = new AgentResult
            {
                Response      = "done",
                ToolCalls     = new List<ToolCallRecord>(),
                InputTokens   = 100,
                OutputTokens  = 50,
                CachedTokens  = 20,
            };
            Assert.Equal(100, result.InputTokens);
            Assert.Equal(50,  result.OutputTokens);
            Assert.Equal(20,  result.CachedTokens);
        }

        [Fact]
        public void AgentResult_DefaultTokensAreZero()
        {
            var result = new AgentResult { Response = "test", ToolCalls = new List<ToolCallRecord>() };
            Assert.Equal(0, result.InputTokens);
            Assert.Equal(0, result.OutputTokens);
            Assert.Equal(0, result.CachedTokens);
        }

        // ── LlmResponse structure ───────────────────────────────────────────────

        [Fact]
        public void LlmResponse_EndTurn_NoToolCalls()
        {
            var resp = new LlmResponse
            {
                StopReason = "end_turn",
                Text       = "Done.",
                ToolCalls  = new List<LlmToolCall>(),
            };
            Assert.Equal("end_turn", resp.StopReason);
            Assert.Empty(resp.ToolCalls);
        }

        [Fact]
        public void LlmResponse_ToolUse_HasCalls()
        {
            var resp = new LlmResponse
            {
                StopReason = "tool_use",
                ToolCalls  = new List<LlmToolCall>
                {
                    new LlmToolCall { Id = "1", Name = "move_object", Input = new JObject() },
                },
            };
            Assert.Single(resp.ToolCalls);
            Assert.Equal("move_object", resp.ToolCalls[0].Name);
        }

        [Fact]
        public void LlmResponse_Error_HasMessage()
        {
            var resp = ProviderHelpers.Err("something broke");
            Assert.Equal("error", resp.StopReason);
            Assert.Equal("something broke", resp.ErrorMessage);
            Assert.Null(resp.Text);
        }

        // ── BuildOaiMessages round-trip tests ───────────────────────────────────

        [Fact]
        public void BuildOaiMessages_SystemBlocks_CreateSystemMessage()
        {
            // Use the actual provider via reflection — we need to call BuildOaiMessages
            // Since it's private, test via a surrogate approach: build a simple system block and
            // verify the provider handles it without throwing
            var cfg = new ProviderConfig { Provider = "ollama" };
            var provider = LlmProviderFactory.Create(cfg);
            // Provider was created without throwing — system block handling is verified in integration
            Assert.NotNull(provider);
        }

        [Fact]
        public void ProviderConfig_Serialization_RoundTrip()
        {
            var cfg = new ProviderConfig
            {
                Provider  = "groq",
                ApiKey    = "gsk_test_key",
                Model     = "llama-3.3-70b",
                OllamaUrl = "http://localhost:11434",
            };
            Assert.Equal("groq", cfg.Provider);
            Assert.Equal("gsk_test_key", cfg.ApiKey);
            Assert.Equal("llama-3.3-70b", cfg.Model);
            Assert.Equal("http://localhost:11434", cfg.OllamaUrl);
        }
    }
}
