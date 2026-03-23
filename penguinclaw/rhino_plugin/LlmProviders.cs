using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PenguinClaw
{
    // ── Configuration ────────────────────────────────────────────────────────

    internal class ProviderConfig
    {
        public string Provider  { get; set; } = "anthropic"; // "anthropic" | "groq" | "ollama"
        public string ApiKey    { get; set; } = "";
        public string Model     { get; set; } = "";          // empty = use provider default
        public string OllamaUrl { get; set; } = "http://localhost:11434";
    }

    // ── Common response types ─────────────────────────────────────────────────

    internal class LlmToolCall
    {
        public string  Id    { get; set; }
        public string  Name  { get; set; }
        public JObject Input { get; set; }
    }

    internal class LlmResponse
    {
        /// <summary>"end_turn" | "tool_use" | "error"</summary>
        public string             StopReason   { get; set; }
        public string             Text         { get; set; }
        public List<LlmToolCall>  ToolCalls    { get; set; } = new List<LlmToolCall>();
        public string             ErrorMessage { get; set; }
    }

    // ── Provider interface ────────────────────────────────────────────────────

    internal interface ILlmProvider
    {
        /// <summary>
        /// Send a chat completion request.
        /// systemBlocks: Anthropic-format system JArray (cache_control blocks included — stripped by non-Anthropic providers).
        /// messages:     Anthropic-format message history (tool_result blocks included — converted by non-Anthropic providers).
        /// tools:        Anthropic-format tool definitions (input_schema — converted by non-Anthropic providers).
        /// </summary>
        LlmResponse Send(JArray systemBlocks, JArray messages, JArray tools, int maxTokens);
    }

    // ── Anthropic provider ────────────────────────────────────────────────────

    internal class AnthropicProvider : ILlmProvider
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";

        private readonly string _apiKey;
        private readonly string _model;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        public AnthropicProvider(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model  = string.IsNullOrWhiteSpace(model) ? "claude-haiku-4-5-20251001" : model;
        }

        public LlmResponse Send(JArray systemBlocks, JArray messages, JArray tools, int maxTokens)
        {
            var body = new JObject
            {
                ["model"]      = _model,
                ["max_tokens"] = maxTokens,
                ["system"]     = systemBlocks,
                ["tools"]      = tools,
                ["messages"]   = messages,
            };

            string rawResp;
            System.Net.HttpStatusCode status;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
                {
                    Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("x-api-key", _apiKey);
                req.Headers.Add("anthropic-version", "2023-06-01");
                req.Headers.Add("anthropic-beta", "prompt-caching-2024-07-31");

                var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                status  = resp.StatusCode;
                rawResp = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return ProviderHelpers.Err($"Connection error: {ex.Message}");
            }

            if ((int)status >= 400)
                return ProviderHelpers.Err(ProviderHelpers.FriendlyApiError("Anthropic", (int)status, rawResp));

            JObject parsed;
            try { parsed = JObject.Parse(rawResp); }
            catch { return ProviderHelpers.Err($"Unexpected API response: {rawResp}"); }

            var stopReason = parsed["stop_reason"]?.ToString();
            var content    = (parsed["content"] as JArray) ?? new JArray();

            var sb    = new StringBuilder();
            var calls = new List<LlmToolCall>();

            foreach (var block in content)
            {
                var type = block["type"]?.ToString();
                if (type == "text")
                    sb.Append(block["text"]?.ToString() ?? "");
                else if (type == "tool_use")
                    calls.Add(new LlmToolCall
                    {
                        Id    = block["id"]?.ToString()    ?? "",
                        Name  = block["name"]?.ToString()  ?? "",
                        Input = (block["input"] as JObject) ?? new JObject(),
                    });
            }

            return new LlmResponse
            {
                StopReason = stopReason == "tool_use" ? "tool_use" : "end_turn",
                Text       = sb.ToString().Trim(),
                ToolCalls  = calls,
            };
        }
    }

    // ── OpenAI-compatible provider (Groq + Ollama) ────────────────────────────

    internal class OpenAiCompatProvider : ILlmProvider
    {
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _model;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        public OpenAiCompatProvider(string baseUrl, string apiKey, string model)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey  = apiKey ?? "";
            _model   = model;
        }

        public LlmResponse Send(JArray systemBlocks, JArray messages, JArray tools, int maxTokens)
        {
            var oaiMessages = BuildOaiMessages(systemBlocks, messages);
            var oaiTools    = BuildOaiTools(tools);

            var body = new JObject
            {
                ["model"]      = _model,
                ["max_tokens"] = maxTokens,
                ["messages"]   = oaiMessages,
            };
            if (oaiTools.Count > 0)
            {
                body["tools"]       = oaiTools;
                body["tool_choice"] = "auto";
            }

            string rawResp;
            System.Net.HttpStatusCode status;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
                {
                    Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
                };
                if (!string.IsNullOrWhiteSpace(_apiKey))
                    req.Headers.Add("Authorization", $"Bearer {_apiKey}");

                var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                status  = resp.StatusCode;
                rawResp = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return ProviderHelpers.Err($"Connection error: {ex.Message}");
            }

            if ((int)status >= 400)
                return ProviderHelpers.Err(ProviderHelpers.FriendlyApiError("AI provider", (int)status, rawResp));

            JObject parsed;
            try { parsed = JObject.Parse(rawResp); }
            catch { return ProviderHelpers.Err($"Unexpected API response: {rawResp}"); }

            var choice       = parsed["choices"]?[0];
            var finishReason = choice?["finish_reason"]?.ToString();
            var msg          = choice?["message"] as JObject;

            if (msg == null)
                return ProviderHelpers.Err($"Unexpected response structure: {rawResp}");

            var text  = msg["content"]?.ToString()?.Trim() ?? "";
            var calls = new List<LlmToolCall>();

            if (finishReason == "tool_calls")
            {
                var tcArr = msg["tool_calls"] as JArray;
                if (tcArr != null)
                {
                    foreach (var tc in tcArr)
                    {
                        var fn      = tc["function"] as JObject;
                        var argStr  = fn?["arguments"]?.ToString() ?? "{}";
                        JObject args;
                        try { args = JObject.Parse(argStr); } catch { args = new JObject(); }

                        calls.Add(new LlmToolCall
                        {
                            Id    = tc["id"]?.ToString()       ?? Guid.NewGuid().ToString(),
                            Name  = fn?["name"]?.ToString()    ?? "",
                            Input = args,
                        });
                    }
                }
            }

            return new LlmResponse
            {
                StopReason = calls.Count > 0 ? "tool_use" : "end_turn",
                Text       = text,
                ToolCalls  = calls,
            };
        }

        // Convert Anthropic-format messages → OpenAI-format messages
        private static JArray BuildOaiMessages(JArray systemBlocks, JArray anthropicMessages)
        {
            var result = new JArray();

            // Flatten system blocks into a single system message
            var sysSb = new StringBuilder();
            foreach (var block in systemBlocks)
                if (block["type"]?.ToString() == "text")
                    sysSb.AppendLine(block["text"]?.ToString() ?? "");
            if (sysSb.Length > 0)
                result.Add(new JObject { ["role"] = "system", ["content"] = sysSb.ToString().Trim() });

            foreach (var msg in anthropicMessages)
            {
                var role    = msg["role"]?.ToString();
                var content = msg["content"];

                // Plain string content
                if (content == null || content.Type == JTokenType.String)
                {
                    result.Add(new JObject { ["role"] = role, ["content"] = content?.ToString() ?? "" });
                    continue;
                }

                if (content is JArray arr)
                {
                    // Anthropic tool_result user message → individual OpenAI "tool" messages
                    if (arr.Count > 0 && arr[0]["type"]?.ToString() == "tool_result")
                    {
                        foreach (var block in arr)
                        {
                            if (block["type"]?.ToString() == "tool_result")
                                result.Add(new JObject
                                {
                                    ["role"]         = "tool",
                                    ["tool_call_id"] = block["tool_use_id"]?.ToString() ?? "",
                                    ["content"]      = block["content"]?.ToString() ?? "",
                                });
                        }
                        continue;
                    }

                    // Assistant message — separate text from tool_use blocks
                    if (role == "assistant")
                    {
                        var textSb  = new StringBuilder();
                        var tcArray = new JArray();

                        foreach (var block in arr)
                        {
                            var type = block["type"]?.ToString();
                            if (type == "text")
                                textSb.Append(block["text"]?.ToString() ?? "");
                            else if (type == "tool_use")
                                tcArray.Add(new JObject
                                {
                                    ["id"]   = block["id"]?.ToString() ?? "",
                                    ["type"] = "function",
                                    ["function"] = new JObject
                                    {
                                        ["name"]      = block["name"]?.ToString() ?? "",
                                        ["arguments"] = (block["input"] ?? new JObject()).ToString(Formatting.None),
                                    },
                                });
                        }

                        var aMsg = new JObject { ["role"] = "assistant" };
                        aMsg["content"] = textSb.Length > 0 ? (JToken)textSb.ToString() : JValue.CreateNull();
                        if (tcArray.Count > 0)
                            aMsg["tool_calls"] = tcArray;
                        result.Add(aMsg);
                        continue;
                    }

                    // Regular user message with content array — flatten to string
                    var sb = new StringBuilder();
                    foreach (var block in arr)
                        if (block["type"]?.ToString() == "text")
                            sb.Append(block["text"]?.ToString() ?? "");
                    result.Add(new JObject { ["role"] = role, ["content"] = sb.ToString() });
                }
            }

            return result;
        }

        // Convert Anthropic-format tool definitions → OpenAI-format
        // Strips cache_control, renames input_schema → parameters, wraps in {"type":"function","function":{...}}
        private static JArray BuildOaiTools(JArray anthropicTools)
        {
            var result = new JArray();
            foreach (var tool in anthropicTools)
            {
                result.Add(new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"]        = tool["name"],
                        ["description"] = tool["description"],
                        ["parameters"]  = tool["input_schema"]
                                          ?? new JObject { ["type"] = "object", ["properties"] = new JObject() },
                    },
                });
            }
            return result;
        }
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    internal static class LlmProviderFactory
    {
        public static ILlmProvider Create(ProviderConfig cfg)
        {
            switch ((cfg.Provider ?? "anthropic").ToLowerInvariant())
            {
                case "groq":
                    return new OpenAiCompatProvider(
                        "https://api.groq.com/openai/v1",
                        cfg.ApiKey,
                        string.IsNullOrWhiteSpace(cfg.Model) ? "llama-3.3-70b-versatile" : cfg.Model);

                case "ollama":
                    var ollamaBase = (string.IsNullOrWhiteSpace(cfg.OllamaUrl)
                        ? "http://localhost:11434"
                        : cfg.OllamaUrl.TrimEnd('/')) + "/v1";
                    return new OpenAiCompatProvider(
                        ollamaBase,
                        "",   // no auth for Ollama
                        string.IsNullOrWhiteSpace(cfg.Model) ? "qwen2.5:7b" : cfg.Model);

                default: // "anthropic"
                    return new AnthropicProvider(
                        cfg.ApiKey,
                        string.IsNullOrWhiteSpace(cfg.Model) ? "claude-haiku-4-5-20251001" : cfg.Model);
            }
        }

        /// <summary>Human-readable default model name for display.</summary>
        public static string DefaultModel(string provider)
        {
            switch ((provider ?? "").ToLowerInvariant())
            {
                case "groq":   return "llama-3.3-70b-versatile";
                case "ollama": return "qwen2.5:7b";
                default:       return "claude-haiku-4-5";
            }
        }
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    internal static class ProviderHelpers
    {
        public static LlmResponse Err(string msg) =>
            new LlmResponse { StopReason = "error", ErrorMessage = msg };

        public static string FriendlyApiError(string providerName, int status, string body)
        {
            switch (status)
            {
                case 401: return $"{providerName}: Invalid API key. Check your key in Settings.";
                case 403: return $"{providerName}: Access denied (403). Check your API key permissions.";
                case 429: return $"{providerName}: Rate limit reached. Wait a moment and try again.";
                case 500:
                case 503: return $"{providerName}: Service temporarily unavailable. Try again shortly.";
                default:  return $"{providerName} error {status}: {body}";
            }
        }
    }
}
