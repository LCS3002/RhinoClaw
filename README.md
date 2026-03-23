<div align="center">

# 🐧 PENGUINCLAW

**The AI plugin for Rhinoceros that actually builds things.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Rhino%208%20%E2%80%A2%20Windows-blue)](https://www.rhino3d.com/)
[![Providers](https://img.shields.io/badge/AI-Anthropic%20%7C%20Groq%20%7C%20Ollama-blueviolet)](#choosing-a-provider)

<br/>

</div>

---

PenguinClaw embeds an AI agent directly into Rhino 8 as a dockable panel. It reads your geometry, executes Rhino commands, manipulates Grasshopper sliders and components, and handles multi-step instructions — all from a chat interface inside the viewport.

```
"Build a 10×10×10 box, fillet the top edges at radius 2,
 then Boolean union it with a sphere at the top"
```

No scripting. No macros. Just natural language.

---

---

## How it works

```
Rhino 8 (C# plugin)
├── Embedded HTTP server      — serves the React UI at localhost:8080
├── AI agent loop             — ReAct loop via Anthropic API with tool use
├── RhinoCommon tool layer    — direct access to geometry, layers, GH canvas
└── GH component registry     — auto-indexes every installed GH component on startup
```

**Agent loop** — each message runs a ReAct loop: the AI receives the user message + tool definitions, returns a tool call or a final answer, tools execute on the Rhino UI thread, results feed back into the next turn. Continues until the model returns `end_turn`.

**Tool selection** — the full GH component registry (hundreds of entries) is keyword-matched against the user message; the top 5 most relevant are sent alongside the core tools. A single `run_rhino_command` tool covers all Rhino commands, keeping the per-request tool list small and stable.

**Thread safety** — all `RhinoDoc` / `RhinoApp` calls are dispatched to the UI thread via `RhinoApp.InvokeOnUiThread` + `ManualResetEventSlim`. The HTTP server and agent loop run on background threads.

**Prompt caching** — when using Anthropic, the system prompt, core tool definitions, and scan context are each marked `cache_control: ephemeral`. After the first request in a session, cached blocks cost ~10% of their normal price.

---

## Features

| | |
|---|---|
| 🤖 **Full Rhino access** | Any built-in command via natural language (`_Box`, `_Loft`, `_FilletEdge`, …) |
| 🌿 **Grasshopper integration** | List/set sliders, enumerate canvas components, build definitions programmatically |
| 📐 **Geometry inspection** | Selected objects, volumes, bounding boxes, layer info |
| 🧠 **Object-aware follow-ups** | Object IDs tracked after every operation — "scale it", "move that", "delete the sphere" all resolve correctly |
| 📏 **Direct transforms** | `move_object`, `scale_object`, `rotate_object`, `mirror_object`, `array_linear`, `array_polar` |
| 🔗 **Boolean operations** | `boolean_union`, `boolean_difference`, `boolean_intersection`, `join_curves` |
| 📸 **Viewport capture** | Captures the active Rhino viewport at its actual resolution |
| 💬 **Persistent history** | Chat history and action log survive panel reloads |
| 🔍 **Dynamic GH registry** | Rebuilt on startup and after `PenguinClawScan`; picks up third-party plugins |

---

## Requirements

- **Rhino 8** for Windows (RhinoCommon `.NET 4.8`)
- **An AI provider** — choose one from the table below (set up inside the plugin, no environment variables needed)

---

## Choosing a provider

PenguinClaw supports three AI backends. Configure in the plugin's Settings tab after installing.

| Provider | Cost | Quality | Setup |
|---|---|---|---|
| **Anthropic** (default) | ~$1–2/month daily use | Best | Free account + API key |
| **Groq** | Free tier (14,400 req/day) | Good | Free account + API key |
| **Ollama** | Free, always | Good on large models | Install Ollama + pull a model |

### Anthropic (best tool-calling quality)

1. Sign up at [console.anthropic.com](https://console.anthropic.com/) — pay-as-you-go, no subscription
2. Go to **API Keys** → **Create Key** → copy the key (`sk-ant-...`)
3. In PenguinClaw → **Settings** tab → select **Anthropic** → paste the key → **Save & Connect**

### Groq (free tier)

1. Sign up at [console.groq.com](https://console.groq.com/) — free, no credit card
2. Go to **API Keys** → **Create API Key** → copy the key (`gsk_...`)
3. In PenguinClaw → **Settings** tab → select **Groq** → paste the key → **Save & Connect**

### Ollama (local, fully free)

1. Install Ollama from [ollama.com](https://ollama.com/)
2. Open a terminal and run: `ollama pull qwen2.5:7b` (~4.7 GB download)
3. In PenguinClaw → **Settings** tab → select **Ollama** → **Save & Connect**

---

## Installation

**1. Install the plugin** — drag `PenguinClaw.rhp` onto the Rhino viewport, or use `PluginManager` → Install.

**2. Open the panel** — run the `PenguinClaw` command in Rhino.

**3. Choose a provider** — the Settings tab opens automatically on first launch.

**4. *(Optional)* Run `PenguinClawScan`** to index your installed Grasshopper components.

---

## Building from source

### Prerequisites

- [.NET 4.8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)
- [Node.js 18+](https://nodejs.org/)
- Rhino 8 installed (provides the RhinoCommon reference)

### Steps

```bash
# 1. Clone
git clone https://github.com/LCS3002/PenguinClaw.git
cd PenguinClaw

# 2. Build the React UI
cd penguinclaw/ui
npm install
npm run build

# 3. Copy the build into the plugin's www folder
cp dist/index.html ../rhino_plugin/www/
cp dist/assets/* ../rhino_plugin/www/assets/

# 4. Build the C# plugin
cd ../rhino_plugin
dotnet build PenguinClaw.csproj -c Release

# 5. Rename DLL → .rhp (Rhino plugin format)
cp bin/Release/net48/PenguinClaw.dll bin/Release/net48/PenguinClaw.rhp
```

Load `PenguinClaw.rhp` into Rhino via `PluginManager` → Install.

---

## Project structure

```
penguinclaw/
├── rhino_plugin/
│   ├── PenguinClawPlugin.cs        # Plugin entry point, panel registration
│   ├── PenguinClawPanel.cs         # Eto dockable panel + WebView host
│   ├── PenguinClawServer.cs        # Embedded HTTP server (port 8080)
│   ├── PenguinClawAgent.cs         # ReAct loop, config, provider selection
│   ├── LlmProviders.cs             # ILlmProvider + Anthropic / Groq / Ollama implementations
│   ├── PenguinClawTools.cs         # Core tool implementations (move, scale, GH, …)
│   ├── RhinoCommandRegistry.cs     # GH component index + keyword matcher
│   ├── PenguinClawActionLog.cs     # Persistent action log (context across restarts)
│   ├── PenguinClawScanCommand.cs   # PenguinClawScan — indexes GH components
│   └── www/                        # Embedded React build (served by HTTP server)
└── ui/
    └── App.jsx                     # React chat UI — chat, tools, settings tabs
```

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

---

## License

MIT — see [LICENSE](LICENSE).
