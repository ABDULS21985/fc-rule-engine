import { useState, useEffect, useMemo } from "react";
import {
  AreaChart, Area, BarChart, Bar, LineChart, Line, PieChart, Pie, Cell,
  XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, ComposedChart,
} from "recharts";
import {
  Shield, AlertTriangle, TrendingUp, TrendingDown, Building2, FileCheck,
  Clock, ChevronRight, ChevronDown, Eye, Bell, Activity, Zap,
  ArrowUpRight, ArrowDownRight, Target, AlertCircle,
  CheckCircle2, XCircle, Timer, Users, Database, Filter,
  Search, MoreHorizontal, ExternalLink, Inbox, ClipboardCheck,
  FileX, RotateCcw, ListChecks, BarChart3, GitPullRequest,
  MessageSquare, Bookmark, ChevronUp, Layers, UserCheck, Flag
} from "lucide-react";

// ── Approval Queue ────────────────────────────────────────────────────────────
const approvalQueue = [
  { id: "SUB-8842", institution: "Access Bank Plc", returnType: "Monthly Prudential Return", submitted: "Mar 10, 2026", status: "pending_review", validationScore: 98, sector: "DMB", assignee: "Self", flags: 0 },
  { id: "SUB-8839", institution: "Kuda MFB", returnType: "Quarterly AML/CFT Report", submitted: "Mar 9, 2026", status: "pending_review", validationScore: 84, sector: "MFB", assignee: "Self", flags: 3 },
  { id: "SUB-8836", institution: "OPay Digital Services", returnType: "PSP Transaction Volume Report", submitted: "Mar 9, 2026", status: "pending_review", validationScore: 91, sector: "PSP", assignee: "Self", flags: 1 },
  { id: "SUB-8834", institution: "Sterling Bank", returnType: "FX Position Report", submitted: "Mar 8, 2026", status: "returned", validationScore: 62, sector: "DMB", assignee: "Self", flags: 7 },
  { id: "SUB-8831", institution: "NPF Microfinance Bank", returnType: "Quarterly Capital Adequacy", submitted: "Mar 8, 2026", status: "pending_review", validationScore: 77, sector: "MFB", assignee: "Self", flags: 4 },
  { id: "SUB-8828", institution: "Zenith Bank Plc", returnType: "Monthly Prudential Return", submitted: "Mar 7, 2026", status: "approved", validationScore: 100, sector: "DMB", assignee: "Self", flags: 0 },
  { id: "SUB-8825", institution: "Moniepoint MFB", returnType: "Agent Network Compliance", submitted: "Mar 7, 2026", status: "pending_review", validationScore: 88, sector: "MFB", assignee: "Self", flags: 2 },
  { id: "SUB-8822", institution: "Flutterwave Payments", returnType: "Settlement Reconciliation", submitted: "Mar 6, 2026", status: "escalated", validationScore: 45, sector: "PSP", assignee: "Dir. FinTech", flags: 12 },
];

const overdueReviews = [
  { institution: "Heritage Bank Plc", returnType: "Quarterly Stress Test Results", dueDate: "Feb 28, 2026", daysOverdue: 12, sector: "DMB", lastReminder: "Mar 5" },
  { institution: "First City MFB", returnType: "AML/CFT Suspicious Activity Log", dueDate: "Mar 1, 2026", daysOverdue: 11, sector: "MFB", lastReminder: "Mar 8" },
  { institution: "Travelex BDC", returnType: "Weekly FX Position", dueDate: "Mar 3, 2026", daysOverdue: 9, sector: "BDC", lastReminder: "Mar 7" },
  { institution: "Coronation Insurance", returnType: "Solvency Margin Report", dueDate: "Mar 5, 2026", daysOverdue: 7, sector: "INS", lastReminder: "Mar 10" },
  { institution: "LAPO MFB", returnType: "Monthly Prudential Return", dueDate: "Mar 7, 2026", daysOverdue: 5, sector: "MFB", lastReminder: "Mar 10" },
];

