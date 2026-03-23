using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json.Linq;
using PenguinClaw;
using Xunit;

namespace PenguinClaw.Tests
{
    public class ProviderConfigTests
    {
        [Fact]
        public void ProviderConfig_DefaultProvider_IsAnthropic()
        {
            var cfg = new ProviderConfig();
            Assert.Equal("anthropic", cfg.Provider);
        }

        [Fact]
        public void ProviderConfig_DefaultApiKey_IsEmpty()
        {
            var cfg = new ProviderConfig();
            Assert.Equal("", cfg.ApiKey);
        }

        [Fact]
        public void ProviderConfig_DefaultOllamaUrl_IsLocalhost()
        {
            var cfg = new ProviderConfig();
            Assert.Equal("http://localhost:11434", cfg.OllamaUrl);
        }

        [Fact]
        public void ProviderConfig_DefaultModel_IsEmpty()
        {
            var cfg = new ProviderConfig();
            Assert.Equal("", cfg.Model);
        }
    }

    public class LlmProviderFactoryTests
    {
        [Fact]
        public void Factory_Anthropic_ReturnsAnthropicProvider()
        {
            var cfg = new ProviderConfig { Provider = "anthropic", ApiKey = "test-key" };
            var provider = LlmProviderFactory.Create(cfg);
            Assert.IsType<AnthropicProvider>(provider);
        }

        [Fact]
        public void Factory_Groq_ReturnsOpenAiCompatProvider()
        {
            var cfg = new ProviderConfig { Provider = "groq", ApiKey = "gsk_test" };
            var provider = LlmProviderFactory.Create(cfg);
            Assert.IsType<OpenAiCompatProvider>(provider);
        }

        [Fact]
        public void Factory_Ollama_ReturnsOpenAiCompatProvider()
        {
            var cfg = new ProviderConfig { Provider = "ollama" };
            var provider = LlmProviderFactory.Create(cfg);
            Assert.IsType<OpenAiCompatProvider>(provider);
        }

        [Fact]
        public void Factory_CaseInsensitive_Anthropic()
        {
            var cfg = new ProviderConfig { Provider = "ANTHROPIC", ApiKey = "test-key" };
            var provider = LlmProviderFactory.Create(cfg);
            Assert.IsType<AnthropicProvider>(provider);
        }

        [Fact]
        public void Factory_CaseInsensitive_Groq()
        {
            var cfg = new ProviderConfig { Provider = "GROQ", ApiKey = "gsk_test" };
            var provider = LlmProviderFactory.Create(cfg);
            Assert.IsType<OpenAiCompatProvider>(provider);
        }

        [Fact]
        public void Factory_UnknownProvider_FallsBackToAnthropic()
        {
            var cfg = new ProviderConfig { Provider = "unknown_provider", ApiKey = "key" };
            var provider = LlmProviderFactory.Create(cfg);
            Assert.IsType<AnthropicProvider>(provider);
        }

        [Fact]
        public void Factory_NullProvider_FallsBackToAnthropic()
        {
            var cfg = new ProviderConfig { Provider = null!, ApiKey = "key" };
            var provider = LlmProviderFactory.Create(cfg);
            Assert.IsType<AnthropicProvider>(provider);
        }

