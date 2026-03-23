# PenguinClaw — Agent Architecture

Technical reference for contributors. Describes the design decisions, contracts, and extension points.

---

## Architectural Philosophy: Why `run_rhino_command` Beats 72 Enumerated Tools

Rhino 8 ships with 980+ built-in commands. A naive approach would define a separate tool for each (`create_box`, `create_sphere`, `extrude_curve`, …). This fails for four reasons:

1. **Context window cost.** Each tool definition takes ~30–80 tokens. 72 tools = ~4,000 tokens per request — before the user's message, history, or tool results.
2. **Prompt cache fragility.** Anthropic's ephemeral cache breaks if the tool list changes between requests. Dynamic, per-request tool sets ruin caching.
3. **Maintenance burden.** Every new Rhino command or third-party plugin command requires a new tool definition, parameter schema, and implementation.
4. **LLM knowledge overlap.** Claude already knows Rhino command syntax from training data. `_Box 0,0,0 10,10,0 10` is not novel information.

PenguinClaw instead exposes a single `run_rhino_command` tool that forwards any command string directly to `RhinoApp.RunScript()`. This gives the agent access to all 980+ Rhino commands (including third-party plugin commands) through one stable tool definition. The tool list stays small, the cache stays hot, and adding a new Rhino command requires zero code changes.

Dedicated typed tools (`move_object`, `scale_object`, `boolean_union`, etc.) exist only where the RhinoCommon API offers something the command line cannot: atomicity, structured return values, or undo safety for operations that need precise object ID tracking.

---

## ReAct Loop Contract

The agent loop is implemented in `PenguinClawAgent.Run()`.

### Turn structure

```
[User message arrives at /chat]
  ↓
Build system prompt array (cached base + scan context + dynamic action log)
Build tool list (core tools + top-5 dynamic GH tools for this message)
Append user message to history
  ↓
LOOP (max 25 iterations):
  │
  ├─ Call provider.Send(systemBlocks, messages, tools, maxTokens=8192)
  │
  ├─ stop_reason == "error"
  │   └─ Return error message to caller. Loop ends.
  │
  ├─ stop_reason == "end_turn"
  │   └─ Return llmResp.Text as final response. Loop ends.
  │
  └─ stop_reason == "tool_use"
      ├─ Reconstruct Anthropic-format assistant message, append to history
      ├─ For each tool call:
      │   ├─ PenguinClawTools.Execute(name, input, doc)    (or RhinoCommandRegistry.ExecuteDynamic)
      │   ├─ RecordToolResult() → update in-session scene state
      │   └─ PenguinClawActionLog.Record() → persist to disk
      ├─ Build tool_result user message, append to history
      ├─ TrimHistory() to MaxHistory=30 messages
      └─ Continue loop

[After 25 iterations without end_turn]
  └─ Return "Stopped: reached maximum tool call iterations."
```

### History format

All messages are stored in **Anthropic canonical format**:
- User messages: `{ "role": "user", "content": "..." }` or `{ "role": "user", "content": [tool_result, ...] }`
- Assistant messages: `{ "role": "assistant", "content": [text?, tool_use?, ...] }`

OpenAI-compatible providers (Groq, Ollama) receive converted messages via `OpenAiCompatProvider.BuildOaiMessages()`.

### History trimming

`TrimHistory()` keeps the last `MaxHistory=30` messages. It never starts on a `tool_result` block — it scans forward to the next normal user message to avoid sending an orphaned tool result without its corresponding assistant turn.

---

## GH Keyword Matcher

The GH registry (`RhinoCommandRegistry`) builds at plugin startup on a background thread. For each Grasshopper component, it stores:

```csharp
CachedTool {
    Definition:  JObject (Claude tool definition)
    Keywords:    string[] (pre-tokenized name + category + subcategory + description)
}
```

**Tokenization:** `Tokenize(text)` splits on whitespace and common delimiters (`_`, `-`, `.`, `/`, `(`, `)`, `,`, `›`), lowercases, removes tokens shorter than 3 characters, and deduplicates.

**Scoring per request:** `GetRelevantTools(userMessage, topK=5)`:
1. Tokenize the user message into a `HashSet<string>`
2. For each `gh_comp_*` entry, count how many of its keyword tokens appear in the query set
3. Sort descending by score, return top-K definitions with score > 0

