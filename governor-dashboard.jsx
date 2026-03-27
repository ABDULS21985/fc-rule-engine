import { useState, useEffect, useRef } from "react";
import {
  AreaChart, Area, BarChart, Bar, LineChart, Line, PieChart, Pie, Cell,
  XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, RadialBarChart, RadialBar
} from "recharts";
import {
  Shield, AlertTriangle, TrendingUp, TrendingDown, Building2, FileCheck,
  Clock, ChevronRight, Eye, Bell, Activity, Zap, Globe, ArrowUpRight,
  ArrowDownRight, BarChart3, Layers, Target, AlertCircle, CheckCircle2,
  XCircle, Timer, Landmark, Users, Database, Radio, Filter
} from "lucide-react";

// ── Data ──────────────────────────────────────────────────────────────────────
const systemicHealth = [
  { month: "Jul", banking: 87, mfb: 72, insurance: 81, psp: 68, bdc: 74 },
  { month: "Aug", banking: 85, mfb: 74, insurance: 79, psp: 71, bdc: 72 },
  { month: "Sep", banking: 88, mfb: 71, insurance: 83, psp: 73, bdc: 75 },
  { month: "Oct", banking: 84, mfb: 69, insurance: 80, psp: 70, bdc: 71 },
  { month: "Nov", banking: 89, mfb: 76, insurance: 85, psp: 74, bdc: 78 },
  { month: "Dec", banking: 86, mfb: 73, insurance: 82, psp: 72, bdc: 76 },
  { month: "Jan", banking: 91, mfb: 78, insurance: 86, psp: 77, bdc: 79 },
  { month: "Feb", banking: 88, mfb: 75, insurance: 84, psp: 75, bdc: 77 },
  { month: "Mar", banking: 92, mfb: 80, insurance: 88, psp: 79, bdc: 81 },
];

const complianceTrend = [
  { month: "Jul", rate: 78, breaches: 42 },
  { month: "Aug", rate: 80, breaches: 38 },
  { month: "Sep", rate: 82, breaches: 35 },
  { month: "Oct", rate: 79, breaches: 41 },
  { month: "Nov", rate: 84, breaches: 29 },
  { month: "Dec", rate: 83, breaches: 31 },
  { month: "Jan", rate: 87, breaches: 24 },
  { month: "Feb", rate: 86, breaches: 26 },
  { month: "Mar", rate: 89, breaches: 19 },
];

const sectorBreakdown = [
  { name: "DMBs", value: 24, color: "#C9A84C" },
  { name: "MFBs", value: 892, color: "#7B8FA1" },
  { name: "PSPs", value: 156, color: "#5B8A72" },
  { name: "BDCs", value: 5200, color: "#A0785A" },
  { name: "Insurance", value: 62, color: "#8B6FA0" },
  { name: "PMIs", value: 34, color: "#6B8E9B" },
];

const riskDistribution = [
  { name: "Low", value: 62, fill: "#3D6B4F" },
  { name: "Medium", value: 24, fill: "#C9A84C" },
  { name: "High", value: 11, fill: "#B8623E" },
  { name: "Critical", value: 3, fill: "#8B2E2E" },
];

const escalationQueue = [
  { id: "ESC-0041", institution: "First Bank Plc", type: "Capital Adequacy Breach", severity: "critical", age: "2d 4h", sector: "DMB" },
  { id: "ESC-0039", institution: "Sterling MFB", type: "Overdue Statutory Return", severity: "high", age: "3d 12h", sector: "MFB" },
  { id: "ESC-0038", institution: "Paystack Ltd", type: "AML Threshold Alert", severity: "high", age: "1d 8h", sector: "PSP" },
  { id: "ESC-0037", institution: "Heritage Insurance", type: "Solvency Ratio Warning", severity: "medium", age: "4d 2h", sector: "INS" },
  { id: "ESC-0035", institution: "GTBank Plc", type: "FX Position Limit Breach", severity: "critical", age: "6h", sector: "DMB" },
  { id: "ESC-0034", institution: "Kuda MFB", type: "Liquidity Coverage Deviation", severity: "medium", age: "5d 1h", sector: "MFB" },
];