const validationFailures = [
  { rule: "CAR below 15% minimum threshold", count: 6, severity: "critical", trend: "down", affected: ["Heritage Bank", "Wema Bank", "Keystone Bank"] },
  { rule: "Row total ≠ sum of line items", count: 18, severity: "medium", trend: "up", affected: ["Multiple MFBs", "3 BDCs"] },
  { rule: "Missing mandatory field: Board approval date", count: 12, severity: "high", trend: "stable", affected: ["5 DMBs", "7 MFBs"] },
  { rule: "Cross-return inconsistency: BS vs P&L", count: 8, severity: "high", trend: "down", affected: ["4 DMBs"] },
  { rule: "Negative deposit balance reported", count: 3, severity: "critical", trend: "stable", affected: ["NPF MFB", "First City MFB"] },
  { rule: "FX position exceeds approved limit", count: 14, severity: "high", trend: "up", affected: ["8 BDCs", "3 DMBs"] },
  { rule: "Late submission penalty threshold", count: 22, severity: "medium", trend: "down", affected: ["Predominantly BDC sector"] },
  { rule: "AML filing gap > 30 days", count: 5, severity: "critical", trend: "stable", affected: ["3 MFBs", "2 BDCs"] },
];

const institutionDrilldown = [
  { name: "Access Bank", submissions: 12, approved: 12, rejected: 0, pending: 0, complianceScore: 99, trend: [95, 97, 98, 99, 99, 99] },
  { name: "Zenith Bank", submissions: 12, approved: 11, rejected: 0, pending: 1, complianceScore: 97, trend: [94, 95, 96, 97, 97, 97] },
  { name: "GTBank", submissions: 12, approved: 10, rejected: 1, pending: 1, complianceScore: 94, trend: [92, 93, 91, 94, 95, 94] },
  { name: "Kuda MFB", submissions: 8, approved: 5, rejected: 2, pending: 1, complianceScore: 78, trend: [70, 72, 74, 76, 75, 78] },
  { name: "Sterling Bank", submissions: 12, approved: 8, rejected: 3, pending: 1, complianceScore: 72, trend: [80, 78, 75, 73, 71, 72] },
  { name: "Heritage Bank", submissions: 10, approved: 5, rejected: 4, pending: 1, complianceScore: 58, trend: [68, 65, 62, 60, 59, 58] },
  { name: "NPF MFB", submissions: 8, approved: 4, rejected: 3, pending: 1, complianceScore: 55, trend: [62, 60, 58, 57, 56, 55] },
  { name: "OPay", submissions: 6, approved: 5, rejected: 0, pending: 1, complianceScore: 88, trend: [80, 82, 84, 86, 87, 88] },
];

const weeklyVolume = [
  { week: "W6", received: 320, reviewed: 290, approved: 260, returned: 30 },
  { week: "W7", received: 410, reviewed: 380, approved: 340, returned: 40 },
  { week: "W8", received: 380, reviewed: 360, approved: 330, returned: 30 },
  { week: "W9", received: 450, reviewed: 410, approved: 370, returned: 40 },
  { week: "W10", received: 520, reviewed: 460, approved: 420, returned: 40 },
  { week: "W11", received: 480, reviewed: 440, approved: 400, returned: 40 },
];

const trendComparisons = [
  { month: "Oct", thisYear: 82, lastYear: 74 },
  { month: "Nov", thisYear: 84, lastYear: 76 },
  { month: "Dec", thisYear: 83, lastYear: 75 },
  { month: "Jan", thisYear: 87, lastYear: 79 },
  { month: "Feb", thisYear: 86, lastYear: 80 },
  { month: "Mar", thisYear: 89, lastYear: 82 },
];

