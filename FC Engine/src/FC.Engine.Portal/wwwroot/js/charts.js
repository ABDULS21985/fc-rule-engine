(function () {
    const chartRegistry = window.__fcCharts || (window.__fcCharts = {});

    // ── Global Tooltip Defaults (portal design system) ─────────────────
    // Applied once when Chart.js is available; all subsequent charts inherit these.
    if (typeof Chart !== 'undefined') {
        Chart.defaults.plugins.tooltip.backgroundColor = '#0f172a';
        Chart.defaults.plugins.tooltip.titleColor = '#f1f5f9';
        Chart.defaults.plugins.tooltip.bodyColor = '#e2e8f0';
        Chart.defaults.plugins.tooltip.borderColor = 'rgba(255,255,255,0.08)';
        Chart.defaults.plugins.tooltip.borderWidth = 1;
        Chart.defaults.plugins.tooltip.cornerRadius = 8;
        Chart.defaults.plugins.tooltip.padding = { x: 10, y: 8 };
        Chart.defaults.plugins.tooltip.titleFont = { size: 12, weight: '600' };
        Chart.defaults.plugins.tooltip.bodyFont = { size: 12 };

        // Global interaction defaults
        Chart.defaults.interaction.mode = 'index';
        Chart.defaults.interaction.intersect = false;

        // Global legend defaults
        Chart.defaults.plugins.legend.labels.usePointStyle = true;
        Chart.defaults.plugins.legend.labels.pointStyle = 'circle';
        Chart.defaults.plugins.legend.labels.padding = 14;
        Chart.defaults.plugins.legend.labels.font = { size: 11 };
    }

    var animDuration = window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 600;

    // Helper: convert hex color to "r,g,b" string for use in rgba()
    function hexToRgb(hex) {
        hex = hex.replace('#', '');
        if (hex.length === 3) hex = hex[0]+hex[0]+hex[1]+hex[1]+hex[2]+hex[2];
        var r = parseInt(hex.substring(0, 2), 16);
        var g = parseInt(hex.substring(2, 4), 16);
        var b = parseInt(hex.substring(4, 6), 16);
        return r + ',' + g + ',' + b;
    }

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

                    if (type === "line") {
                        // Build gradient fill for line charts
                        var gradient = ctx.createLinearGradient(0, 0, 0, canvas.height);
                        var rgb = hexToRgb(color);
                        gradient.addColorStop(0, "rgba(" + rgb + ",0.35)");
                        gradient.addColorStop(1, "rgba(" + rgb + ",0.02)");

                        return {
                            label: dataset.label,
                            data: dataset.data || [],
                            borderColor: color,
                            backgroundColor: gradient,
                            borderWidth: 2,
                            tension: 0.4,
                            fill: true,
                            pointRadius: 0,
                            pointHoverRadius: 5
                        };
                    }

                    if (type === "doughnut") {
                        return {
                            label: dataset.label,
                            data: dataset.data || [],
                            borderColor: color,
                            backgroundColor: dataset.backgroundColor || color,
                            borderWidth: 0,
                            hoverOffset: 6
                        };
                    }

                    return {
                        label: dataset.label,
                        data: dataset.data || [],
                        borderColor: color,
                        backgroundColor: dataset.backgroundColor || color,
                        borderWidth: 2,
                        tension: 0.3,
                        fill: true
                    };
                })
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: animDuration },
                plugins: {
                    legend: {
                        position: "bottom"
                    }
                },
                ...(type === "doughnut" ? { cutout: "68%", spacing: 3 } : {}),
                scales: type !== "doughnut" ? {
                    y: {
                        beginAtZero: true,
                        grid: {
                            color: "rgba(148, 163, 184, 0.12)"
                        },
                        ticks: { font: { size: 11 } }
                    },
                    x: {
                        grid: {
                            display: false
                        },
                        ticks: { font: { size: 11 } }
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
                animation: { duration: animDuration },
                plugins: { legend: { position: "bottom", labels: { usePointStyle: true, padding: 16 } } },
                scales: {
                    y: { beginAtZero: true, stacked: false, grid: { color: "rgba(148, 163, 184, 0.12)" }, ticks: { precision: 0, font: { size: 11 } } },
                    x: { grid: { display: false } }
                }
            }
        });
    };

    // ── Horizontal Bar Chart (top modules) ────────────────────────────
    window.renderHorizontalBarChart = function (canvasId, labels, values, compliance, dotNetRef) {
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
                datasets: [{ label: "Submissions", data: values, backgroundColor: colors, borderRadius: 6, borderWidth: 0, barThickness: 18 }]
            },
            options: {
                indexAxis: "y",
                responsive: true, maintainAspectRatio: false,
                animation: { duration: animDuration },
                plugins: {
                    legend: { display: false },
                    tooltip: { callbacks: { afterLabel: function (ctx) {
                        return compliance && compliance[ctx.dataIndex] !== undefined
                            ? "Compliance: " + compliance[ctx.dataIndex] + "%" : "";
                    }}}
                },
                onClick: function (evt, elements) {
                    if (elements.length && dotNetRef) {
                        var idx = elements[0].index;
                        dotNetRef.invokeMethodAsync('OnChartSegmentClick', canvasId, idx, labels[idx]);
                    }
                },
                scales: {
                    x: { beginAtZero: true, grid: { color: "rgba(148, 163, 184, 0.12)" }, ticks: { precision: 0, font: { size: 11 } } },
                    y: { grid: { display: false }, ticks: { font: { size: 11 } } }
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

        const darkMode = document.documentElement.getAttribute('data-theme') === 'dark';
        const intensityColors = darkMode
            ? ["#161b22", "#0e4429", "#006d32", "#26a641", "#39d353"]
            : ["#ebedf0", "#9be9a8", "#40c463", "#30a14e", "#216e39"];
        const labelColor = darkMode ? "#94A3B8" : "#64748b";
        const dayLabels = ["", "Mon", "", "Wed", "", "Fri", ""];
        const monthNames = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"];

        // Draw day labels
        ctx.font = "10px sans-serif";
        ctx.fillStyle = labelColor;
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
                ctx.fillStyle = labelColor;
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

    // ── Donut Chart with per-segment colors ──────────────────────────
    window.renderDonutWithColors = function (canvasId, labels, values, colors, dotNetRef) {
        const canvas = document.getElementById(canvasId);
        if (!canvas || typeof Chart === "undefined") return;
        const ctx = canvas.getContext("2d");
        if (!ctx) return;
        if (chartRegistry[canvasId]) chartRegistry[canvasId].destroy();

        chartRegistry[canvasId] = new Chart(ctx, {
            type: "doughnut",
            data: {
                labels: labels,
                datasets: [{ data: values, backgroundColor: colors, borderWidth: 0, hoverOffset: 8 }]
            },
            options: {
                responsive: true, maintainAspectRatio: false,
                animation: { duration: animDuration },
                plugins: {
                    legend: { position: "bottom", labels: { usePointStyle: true, padding: 12, font: { size: 11 } } },
                    tooltip: { callbacks: { label: function (ctx) { return " " + ctx.label + ": " + ctx.formattedValue + "%"; } } }
                },
                onClick: function (evt, elements) {
                    if (elements.length && dotNetRef) {
                        var idx = elements[0].index;
                        dotNetRef.invokeMethodAsync('OnChartSegmentClick', canvasId, idx, labels[idx]);
                    }
                },
                cutout: "68%",
                spacing: 4
            }
        });
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

    // ── Dark Mode Chart Theming ───────────────────────────────────────
    window.portalUpdateChartsTheme = function (isDark) {
        var gridColor = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(148, 163, 184, 0.12)';
        var tickColor = isDark ? '#94A3B8' : '#6B7280';
        var bgColor   = isDark ? '#1E293B' : '#FFFFFF';
        if (typeof Chart !== 'undefined') {
            Chart.defaults.color = tickColor;
            Chart.defaults.backgroundColor = bgColor;
            if (Chart.defaults.scale) {
                if (Chart.defaults.scale.grid)  Chart.defaults.scale.grid.color  = gridColor;
                if (Chart.defaults.scale.ticks) Chart.defaults.scale.ticks.color = tickColor;
            }
            Object.values(Chart.instances).forEach(function (chart) {
                if (chart.options && chart.options.scales) {
                    Object.values(chart.options.scales).forEach(function (scale) {
                        if (scale.grid)  scale.grid.color  = gridColor;
                        if (scale.ticks) scale.ticks.color = tickColor;
                    });
                }
                chart.update('none');
            });
        }
    };

})();
