/**
 * FCAccessibility — Accessibility utilities for FC Engine Admin Portal
 * Covers: focus trap, keyboard shortcuts, route announcer, high-contrast detection.
 */
window.FCAccessibility = (function () {
    'use strict';

    // ── Selectors for focusable elements ──────────────────────────────────────
    var FOCUSABLE_SELECTORS = [
        'a[href]:not([hidden])',
        'button:not([disabled]):not([hidden])',
        'input:not([disabled]):not([hidden])',
        'select:not([disabled]):not([hidden])',
        'textarea:not([disabled]):not([hidden])',
        '[tabindex]:not([tabindex="-1"]):not([hidden])',
        '[contenteditable="true"]:not([hidden])'
    ].join(',');

    function getFocusableElements(container) {
        return Array.from(container.querySelectorAll(FOCUSABLE_SELECTORS))
            .filter(function (el) {
                // Exclude elements inside hidden parents
                return el.offsetParent !== null || el.tagName === 'BODY';
            });
    }

    // ── Focus Trap ────────────────────────────────────────────────────────────
    var _trapStack = []; // { el, trigger, tabHandler }

    function trapFocus(elementId) {
        var el = document.getElementById(elementId);
        if (!el) return;

        // Store element that had focus before trap
        var trigger = document.activeElement;

        // Focus first focusable element inside, or the container itself
        var focusable = getFocusableElements(el);
        requestAnimationFrame(function () {
            if (focusable.length > 0) {
                focusable[0].focus();
            } else {
                el.focus();
            }
        });

        function tabHandler(e) {
            if (e.key !== 'Tab') return;

            var items = getFocusableElements(el);
            if (items.length === 0) {
                e.preventDefault();
                return;
            }

            var first = items[0];
            var last = items[items.length - 1];
            var active = document.activeElement;

            if (e.shiftKey) {
                // Shift+Tab: wrap from first to last
                if (active === first || !el.contains(active)) {
                    e.preventDefault();
                    last.focus();
                }
            } else {
                // Tab: wrap from last to first
                if (active === last || !el.contains(active)) {
                    e.preventDefault();
                    first.focus();
                }
            }
        }

        el.addEventListener('keydown', tabHandler);
        _trapStack.push({ el: el, trigger: trigger, tabHandler: tabHandler });
    }

    function releaseFocus(elementId) {
        for (var i = _trapStack.length - 1; i >= 0; i--) {
            var frame = _trapStack[i];
            if (!elementId || (frame.el && frame.el.id === elementId)) {
                frame.el.removeEventListener('keydown', frame.tabHandler);
                var prev = frame.trigger;
                _trapStack.splice(i, 1);
                // Return focus to element that opened the dialog
                if (prev && typeof prev.focus === 'function' && document.contains(prev)) {
                    requestAnimationFrame(function () {
                        try { prev.focus(); } catch (_) { /* ignore */ }
                    });
                }
                break;
            }
        }
    }

    // ── Route / Page Change Announcer ─────────────────────────────────────────
    var _announcer = null;

    function initRouteAnnouncer() {
        _announcer = document.getElementById('fc-route-announcer');
    }

    function announceRoute(pageName) {
        if (!_announcer) _announcer = document.getElementById('fc-route-announcer');
        if (!_announcer) return;
        // Clear first, then set on next frame — ensures re-announcement of same text
        _announcer.textContent = '';
        requestAnimationFrame(function () {
            requestAnimationFrame(function () {
                _announcer.textContent = 'Navigated to ' + pageName;
            });
        });
    }

    // ── Keyboard Shortcuts ────────────────────────────────────────────────────
    var _shortcutsRef = null;
    var _shortcutsHandler = null;
    var _chordState = { key: null, timer: null };
    var CHORD_TIMEOUT_MS = 1200;

    // Navigation chord map: g+<key> → path
    var NAV_CHORDS = {
        'd': '/',
        't': '/templates',
        's': '/submissions',
        'a': '/audit',
        'f': '/formulas',
        'u': '/users',
        'p': '/dashboard/platform',
        'j': '/jurisdictions',
        'r': '/business-rules'
    };

    function isInTextField() {
        var el = document.activeElement;
        if (!el) return false;
        var tag = el.tagName.toLowerCase();
        return tag === 'input' || tag === 'textarea' || tag === 'select' ||
            el.isContentEditable ||
            el.getAttribute('role') === 'textbox' ||
            el.getAttribute('role') === 'searchbox';
    }

    function initShortcuts(dotNetRef) {
        _shortcutsRef = dotNetRef;

        _shortcutsHandler = function (e) {
            // Never intercept when modifier keys active (except Escape/?)
            var hasModifier = e.ctrlKey || e.metaKey || e.altKey;

            // Escape — close overlay (no modifier check)
            if (e.key === 'Escape') {
                if (_shortcutsRef) _shortcutsRef.invokeMethodAsync('CloseOverlay');
                _chordState.key = null;
                return; // let other handlers also process Escape
            }

            if (hasModifier) return;
            if (isInTextField()) { _chordState.key = null; return; }

            // ? — toggle shortcuts overlay
            if (e.key === '?' || (e.shiftKey && e.key === '/')) {
                e.preventDefault();
                if (_shortcutsRef) _shortcutsRef.invokeMethodAsync('ToggleOverlay');
                _chordState.key = null;
                return;
            }

            // g chord: press g, then a navigation key
            if (e.key === 'g' && !_chordState.key) {
                e.preventDefault();
                _chordState.key = 'g';
                if (_chordState.timer) clearTimeout(_chordState.timer);
                _chordState.timer = setTimeout(function () {
                    _chordState.key = null;
                    _chordState.timer = null;
                }, CHORD_TIMEOUT_MS);
                return;
            }

            if (_chordState.key === 'g') {
                clearTimeout(_chordState.timer);
                _chordState.key = null;
                _chordState.timer = null;
                var dest = NAV_CHORDS[e.key];
                if (dest && _shortcutsRef) {
                    e.preventDefault();
                    _shortcutsRef.invokeMethodAsync('NavigateTo', dest);
                }
                return;
            }
        };

        document.addEventListener('keydown', _shortcutsHandler, false);
    }

    function disposeShortcuts() {
        if (_shortcutsHandler) {
            document.removeEventListener('keydown', _shortcutsHandler, false);
            _shortcutsHandler = null;
        }
        if (_chordState.timer) {
            clearTimeout(_chordState.timer);
            _chordState = { key: null, timer: null };
        }
        _shortcutsRef = null;
    }

    // ── Windows High Contrast Detection ───────────────────────────────────────
    var _hcQuery = null;

    function initHighContrast() {
        _hcQuery = window.matchMedia('(forced-colors: active)');
        function applyHC(matches) {
            document.documentElement.classList.toggle('fc-forced-colors', matches);
        }
        applyHC(_hcQuery.matches);
        _hcQuery.addEventListener('change', function (e) { applyHC(e.matches); });
    }

    // Initialise high-contrast detection immediately on script load
    initHighContrast();

    // ── Public API ────────────────────────────────────────────────────────────
    return {
        trapFocus: trapFocus,
        releaseFocus: releaseFocus,
        initRouteAnnouncer: initRouteAnnouncer,
        announceRoute: announceRoute,
        initShortcuts: initShortcuts,
        disposeShortcuts: disposeShortcuts
    };
})();
