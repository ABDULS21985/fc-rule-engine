import { useState, useEffect, useMemo } from "react";
import {
  AreaChart, Area, BarChart, Bar, LineChart, Line, PieChart, Pie, Cell,
  XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, RadarChart,
  PolarGrid, PolarAngleAxis, Radar,
} from "recharts";
import {
  Shield, AlertTriangle, TrendingUp, TrendingDown, Building2, FileCheck,
  Clock, ChevronRight, Eye, Bell, Activity,
  ArrowUpRight, ArrowDownRight, Target, AlertCircle,
  CheckCircle2, XCircle, Timer, Calendar, FileText,
  Send, BarChart3, Award, Users, Gauge, CircleDot,
  MessageSquare, Upload, ClipboardList, Percent, Star
} from "lucide-react";

// ── Institution Profile ───────────────────────────────────────────────────────
const institution = {
  name: "Zenith Bank Plc",
  sector: "Deposit Money Bank",
  license: "DMB/2003/001",
  riskTier: "Low",
  complianceScore: 97,
  rank: 2,
  totalPeers: 24,
};

// ── Filing Posture ────────────────────────────────────────────────────────────
const filingPosture = [
  { month: "Oct", submitted: 14, onTime: 13, late: 1, rejected: 0 },
  { month: "Nov", submitted: 12, onTime: 12, late: 0, rejected: 0 },
  { month: "Dec", submitted: 16, onTime: 15, late: 1, rejected: 0 },
  { month: "Jan", submitted: 14, onTime: 14, late: 0, rejected: 0 },
  { month: "Feb", submitted: 12, onTime: 12, late: 0, rejected: 0 },
  { month: "Mar", submitted: 8, onTime: 8, late: 0, rejected: 0 },
];

const returnStatus = [
  { name: "Monthly Prudential Return", due: "Mar 15, 2026", status: "submitted", daysLeft: 3 },
  { name: "FX Net Open Position", due: "Mar 14, 2026", status: "submitted", daysLeft: 2 },
  { name: "AML/CFT Quarterly Report", due: "Mar 31, 2026", status: "in_progress", daysLeft: 19 },
  { name: "IFRS 9 Impairment Schedule", due: "Mar 31, 2026", status: "in_progress", daysLeft: 19 },
  { name: "Capital Adequacy Computation", due: "Apr 15, 2026", status: "not_started", daysLeft: 34 },
  { name: "Consumer Complaint Summary", due: "Apr 15, 2026", status: "not_started", daysLeft: 34 },
  { name: "Liquidity Coverage Ratio", due: "Apr 30, 2026", status: "not_started", daysLeft: 49 },
  { name: "Board Governance Return", due: "Jun 30, 2026", status: "not_started", daysLeft: 110 },
];

const rejectedReturns = [
  { id: "SUB-8710", name: "FX Position Report — January", rejectedOn: "Feb 8, 2026", reason: "Net position calculation error in Column G", resubmitted: true },
  { id: "SUB-8542", name: "AML Quarterly — Q3 2025", rejectedOn: "Nov 12, 2025", reason: "Missing Board sign-off attachment", resubmitted: true },
  { id: "SUB-8301", name: "Stress Test Results — H1 2025", rejectedOn: "Aug 22, 2025", reason: "Scenario parameters did not match CBN specification", resubmitted: true },
];

// ── Peer Benchmarks ───────────────────────────────────────────────────────────
const peerBenchmark = [
  { metric: "On-Time Filing", you: 98, peerAvg: 89, peerBest: 100 },
  { metric: "Validation Pass Rate", you: 96, peerAvg: 82, peerBest: 99 },
  { metric: "Query Response Time", you: 92, peerAvg: 78, peerBest: 95 },
  { metric: "Breach Frequency", you: 95, peerAvg: 74, peerBest: 98 },
  { metric: "Data Quality Score", you: 94, peerAvg: 80, peerBest: 97 },
  { metric: "Regulatory Engagement", you: 90, peerAvg: 76, peerBest: 94 },
];

