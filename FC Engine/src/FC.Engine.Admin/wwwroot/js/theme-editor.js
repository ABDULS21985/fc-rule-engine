/**
 * fc-theme-editor.js  —  Theme & Branding editor utilities
 * Loaded on the /settings/theme page.
 */

window.fcThemeEditor = (function () {
    'use strict';

    /** Cache of loaded Google Font family names to avoid duplicate <link> injections. */
    const _loadedFonts = new Set();

    /**
     * Dynamically inject a Google Fonts <link> for the given family name.
     * Idempotent — calling multiple times for the same font is safe.
     *
     * @param {string} fontName  e.g. "Inter", "Plus Jakarta Sans"
     */
    function loadGoogleFont(fontName) {
        if (!fontName || _loadedFonts.has(fontName)) return;

        // Check whether the font is already applied by the browser
        const slug = fontName.replace(/\s+/g, '+');
        const href = `https://fonts.googleapis.com/css2?family=${slug}:wght@400;500;600;700&display=swap`;

        // Avoid duplicate <link> tags
        const existing = document.querySelector(`link[href="${href}"]`);
        if (existing) { _loadedFonts.add(fontName); return; }

        const link = document.createElement('link');
        link.rel  = 'stylesheet';
        link.href = href;
        document.head.appendChild(link);
        _loadedFonts.add(fontName);
    }

    /**
     * Trigger a browser download of the given JSON string as a .json file.
     *
     * @param {string} json      Serialised JSON content
     * @param {string} filename  Suggested filename (e.g. "theme-custom-20260306.json")
     */
    function downloadJson(json, filename) {
        const blob = new Blob([json], { type: 'application/json' });
        const url  = URL.createObjectURL(blob);
        const a    = Object.assign(document.createElement('a'), {
            href:     url,
            download: filename || 'theme.json',
        });
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    /**
     * Calculate the WCAG 2.1 contrast ratio between two hex colours.
     * Returns a number, e.g. 4.56 (meaning 4.56:1).
     *
     * @param {string} hex1  e.g. "#006B3F"
     * @param {string} hex2  e.g. "#FFFFFF"
     * @returns {number}
     */
    function computeContrast(hex1, hex2) {
        const l1 = _relativeLuminance(_parseHex(hex1));
        const l2 = _relativeLuminance(_parseHex(hex2));
        const lighter = Math.max(l1, l2);
        const darker  = Math.min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /**
     * Given a contrast ratio, return the WCAG grade string:
     * "AAA" (≥7), "AA" (≥4.5), "AA Large" (≥3), or "Fail".
     *
     * @param {number} ratio
     * @returns {string}
     */
    function contrastGrade(ratio) {
        if (ratio >= 7.0)  return 'AAA';
        if (ratio >= 4.5)  return 'AA';
        if (ratio >= 3.0)  return 'AA Large';
        return 'Fail';
    }

    /**
     * Lighten a hex colour by mixing it toward white.
     *
     * @param {string} hex    Input colour, e.g. "#006B3F"
     * @param {number} factor Amount to lighten, 0–1 (e.g. 0.85)
     * @returns {string}  Lightened hex colour
     */
    function lightenColor(hex, factor) {
        const { r, g, b } = _parseHex(hex);
        const mix = (c) => Math.round(c + (255 - c) * factor);
        return _toHex(mix(r), mix(g), mix(b));
    }

    /**
     * Darken a hex colour by reducing RGB channels.
     *
     * @param {string} hex    Input colour, e.g. "#C8A415"
     * @param {number} factor Amount to darken, 0–1 (e.g. 0.15)
     * @returns {string}  Darkened hex colour
     */
    function darkenColor(hex, factor) {
        const { r, g, b } = _parseHex(hex);
        const darken = (c) => Math.round(c * (1 - factor));
        return _toHex(darken(r), darken(g), darken(b));
    }

    // ── Private helpers ─────────────────────────────────────────────

    function _parseHex(hex) {
        const s = (hex || '').replace('#', '').trim();
        const v = s.length === 6 ? s : '006B3F';
        return {
            r: parseInt(v.substring(0, 2), 16),
            g: parseInt(v.substring(2, 4), 16),
            b: parseInt(v.substring(4, 6), 16),
        };
    }

    function _relativeLuminance({ r, g, b }) {
        const sRGB = (c) => {
            const v = c / 255;
            return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
        };
        return 0.2126 * sRGB(r) + 0.7152 * sRGB(g) + 0.0722 * sRGB(b);
    }

    function _toHex(r, g, b) {
        const clamp = (v) => Math.max(0, Math.min(255, v));
        return '#' + [r, g, b].map((c) => clamp(c).toString(16).padStart(2, '0')).join('');
    }

    // ── Public API ──────────────────────────────────────────────────
    return {
        loadGoogleFont,
        downloadJson,
        computeContrast,
        contrastGrade,
        lightenColor,
        darkenColor,
    };
})();
