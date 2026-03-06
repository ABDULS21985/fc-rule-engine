(function () {
    const chartRegistry = window.__fcCharts || (window.__fcCharts = {});

    window.renderChart = function (canvasId, type, data) {
        const canvas = document.getElementById(canvasId);
        if (!canvas || typeof Chart === "undefined") {
            return;
        }

        const ctx = canvas.getContext("2d");
        if (!ctx) {
            return;
        }

        if (chartRegistry[canvasId]) {
            chartRegistry[canvasId].destroy();
        }

        chartRegistry[canvasId] = new Chart(ctx, {
            type: type,
            data: {
                labels: data.labels || [],
                datasets: (data.datasets || []).map(function (dataset, index) {
                    const palette = [
                        "#0f766e",
                        "#1d4ed8",
                        "#d97706",
                        "#b91c1c",
                        "#7c3aed",
                        "#0ea5e9"
                    ];

                    const color = dataset.borderColor || dataset.backgroundColor || palette[index % palette.length];
                    const fill = type === "line" ? false : true;

                    return {
                        label: dataset.label,
                        data: dataset.data || [],
                        borderColor: color,
                        backgroundColor: dataset.backgroundColor || color,
                        borderWidth: 2,
                        tension: 0.3,
                        fill: fill
                    };
                })
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        position: "bottom"
                    }
                },
                scales: type !== "doughnut" ? {
                    y: {
                        beginAtZero: true,
                        grid: {
                            color: "#E2E8F0"
                        }
                    },
                    x: {
                        grid: {
                            display: false
                        }
                    }
                } : undefined
            }
        });
    };
    // ── Area Chart (stacked submission volume) ─────────────────────────
    window.renderAreaChart = function (canvasId, labels, accepted, rejected, pending) {
        const canvas = document.getElementById(canvasId);
        if (!canvas || typeof Chart === "undefined") return;
        const ctx = canvas.getContext("2d");
        if (!ctx) return;
        if (chartRegistry[canvasId]) chartRegistry[canvasId].destroy();

        const gradientGreen = ctx.createLinearGradient(0, 0, 0, canvas.height);
        gradientGreen.addColorStop(0, "rgba(15,118,110,0.4)");
        gradientGreen.addColorStop(1, "rgba(15,118,110,0.02)");
        const gradientRed = ctx.createLinearGradient(0, 0, 0, canvas.height);
        gradientRed.addColorStop(0, "rgba(185,28,28,0.35)");
        gradientRed.addColorStop(1, "rgba(185,28,28,0.02)");
        const gradientAmber = ctx.createLinearGradient(0, 0, 0, canvas.height);
        gradientAmber.addColorStop(0, "rgba(217,119,6,0.35)");
        gradientAmber.addColorStop(1, "rgba(217,119,6,0.02)");

        chartRegistry[canvasId] = new Chart(ctx, {
            type: "line",
            data: {
                labels: labels,
                datasets: [
                    { label: "Accepted", data: accepted, borderColor: "#0f766e", backgroundColor: gradientGreen, fill: true, tension: 0.4, borderWidth: 2, pointRadius: 3 },
                    { label: "Rejected", data: rejected, borderColor: "#b91c1c", backgroundColor: gradientRed, fill: true, tension: 0.4, borderWidth: 2, pointRadius: 3 },
                    { label: "Pending", data: pending, borderColor: "#d97706", backgroundColor: gradientAmber, fill: true, tension: 0.4, borderWidth: 2, pointRadius: 3 }
                ]
            },
            options: {
                responsive: true, maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                animation: { duration: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : 800 },
                plugins: { legend: { position: "bottom", labels: { usePointStyle: true, padding: 16 } } },
                scales: {
                    y: { beginAtZero: true, stacked: false, grid: { color: "#E2E8F0" }, ticks: { precision: 0 } },
                    x: { grid: { display: false } }
                }
            }
        });
    };

    // ── Horizontal Bar Chart (top modules) ────────────────────────────
    window.renderHorizontalBarChart = function (canvasId, labels, values, compliance) {
        const canvas = document.getElementById(canvasId);
        if (!canvas || typeof Chart === "undefined") return;
        const ctx = canvas.getContext("2d");
        if (!ctx) return;
        if (chartRegistry[canvasId]) chartRegistry[canvasId].destroy();

        const colors = (compliance || []).map(function (c) {
            return c >= 90 ? "#0f766e" : c >= 70 ? "#d97706" : "#b91c1c";
        });

        chartRegistry[canvasId] = new Chart(ctx, {
            type: "bar",
            data: {
                labels: labels,
                datasets: [{ label: "Submissions", data: values, backgroundColor: colors, borderRadius: 4, borderWidth: 0 }]
            },
            options: {
                indexAxis: "y",
                responsive: true, maintainAspectRatio: false,
                animation: { duration: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : 700 },
                plugins: { legend: { display: false },
                    tooltip: { callbacks: { afterLabel: function (ctx) {
                        return compliance && compliance[ctx.dataIndex] !== undefined
                            ? "Compliance: " + compliance[ctx.dataIndex] + "%" : "";
                    }}}
                },
                scales: {
                    x: { beginAtZero: true, grid: { color: "#E2E8F0" }, ticks: { precision: 0 } },
                    y: { grid: { display: false } }
                }
            }
        });
    };

    // ── Heatmap (GitHub-style, pure Canvas 2D) ─────────────────────────
    window.renderHeatmap = function (canvasId, days) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        if (chartRegistry[canvasId]) { chartRegistry[canvasId] = null; }

        const cols = 13, rows = 7;
        const cellSize = 14, gap = 3;
        const leftPad = 30, topPad = 22;
        canvas.width = cols * (cellSize + gap) + leftPad;
        canvas.height = rows * (cellSize + gap) + topPad;
        const ctx = canvas.getContext("2d");
        if (!ctx) return;

        const intensityColors = ["#ebedf0", "#9be9a8", "#40c463", "#30a14e", "#216e39"];
        const dayLabels = ["", "Mon", "", "Wed", "", "Fri", ""];
        const monthNames = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"];

        // Draw day labels
        ctx.font = "10px sans-serif";
        ctx.fillStyle = "#64748b";
        for (let r = 0; r < rows; r++) {
            if (dayLabels[r]) ctx.fillText(dayLabels[r], 0, topPad + r * (cellSize + gap) + cellSize - 2);
        }

        // Draw month labels and cells
        let lastMonth = -1;
        days.forEach(function (day, i) {
            const col = Math.floor(i / rows);
            const row = i % rows;
            const x = leftPad + col * (cellSize + gap);
            const y = topPad + row * (cellSize + gap);
            const date = new Date(day.date);
            const month = date.getMonth();
            if (row === 0 && month !== lastMonth) {
                ctx.fillStyle = "#64748b";
                ctx.fillText(monthNames[month], x, topPad - 6);
                lastMonth = month;
            }
            const color = intensityColors[Math.min(day.intensity, 4)];
            ctx.fillStyle = color;
            ctx.beginPath();
            ctx.roundRect(x, y, cellSize, cellSize, 2);
            ctx.fill();
        });

        // Store tooltip handler
        canvas._heatmapDays = days;
        canvas.onmousemove = function (e) {
            const rect = canvas.getBoundingClientRect();
            const mx = e.clientX - rect.left, my = e.clientY - rect.top;
            const col = Math.floor((mx - leftPad) / (cellSize + gap));
            const row = Math.floor((my - topPad) / (cellSize + gap));
            const idx = col * rows + row;
            if (idx >= 0 && idx < days.length && col >= 0 && row >= 0 && row < rows) {
                const d = days[idx];
                canvas.title = new Date(d.date).toDateString() + ": " + d.count + " submission" + (d.count !== 1 ? "s" : "");
            } else {
                canvas.title = "";
            }
        };

        chartRegistry[canvasId] = { destroy: function () { canvas.onmousemove = null; canvas.title = ""; } };
    };

    // ── Animated Counter ──────────────────────────────────────────────
    window.animateCounter = function (elementId, targetValue, durationMs, suffix) {
        const el = document.getElementById(elementId);
        if (!el) return;
        if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
            el.textContent = (Number.isInteger(targetValue) ? targetValue : targetValue.toFixed(1)) + (suffix || "");
            return;
        }
        const start = performance.now();
        const from = 0;
        const to = parseFloat(targetValue);
        const isDecimal = !Number.isInteger(parseFloat(targetValue)) || String(targetValue).includes(".");
        function step(now) {
            const elapsed = now - start;
            const progress = Math.min(elapsed / durationMs, 1);
            const eased = 1 - Math.pow(1 - progress, 3);
            const current = from + (to - from) * eased;
            el.textContent = (isDecimal ? current.toFixed(1) : Math.round(current)) + (suffix || "");
            if (progress < 1) requestAnimationFrame(step);
        }
        requestAnimationFrame(step);
    };

    // ── Drag-and-Drop Widget Ordering ─────────────────────────────────
    window.initDragDrop = function (dotNetRef) {
        const STORAGE_KEY = "fc-dashboard-widget-order";
        let dragSrc = null;

        document.querySelectorAll(".portal-widget[draggable]").forEach(function (widget) {
            widget.addEventListener("dragstart", function (e) {
                dragSrc = widget;
                widget.classList.add("portal-widget-dragging");
                e.dataTransfer.effectAllowed = "move";
            });
            widget.addEventListener("dragend", function () {
                widget.classList.remove("portal-widget-dragging");
                document.querySelectorAll(".portal-widget").forEach(function (w) {
                    w.classList.remove("portal-widget-dragover");
                });
            });
            widget.addEventListener("dragover", function (e) {
                e.preventDefault();
                e.dataTransfer.dropEffect = "move";
                if (dragSrc && dragSrc !== widget) widget.classList.add("portal-widget-dragover");
            });
            widget.addEventListener("dragleave", function () {
                widget.classList.remove("portal-widget-dragover");
            });
            widget.addEventListener("drop", function (e) {
                e.preventDefault();
                widget.classList.remove("portal-widget-dragover");
                if (!dragSrc || dragSrc === widget) return;
                const widgets = Array.from(document.querySelectorAll(".portal-widget[draggable]"));
                const fromIdx = widgets.indexOf(dragSrc);
                const toIdx = widgets.indexOf(widget);
                if (fromIdx === -1 || toIdx === -1) return;
                const order = widgets.map(function (w) { return w.dataset.widgetId; });
                const moved = order.splice(fromIdx, 1)[0];
                order.splice(toIdx, 0, moved);
                localStorage.setItem(STORAGE_KEY, JSON.stringify(order));
                if (dotNetRef) dotNetRef.invokeMethodAsync("OnWidgetReordered", fromIdx, toIdx);
            });
        });
    };

    window.getDashboardWidgetOrder = function () {
        return localStorage.getItem("fc-dashboard-widget-order");
    };

    // ── Staggered Widget Reveal ───────────────────────────────────────
    window.renderStaggered = function (widgetIds, delayMs) {
        if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
            widgetIds.forEach(function (id) {
                const el = document.getElementById(id);
                if (el) el.classList.add("portal-widget-visible");
            });
            return;
        }
        widgetIds.forEach(function (id, i) {
            setTimeout(function () {
                const el = document.getElementById(id);
                if (el) el.classList.add("portal-widget-visible");
            }, i * (delayMs || 100));
        });
    };

})();
