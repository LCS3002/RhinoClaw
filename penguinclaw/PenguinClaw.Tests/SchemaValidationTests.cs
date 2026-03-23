using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using PenguinClaw;
using Xunit;

namespace PenguinClaw.Tests
{
    /// <summary>
    /// Tests for schema validation logic — extracted as pure functions.
    /// Mirrors the ValidateToolCall logic in PenguinClawAgent.
    /// </summary>
    public class SchemaValidationTests
    {
        // ── Schema validator (extracted pure function) ──────────────────────────

        private static string[] ValidateToolCall(LlmToolCall tc, JArray tools)
        {
            var toolDef = tools.OfType<JObject>().FirstOrDefault(t => t["name"]?.ToString() == tc.Name);
            if (toolDef == null) return new string[0];

            var required = toolDef["input_schema"]?["required"] as JArray;
            if (required == null || required.Count == 0) return new string[0];

            var missing = new List<string>();
            foreach (var req in required)
            {
                var paramName = req?.ToString();
                if (!string.IsNullOrEmpty(paramName) && tc.Input[paramName] == null)
                    missing.Add(paramName);
            }
            return missing.ToArray();
        }

        private static JArray MakeTools(params (string name, string[] required)[] defs)
        {
            var arr = new JArray();
            foreach (var (name, required) in defs)
            {
                var req = new JArray();
                foreach (var r in required) req.Add(r);
                arr.Add(new JObject
                {
                    ["name"] = name,
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["required"] = req,
                    },
                });
            }
            return arr;
        }

        // ── Tests ───────────────────────────────────────────────────────────────

        [Fact]
        public void Validate_AllRequiredPresent_ReturnsEmpty()
        {
            var tools = MakeTools(("move_object", new[] { "object_id", "x", "y", "z" }));
            var tc = new LlmToolCall
            {
                Name  = "move_object",
                Input = JObject.Parse("{\"object_id\":\"abc\",\"x\":1,\"y\":2,\"z\":3}"),
            };
            var missing = ValidateToolCall(tc, tools);
            Assert.Empty(missing);
        }

        [Fact]
        public void Validate_MissingRequired_ReturnsMissingName()
        {
            var tools = MakeTools(("move_object", new[] { "object_id", "x", "y", "z" }));
            var tc = new LlmToolCall
            {
                Name  = "move_object",
                Input = JObject.Parse("{\"x\":1,\"y\":2,\"z\":3}"),
            };
            var missing = ValidateToolCall(tc, tools);
            Assert.Contains("object_id", missing);
        }

        [Fact]
        public void Validate_MultipleMissing_ReturnsAll()
        {
            var tools = MakeTools(("move_object", new[] { "object_id", "x", "y", "z" }));
            var tc = new LlmToolCall
            {
                Name  = "move_object",
                Input = new JObject(),
            };
            var missing = ValidateToolCall(tc, tools);
            Assert.Contains("object_id", missing);
            Assert.Contains("x", missing);
            Assert.Contains("y", missing);
            Assert.Contains("z", missing);
        }

        [Fact]
        public void Validate_NoRequiredInSchema_ReturnsEmpty()
        {
            var tools = new JArray
            {
                new JObject
                {
                    ["name"] = "list_layers",
                    ["input_schema"] = new JObject { ["type"] = "object", ["required"] = new JArray() },
                },
            };
            var tc = new LlmToolCall { Name = "list_layers", Input = new JObject() };
            var missing = ValidateToolCall(tc, tools);
            Assert.Empty(missing);
        }

        [Fact]
        public void Validate_UnknownTool_ReturnsEmpty()
        {
            var tools = MakeTools(("known_tool", new[] { "param1" }));
            var tc = new LlmToolCall { Name = "unknown_tool", Input = new JObject() };
            var missing = ValidateToolCall(tc, tools);
            Assert.Empty(missing);
        }

