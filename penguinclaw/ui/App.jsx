import { useState, useEffect, useRef } from "react";

const API_BASE = window.location.port === "5173" ? "http://localhost:8080" : "";

// ── Palette ──────────────────────────────────────────────────────────────────
const C = {
  black:      "#111111",
  blackSoft:  "#1C1C1C",
  orange:     "#F97316",
  orangeDim:  "#EA6C0D",
  orangeFaint:"rgba(249,115,22,0.12)",
  orangeBorder:"rgba(249,115,22,0.25)",
  white:      "#FFFFFF",
  offWhite:   "#F9F7F5",
  gray100:    "#F3F4F6",
  gray300:    "#D1D5DB",
  gray500:    "#6B7280",
  gray700:    "#374151",
  red:        "#DC2626",
  green:      "#059669",
  purple:     "#9333EA",
  purpleFaint:"rgba(147,51,234,0.08)",
  purpleBorder:"rgba(147,51,234,0.2)",
};

const PROVIDERS = {
  anthropic: {
    label:       "Anthropic (best quality)",
    keyPrefix:   "sk-ant-",
    keyPlaceholder: "sk-ant-...",
    description: "Best tool-calling quality. ~$1–2/month for daily use.",
    link:        "https://console.anthropic.com/",
    linkLabel:   "Get API key at console.anthropic.com ↗",
  },
  groq: {
    label:       "Groq (free tier)",
    keyPrefix:   "gsk_",
    keyPlaceholder: "gsk_...",
    description: "Free up to 14,400 requests/day. Requires a free Groq account.",
    link:        "https://console.groq.com/",
    linkLabel:   "Get free API key at console.groq.com ↗",
  },
  ollama: {
    label:       "Ollama (local, free)",
    keyPrefix:   null,
    keyPlaceholder: null,
    description: "Fully local — no API key, no cost. Requires Ollama installed and a model downloaded (e.g. qwen2.5:7b).",
    link:        "https://ollama.com/",
    linkLabel:   "Install Ollama at ollama.com ↗",
  },
};

// ── Penguin logo ─────────────────────────────────────────────────────────────
function PenguinMark({ size = 24, glow = false }) {
  return (
    <svg width={size} height={size * 1.25} viewBox="0 0 80 100" fill="none"
      xmlns="http://www.w3.org/2000/svg"
      style={glow ? { filter: `drop-shadow(0 0 6px rgba(249,115,22,0.5))` } : {}}>
      {/* Body */}
      <ellipse cx="40" cy="63" rx="26" ry="32" fill={C.black} />
      {/* Belly */}
      <ellipse cx="40" cy="69" rx="14" ry="21" fill={C.white} />
      {/* Head */}
      <ellipse cx="40" cy="30" rx="18" ry="18" fill={C.black} />
      {/* Eyes */}
      <circle cx="33" cy="26" r="4.5" fill={C.white} />
      <circle cx="47" cy="26" r="4.5" fill={C.white} />
      <circle cx="34.5" cy="26.5" r="2.2" fill={C.black} />
      <circle cx="48.5" cy="26.5" r="2.2" fill={C.black} />
      <circle cx="35.2" cy="25.8" r="0.8" fill={C.white} />
      <circle cx="49.2" cy="25.8" r="0.8" fill={C.white} />
      {/* Beak */}
      <polygon points="40,34 35,42 45,42" fill={C.orange} />
      {/* Left wing */}
      <ellipse cx="15" cy="63" rx="10" ry="21" fill={C.black} transform="rotate(-12 15 63)" />
      {/* Right wing */}
      <ellipse cx="65" cy="63" rx="10" ry="21" fill={C.black} transform="rotate(12 65 63)" />
      {/* Feet */}
      <ellipse cx="31" cy="94" rx="10" ry="5" fill={C.orange} />
      <ellipse cx="51" cy="94" rx="10" ry="5" fill={C.orange} />
    </svg>
  );
}

