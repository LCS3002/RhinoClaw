using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;

namespace PenguinClaw
{
    internal class ToolCallRecord
    {
        public string Name   { get; set; }
        public string Args   { get; set; }
        public string Result { get; set; }
    }

    internal class AgentResult
    {
        public string Response              { get; set; }
        public List<ToolCallRecord> ToolCalls { get; set; }
        public int InputTokens  { get; set; }
        public int OutputTokens { get; set; }
        public int CachedTokens { get; set; }
    }

    internal static class PenguinClawAgent
    {
        private const int MaxTokens  = 8192;
        private const int MaxHistory = 20;   // reduced: vision base64 bloats history fast
        private const int MaxIter    = 25;

        private const string BaseSystemPrompt =
            "You are PenguinClaw — an AI agent embedded inside Rhino 8. " +
            "You control 3D modelling, Grasshopper, and rendering through tools. " +
            "Always respond in the user's language. Be concise. Chain multiple tool calls freely to complete tasks.\n\n" +

            "## Object identity — critical\n" +
            "- After creating or modifying ANY object, always include its ID in your reply, e.g. 'Created box (ID: abc-123...)'.\n" +
            "- When the user says 'it', 'that', 'the box', or references a prior object: look in scene state below and use that ID.\n" +
            "- NEVER create new geometry as a substitute for operating on an existing object.\n\n" +

            "## Operations on existing objects — use dedicated tools (undo-safe, faster)\n" +
            "- Move:         move_object(object_id, x, y, z)\n" +
            "- Scale:        scale_object(object_id, factor)\n" +
            "- Rotate:       rotate_object(object_id, angle_degrees, axis) — axis is 'x', 'y', or 'z' (default 'z')\n" +
            "- Delete:       delete_object(object_id)\n" +
            "- Mirror:       mirror_object(object_id, mirror_plane) — plane is 'xy', 'xz', or 'yz'\n" +
            "- Linear array: array_linear(object_id, dx, dy, dz, count) — creates count copies each offset by (dx,dy,dz)\n" +
            "- Polar array:  array_polar(object_id, count, total_angle, center_x, center_y, center_z)\n" +
            "- Move to layer:set_object_layer(object_id, layer_name)\n" +
            "- Set color:    set_object_color(object_id, r, g, b) — values 0–255\n" +
            "- Rename:       rename_object(object_id, name)\n\n" +

            "## Creating geometry — ALWAYS use execute_python_code with rhinoscriptsyntax\n" +
            "- For ALL geometry creation (shapes, objects, colours), ALWAYS use execute_python_code with rhinoscriptsyntax.\n" +
            "- rhinoscriptsyntax returns object IDs directly, lets you set colour immediately, and chains naturally.\n" +
            "- Sphere:   id = rs.AddSphere((0,0,0), 5)\n" +
            "- Cylinder: id = rs.AddCylinder((0,0,0), 10, 3)    # base_center, height, radius\n" +
            "- Cone:     id = rs.AddCone((0,0,0), 10, 5)         # base_center, height, radius\n" +
            "- Torus:    id = rs.AddTorus((0,0,0), 10, 2)\n" +
            "- Line:     id = rs.AddLine((0,0,0), (10,0,0))\n" +
            "- Circle:   id = rs.AddCircle((0,0,0), 5)\n" +
            "- Box (axis-aligned, from min to max corner):\n" +
            "  p0=(0,0,0); p1=(10,10,10)\n" +
            "  corners=[(p0[0],p0[1],p0[2]),(p1[0],p0[1],p0[2]),(p1[0],p1[1],p0[2]),(p0[0],p1[1],p0[2]),\n" +
            "           (p0[0],p0[1],p1[2]),(p1[0],p0[1],p1[2]),(p1[0],p1[1],p1[2]),(p0[0],p1[1],p1[2])]\n" +
            "  id = rs.AddBox(corners)\n" +
            "- Set colour right after creation: rs.ObjectColor(id, (255, 140, 0))\n" +
            "- Always print() the ID so it appears in output: print('id: ' + str(id))\n" +
            "- Only use run_rhino_command for surface ops that rhinoscriptsyntax cannot handle:\n" +
            "  Loft, Sweep1, FilletEdge, Revolve, ExtrudeCrv, Cap, Shell, Rebuild\n\n" +