const radarData = peerBenchmark.map((p) => ({
  metric: p.metric.split(" ").slice(0, 2).join(" "),
  you: p.you,
  peerAvg: p.peerAvg,
}));

const peerRanking = [
  { rank: 1, name: "Access Bank", score: 99 },
  { rank: 2, name: "Zenith Bank", score: 97, isYou: true },
  { rank: 3, name: "GTBank", score: 94 },
  { rank: 4, name: "UBA", score: 91 },
  { rank: 5, name: "First Bank", score: 88 },
  { rank: 6, name: "Stanbic IBTC", score: 86 },
  { rank: 7, name: "Fidelity Bank", score: 84 },
  { rank: 8, name: "Sterling Bank", score: 72 },
];

// ── Unresolved Queries ────────────────────────────────────────────────────────
const unresolvedQueries = [
  { id: "QRY-398", from: "Dir. BSD", subject: "Connected lending limit — board disclosure clarification", received: "Mar 6, 2026", age: "6d", priority: "high" },
  { id: "QRY-372", from: "AML Unit", subject: "Enhanced due diligence on flagged PEP accounts", received: "Feb 24, 2026", age: "16d", priority: "medium" },
  { id: "QRY-361", from: "Dir. Examinations", subject: "Off-balance sheet exposure reconciliation", received: "Feb 18, 2026", age: "22d", priority: "high" },
];

const complianceTimeline = [
  { month: "Apr 2025", score: 92 },
  { month: "May", score: 93 },
  { month: "Jun", score: 91 },
  { month: "Jul", score: 94 },
  { month: "Aug", score: 93 },
  { month: "Sep", score: 95 },
  { month: "Oct", score: 96 },
  { month: "Nov", score: 95 },
  { month: "Dec", score: 96 },
  { month: "Jan 2026", score: 97 },
  { month: "Feb", score: 96 },
  { month: "Mar", score: 97 },
];

// ── Helpers ───────────────────────────────────────────────────────────────────
const statusDef = {
  submitted: { label: "SUBMITTED", bg: "rgba(61,107,79,0.12)", color: "#5B9B6F", icon: CheckCircle2 },
  in_progress: { label: "IN PROGRESS", bg: "rgba(201,168,76,0.12)", color: "#C9A84C", icon: Clock },
  not_started: { label: "NOT STARTED", bg: "rgba(255,255,255,0.04)", color: "#5A6270", icon: CircleDot },
};

const priorityDef = {
  high: { bg: "rgba(184,98,62,0.12)", color: "#D4884A" },
  medium: { bg: "rgba(201,168,76,0.1)", color: "#C9A84C" },
};

const urgencyColor = (days) => days <= 3 ? "#D4564A" : days <= 14 ? "#D4884A" : days <= 30 ? "#C9A84C" : "#5B9B6F";
const mono = { fontFamily: "'IBM Plex Mono', monospace" };

const Tip = ({ active, payload, label }) => {
  if (!active || !payload?.length) return null;
  return (
    <div style={{ background: "#1A2030", border: "1px solid rgba(166,139,97,0.25)", borderRadius: 6, padding: "10px 14px", fontSize: 11, boxShadow: "0 8px 24px rgba(0,0,0,0.4)" }}>
      <div style={{ color: "#A68B61", fontWeight: 600, marginBottom: 6 }}>{label}</div>
      {payload.map((p, i) => (
        <div key={i} style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 2 }}>
          <span style={{ width: 8, height: 8, borderRadius: 2, background: p.color || p.stroke, display: "inline-block" }} />
          <span style={{ color: "#8B95A5" }}>{p.name || p.dataKey}:</span>
          <span style={{ color: "#E8E0D0", fontWeight: 600, ...mono }}>{p.value}</span>
        </div>
      ))}
    </div>
  );
};