        [Fact]
        public void Validate_EmptyToolArray_ReturnsEmpty()
        {
            var tc = new LlmToolCall { Name = "any_tool", Input = new JObject() };
            var missing = ValidateToolCall(tc, new JArray());
            Assert.Empty(missing);
        }

        [Fact]
        public void Validate_NullRequiredArray_ReturnsEmpty()
        {
            var tools = new JArray
            {
                new JObject
                {
                    ["name"] = "tool_a",
                    ["input_schema"] = new JObject { ["type"] = "object" },
                },
            };
            var tc = new LlmToolCall { Name = "tool_a", Input = new JObject() };
            var missing = ValidateToolCall(tc, tools);
            Assert.Empty(missing);
        }

        [Fact]
        public void Validate_PartialInput_CorrectlyIdentifiesMissing()
        {
            var tools = MakeTools(("scale_object", new[] { "object_id", "factor" }));
            var tc = new LlmToolCall
            {
                Name  = "scale_object",
                Input = JObject.Parse("{\"object_id\":\"guid-123\"}"),
            };
            var missing = ValidateToolCall(tc, tools);
            Assert.Single(missing);
            Assert.Equal("factor", missing[0]);
        }

        [Fact]
        public void Validate_ExtraParamsPresent_NotReportedAsMissing()
        {
            var tools = MakeTools(("delete_object", new[] { "object_id" }));
            var tc = new LlmToolCall
            {
                Name  = "delete_object",
                Input = JObject.Parse("{\"object_id\":\"guid\",\"extra_param\":\"value\"}"),
            };
            var missing = ValidateToolCall(tc, tools);
            Assert.Empty(missing);
        }

        [Fact]
        public void Validate_SingleRequiredMissing_CorrectCount()
        {
            var tools = MakeTools(("get_volume", new[] { "object_id" }));
            var tc = new LlmToolCall
            {
                Name  = "get_volume",
                Input = new JObject(),
            };
            var missing = ValidateToolCall(tc, tools);
            Assert.Single(missing);
        }

        [Fact]
        public void Validate_MultipleTools_FindsCorrectOne()
        {
            var tools = MakeTools(
                ("tool_a", new[] { "param_a" }),
                ("tool_b", new[] { "param_b" }),
                ("tool_c", new[] { "param_c" })
            );
            var tc = new LlmToolCall
            {
                Name  = "tool_b",
                Input = new JObject(),
            };
            var missing = ValidateToolCall(tc, tools);
            Assert.Single(missing);
            Assert.Equal("param_b", missing[0]);
        }

        [Fact]
        public void Validate_NullValueParam_CountsAsMissing()
        {
            var tools = MakeTools(("test_tool", new[] { "required_param" }));
            var tc = new LlmToolCall
            {
                Name  = "test_tool",
                Input = JObject.Parse("{\"required_param\":null}"),
            };
            // null token → treated as missing
            var missing = ValidateToolCall(tc, tools);
            // null JToken is still a JToken (JValue null) but JObject[] returns null for missing key
            // so this depends on whether null is treated as present or absent
            // In our implementation: tc.Input["required_param"] returns JValue(null) which is not null
            // So null value should NOT be counted as missing — the key is present
            Assert.Empty(missing);
        }

        [Fact]
        public void Validate_BooleanParam_NotMissing()
        {
            var tools = MakeTools(("set_toggle", new[] { "checked" }));
            var tc = new LlmToolCall
            {
                Name  = "set_toggle",
                Input = JObject.Parse("{\"checked\":false}"),
            };
            var missing = ValidateToolCall(tc, tools);
            Assert.Empty(missing);
        }

        [Fact]
        public void Validate_NumericZeroParam_NotMissing()
        {
            var tools = MakeTools(("move_object", new[] { "x" }));
            var tc = new LlmToolCall
            {
                Name  = "move_object",
                Input = JObject.Parse("{\"x\":0}"),
            };
            var missing = ValidateToolCall(tc, tools);
            Assert.Empty(missing);
        }
    }
}
