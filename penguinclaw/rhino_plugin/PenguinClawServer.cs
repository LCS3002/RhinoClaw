using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;

namespace PenguinClaw
{
    public static class PenguinClawServer
    {
        private static HttpListener _listener;
        private static Thread       _thread;
        private static bool         _running;

        private const int Port = 8080;

        public static void StartServer()
        {
            if (_running) return;

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");

            try
            {
                _listener.Start();
                _running = true;
                _thread  = new Thread(Loop) { IsBackground = true, Name = "PenguinClawServer" };
                _thread.Start();
                RhinoApp.WriteLine($"PenguinClaw: server started on http://localhost:{Port}");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"PenguinClaw: failed to start server — {ex.Message}");
            }
        }

        public static void StopServer()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
        }

        // ── Server loop ──────────────────────────────────────────────────────────

        private static void Loop()
        {
            while (_running && (_listener?.IsListening ?? false))
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(Handle, ctx);
                }
                catch (Exception ex)
                {
                    if (_running) RhinoApp.WriteLine($"PenguinClaw server error: {ex.Message}");
                }
            }
        }

        private static void Handle(object state)
        {
            var ctx  = (HttpListenerContext)state;
            var req  = ctx.Request;
            var resp = ctx.Response;

            try
            {
                resp.Headers.Add("Access-Control-Allow-Origin",  "*");
                resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (req.HttpMethod == "OPTIONS")
                {
                    resp.StatusCode = 200;
                    resp.Close();
                    return;
                }

                var path = req.Url.AbsolutePath.TrimEnd('/');

                switch (path)
                {
                    case "/health":           RouteHealth(resp); break;
                    case "/tools":            RouteTools(resp);  break;
                    case "/chat":             RouteChat(req, resp); break;
                    case "/viewport":         RouteViewport(resp); break;
                    case "/rebuild-registry": RouteRebuildRegistry(resp); break;
                    case "/settings":         RouteSettings(req, resp); break;
                    default:                  ServeStatic(req, resp); break;
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"PenguinClaw request error: {ex.Message}");
                try { resp.StatusCode = 500; resp.Close(); } catch { }
            }
        }

        // ── Route handlers ───────────────────────────────────────────────────────

        private static void RouteHealth(HttpListenerResponse resp)
        {
            // RhinoDoc.ActiveDoc must be read on the UI thread
            string docName = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                docName = RhinoDoc.ActiveDoc?.Name;
                done.Set();
            }));
            done.Wait(2000);

            var cfg = PenguinClawAgent.LoadConfig();
            var body = new JObject
            {
                ["status"]               = "ok",
                ["rhino_connected"]      = true,
                ["document_open"]        = (docName != null),
                ["document"]             = docName ?? "(no document)",
                ["ai_configured"]        = PenguinClawAgent.IsAiConfigured(),
                ["provider"]             = cfg.Provider ?? "anthropic",
                ["agent_model"]          = string.IsNullOrWhiteSpace(cfg.Model)
                                               ? LlmProviderFactory.DefaultModel(cfg.Provider)
                                               : cfg.Model,
                ["tools_loaded"]         = PenguinClawTools.GetToolDefinitions().Count + RhinoCommandRegistry.CachedCount,
                ["tools_rhino_commands"] = RhinoCommandRegistry.RhinoCommandCount,
                ["tools_gh_components"]  = RhinoCommandRegistry.GhComponentCount,
                ["server_port"]          = 8080,
            };
            WriteJson(resp, body);
        }

        private static void RouteTools(HttpListenerResponse resp)
        {
            var brief = new JArray();

            // Static core tools
            foreach (var t in PenguinClawTools.GetToolDefinitions())
                brief.Add(new JObject
                {
                    ["name"]     = t["name"],
                    ["description"] = t["description"],
                    ["category"] = "core",
                });

            // Registry summary entries (one per type)
            var rhinoCount = RhinoCommandRegistry.RhinoCommandCount;
            var ghCount    = RhinoCommandRegistry.GhComponentCount;
            if (rhinoCount > 0)
                brief.Add(new JObject
                {
                    ["name"]        = $"rhino_cmd_* ({rhinoCount} commands)",
                    ["description"] = "All built-in Rhino commands — selected per request by keyword matching.",
                    ["category"]    = "rhino",
                });
            if (ghCount > 0)
                brief.Add(new JObject
                {
                    ["name"]        = $"gh_comp_* ({ghCount} components)",
                    ["description"] = "All installed Grasshopper components — selected per request by keyword matching.",
                    ["category"]    = "grasshopper",
                });

            WriteJson(resp, brief);
        }

        private static void RouteChat(HttpListenerRequest req, HttpListenerResponse resp)
        {
            if (req.HttpMethod != "POST") { resp.StatusCode = 405; resp.Close(); return; }

            string body;
            using (var sr = new StreamReader(req.InputStream, req.ContentEncoding))
                body = sr.ReadToEnd();

            JObject data;
            try   { data = JObject.Parse(body); }
            catch { WriteJson(resp, new JObject { ["response"] = "Invalid JSON.", ["tool_calls"] = new JArray() }, 400); return; }

            var message = data["message"]?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(message))
            {
                WriteJson(resp, new JObject { ["response"] = "No message provided.", ["tool_calls"] = new JArray() }, 400);
                return;
            }

            var history = (data["history"] as JArray) ?? new JArray();
            var doc     = RhinoDoc.ActiveDoc;

            AgentResult result;
            try   { result = PenguinClawAgent.Run(message, history, doc); }
            catch (Exception ex)
            {
                WriteJson(resp, new JObject
                {
                    ["response"]   = $"Agent error: {ex.Message}",
                    ["tool_calls"] = new JArray(),
                }, 500);
                return;
            }

            var toolCallsJson = new JArray();
            foreach (var tc in result.ToolCalls)
                toolCallsJson.Add(new JObject
                {
                    ["name"]   = tc.Name,
                    ["args"]   = tc.Args,
                    ["result"] = tc.Result,
                });

            WriteJson(resp, new JObject
            {
                ["response"]   = result.Response,
                ["tool_calls"] = toolCallsJson,
            });
        }

        private static void RouteViewport(HttpListenerResponse resp)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) { WriteJson(resp, new JObject { ["error"] = "No active Rhino document." }, 500); return; }

            // Use the tool — it dispatches to main thread internally
            var resultStr = PenguinClawTools.Execute("capture_viewport", new JObject(), doc);
            JObject result;
            try   { result = JObject.Parse(resultStr); }
            catch { WriteJson(resp, new JObject { ["error"] = "Failed to parse viewport result." }, 500); return; }

            if (result["success"]?.ToObject<bool>() != true)
            {
                WriteJson(resp, new JObject { ["error"] = result["message"]?.ToString() ?? "Unknown error." }, 500);
                return;
            }

            var path = result["path"]?.ToString();
            if (!File.Exists(path)) { WriteJson(resp, new JObject { ["error"] = "Viewport file not found." }, 500); return; }

            var bytes = File.ReadAllBytes(path);
            var b64   = Convert.ToBase64String(bytes);
            WriteJson(resp, new JObject { ["image"] = b64, ["path"] = path });
        }

        private static void RouteSettings(HttpListenerRequest req, HttpListenerResponse resp)
        {
            // GET — return current config (API key omitted for security, presence indicated by has_api_key)
            if (req.HttpMethod == "GET")
            {
                var cfg = PenguinClawAgent.LoadConfig();
                WriteJson(resp, new JObject
                {
                    ["provider"]    = cfg.Provider  ?? "anthropic",
                    ["model"]       = cfg.Model     ?? "",
                    ["ollama_url"]  = cfg.OllamaUrl ?? "http://localhost:11434",
                    ["has_api_key"] = !string.IsNullOrWhiteSpace(cfg.ApiKey),
                });
                return;
            }

            // POST — save new config
            if (req.HttpMethod == "POST")
            {
                string body;
                using (var sr = new StreamReader(req.InputStream, req.ContentEncoding))
                    body = sr.ReadToEnd();
                try
                {
                    var data     = JObject.Parse(body);
                    var provider = (data["provider"]?.ToString() ?? "anthropic").ToLowerInvariant();
                    var apiKey   = data["api_key"]?.ToString()?.Trim()    ?? "";
                    var model    = data["model"]?.ToString()?.Trim()      ?? "";
                    var ollamaUrl = data["ollama_url"]?.ToString()?.Trim() ?? "http://localhost:11434";

                    // For non-Ollama providers, require an API key (fall back to existing if not sent)
                    if (provider != "ollama" && string.IsNullOrWhiteSpace(apiKey))
                    {
                        var existing = PenguinClawAgent.LoadConfig();
                        if (!string.IsNullOrWhiteSpace(existing.ApiKey))
                            apiKey = existing.ApiKey;
                        else
                        {
                            WriteJson(resp, new JObject { ["success"] = false, ["message"] = "API key is required for this provider." }, 400);
                            return;
                        }
                    }

                    PenguinClawAgent.SaveConfig(new ProviderConfig
                    {
                        Provider  = provider,
                        ApiKey    = apiKey,
                        Model     = model,
                        OllamaUrl = ollamaUrl,
                    });
                    WriteJson(resp, new JObject { ["success"] = true, ["message"] = "Settings saved." });
                }
                catch (Exception ex)
                {
                    WriteJson(resp, new JObject { ["success"] = false, ["message"] = ex.Message }, 500);
                }
                return;
            }

            WriteJson(resp, new JObject { ["success"] = false, ["message"] = "GET or POST required." }, 405);
        }

        private static void RouteRebuildRegistry(HttpListenerResponse resp)
        {
            try
            {
                var scanPath = FindScanOutputJson();
                if (scanPath == null || !File.Exists(scanPath))
                {
                    WriteJson(resp, new JObject { ["success"] = false, ["message"] = "scan_output.json not found. Run PenguinClawScan first." });
                    return;
                }

                var json    = File.ReadAllText(scanPath);
                var scan    = JObject.Parse(json);
                var sb      = new StringBuilder();

                sb.AppendLine("## Scan data (from PenguinClawScan)");

                var ghInstalled = scan["gh_installed_components"] as JArray
                               ?? scan["grasshopper"]?["installed_components"] as JArray;
                if (ghInstalled != null && ghInstalled.Count > 0)
                {
                    sb.AppendLine($"\n### Installed Grasshopper components ({ghInstalled.Count} total)");
                    // Summarise by category — sending all would be too long
                    var cats = ghInstalled
                        .GroupBy(c => c["category"]?.ToString() ?? "Other")
                        .OrderBy(g => g.Key);
                    foreach (var cat in cats)
                        sb.AppendLine($"- {cat.Key}: {string.Join(", ", cat.Take(8).Select(c => c["name"]?.ToString()))}{ (cat.Count() > 8 ? $" … +{cat.Count()-8} more" : "") }");
                }

                var ghCanvas = scan["gh_active_canvas_components"] as JArray
                            ?? scan["grasshopper"]?["active_canvas_components"] as JArray;
                if (ghCanvas != null && ghCanvas.Count > 0)
                {
                    sb.AppendLine($"\n### Active canvas components ({ghCanvas.Count})");
                    foreach (var c in ghCanvas.Take(40))
                        sb.AppendLine($"- {c["name"]} ({c["type"]})");
                }

                PenguinClawAgent.ScanContext = sb.ToString();

                // Also refresh the per-command tool registry from the new scan data
                System.Threading.ThreadPool.QueueUserWorkItem(_ => RhinoCommandRegistry.Build());

                WriteJson(resp, new JObject
                {
                    ["success"]              = true,
                    ["message"]              = "Scan context loaded into agent.",
                    ["gh_installed"]         = ghInstalled?.Count ?? 0,
                    ["gh_canvas_components"] = ghCanvas?.Count    ?? 0,
                });
            }
            catch (Exception ex)
            {
                WriteJson(resp, new JObject { ["success"] = false, ["message"] = ex.Message }, 500);
            }
        }

        private static string FindScanOutputJson()
        {
            // Walk up from assembly location to find agent/tools/auto_generated/scan_output.json
            var dir = new DirectoryInfo(Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location));
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "agent", "tools", "auto_generated", "scan_output.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        // ── Static file serving (embedded React build) ───────────────────────────

        private static void ServeStatic(HttpListenerRequest req, HttpListenerResponse resp)
        {
            var path = req.Url.AbsolutePath;
            if (string.IsNullOrEmpty(path) || path == "/") path = "/index.html";

            var assembly = Assembly.GetExecutingAssembly();

            // Try exact resource name
            var resourceName = "PenguinClaw.www." + path.TrimStart('/').Replace('/', '.');
            var stream       = assembly.GetManifestResourceStream(resourceName);

            // Fallback for assets subfolder
            if (stream == null && path.StartsWith("/assets/"))
                stream = assembly.GetManifestResourceStream(
                    "PenguinClaw.www.assets." + Path.GetFileName(path));

            if (stream == null)
            {
                resp.StatusCode = 404;
                resp.Close();
                return;
            }

            using (stream)
            {
                var content = new byte[stream.Length];
                stream.Read(content, 0, content.Length);
                resp.ContentType     = MimeType(path);
                resp.ContentLength64 = content.Length;
                resp.OutputStream.Write(content, 0, content.Length);
                resp.Close();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static void WriteJson(HttpListenerResponse resp, JToken body, int status = 200)
        {
            resp.StatusCode  = status;
            resp.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            resp.ContentLength64 = bytes.Length;
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.Close();
        }

        private static string MimeType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js":   return "application/javascript";
                case ".css":  return "text/css";
                case ".json": return "application/json";
                case ".png":  return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".svg":  return "image/svg+xml";
                case ".ico":  return "image/x-icon";
                default:      return "application/octet-stream";
            }
        }
    }
}