            "## Boolean operations — use dedicated tools (handle selection automatically)\n" +
            "- boolean_union(object_ids[])                        — merge into one solid\n" +
            "- boolean_difference(target_ids[], cutter_ids[])     — subtract cutters from targets\n" +
            "- boolean_intersection(target_ids[], cutter_ids[])   — keep overlapping volume\n" +
            "- join_curves(object_ids[])                          — join open curves into one\n\n" +

            "## Layers and organisation\n" +
            "- list_layers() / create_layer(name) / set_current_layer(name) / set_object_layer(object_id, layer_name)\n\n" +

            "## Grasshopper\n" +
            "- list_gh_sliders() / set_gh_slider(name, value) / list_gh_components()\n" +
            "- build_gh_definition(components[], wires[], solve, clear_canvas) — build a GH definition from scratch.\n" +
            "  WIRE FORMAT (critical): wires is an array of objects: [{\"from\":\"id:outputIndex\",\"to\":\"id:inputIndex\"}] — 0-based indices.\n" +
            "  COMPONENT TYPES:\n" +
            "    'slider' — number slider. Fields: value, min, max.\n" +
            "    'panel'  — text panel. Field: text.\n" +
            "    'python3'— Python 3 script component. Fields: code (string), inputs (string[]), outputs (string[]). USE THIS for all geometry creation in GH.\n" +
            "    'component' — built-in GH component by component_name (fuzzy matched). Reliable names: 'Addition', 'Multiplication', 'Area', 'Volume', 'Loft', 'Extrude', 'Offset Curve'. Geometry primitives (Sphere, Box, Cylinder) are UNRELIABLE — use python3 instead.\n" +
            "    'sdk' — component by GUID. Field: guid.\n" +
            "  EXAMPLE — parametric sphere with python3:\n" +
            "    components: [{\"id\":\"r\",\"type\":\"slider\",\"name\":\"Radius\",\"value\":5,\"min\":1,\"max\":20}, {\"id\":\"py\",\"type\":\"python3\",\"name\":\"Sphere\",\"inputs\":[\"radius\"],\"outputs\":[\"geo\"],\"code\":\"import rhinoscriptsyntax as rs\\ngeo = rs.AddSphere((0,0,0), radius)\"}]\n" +
            "    wires: [{\"from\":\"r:0\",\"to\":\"py:0\"}]\n" +
            "- search_gh_components(keyword) — search GH server by name (e.g. 'python', 'sphere'). Returns exact names + GUIDs. Call this FIRST if unsure of component_name.\n" +
            "- solve_gh_definition() — trigger recompute on active canvas\n" +
            "- bake_gh_definition(layer_name) — bake all geometry to a named Rhino layer\n\n" +

            "## Spatial placement — always measure, never guess\n" +
            "- Before placing any object relative to another, call get_scene_layout() to see all objects with bounding boxes.\n" +
            "- bounding_box format: [xmin, ymin, zmin, xmax, ymax, zmax]\n" +
            "- To sit object B on top of object A:  place_object(B, anchor='base', target_z=A_bbox[5])\n" +
            "- To place B next to A (right side):   place_object(B, anchor='min', target_x=A_bbox[3]+gap)\n" +
            "- To center B above A:                 place_object(B, anchor='base', target_x=A_center_x, target_y=A_center_y, target_z=A_bbox[5])\n" +
            "- Never move by a guessed delta when you need precise placement — use place_object instead.\n\n" +

            "## Building tables and surfaces\n" +
            "- Build a table as: 4 legs (box, Z=0 to Z=table_h) + tabletop (box, Z=table_h to Z=table_h+0.2).\n" +
            "- Never create a table as a single flat slab below or overlapping existing geometry.\n" +
            "- After building the table, call get_scene_layout() to confirm the tabletop Z, then use place_object to sit objects on it.\n\n" +

