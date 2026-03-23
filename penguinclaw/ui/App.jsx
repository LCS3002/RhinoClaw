import { useState, useEffect, useRef } from "react";

const API_BASE = window.location.port === "5173" ? "http://localhost:8080" : "";

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

// ── Penguin logo ────────────────────────────────────────────────────────────
function PenguinMark({ size = 24, color = "#2563EB", glow = false }) {
  return (
    <svg width={size} height={size * 1.25} viewBox="0 0 80 100" fill="none"
      xmlns="http://www.w3.org/2000/svg"
      style={glow ? { filter: `drop-shadow(0 0 8px ${color}44)` } : {}}>
      {/* Body */}
      <ellipse cx="40" cy="63" rx="26" ry="32" fill={color} />
      {/* Belly */}
      <ellipse cx="40" cy="69" rx="14" ry="21" fill="#FFFFFF" />
      {/* Head */}
      <ellipse cx="40" cy="30" rx="18" ry="18" fill={color} />
      {/* Eyes */}
      <circle cx="33" cy="26" r="4.5" fill="#FFFFFF" />
      <circle cx="47" cy="26" r="4.5" fill="#FFFFFF" />
      <circle cx="34.5" cy="26.5" r="2.2" fill="#1A1A2E" />
      <circle cx="48.5" cy="26.5" r="2.2" fill="#1A1A2E" />
      <circle cx="35.2" cy="25.8" r="0.8" fill="#FFFFFF" />
      <circle cx="49.2" cy="25.8" r="0.8" fill="#FFFFFF" />
      {/* Beak */}
      <polygon points="40,34 35,42 45,42" fill="#F59E0B" />
      {/* Left wing */}
      <ellipse cx="15" cy="63" rx="10" ry="21" fill={color} transform="rotate(-12 15 63)" />
      {/* Right wing */}
      <ellipse cx="65" cy="63" rx="10" ry="21" fill={color} transform="rotate(12 65 63)" />
      {/* Feet */}
      <ellipse cx="31" cy="94" rx="10" ry="5" fill="#F59E0B" />
      <ellipse cx="51" cy="94" rx="10" ry="5" fill="#F59E0B" />
    </svg>
  );
}