// ── Helpers ───────────────────────────────────────────────────────────────────
const statusConfig = {
  pending_review: { label: "PENDING", bg: "rgba(201,168,76,0.12)", color: "#C9A84C", border: "rgba(201,168,76,0.25)" },
  approved: { label: "APPROVED", bg: "rgba(61,107,79,0.12)", color: "#5B9B6F", border: "rgba(91,155,111,0.25)" },
  returned: { label: "RETURNED", bg: "rgba(184,98,62,0.12)", color: "#D4884A", border: "rgba(184,98,62,0.25)" },
  escalated: { label: "ESCALATED", bg: "rgba(139,46,46,0.15)", color: "#D4564A", border: "rgba(139,46,46,0.3)" },
};

const sevConfig = {
  critical: { bg: "rgba(139,46,46,0.15)", color: "#D4564A" },
  high: { bg: "rgba(184,98,62,0.12)", color: "#D4884A" },
  medium: { bg: "rgba(201,168,76,0.1)", color: "#C9A84C" },
};

const validationColor = (score) =>
  score >= 95 ? "#5B9B6F" : score >= 80 ? "#C9A84C" : score >= 60 ? "#D4884A" : "#D4564A";

const mono = { fontFamily: "'IBM Plex Mono', monospace" };

const Tip = ({ active, payload, label }) => {
  if (!active || !payload?.length) return null;
  return (
    <div style={{ background: "#1A2030", border: "1px solid rgba(107,142,155,0.25)", borderRadius: 6, padding: "10px 14px", fontSize: 11, boxShadow: "0 8px 24px rgba(0,0,0,0.4)" }}>
      <div style={{ color: "#6B8E9B", fontWeight: 600, marginBottom: 6 }}>{label}</div>
      {payload.map((p, i) => (
        <div key={i} style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 2 }}>
          <span style={{ width: 8, height: 8, borderRadius: 2, background: p.color || p.fill, display: "inline-block" }} />
          <span style={{ color: "#8B95A5" }}>{p.dataKey}:</span>
          <span style={{ color: "#E8E0D0", fontWeight: 600, ...mono }}>{p.value}</span>
        </div>
      ))}
    </div>
  );
};