**Why only 5?** Each GH component tool definition is 80–120 tokens. Sending 5 adds ~500 tokens per request — acceptable. Sending 50 would add ~5,000 tokens and break the prompt cache. `rhino_cmd_*` entries are excluded from per-request injection; `run_rhino_command` already covers them.

**Why keyword matching (not embeddings)?** The plugin targets .NET 4.8. There is no practical embedding model available in-process. Keyword overlap is fast (sub-millisecond), dependency-free, and sufficient for command names like "Amplitude", "Extrude", "Voronoi".

---

## Thread Safety Model

All `RhinoDoc` and `RhinoApp` calls must execute on Rhino's UI thread. The HTTP server and agent loop run on background threads. Every core tool follows this pattern:

```csharp
object result = null;
Exception error = null;
var done = new ManualResetEventSlim(false);

RhinoApp.InvokeOnUiThread(() =>
{
    try   { result = /* RhinoCommon call */ ; }
    catch (Exception ex) { error = ex; }
    finally { done.Set(); }
});

if (!done.Wait(TimeSpan.FromSeconds(30)))
    return Fail("Timeout: Rhino UI thread did not respond within 30 seconds.");
if (error != null)
    return Fail(error.Message);
```

`ManualResetEventSlim` is used (not `ManualResetEvent`) for lower allocation overhead. The 30-second timeout prevents the agent from hanging indefinitely if Rhino's UI thread is blocked (e.g., during a modal dialog).

**Why InvokeOnUiThread?** RhinoCommon is not thread-safe. Calling `doc.Objects.Add()` from a ThreadPool thread causes undefined behaviour — typically access violations or silent data corruption. There is no exception-based protection; the crash happens inside unmanaged code.

**Grasshopper interop** uses the same pattern via reflection (`Assembly.Load("Grasshopper")`). This avoids a hard compile-time reference to `Grasshopper.dll`, making the plugin loadable in Rhino environments without Grasshopper installed.

---

## Prompt Caching

Anthropic charges ~10% of normal input token cost for cache hits. PenguinClaw marks three system prompt blocks as `cache_control: ephemeral`:

| Block | Content | When it changes |
|---|---|---|
| Block 1 — base prompt | Static agent instructions + tool usage guide | Never (fixed string) |
| Block 2 — scan context | GH registry context from `PenguinClawScan` | Only after `PenguinClawScan` runs |
| Block 3 — dynamic context | Action log + session scene state | Every turn |

Block 3 is intentionally not cached because it changes every turn. Blocks 1 and 2 are stable across the session and hit cache after the first request.

Additionally, a `cache_control: ephemeral` breakpoint is placed on the **last core tool definition**. This caches all core tool definitions (the stable portion of the tool list). Dynamic GH tools appended after the breakpoint are not cached.

**Cost implication:** A 10-turn conversation with a 5,000-token system prompt + 3,000-token core tool list incurs ~8,000 cached tokens × 9 turns = ~72,000 cached input tokens instead of 80,000 full-price tokens. At Anthropic's pricing this is roughly a 90% saving on those tokens.

**OpenAI-compatible providers** (Groq, Ollama) receive the messages without `cache_control` fields — `OpenAiCompatProvider.BuildOaiMessages()` strips them automatically.

---

## How to Add a New Core Tool

A core tool is always sent to the LLM regardless of the user's message (unlike dynamic GH tools). Add one when:
- The operation requires structured typed parameters that `run_rhino_command` cannot express
- The operation needs to return structured data (object IDs, measurements) back to the agent
- The operation must be undo-safe and atomic

### Step 1 — Add the tool definition

In `PenguinClawTools.GetToolDefinitions()`, add a `JObject` to the returned `JArray`:

```csharp
new JObject
{
    ["name"]        = "my_new_tool",
    ["description"] = "One-sentence description. Be precise — this is what the LLM reads to decide when to call it.",
    ["input_schema"] = new JObject
    {
        ["type"] = "object",
        ["properties"] = new JObject
        {
            ["param_name"] = new JObject
            {
                ["type"]        = "string",  // "string" | "number" | "boolean" | "array"
                ["description"] = "What this parameter does.",
            },
        },
        ["required"] = new JArray { "param_name" },
    },
},
```

### Step 2 — Add the dispatch case

In `PenguinClawTools.Execute()`, add a case to the switch statement:

```csharp
case "my_new_tool": return MyNewTool(S(input, "param_name"));
```