const earlyWarnings = [
  { signal: "NPL ratio acceleration in MFB sector", trend: "up", delta: "+2.3pp", timeframe: "90d", confidence: 87 },
  { signal: "FX liquidity tightening across DMBs", trend: "up", delta: "+₦340B", timeframe: "30d", confidence: 92 },
  { signal: "BDC compliance submission rate declining", trend: "down", delta: "-8.1%", timeframe: "60d", confidence: 78 },
  { signal: "PSP settlement failure rate increasing", trend: "up", delta: "+0.4%", timeframe: "14d", confidence: 84 },
];

const submissionPerformance = [
  { period: "Q1 2025", onTime: 4820, late: 680, missing: 312 },
  { period: "Q2 2025", onTime: 5100, late: 540, missing: 198 },
  { period: "Q3 2025", onTime: 5340, late: 410, missing: 142 },
  { period: "Q4 2025", onTime: 5580, late: 320, missing: 89 },
  { period: "Q1 2026", onTime: 5712, late: 248, missing: 52 },
];

const topExceptions = [
  { institution: "Access Bank", count: 7, trend: "up" },
  { institution: "Stanbic IBTC", count: 5, trend: "down" },
  { institution: "First City MFB", count: 5, trend: "up" },
  { institution: "Flutterwave PSP", count: 4, trend: "stable" },
  { institution: "Wema Bank", count: 4, trend: "down" },
];

// ── Helpers ───────────────────────────────────────────────────────────────────
const fmt = (n) => n >= 1000 ? (n / 1000).toFixed(1) + "k" : n.toString();

const severityColor = {
  critical: { bg: "rgba(139,46,46,0.15)", text: "#D4564A", border: "#8B2E2E" },
  high: { bg: "rgba(184,98,62,0.12)", text: "#D4884A", border: "#B8623E" },
  medium: { bg: "rgba(201,168,76,0.12)", text: "#C9A84C", border: "#A08040" },
  low: { bg: "rgba(61,107,79,0.12)", text: "#5B9B6F", border: "#3D6B4F" },
};

const trendIcon = (t) =>
  t === "up" ? <ArrowUpRight size={13} /> : t === "down" ? <ArrowDownRight size={13} /> : <Activity size={13} />;