            "## Complex figurative models (characters, creatures, objects)\n" +
            "- Plan all coordinates before starting: sketch parts, heights, and offsets mentally first.\n" +
            "- Build bottom-up: feet → body → head → details (eyes, beak, fins, etc.).\n" +
            "- After every 4–5 tool calls on a complex model, call capture_and_assess to verify shape and proportions.\n" +
            "- Use specific colours per part as you go — do not leave all colouring to the end.\n" +
            "- Use execute_python_code with rhinoscriptsyntax for all primitives (AddSphere, AddCylinder, AddCone, AddBox).\n\n" +

            "## Visual verification\n" +
            "- After completing significant modeling steps (boolean operations, complex geometry creation), call capture_and_assess to visually verify the result.\n" +
            "- Use capture_and_assess when the user asks 'how does it look', 'check the result', or 'is that right?'\n\n" +

            "## run_rhino_command — use ONLY for views, selections, and advanced surface ops\n" +
            "- View/display: _Zoom, _Pan, _SetView, _Plan, _Perspective, _Show, _Hide, _Lock\n" +
            "- Selection: _SelAll, _SelNone, _Invert, _SelLayer, _SelName\n" +
            "- Advanced surface ops with no rhinoscriptsyntax equivalent: Loft, Sweep1, FilletEdge, Revolve, ExtrudeCrv, Cap, Shell, Fillet, Rebuild\n" +
            "- Do NOT use run_rhino_command for creating geometry — use execute_python_code instead.\n" +
            "- Do NOT use run_rhino_command for: Move, Scale, Rotate, Delete, Mirror, Array, Boolean,\n" +
            "  colour, or layer assignment — all of those have dedicated tools or can be done via Python.\n\n" +
            "## execute_python_code — PRIMARY tool for geometry creation and complex operations\n" +
            "- Geometry creation: ALWAYS use rhinoscriptsyntax (rs.AddBox, rs.AddSphere, rs.AddCylinder, rs.AddCone, etc.)\n" +
            "- Colour: rs.ObjectColor(id, (r, g, b)) — set immediately after creation\n" +
            "- Bulk operations (create 20+ objects, iterate over selection, parametric geometry)\n" +
            "- Anything requiring loops, conditionals, or math\n" +
            "- Loft/Sweep/Extrude/Revolve on specific curve IDs\n" +
            "- Anything that would take more than 4 individual tool calls\n" +
            "IMPORTANT: the engine is IronPython 2 (not Python 3). Syntax rules:\n" +
            "- NO f-strings — use 'Hello {}'.format(x) instead of f'Hello {x}'\n" +
            "- NO walrus operator :=\n" +
            "- print is a function: print('x') is fine, print 'x' is not\n" +
            "- 'import rhinoscriptsyntax as rs' for high-level ops; 'import Rhino' for RhinoCommon\n" +
            "- Variable 'doc' is pre-set to the active RhinoDoc\n" +
            "- Always print() results so they appear in output\n" +
            "- If Python fails, read the error message carefully — it will tell you the line and syntax issue\n\n" +

            "## Scene query tools\n" +
            "- get_scene_layout()          — all objects + bounding boxes in one call (use before spatial placement)\n" +
            "- get_object_info(id)         — single object detail including bounding_box\n" +
            "- get_document_summary()      — object counts, layers, scene bbox overview\n\n" +

            "## Context and memory\n" +
            "- Scene state and action history below are your persistent memory.\n" +
            "- To undo: call undo(steps=1).\n" +
            "- Only call get_selected_objects when the user explicitly asks about their selection.";

        // Populated by PenguinClawScan via /rebuild-registry
        public static string ScanContext { get; set; } = null;

        // Scene state: tracks objects created/modified this session
        private static readonly List<SceneObject> _sceneObjects = new List<SceneObject>();
        private static readonly object _sceneLock = new object();

