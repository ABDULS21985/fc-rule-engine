// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  FC ENGINE PORTAL — Keyboard-First Navigation                              ║
// ║  window.portalShortcuts                                                    ║
// ║                                                                            ║
// ║  Features:                                                                 ║
// ║  • G→chord navigation  (G+S/C/T/N/H, N+R/B)                              ║
// ║  • Form shortcuts      (Ctrl+S = save draft, F1 = field help)             ║
// ║  • Table keyboard nav  (↑↓ focus rows, ↵ open, Space toggle)              ║
// ║  • Discovery tooltips  (hint after 5 mouse-nav uses of same route)        ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

window.portalShortcuts = (function () {
    'use strict';

    // ── State ──────────────────────────────────────────────────────────────────
    let _dotNetRef = null;
    let _kbHandler = null;
    let _chordKey   = null;
    let _chordTimer = null;
    let _tooltipEl  = null;

    // ── Chord map: first key → { second key → route } ─────────────────────────
    const CHORD_ROUTES = {
        g: { s: '/submissions', c: '/calendar', t: '/templates', n: '/notifications', h: '/help' },
        n: { r: '/submit',      b: '/submit/bulk' },
    };

    const CHORD_TIMEOUT_MS = 1500;
    const DISCOVERY_THRESHOLD = 5;
    const DISCOVERY_PREFIX = 'fc_disc_';

    // Labels shown in discovery tooltips (chord key → human hint)
    const CHORD_HINTS = {
        'g+s': { keys: ['G', 'S'], label: 'Jump to Submissions' },
        'g+c': { keys: ['G', 'C'], label: 'Jump to Calendar' },
        'g+t': { keys: ['G', 'T'], label: 'Jump to Templates' },
        'g+n': { keys: ['G', 'N'], label: 'Jump to Notifications' },
        'g+h': { keys: ['G', 'H'], label: 'Jump to Help' },
    };

    // Route → chord key for discovery tooltip lookup
    const ROUTE_TO_CHORD = {
        '/submissions':   'g+s',
        '/calendar':      'g+c',
        '/templates':     'g+t',
        '/notifications': 'g+n',
        '/help':          'g+h',
    };

    // ── Input detection ────────────────────────────────────────────────────────
    function isTyping() {
        const el = document.activeElement;
        if (!el) return false;
        const tag = el.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
        if (el.isContentEditable) return true;
        if (el.closest('.cm-editor, .ace_editor, [data-editor]')) return true;
        return false;
    }

    function isInTable() {
        const el = document.activeElement;
        return !!el && !!el.closest('table, [role="grid"]');
    }

    function getFocusedFieldId() {
        const el = document.activeElement;
        if (!el) return '';
        return el.dataset.fieldCode || el.dataset.field || el.id || '';
    }

    // ── Chord state machine ────────────────────────────────────────────────────
    function startChord(key) {
        clearChordTimer();
        _chordKey   = key.toLowerCase();
        _chordTimer = setTimeout(cancelChord, CHORD_TIMEOUT_MS);
    }

    function cancelChord() {
        _chordKey   = null;
        _chordTimer = null;
    }

    function clearChordTimer() {
        if (_chordTimer) { clearTimeout(_chordTimer); _chordTimer = null; }
    }

    /** Returns true and fires navigation if chord is complete. */
    function tryCompleteChord(secondKey) {
        if (!_chordKey) return false;
        const first  = _chordKey;
        const second = secondKey.toLowerCase();
        clearChordTimer();
        _chordKey = null;

        const route = (CHORD_ROUTES[first] || {})[second];
        if (route && _dotNetRef) {
            markShortcutUsed(`${first}+${second}`);
            _dotNetRef.invokeMethodAsync('OnNavigationShortcut', route).catch(() => {});
            return true;
        }
        return false;
    }

    // ── Global keydown handler ─────────────────────────────────────────────────
    function handleKeyDown(e) {
        // Ctrl+S / ⌘+S — save draft (skipped inside tables; works inside form inputs)
        if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 's') {
            if (!isInTable()) {
                e.preventDefault();
                if (_dotNetRef) _dotNetRef.invokeMethodAsync('OnGlobalShortcut', 'ctrl+s').catch(() => {});
            }
            return;
        }

        // F1 — field-level help
        if (e.key === 'F1') {
            e.preventDefault();
            if (_dotNetRef) {
                const fieldId = getFocusedFieldId();
                _dotNetRef.invokeMethodAsync('OnGlobalShortcut', 'f1:' + fieldId).catch(() => {});
            }
            return;
        }

        // All remaining shortcuts require the user to NOT be typing and no modifiers
        if (isTyping()) return;
        if (e.ctrlKey || e.altKey || e.metaKey) return;

        const key = e.key;

        // Second key of a chord in progress
        if (_chordKey && key.length === 1) {
            if (tryCompleteChord(key)) {
                e.preventDefault();
                return;
            }
        }

        // First key of a chord
        if (CHORD_ROUTES[key.toLowerCase()]) {
            startChord(key);
            return;
        }
    }

    // ── Table keyboard navigation ──────────────────────────────────────────────
    const _tableInstances = new WeakSet();

    function initTableNav() {
        document.querySelectorAll('[data-keyboard-nav="true"]').forEach(table => {
            if (_tableInstances.has(table)) return;
            _tableInstances.add(table);

            table.addEventListener('keydown', handleTableKeyDown);

            // Ensure first row is focusable; all others reachable via arrow nav
            const rows = getTableRows(table);
            rows.forEach((row, i) => {
                if (!row.getAttribute('tabindex')) {
                    row.setAttribute('tabindex', i === 0 ? '0' : '-1');
                }
            });
        });
    }

    function getTableRows(table) {
        return Array.from(table.querySelectorAll('tbody tr:not([hidden]):not(.portal-table-empty-row)'));
    }

    function handleTableKeyDown(e) {
        // Skip if focus is inside a cell control (input, button, etc.)
        if (e.target !== e.currentTarget && e.target.closest('input, button, a, select, textarea')) return;

        const table = e.currentTarget;
        const rows  = getTableRows(table);
        const focusedRow = document.activeElement?.closest('tr');
        const idx = focusedRow ? rows.indexOf(focusedRow) : -1;

        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                if (idx < rows.length - 1) focusRow(rows[idx + 1], rows);
                break;
            case 'ArrowUp':
                e.preventDefault();
                if (idx > 0) focusRow(rows[idx - 1], rows);
                break;
            case 'Home':
                e.preventDefault();
                if (rows.length) focusRow(rows[0], rows);
                break;
            case 'End':
                e.preventDefault();
                if (rows.length) focusRow(rows[rows.length - 1], rows);
                break;
            case 'Enter':
                if (focusedRow) {
                    e.preventDefault();
                    // Prefer an explicit row-link; fall back to clicking the row itself
                    const link = focusedRow.querySelector('[data-row-link]');
                    (link || focusedRow).click();
                }
                break;
            // Space is handled by Blazor's @onkeydown — don't interfere
        }
    }

    function focusRow(row, allRows) {
        allRows.forEach(r => {
            r.setAttribute('tabindex', '-1');
            r.classList.remove('portal-row-focused');
        });
        row.setAttribute('tabindex', '0');
        row.classList.add('portal-row-focused');
        row.focus({ preventScroll: false });
    }

    // ── Discovery tooltips ─────────────────────────────────────────────────────
    function trackMouseNav(pathname) {
        // Normalise (strip trailing slash except root)
        const route = pathname.length > 1 ? pathname.replace(/\/$/, '') : '/';
        const chord = ROUTE_TO_CHORD[route];
        if (!chord || isShortcutUsed(chord)) return;

        const storageKey = DISCOVERY_PREFIX + 'count_' + chord;
        const count = (parseInt(localStorage.getItem(storageKey) || '0', 10)) + 1;
        localStorage.setItem(storageKey, String(count));

        if (count === DISCOVERY_THRESHOLD) {
            const hint = CHORD_HINTS[chord];
            if (hint) showDiscoveryTooltip(hint.keys, hint.label, chord);
        }
    }

    function markShortcutUsed(chord) {
        localStorage.setItem(DISCOVERY_PREFIX + 'used_' + chord, '1');
        hideDiscoveryTooltip();
    }

    function isShortcutUsed(chord) {
        return localStorage.getItem(DISCOVERY_PREFIX + 'used_' + chord) === '1';
    }

    function showDiscoveryTooltip(keys, label, chord) {
        hideDiscoveryTooltip();

        const el = document.createElement('div');
        el.className = 'portal-discovery-tip';
        el.setAttribute('role', 'status');
        el.setAttribute('aria-live', 'polite');
        el.setAttribute('aria-label', `Keyboard tip: ${label}`);

        const kbdHtml = keys.map((k, i) =>
            `${i > 0 ? '<span class="portal-disc-then" aria-hidden="true">then</span>' : ''}<kbd>${k}</kbd>`
        ).join('');

        el.innerHTML = `
            <svg class="portal-disc-icon" width="14" height="14" viewBox="0 0 24 24"
                 fill="none" stroke="currentColor" stroke-width="2"
                 stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <path d="M9 21h6M12 3a6 6 0 016 6c0 2.22-1.2 4.16-3 5.2V17H9v-2.8C7.2 13.16 6 11.22 6 9a6 6 0 016-6z"/>
            </svg>
            <span class="portal-disc-body">
                <span class="portal-disc-tip-label">Tip: </span>
                <span class="portal-disc-text">${label} — press </span>
                <span class="portal-disc-keys">${kbdHtml}</span>
            </span>
            <button class="portal-disc-dismiss" type="button" aria-label="Dismiss tip">×</button>
        `;

        el.querySelector('.portal-disc-dismiss').addEventListener('click', () => {
            markShortcutUsed(chord);
        });

        document.body.appendChild(el);
        _tooltipEl = el;

        // Animate in on next frame
        requestAnimationFrame(() => el.classList.add('portal-discovery-tip--visible'));

        // Auto-dismiss after 9 s
        setTimeout(hideDiscoveryTooltip, 9000);
    }

    function hideDiscoveryTooltip() {
        if (!_tooltipEl) return;
        const el = _tooltipEl;
        _tooltipEl = null;
        el.classList.remove('portal-discovery-tip--visible');
        el.addEventListener('transitionend', () => el.remove(), { once: true });
        // Fallback remove in case transitionend never fires
        setTimeout(() => el.remove(), 400);
    }

    // ── Mouse navigation tracking (click on nav links) ─────────────────────────
    function initMouseTracking() {
        document.addEventListener('click', e => {
            const link = e.target.closest('a[href]');
            if (!link) return;
            const href = link.getAttribute('href') || '';
            if (!href || href.startsWith('#') || href.startsWith('javascript') || href.startsWith('mailto')) return;
            // Only internal links
            if (link.hostname && link.hostname !== location.hostname) return;
            trackMouseNav(link.pathname || href);
        }, true);
    }

    // ── Public API ─────────────────────────────────────────────────────────────
    function init(dotNetRef) {
        _dotNetRef = dotNetRef;
        _kbHandler = handleKeyDown;
        document.addEventListener('keydown', _kbHandler);
        initMouseTracking();
        initTableNav();
    }

    function dispose() {
        if (_kbHandler) {
            document.removeEventListener('keydown', _kbHandler);
            _kbHandler = null;
        }
        clearChordTimer();
        cancelChord();
        hideDiscoveryTooltip();
        _dotNetRef = null;
    }

    /** Call after Blazor navigation so newly rendered tables get keyboard nav. */
    function reinitTableNav() {
        // Small delay to let Blazor finish rendering the new page
        setTimeout(initTableNav, 150);
    }

    return { init, dispose, reinitTableNav };
})();