// ── Styles ────────────────────────────────────────────────────────────────────
const styles = {
  root: {
    background: "linear-gradient(145deg, #0B0F14 0%, #111820 40%, #0D1218 100%)",
    minHeight: "100vh",
    color: "#C8CDD4",
    fontFamily: "'IBM Plex Sans', 'SF Pro Display', -apple-system, sans-serif",
    fontSize: 13,
  },
  header: {
    background: "linear-gradient(180deg, rgba(201,168,76,0.06) 0%, transparent 100%)",
    borderBottom: "1px solid rgba(201,168,76,0.12)",
    padding: "18px 28px",
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
  },
  headerLeft: { display: "flex", alignItems: "center", gap: 16 },
  crest: {
    width: 38,
    height: 38,
    borderRadius: "50%",
    background: "linear-gradient(135deg, #C9A84C 0%, #A08040 100%)",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    boxShadow: "0 0 20px rgba(201,168,76,0.2)",
  },
  title: {
    fontSize: 18,
    fontWeight: 600,
    color: "#E8E0D0",
    letterSpacing: "-0.3px",
  },
  subtitle: {
    fontSize: 11,
    color: "rgba(201,168,76,0.6)",
    letterSpacing: "1.5px",
    textTransform: "uppercase",
    marginTop: 2,
  },
  headerRight: { display: "flex", alignItems: "center", gap: 16 },
  liveChip: {
    display: "flex",
    alignItems: "center",
    gap: 6,
    padding: "5px 12px",
    borderRadius: 20,
    background: "rgba(61,107,79,0.15)",
    border: "1px solid rgba(91,155,111,0.25)",
    fontSize: 11,
    color: "#5B9B6F",
    fontWeight: 500,
  },
  liveDot: {
    width: 6,
    height: 6,
    borderRadius: "50%",
    background: "#5B9B6F",
    animation: "pulse 2s infinite",
  },
  timestamp: { fontSize: 11, color: "#5A6270" },
  bellBtn: {
    position: "relative",
    background: "rgba(255,255,255,0.04)",
    border: "1px solid rgba(255,255,255,0.06)",
    borderRadius: 8,
    padding: 8,
    cursor: "pointer",
    color: "#7B8494",
    display: "flex",
  },
  bellDot: {
    position: "absolute",
    top: 6,
    right: 6,
    width: 7,
    height: 7,
    borderRadius: "50%",
    background: "#D4564A",
    border: "2px solid #111820",
  },
  grid: {
    display: "grid",
    gridTemplateColumns: "repeat(12, 1fr)",
    gap: 16,
    padding: "20px 28px",
  },
  card: (span = 3) => ({
    gridColumn: `span ${span}`,
    background: "rgba(255,255,255,0.025)",
    border: "1px solid rgba(255,255,255,0.05)",
    borderRadius: 10,
    padding: 18,
    backdropFilter: "blur(12px)",
    transition: "border-color 0.2s",
  }),
  cardHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: 14,
  },
  cardTitle: {
    fontSize: 11,
    textTransform: "uppercase",
    letterSpacing: "1px",
    color: "#6B7280",
    fontWeight: 500,
  },
  kpiValue: {
    fontSize: 32,
    fontWeight: 700,
    color: "#E8E0D0",
    letterSpacing: "-1px",
    lineHeight: 1,
    fontFamily: "'IBM Plex Mono', monospace",
  },
  kpiDelta: (positive) => ({
    display: "inline-flex",
    alignItems: "center",
    gap: 3,
    fontSize: 11,
    fontWeight: 500,
    color: positive ? "#5B9B6F" : "#D4564A",
    padding: "2px 8px",
    borderRadius: 4,
    background: positive ? "rgba(91,155,111,0.1)" : "rgba(212,86,74,0.1)",
    marginLeft: 10,
  }),
  kpiLabel: { fontSize: 12, color: "#6B7280", marginTop: 6 },
  table: { width: "100%", borderCollapse: "separate", borderSpacing: "0 4px" },
  th: {
    fontSize: 10,
    textTransform: "uppercase",
    letterSpacing: "0.8px",
    color: "#4A5260",
    fontWeight: 500,
    textAlign: "left",
    padding: "6px 10px",
  },
  td: {
    padding: "10px 10px",
    fontSize: 12,
    borderTop: "1px solid rgba(255,255,255,0.02)",
  },
  severityBadge: (sev) => ({
    display: "inline-block",
    padding: "3px 8px",
    borderRadius: 4,
    fontSize: 10,
    fontWeight: 600,
    textTransform: "uppercase",
    letterSpacing: "0.5px",
    background: severityColor[sev]?.bg,
    color: severityColor[sev]?.text,
    border: `1px solid ${severityColor[sev]?.border}40`,
  }),
  sectorTag: {
    display: "inline-block",
    padding: "2px 6px",
    borderRadius: 3,
    fontSize: 10,
    fontWeight: 500,
    background: "rgba(255,255,255,0.04)",
    color: "#7B8494",
    border: "1px solid rgba(255,255,255,0.06)",
  },
  warningRow: {
    display: "flex",
    alignItems: "flex-start",
    gap: 10,
    padding: "10px 0",
    borderBottom: "1px solid rgba(255,255,255,0.03)",
  },
  confidenceBar: (pct) => ({
    height: 3,
    borderRadius: 2,
    background: `linear-gradient(90deg, rgba(201,168,76,0.7) ${pct}%, rgba(255,255,255,0.05) ${pct}%)`,
    marginTop: 4,
    width: 60,
  }),
  viewAll: {
    display: "flex",
    alignItems: "center",
    gap: 4,
    fontSize: 11,
    color: "rgba(201,168,76,0.6)",
    cursor: "pointer",
    border: "none",
    background: "none",
    padding: 0,
    fontFamily: "inherit",
  },
};

// ── Custom Tooltip ────────────────────────────────────────────────────────────
const CustomTooltip = ({ active, payload, label }) => {
  if (!active || !payload?.length) return null;
  return (
    <div style={{
      background: "#1A2030",
      border: "1px solid rgba(201,168,76,0.2)",
      borderRadius: 6,
      padding: "10px 14px",
      fontSize: 11,
      boxShadow: "0 8px 24px rgba(0,0,0,0.4)",
    }}>
      <div style={{ color: "#C9A84C", fontWeight: 600, marginBottom: 6 }}>{label}</div>
      {payload.map((p, i) => (
        <div key={i} style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 2 }}>
          <span style={{ width: 8, height: 8, borderRadius: 2, background: p.color, display: "inline-block" }} />
          <span style={{ color: "#8B95A5" }}>{p.dataKey}:</span>
          <span style={{ color: "#E8E0D0", fontWeight: 600, fontFamily: "'IBM Plex Mono', monospace" }}>{p.value}</span>
        </div>
      ))}
    </div>
  );
};