// ── Sparkline ─────────────────────────────────────────────────────────────────
const Sparkline = ({ data, color = "#5B9B6F", width = 60, height = 20 }) => {
  const min = Math.min(...data);
  const max = Math.max(...data);
  const range = max - min || 1;
  const points = data.map((v, i) => `${(i / (data.length - 1)) * width},${height - ((v - min) / range) * height}`).join(" ");
  return (
    <svg width={width} height={height} style={{ display: "block" }}>
      <polyline points={points} fill="none" stroke={color} strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
};

// ── Main ──────────────────────────────────────────────────────────────────────
export default function DirectorExaminerDashboard() {
  const [now, setNow] = useState(new Date());
  const [queueFilter, setQueueFilter] = useState("all");
  const [failureSort, setFailureSort] = useState("severity");

  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 60000);
    return () => clearInterval(t);
  }, []);

  const filteredQueue = queueFilter === "all" ? approvalQueue : approvalQueue.filter((s) => s.status === queueFilter);
  const pendingCount = approvalQueue.filter((s) => s.status === "pending_review").length;

  const sortedFailures = useMemo(() => {
    const sevOrder = { critical: 0, high: 1, medium: 2 };
    return [...validationFailures].sort((a, b) =>
      failureSort === "severity" ? sevOrder[a.severity] - sevOrder[b.severity] : b.count - a.count
    );
  }, [failureSort]);

  const cs = {
    root: {
      background: "linear-gradient(145deg, #0B0F14 0%, #111820 40%, #0D1218 100%)",
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
    sectorTag: { display: "inline-block", padding: "2px 6px", borderRadius: 3, fontSize: 10, fontWeight: 500, background: "rgba(255,255,255,0.04)", color: "#7B8494", border: "1px solid rgba(255,255,255,0.06)" },
  };

  const accentColor = "#6B8E9B";

  return (
    <div style={cs.root}>
      <style>{`
        @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@300;400;500;600;700&family=IBM+Plex+Mono:wght@400;500;600;700&display=swap');
        @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:0.4} }
        @keyframes fadeIn { from{opacity:0;transform:translateY(6px)} to{opacity:1;transform:translateY(0)} }
        .fade-in { animation: fadeIn 0.35s ease-out forwards; }
        .card-hover:hover { border-color: rgba(107,142,155,0.2) !important; }
        ::-webkit-scrollbar{width:5px}::-webkit-scrollbar-track{background:transparent}::-webkit-scrollbar-thumb{background:rgba(107,142,155,0.15);border-radius:3px}
      `}</style>

      {/* ── Header ──────────────────────────────────────────────────────── */}
      <div style={{
        background: "linear-gradient(180deg, rgba(107,142,155,0.06) 0%, transparent 100%)",
        borderBottom: "1px solid rgba(107,142,155,0.12)", padding: "18px 28px",
        display: "flex", alignItems: "center", justifyContent: "space-between",
      }}>
        <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
          <div style={{
            width: 38, height: 38, borderRadius: "50%",
            background: "linear-gradient(135deg, #6B8E9B 0%, #4A6E7A 100%)",
            display: "flex", alignItems: "center", justifyContent: "center",
            boxShadow: "0 0 20px rgba(107,142,155,0.2)",
          }}><ClipboardCheck size={18} color="#111820" /></div>
          <div>
            <div style={{ fontSize: 18, fontWeight: 600, color: "#E8E0D0", letterSpacing: "-0.3px" }}>
              Director / Examiner — Operations Centre
            </div>
            <div style={{ fontSize: 11, color: "rgba(107,142,155,0.7)", letterSpacing: "1.5px", textTransform: "uppercase", marginTop: 2 }}>
              RegOS™ — Examination & Review Workflow
            </div>
          </div>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
          <div style={{
            display: "flex", alignItems: "center", gap: 6, padding: "5px 12px", borderRadius: 20,
            background: "rgba(61,107,79,0.15)", border: "1px solid rgba(91,155,111,0.25)",
            fontSize: 11, color: "#5B9B6F", fontWeight: 500,
          }}>
            <div style={{ width: 6, height: 6, borderRadius: "50%", background: "#5B9B6F", animation: "pulse 2s infinite" }} />LIVE
          </div>
          <div style={{ fontSize: 11, color: "#5A6270" }}>
            {now.toLocaleDateString("en-NG", { weekday: "short", day: "numeric", month: "short", year: "numeric" })}
          </div>
          <button style={{
            position: "relative", background: "rgba(255,255,255,0.04)", border: "1px solid rgba(255,255,255,0.06)",
            borderRadius: 8, padding: 8, cursor: "pointer", color: "#7B8494", display: "flex",
          }}>
            <Bell size={16} />
            {pendingCount > 0 && <div style={{
              position: "absolute", top: -4, right: -4, minWidth: 18, height: 18, borderRadius: 9,
              background: "#D4564A", display: "flex", alignItems: "center", justifyContent: "center",
              fontSize: 10, fontWeight: 700, color: "#fff", border: "2px solid #111820", ...mono,
            }}>{pendingCount}</div>}
          </button>
        </div>
      </div>

      {/* ── Inbox KPIs ──────────────────────────────────────────────────── */}
      <div style={{ ...cs.grid, paddingTop: 20 }}>
        {[
          { icon: Inbox, label: "Pending Reviews", value: pendingCount.toString(), accent: "#C9A84C", sub: "In your queue" },
          { icon: Timer, label: "Avg Review Time", value: "4.2h", accent: "#6B8E9B", sub: "Target: < 8h" },
          { icon: FileX, label: "Overdue Returns", value: overdueReviews.length.toString(), accent: "#D4564A", sub: "Across all sectors" },
          { icon: CheckCircle2, label: "Approved This Week", value: "38", accent: "#5B9B6F", sub: "92% approval rate" },
          { icon: AlertTriangle, label: "Validation Failures", value: validationFailures.reduce((a, b) => a + b.count, 0).toString(), accent: "#D4884A", sub: `${validationFailures.filter(v => v.severity === "critical").length} critical rules` },
          { icon: GitPullRequest, label: "Escalated Up", value: "3", accent: "#8B6FA0", sub: "To Deputy Governor" },
        ].map((k, i) => (
          <div key={i} className="card-hover fade-in" style={{ ...cs.card(2), animationDelay: `${i * 50}ms` }}>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 10 }}>
              <k.icon size={15} color={k.accent} strokeWidth={1.5} />
              <span style={{ fontSize: 9, textTransform: "uppercase", letterSpacing: "0.8px", color: "#4A5260" }}>{k.label}</span>
            </div>
            <div style={{ fontSize: 26, fontWeight: 700, color: "#E8E0D0", letterSpacing: "-1px", ...mono }}>{k.value}</div>
            <div style={{ fontSize: 10, color: "#5A6270", marginTop: 4 }}>{k.sub}</div>
          </div>
        ))}
      </div>

      {/* ── Row 2: Approval Queue ──────────────────────────────────────── */}
      <div style={cs.grid}>
        <div className="card-hover fade-in" style={cs.card(12)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
              <span style={cs.cardTitle}>Approval Queue</span>
              <div style={{ display: "flex", gap: 4 }}>
                {[
                  { key: "all", label: "All" },
                  { key: "pending_review", label: "Pending" },
                  { key: "returned", label: "Returned" },
                  { key: "escalated", label: "Escalated" },
                ].map((f) => (
                  <button key={f.key} onClick={() => setQueueFilter(f.key)} style={{
                    padding: "4px 10px", borderRadius: 5, cursor: "pointer",
                    border: queueFilter === f.key ? `1px solid ${accentColor}40` : "1px solid rgba(255,255,255,0.05)",
                    background: queueFilter === f.key ? `${accentColor}12` : "rgba(255,255,255,0.02)",
                    color: queueFilter === f.key ? accentColor : "#6B7280",
                    fontSize: 10, fontWeight: 500, fontFamily: "'IBM Plex Sans', sans-serif",
                  }}>{f.label}</button>
                ))}
              </div>
            </div>
            <span style={{ fontSize: 10, color: "#4A5260" }}>{filteredQueue.length} items</span>
          </div>
          <div style={{ maxHeight: 290, overflowY: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 3px" }}>
              <thead>
                <tr>
                  {["ID", "Institution", "Return Type", "Submitted", "Validation", "Flags", "Status", ""].map((h) => (
                    <th key={h} style={cs.th}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {filteredQueue.map((s) => {
                  const sc = statusConfig[s.status];
                  return (
                    <tr key={s.id} style={{ cursor: "pointer" }}
                      onMouseOver={(e) => e.currentTarget.style.background = "rgba(107,142,155,0.03)"}
                      onMouseOut={(e) => e.currentTarget.style.background = "transparent"}
                    >
                      <td style={{ ...cs.td, ...mono, color: "#6B7280", fontSize: 11 }}>{s.id}</td>
                      <td style={{ ...cs.td, color: "#C8CDD4", fontWeight: 500 }}>
                        {s.institution}
                        <span style={{ ...cs.sectorTag, marginLeft: 6 }}>{s.sector}</span>
                      </td>
                      <td style={{ ...cs.td, color: "#8B95A5", maxWidth: 200, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{s.returnType}</td>
                      <td style={{ ...cs.td, color: "#6B7280", fontSize: 11 }}>{s.submitted}</td>
                      <td style={cs.td}>
                        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                          <div style={{ width: 40, height: 4, borderRadius: 2, background: "rgba(255,255,255,0.06)", overflow: "hidden" }}>
                            <div style={{ height: "100%", borderRadius: 2, width: `${s.validationScore}%`, background: validationColor(s.validationScore) }} />
                          </div>
                          <span style={{ ...mono, fontSize: 11, fontWeight: 600, color: validationColor(s.validationScore) }}>{s.validationScore}</span>
                        </div>
                      </td>
                      <td style={cs.td}>
                        {s.flags > 0 ? (
                          <span style={{
                            ...mono, fontSize: 11, fontWeight: 600,
                            color: s.flags >= 5 ? "#D4564A" : s.flags >= 2 ? "#D4884A" : "#C9A84C",
                          }}>{s.flags}</span>
                        ) : <span style={{ color: "#3A4050" }}>—</span>}
                      </td>
                      <td style={cs.td}>
                        <span style={{
                          display: "inline-block", padding: "3px 8px", borderRadius: 4,
                          fontSize: 9, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.5px",
                          background: sc.bg, color: sc.color, border: `1px solid ${sc.border}`,
                        }}>{sc.label}</span>
                      </td>
                      <td style={cs.td}>
                        <button style={{
                          background: s.status === "pending_review" ? `${accentColor}15` : "transparent",
                          border: s.status === "pending_review" ? `1px solid ${accentColor}30` : "1px solid rgba(255,255,255,0.04)",
                          borderRadius: 5, padding: "4px 10px", cursor: "pointer",
                          fontSize: 10, fontWeight: 500, color: s.status === "pending_review" ? accentColor : "#4A5260",
                          fontFamily: "'IBM Plex Sans', sans-serif",
                        }}>
                          {s.status === "pending_review" ? "Review" : "View"}
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {/* ── Row 3: Overdue + Validation Failures ───────────────────────── */}
      <div style={cs.grid}>
        <div className="card-hover fade-in" style={cs.card(5)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Overdue Returns — Pending Institutional Response</span>
            <span style={{ ...mono, fontSize: 10, fontWeight: 600, color: "#D4564A", background: "rgba(212,86,74,0.1)", padding: "3px 8px", borderRadius: 4 }}>
              {overdueReviews.length} OVERDUE
            </span>
          </div>
          {overdueReviews.map((o, i) => (
            <div key={i} style={{
              display: "flex", alignItems: "center", gap: 12, padding: "10px 0",
              borderBottom: i < overdueReviews.length - 1 ? "1px solid rgba(255,255,255,0.03)" : "none",
            }}>
              <div style={{
                width: 34, height: 34, borderRadius: 7, display: "flex", alignItems: "center", justifyContent: "center",
                background: o.daysOverdue >= 10 ? "rgba(139,46,46,0.12)" : "rgba(184,98,62,0.1)",
                ...mono, fontSize: 12, fontWeight: 700,
                color: o.daysOverdue >= 10 ? "#D4564A" : "#D4884A",
              }}>{o.daysOverdue}d</div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                  <span style={{ fontSize: 12, color: "#C8CDD4", fontWeight: 500 }}>{o.institution}</span>
                  <span style={cs.sectorTag}>{o.sector}</span>
                </div>
                <div style={{ fontSize: 11, color: "#5A6270", marginTop: 2, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{o.returnType}</div>
              </div>
              <div style={{ textAlign: "right" }}>
                <div style={{ fontSize: 10, color: "#4A5260" }}>Due: {o.dueDate}</div>
                <div style={{ fontSize: 10, color: "#4A5260", marginTop: 2 }}>Reminded: {o.lastReminder}</div>
              </div>
            </div>
          ))}
        </div>

        <div className="card-hover fade-in" style={cs.card(7)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Validation Failure Analysis</span>
            <div style={{ display: "flex", gap: 4 }}>
              {["severity", "count"].map((sort) => (
                <button key={sort} onClick={() => setFailureSort(sort)} style={{
                  padding: "4px 10px", borderRadius: 5, cursor: "pointer",
                  border: failureSort === sort ? `1px solid ${accentColor}40` : "1px solid rgba(255,255,255,0.05)",
                  background: failureSort === sort ? `${accentColor}12` : "rgba(255,255,255,0.02)",
                  color: failureSort === sort ? accentColor : "#6B7280",
                  fontSize: 10, fontWeight: 500, fontFamily: "'IBM Plex Sans', sans-serif", textTransform: "capitalize",
                }}>By {sort}</button>
              ))}
            </div>
          </div>
          <div style={{ maxHeight: 260, overflowY: "auto" }}>
            {sortedFailures.map((f, i) => (
              <div key={i} style={{
                display: "flex", alignItems: "flex-start", gap: 10, padding: "10px 0",
                borderBottom: i < sortedFailures.length - 1 ? "1px solid rgba(255,255,255,0.03)" : "none",
              }}>
                <span style={{
                  padding: "3px 7px", borderRadius: 4, fontSize: 9, fontWeight: 600, textTransform: "uppercase",
                  background: sevConfig[f.severity].bg, color: sevConfig[f.severity].color, whiteSpace: "nowrap", marginTop: 1,
                }}>{f.severity}</span>
                <div style={{ flex: 1 }}>
                  <div style={{ fontSize: 12, color: "#C8CDD4", lineHeight: 1.4 }}>{f.rule}</div>
                  <div style={{ fontSize: 10, color: "#4A5260", marginTop: 3 }}>
                    Affected: {f.affected.join(", ")}
                  </div>
                </div>
                <div style={{ textAlign: "right", whiteSpace: "nowrap" }}>
                  <span style={{ ...mono, fontSize: 16, fontWeight: 700, color: "#E8E0D0" }}>{f.count}</span>
                  <div style={{ display: "flex", alignItems: "center", gap: 3, justifyContent: "flex-end", marginTop: 2 }}>
                    {f.trend === "up" ? <ArrowUpRight size={11} color="#D4564A" /> : f.trend === "down" ? <ArrowDownRight size={11} color="#5B9B6F" /> : <Activity size={11} color="#6B7280" />}
                    <span style={{ fontSize: 10, color: f.trend === "up" ? "#D4564A" : f.trend === "down" ? "#5B9B6F" : "#6B7280" }}>
                      {f.trend === "up" ? "Rising" : f.trend === "down" ? "Falling" : "Stable"}
                    </span>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* ── Row 4: Institution Drilldown + Volume + Y/Y Comparison ──────── */}
      <div style={cs.grid}>
        <div className="card-hover fade-in" style={cs.card(5)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Institution Compliance Drilldown</span>
            <BarChart3 size={14} color={accentColor} strokeWidth={1.5} />
          </div>
          <div style={{ maxHeight: 250, overflowY: "auto" }}>
            {institutionDrilldown.map((inst, i) => (
              <div key={i} style={{
                display: "flex", alignItems: "center", gap: 10, padding: "8px 0",
                borderBottom: i < institutionDrilldown.length - 1 ? "1px solid rgba(255,255,255,0.03)" : "none",
              }}>
                <div style={{ width: 100, fontSize: 12, color: "#C8CDD4", fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{inst.name}</div>
                <div style={{ flex: 1, display: "flex", alignItems: "center", gap: 6 }}>
                  <div style={{ flex: 1, height: 6, borderRadius: 3, background: "rgba(255,255,255,0.04)", overflow: "hidden", display: "flex" }}>
                    <div style={{ height: "100%", width: `${(inst.approved / inst.submissions) * 100}%`, background: "#3D6B4F" }} />
                    <div style={{ height: "100%", width: `${(inst.pending / inst.submissions) * 100}%`, background: "#C9A84C" }} />
                    <div style={{ height: "100%", width: `${(inst.rejected / inst.submissions) * 100}%`, background: "#8B2E2E" }} />
                  </div>
                  <span style={{ ...mono, fontSize: 11, fontWeight: 600, color: validationColor(inst.complianceScore), minWidth: 28, textAlign: "right" }}>
                    {inst.complianceScore}
                  </span>
                </div>
                <Sparkline data={inst.trend} color={inst.trend[inst.trend.length - 1] >= inst.trend[0] ? "#5B9B6F" : "#D4564A"} />
              </div>
            ))}
          </div>
          <div style={{ display: "flex", gap: 14, marginTop: 10 }}>
            {[["Approved", "#3D6B4F"], ["Pending", "#C9A84C"], ["Rejected", "#8B2E2E"]].map(([l, c]) => (
              <div key={l} style={{ display: "flex", alignItems: "center", gap: 5, fontSize: 10, color: "#6B7280" }}>
                <span style={{ width: 8, height: 8, borderRadius: 2, background: c, display: "inline-block" }} />{l}
              </div>
            ))}
          </div>
        </div>

        <div className="card-hover fade-in" style={cs.card(4)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Weekly Review Volume</span>
            <Layers size={14} color={accentColor} strokeWidth={1.5} />
          </div>
          <ResponsiveContainer width="100%" height={200}>
            <ComposedChart data={weeklyVolume} margin={{ top: 5, right: 5, bottom: 0, left: -20 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.03)" />
              <XAxis dataKey="week" tick={{ fill: "#4A5260", fontSize: 10 }} axisLine={false} tickLine={false} />
              <YAxis tick={{ fill: "#4A5260", fontSize: 10 }} axisLine={false} tickLine={false} />
              <Tooltip content={<Tip />} />
              <Bar dataKey="received" fill="rgba(107,142,155,0.3)" radius={[3, 3, 0, 0]} name="Received" />
              <Line type="monotone" dataKey="approved" stroke="#5B9B6F" strokeWidth={2} dot={{ r: 3, fill: "#5B9B6F" }} name="Approved" />
              <Line type="monotone" dataKey="returned" stroke="#D4884A" strokeWidth={2} dot={{ r: 3, fill: "#D4884A" }} name="Returned" />
            </ComposedChart>
          </ResponsiveContainer>
        </div>

        <div className="card-hover fade-in" style={cs.card(3)}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={cs.cardTitle}>Y/Y Compliance Trend</span>
            <TrendingUp size={14} color={accentColor} strokeWidth={1.5} />
          </div>
          <ResponsiveContainer width="100%" height={200}>
            <AreaChart data={trendComparisons} margin={{ top: 5, right: 5, bottom: 0, left: -20 }}>
              <defs>
                <linearGradient id="gThis" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor="#6B8E9B" stopOpacity={0.2} />
                  <stop offset="100%" stopColor="#6B8E9B" stopOpacity={0} />
                </linearGradient>
                <linearGradient id="gLast" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor="#4A5260" stopOpacity={0.1} />
                  <stop offset="100%" stopColor="#4A5260" stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.03)" />
              <XAxis dataKey="month" tick={{ fill: "#4A5260", fontSize: 10 }} axisLine={false} tickLine={false} />
              <YAxis domain={[65, 95]} tick={{ fill: "#4A5260", fontSize: 10 }} axisLine={false} tickLine={false} />
              <Tooltip content={<Tip />} />
              <Area type="monotone" dataKey="thisYear" stroke="#6B8E9B" fill="url(#gThis)" strokeWidth={2} dot={false} name="2025/26" />
              <Area type="monotone" dataKey="lastYear" stroke="#4A5260" fill="url(#gLast)" strokeWidth={1.5} strokeDasharray="4 3" dot={false} name="2024/25" />
            </AreaChart>
          </ResponsiveContainer>
          <div style={{ display: "flex", gap: 14, marginTop: 6 }}>
            {[["2025/26", "#6B8E9B"], ["2024/25", "#4A5260"]].map(([l, c]) => (
              <div key={l} style={{ display: "flex", alignItems: "center", gap: 5, fontSize: 10, color: "#6B7280" }}>
                <span style={{ width: 12, height: 3, borderRadius: 2, background: c, display: "inline-block" }} />{l}
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* ── Footer ──────────────────────────────────────────────────────── */}
      <div style={{
        padding: "14px 28px", borderTop: "1px solid rgba(255,255,255,0.03)",
        display: "flex", justifyContent: "space-between",
      }}>
        <div style={{ fontSize: 10, color: "#3A4050" }}>RegOS™ v3.2.0 · Examination & Review · Central Bank of Nigeria</div>
        <div style={{ fontSize: 10, color: "#3A4050" }}>Last sync: {now.toLocaleTimeString("en-NG", { hour: "2-digit", minute: "2-digit" })} WAT</div>
      </div>
    </div>
  );
}