// ── Tool call card ───────────────────────────────────────────────────────────
function ToolCall({ tool }) {
  return (
    <div style={{
      margin: "4px 0", padding: "6px 10px",
      background: "rgba(37,99,235,0.05)",
      border: "1px solid rgba(37,99,235,0.15)",
      borderLeft: "3px solid #2563EB",
      borderRadius: "4px",
      fontFamily: "monospace", fontSize: "11px",
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
        <span style={{ color: "#2563EB", fontWeight: 700 }}>⚡ {tool.name}</span>
        <span style={{ marginLeft: "auto", color: "#059669", fontSize: "10px" }}>✓</span>
      </div>
      {tool.result && (
        <div style={{ color: "#6B7280", fontSize: "10px", marginTop: "3px",
          borderTop: "1px solid rgba(37,99,235,0.08)", paddingTop: "3px",
          wordBreak: "break-all" }}>
          → {tool.result.length > 120 ? tool.result.slice(0, 120) + "…" : tool.result}
        </div>
      )}
    </div>
  );
}

// ── Chat message ─────────────────────────────────────────────────────────────
function Message({ msg }) {
  if (msg.role === "user") {
    return (
      <div style={{ display: "flex", justifyContent: "flex-end", margin: "8px 0" }}>
        <div style={{
          maxWidth: "80%", background: "linear-gradient(135deg,#2563EB,#1D4ED8)",
          color: "#fff", padding: "8px 12px", borderRadius: "14px 14px 3px 14px",
          fontSize: "13px", lineHeight: 1.5,
        }}>{msg.text}</div>
      </div>
    );
  }
  return (
    <div style={{ margin: "8px 0", display: "flex", gap: "8px" }}>
      <div style={{
        width: 26, height: 26, borderRadius: "50%",
        background: "#EEF2FF",
        display: "flex", alignItems: "center", justifyContent: "center",
        flexShrink: 0, marginTop: 2,
        border: "1px solid rgba(37,99,235,0.2)",
      }}><PenguinMark size={16} color="#2563EB" glow /></div>
      <div style={{ flex: 1, minWidth: 0 }}>
        {msg.tools && msg.tools.map((t, i) => <ToolCall key={i} tool={t} />)}
        {msg.text_final && (
          <div style={{ marginTop: msg.tools?.length ? "6px" : 0,
            color: "#1A1A2E", fontSize: "13px", lineHeight: 1.6 }}>
            {msg.text_final.split("**").map((p, i) =>
              i % 2 === 1
                ? <strong key={i} style={{ color: "#2563EB" }}>{p}</strong>
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
  const [provider,   setProvider]   = useState("anthropic");
  const [apiKey,     setApiKey]     = useState("");
  const [model,      setModel]      = useState("");
  const [ollamaUrl,  setOllamaUrl]  = useState("http://localhost:11434");
  const [saving,     setSaving]     = useState(false);
  const [msg,        setMsg]        = useState({ type: "", text: "" });
  const [loaded,     setLoaded]     = useState(false);

  useEffect(() => {
    fetch(`${API_BASE}/settings`)
      .then(r => r.json())
      .then(d => {
        setProvider(d.provider   || "anthropic");
        setModel(d.model         || "");
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

  if (!loaded) return <div style={{ color: "#9CA3AF", fontSize: "11px" }}>Loading…</div>;

  const p = PROVIDERS[provider] || PROVIDERS.anthropic;
  const inputStyle = {
    background: "#FFFFFF", border: "1px solid #E5E2DB",
    borderRadius: "6px", padding: "8px 10px", color: "#1A1A2E",
    fontSize: "11px", outline: "none", width: "100%", boxSizing: "border-box",
  };
  const labelStyle = { color: "#6B7280", fontSize: "10px", marginBottom: "3px", display: "block" };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "10px", width: "100%" }}>

      {/* Provider */}
      <div>
        <span style={labelStyle}>AI Provider</span>
        <select value={provider} onChange={e => { setProvider(e.target.value); setMsg({ type: "", text: "" }); }}
          style={{ ...inputStyle, fontFamily: "inherit", cursor: "pointer" }}>
          {Object.entries(PROVIDERS).map(([k, v]) => (
            <option key={k} value={k}>{v.label}</option>
          ))}
        </select>
      </div>

      {/* Description */}
      <div style={{ color: "#9CA3AF", fontSize: "10px", lineHeight: 1.5 }}>{p.description}</div>

      {/* API key (hidden for Ollama) */}
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

      {/* Ollama URL */}
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

      {/* Model override */}
      <div>
        <span style={labelStyle}>
          Model override <span style={{ color: "#D1D5DB" }}>(optional — leave blank for default)</span>
        </span>
        <input
          type="text"
          placeholder={`default: ${provider === "anthropic" ? "claude-haiku-4-5" : provider === "groq" ? "llama-3.3-70b-versatile" : "qwen2.5:7b"}`}
          value={model}
          onChange={e => setModel(e.target.value)}
          style={inputStyle}
        />
      </div>

      {/* Feedback */}
      {msg.text && (
        <div style={{ fontSize: "10px", textAlign: "center", color: msg.type === "ok" ? "#059669" : "#DC2626" }}>
          {msg.text}
        </div>
      )}

      {/* Save */}
      <button onClick={save} disabled={saving} style={{
        background: "linear-gradient(135deg,#2563EB,#1D4ED8)", border: "none",
        borderRadius: "6px", padding: "9px", color: "#fff",
        fontSize: "12px", fontWeight: 700, cursor: "pointer",
      }}>
        {saving ? "Saving…" : "Save & Connect"}
      </button>

      {/* Link */}
      <a href={p.link} target="_blank" rel="noreferrer"
        style={{ color: "#9CA3AF", fontSize: "10px", textAlign: "center", textDecoration: "none" }}>
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
  const [tab, setTab]             = useState("chat");
  const [isTyping, setIsTyping]   = useState(false);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const chatEndRef = useRef(null);

  useEffect(() => { chatEndRef.current?.scrollIntoView({ behavior: "smooth" }); }, [messages, isTyping]);
  useEffect(() => { try { localStorage.setItem("pc_messages", JSON.stringify(messages)); } catch {} }, [messages]);
  useEffect(() => { try { localStorage.setItem("pc_history",  JSON.stringify(history));  } catch {} }, [history]);

  // Poll health
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

  // Poll tools
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
    if (!msg) return;
    setInput("");
    setMessages(p => [...p, { role: "user", text: msg, id: Date.now() }]);
    setIsTyping(true);
    try {
      const r = await fetch(`${API_BASE}/chat`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message: msg, history }),
      });
      const d = await r.json();
      setIsTyping(false);
      setMessages(p => [...p, {
        role: "agent", tools: d.tool_calls || [],
        text_final: d.response || "", id: Date.now() + 1,
      }]);
      const toolSummary = d.tool_calls?.length
        ? "\n[Tools used: " + d.tool_calls.map(t => t.name).join(", ") + "]"
        : "";
      setHistory(p => [...p,
        { role: "user",      content: msg },
        { role: "assistant", content: (d.response || "") + toolSummary },
      ]);
    } catch {
      setIsTyping(false);
      setMessages(p => [...p, { role: "agent", text_final: "Could not reach PenguinClaw server.", id: Date.now() + 1 }]);
    }
  }

  const dot = (ok, unknown) => ({
    width: 7, height: 7, borderRadius: "50%", flexShrink: 0,
    background: unknown ? "#D1D5DB" : ok ? "#059669" : "#DC2626",
  });
  const catColor = c => c === "GH" ? "#9333EA" : c === "Rhino" ? "#F59E0B" : "#2563EB";

  const providerLabel = PROVIDERS[health.provider]?.label?.split(" ")[0] || "AI";

  return (
    <div style={{ position: "fixed", inset: 0, background: "#F7F6F2", display: "flex", flexDirection: "column", fontFamily: "'DM Sans',system-ui,sans-serif", overflow: "hidden" }}>
      <style>{`
        @import url('https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@600;700&family=DM+Sans:wght@400;600;700&family=JetBrains+Mono:wght@400;600&display=swap');
        * { box-sizing: border-box; }
        @keyframes dotBounce { 0%,100%{transform:translateY(0)} 50%{transform:translateY(-3px)} }
        ::-webkit-scrollbar { width: 3px; }
        ::-webkit-scrollbar-thumb { background: rgba(37,99,235,0.2); border-radius: 2px; }
        select, input, textarea { color-scheme: light; }
      `}</style>

      {/* ── Header ── */}
      <div style={{ display: "flex", alignItems: "center", gap: "8px", padding: "8px 12px", borderBottom: "1px solid rgba(37,99,235,0.1)", background: "#FFFFFF", flexShrink: 0 }}>
        <PenguinMark size={20} color="#2563EB" glow />
        <span style={{ fontSize: "13px", fontWeight: 700, color: "#2563EB", letterSpacing: "0.1em", fontFamily: "'Space Grotesk',sans-serif" }}>PENGUINCLAW</span>

        {/* Status dots */}
        <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: "5px" }}>
          <div title="Server"   style={dot(health.fetch_ok, false)} />
          <div title="Rhino"    style={dot(health.rhino_connected, !health.fetch_ok)} />
          <div title="Document" style={dot(health.document_open,   !health.fetch_ok)} />
          <div title={providerLabel} style={dot(health.ai_configured, !health.fetch_ok)} />
        </div>

        <button onClick={() => setSidebarOpen(o => !o)} title="Status" style={{ background: "none", border: "none", color: sidebarOpen ? "#2563EB" : "#9CA3AF", cursor: "pointer", fontSize: "14px", padding: "2px 4px", lineHeight: 1 }}>☰</button>
      </div>

      {/* ── Tabs ── */}
      <div style={{ display: "flex", borderBottom: "1px solid #E5E2DB", background: "#FFFFFF", flexShrink: 0 }}>
        {[
          ["chat",     `💬 Chat`],
          ["tools",    `🔧 Tools (${health.tools_loaded})`],
          ["settings", `⚙ Settings`],
        ].map(([t, label]) => (
          <button key={t} onClick={() => setTab(t)} style={{
            padding: "7px 14px", fontSize: "11px", cursor: "pointer",
            background: "none", border: "none",
            color: tab === t ? "#2563EB" : "#9CA3AF",
            borderBottom: `2px solid ${tab === t ? "#2563EB" : "transparent"}`,
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
                  <PenguinMark size={56} color="#2563EB" glow />
                  <div style={{ color: "#1A1A2E", fontSize: "15px", fontWeight: 800, letterSpacing: "0.12em", fontFamily: "'Space Grotesk',sans-serif" }}>PENGUINCLAW</div>
                  {!health.fetch_ok || health.ai_configured ? (
                    <div style={{ color: "#9CA3AF", fontSize: "11px", textAlign: "center", maxWidth: "200px", lineHeight: 1.6 }}>
                      The AI that actually builds.
                    </div>
                  ) : (
                    <div style={{ width: "240px" }}>
                      <div style={{ color: "#6B7280", fontSize: "11px", textAlign: "center", marginBottom: "12px", lineHeight: 1.5 }}>
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
                    <div style={{ display: "flex", gap: "4px", alignItems: "center", padding: "6px 0" }}>
                      <PenguinMark size={16} color="#2563EB" />
                      {[0,1,2].map(i => <div key={i} style={{ width: 4, height: 4, borderRadius: "50%", background: "#2563EB", animation: `dotBounce 0.8s ease-in-out ${i*0.15}s infinite` }} />)}
                    </div>
                  )}
                  <div ref={chatEndRef} />
                </>
              )}
            </div>

            {/* Input */}
            <div style={{ padding: "8px 10px", borderTop: "1px solid #E5E2DB", background: "#FFFFFF", display: "flex", gap: "6px", alignItems: "flex-end", flexShrink: 0 }}>
              <textarea
                style={{ flex: 1, background: "#F7F6F2", border: "1px solid #E5E2DB", borderRadius: "8px", padding: "8px 10px", color: "#1A1A2E", fontSize: "12px", fontFamily: "inherit", outline: "none", resize: "none", minHeight: "36px", maxHeight: "90px", lineHeight: 1.5 }}
                placeholder="Ask PenguinClaw…"
                value={input}
                onChange={e => setInput(e.target.value)}
                onKeyDown={e => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); sendMessage(); } }}
                rows={1}
              />
              <button onClick={sendMessage} style={{ background: "linear-gradient(135deg,#2563EB,#1D4ED8)", border: "none", borderRadius: "7px", padding: "8px 12px", color: "#fff", cursor: "pointer", fontSize: "14px", fontWeight: 700, flexShrink: 0 }}>↑</button>
              <button onClick={() => { setMessages([]); setHistory([]); }} title="Clear conversation" style={{ background: "none", border: "1px solid #E5E2DB", borderRadius: "7px", padding: "8px 10px", color: "#9CA3AF", cursor: "pointer", fontSize: "12px", flexShrink: 0 }}>✕</button>
            </div>
          </>)}

          {/* Tools tab */}
          {tab === "tools" && (
            <div style={{ flex: 1, overflowY: "auto", padding: "10px" }}>
              {tools.length === 0 ? (
                <div style={{ color: "#9CA3AF", fontSize: "12px", textAlign: "center", marginTop: "40px" }}>No tools loaded yet.</div>
              ) : tools.map(t => {
                const cat = inferCategory(t.category, t.name);
                const c = catColor(cat);
                const isSummary = t.category === "rhino" || t.category === "grasshopper";
                return (
                  <div key={t.name} style={{ padding: "7px 10px", marginBottom: "4px", borderRadius: "6px", background: "#FFFFFF", border: `1px solid ${c}${isSummary ? "33" : "18"}`, borderLeft: isSummary ? `3px solid ${c}` : `1px solid ${c}18` }}>
                    <div style={{ fontSize: "11px", fontFamily: "monospace", color: c, fontWeight: 600 }}>{t.name}</div>
                    {t.description && <div style={{ fontSize: "10px", color: "#9CA3AF", marginTop: "2px", lineHeight: 1.4 }}>{t.description}</div>}
                  </div>
                );
              })}
            </div>
          )}

          {/* Settings tab */}
          {tab === "settings" && (
            <div style={{ flex: 1, overflowY: "auto", padding: "16px" }}>
              <div style={{ color: "#9CA3AF", fontSize: "10px", letterSpacing: "0.1em", textTransform: "uppercase", marginBottom: "12px" }}>AI Provider</div>
              <SettingsForm onSaved={provider => {
                setHealth(p => ({ ...p, ai_configured: true, provider }));
                setTab("chat");
              }} />
            </div>
          )}
        </div>

        {/* ── Sidebar ── */}
        {sidebarOpen && (
          <div style={{ width: "160px", borderLeft: "1px solid #E5E2DB", background: "#FFFFFF", display: "flex", flexDirection: "column", flexShrink: 0, overflow: "hidden" }}>
            <div style={{ padding: "8px", borderBottom: "1px solid #E5E2DB", fontSize: "9px", color: "#9CA3AF", letterSpacing: "0.1em", textTransform: "uppercase" }}>Status</div>
            <div style={{ padding: "8px", fontSize: "9px", lineHeight: 2, color: "#9CA3AF", borderBottom: "1px solid #E5E2DB" }}>
              {[
                ["Server",      health.fetch_ok,         false],
                ["Rhino",       health.rhino_connected,   !health.fetch_ok],
                ["Document",    health.document_open,     !health.fetch_ok],
                [providerLabel, health.ai_configured,     !health.fetch_ok],
              ].map(([label, ok, unknown]) => (
                <div key={label} style={{ display: "flex", alignItems: "center", gap: "5px" }}>
                  <div style={dot(ok, unknown)} />
                  <span style={{ color: unknown ? "#D1D5DB" : ok ? "#059669" : "#DC2626" }}>{label}</span>
                </div>
              ))}
              {health.document && <div style={{ color: "#D1D5DB", marginTop: "4px", wordBreak: "break-all" }}>{health.document}</div>}
            </div>
            <div style={{ padding: "8px 6px", flex: 1, overflowY: "auto", fontSize: "9px" }}>
              <div style={{ color: "#9CA3AF", letterSpacing: "0.1em", textTransform: "uppercase", marginBottom: "6px", padding: "0 2px" }}>Tools</div>
              {tools.map(t => {
                const cat = inferCategory(t.category, t.name);
                return (
                  <div key={t.name} style={{ padding: "3px 6px", marginBottom: "1px", borderRadius: "3px", color: catColor(cat), fontFamily: "monospace", fontSize: "9px", background: `${catColor(cat)}10` }}>
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
