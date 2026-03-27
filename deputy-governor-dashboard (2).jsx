import { useState, useEffect } from "react";
import {
  AreaChart, Area, BarChart, Bar, LineChart, Line, PieChart, Pie, Cell,
  XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, ComposedChart,
} from "recharts";
import {
  Shield, AlertTriangle, TrendingUp, TrendingDown, Building2, FileCheck,
  Clock, ChevronRight, ChevronDown, Eye, Bell, Activity, Zap, Globe,
  ArrowUpRight, ArrowDownRight, BarChart3, Layers, Target, AlertCircle,
  CheckCircle2, XCircle, Timer, Landmark, Users, Database, Radio, Filter,
  CreditCard, Scale, Banknote, Search, MoreHorizontal, ExternalLink,
  Briefcase, ArrowRight, Flame, Lock, Unlock, RefreshCw
} from "lucide-react";

// ── Portfolio Data ────────────────────────────────────────────────────────────
const portfolios = {
  banking: {
    label: "Banking Supervision",
    icon: Building2,
    color: "#C9A84C",
    kpis: [
      { label: "DMBs Under Watch", value: "24", delta: "+0", positive: true },
      { label: "CAR Average", value: "16.2%", delta: "+0.4pp", positive: true },
      { label: "NPL Ratio (System)", value: "4.8%", delta: "-0.2pp", positive: true },
      { label: "Open Breaches", value: "7", delta: "-2", positive: true },
    ],
  },
  fx: {
    label: "FX & BDC Operations",
    icon: Banknote,
    color: "#5B8A72",
    kpis: [
      { label: "Licensed BDCs", value: "5,200", delta: "+38", positive: true },
      { label: "FX Compliance Rate", value: "81%", delta: "+2.7pp", positive: true },
      { label: "Position Limit Breaches", value: "14", delta: "+3", positive: false },
      { label: "Pending Reviews", value: "89", delta: "-12", positive: true },
    ],
  },
  aml: {
    label: "AML / CFT / NFIU",
    icon: Lock,
    color: "#B8623E",
    kpis: [
      { label: "STRs This Quarter", value: "1,247", delta: "+18%", positive: false },
      { label: "CTR Filing Rate", value: "94%", delta: "+1.2pp", positive: true },
      { label: "Sanctions Alerts", value: "23", delta: "+5", positive: false },
      { label: "PEP Escalations", value: "8", delta: "+2", positive: false },
    ],
  },
  payments: {
    label: "Payments & FinTech",
    icon: CreditCard,
    color: "#8B6FA0",
    kpis: [
      { label: "Licensed PSPs", value: "156", delta: "+12", positive: true },
      { label: "Settlement SLA", value: "99.2%", delta: "+0.1pp", positive: true },
      { label: "Consumer Complaints", value: "342", delta: "-28", positive: true },
      { label: "Sandbox Participants", value: "18", delta: "+3", positive: true },
    ],
  },
};

// ── Heatmap data: rows = sectors, cols = months ───────────────────────────────
const breachHeatmap = {
  sectors: ["DMBs", "MFBs", "PSPs", "BDCs", "Insurance", "PMIs"],
  months: ["Oct", "Nov", "Dec", "Jan", "Feb", "Mar"],
  data: [
    [4, 3, 5, 2, 3, 1],
    [12, 9, 8, 11, 7, 6],
    [6, 5, 4, 3, 5, 4],
    [18, 14, 16, 12, 10, 8],
    [3, 2, 4, 2, 1, 2],
    [1, 2, 1, 0, 1, 0],
  ],
};

const submissionsByPortfolio = [
  { month: "Oct", banking: 96, fx: 72, aml: 88, payments: 91 },
  { month: "Nov", banking: 94, fx: 75, aml: 90, payments: 89 },
  { month: "Dec", banking: 97, fx: 78, aml: 86, payments: 93 },
  { month: "Jan", banking: 95, fx: 80, aml: 91, payments: 92 },
  { month: "Feb", banking: 98, fx: 82, aml: 93, payments: 94 },
  { month: "Mar", banking: 97, fx: 84, aml: 95, payments: 96 },
];