// ── Tool call card ────────────────────────────────────────────────────────────
function ToolCall({ tool }) {
  const isVision = tool.name === "capture_and_assess";
  let imageSrc = null;
  if (isVision && tool.result) {
    try {
      const r = JSON.parse(tool.result);
      if (r.base64) imageSrc = `data:image/png;base64,${r.base64}`;
    } catch {}
  }
  const borderColor = isVision ? C.purple     : C.orange;
  const bgColor     = isVision ? C.purpleFaint : C.orangeFaint;
  const bdColor     = isVision ? C.purpleBorder: C.orangeBorder;
  return (
    <div style={{
      margin: "3px 0", padding: "5px 9px",
      background: bgColor,
      border: `1px solid ${bdColor}`,
      borderLeft: `3px solid ${borderColor}`,
      borderRadius: "4px",
      fontFamily: "monospace", fontSize: "11px",
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
        <span style={{ color: borderColor, fontWeight: 700 }}>
          {isVision ? "👁" : "⚡"} {tool.name}
        </span>
        <span style={{ marginLeft: "auto", color: C.green, fontSize: "10px" }}>✓</span>
      </div>
      {tool.result && (
        <div style={{ color: C.gray500, fontSize: "10px", marginTop: "3px",
          borderTop: `1px solid ${bdColor}`, paddingTop: "3px",
          wordBreak: "break-all" }}>
          → {tool.result.length > 120 ? tool.result.slice(0, 120) + "…" : tool.result}
        </div>
      )}
      {imageSrc && (
        <img src={imageSrc} alt="viewport" style={{
          marginTop: "6px", maxWidth: "100%", maxHeight: "120px",
          borderRadius: "4px", border: `1px solid ${C.purpleBorder}`,
          objectFit: "contain",
        }} />
      )}
    </div>
  );
}

// ── Chat message ──────────────────────────────────────────────────────────────
function Message({ msg }) {
  if (msg.role === "user") {
    return (
      <div style={{ display: "flex", justifyContent: "flex-end", margin: "8px 0" }}>
        <div style={{
          maxWidth: "80%",
          background: `linear-gradient(135deg, ${C.orange}, ${C.orangeDim})`,
          color: C.white, padding: "8px 12px", borderRadius: "14px 14px 3px 14px",
          fontSize: "13px", lineHeight: 1.5,
        }}>{msg.text}</div>
      </div>
    );
  }
  return (
    <div style={{ margin: "8px 0", display: "flex", gap: "8px" }}>
      <div style={{
        width: 26, height: 26, borderRadius: "50%",
        background: C.black,
        display: "flex", alignItems: "center", justifyContent: "center",
        flexShrink: 0, marginTop: 2,
        border: `1px solid ${C.orangeBorder}`,
      }}><PenguinMark size={16} glow /></div>
      <div style={{ flex: 1, minWidth: 0 }}>
        {msg.tools && msg.tools.map((t, i) => <ToolCall key={i} tool={t} />)}
        {msg.text_final && (
          <div style={{ marginTop: msg.tools?.length ? "6px" : 0,
            color: C.gray700, fontSize: "13px", lineHeight: 1.6 }}>
            {msg.text_final.split("**").map((p, i) =>
              i % 2 === 1
                ? <strong key={i} style={{ color: C.black }}>{p}</strong>
                : <span key={i}>{p}</span>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

function inferCategory(cat, name = "") {
  if (cat === "grasshopper") return "GH";
  if (cat === "rhino") return "Rhino";
  if (cat === "core") {
    const n = name.toLowerCase();
    if (n.includes("gh") || n.includes("grasshopper") || n.includes("slider")) return "GH";
    return "Core";
  }
  return "Core";
}

// ── Settings form ─────────────────────────────────────────────────────────────
function SettingsForm({ onSaved }) {
  const [provider,       setProvider]       = useState("anthropic");
  const [apiKey,         setApiKey]         = useState("");
  const [model,          setModel]          = useState("");
  const [ollamaUrl,      setOllamaUrl]      = useState("http://localhost:11434");
  const [saving,         setSaving]         = useState(false);
  const [msg,            setMsg]            = useState({ type: "", text: "" });
  const [loaded,         setLoaded]         = useState(false);
  const [providerStatus, setProviderStatus] = useState(null);

  useEffect(() => {
    let alive = true;
    const check = async () => {
      try {
        const r = await fetch(`${API_BASE}/health`);
        const d = await r.json();
        if (alive) setProviderStatus(!!d.ai_configured);
      } catch {
        if (alive) setProviderStatus(false);
      }
    };
    check();
    const t = setInterval(check, 30000);
    return () => { alive = false; clearInterval(t); };
  }, []);

  useEffect(() => {
    fetch(`${API_BASE}/settings`)
      .then(r => r.json())
      .then(d => {
        setProvider(d.provider    || "anthropic");
        setModel(d.model          || "");
        setOllamaUrl(d.ollama_url || "http://localhost:11434");
        setLoaded(true);
      })
      .catch(() => setLoaded(true));
  }, []);

  async function save() {
    const p = PROVIDERS[provider];
    if (provider !== "ollama" && !apiKey.trim()) {
      setMsg({ type: "err", text: `API key is required for ${p.label}.` });
      return;
    }
    setSaving(true); setMsg({ type: "", text: "" });
    try {
      const r = await fetch(`${API_BASE}/settings`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ provider, api_key: apiKey, model, ollama_url: ollamaUrl }),
      });
      const d = await r.json();
      if (d.success) {
        setMsg({ type: "ok", text: "Settings saved." });
        setApiKey("");
        if (onSaved) onSaved(provider);
      } else {
        setMsg({ type: "err", text: d.message || "Failed to save." });
      }
    } catch {
      setMsg({ type: "err", text: "Could not reach server." });
    }
    setSaving(false);
  }

  if (!loaded) return <div style={{ color: C.gray500, fontSize: "11px" }}>Loading…</div>;

  const p = PROVIDERS[provider] || PROVIDERS.anthropic;
  const inputStyle = {
    background: C.white, border: `1px solid ${C.gray300}`,
    borderRadius: "6px", padding: "8px 10px", color: C.black,
    fontSize: "11px", outline: "none", width: "100%", boxSizing: "border-box",
  };
  const labelStyle = { color: C.gray500, fontSize: "10px", marginBottom: "3px", display: "block" };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "10px", width: "100%" }}>

      <div>
        <span style={labelStyle}>AI Provider</span>
        <select value={provider} onChange={e => { setProvider(e.target.value); setMsg({ type: "", text: "" }); }}
          style={{ ...inputStyle, fontFamily: "inherit", cursor: "pointer" }}>
          {Object.entries(PROVIDERS).map(([k, v]) => (
            <option key={k} value={k}>{v.label}</option>
          ))}
        </select>
      </div>

      <div style={{ color: C.gray500, fontSize: "10px", lineHeight: 1.5 }}>{p.description}</div>

      {provider !== "ollama" && (
        <div>
          <span style={labelStyle}>API Key</span>
          <input
            type="password"
            placeholder={p.keyPlaceholder}
            value={apiKey}
            onChange={e => { setApiKey(e.target.value); setMsg({ type: "", text: "" }); }}
            onKeyDown={e => e.key === "Enter" && save()}
            style={{ ...inputStyle, fontFamily: "monospace" }}
          />
        </div>
      )}

      {provider === "ollama" && (
        <div>
          <span style={labelStyle}>Ollama URL</span>
          <input
            type="text"
            placeholder="http://localhost:11434"
            value={ollamaUrl}
            onChange={e => setOllamaUrl(e.target.value)}
            style={inputStyle}
          />
        </div>
      )}

      <div>
        <span style={labelStyle}>
          Model override <span style={{ color: C.gray300 }}>(optional — leave blank for default)</span>
        </span>
        <input
          type="text"
          placeholder={`default: ${provider === "anthropic" ? "claude-haiku-4-5" : provider === "groq" ? "llama-3.3-70b-versatile" : "qwen2.5:7b"}`}
          value={model}
          onChange={e => setModel(e.target.value)}
          style={inputStyle}
        />
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "10px", color: C.gray500 }}>
        <div style={{ width: 8, height: 8, borderRadius: "50%",
          background: providerStatus === null ? C.gray300 : providerStatus ? C.green : C.red }} />
        {providerStatus === null ? "Checking…" : providerStatus ? "Connected" : "Not configured"}
      </div>

      {msg.text && (
        <div style={{ fontSize: "10px", textAlign: "center", color: msg.type === "ok" ? C.green : C.red }}>
          {msg.text}
        </div>
      )}

      <button onClick={save} disabled={saving} style={{
        background: saving ? C.gray300 : `linear-gradient(135deg, ${C.orange}, ${C.orangeDim})`,
        border: "none", borderRadius: "6px", padding: "9px", color: C.white,
        fontSize: "12px", fontWeight: 700, cursor: saving ? "default" : "pointer",
      }}>
        {saving ? "Saving…" : "Save & Connect"}
      </button>

      <a href={p.link} target="_blank" rel="noreferrer"
        style={{ color: C.gray500, fontSize: "10px", textAlign: "center", textDecoration: "none" }}>
        {p.linkLabel}
      </a>
    </div>
  );
}