// ── Component ─────────────────────────────────────────────────────────────────
export default function InstitutionExecutiveDashboard() {
  const [now, setNow] = useState(new Date());

  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 60000);
    return () => clearInterval(t);
  }, []);

  const totalFiled = filingPosture.reduce((a, b) => a + b.submitted, 0);
  const totalOnTime = filingPosture.reduce((a, b) => a + b.onTime, 0);
  const onTimeRate = ((totalOnTime / totalFiled) * 100).toFixed(1);
  const upcomingDeadlines = returnStatus.filter((r) => r.status !== "submitted").length;
  const accent = "#A68B61";

  const cs = {
    root: {
      background: "linear-gradient(145deg, #0C0E12 0%, #12151C 40%, #0E1117 100%)",
      minHeight: "100vh", color: "#C8CDD4",
      fontFamily: "'IBM Plex Sans', 'SF Pro Display', -apple-system, sans-serif", fontSize: 13,
    },
    grid: { display: "grid", gridTemplateColumns: "repeat(12, 1fr)", gap: 16, padding: "0 28px 16px" },
    card: (span) => ({
      gridColumn: `span ${span}`, background: "rgba(255,255,255,0.025)",
      border: "1px solid rgba(255,255,255,0.05)", borderRadius: 10, padding: 18,
      backdropFilter: "blur(12px)", transition: "border-color 0.2s",
    }),
    cardTitle: { fontSize: 11, textTransform: "uppercase", letterSpacing: "1px", color: "#6B7280", fontWeight: 500 },
    th: { fontSize: 10, textTransform: "uppercase", letterSpacing: "0.8px", color: "#4A5260", fontWeight: 500, textAlign: "left", padding: "6px 8px" },
    td: { padding: "9px 8px", fontSize: 12, borderTop: "1px solid rgba(255,255,255,0.02)" },
  };

  return (
    <div style={cs.root}>
      <style>{`
        @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@300;400;500;600;700&family=IBM+Plex+Mono:wght@400;500;600;700&display=swap');
        @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:0.4} }
        @keyframes fadeIn { from{opacity:0;transform:translateY(6px)} to{opacity:1;transform:translateY(0)} }
        .fade-in { animation: fadeIn 0.35s ease-out forwards; }
        .card-hover:hover { border-color: rgba(166,139,97,0.2) !important; }
        ::-webkit-scrollbar{width:5px}::-webkit-scrollbar-track{background:transparent}::-webkit-scrollbar-thumb{background:rgba(166,139,97,0.15);border-radius:3px}
      `}</style>

      {/* ── Header ──────────────────────────────────────────────────────── */}
      <div style={{
        background: "linear-gradient(180deg, rgba(166,139,97,0.06) 0%, transparent 100%)",
        borderBottom: "1px solid rgba(166,139,97,0.12)", padding: "18px 28px",
        display: "flex", alignItems: "center", justifyContent: "space-between",
      }}>
        <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
          <div style={{
            width: 38, height: 38, borderRadius: "50%",
            background: "linear-gradient(135deg, #A68B61 0%, #8A7350 100%)",
            display: "flex", alignItems: "center", justifyContent: "center",
            boxShadow: "0 0 20px rgba(166,139,97,0.2)",
          }}><Building2 size={18} color="#111820" /></div>
          <div>
            <div style={{ fontSize: 18, fontWeight: 600, color: "#E8E0D0", letterSpacing: "-0.3px" }}>
              {institution.name}
            </div>
            <div style={{ fontSize: 11, color: "rgba(166,139,97,0.7)", letterSpacing: "1.5px", textTransform: "uppercase", marginTop: 2 }}>
              RegOS™ — Institutional Compliance Portal
            </div>
          </div>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
          <div style={{
            display: "flex", alignItems: "center", gap: 8, padding: "6px 14px", borderRadius: 8,
            background: "rgba(166,139,97,0.08)", border: "1px solid rgba(166,139,97,0.15)",
          }}>
            <span style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: "0.8px", color: "#6B7280" }}>License:</span>
            <span style={{ ...mono, fontSize: 11, color: accent, fontWeight: 500 }}>{institution.license}</span>
          </div>
          <div style={{
            display: "flex", alignItems: "center", gap: 8, padding: "6px 14px", borderRadius: 8,
            background: "rgba(61,107,79,0.1)", border: "1px solid rgba(91,155,111,0.15)",
          }}>
            <span style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: "0.8px", color: "#6B7280" }}>Risk Tier:</span>
            <span style={{ fontSize: 11, color: "#5B9B6F", fontWeight: 600 }}>{institution.riskTier}</span>
          </div>
          <div style={{ fontSize: 11, color: "#5A6270" }}>
            {now.toLocaleDateString("en-NG", { weekday: "short", day: "numeric", month: "short", year: "numeric" })}
          </div>
        </div>
      </div>

      {/* ── Scorecard Strip ─────────────────────────────────────────────── */}
      <div style={{ ...cs.grid, paddingTop: 20 }}>
        {[
          { icon: Award, label: "Compliance Score", value: `${institution.complianceScore}`, sub: `Rank #${institution.rank} of ${institution.totalPeers} DMBs`, color: "#5B9B6F" },
          { icon: FileCheck, label: "On-Time Filing Rate", value: `${onTimeRate}%`, sub: `${totalOnTime}/${totalFiled} returns this half`, color: accent },
          { icon: Calendar, label: "Upcoming Deadlines", value: `${upcomingDeadlines}`, sub: `Next: ${returnStatus.find(r => r.status !== "submitted")?.due || "—"}`, color: "#D4884A" },
          { icon: MessageSquare, label: "Open Regulator Queries", value: `${unresolvedQueries.length}`, sub: `${unresolvedQueries.filter(q => q.priority === "high").length} high priority`, color: "#D4564A" },
        ].map((k, i) => (
          <div key={i} className="card-hover fade-in" style={{ ...cs.card(3), animationDelay: `${i * 60}ms` }}>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 10 }}>
              <span style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: "1px", color: "#5A6270" }}>{k.label}</span>
              <k.icon size={15} color={k.color} strokeWidth={1.5} />
            </div>
            <div style={{ fontSize: 32, fontWeight: 700, color: "#E8E0D0", letterSpacing: "-1px", ...mono }}>{k.value}</div>
            <div style={{ fontSize: 11, color: "#5A6270", marginTop: 4 }}>{k.sub}</div>
          </div>
        ))}
      </div>

      {/* ── Row 2: Upcoming Deadlines + Filing History ──────────────────── */}
      <div style={cs.grid}>
        <div className="card-hover fade-in" style={cs.card(7)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Regulatory Return Calendar</span>
            <span style={{ fontSize: 10, color: "#4A5260" }}>{returnStatus.length} returns tracked</span>
          </div>
          <div style={{ maxHeight: 300, overflowY: "auto" }}>
            {returnStatus.map((r, i) => {
              const st = statusDef[r.status];
              const Icon = st.icon;
              return (
                <div key={i} style={{
                  display: "flex", alignItems: "center", gap: 12, padding: "11px 0",
                  borderBottom: i < returnStatus.length - 1 ? "1px solid rgba(255,255,255,0.03)" : "none",
                }}>
                  <div style={{
                    width: 36, height: 36, borderRadius: 8, display: "flex", alignItems: "center", justifyContent: "center",
                    background: r.status === "submitted" ? "rgba(61,107,79,0.1)" : `rgba(${r.daysLeft <= 14 ? "184,98,62" : "255,255,255"},0.06)`,
                  }}>
                    <Icon size={16} color={r.status === "submitted" ? "#5B9B6F" : urgencyColor(r.daysLeft)} />
                  </div>
                  <div style={{ flex: 1 }}>
                    <div style={{ fontSize: 12, color: "#C8CDD4", fontWeight: 500 }}>{r.name}</div>
                    <div style={{ fontSize: 10, color: "#4A5260", marginTop: 2 }}>Due: {r.due}</div>
                  </div>
                  <div style={{ textAlign: "right", display: "flex", flexDirection: "column", alignItems: "flex-end", gap: 4 }}>
                    <span style={{
                      display: "inline-block", padding: "3px 8px", borderRadius: 4,
                      fontSize: 9, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.5px",
                      background: st.bg, color: st.color,
                    }}>{st.label}</span>
                    {r.status !== "submitted" && (
                      <span style={{
                        ...mono, fontSize: 11, fontWeight: 600,
                        color: urgencyColor(r.daysLeft),
                      }}>{r.daysLeft}d</span>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        <div className="card-hover fade-in" style={cs.card(5)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Filing History — 6 Months</span>
            <BarChart3 size={14} color={accent} strokeWidth={1.5} />
          </div>
          <ResponsiveContainer width="100%" height={180}>
            <BarChart data={filingPosture} margin={{ top: 5, right: 5, bottom: 0, left: -20 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.03)" />
              <XAxis dataKey="month" tick={{ fill: "#4A5260", fontSize: 10 }} axisLine={false} tickLine={false} />
              <YAxis tick={{ fill: "#4A5260", fontSize: 10 }} axisLine={false} tickLine={false} />
              <Tooltip content={<Tip />} />
              <Bar dataKey="onTime" stackId="a" fill="#3D6B4F" name="On Time" />
              <Bar dataKey="late" stackId="a" fill="#C9A84C" name="Late" />
              <Bar dataKey="rejected" stackId="a" fill="#8B2E2E" radius={[3, 3, 0, 0]} name="Rejected" />
            </BarChart>
          </ResponsiveContainer>
          <div style={{ display: "flex", gap: 14, marginTop: 8 }}>
            {[["On Time", "#3D6B4F"], ["Late", "#C9A84C"], ["Rejected", "#8B2E2E"]].map(([l, c]) => (
              <div key={l} style={{ display: "flex", alignItems: "center", gap: 5, fontSize: 10, color: "#6B7280" }}>
                <span style={{ width: 8, height: 8, borderRadius: 2, background: c, display: "inline-block" }} />{l}
              </div>
            ))}
          </div>

          {/* Compliance timeline */}
          <div style={{ marginTop: 18 }}>
            <span style={{ ...cs.cardTitle, fontSize: 10 }}>12-Month Compliance Trajectory</span>
          </div>
          <ResponsiveContainer width="100%" height={80}>
            <AreaChart data={complianceTimeline} margin={{ top: 8, right: 5, bottom: 0, left: -20 }}>
              <defs>
                <linearGradient id="gComp" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={accent} stopOpacity={0.2} />
                  <stop offset="100%" stopColor={accent} stopOpacity={0} />
                </linearGradient>
              </defs>
              <XAxis dataKey="month" tick={{ fill: "#3A4050", fontSize: 8 }} axisLine={false} tickLine={false} />
              <YAxis domain={[88, 100]} hide />
              <Area type="monotone" dataKey="score" stroke={accent} fill="url(#gComp)" strokeWidth={1.5} dot={false} />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      </div>

      {/* ── Row 3: Peer Benchmarks + Radar ─────────────────────────────── */}
      <div style={cs.grid}>
        <div className="card-hover fade-in" style={cs.card(4)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Peer Benchmark — DMB Sector</span>
            <Target size={14} color={accent} strokeWidth={1.5} />
          </div>
          <ResponsiveContainer width="100%" height={210}>
            <RadarChart cx="50%" cy="50%" outerRadius={75} data={radarData}>
              <PolarGrid stroke="rgba(255,255,255,0.06)" />
              <PolarAngleAxis dataKey="metric" tick={{ fill: "#5A6270", fontSize: 9 }} />
              <Radar name="You" dataKey="you" stroke={accent} fill={accent} fillOpacity={0.15} strokeWidth={2} />
              <Radar name="Peer Avg" dataKey="peerAvg" stroke="#4A5260" fill="#4A5260" fillOpacity={0.05} strokeWidth={1.5} strokeDasharray="4 3" />
            </RadarChart>
          </ResponsiveContainer>
          <div style={{ display: "flex", gap: 14, marginTop: 4, justifyContent: "center" }}>
            {[["Your Score", accent], ["Peer Average", "#4A5260"]].map(([l, c]) => (
              <div key={l} style={{ display: "flex", alignItems: "center", gap: 5, fontSize: 10, color: "#6B7280" }}>
                <span style={{ width: 12, height: 3, borderRadius: 2, background: c, display: "inline-block" }} />{l}
              </div>
            ))}
          </div>
        </div>

        <div className="card-hover fade-in" style={cs.card(4)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Metric Comparison</span>
          </div>
          {peerBenchmark.map((p, i) => (
            <div key={i} style={{ marginBottom: 12 }}>
              <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 4 }}>
                <span style={{ fontSize: 11, color: "#8B95A5" }}>{p.metric}</span>
                <div style={{ display: "flex", gap: 10 }}>
                  <span style={{ ...mono, fontSize: 11, fontWeight: 600, color: accent }}>{p.you}</span>
                  <span style={{ ...mono, fontSize: 11, color: "#4A5260" }}>{p.peerAvg}</span>
                </div>
              </div>
              <div style={{ position: "relative", height: 5, borderRadius: 3, background: "rgba(255,255,255,0.04)" }}>
                <div style={{ position: "absolute", height: "100%", borderRadius: 3, width: `${p.peerAvg}%`, background: "rgba(74,82,96,0.3)" }} />
                <div style={{ position: "absolute", height: "100%", borderRadius: 3, width: `${p.you}%`, background: `linear-gradient(90deg, ${accent}80, ${accent})` }} />
                <div style={{
                  position: "absolute", top: -2, left: `${p.peerBest}%`, transform: "translateX(-50%)",
                  width: 2, height: 9, background: "#5B9B6F", borderRadius: 1,
                }} />
              </div>
            </div>
          ))}
          <div style={{ display: "flex", gap: 14, marginTop: 8 }}>
            {[["You", accent], ["Peer Avg", "#4A5260"], ["Best", "#5B9B6F"]].map(([l, c]) => (
              <div key={l} style={{ display: "flex", alignItems: "center", gap: 5, fontSize: 10, color: "#6B7280" }}>
                <span style={{ width: c === "#5B9B6F" ? 2 : 8, height: c === "#5B9B6F" ? 8 : 3, borderRadius: 2, background: c, display: "inline-block" }} />{l}
              </div>
            ))}
          </div>
        </div>

        <div className="card-hover fade-in" style={cs.card(4)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Peer Ranking — DMB Compliance</span>
            <Users size={14} color={accent} strokeWidth={1.5} />
          </div>
          {peerRanking.map((p, i) => (
            <div key={i} style={{
              display: "flex", alignItems: "center", gap: 10, padding: "8px 8px",
              borderRadius: 6, marginBottom: 3,
              background: p.isYou ? `${accent}0A` : "transparent",
              border: p.isYou ? `1px solid ${accent}20` : "1px solid transparent",
            }}>
              <span style={{
                width: 22, height: 22, borderRadius: 5, display: "flex", alignItems: "center", justifyContent: "center",
                ...mono, fontSize: 11, fontWeight: 700,
                background: i === 0 ? "rgba(201,168,76,0.15)" : "rgba(255,255,255,0.04)",
                color: i === 0 ? "#C9A84C" : "#5A6270",
              }}>{p.rank}</span>
              <span style={{ flex: 1, fontSize: 12, color: p.isYou ? "#E8E0D0" : "#8B95A5", fontWeight: p.isYou ? 600 : 400 }}>
                {p.name}
                {p.isYou && <span style={{ fontSize: 9, color: accent, marginLeft: 6, fontWeight: 500 }}>YOU</span>}
              </span>
              <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                <div style={{ width: 50, height: 4, borderRadius: 2, background: "rgba(255,255,255,0.04)", overflow: "hidden" }}>
                  <div style={{ height: "100%", borderRadius: 2, width: `${p.score}%`, background: p.isYou ? accent : "rgba(255,255,255,0.15)" }} />
                </div>
                <span style={{ ...mono, fontSize: 12, fontWeight: 600, color: p.isYou ? accent : "#6B7280", minWidth: 22, textAlign: "right" }}>{p.score}</span>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* ── Row 4: Queries + Rejected Returns ──────────────────────────── */}
      <div style={cs.grid}>
        <div className="card-hover fade-in" style={cs.card(6)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Open Regulator Queries</span>
            <span style={{
              ...mono, fontSize: 10, fontWeight: 600, color: "#D4884A",
              background: "rgba(184,98,62,0.1)", padding: "3px 8px", borderRadius: 4,
            }}>{unresolvedQueries.length} PENDING</span>
          </div>
          {unresolvedQueries.map((q, i) => (
            <div key={i} style={{
              padding: "12px 0",
              borderBottom: i < unresolvedQueries.length - 1 ? "1px solid rgba(255,255,255,0.03)" : "none",
            }}>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 4 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <span style={{ ...mono, fontSize: 10, color: "#5A6270" }}>{q.id}</span>
                  <span style={{
                    padding: "2px 7px", borderRadius: 4, fontSize: 9, fontWeight: 600, textTransform: "uppercase",
                    background: priorityDef[q.priority].bg, color: priorityDef[q.priority].color,
                  }}>{q.priority}</span>
                </div>
                <span style={{
                  ...mono, fontSize: 11, fontWeight: 600,
                  color: parseInt(q.age) >= 14 ? "#D4564A" : "#D4884A",
                }}>{q.age}</span>
              </div>
              <div style={{ fontSize: 12, color: "#C8CDD4", marginBottom: 3 }}>{q.subject}</div>
              <div style={{ fontSize: 10, color: "#4A5260" }}>From: {q.from} · Received: {q.received}</div>
            </div>
          ))}
          <div style={{
            marginTop: 12, padding: "10px 14px", borderRadius: 6,
            background: "rgba(212,86,74,0.06)", border: "1px solid rgba(212,86,74,0.1)",
            fontSize: 11, color: "#9B9FAA",
          }}>
            <strong style={{ color: "#D4564A" }}>1 query</strong> exceeds 21-day response SLA. Escalation risk.
          </div>
        </div>

        <div className="card-hover fade-in" style={cs.card(6)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Previously Rejected Returns</span>
            <XCircle size={14} color="#D4884A" strokeWidth={1.5} />
          </div>
          {rejectedReturns.map((r, i) => (
            <div key={i} style={{
              padding: "12px 0",
              borderBottom: i < rejectedReturns.length - 1 ? "1px solid rgba(255,255,255,0.03)" : "none",
            }}>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 4 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <span style={{ ...mono, fontSize: 10, color: "#5A6270" }}>{r.id}</span>
                  <span style={{ fontSize: 12, color: "#C8CDD4", fontWeight: 500 }}>{r.name}</span>
                </div>
                {r.resubmitted && (
                  <span style={{
                    fontSize: 9, fontWeight: 600, textTransform: "uppercase", padding: "3px 7px", borderRadius: 4,
                    background: "rgba(61,107,79,0.12)", color: "#5B9B6F",
                  }}>RESUBMITTED</span>
                )}
              </div>
              <div style={{ fontSize: 11, color: "#8B95A5", marginBottom: 2 }}>
                <strong style={{ color: "#D4884A" }}>Reason:</strong> {r.reason}
              </div>
              <div style={{ fontSize: 10, color: "#4A5260" }}>Rejected: {r.rejectedOn}</div>
            </div>
          ))}
          <div style={{
            marginTop: 12, padding: "10px 14px", borderRadius: 6,
            background: "rgba(61,107,79,0.06)", border: "1px solid rgba(61,107,79,0.1)",
            fontSize: 11, color: "#8B95A5",
          }}>
            All rejected returns have been <strong style={{ color: "#5B9B6F" }}>successfully resubmitted</strong>. No outstanding remediation.
          </div>
        </div>
      </div>

      {/* ── Footer ──────────────────────────────────────────────────────── */}
      <div style={{
        padding: "14px 28px", borderTop: "1px solid rgba(255,255,255,0.03)",
        display: "flex", justifyContent: "space-between",
      }}>
        <div style={{ fontSize: 10, color: "#3A4050" }}>RegOS™ v3.2.0 · Institutional Compliance Portal · Central Bank of Nigeria</div>
        <div style={{ fontSize: 10, color: "#3A4050" }}>{institution.name} · Last sync: {now.toLocaleTimeString("en-NG", { hour: "2-digit", minute: "2-digit" })} WAT</div>
      </div>
    </div>
  );
}