const unresolvedQueries = [
  { id: "QRY-412", institution: "Zenith Bank", portfolio: "banking", subject: "IFRS 9 provisioning methodology deviation", age: "8d", priority: "high", assignee: "Dir. Examinations" },
  { id: "QRY-408", institution: "Travelex BDC", portfolio: "fx", subject: "Weekly position report format non-compliance", age: "12d", priority: "medium", assignee: "FX Desk" },
  { id: "QRY-405", institution: "OPay", portfolio: "payments", subject: "Transaction volume threshold exceeded without notification", age: "5d", priority: "high", assignee: "FinTech Unit" },
  { id: "QRY-401", institution: "First City MFB", portfolio: "aml", subject: "Missing CTR filings for Q4 2025", age: "18d", priority: "critical", assignee: "NFIU Liaison" },
  { id: "QRY-398", institution: "Access Bank", portfolio: "banking", subject: "Connected lending limit query — board disclosure", age: "6d", priority: "high", assignee: "Dir. BSD" },
  { id: "QRY-394", institution: "Moniepoint", portfolio: "payments", subject: "Agent network KYC compliance gap", age: "14d", priority: "medium", assignee: "FinTech Unit" },
  { id: "QRY-390", institution: "UBA", portfolio: "aml", subject: "Cross-border wire transfer flagging deficiency", age: "21d", priority: "high", assignee: "NFIU Liaison" },
];

const institutionWatchlist = [
  { name: "Heritage Bank", sector: "DMB", riskScore: 38, trigger: "CAR below threshold for 2 consecutive quarters", status: "escalated" },
  { name: "First City MFB", sector: "MFB", riskScore: 42, trigger: "Multiple AML filing failures", status: "under_review" },
  { name: "Travelex BDC", sector: "BDC", riskScore: 51, trigger: "Persistent FX position limit breaches", status: "under_review" },
  { name: "NPF Microfinance", sector: "MFB", riskScore: 55, trigger: "Liquidity ratio deterioration", status: "monitoring" },
  { name: "Flutterwave", sector: "PSP", riskScore: 58, trigger: "Consumer complaint volume spike", status: "monitoring" },
];

const returnCategories = [
  { name: "Prudential Returns", total: 1480, compliant: 1390, rate: 93.9 },
  { name: "AML/CFT Returns", total: 980, compliant: 920, rate: 93.9 },
  { name: "FX Returns", total: 2400, compliant: 1920, rate: 80.0 },
  { name: "Consumer Protection", total: 620, compliant: 580, rate: 93.5 },
  { name: "Open Banking", total: 340, compliant: 310, rate: 91.2 },
];

// ── Helpers ───────────────────────────────────────────────────────────────────
const heatColor = (val) => {
  if (val === 0) return "rgba(255,255,255,0.02)";
  if (val <= 3) return "rgba(61,107,79,0.3)";
  if (val <= 7) return "rgba(201,168,76,0.3)";
  if (val <= 12) return "rgba(184,98,62,0.35)";
  return "rgba(139,46,46,0.4)";
};

const priorityStyle = {
  critical: { bg: "rgba(139,46,46,0.15)", color: "#D4564A", border: "rgba(139,46,46,0.3)" },
  high: { bg: "rgba(184,98,62,0.12)", color: "#D4884A", border: "rgba(184,98,62,0.25)" },
  medium: { bg: "rgba(201,168,76,0.1)", color: "#C9A84C", border: "rgba(201,168,76,0.2)" },
};

const statusStyle = {
  escalated: { label: "ESCALATED", bg: "rgba(139,46,46,0.15)", color: "#D4564A" },
  under_review: { label: "REVIEW", bg: "rgba(184,98,62,0.12)", color: "#D4884A" },
  monitoring: { label: "MONITOR", bg: "rgba(201,168,76,0.1)", color: "#C9A84C" },
};

const s = {
  root: {
    background: "linear-gradient(145deg, #0B0F14 0%, #111820 40%, #0D1218 100%)",
    minHeight: "100vh",
    color: "#C8CDD4",
    fontFamily: "'IBM Plex Sans', 'SF Pro Display', -apple-system, sans-serif",
    fontSize: 13,
  },
  header: {
    background: "linear-gradient(180deg, rgba(91,138,114,0.06) 0%, transparent 100%)",
    borderBottom: "1px solid rgba(91,138,114,0.12)",
    padding: "18px 28px",
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
  },
  grid: {
    display: "grid",
    gridTemplateColumns: "repeat(12, 1fr)",
    gap: 16,
    padding: "0 28px 16px",
  },
  card: (span) => ({
    gridColumn: `span ${span}`,
    background: "rgba(255,255,255,0.025)",
    border: "1px solid rgba(255,255,255,0.05)",
    borderRadius: 10,
    padding: 18,
    backdropFilter: "blur(12px)",
    transition: "border-color 0.2s",
  }),
  cardTitle: {
    fontSize: 11,
    textTransform: "uppercase",
    letterSpacing: "1px",
    color: "#6B7280",
    fontWeight: 500,
  },
  th: {
    fontSize: 10, textTransform: "uppercase", letterSpacing: "0.8px",
    color: "#4A5260", fontWeight: 500, textAlign: "left", padding: "6px 8px",
  },
  td: { padding: "9px 8px", fontSize: 12, borderTop: "1px solid rgba(255,255,255,0.02)" },
  mono: { fontFamily: "'IBM Plex Mono', monospace" },
};