        private class SceneObject
        {
            public string Id;
            public string Type;
            public string CreatedBy;
        }

        /// <summary>Called after every tool execution to keep scene state current.</summary>
        public static void RecordToolResult(string toolName, string resultJson)
        {
            try
            {
                var obj      = JObject.Parse(resultJson);
                var selected = obj["selected_objects"] as JArray;
                if (selected == null || selected.Count == 0) return;

                lock (_sceneLock)
                {
                    foreach (var item in selected)
                    {
                        var id = item["id"]?.ToString();
                        if (string.IsNullOrEmpty(id)) continue;
                        var existing = _sceneObjects.FirstOrDefault(s => s.Id == id);
                        if (existing != null)
                            existing.CreatedBy = toolName;
                        else
                            _sceneObjects.Add(new SceneObject
                            {
                                Id        = id,
                                Type      = item["type"]?.ToString() ?? "object",
                                CreatedBy = toolName,
                            });
                    }
                    while (_sceneObjects.Count > 20)
                        _sceneObjects.RemoveAt(0);
                }
            }
            catch { }
        }

        /// <summary>
        /// Builds the system prompt as a JArray of blocks so the static base can be
        /// prompt-cached by Anthropic. OpenAI-compatible providers flatten this to a
        /// single system message automatically.
        /// </summary>
        private static JArray BuildSystemPromptArray()
        {
            var arr = new JArray();

            // Block 1 — static base prompt (cached by Anthropic)
            arr.Add(new JObject
            {
                ["type"]          = "text",
                ["text"]          = BaseSystemPrompt,
                ["cache_control"] = new JObject { ["type"] = "ephemeral" },
            });

            // Block 2 — scan context (large, rarely changes, cached separately)
            if (!string.IsNullOrEmpty(ScanContext))
            {
                arr.Add(new JObject
                {
                    ["type"]          = "text",
                    ["text"]          = ScanContext,
                    ["cache_control"] = new JObject { ["type"] = "ephemeral" },
                });
            }

            // Block 3 — dynamic per-turn context: action log + scene state
            var sb = new StringBuilder();

            var logBlock = PenguinClawActionLog.GetContextBlock();
            if (!string.IsNullOrEmpty(logBlock))
                sb.Append(logBlock);

            lock (_sceneLock)
            {
                if (_sceneObjects.Count > 0)
                {
                    if (sb.Length > 0) sb.Append("\n\n");
                    sb.Append("## Scene state (this session)\n");
                    foreach (var o in _sceneObjects)
                        sb.AppendLine($"- ID: {o.Id}  Type: {o.Type}  (via {o.CreatedBy})");
                }
            }

            if (sb.Length > 0)
                arr.Add(new JObject { ["type"] = "text", ["text"] = sb.ToString() });

            return arr;
        }

        // ── Main entry point ─────────────────────────────────────────────────

        // Replaceable in tests for tool dispatch override
        public static Func<string, JObject, string> OverrideDispatcher = null;