// ── Main app ──────────────────────────────────────────────────────────────────
export default function PenguinClaw() {
  const [messages, setMessages] = useState(() => {
    try { return JSON.parse(localStorage.getItem("pc_messages") || "[]"); } catch { return []; }
  });
  const [input, setInput]     = useState("");
  const [history, setHistory] = useState(() => {
    try { return JSON.parse(localStorage.getItem("pc_history") || "[]"); } catch { return []; }
  });
  const [tools, setTools]   = useState([]);
  const [health, setHealth] = useState({
    fetch_ok: false, rhino_connected: false, document_open: false,
    ai_configured: false, tools_loaded: 0, provider: "anthropic",
  });
  const [tab, setTab]               = useState("chat");
  const [isTyping, setIsTyping]     = useState(false);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [abortCtrl, setAbortCtrl]   = useState(null);
  const [visionActive, setVisionActive] = useState(false);
  const [turnStats, setTurnStats]   = useState({ turn: 0, inputTokens: 0, outputTokens: 0 });
  const chatEndRef = useRef(null);

  useEffect(() => { chatEndRef.current?.scrollIntoView({ behavior: "smooth" }); }, [messages, isTyping]);
  useEffect(() => { try { localStorage.setItem("pc_messages", JSON.stringify(messages)); } catch {} }, [messages]);
  useEffect(() => { try { localStorage.setItem("pc_history",  JSON.stringify(history));  } catch {} }, [history]);

  useEffect(() => {
    let alive = true;
    const poll = async () => {
      try {
        const r = await fetch(`${API_BASE}/health`);
        const d = await r.json();
        if (!alive) return;
        setHealth({
          fetch_ok:        true,
          rhino_connected: !!d.rhino_connected,
          document_open:   !!d.document_open,
          ai_configured:   !!d.ai_configured,
          tools_loaded:    d.tools_loaded || 0,
          document:        d.document || "",
          provider:        d.provider || "anthropic",
        });
      } catch {
        if (alive) setHealth(p => ({ ...p, fetch_ok: false }));
      }
    };
    poll();
    const t = setInterval(poll, 5000);
    return () => { alive = false; clearInterval(t); };
  }, []);

  useEffect(() => {
    let alive = true;
    const poll = async () => {
      try {
        const r = await fetch(`${API_BASE}/tools`);
        const d = await r.json();
        if (alive && Array.isArray(d) && d.length) setTools(d);
      } catch {}
    };
    poll();
    const t = setInterval(poll, 5000);
    return () => { alive = false; clearInterval(t); };
  }, []);

  async function sendMessage() {
    const msg = input.trim();
    if (!msg || isTyping) return;
    setInput("");
    setMessages(p => [...p, { role: "user", text: msg, id: Date.now() }]);
    setIsTyping(true);
    setVisionActive(false);

    const ctrl = new AbortController();
    setAbortCtrl(ctrl);

    try {
      const r = await fetch(`${API_BASE}/chat`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message: msg, history }),
        signal: ctrl.signal,
      });
      const d = await r.json();
      setIsTyping(false);
      setAbortCtrl(null);

      const hasVision = d.tool_calls?.some(t => t.name === "capture_and_assess");
      setVisionActive(hasVision || false);

      setMessages(p => [...p, {
        role: "agent", tools: d.tool_calls || [],
        text_final: d.response || "", id: Date.now() + 1,
      }]);

      setTurnStats(p => ({
        turn: p.turn + 1 + (d.tool_calls?.length || 0),
        inputTokens:  p.inputTokens  + (d.input_tokens  || 0),
        outputTokens: p.outputTokens + (d.output_tokens || 0),
      }));

      const toolSummary = d.tool_calls?.length
        ? "\n[Tools used: " + d.tool_calls.map(t => t.name).join(", ") + "]"
        : "";
      setHistory(p => [...p,
        { role: "user",      content: msg },
        { role: "assistant", content: (d.response || "") + toolSummary },
      ]);
    } catch (err) {
      setIsTyping(false);
      setAbortCtrl(null);
      if (err.name !== "AbortError") {
        setMessages(p => [...p, { role: "agent", text_final: "Could not reach PenguinClaw server.", id: Date.now() + 1 }]);
      }
    }
  }

  function stopGeneration() {
    abortCtrl?.abort();
    fetch(`${API_BASE}/stop`, { method: "POST" }).catch(() => {});
    setIsTyping(false);
    setAbortCtrl(null);
  }

  const dot = (ok, unknown) => ({
    width: 7, height: 7, borderRadius: "50%", flexShrink: 0,
    background: unknown ? C.gray300 : ok ? C.green : C.red,
  });
  const catColor = c => c === "GH" ? C.purple : c === "Rhino" ? C.orange : C.gray700;

  const providerLabel = PROVIDERS[health.provider]?.label?.split(" ")[0] || "AI";

  return (
    <div style={{ position: "fixed", inset: 0, background: C.offWhite, display: "flex", flexDirection: "column", fontFamily: "'DM Sans',system-ui,sans-serif", overflow: "hidden" }}>
      <style>{`
        @import url('https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@600;700&family=DM+Sans:wght@400;600;700&family=JetBrains+Mono:wght@400;600&display=swap');
        * { box-sizing: border-box; }
        @keyframes dotBounce { 0%,100%{transform:translateY(0)} 50%{transform:translateY(-4px)} }
        @keyframes eyeBlink { 0%,100%{transform:scaleY(1)} 40%{transform:scaleY(0.1)} }
        ::-webkit-scrollbar { width: 3px; }
        ::-webkit-scrollbar-thumb { background: rgba(249,115,22,0.3); border-radius: 2px; }
        select, input, textarea { color-scheme: light; }
      `}</style>

      {/* ── Header ── */}
      <div style={{ display: "flex", alignItems: "center", gap: "8px", padding: "8px 12px", background: C.black, flexShrink: 0 }}>
        <PenguinMark size={20} glow />
        <span style={{ fontSize: "13px", fontWeight: 700, color: C.white, letterSpacing: "0.1em", fontFamily: "'Space Grotesk',sans-serif" }}>
          PENGUIN<span style={{ color: C.orange }}>CLAW</span>
        </span>

        <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: "5px" }}>
          <div title="Server"   style={dot(health.fetch_ok, false)} />
          <div title="Rhino"    style={dot(health.rhino_connected, !health.fetch_ok)} />
          <div title="Document" style={dot(health.document_open,   !health.fetch_ok)} />
          <div title={providerLabel} style={dot(health.ai_configured, !health.fetch_ok)} />
        </div>

        <button onClick={() => setSidebarOpen(o => !o)} title="Status" style={{ background: "none", border: "none", color: sidebarOpen ? C.orange : "rgba(255,255,255,0.4)", cursor: "pointer", fontSize: "14px", padding: "2px 4px", lineHeight: 1 }}>☰</button>
      </div>

      {/* ── Tabs ── */}
      <div style={{ display: "flex", borderBottom: `1px solid ${C.gray300}`, background: C.white, flexShrink: 0 }}>
        {[
          ["chat",     `💬 Chat`],
          ["tools",    `🔧 Tools (${health.tools_loaded})`],
          ["settings", `⚙ Settings`],
        ].map(([t, label]) => (
          <button key={t} onClick={() => setTab(t)} style={{
            padding: "7px 14px", fontSize: "11px", cursor: "pointer",
            background: "none", border: "none",
            color: tab === t ? C.orange : C.gray500,
            borderBottom: `2px solid ${tab === t ? C.orange : "transparent"}`,
            fontWeight: tab === t ? 700 : 400, letterSpacing: "0.04em",
          }}>{label}</button>
        ))}
      </div>

      {/* ── Body ── */}
      <div style={{ flex: 1, display: "flex", overflow: "hidden" }}>

        <div style={{ flex: 1, display: "flex", flexDirection: "column", overflow: "hidden" }}>

          {/* Chat tab */}
          {tab === "chat" && (<>
            <div style={{ flex: 1, overflowY: "auto", padding: "12px" }}>
              {messages.length === 0 ? (
                <div style={{ height: "100%", display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: "12px" }}>
                  <PenguinMark size={56} glow />
                  <div style={{ color: C.black, fontSize: "15px", fontWeight: 800, letterSpacing: "0.12em", fontFamily: "'Space Grotesk',sans-serif" }}>
                    PENGUIN<span style={{ color: C.orange }}>CLAW</span>
                  </div>
                  {!health.fetch_ok || health.ai_configured ? (
                    <div style={{ color: C.gray500, fontSize: "11px", textAlign: "center", maxWidth: "200px", lineHeight: 1.6 }}>
                      The AI that actually builds.
                    </div>
                  ) : (
                    <div style={{ width: "240px" }}>
                      <div style={{ color: C.gray500, fontSize: "11px", textAlign: "center", marginBottom: "12px", lineHeight: 1.5 }}>
                        Choose your AI provider to get started.
                      </div>
                      <SettingsForm onSaved={() => setHealth(p => ({ ...p, ai_configured: true }))} />
                    </div>
                  )}
                </div>
              ) : (
                <>
                  {messages.map(m => <Message key={m.id} msg={m} />)}
                  {isTyping && (
                    <div style={{ display: "flex", flexDirection: "column", gap: "2px", padding: "6px 0" }}>
                      <div style={{ display: "flex", gap: "4px", alignItems: "center" }}>
                        <div style={{ width: 22, height: 22, borderRadius: "50%", background: C.black, display: "flex", alignItems: "center", justifyContent: "center", border: `1px solid ${C.orangeBorder}` }}>
                          <PenguinMark size={14} />
                        </div>
                        {[0,1,2].map(i => <div key={i} style={{ width: 4, height: 4, borderRadius: "50%", background: C.orange, animation: `dotBounce 0.8s ease-in-out ${i*0.15}s infinite` }} />)}
                      </div>
                      {visionActive && (
                        <div style={{ display: "flex", alignItems: "center", gap: "4px", fontSize: "10px", color: C.purple, paddingLeft: "2px" }}>
                          <span style={{ animation: "eyeBlink 2s ease-in-out infinite", display: "inline-block" }}>👁</span>
                          <span>vision</span>
                        </div>
                      )}
                    </div>
                  )}
                  <div ref={chatEndRef} />
                </>
              )}
            </div>

            {/* Input */}
            <div style={{ borderTop: `1px solid ${C.gray300}`, background: C.white, flexShrink: 0 }}>
              {turnStats.turn > 0 && (
                <div style={{ fontSize: "9px", color: C.gray300, textAlign: "center", paddingTop: "3px" }}>
                  Turn {turnStats.turn}
                  {turnStats.outputTokens > 0 && ` · ~$${((turnStats.inputTokens * 0.00000025 + turnStats.outputTokens * 0.00000125)).toFixed(4)}`}
                </div>
              )}
              <div style={{ padding: "8px 10px", display: "flex", gap: "6px", alignItems: "flex-end" }}>
                <textarea
                  style={{ flex: 1, background: C.offWhite, border: `1px solid ${C.gray300}`, borderRadius: "8px", padding: "8px 10px", color: C.black, fontSize: "12px", fontFamily: "inherit", outline: "none", resize: "none", minHeight: "36px", maxHeight: "90px", lineHeight: 1.5 }}
                  placeholder="Ask PenguinClaw…"
                  value={input}
                  onChange={e => setInput(e.target.value)}
                  onKeyDown={e => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); sendMessage(); } }}
                  rows={1}
                />
                {isTyping ? (
                  <button onClick={stopGeneration} style={{ background: C.red, border: "none", borderRadius: "7px", padding: "8px 12px", color: C.white, cursor: "pointer", fontSize: "12px", fontWeight: 700, flexShrink: 0 }}>■ Stop</button>
                ) : (
                  <button onClick={sendMessage} style={{ background: `linear-gradient(135deg, ${C.orange}, ${C.orangeDim})`, border: "none", borderRadius: "7px", padding: "8px 12px", color: C.white, cursor: "pointer", fontSize: "14px", fontWeight: 700, flexShrink: 0 }}>↑</button>
                )}
                <button onClick={() => { setMessages([]); setHistory([]); setTurnStats({ turn: 0, inputTokens: 0, outputTokens: 0 }); }} title="Clear conversation" style={{ background: "none", border: `1px solid ${C.gray300}`, borderRadius: "7px", padding: "8px 10px", color: C.gray500, cursor: "pointer", fontSize: "12px", flexShrink: 0 }}>✕</button>
              </div>
            </div>
          </>)}

          {/* Tools tab */}
          {tab === "tools" && (
            <div style={{ flex: 1, overflowY: "auto", padding: "10px" }}>
              {tools.length === 0 ? (
                <div style={{ color: C.gray500, fontSize: "12px", textAlign: "center", marginTop: "40px" }}>No tools loaded yet.</div>
              ) : tools.map(t => {
                const cat = inferCategory(t.category, t.name);
                const c   = catColor(cat);
                const isSummary = t.category === "rhino" || t.category === "grasshopper";
                return (
                  <div key={t.name} style={{ padding: "7px 10px", marginBottom: "4px", borderRadius: "6px", background: C.white, border: `1px solid rgba(0,0,0,0.07)`, borderLeft: isSummary ? `3px solid ${c}` : `1px solid rgba(0,0,0,0.07)` }}>
                    <div style={{ fontSize: "11px", fontFamily: "monospace", color: c, fontWeight: 600 }}>{t.name}</div>
                    {t.description && <div style={{ fontSize: "10px", color: C.gray500, marginTop: "2px", lineHeight: 1.4 }}>{t.description}</div>}
                  </div>
                );
              })}
            </div>
          )}

          {/* Settings tab */}
          {tab === "settings" && (
            <div style={{ flex: 1, overflowY: "auto", padding: "16px" }}>
              <div style={{ color: C.gray500, fontSize: "10px", letterSpacing: "0.1em", textTransform: "uppercase", marginBottom: "12px" }}>AI Provider</div>
              <SettingsForm onSaved={provider => {
                setHealth(p => ({ ...p, ai_configured: true, provider }));
                setTab("chat");
              }} />
            </div>
          )}
        </div>

        {/* ── Sidebar ── */}
        {sidebarOpen && (
          <div style={{ width: "160px", borderLeft: `1px solid ${C.gray300}`, background: C.white, display: "flex", flexDirection: "column", flexShrink: 0, overflow: "hidden" }}>
            <div style={{ padding: "8px", borderBottom: `1px solid ${C.gray300}`, fontSize: "9px", color: C.gray500, letterSpacing: "0.1em", textTransform: "uppercase" }}>Status</div>
            <div style={{ padding: "8px", fontSize: "9px", lineHeight: 2, color: C.gray500, borderBottom: `1px solid ${C.gray300}` }}>
              {[
                ["Server",      health.fetch_ok,         false],
                ["Rhino",       health.rhino_connected,  !health.fetch_ok],
                ["Document",    health.document_open,    !health.fetch_ok],
                [providerLabel, health.ai_configured,    !health.fetch_ok],
              ].map(([label, ok, unknown]) => (
                <div key={label} style={{ display: "flex", alignItems: "center", gap: "5px" }}>
                  <div style={dot(ok, unknown)} />
                  <span style={{ color: unknown ? C.gray300 : ok ? C.green : C.red }}>{label}</span>
                </div>
              ))}
              {health.document && <div style={{ color: C.gray300, marginTop: "4px", wordBreak: "break-all" }}>{health.document}</div>}
            </div>
            <div style={{ padding: "8px 6px", flex: 1, overflowY: "auto", fontSize: "9px" }}>
              <div style={{ color: C.gray500, letterSpacing: "0.1em", textTransform: "uppercase", marginBottom: "6px", padding: "0 2px" }}>Tools</div>
              {tools.map(t => {
                const cat = inferCategory(t.category, t.name);
                const c   = catColor(cat);
                return (
                  <div key={t.name} style={{ padding: "3px 6px", marginBottom: "1px", borderRadius: "3px", color: c, fontFamily: "monospace", fontSize: "9px", background: `${c}14` }}>
                    {t.name}
                  </div>
                );
              })}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