Helper extractors available: `S(input, key)` → string, `D(input, key)` → double, `I(input, key)` → int.

### Step 3 — Implement the method

```csharp
private static string MyNewTool(string paramName)
{
    object result = null;
    Exception error = null;
    var done = new ManualResetEventSlim(false);

    RhinoApp.InvokeOnUiThread(() =>
    {
        try
        {
            // RhinoCommon calls here
            var doc = RhinoDoc.ActiveDoc;
            result = /* ... */;
        }
        catch (Exception ex) { error = ex; }
        finally { done.Set(); }
    });

    if (!done.Wait(TimeSpan.FromSeconds(30)))
        return Fail("Timeout waiting for Rhino UI thread.");
    if (error != null)
        return Fail(error.Message);

    return new JObject { ["success"] = true, ["result"] = result?.ToString() }
        .ToString(Formatting.None);
}
```

Rules:
- Never throw from a tool implementation. Always return a structured error via `Fail(message)`.
- Always go through `InvokeOnUiThread` for any `RhinoDoc`/`RhinoApp` call.
- Return JSON with at least `{ "success": true/false }`.
- If the tool creates or modifies objects, include `"selected_objects": [{ "id": "...", "type": "..." }]` in the result so `RecordToolResult()` can update scene state.

### Step 4 — Update the system prompt (if needed)

If the agent needs to know when to call the new tool, add a line to `BaseSystemPrompt` in `PenguinClawAgent.cs`. Keep it concise — the system prompt is already substantial.

---

## How to Add a New LLM Provider

All providers implement `ILlmProvider`:

```csharp
internal interface ILlmProvider
{
    LlmResponse Send(JArray systemBlocks, JArray messages, JArray tools, int maxTokens);
}
```

Input format is always **Anthropic canonical** (system as JArray of typed blocks, messages with tool_use/tool_result content blocks, tools with `input_schema`). Non-Anthropic providers must convert this internally.

### Step 1 — Implement the provider class

```csharp
internal class MyProvider : ILlmProvider
{
    public LlmResponse Send(JArray systemBlocks, JArray messages, JArray tools, int maxTokens)
    {
        // Convert inputs from Anthropic format to your provider's format
        // Call the API (synchronously via .GetAwaiter().GetResult())
        // Parse the response
        // Return LlmResponse with StopReason = "end_turn" | "tool_use" | "error"
    }
}
```

See `OpenAiCompatProvider` for a complete example of Anthropic→OpenAI format conversion.

### Step 2 — Register in the factory

In `LlmProviderFactory.Create()`:

```csharp
case "myprovider":
    return new MyProvider(cfg.ApiKey, cfg.Model ?? "default-model-id");
```

### Step 3 — Add a default model name

In `LlmProviderFactory.DefaultModel()`:

```csharp
case "myprovider": return "my-model-name";
```

### Step 4 — Expose in the UI

In `penguinclaw/ui/App.jsx`, add the provider name to the Settings tab's provider selector. The `POST /settings` endpoint accepts `{ provider, api_key, model, ollama_url }` and saves to `%APPDATA%/PenguinClaw/config.json`.

---

## Tool-Calling Reliability: Anthropic vs Groq vs Ollama

| | Anthropic (Claude) | Groq (Llama 3.3 70B) | Ollama (Qwen 2.5 7B) |
|---|---|---|---|
| **Parallel tool calls** | Reliable | Occasional misses | Rare |
| **Nested JSON in args** | Correct | Usually correct | Sometimes flattened |
| **Tool call on first turn** | Always when appropriate | Usually | Inconsistent |
| **Long tool result handling** | Handles 8K+ tokens | Handles 4K tokens | Degrades past 2K |
| **Following stop conditions** | Reliable | Reliable | Sometimes loops |
| **Cost** | ~$0.25/MTok (cached) | Free tier | Free |

**Practical implications:**
- For multi-step Grasshopper definition building (many tool calls in sequence), Anthropic is significantly more reliable.
- Groq is a good free alternative for single-step geometry creation.
- Ollama's 7B model works for simple commands but struggles with complex ReAct chains. A 32B+ model (e.g. `qwen2.5:32b`) closes the gap substantially.
- The `build_gh_definition` tool requires precise nested JSON for `components[]` and `wires[]` — Anthropic handles this consistently; smaller models often malform the arrays.