        public static AgentResult Run(string userMessage, JArray history, RhinoDoc doc,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var cfg = LoadConfig();
            if (!IsAiConfigured(cfg))
                return Error("No AI provider configured. Open Settings to choose a provider and add your API key.");

            var provider = LlmProviderFactory.Create(cfg);

            // Base tools (stable, prompt-cached for Anthropic), dynamic tools appended
            var baseTools    = PenguinClawTools.GetToolDefinitions();
            var dynamicTools = RhinoCommandRegistry.GetRelevantTools(userMessage, 5);
            var tools        = new JArray();
            foreach (var t in baseTools)    tools.Add(t);
            foreach (var t in dynamicTools) tools.Add(t);

            // Cache breakpoint on last base tool (Anthropic only; stripped by OpenAI providers)
            if (baseTools.Count > 0)
            {
                var lastBase = (JObject)tools[baseTools.Count - 1];
                lastBase["cache_control"] = new JObject { ["type"] = "ephemeral" };
            }

            var messages = new JArray();
            foreach (var msg in history)
                messages.Add(msg.DeepClone());
            messages.Add(new JObject { ["role"] = "user", ["content"] = userMessage });
            messages = TrimHistory(messages);

            var toolCallsMade = new List<ToolCallRecord>();
            int malformedRecoveryCount = 0;

            for (int iter = 0; iter < MaxIter; iter++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return new AgentResult { Response = "Operation cancelled.", ToolCalls = toolCallsMade };

                var llmResp = provider.Send(BuildSystemPromptArray(), messages, tools, MaxTokens);

                if (llmResp.StopReason == "error")
                    return new AgentResult { Response = llmResp.ErrorMessage, ToolCalls = toolCallsMade };

                if (llmResp.StopReason == "end_turn")
                    return new AgentResult
                    {
                        Response  = string.IsNullOrEmpty(llmResp.Text) ? "(No response)" : llmResp.Text,
                        ToolCalls = toolCallsMade,
                    };

                if (llmResp.StopReason == "tool_use")
                {
                    // Handle malformed tool_use (model declared tool_use but gave no calls)
                    if (llmResp.ToolCalls.Count == 0)
                    {
                        if (malformedRecoveryCount >= 2)
                            return new AgentResult
                            {
                                Response  = llmResp.Text ?? "Model indicated tool use but provided no tool calls.",
                                ToolCalls = toolCallsMade,
                            };

                        malformedRecoveryCount++;
                        var toolNames = string.Join(", ", tools.OfType<JObject>()
                            .Select(t => t["name"]?.ToString())
                            .Where(n => n != null).Take(10));
                        var corrective = $"You indicated tool_use but did not provide any tool calls. Available tools include: {toolNames}. Please call one of the available tools or respond with end_turn.";
                        messages.Add(new JObject
                        {
                            ["role"]    = "assistant",
                            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = llmResp.Text ?? "" } },
                        });
                        messages.Add(new JObject { ["role"] = "user", ["content"] = corrective });
                        PenguinClawActionLog.RecordMalformedCall(cfg.Provider ?? "unknown");
                        continue;
                    }

                    // Reconstruct Anthropic-format assistant message (internal canonical format)
                    var assistantContent = new JArray();
                    if (!string.IsNullOrEmpty(llmResp.Text))
                        assistantContent.Add(new JObject { ["type"] = "text", ["text"] = llmResp.Text });
                    foreach (var tc in llmResp.ToolCalls)
                        assistantContent.Add(new JObject
                        {
                            ["type"]  = "tool_use",
                            ["id"]    = tc.Id,
                            ["name"]  = tc.Name,
                            ["input"] = tc.Input,
                        });
                    messages.Add(new JObject { ["role"] = "assistant", ["content"] = assistantContent });

                    // Execute tools, collect Anthropic-format tool_result blocks
                    var toolResults = new JArray();
                    var visionInjections = new List<JObject>();
                    string lastCaptureResult = null;

                    foreach (var tc in llmResp.ToolCalls)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return new AgentResult { Response = "Operation cancelled.", ToolCalls = toolCallsMade };

                        // Schema validation
                        var missing = ValidateToolCall(tc, tools);
                        if (missing.Length > 0)
                        {
                            var errMsg = $"Tool call '{tc.Name}' is missing required parameters: {string.Join(", ", missing)}. Please provide all required parameters.";
                            toolCallsMade.Add(new ToolCallRecord
                            {
                                Name   = tc.Name,
                                Args   = tc.Input.ToString(Formatting.None),
                                Result = errMsg,
                            });
                            toolResults.Add(new JObject
                            {
                                ["type"]        = "tool_result",
                                ["tool_use_id"] = tc.Id,
                                ["content"]     = new JObject { ["success"] = false, ["message"] = errMsg }.ToString(Formatting.None),
                            });
                            continue;
                        }

                        string resultStr;
                        try
                        {
                            resultStr = OverrideDispatcher != null
                                ? OverrideDispatcher(tc.Name, tc.Input)
                                : PenguinClawTools.Execute(tc.Name, tc.Input, doc);
                            RecordToolResult(tc.Name, resultStr);
                            PenguinClawActionLog.Record(tc.Name, tc.Input, resultStr);

                            // Check for vision injection signal
                            if (tc.Name == "capture_and_assess")
                            {
                                lastCaptureResult = resultStr;
                                try
                                {
                                    var vr = JObject.Parse(resultStr);
                                    if (vr["vision_ready"]?.ToObject<bool>() == true)
                                    {
                                        visionInjections.Add(new JObject
                                        {
                                            ["base64"]     = vr["base64"],
                                            ["media_type"] = vr["media_type"] ?? "image/png",
                                            ["prompt"]     = vr["prompt"] ?? "What do you see?",
                                        });
                                    }
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            resultStr = new JObject
                            {
                                ["success"]    = false,
                                ["error"]      = "exception",
                                ["tool"]       = tc.Name,
                                ["message"]    = ex.Message,
                                ["suggestion"] = "Check the tool parameters and try again, or use a different approach.",
                            }.ToString(Formatting.None);
                        }

                        toolCallsMade.Add(new ToolCallRecord
                        {
                            Name   = tc.Name,
                            Args   = tc.Input.ToString(Formatting.None),
                            Result = resultStr.Length > 300 ? resultStr.Substring(0, 300) + "..." : resultStr,
                        });

                        toolResults.Add(new JObject
                        {
                            ["type"]        = "tool_result",
                            ["tool_use_id"] = tc.Id,
                            ["content"]     = resultStr,
                        });
                    }

                    // Inject vision image blocks if any capture_and_assess returned vision data
                    if (visionInjections.Count > 0 && provider is AnthropicProvider)
                    {
                        var visionContent = new JArray();
                        foreach (var tr in toolResults)
                            visionContent.Add(tr);

                        foreach (var vi in visionInjections)
                        {
                            visionContent.Add(new JObject
                            {
                                ["type"] = "image",
                                ["source"] = new JObject
                                {
                                    ["type"]       = "base64",
                                    ["media_type"] = vi["media_type"],
                                    ["data"]       = vi["base64"],
                                },
                            });
                            visionContent.Add(new JObject
                            {
                                ["type"] = "text",
                                ["text"] = vi["prompt"]?.ToString() ?? "What do you see?",
                            });
                        }
                        messages.Add(new JObject { ["role"] = "user", ["content"] = visionContent });
                    }
                    else
                    {
                        messages.Add(new JObject { ["role"] = "user", ["content"] = toolResults });
                    }

                    messages = TrimHistory(messages);
                    continue;
                }

                // Unexpected stop reason
                return new AgentResult
                {
                    Response  = string.IsNullOrEmpty(llmResp.Text) ? $"Stopped: {llmResp.StopReason}" : llmResp.Text,
                    ToolCalls = toolCallsMade,
                };
            }

