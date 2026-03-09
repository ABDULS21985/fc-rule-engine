/**
 * FCAccessibility — Accessibility utilities for RegOS™ Admin Portal
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
                return el.offsetParent !== null || el.tagName === 'BODY';
            });
    }

    // ── Focus Trap ────────────────────────────────────────────────────────────
    var _trapStack = []; // { el, trigger, tabHandler }

    function trapFocus(elementId) {
        var el = document.getElementById(elementId);
        if (!el) return;

        var trigger = document.activeElement;
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
                if (active === first || !el.contains(active)) {
                    e.preventDefault();
                    last.focus();
                }
            } else {
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
                if (prev && typeof prev.focus === 'function' && document.contains(prev)) {
                    requestAnimationFrame(function () {
                        try { prev.focus(); } catch (_) { }
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
    var CHORD_TIMEOUT_MS = 500;

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
        'm': '/modules',
        'r': '/business-rules'
    };

    // Returns true when focus is inside a text-entry element
    function isInTextField() {
        var el = document.activeElement;
        if (!el) return false;
        var tag = el.tagName.toLowerCase();
        return tag === 'input' || tag === 'textarea' || tag === 'select' ||
            el.isContentEditable ||
            el.getAttribute('role') === 'textbox' ||
            el.getAttribute('role') === 'searchbox';
    }

    // Returns true when focus is on an element that natively handles arrow keys / Enter
    function isInteractiveElement() {
        var el = document.activeElement;
        if (!el || el === document.body) return false;
        var tag = el.tagName.toLowerCase();
        if (tag === 'button' || tag === 'a' || tag === 'select') return true;
        var role = el.getAttribute('role') || '';
        return role === 'button' || role === 'link' || role === 'option' ||
               role === 'menuitem' || role === 'tab' || role === 'radio' ||
               role === 'checkbox';
    }

    // Safely invoke a JSInvokable method on the Blazor dotnet reference
    function invoke(method, arg) {
        if (!_shortcutsRef) return;
        try {
            if (arg !== undefined) {
                _shortcutsRef.invokeMethodAsync(method, arg);
            } else {
                _shortcutsRef.invokeMethodAsync(method);
            }
        } catch (_) { /* Blazor circuit may be disconnected */ }
    }

    function initShortcuts(dotNetRef) {
        _shortcutsRef = dotNetRef;

        _shortcutsHandler = function (e) {

            // ── Escape: always fire — lets dialogs / panels close ──────────
            if (e.key === 'Escape') {
                invoke('CloseOverlay');
                _chordState.key = null;
                return; // let other handlers also process Escape
            }

            // ── Ctrl/Cmd+K: open command palette ───────────────────────────
            if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey &&
                e.key.toLowerCase() === 'k') {
                e.preventDefault();
                invoke('OpenCommandPalette');
                return;
            }

            // ── Ctrl/Cmd+/: focus sidebar search ───────────────────────────
            if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey &&
                e.key === '/') {
                e.preventDefault();
                invoke('FocusSidebarSearch');
                return;
            }

            // ── Ctrl/Cmd+Shift+N: new item (global) ────────────────────────
            if ((e.ctrlKey || e.metaKey) && e.shiftKey && !e.altKey &&
                e.key.toLowerCase() === 'n') {
                e.preventDefault();
                invoke('NewItemGlobal');
                return;
            }

            // ── ⌘⌫ (Mac) or Delete key: delete current item ───────────────
            if (!isInTextField()) {
                var isMacDelete = e.metaKey && !e.ctrlKey && !e.altKey && e.key === 'Backspace';
                var isDeleteKey = !e.ctrlKey && !e.metaKey && !e.altKey && !e.shiftKey && e.key === 'Delete';
                if (isMacDelete || isDeleteKey) {
                    e.preventDefault();
                    invoke('DeleteItem');
                    return;
                }
            }

            // All remaining shortcuts require no modifier keys
            if (e.ctrlKey || e.metaKey || e.altKey) return;

            // Clear chord state and bail if focus is inside a text field
            if (isInTextField()) {
                _chordState.key = null;
                return;
            }

            // ── ? key: toggle overlay ──────────────────────────────────────
            if (e.key === '?') {
                e.preventDefault();
                invoke('ToggleOverlay');
                _chordState.key = null;
                return;
            }

            // ── Single-key context shortcuts (no chord in progress) ─────────
            if (!_chordState.key) {
                switch (e.key) {
                    case 'n':
                        e.preventDefault();
                        invoke('NewItem');
                        return;

                    case 'f':
                        e.preventDefault();
                        invoke('FocusSearch');
                        return;

                    case 'e':
                        e.preventDefault();
                        // Both events fire; only pages that subscribed react.
                        // List pages handle OnExport; detail pages handle OnEditMode.
                        invoke('ExportOrEdit');
                        return;

                    case 's':
                        e.preventDefault();
                        invoke('Save');
                        return;

                    case 'ArrowUp':
                        if (!isInteractiveElement()) {
                            e.preventDefault();
                            invoke('NavigateRows', -1);
                        }
                        return;

                    case 'ArrowDown':
                        if (!isInteractiveElement()) {
                            e.preventDefault();
                            invoke('NavigateRows', 1);
                        }
                        return;

                    case 'Enter':
                        if (!isInteractiveElement() && !isInTextField()) {
                            e.preventDefault();
                            invoke('OpenRow');
                        }
                        return;
                }
            }

            // ── g chord: press g, then a navigation key ────────────────────
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
                if (dest) {
                    e.preventDefault();
                    invoke('NavigateTo', dest);
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

    // Focus the topbar search / command palette trigger
    function focusSidebarSearch() {
        var el = document.getElementById('fc-topbar-search');
        if (el) el.focus();
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

    initHighContrast();

    // ── Public API ────────────────────────────────────────────────────────────
    return {
        trapFocus: trapFocus,
        releaseFocus: releaseFocus,
        initRouteAnnouncer: initRouteAnnouncer,
        announceRoute: announceRoute,
        initShortcuts: initShortcuts,
        disposeShortcuts: disposeShortcuts,
        focusSidebarSearch: focusSidebarSearch
    };
})();
