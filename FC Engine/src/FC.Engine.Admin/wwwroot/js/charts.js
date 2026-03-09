/**
 * RegOS™ Chart Module (fcCharts)
 * Wraps Chart.js with the design-system token palette, custom tooltips,
 * dark-mode awareness, and a clean init/destroy lifecycle for Blazor.
 */
(function () {
    'use strict';

    /* ── Registry ──────────────────────────────────────────────────── */
    const _reg = new Map(); // canvasId → Chart instance

    /* ── CSS variable helpers ──────────────────────────────────────── */
    function _cv(name) {
        return getComputedStyle(document.documentElement)
            .getPropertyValue(name).trim() || null;
    }

    function _resolveColor(c) {
        if (!c) return null;
        const map = {
            primary : '--cbn-primary',
            accent  : '--cbn-accent',
            info    : '--cbn-info',
            danger  : '--cbn-danger',
            success : '--cbn-success',
            warning : '--cbn-warning',
        };
        if (map[c]) return _cv(map[c]) || _fallback(c);
        if (c.startsWith('var(')) return _cv(c.slice(4, -1).trim()) || c;
        return c;
    }

    function _fallback(name) {
        return { primary:'#006B3F', accent:'#C8A415', info:'#0ea5e9',
                 danger:'#dc2626',  success:'#16a34a', warning:'#d97706' }[name] || '#888';
    }

    function _palette() {
        return [
            _cv('--cbn-primary')  || '#006B3F',
            _cv('--cbn-accent')   || '#C8A415',
            _cv('--cbn-info')     || '#0ea5e9',
            _cv('--cbn-danger')   || '#dc2626',
            '#7c3aed', '#0f766e',
        ];
    }

    function _grid()    { return _cv('--cbn-border')         || '#E2E8F0'; }
    function _text()    { return _cv('--cbn-text-secondary') || '#6b7280'; }
    function _surface() { return _cv('--cbn-surface')        || '#ffffff'; }
    function _textPrimary() { return _cv('--cbn-text-primary') || '#111827'; }

    /* ── Tooltip defaults (design-system styled) ───────────────────── */
    function _tooltip() {
        return {
            backgroundColor : _surface(),
            titleColor      : _textPrimary(),
            bodyColor       : _text(),
            borderColor     : _grid(),
            borderWidth     : 1,
            padding         : 12,
            cornerRadius    : 8,
            displayColors   : true,
            boxWidth        : 10,
            boxHeight       : 10,
            boxPadding      : 4,
            usePointStyle   : true,
        };
    }

    /* ── Dataset builders ──────────────────────────────────────────── */
    function _areaDs(ds, color) {
        const c40 = color + '40', c05 = color + '05';
        const useFill = ds.fill !== false;
        return {
            label            : ds.label || '',
            data             : ds.data  || [],
            borderColor      : color,
            backgroundColor  : useFill
                ? function (ctx) {
                    const chart = ctx.chart;
                    if (!chart.chartArea) return c40;
                    const g = chart.ctx.createLinearGradient(
                        0, chart.chartArea.top, 0, chart.chartArea.bottom);
                    g.addColorStop(0, c40);
                    g.addColorStop(1, c05);
                    return g;
                }
                : 'transparent',
            borderWidth          : 2,
            tension              : 0.4,
            fill                 : useFill,
            pointRadius          : 4,
            pointHoverRadius     : 6,
            pointBackgroundColor : color,
            pointBorderColor     : _surface(),
            pointBorderWidth     : 2,
        };
    }

    function _barDs(ds, color) {
        return {
            label                : ds.label || '',
            data                 : ds.data  || [],
            backgroundColor      : color + 'CC',
            hoverBackgroundColor : color,
            borderRadius         : 4,
            borderSkipped        : false,
            borderWidth          : 0,
        };
    }

    function _doughnutDs(ds, colors) {
        return {
            label                : ds.label || '',
            data                 : ds.data  || [],
            backgroundColor      : colors.map(c => c + 'CC'),
            hoverBackgroundColor : colors,
            hoverOffset          : 4,
            borderWidth          : 2,
            borderColor          : _surface(),
        };
    }

    function _scatterDs(ds, color) {
        return {
            label            : ds.label || '',
            data             : ds.data  || [],
            backgroundColor  : color + '80',
            borderColor      : color,
            borderWidth      : 1.5,
            pointRadius      : 5,
            pointHoverRadius : 7,
        };
    }

    /* ── Tick formatter factory (based on spec.yFormat) ───────────── */
    function _tickFmt(fmt) {
        if (!fmt) return undefined;
        if (fmt === 'currency') {
            return function (v) {
                const abs = Math.abs(v);
                if (abs >= 1e9)  return 'NGN ' + (v / 1e9).toFixed(1)  + 'B';
                if (abs >= 1e6)  return 'NGN ' + (v / 1e6).toFixed(1)  + 'M';
                if (abs >= 1e3)  return 'NGN ' + (v / 1e3).toFixed(0)  + 'K';
                return 'NGN ' + v.toFixed(0);
            };
        }
        if (fmt === 'compact') {
            return function (v) {
                const abs = Math.abs(v);
                if (abs >= 1e6) return (v / 1e6).toFixed(1) + 'M';
                if (abs >= 1e3) return (v / 1e3).toFixed(0) + 'K';
                return String(v);
            };
        }
        if (fmt === 'percent') return v => v + '%';
        return undefined;
    }

    /* ── Tooltip body formatter (based on spec.tooltipFormat) ─────── */
    function _tooltipCallbacks(fmt) {
        if (fmt === 'currency') {
            return {
                label: function (ctx) {
                    const v = typeof ctx.raw === 'object' ? ctx.raw.y : ctx.raw;
                    const abs = Math.abs(v);
                    let s;
                    if (abs >= 1e6) s = 'NGN ' + (v / 1e6).toFixed(2) + 'M';
                    else            s = 'NGN ' + Number(v).toLocaleString('en-NG', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                    return (ctx.dataset.label ? ctx.dataset.label + ': ' : '') + s;
                }
            };
        }
        return {};
    }

    /* ── Config builder ────────────────────────────────────────────── */
    function _build(spec) {
        const colors   = _palette();
        const type     = spec.type || 'bar';
        const labels   = spec.labels || [];
        const isMobile = typeof window !== 'undefined' && window.matchMedia('(max-width:640px)').matches;
        const isDough  = type === 'doughnut' || type === 'donut';

        /* datasets */
        let datasets;
        if (isDough) {
            const raw = (spec.datasets && spec.datasets[0]) || { label: '', data: [] };
            datasets = [_doughnutDs(raw, colors)];
        } else {
            datasets = (spec.datasets || []).map(function (ds, i) {
                const c = _resolveColor(ds.color) || colors[i % colors.length];
                if (type === 'line')    return _areaDs(ds, c);
                if (type === 'scatter') return _scatterDs(ds, c);
                return _barDs(ds, c);
            });
        }

        /* scales (not for doughnut) */
        let scales;
        if (!isDough) {
            const horiz = spec.indexAxis === 'y';
            const yFmt  = _tickFmt(spec.yFormat);
            const xFmt  = _tickFmt(spec.xFormat);
            scales = {
                y: {
                    beginAtZero : true,
                    grid        : { color: horiz ? 'transparent' : _grid() },
                    ticks       : {
                        color        : _text(),
                        font         : { size: isMobile ? 10 : 11 },
                        maxTicksLimit: isMobile ? 4 : 6,
                        callback     : yFmt,
                    },
                    border: { display: false },
                    title : spec.yLabel
                        ? { display: true, text: spec.yLabel, color: _text(), font: { size: 11 } }
                        : undefined,
                },
                x: {
                    beginAtZero : type === 'scatter' ? false : true,
                    grid        : { display: !horiz ? false : true, color: horiz ? _grid() : 'transparent' },
                    ticks       : {
                        color       : _text(),
                        font        : { size: isMobile ? 10 : 11 },
                        maxRotation : isMobile ? 45 : 0,
                        maxTicksLimit: isMobile ? 4 : undefined,
                        callback    : xFmt,
                    },
                    border: { display: false },
                    title : spec.xLabel
                        ? { display: true, text: spec.xLabel, color: _text(), font: { size: 11 } }
                        : undefined,
                },
            };
        }

        if (scales && spec.stacked) {
            scales.x.stacked = true;
            scales.y.stacked = true;
        }

        return {
            type : isDough ? 'doughnut' : type,
            data : { labels, datasets },
            options: {
                responsive          : true,
                maintainAspectRatio : false,
                animation           : { duration: 800, easing: 'easeOutQuart' },
                indexAxis           : spec.indexAxis || 'x',
                plugins: {
                    legend: {
                        display  : spec.legend !== false,
                        position : isDough ? (isMobile ? 'bottom' : 'right') : 'bottom',
                        labels   : {
                            color        : _text(),
                            font         : { size: isMobile ? 11 : 12 },
                            boxWidth     : 12,
                            padding      : isMobile ? 8 : 16,
                            usePointStyle: true,
                        },
                    },
                    tooltip: {
                        ..._tooltip(),
                        callbacks: _tooltipCallbacks(spec.tooltipFormat),
                    },
                },
                scales : scales,
                cutout : isDough ? '65%' : undefined,
            },
        };
    }

    /* ── Key helper ────────────────────────────────────────────────── */
    function _key(elOrId) {
        if (typeof elOrId === 'string') return elOrId;
        if (elOrId && elOrId.id) return elOrId.id;
        if (elOrId) {
            const k = 'fc-c-' + Math.random().toString(36).slice(2);
            elOrId.id = k;
            return k;
        }
        return null;
    }

    function _el(elOrId) {
        if (typeof elOrId === 'string') return document.getElementById(elOrId);
        return elOrId;
    }

    /* ── Public API ────────────────────────────────────────────────── */

    /**
     * Initialise (or re-initialise) a chart on the given canvas.
     * @param {HTMLCanvasElement|string} elOrId  Canvas element or its id.
     * @param {object}                   spec     Chart spec (see README).
     * @param {DotNetObjectReference}   [dotNetRef] Optional Blazor interop ref.
     */
    function init(elOrId, spec, dotNetRef) {
        if (typeof Chart === 'undefined') {
            console.warn('[fcCharts] Chart.js is not loaded');
            return;
        }
        const el  = _el(elOrId);
        if (!el)  return;
        const key = _key(el);

        // Destroy existing instance if any
        if (_reg.has(key)) {
            _reg.get(key).destroy();
            _reg.delete(key);
        }

        try {
            const chart = new Chart(el, _build(spec));
            _reg.set(key, chart);
        } catch (err) {
            console.error('[fcCharts] init error', err);
        }
    }

    /**
     * Destroy the Chart.js instance for the given canvas.
     */
    function destroy(elOrId) {
        const key = typeof elOrId === 'string' ? elOrId : _key(_el(elOrId));
        if (key && _reg.has(key)) {
            _reg.get(key).destroy();
            _reg.delete(key);
        }
    }

    /**
     * Re-apply design-system colours after a theme change.
     * Call this whenever dark-mode is toggled.
     */
    function updateTheme(elOrId) {
        const key   = typeof elOrId === 'string' ? elOrId : _key(_el(elOrId));
        const chart = key && _reg.get(key);
        if (!chart) return;
        const tt = chart.options.plugins && chart.options.plugins.tooltip;
        if (tt) Object.assign(tt, _tooltip());
        chart.update('none');
    }

    window.fcCharts = { init, destroy, updateTheme };

    /* ── Backward-compat shim ──────────────────────────────────────── */
    window.renderChart = function (canvasId, type, data) {
        init(canvasId, { type: type, labels: data.labels, datasets: data.datasets });
    };

})();
