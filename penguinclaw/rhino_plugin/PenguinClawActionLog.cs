using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PenguinClaw
{
    /// <summary>
    /// Persistent action log — survives Rhino restarts.
    /// Every tool call that creates, modifies, or deletes objects is recorded to disk.
    /// The last N entries are injected into the system prompt so Claude has full session history.
    /// </summary>
    internal static class PenguinClawActionLog
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PenguinClaw", "action_log.json");

        private static readonly object _lock = new object();
        private const int MaxDiskEntries    = 300;
        private const int MaxPromptEntries  = 25;

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>Call after every tool execution to record what happened.</summary>
        public static void Record(string toolName, JObject input, string resultJson)
        {
            // Skip read-only tools — they don't change scene state
            if (IsReadOnly(toolName)) return;

            var entry = BuildEntry(toolName, input, resultJson);
            if (entry == null) return;

            lock (_lock)
            {
                var log = LoadRaw();
                log.Add(entry);
                while (log.Count > MaxDiskEntries)
                    log.RemoveAt(0);
                Save(log);
            }
        }

        /// <summary>Returns a formatted string for injection into the system prompt.</summary>
        public static string GetContextBlock()
        {
            List<JObject> log;
            lock (_lock) { log = LoadRaw(); }
            if (log.Count == 0) return null;

            var recent = log.Skip(Math.Max(0, log.Count - MaxPromptEntries)).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("## Action history (persistent — survives restarts)");
            foreach (var e in recent)
            {
                var ts   = e["ts"]?.ToString()  ?? "";
                var desc = e["desc"]?.ToString() ?? "";
                var ids  = e["ids"] as JArray;
                var line = $"- [{ts}] {desc}";
                if (ids != null && ids.Count > 0)
                    line += $"  →  ID: {string.Join(", ", ids.Select(x => x.ToString()))}";
                sb.AppendLine(line);
            }
            sb.AppendLine("(Use _Undo to reverse recent operations if the user asks to restore something.)");
            return sb.ToString().TrimEnd();
        }

        // ── Entry construction ───────────────────────────────────────────────────

        private static JObject BuildEntry(string toolName, JObject input, string resultJson)
        {
            JObject result = null;
            try { result = JObject.Parse(resultJson); } catch { }

            var ids = ExtractIds(result);
            var desc = BuildDescription(toolName, input, result, ids);
            if (string.IsNullOrEmpty(desc)) return null;

            return new JObject
            {
                ["ts"]   = DateTime.Now.ToString("MM-dd HH:mm"),
                ["tool"] = toolName,
                ["desc"] = desc,
                ["ids"]  = ids != null && ids.Count > 0 ? new JArray(ids) : new JArray(),
            };
        }

        private static string BuildDescription(string toolName, JObject input, JObject result, List<string> ids)
        {
            var success = result?["success"]?.ToObject<bool>() ?? true;
            var suffix  = success ? "" : " [failed]";
            var idCount = ids?.Count ?? 0;
            var objWord = idCount == 1 ? "object" : "objects";

            // Dynamic Rhino commands
            if (toolName.StartsWith("rhino_cmd_"))
            {
                var cmdName = toolName.Substring("rhino_cmd_".Length);
                var args    = input?["args"]?.ToString();
                var argStr  = string.IsNullOrWhiteSpace(args) ? "" : $" ({args})";

                if (IsCreateCmd(cmdName))
                    return $"Created {cmdName}{argStr} → {idCount} {objWord}{suffix}";
                if (IsDeleteCmd(cmdName))
                    return $"Deleted{argStr}{suffix}";
                if (IsTransformCmd(cmdName))
                    return $"{cmdName}{argStr} on {idCount} {objWord}{suffix}";
                return $"{cmdName}{argStr}{suffix}";
            }

            // run_rhino_command fallback
            if (toolName == "run_rhino_command")
            {
                var cmd = input?["command"]?.ToString() ?? "";
                return $"Ran command: {cmd}{suffix}";
            }

            // select_objects_by_id — skip, not an action
            if (toolName == "select_objects_by_id") return null;

            // delete_object
            if (toolName == "delete_object")
            {
                var id = input?["object_id"]?.ToString() ?? "";
                return $"Deleted object {id}{suffix}";
            }

            // rename_object
            if (toolName == "rename_object")
            {
                var id   = input?["object_id"]?.ToString() ?? "";
                var name = input?["name"]?.ToString() ?? "";
                return $"Renamed object {id} to '{name}'{suffix}";
            }

            // create_layer / set_current_layer
            if (toolName == "create_layer")
            {
                var name = input?["name"]?.ToString() ?? "";
                return $"Created layer '{name}'{suffix}";
            }
            if (toolName == "set_current_layer") return null; // not a scene change worth logging

            // undo / redo
            if (toolName == "undo")
            {
                var steps = input?["steps"]?.ToObject<int>() ?? 1;
                return $"Undo x{steps}{suffix}";
            }
            if (toolName == "redo")
            {
                var steps = input?["steps"]?.ToObject<int>() ?? 1;
                return $"Redo x{steps}{suffix}";
            }

            // execute_python_code
            if (toolName == "execute_python_code")
            {
                var snippet = (input?["code"]?.ToString() ?? "").Split('\n')[0].Trim();
                if (snippet.Length > 60) snippet = snippet.Substring(0, 60) + "…";
                return $"Python: {snippet}{suffix}";
            }

            // scale_object
            if (toolName == "scale_object")
            {
                var id     = input?["object_id"]?.ToString() ?? "";
                var factor = input?["factor"]?.ToString() ?? "";
                return $"Scaled object {id} by {factor}x{suffix}";
            }

            // move_object (C# direct)
            if (toolName == "move_object")
            {
                var id = input?["object_id"]?.ToString() ?? "";
                var x  = input?["x"]?.ToObject<double>() ?? 0;
                var y  = input?["y"]?.ToObject<double>() ?? 0;
                var z  = input?["z"]?.ToObject<double>() ?? 0;
                return $"Moved object {id} by ({x},{y},{z}){suffix}";
            }

            // rotate / mirror / array
            if (toolName == "rotate_object")
            {
                var id  = input?["object_id"]?.ToString() ?? "";
                var ang = input?["angle_degrees"]?.ToString() ?? "";
                var ax  = input?["axis"]?.ToString() ?? "z";
                return $"Rotated object {id} by {ang}° around {ax}{suffix}";
            }
            if (toolName == "mirror_object")
            {
                var id = input?["object_id"]?.ToString() ?? "";
                var pl = input?["mirror_plane"]?.ToString() ?? "xy";
                return $"Mirrored object {id} across {pl}{suffix}";
            }
            if (toolName == "array_linear")
            {
                var id  = input?["object_id"]?.ToString() ?? "";
                var cnt = input?["count"]?.ToString() ?? "";
                return $"Linear array of {id} ×{cnt}{suffix}";
            }
            if (toolName == "array_polar")
            {
                var id  = input?["object_id"]?.ToString() ?? "";
                var cnt = input?["count"]?.ToString() ?? "";
                return $"Polar array of {id} ×{cnt}{suffix}";
            }

            // booleans / join
            if (toolName == "boolean_union")        return $"Boolean union → {idCount} result(s){suffix}";
            if (toolName == "boolean_difference")   return $"Boolean difference → {idCount} result(s){suffix}";
            if (toolName == "boolean_intersection") return $"Boolean intersection → {idCount} result(s){suffix}";
            if (toolName == "join_curves")          return $"Joined curves → {idCount} result(s){suffix}";

            // layer / colour
            if (toolName == "set_object_layer")
            {
                var id    = input?["object_id"]?.ToString() ?? "";
                var layer = input?["layer_name"]?.ToString() ?? "";
                return $"Moved {id} to layer '{layer}'{suffix}";
            }
            if (toolName == "set_object_color")
            {
                var id = input?["object_id"]?.ToString() ?? "";
                var r  = input?["r"]?.ToString() ?? "0";
                var g  = input?["g"]?.ToString() ?? "0";
                var b  = input?["b"]?.ToString() ?? "0";
                return $"Set colour of {id} to rgb({r},{g},{b}){suffix}";
            }

            // GH tools
            if (toolName == "set_gh_slider")
            {
                var name  = input?["name"]?.ToString() ?? "";
                var value = input?["value"]?.ToString() ?? "";
                return $"Set GH slider '{name}' to {value}{suffix}";
            }
            if (toolName.StartsWith("gh_comp_"))
            {
                var compName = toolName.Substring("gh_comp_".Length);
                return $"Added GH component {compName}{suffix}";
            }

            // build_gh_definition
            if (toolName == "build_gh_definition")
            {
                var compCount = (input?["components"] as JArray)?.Count ?? 0;
                var wireCount = (input?["wires"]      as JArray)?.Count ?? 0;
                return $"Built GH definition ({compCount} components, {wireCount} wires){suffix}";
            }

            return null; // unknown — don't log
        }

        private static List<string> ExtractIds(JObject result)
        {
            var ids = new List<string>();
            if (result == null) return ids;

            var arr = result["selected_objects"] as JArray;
            if (arr != null)
                foreach (var item in arr)
                {
                    var id = item["id"]?.ToString();
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                }
            return ids;
        }

        // ── Disk I/O ─────────────────────────────────────────────────────────────

        private static List<JObject> LoadRaw()
        {
            try
            {
                if (!File.Exists(LogPath)) return new List<JObject>();
                var json = File.ReadAllText(LogPath, Encoding.UTF8);
                var arr  = JArray.Parse(json);
                return arr.OfType<JObject>().ToList();
            }
            catch { return new List<JObject>(); }
        }

        private static void Save(List<JObject> log)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.WriteAllText(LogPath,
                    new JArray(log).ToString(Formatting.Indented),
                    Encoding.UTF8);
            }
            catch { }
        }

        // ── Retry / recovery log entries ─────────────────────────────────────────

        public static void RecordRetry(string provider, int statusCode, int attempt)
        {
            lock (_lock)
            {
                var log = LoadRaw();
                log.Add(new JObject
                {
                    ["ts"]   = DateTime.Now.ToString("MM-dd HH:mm"),
                    ["tool"] = "api_retry",
                    ["desc"] = $"Retry {attempt}/3 from {provider} (HTTP {statusCode}) — waiting {(int)Math.Pow(2, attempt - 1)}s",
                    ["ids"]  = new JArray(),
                });
                while (log.Count > MaxDiskEntries) log.RemoveAt(0);
                Save(log);
            }
        }

        public static void RecordRetryFailure(string provider, int statusCode, int attempts)
        {
            lock (_lock)
            {
                var log = LoadRaw();
                log.Add(new JObject
                {
                    ["ts"]   = DateTime.Now.ToString("MM-dd HH:mm"),
                    ["tool"] = "api_retry_failed",
                    ["desc"] = $"All {attempts} retries failed for {provider} (HTTP {statusCode})",
                    ["ids"]  = new JArray(),
                });
                while (log.Count > MaxDiskEntries) log.RemoveAt(0);
                Save(log);
            }
        }

        public static void RecordMalformedCall(string provider)
        {
            lock (_lock)
            {
                var log = LoadRaw();
                log.Add(new JObject
                {
                    ["ts"]   = DateTime.Now.ToString("MM-dd HH:mm"),
                    ["tool"] = "malformed_tool_call",
                    ["desc"] = $"Malformed tool call from {provider} — injected recovery message",
                    ["ids"]  = new JArray(),
                });
                while (log.Count > MaxDiskEntries) log.RemoveAt(0);
                Save(log);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool IsReadOnly(string tool) =>
            tool == "get_selected_objects" ||
            tool == "get_object_info"      ||
            tool == "get_volume"           ||
            tool == "get_document_summary" ||
            tool == "list_layers"          ||
            tool == "list_gh_sliders"      ||
            tool == "list_gh_components"   ||
            tool == "capture_viewport"     ||
            tool == "select_objects_by_id";

        private static bool IsCreateCmd(string cmd)
        {
            var creates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Box","Sphere","Cylinder","Cone","Torus","Ellipsoid","Plane","PlanarSrf",
                "ExtrudeCrv","ExtrudeSrf","Extrude","Loft","Sweep1","Sweep2","Revolve",
                "NetworkSrf","Patch","SubDBox","SubDSphere","Circle","Line","Arc","Curve",
                "InterpCrv","Polyline","Rectangle","Polygon","Spiral","Helix","Points",
                "BooleanUnion","BooleanDifference","BooleanIntersection","Cap","Join",
                "Offset","OffsetCrv","OffsetSrf","DupEdge","DupBorder","Contour","Section",
                "Text","Hatch","Block","Insert",
            };
            return creates.Contains(cmd);
        }

        private static bool IsDeleteCmd(string cmd) =>
            cmd.Equals("Delete", StringComparison.OrdinalIgnoreCase) ||
            cmd.Equals("Trim",   StringComparison.OrdinalIgnoreCase);

        private static bool IsTransformCmd(string cmd)
        {
            var transforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Move","Copy","Rotate","Rotate3D","Scale","Scale1D","Scale2D",
                "Mirror","Array","ArrayLinear","ArrayPolar","ArrayCrv",
                "Twist","Bend","Taper","Flow","FilletEdge","ChamferEdge","Shell",
                "Rebuild","Smooth","Weld","ReduceMesh","QuadRemesh",
            };
            return transforms.Contains(cmd);
        }
    }
}
