/**
 * RG-33 — Regulatory Sandbox & Scenario Simulation
 * JS interop helpers for scenario pages.
 */
window.fcScenarios = (function () {
    'use strict';

    /** Sync horizontal scroll between two overflow containers */
    function syncScroll(sourceId, targetId) {
        const src = document.getElementById(sourceId);
        const tgt = document.getElementById(targetId);
        if (!src || !tgt) return;
        src.addEventListener('scroll', () => { tgt.scrollLeft = src.scrollLeft; }, { passive: true });
    }

    /** Animate a numeric value from → to over durationMs */
    function animateMetric(elementId, from, to, durationMs) {
        const el = document.getElementById(elementId);
        if (!el) return;
        const start = performance.now();
        const delta = to - from;
        function frame(now) {
            const t = Math.min((now - start) / durationMs, 1);
            const ease = 1 - Math.pow(1 - t, 3);               // ease-out cubic
            el.textContent = (from + delta * ease).toFixed(1);
            if (t < 1) requestAnimationFrame(frame);
        }
        requestAnimationFrame(frame);
    }

    /** Trigger browser print for a comparison view */
    function exportComparison(containerId) {
        const el = document.getElementById(containerId);
        if (!el) { window.print(); return; }
        el.classList.add('fc-sc-print-target');
        window.print();
        el.classList.remove('fc-sc-print-target');
    }

    return { syncScroll, animateMetric, exportComparison };
})();