// ── Main Component ────────────────────────────────────────────────────────────
export default function GovernorDashboard() {
  const [now, setNow] = useState(new Date());
  const [activeTab, setActiveTab] = useState("overview");

  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 60000);
    return () => clearInterval(t);
  }, []);

  const totalInstitutions = sectorBreakdown.reduce((a, b) => a + b.value, 0);

  return (
    <div style={styles.root}>
      <style>{`
        @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@300;400;500;600;700&family=IBM+Plex+Mono:wght@400;500;600;700&display=swap');
        @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:0.4} }
        @keyframes fadeIn { from{opacity:0;transform:translateY(6px)} to{opacity:1;transform:translateY(0)} }
        .fade-in { animation: fadeIn 0.4s ease-out forwards; }
        .card-hover:hover { border-color: rgba(201,168,76,0.15) !important; }
        ::-webkit-scrollbar { width:5px }
        ::-webkit-scrollbar-track { background:transparent }
        ::-webkit-scrollbar-thumb { background:rgba(201,168,76,0.15); border-radius:3px }
      `}</style>

      {/* ── Header ──────────────────────────────────────────────────────── */}
      <div style={styles.header}>
        <div style={styles.headerLeft}>
          <div style={styles.crest}><Landmark size={18} color="#111820" /></div>
          <div>
            <div style={styles.title}>Governor's Command Centre</div>
            <div style={styles.subtitle}>RegOS™ — Systemic Oversight</div>
          </div>
        </div>
        <div style={styles.headerRight}>
          <div style={styles.liveChip}>
            <div style={styles.liveDot} />
            LIVE
          </div>
          <div style={styles.timestamp}>
            {now.toLocaleDateString("en-NG", { weekday: "short", day: "numeric", month: "short", year: "numeric" })}
            {" · "}
            {now.toLocaleTimeString("en-NG", { hour: "2-digit", minute: "2-digit" })}
          </div>
          <button style={styles.bellBtn}>
            <Bell size={16} />
            <div style={styles.bellDot} />
          </button>
        </div>
      </div>

      {/* ── KPI Strip ───────────────────────────────────────────────────── */}
      <div style={styles.grid}>
        {[
          { icon: Building2, label: "Regulated Institutions", value: fmt(totalInstitutions), delta: "+38", positive: true, sub: "6 sectors supervised" },
          { icon: Shield, label: "System Compliance Rate", value: "89%", delta: "+3.1pp", positive: true, sub: "vs 85.9% previous quarter" },
          { icon: AlertTriangle, label: "Active Breaches", value: "19", delta: "-7", positive: true, sub: "3 critical · 8 high" },
          { icon: FileCheck, label: "Submissions This Period", value: fmt(6012), delta: "95.1%", positive: true, sub: "on-time filing rate" },
        ].map((kpi, i) => (
          <div key={i} className="card-hover fade-in" style={{ ...styles.card(3), animationDelay: `${i * 80}ms` }}>
            <div style={styles.cardHeader}>
              <span style={styles.cardTitle}>{kpi.label}</span>
              <kpi.icon size={15} color="#C9A84C" strokeWidth={1.5} />
            </div>
            <div style={{ display: "flex", alignItems: "baseline" }}>
              <span style={styles.kpiValue}>{kpi.value}</span>
              <span style={styles.kpiDelta(kpi.positive)}>
                {kpi.positive ? <TrendingUp size={11} /> : <TrendingDown size={11} />}
                {kpi.delta}
              </span>
            </div>
            <div style={styles.kpiLabel}>{kpi.sub}</div>
          </div>
        ))}
      </div>

      {/* ── Row 2: Systemic Health + Compliance Trend ───────────────────── */}
      <div style={styles.grid}>
        <div className="card-hover fade-in" style={styles.card(8)}>
          <div style={styles.cardHeader}>
            <span style={styles.cardTitle}>Systemic Health Index — Sector Overlay</span>
            <button style={styles.viewAll}><Filter size={11} /> Last 9 months <ChevronRight size={12} /></button>
          </div>
          <ResponsiveContainer width="100%" height={220}>
            <AreaChart data={systemicHealth} margin={{ top: 5, right: 5, bottom: 0, left: -20 }}>
              <defs>
                {[
                  ["banking", "#C9A84C"],
                  ["mfb", "#7B8FA1"],
                  ["insurance", "#8B6FA0"],
                  ["psp", "#5B8A72"],
                  ["bdc", "#A0785A"],
                ].map(([id, color]) => (
                  <linearGradient key={id} id={`g_${id}`} x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor={color} stopOpacity={0.25} />
                    <stop offset="100%" stopColor={color} stopOpacity={0} />
                  </linearGradient>
                ))}
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.03)" />
              <XAxis dataKey="month" tick={{ fill: "#4A5260", fontSize: 10 }} axisLine={false} tickLine={false} />
              <YAxis domain={[60, 100]} tick={{ fill: "#4A5260", fontSize: 10 }} axisLine={false} tickLine={false} />
              <Tooltip content={<CustomTooltip />} />
              <Area type="monotone" dataKey="banking" stroke="#C9A84C" fill="url(#g_banking)" strokeWidth={2} dot={false} />
              <Area type="monotone" dataKey="mfb" stroke="#7B8FA1" fill="url(#g_mfb)" strokeWidth={1.5} dot={false} />
              <Area type="monotone" dataKey="insurance" stroke="#8B6FA0" fill="url(#g_insurance)" strokeWidth={1.5} dot={false} />
              <Area type="monotone" dataKey="psp" stroke="#5B8A72" fill="url(#g_psp)" strokeWidth={1.5} dot={false} />
              <Area type="monotone" dataKey="bdc" stroke="#A0785A" fill="url(#g_bdc)" strokeWidth={1.5} dot={false} />
            </AreaChart>
          </ResponsiveContainer>
          <div style={{ display: "flex", gap: 16, marginTop: 8, flexWrap: "wrap" }}>
            {[
              ["DMBs", "#C9A84C"], ["MFBs", "#7B8FA1"], ["Insurance", "#8B6FA0"], ["PSPs", "#5B8A72"], ["BDCs", "#A0785A"]
            ].map(([l, c]) => (
              <div key={l} style={{ display: "flex", alignItems: "center", gap: 5, fontSize: 10, color: "#6B7280" }}>
                <span style={{ width: 10, height: 3, borderRadius: 2, background: c, display: "inline-block" }} />
                {l}
              </div>
            ))}
          </div>
        </div>

        <div className="card-hover fade-in" style={styles.card(4)}>
          <div style={styles.cardHeader}>
            <span style={styles.cardTitle}>Risk Distribution</span>
            <Target size={14} color="#C9A84C" strokeWidth={1.5} />
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <ResponsiveContainer width={130} height={130}>
              <PieChart>
                <Pie data={riskDistribution} cx="50%" cy="50%" innerRadius={38} outerRadius={58} paddingAngle={3} dataKey="value" stroke="none">
                  {riskDistribution.map((entry, index) => (
                    <Cell key={index} fill={entry.fill} />
                  ))}
                </Pie>
              </PieChart>
            </ResponsiveContainer>
            <div style={{ flex: 1 }}>
              {riskDistribution.map((r) => (
                <div key={r.name} style={{ display: "flex", alignItems: "center", justifyContent: "space-between", padding: "5px 0", borderBottom: "1px solid rgba(255,255,255,0.03)" }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                    <span style={{ width: 8, height: 8, borderRadius: 2, background: r.fill, display: "inline-block" }} />
                    <span style={{ fontSize: 11, color: "#8B95A5" }}>{r.name}</span>
                  </div>
                  <span style={{ fontSize: 13, fontWeight: 600, color: "#E8E0D0", fontFamily: "'IBM Plex Mono', monospace" }}>{r.value}%</span>
                </div>
              ))}
            </div>
          </div>
          <div style={{ marginTop: 14, padding: "10px 12px", background: "rgba(139,46,46,0.08)", border: "1px solid rgba(139,46,46,0.15)", borderRadius: 6 }}>
            <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: "0.5px", color: "#D4564A", fontWeight: 600, marginBottom: 4 }}>Attention Required</div>
            <div style={{ fontSize: 11, color: "#9B9FAA" }}>3 institutions in <strong style={{ color: "#D4564A" }}>critical</strong> risk tier — all DMB sector. Governor review pending.</div>
          </div>
        </div>
      </div>

      {/* ── Row 3: Escalation Queue + Early Warnings ────────────────────── */}
      <div style={styles.grid}>
        <div className="card-hover fade-in" style={styles.card(7)}>
          <div style={styles.cardHeader}>
            <span style={styles.cardTitle}>Escalation Queue — Awaiting Governor Action</span>
            <span style={{
              fontSize: 10, fontWeight: 600, color: "#D4564A",
              background: "rgba(212,86,74,0.1)", padding: "3px 8px", borderRadius: 4,
            }}>
              {escalationQueue.length} PENDING
            </span>
          </div>
          <div style={{ maxHeight: 260, overflowY: "auto" }}>
            <table style={styles.table}>
              <thead>
                <tr>
                  <th style={styles.th}>ID</th>
                  <th style={styles.th}>Institution</th>
                  <th style={styles.th}>Exception Type</th>
                  <th style={styles.th}>Severity</th>
                  <th style={styles.th}>Age</th>
                  <th style={styles.th}>Sector</th>
                </tr>
              </thead>
              <tbody>
                {escalationQueue.map((e) => (
                  <tr key={e.id} style={{ cursor: "pointer" }} onMouseOver={(ev) => ev.currentTarget.style.background = "rgba(201,168,76,0.03)"} onMouseOut={(ev) => ev.currentTarget.style.background = "transparent"}>
                    <td style={{ ...styles.td, fontFamily: "'IBM Plex Mono', monospace", color: "#7B8494", fontSize: 11 }}>{e.id}</td>
                    <td style={{ ...styles.td, color: "#C8CDD4", fontWeight: 500 }}>{e.institution}</td>
                    <td style={{ ...styles.td, color: "#8B95A5" }}>{e.type}</td>
                    <td style={styles.td}><span style={styles.severityBadge(e.severity)}>{e.severity}</span></td>
                    <td style={{ ...styles.td, fontFamily: "'IBM Plex Mono', monospace", color: e.severity === "critical" ? "#D4564A" : "#8B95A5", fontSize: 11 }}>{e.age}</td>
                    <td style={styles.td}><span style={styles.sectorTag}>{e.sector}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        <div className="card-hover fade-in" style={styles.card(5)}>
          <div style={styles.cardHeader}>
            <span style={styles.cardTitle}>Early Warning Signals</span>
            <Radio size={14} color="#C9A84C" strokeWidth={1.5} />
          </div>
          {earlyWarnings.map((w, i) => (
            <div key={i} style={styles.warningRow}>
              <div style={{
                marginTop: 2,
                color: w.trend === "up" ? "#D4884A" : "#5B9B6F",
              }}>
                {w.trend === "up" ? <TrendingUp size={14} /> : <TrendingDown size={14} />}
              </div>
              <div style={{ flex: 1 }}>
                <div style={{ fontSize: 12, color: "#C8CDD4", lineHeight: 1.4 }}>{w.signal}</div>
                <div style={{ display: "flex", alignItems: "center", gap: 10, marginTop: 5 }}>
                  <span style={{
                    fontSize: 12, fontWeight: 600, fontFamily: "'IBM Plex Mono', monospace",
                    color: w.trend === "up" ? "#D4884A" : "#5B9B6F",
                  }}>{w.delta}</span>
                  <span style={{ fontSize: 10, color: "#4A5260" }}>over {w.timeframe}</span>
                  <div style={{ display: "flex", alignItems: "center", gap: 4, marginLeft: "auto" }}>
                    <div style={styles.confidenceBar(w.confidence)} />
                    <span style={{ fontSize: 10, color: "#6B7280", fontFamily: "'IBM Plex Mono', monospace" }}>{w.confidence}%</span>
                  </div>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* ── Row 4: Submission Performance + Sector Composition + Top Exceptions */}
      <div style={styles.grid}>
        <div className="card-hover fade-in" style={styles.card(5)}>
          <div style={styles.cardHeader}>
            <span style={styles.cardTitle}>Submission Performance — 5 Quarters</span>
            <FileCheck size={14} color="#C9A84C" strokeWidth={1.5} />
          </div>
          <ResponsiveContainer width="100%" height={190}>
            <BarChart data={submissionPerformance} margin={{ top: 5, right: 5, bottom: 0, left: -20 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.03)" />
              <XAxis dataKey="period" tick={{ fill: "#4A5260", fontSize: 9 }} axisLine={false} tickLine={false} />
              <YAxis tick={{ fill: "#4A5260", fontSize: 10 }} axisLine={false} tickLine={false} />
              <Tooltip content={<CustomTooltip />} />
              <Bar dataKey="onTime" stackId="a" fill="#3D6B4F" radius={[0, 0, 0, 0]} name="On Time" />
              <Bar dataKey="late" stackId="a" fill="#C9A84C" name="Late" />
              <Bar dataKey="missing" stackId="a" fill="#8B2E2E" radius={[3, 3, 0, 0]} name="Missing" />
            </BarChart>
          </ResponsiveContainer>
          <div style={{ display: "flex", gap: 14, marginTop: 6 }}>
            {[["On Time", "#3D6B4F"], ["Late", "#C9A84C"], ["Missing", "#8B2E2E"]].map(([l, c]) => (
              <div key={l} style={{ display: "flex", alignItems: "center", gap: 5, fontSize: 10, color: "#6B7280" }}>
                <span style={{ width: 8, height: 8, borderRadius: 2, background: c, display: "inline-block" }} />
                {l}
              </div>
            ))}
          </div>
        </div>

        <div className="card-hover fade-in" style={styles.card(4)}>
          <div style={styles.cardHeader}>
            <span style={styles.cardTitle}>Sector Composition</span>
            <Layers size={14} color="#C9A84C" strokeWidth={1.5} />
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            {sectorBreakdown.map((s) => {
              const pct = ((s.value / totalInstitutions) * 100).toFixed(1);
              return (
                <div key={s.name}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", marginBottom: 3 }}>
                    <span style={{ fontSize: 11, color: "#8B95A5" }}>{s.name}</span>
                    <span style={{ fontSize: 12, fontWeight: 600, fontFamily: "'IBM Plex Mono', monospace", color: "#C8CDD4" }}>
                      {fmt(s.value)} <span style={{ fontSize: 10, color: "#4A5260", fontWeight: 400 }}>({pct}%)</span>
                    </span>
                  </div>
                  <div style={{ height: 4, borderRadius: 2, background: "rgba(255,255,255,0.04)" }}>
                    <div style={{
                      height: "100%",
                      borderRadius: 2,
                      background: s.color,
                      width: `${Math.min(parseFloat(pct) * 1.2, 100)}%`,
                      opacity: 0.8,
                      transition: "width 0.6s ease",
                    }} />
                  </div>
                </div>
              );
            })}
          </div>
          <div style={{
            marginTop: 14,
            padding: "8px 12px",
            background: "rgba(201,168,76,0.05)",
            border: "1px solid rgba(201,168,76,0.1)",
            borderRadius: 6,
            fontSize: 11,
            color: "#8B95A5",
          }}>
            <strong style={{ color: "#C9A84C" }}>{fmt(totalInstitutions)}</strong> total supervised entities across <strong style={{ color: "#C9A84C" }}>6</strong> regulatory sectors
          </div>
        </div>

        <div className="card-hover fade-in" style={styles.card(3)}>
          <div style={styles.cardHeader}>
            <span style={styles.cardTitle}>Top Exceptions</span>
            <AlertCircle size={14} color="#C9A84C" strokeWidth={1.5} />
          </div>
          {topExceptions.map((e, i) => (
            <div key={i} style={{
              display: "flex", alignItems: "center", justifyContent: "space-between",
              padding: "9px 0",
              borderBottom: i < topExceptions.length - 1 ? "1px solid rgba(255,255,255,0.03)" : "none",
            }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                <span style={{
                  width: 20, height: 20, borderRadius: 4, display: "flex", alignItems: "center", justifyContent: "center",
                  fontSize: 10, fontWeight: 600, fontFamily: "'IBM Plex Mono', monospace",
                  background: i === 0 ? "rgba(201,168,76,0.12)" : "rgba(255,255,255,0.04)",
                  color: i === 0 ? "#C9A84C" : "#6B7280",
                }}>
                  {i + 1}
                </span>
                <span style={{ fontSize: 12, color: "#C8CDD4" }}>{e.institution}</span>
              </div>
              <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                <span style={{
                  fontSize: 14, fontWeight: 700, fontFamily: "'IBM Plex Mono', monospace", color: "#E8E0D0",
                }}>{e.count}</span>
                <span style={{
                  color: e.trend === "up" ? "#D4564A" : e.trend === "down" ? "#5B9B6F" : "#6B7280",
                }}>
                  {trendIcon(e.trend)}
                </span>
              </div>
            </div>
          ))}
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
          Data refresh: Real-time · Last full sync: {now.toLocaleTimeString("en-NG", { hour: "2-digit", minute: "2-digit" })} WAT
        </div>
      </div>
    </div>
  );
}