            return new AgentResult
            {
                Response  = "Stopped: reached maximum tool call iterations.",
                ToolCalls = toolCallsMade,
            };
        }

        private static string[] ValidateToolCall(LlmToolCall tc, JArray tools)
        {
            var toolDef = tools.OfType<JObject>().FirstOrDefault(t => t["name"]?.ToString() == tc.Name);
            if (toolDef == null) return new string[0]; // unknown tool, let Execute() handle it

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

        // ── History management ───────────────────────────────────────────────

        private static JArray TrimHistory(JArray messages)
        {
            // Strip base64 image data from all but the most recent vision turn
            // to prevent capture_and_assess history from blowing up the token count
            messages = StripOldVisionImages(messages);

            if (messages.Count <= MaxHistory) return messages;

            int start = messages.Count - MaxHistory;

            // Never start on a tool_result block — skip forward until a normal user message
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

        /// <summary>
        /// Replaces base64 image blocks in history with a lightweight placeholder,
        /// keeping only the most recent vision image. Prevents token overflow when
        /// capture_and_assess is called multiple times in a session.
        /// </summary>
        private static JArray StripOldVisionImages(JArray messages)
        {
            // Find index of the last user message that contains an image block
            int lastImageIdx = -1;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                var role    = messages[i]["role"]?.ToString();
                var content = messages[i]["content"] as JArray;
                if (role == "user" && content != null && content.Any(b => b["type"]?.ToString() == "image"))
                {
                    lastImageIdx = i;
                    break;
                }
            }

            if (lastImageIdx < 0) return messages; // no images in history

            var result = new JArray();
            for (int i = 0; i < messages.Count; i++)
            {
                var msg     = (JObject)messages[i];
                var role    = msg["role"]?.ToString();
                var content = msg["content"] as JArray;

                if (i == lastImageIdx || role != "user" || content == null
                    || !content.Any(b => b["type"]?.ToString() == "image"))
                {
                    result.Add(msg.DeepClone());
                    continue;
                }

                // Replace image blocks with a placeholder text block
                var stripped = new JArray();
                bool addedPlaceholder = false;
                foreach (var block in content)
                {
                    if (block["type"]?.ToString() == "image")
                    {
                        if (!addedPlaceholder)
                        {
                            stripped.Add(new JObject { ["type"] = "text", ["text"] = "[viewport image — stripped from history]" });
                            addedPlaceholder = true;
                        }
                    }
                    else
                    {
                        stripped.Add(block.DeepClone());
                    }
                }
                var clone = msg.DeepClone() as JObject;
                clone["content"] = stripped;
                result.Add(clone);
            }
            return result;
        }

        // ── Config management ────────────────────────────────────────────────

        public static bool IsAiConfigured() => IsAiConfigured(LoadConfig());

        private static bool IsAiConfigured(ProviderConfig cfg) =>
            (cfg.Provider ?? "").ToLowerInvariant() == "ollama"
            || !string.IsNullOrWhiteSpace(cfg.ApiKey);

        public static ProviderConfig LoadConfig()
        {
            try
            {
                var configPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PenguinClaw", "config.json");

                if (System.IO.File.Exists(configPath))
                {
                    var cfg = JObject.Parse(System.IO.File.ReadAllText(configPath));
                    return new ProviderConfig
                    {
                        Provider  = cfg["provider"]?.ToString()   ?? "anthropic",
                        ApiKey    = cfg["api_key"]?.ToString()    ?? "",
                        Model     = cfg["model"]?.ToString()      ?? "",
                        OllamaUrl = cfg["ollama_url"]?.ToString() ?? "http://localhost:11434",
                    };
                }
            }
            catch { }

            // Fall back to environment variable (backwards compatibility)
            var fromEnv = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (!string.IsNullOrWhiteSpace(fromEnv))
                return new ProviderConfig { Provider = "anthropic", ApiKey = fromEnv.Trim() };

            return new ProviderConfig();
        }

        public static void SaveConfig(ProviderConfig cfg)
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PenguinClaw");
            System.IO.Directory.CreateDirectory(dir);

            var json = new JObject
            {
                ["provider"]   = cfg.Provider   ?? "anthropic",
                ["api_key"]    = cfg.ApiKey     ?? "",
                ["model"]      = cfg.Model      ?? "",
                ["ollama_url"] = cfg.OllamaUrl  ?? "http://localhost:11434",
            };
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(dir, "config.json"),
                json.ToString(Formatting.Indented));
        }

        /// <summary>Backwards-compatible single-key save (keeps existing provider/model).</summary>
        public static void SaveApiKey(string key)
        {
            var cfg  = LoadConfig();
            cfg.Provider = "anthropic";
            cfg.ApiKey   = key.Trim();
            SaveConfig(cfg);
        }

        /// <summary>Returns the current API key — used by health endpoint.</summary>
        public static string ResolveApiKeyPublic() => LoadConfig().ApiKey;

        private static AgentResult Error(string msg) =>
            new AgentResult { Response = msg, ToolCalls = new List<ToolCallRecord>() };
    }
}
