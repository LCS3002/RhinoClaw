# Contributing to PenguinClaw

Thanks for your interest. Bug fixes, new tools, UI improvements, and documentation are all welcome.

---

## Before you start

- For anything beyond a small fix, **open an issue first** to describe what you want to change and why. This avoids duplicated effort and keeps the project focused.
- PenguinClaw requires Rhino 8. If you don't have a license, you can still contribute to the React UI (`penguinclaw/ui/`) or documentation.

---

## Setup

Follow the [Building from source](README.md#building-from-source) steps in the README.

For UI-only changes you don't need to rebuild the C# plugin — run `npm run dev` in `penguinclaw/ui/` and point it at a running PenguinClaw instance (Rhino must be open with the plugin loaded).

---

## Adding a new tool

Tools live in `PenguinClawTools.cs`. Each tool needs three things:

**1. A definition in `GetToolDefinitions()`**

```csharp
new JObject
{
    ["name"]        = "my_tool",
    ["description"] = "Does something specific and useful. Be descriptive — Claude uses this to pick the right tool.",
    ["input_schema"] = new JObject
    {
        ["type"] = "object",
        ["properties"] = new JObject
        {
            ["param"] = new JObject { ["type"] = "string", ["description"] = "What this parameter controls." },
        },
        ["required"] = new JArray { "param" },
    },
},
```

**2. A case in `Execute()`**

```csharp
case "my_tool": return MyTool(S(input, "param"));
```

**3. An implementation** — must use `OnMain()` for any `RhinoDoc` / `RhinoApp` calls:

```csharp
private static string MyTool(string param)
{
    return OnMain(() =>
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc == null) return new JObject { ["success"] = false, ["message"] = "No active document." };
        return Obj("message", $"Did the thing with {param}.");
    });
}
```

> **Tool descriptions matter.** Claude uses them to decide which tool to call. Be specific about what the tool does and *when* to use it.

---

## Pull request checklist

- [ ] Tested against a live Rhino 8 session
- [ ] Multi-step sequences still work (create object → reference its ID → operate on it)
- [ ] Tool description is specific and action-oriented
- [ ] No new external dependencies without prior discussion
- [ ] One thing per PR

---

## Code style

- **C#** — 4-space indent, `var` where type is obvious, no unnecessary abstraction
- **React** — plain CSS, no new UI libraries
- **Commits** — short imperative summary: `Add material query tool`, `Fix slider name matching`

---

## Reporting bugs

Open a GitHub issue with:

1. What you typed in the chat
2. What the agent did vs. what you expected
3. Your Rhino version and any relevant plugins loaded
4. The error from the Rhino command line (if any)