        [Fact]
        public void DefaultModel_Anthropic_ReturnsHaiku()
        {
            var model = LlmProviderFactory.DefaultModel("anthropic");
            Assert.Contains("haiku", model, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DefaultModel_Groq_ReturnsLlama()
        {
            var model = LlmProviderFactory.DefaultModel("groq");
            Assert.Contains("llama", model, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DefaultModel_Ollama_ReturnsQwen()
        {
            var model = LlmProviderFactory.DefaultModel("ollama");
            Assert.Contains("qwen", model, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DefaultModel_Unknown_ReturnsNonEmpty()
        {
            var model = LlmProviderFactory.DefaultModel("totally_unknown");
            Assert.False(string.IsNullOrWhiteSpace(model));
        }

        [Fact]
        public void DefaultModel_CaseInsensitive()
        {
            var lower = LlmProviderFactory.DefaultModel("groq");
            var upper = LlmProviderFactory.DefaultModel("GROQ");
            Assert.Equal(lower, upper);
        }
    }

    public class ProviderHelpersTests
    {
        [Fact]
        public void Err_SetsStopReasonToError()
        {
            var resp = ProviderHelpers.Err("test error");
            Assert.Equal("error", resp.StopReason);
        }

        [Fact]
        public void Err_SetsErrorMessage()
        {
            var resp = ProviderHelpers.Err("test error");
            Assert.Equal("test error", resp.ErrorMessage);
        }

        [Fact]
        public void FriendlyApiError_401_MentionsApiKey()
        {
            var msg = ProviderHelpers.FriendlyApiError("Test", 401, "");
            Assert.Contains("API key", msg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FriendlyApiError_403_MentionsAccessDenied()
        {
            var msg = ProviderHelpers.FriendlyApiError("Test", 403, "");
            Assert.Contains("denied", msg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FriendlyApiError_429_MentionsRateLimit()
        {
            var msg = ProviderHelpers.FriendlyApiError("Test", 429, "");
            Assert.Contains("rate", msg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FriendlyApiError_500_MentionsUnavailable()
        {
            var msg = ProviderHelpers.FriendlyApiError("Test", 500, "");
            Assert.Contains("unavailable", msg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FriendlyApiError_503_MentionsUnavailable()
        {
            var msg = ProviderHelpers.FriendlyApiError("Test", 503, "");
            Assert.Contains("unavailable", msg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FriendlyApiError_Unknown_IncludesStatusCode()
        {
            var msg = ProviderHelpers.FriendlyApiError("Test", 418, "teapot");
            Assert.Contains("418", msg);
        }

        [Fact]
        public void FriendlyApiError_IncludesProviderName()
        {
            var msg = ProviderHelpers.FriendlyApiError("MyProvider", 401, "");
            Assert.Contains("MyProvider", msg);
        }
    }

    public class RetryPolicyTests
    {
        [Fact]
        public void RetryPolicy_SuccessOnFirstAttempt_NoRetries()
        {
            PenguinClawActionLog.TestLog.Clear();
            int callCount = 0;
            var result = RetryPolicy.SendWithRetry(
                () => { callCount++; return (HttpStatusCode.OK, "{\"stop_reason\":\"end_turn\",\"content\":[]}"); },
                (status, body) => ProviderHelpers.Err("parsed"),
                "Test");
            Assert.Equal(1, callCount);
            Assert.Empty(PenguinClawActionLog.TestLog);
        }

        [Fact]
        public void RetryPolicy_429_RetriesAndLogsRetry()
        {
            PenguinClawActionLog.TestLog.Clear();
            int callCount = 0;
            RetryPolicy.SendWithRetry(
                () =>
                {
                    callCount++;
                    if (callCount < 3) return (HttpStatusCode.TooManyRequests, "rate limited");
                    return (HttpStatusCode.OK, "ok");
                },
                (status, body) => ProviderHelpers.Err(status.ToString()),
                "TestProvider");

            Assert.True(callCount >= 2);
            Assert.True(PenguinClawActionLog.TestLog.Count >= 1);
            Assert.Contains(PenguinClawActionLog.TestLog, e => e.StartsWith("retry:TestProvider"));
        }

        [Fact]
        public void RetryPolicy_500_Retries()
        {
            PenguinClawActionLog.TestLog.Clear();
            int callCount = 0;
            RetryPolicy.SendWithRetry(
                () =>
                {
                    callCount++;
                    if (callCount == 1) return (HttpStatusCode.InternalServerError, "error");
                    return (HttpStatusCode.OK, "ok");
                },
                (status, body) => ProviderHelpers.Err(status.ToString()),
                "TestProvider");

            Assert.True(callCount >= 2);
        }

        [Fact]
        public void RetryPolicy_AllAttemptsFailWith429_LogsRetryFailure()
        {
            PenguinClawActionLog.TestLog.Clear();
            RetryPolicy.SendWithRetry(
                () => (HttpStatusCode.TooManyRequests, "always rate limited"),
                (status, body) => ProviderHelpers.Err("rate limited"),
                "TestProvider");

            Assert.Contains(PenguinClawActionLog.TestLog,
                e => e.StartsWith("retry_fail:TestProvider"));
        }

        [Fact]
        public void RetryPolicy_ConnectionException_ReturnsConnectionError()
        {
            PenguinClawActionLog.TestLog.Clear();
            var result = RetryPolicy.SendWithRetry(
                () => throw new Exception("network failure"),
                (status, body) => ProviderHelpers.Err("parsed"),
                "Test");
            Assert.Equal("error", result.StopReason);
            Assert.Contains("Connection error", result.ErrorMessage!);
        }

        [Fact]
        public void RetryPolicy_200Response_CallsParser()
        {
            bool parserCalled = false;
            RetryPolicy.SendWithRetry(
                () => (HttpStatusCode.OK, "body"),
                (status, body) =>
                {
                    parserCalled = true;
                    return ProviderHelpers.Err("ok");
                },
                "Test");
            Assert.True(parserCalled);
        }

        [Fact]
        public void RetryPolicy_400Response_DoesNotRetry()
        {
            int callCount = 0;
            RetryPolicy.SendWithRetry(
                () => { callCount++; return (HttpStatusCode.BadRequest, "bad"); },
                (status, body) => ProviderHelpers.Err("bad request"),
                "Test");
            // 400 should NOT be retried
            Assert.Equal(1, callCount);
        }
    }

    public class LlmResponseTests
    {
        [Fact]
        public void LlmResponse_DefaultToolCalls_IsEmptyList()
        {
            var resp = new LlmResponse();
            Assert.NotNull(resp.ToolCalls);
            Assert.Empty(resp.ToolCalls);
        }

        [Fact]
        public void LlmToolCall_PropertiesRoundtrip()
        {
            var tc = new LlmToolCall
            {
                Id    = "call-123",
                Name  = "get_volume",
                Input = JObject.Parse("{\"object_id\":\"abc\"}"),
            };
            Assert.Equal("call-123", tc.Id);
            Assert.Equal("get_volume", tc.Name);
            Assert.Equal("abc", tc.Input["object_id"]?.ToString());
        }
    }
}