const Tip = ({ active, payload, label }) => {
  if (!active || !payload?.length) return null;
  return (
    <div style={{ background: "#1A2030", border: "1px solid rgba(201,168,76,0.2)", borderRadius: 6, padding: "10px 14px", fontSize: 11, boxShadow: "0 8px 24px rgba(0,0,0,0.4)" }}>
      <div style={{ color: "#C9A84C", fontWeight: 600, marginBottom: 6 }}>{label}</div>
      {payload.map((p, i) => (
        <div key={i} style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 2 }}>
          <span style={{ width: 8, height: 8, borderRadius: 2, background: p.color, display: "inline-block" }} />
          <span style={{ color: "#8B95A5" }}>{p.dataKey}:</span>
          <span style={{ color: "#E8E0D0", fontWeight: 600, ...s.mono }}>{p.value}%</span>
        </div>
      ))}
    </div>
  );
};

// ── Component ─────────────────────────────────────────────────────────────────
export default function DeputyGovernorDashboard() {
  const [activePortfolio, setActivePortfolio] = useState("banking");
  const [now, setNow] = useState(new Date());

  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 60000);
    return () => clearInterval(t);
  }, []);

  const portfolio = portfolios[activePortfolio];

  return (
    <div style={s.root}>
      <style>{`
        @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@300;400;500;600;700&family=IBM+Plex+Mono:wght@400;500;600;700&display=swap');
        @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:0.4} }
        @keyframes fadeIn { from{opacity:0;transform:translateY(6px)} to{opacity:1;transform:translateY(0)} }
        .fade-in { animation: fadeIn 0.35s ease-out forwards; }
        .card-hover:hover { border-color: rgba(91,138,114,0.2) !important; }
        ::-webkit-scrollbar { width:5px }
        ::-webkit-scrollbar-track { background:transparent }
        ::-webkit-scrollbar-thumb { background:rgba(91,138,114,0.15); border-radius:3px }
      `}</style>

      {/* ── Header ──────────────────────────────────────────────────────── */}
      <div style={s.header}>
        <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
          <div style={{
            width: 38, height: 38, borderRadius: "50%",
            background: "linear-gradient(135deg, #5B8A72 0%, #3D6B4F 100%)",
            display: "flex", alignItems: "center", justifyContent: "center",
            boxShadow: "0 0 20px rgba(91,138,114,0.2)",
          }}>
            <Scale size={18} color="#111820" />
          </div>
          <div>
            <div style={{ fontSize: 18, fontWeight: 600, color: "#E8E0D0", letterSpacing: "-0.3px" }}>
              Deputy Governor — Portfolio Oversight
            </div>
            <div style={{ fontSize: 11, color: "rgba(91,138,114,0.7)", letterSpacing: "1.5px", textTransform: "uppercase", marginTop: 2 }}>
              RegOS™ — Supervisory Intelligence
            </div>
          </div>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
          <div style={{
            display: "flex", alignItems: "center", gap: 6, padding: "5px 12px", borderRadius: 20,
            background: "rgba(61,107,79,0.15)", border: "1px solid rgba(91,155,111,0.25)",
            fontSize: 11, color: "#5B9B6F", fontWeight: 500,
          }}>
            <div style={{ width: 6, height: 6, borderRadius: "50%", background: "#5B9B6F", animation: "pulse 2s infinite" }} />
            LIVE
          </div>
          <div style={{ fontSize: 11, color: "#5A6270" }}>
            {now.toLocaleDateString("en-NG", { weekday: "short", day: "numeric", month: "short", year: "numeric" })}
          </div>
        </div>
      </div>

      {/* ── Portfolio Tabs ──────────────────────────────────────────────── */}
      <div style={{ padding: "16px 28px 0", display: "flex", gap: 8 }}>
        {Object.entries(portfolios).map(([key, p]) => {
          const active = key === activePortfolio;
          const Icon = p.icon;
          return (
            <button
              key={key}
              onClick={() => setActivePortfolio(key)}
              style={{
                display: "flex", alignItems: "center", gap: 7,
                padding: "9px 18px", borderRadius: 8, cursor: "pointer",
                border: active ? `1px solid ${p.color}40` : "1px solid rgba(255,255,255,0.05)",
                background: active ? `${p.color}12` : "rgba(255,255,255,0.02)",
                color: active ? p.color : "#6B7280",
                fontSize: 12, fontWeight: active ? 600 : 400,
                fontFamily: "'IBM Plex Sans', sans-serif",
                transition: "all 0.2s",
              }}
            >
              <Icon size={14} />
              {p.label}
            </button>
          );
        })}
      </div>

      {/* ── Portfolio KPIs ──────────────────────────────────────────────── */}
      <div style={{ ...s.grid, paddingTop: 16 }}>
        {portfolio.kpis.map((kpi, i) => (
          <div key={i} className="card-hover fade-in" style={{ ...s.card(3), animationDelay: `${i * 60}ms` }}>
            <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: "1px", color: "#5A6270", marginBottom: 10 }}>{kpi.label}</div>
            <div style={{ display: "flex", alignItems: "baseline" }}>
              <span style={{ fontSize: 28, fontWeight: 700, color: "#E8E0D0", letterSpacing: "-1px", ...s.mono }}>{kpi.value}</span>
              <span style={{
                display: "inline-flex", alignItems: "center", gap: 3, fontSize: 11, fontWeight: 500,
                color: kpi.positive ? "#5B9B6F" : "#D4564A",
                padding: "2px 8px", borderRadius: 4, marginLeft: 10,
                background: kpi.positive ? "rgba(91,155,111,0.1)" : "rgba(212,86,74,0.1)",
              }}>
                {kpi.positive ? <TrendingUp size={11} /> : <TrendingDown size={11} />}
                {kpi.delta}
              </span>
            </div>
          </div>
        ))}
      </div>

      {/* ── Row 2: Submission Rate + Breach Heatmap ─────────────────────── */}
      <div style={s.grid}>
        <div className="card-hover fade-in" style={s.card(7)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={s.cardTitle}>On-Time Submission Rate by Portfolio</span>
            <span style={{ fontSize: 10, color: "#4A5260" }}>6-month trend (%)</span>
          </div>
          <ResponsiveContainer width="100%" height={210}>
            <LineChart data={submissionsByPortfolio} margin={{ top: 5, right: 5, bottom: 0, left: -20 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.03)" />
              <XAxis dataKey="month" tick={{ fill: "#4A5260", fontSize: 10 }} axisLine={false} tickLine={false} />
              <YAxis domain={[65, 100]} tick={{ fill: "#4A5260", fontSize: 10 }} axisLine={false} tickLine={false} />
              <Tooltip content={<Tip />} />
              <Line type="monotone" dataKey="banking" stroke="#C9A84C" strokeWidth={2} dot={{ r: 3, fill: "#C9A84C" }} />
              <Line type="monotone" dataKey="fx" stroke="#5B8A72" strokeWidth={2} dot={{ r: 3, fill: "#5B8A72" }} />
              <Line type="monotone" dataKey="aml" stroke="#B8623E" strokeWidth={2} dot={{ r: 3, fill: "#B8623E" }} />
              <Line type="monotone" dataKey="payments" stroke="#8B6FA0" strokeWidth={2} dot={{ r: 3, fill: "#8B6FA0" }} />
            </LineChart>
          </ResponsiveContainer>
          <div style={{ display: "flex", gap: 16, marginTop: 8 }}>
            {[["Banking", "#C9A84C"], ["FX/BDC", "#5B8A72"], ["AML/CFT", "#B8623E"], ["Payments", "#8B6FA0"]].map(([l, c]) => (
              <div key={l} style={{ display: "flex", alignItems: "center", gap: 5, fontSize: 10, color: "#6B7280" }}>
                <span style={{ width: 12, height: 3, borderRadius: 2, background: c, display: "inline-block" }} />{l}
              </div>
            ))}
          </div>
        </div>

        <div className="card-hover fade-in" style={s.card(5)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={s.cardTitle}>Breach Heatmap — Sector × Month</span>
            <Flame size={14} color="#B8623E" strokeWidth={1.5} />
          </div>
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "separate", borderSpacing: 3 }}>
              <thead>
                <tr>
                  <th style={{ ...s.th, minWidth: 60 }}></th>
                  {breachHeatmap.months.map((m) => <th key={m} style={{ ...s.th, textAlign: "center" }}>{m}</th>)}
                </tr>
              </thead>
              <tbody>
                {breachHeatmap.sectors.map((sector, ri) => (
                  <tr key={sector}>
                    <td style={{ fontSize: 10, color: "#7B8494", padding: "4px 6px", fontWeight: 500 }}>{sector}</td>
                    {breachHeatmap.data[ri].map((val, ci) => (
                      <td key={ci} style={{
                        textAlign: "center",
                        padding: 6,
                        background: heatColor(val),
                        borderRadius: 4,
                        fontSize: 11,
                        fontWeight: 600,
                        color: val === 0 ? "#3A4050" : val <= 3 ? "#5B9B6F" : val <= 7 ? "#C9A84C" : val <= 12 ? "#D4884A" : "#D4564A",
                        ...s.mono,
                      }}>
                        {val}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div style={{ display: "flex", gap: 10, marginTop: 10, justifyContent: "center" }}>
            {[["0", "rgba(255,255,255,0.04)"], ["1–3", "rgba(61,107,79,0.3)"], ["4–7", "rgba(201,168,76,0.3)"], ["8–12", "rgba(184,98,62,0.35)"], ["13+", "rgba(139,46,46,0.4)"]].map(([l, c]) => (
              <div key={l} style={{ display: "flex", alignItems: "center", gap: 4, fontSize: 9, color: "#5A6270" }}>
                <span style={{ width: 12, height: 12, borderRadius: 3, background: c, display: "inline-block" }} />{l}
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* ── Row 3: Unresolved Queries ──────────────────────────────────── */}
      <div style={s.grid}>
        <div className="card-hover fade-in" style={s.card(12)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={s.cardTitle}>Unresolved Regulator Queries</span>
            <span style={{
              fontSize: 10, fontWeight: 600, color: "#D4884A",
              background: "rgba(184,98,62,0.1)", padding: "3px 10px", borderRadius: 4,
            }}>{unresolvedQueries.length} OPEN</span>
          </div>
          <div style={{ maxHeight: 240, overflowY: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 3px" }}>
              <thead>
                <tr>
                  {["ID", "Institution", "Subject", "Priority", "Age", "Assigned To"].map((h) => (
                    <th key={h} style={s.th}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {unresolvedQueries.map((q) => (
                  <tr key={q.id} style={{ cursor: "pointer" }}
                    onMouseOver={(e) => e.currentTarget.style.background = "rgba(91,138,114,0.03)"}
                    onMouseOut={(e) => e.currentTarget.style.background = "transparent"}
                  >
                    <td style={{ ...s.td, ...s.mono, color: "#6B7280", fontSize: 11 }}>{q.id}</td>
                    <td style={{ ...s.td, color: "#C8CDD4", fontWeight: 500 }}>{q.institution}</td>
                    <td style={{ ...s.td, color: "#8B95A5", maxWidth: 300, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{q.subject}</td>
                    <td style={s.td}>
                      <span style={{
                        display: "inline-block", padding: "3px 8px", borderRadius: 4,
                        fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.5px",
                        background: priorityStyle[q.priority].bg,
                        color: priorityStyle[q.priority].color,
                        border: `1px solid ${priorityStyle[q.priority].border}`,
                      }}>{q.priority}</span>
                    </td>
                    <td style={{
                      ...s.td, ...s.mono, fontSize: 11,
                      color: parseInt(q.age) >= 14 ? "#D4564A" : parseInt(q.age) >= 7 ? "#D4884A" : "#8B95A5",
                    }}>{q.age}</td>
                    <td style={{ ...s.td, color: "#7B8494", fontSize: 11 }}>{q.assignee}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {/* ── Row 4: Watchlist + Return Categories ───────────────────────── */}
      <div style={s.grid}>
        <div className="card-hover fade-in" style={s.card(6)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={s.cardTitle}>Institution Watchlist</span>
            <Eye size={14} color="#D4884A" strokeWidth={1.5} />
          </div>
          {institutionWatchlist.map((inst, i) => (
            <div key={i} style={{
              display: "flex", alignItems: "center", gap: 12, padding: "10px 0",
              borderBottom: i < institutionWatchlist.length - 1 ? "1px solid rgba(255,255,255,0.03)" : "none",
            }}>
              <div style={{
                width: 36, height: 36, borderRadius: 8, display: "flex", alignItems: "center", justifyContent: "center",
                background: inst.riskScore < 45 ? "rgba(139,46,46,0.12)" : inst.riskScore < 55 ? "rgba(184,98,62,0.1)" : "rgba(201,168,76,0.08)",
                ...s.mono, fontSize: 13, fontWeight: 700,
                color: inst.riskScore < 45 ? "#D4564A" : inst.riskScore < 55 ? "#D4884A" : "#C9A84C",
              }}>
                {inst.riskScore}
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <span style={{ fontSize: 12, color: "#C8CDD4", fontWeight: 500 }}>{inst.name}</span>
                  <span style={{
                    fontSize: 9, padding: "2px 5px", borderRadius: 3, fontWeight: 500,
                    background: "rgba(255,255,255,0.04)", color: "#6B7280", border: "1px solid rgba(255,255,255,0.06)",
                  }}>{inst.sector}</span>
                </div>
                <div style={{ fontSize: 11, color: "#5A6270", marginTop: 2, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{inst.trigger}</div>
              </div>
              <span style={{
                fontSize: 9, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.5px",
                padding: "3px 7px", borderRadius: 4,
                background: statusStyle[inst.status].bg,
                color: statusStyle[inst.status].color,
              }}>{statusStyle[inst.status].label}</span>
            </div>
          ))}
        </div>

        <div className="card-hover fade-in" style={s.card(6)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={s.cardTitle}>Return Category Performance</span>
            <Database size={14} color="#5B8A72" strokeWidth={1.5} />
          </div>
          {returnCategories.map((cat, i) => (
            <div key={i} style={{ marginBottom: 14 }}>
              <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 4 }}>
                <span style={{ fontSize: 11, color: "#8B95A5" }}>{cat.name}</span>
                <div style={{ display: "flex", alignItems: "baseline", gap: 6 }}>
                  <span style={{ ...s.mono, fontSize: 12, fontWeight: 600, color: cat.rate >= 90 ? "#5B9B6F" : cat.rate >= 80 ? "#C9A84C" : "#D4564A" }}>
                    {cat.rate}%
                  </span>
                  <span style={{ fontSize: 10, color: "#4A5260" }}>{cat.compliant}/{cat.total}</span>
                </div>
              </div>
              <div style={{ height: 6, borderRadius: 3, background: "rgba(255,255,255,0.04)", overflow: "hidden" }}>
                <div style={{
                  height: "100%", borderRadius: 3, transition: "width 0.6s ease",
                  width: `${cat.rate}%`,
                  background: cat.rate >= 90
                    ? "linear-gradient(90deg, #3D6B4F, #5B9B6F)"
                    : cat.rate >= 80
                      ? "linear-gradient(90deg, #A08040, #C9A84C)"
                      : "linear-gradient(90deg, #8B2E2E, #D4564A)",
                }} />
              </div>
            </div>
          ))}
          <div style={{
            marginTop: 8, padding: "10px 12px", borderRadius: 6,
            background: "rgba(91,138,114,0.06)", border: "1px solid rgba(91,138,114,0.12)",
            fontSize: 11, color: "#8B95A5",
          }}>
            FX Returns at <strong style={{ color: "#C9A84C" }}>80%</strong> compliance — lowest category. BDC sector accounts for 68% of late filings.
          </div>
        </div>
      </div>

      {/* ── Footer ──────────────────────────────────────────────────────── */}
      <div style={{
        padding: "14px 28px",
        borderTop: "1px solid rgba(255,255,255,0.03)",
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
      }}>
        <div style={{ fontSize: 10, color: "#3A4050" }}>
          RegOS™ v3.2.0 · Regulatory & SupTech Platform · Central Bank of Nigeria
        </div>
        <div style={{ fontSize: 10, color: "#3A4050" }}>
          Portfolio: {portfolio.label} · Last sync: {now.toLocaleTimeString("en-NG", { hour: "2-digit", minute: "2-digit" })} WAT
        </div>
      </div>
    </div>
  );
}
