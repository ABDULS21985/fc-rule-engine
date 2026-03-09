// RegOS™ Portal — JS interop functions

// ── Scroll to first matching element ──────────────────────────────────────────
window.portalScrollToElement = function (selector) {
    var el = document.querySelector(selector);
    if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
};

// ── Data Entry Form: Ctrl+Enter keyboard shortcut ─────────────────────────────
window.portalDataEntryForm = (function () {
    var _dotNetRef = null;
    var _handler = null;

    return {
        init: function (dotNetRef) {
            _dotNetRef = dotNetRef;
            _handler = function (e) {
                if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
                    e.preventDefault();
                    if (_dotNetRef) {
                        _dotNetRef.invokeMethodAsync('OnCtrlEnter');
                    }
                }
            };
            document.addEventListener('keydown', _handler);
        },
        dispose: function () {
            if (_handler) {
                document.removeEventListener('keydown', _handler);
                _handler = null;
            }
            _dotNetRef = null;
        }
    };
})();

window.portalCopyToClipboard = function (text) {
    return navigator.clipboard.writeText(text);
};

// ── Validation Hub: highlight & scroll to a field by name ─────────────────────
window.portalHighlightField = function (fieldName) {
    if (!fieldName) return;
    // Try multiple selector strategies: data-field, id, name attribute
    var el = document.querySelector('[data-field="' + CSS.escape(fieldName) + '"]')
          || document.getElementById('field-' + fieldName)
          || document.querySelector('[name="' + CSS.escape(fieldName) + '"]')
          || document.querySelector('input[id*="' + CSS.escape(fieldName) + '"]');

    if (!el) return;

    el.scrollIntoView({ behavior: 'smooth', block: 'center' });

    // Apply pulsing highlight ring
    el.classList.add('portal-field-highlight');
    setTimeout(function () {
        el.classList.add('portal-field-highlight--active');
    }, 50);
    setTimeout(function () {
        el.classList.remove('portal-field-highlight', 'portal-field-highlight--active');
    }, 4000);
};

window.portalDownloadFile = function (content, filename, contentType) {
    var blob = new Blob([content], { type: contentType });
    var url = URL.createObjectURL(blob);
    var a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

window.portalDownloadBase64File = function (base64Content, filename, contentType) {
    var binary = atob(base64Content);
    var len = binary.length;
    var bytes = new Uint8Array(len);
    for (var i = 0; i < len; i++) {
        bytes[i] = binary.charCodeAt(i);
    }

    var blob = new Blob([bytes], { type: contentType });
    var url = URL.createObjectURL(blob);
    var a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

window.portalPrintElement = function (elementId) {
    var element = document.getElementById(elementId);
    if (!element) return;
    var printWindow = window.open("", "_blank", "width=900,height=700");
    if (!printWindow) return;
    var styleSheets = Array.from(document.styleSheets)
        .map(function (sheet) {
            try {
                return sheet.href
                    ? '<link rel="stylesheet" href="' + sheet.href + '">'
                    : '<style>' + Array.from(sheet.cssRules).map(function (r) { return r.cssText; }).join("\n") + '</style>';
            } catch (e) {
                return sheet.href ? '<link rel="stylesheet" href="' + sheet.href + '">' : "";
            }
        }).join("\n");
    printWindow.document.write(
        '<!DOCTYPE html><html><head><title>Print</title>' + styleSheets +
        '<style>body{margin:0;padding:0}@media print{body{-webkit-print-color-adjust:exact;print-color-adjust:exact}}</style>' +
        '</head><body>' + element.outerHTML + '</body></html>'
    );
    printWindow.document.close();
    printWindow.focus();
    setTimeout(function () { printWindow.print(); printWindow.close(); }, 500);
};

// ═══════════════════════════════════════════════════════════════════════
// Validation Preview — Debounce + Field Change Interop
// ═══════════════════════════════════════════════════════════════════════

window.portalDebounce = (function () {
    var timers = {};
    return function (key, dotnetRef, methodName, delayMs) {
        if (timers[key]) clearTimeout(timers[key]);
        timers[key] = setTimeout(function () {
            dotnetRef.invokeMethodAsync(methodName);
        }, delayMs || 300);
    };
})();

window.portalGetFormFieldValues = function (containerId) {
    var container = document.getElementById(containerId);
    if (!container) return {};
    var values = {};
    var inputs = container.querySelectorAll("input[data-field], select[data-field], textarea[data-field]");
    inputs.forEach(function (input) {
        var fieldName = input.getAttribute("data-field");
        if (fieldName) {
            values[fieldName] = input.value || "";
        }
    });
    return values;
};

// portalApplyFieldValidation has two calling conventions:
//   Batch:       portalApplyFieldValidation(containerId, fieldStatusesArray)
//   Single-field: portalApplyFieldValidation(fieldId, 'valid'|'error'|'warning'|'clear')
window.portalApplyFieldValidation = function (firstArg, secondArg) {
    // Single-field mode: secondArg is a string status
    if (typeof secondArg === "string") {
        var input = document.getElementById(firstArg);
        if (!input) return;
        var wrapper = input.closest(".portal-field-wrapper") || input.parentElement;
        // Remove existing state classes from both input and wrapper
        [input, wrapper].forEach(function (el) {
            el.classList.remove("portal-field-valid", "portal-field-error", "portal-field-warning",
                "portal-field-wrapper--valid", "portal-field-wrapper--error", "portal-field-wrapper--warning");
        });
        if (secondArg === "valid") {
            wrapper.classList.add("portal-field-wrapper--valid");
        } else if (secondArg === "error") {
            wrapper.classList.add("portal-field-wrapper--error");
            // Shake on error for immediate feedback
            if (window.FCMotion && typeof window.FCMotion.shakeElement === "function") {
                window.FCMotion.shakeElement(wrapper);
            }
        } else if (secondArg === "warning") {
            wrapper.classList.add("portal-field-wrapper--warning");
        }
        return;
    }

    // Batch mode: firstArg = containerId, secondArg = fieldStatuses array
    var container = document.getElementById(firstArg);
    if (!container) return;
    container.querySelectorAll(".portal-field-status").forEach(function (el) { el.remove(); });
    container.querySelectorAll(".portal-field-valid, .portal-field-error, .portal-field-warning")
        .forEach(function (el) {
            el.classList.remove("portal-field-valid", "portal-field-error", "portal-field-warning");
        });
    secondArg.forEach(function (fs) {
        var input = container.querySelector("[data-field='" + fs.fieldName + "']");
        if (!input) return;
        var wrapper = input.closest(".portal-form-group") || input.parentElement;
        if (fs.status === "Valid") input.classList.add("portal-field-valid");
        else if (fs.status === "Error") input.classList.add("portal-field-error");
        else if (fs.status === "Warning") input.classList.add("portal-field-warning");
        if (fs.status !== "Empty" && fs.message) {
            var indicator = document.createElement("span");
            indicator.className = "portal-field-status portal-field-status-" + fs.status.toLowerCase();
            indicator.textContent = fs.message;
            wrapper.appendChild(indicator);
        }
    });
};

window.portalUpdateFormulaTotals = function (formulaResults) {
    formulaResults.forEach(function (fr) {
        var totalEl = document.querySelector("[data-formula-total='" + fr.fieldName + "']");
        if (totalEl) {
            totalEl.textContent = "Expected: " + (fr.computedValue || "\u2014");
            totalEl.className = "portal-formula-total " +
                (fr.matches ? "portal-formula-match" : "portal-formula-mismatch");
        }
    });
};

// ═══════════════════════════════════════════════════════════════════════
// Accessibility — Focus Trap for Modals
// ═══════════════════════════════════════════════════════════════════════

window.portalTrapFocus = function (elementId) {
    var container = document.getElementById(elementId);
    if (!container) return;
    var focusable = container.querySelectorAll(
        'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'
    );
    if (focusable.length === 0) return;
    var first = focusable[0];
    var last = focusable[focusable.length - 1];
    first.focus();
    container.addEventListener("keydown", function trapHandler(e) {
        if (e.key !== "Tab") return;
        if (e.shiftKey) {
            if (document.activeElement === first) {
                e.preventDefault();
                last.focus();
            }
        } else {
            if (document.activeElement === last) {
                e.preventDefault();
                first.focus();
            }
        }
    });
};

// Announce text to screen readers via live region
window.portalAnnounce = function (message) {
    var region = document.querySelector("[aria-live='polite'][role='status']");
    if (region) {
        region.textContent = "";
        setTimeout(function () { region.textContent = message; }, 100);
    }
};

// ═══════════════════════════════════════════════════════════════════════
// Notification Sound — Play/Mute with localStorage persistence
// ═══════════════════════════════════════════════════════════════════════

window.portalNotifPlaySound = function () {
    try {
        var ctx = new (window.AudioContext || window.webkitAudioContext)();
        var osc = ctx.createOscillator();
        var gain = ctx.createGain();
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.type = "sine";
        osc.frequency.setValueAtTime(880, ctx.currentTime);
        osc.frequency.setValueAtTime(1046.5, ctx.currentTime + 0.08);
        gain.gain.setValueAtTime(0.15, ctx.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.3);
        osc.start(ctx.currentTime);
        osc.stop(ctx.currentTime + 0.3);
    } catch (e) { /* AudioContext not available */ }
};

window.portalNotifGetSoundPref = function () {
    try {
        return localStorage.getItem("portalNotifSound") === "true";
    } catch (e) { return false; }
};

window.portalNotifSetSoundPref = function (enabled) {
    try {
        localStorage.setItem("portalNotifSound", enabled ? "true" : "false");
    } catch (e) { /* storage not available */ }
};

// Scroll an element into view by ID (used for carry-forward field navigation)
window.portalScrollIntoView = function (elementId) {
    var el = document.getElementById(elementId);
    if (el) {
        el.scrollIntoView({ behavior: "smooth", block: "center", inline: "nearest" });
        // Also focus the first non-checkbox input for keyboard accessibility
        var input = el.querySelector("input:not([type='checkbox']), select, textarea");
        if (input) { setTimeout(function () { input.focus(); }, 350); }
    }
};

// Guided Tour overlay helpers
window.portalTour = (function () {
    var activeElement = null;

    function clear() {
        if (activeElement) {
            activeElement.classList.remove("portal-tour-highlight");
            activeElement = null;
        }
    }

    function highlight(selector) {
        clear();
        if (!selector) {
            return null;
        }

        var target = document.querySelector(selector);
        if (!target) {
            return null;
        }

        target.classList.add("portal-tour-highlight");
        target.scrollIntoView({ behavior: "smooth", block: "center", inline: "nearest" });
        activeElement = target;

        var rect = target.getBoundingClientRect();
        return {
            top: rect.top + window.scrollY,
            left: rect.left + window.scrollX,
            width: rect.width,
            height: rect.height
        };
    }

    return {
        highlight: highlight,
        clear: clear
    };
})();

// ── Feature-discovery beacons ─────────────────────────────────────────────────
// Adds the .portal-tour-beacon pulsing ring to DOM elements and wires click
// callbacks back into Blazor via DotNetObjectReference.

window.portalTour.initBeacons = function (selectors, dotNetRef) {
    selectors.forEach(function (selector, idx) {
        var el = document.querySelector(selector);
        if (!el) return;
        el.classList.add('portal-tour-beacon');
        el._tourClickHandler = function (e) {
            e.stopPropagation();
            dotNetRef.invokeMethodAsync('OnBeaconClicked', idx);
        };
        el.addEventListener('click', el._tourClickHandler, { capture: true });
    });
};

window.portalTour.removeBeacon = function (selector) {
    var el = document.querySelector(selector);
    if (!el) return;
    el.classList.remove('portal-tour-beacon');
    if (el._tourClickHandler) {
        el.removeEventListener('click', el._tourClickHandler, { capture: true });
        delete el._tourClickHandler;
    }
};

window.portalTour.clearAllBeacons = function () {
    document.querySelectorAll('.portal-tour-beacon').forEach(function (el) {
        el.classList.remove('portal-tour-beacon');
        if (el._tourClickHandler) {
            el.removeEventListener('click', el._tourClickHandler, { capture: true });
            delete el._tourClickHandler;
        }
    });
};

// Smart tooltip positioning: returns { top, left, arrowDir } keeping the card
// inside the viewport. Called by TourBeaconSet.razor OnBeaconClicked.
window.portalTour.getTooltipPosition = function (selector, tooltipW, tooltipH) {
    var el = document.querySelector(selector);
    if (!el) return { top: 120, left: 120, arrowDir: 'top' };

    var rect = el.getBoundingClientRect();
    var vw = window.innerWidth;
    var vh = window.innerHeight;
    var gap = 14;
    var scrollY = window.scrollY;
    var scrollX = window.scrollX;

    function clampLeft(l) { return Math.min(Math.max(l, 8), vw - tooltipW - 8); }

    // Prefer below
    if (rect.bottom + tooltipH + gap < vh) {
        return { top: rect.bottom + gap + scrollY, left: clampLeft(rect.left + scrollX), arrowDir: 'top' };
    }
    // Try above
    if (rect.top - tooltipH - gap > 0) {
        return { top: rect.top - tooltipH - gap + scrollY, left: clampLeft(rect.left + scrollX), arrowDir: 'bottom' };
    }
    // Try right
    if (rect.right + tooltipW + gap < vw) {
        return { top: rect.top + scrollY, left: rect.right + gap + scrollX, arrowDir: 'left' };
    }
    // Fallback left
    return { top: rect.top + scrollY, left: Math.max(8, rect.left - tooltipW - gap + scrollX), arrowDir: 'right' };
};

// ── Spotlight highlight (used by GuidedTour IsSpotlight mode) ─────────────────
// Applies a box-shadow "spotlight" to the target element so the surrounding
// page appears darkened through the CSS .portal-tour-backdrop-spotlight overlay.

window.portalTour.spotlightOn = function (selector) {
    window.portalTour.spotlightOff();
    var el = document.querySelector(selector);
    if (!el) return null;
    el.classList.add('portal-tour-spotlight-target');
    el.scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'nearest' });
    var rect = el.getBoundingClientRect();
    return { top: rect.top + window.scrollY, left: rect.left + window.scrollX, width: rect.width, height: rect.height };
};

window.portalTour.spotlightOff = function () {
    document.querySelectorAll('.portal-tour-spotlight-target').forEach(function (el) {
        el.classList.remove('portal-tour-spotlight-target');
    });
};

window.portalScrollToSection = function (elementId) {
    var el = document.getElementById(elementId);
    if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
};

// ─── Error Summary: Jump-to-Field Navigation ────────────────────────────────
window.portalScrollToField = function (fieldCode) {
    var wrapper = document.querySelector('[data-field="' + CSS.escape(fieldCode) + '"]');
    if (!wrapper) return;

    wrapper.scrollIntoView({ behavior: 'smooth', block: 'center' });

    // Restart highlight animation
    wrapper.classList.remove('portal-field-highlight');
    void wrapper.offsetWidth;
    wrapper.classList.add('portal-field-highlight');

    // Focus the first editable input inside the wrapper
    var focusable = wrapper.querySelector(
        'input:not([type="hidden"]):not([readonly]):not([disabled]),' +
        'select:not([disabled]),' +
        'textarea:not([readonly]):not([disabled])'
    );
    if (focusable) {
        setTimeout(function () { focusable.focus({ preventScroll: true }); }, 350);
    }

    // Remove highlight class after animation completes
    setTimeout(function () { wrapper.classList.remove('portal-field-highlight'); }, 2500);
};

// ═══════════════════════════════════════════════════════════════════════
// Bulk Upload — Client-side Pre-validation Helpers
// ═══════════════════════════════════════════════════════════════════════

window.portalBulkCheckDuplicate = function (hash) {
    try {
        var stored = JSON.parse(localStorage.getItem("portal_bulk_hashes") || "[]");
        var match = stored.find(function (h) { return h.hash === hash; });
        if (match) return { isDuplicate: true, date: match.date, fileName: match.fileName };
    } catch (e) { /* storage unavailable */ }
    return { isDuplicate: false, date: null, fileName: null };
};

window.portalBulkStoreHash = function (hash, fileName) {
    try {
        var stored = JSON.parse(localStorage.getItem("portal_bulk_hashes") || "[]");
        // Remove existing entry for same hash, then prepend new one
        stored = stored.filter(function (h) { return h.hash !== hash; });
        stored.unshift({ hash: hash, fileName: fileName, date: new Date().toISOString() });
        if (stored.length > 50) stored = stored.slice(0, 50);
        localStorage.setItem("portal_bulk_hashes", JSON.stringify(stored));
    } catch (e) { /* storage unavailable */ }
};

window.portalBulkDownloadCsv = function (csvContent, fileName) {
    var bom = "\uFEFF"; // UTF-8 BOM for Excel compatibility
    var blob = new Blob([bom + csvContent], { type: "text/csv;charset=utf-8;" });
    var url = URL.createObjectURL(blob);
    var a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
};

// ── Bulk selection helpers ────────────────────────────────────────────────────

/** Set the indeterminate property on a checkbox element reference */
window.portalSetIndeterminate = function (el, value) {
    try { if (el) el.indeterminate = value; } catch (e) { /* ignore */ }
};

/** Register a document-level Escape key handler for bulk deselect */
let _portalEscHandler = null;
window.portalInitEscHandler = function (dotnetRef) {
    portalDisposeEscHandler();
    _portalEscHandler = function (e) {
        if (e.key === 'Escape') {
            dotnetRef.invokeMethodAsync('HandleEscapeKey').catch(() => {});
        }
    };
    document.addEventListener('keydown', _portalEscHandler);
};

window.portalDisposeEscHandler = function () {
    if (_portalEscHandler) {
        document.removeEventListener('keydown', _portalEscHandler);
        _portalEscHandler = null;
    }
};

// ── Compliance Certificate V2 helpers ────────────────────────────────────────

/**
 * Renders a QR-like canvas pattern for the compliance certificate.
 * Produces proper corner finder patterns and pseudo-random data cells
 * derived from the text. Not a standards-compliant QR code, but
 * visually appropriate for print documents.
 */
window.portalCertQr = function (canvasId, text) {
    var canvas = document.getElementById(canvasId);
    if (!canvas) return;
    var ctx = canvas.getContext('2d');
    var size = canvas.width;
    var M = 21; // 21×21 modules (version 1)
    var cell = Math.floor(size / M);

    // Simple 32-bit hash of text for deterministic pattern
    var h = 0x12345678;
    for (var i = 0; i < text.length; i++) {
        h = (Math.imul(h ^ text.charCodeAt(i), 0x9e3779b9) >>> 0);
    }

    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, size, size);
    ctx.fillStyle = '#1a1a1a';

    function fillCell(r, c) {
        ctx.fillRect(c * cell + 1, r * cell + 1, cell - 1, cell - 1);
    }

    function finder(dr, dc) {
        for (var fr = 0; fr < 7; fr++) {
            for (var fc = 0; fc < 7; fc++) {
                if (fr === 0 || fr === 6 || fc === 0 || fc === 6 ||
                    (fr >= 2 && fr <= 4 && fc >= 2 && fc <= 4)) {
                    fillCell(dr + fr, dc + fc);
                }
            }
        }
    }

    finder(0, 0);
    finder(0, M - 7);
    finder(M - 7, 0);

    for (var t = 8; t < M - 8; t++) {
        if (t % 2 === 0) { fillCell(6, t); fillCell(t, 6); }
    }

    for (var r = 0; r < M; r++) {
        for (var c = 0; c < M; c++) {
            if ((r < 9 && c < 9) || (r < 9 && c > M - 9) ||
                (r > M - 9 && c < 9) || r === 6 || c === 6) continue;
            var s = ((h ^ (r * 31 + c * 37) * 0x9e3779b9) >>> 0);
            s ^= s >>> 17;
            s = (Math.imul(s, 0x45d9f3b) >>> 0);
            s ^= s >>> 16;
            if (s & 1) fillCell(r, c);
        }
    }
};

/**
 * Copies the certificate verification URL to the clipboard.
 * Temporarily updates the button label to confirm the copy.
 */
window.portalCertCopyLink = function (url, buttonId) {
    function doCopy() {
        var btn = buttonId ? document.getElementById(buttonId) : null;
        if (btn) {
            var original = btn.innerHTML;
            btn.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" aria-hidden="true"><polyline points="20 6 9 17 4 12"/></svg> Copied!';
            btn.classList.add('portal-cert-v2-copy-btn--copied');
            setTimeout(function () {
                btn.innerHTML = original;
                btn.classList.remove('portal-cert-v2-copy-btn--copied');
            }, 2200);
        }
    }
    if (navigator.clipboard && navigator.clipboard.writeText) {
        return navigator.clipboard.writeText(url).then(doCopy).catch(function () { doCopy(); });
    }
    var ta = document.createElement('textarea');
    ta.value = url;
    ta.style.cssText = 'position:fixed;opacity:0;top:0;left:0';
    document.body.appendChild(ta);
    ta.focus(); ta.select();
    try { document.execCommand('copy'); } catch (e) { /* ignore */ }
    document.body.removeChild(ta);
    doCopy();
};

// ── Sidebar nav: collapsed sections (localStorage) ─────────────────────────

/**
 * Returns array of section names that are currently collapsed.
 * @returns {string[]}
 */
window.portalNavGetCollapsed = function () {
    try {
        const raw = localStorage.getItem('fc_nav_collapsed');
        return raw ? JSON.parse(raw) : [];
    } catch {
        return [];
    }
};

/**
 * Persists a section's collapsed state to localStorage.
 * @param {string} section
 * @param {boolean} collapsed
 */
window.portalNavSetCollapsed = function (section, collapsed) {
    try {
        const raw = localStorage.getItem('fc_nav_collapsed');
        const list = raw ? JSON.parse(raw) : [];
        const idx = list.indexOf(section);
        if (collapsed && idx === -1) list.push(section);
        else if (!collapsed && idx !== -1) list.splice(idx, 1);
        localStorage.setItem('fc_nav_collapsed', JSON.stringify(list));
    } catch { /* non-fatal */ }
};

// ── Sidebar nav: recent pages history (localStorage) ───────────────────────

const FC_NAV_RECENT_KEY = 'fc_nav_recent_pages';
const FC_NAV_RECENT_MAX = 3;

/**
 * Returns the last N visited pages as [{href, label}].
 * @returns {{href:string, label:string}[]}
 */
window.portalNavGetRecentPages = function () {
    try {
        const raw = localStorage.getItem(FC_NAV_RECENT_KEY);
        return raw ? JSON.parse(raw) : [];
    } catch {
        return [];
    }
};

/**
 * Prepends a page to the recent-pages list, removing duplicates, capping at MAX.
 * Skips pages with empty labels (e.g. dashboard root is not tracked).
 * @param {string} href
 * @param {string} label
 */
window.portalNavAddRecentPage = function (href, label) {
    if (!label) return;
    try {
        const raw = localStorage.getItem(FC_NAV_RECENT_KEY);
        let list = raw ? JSON.parse(raw) : [];
        // Remove existing entry for same href
        list = list.filter(p => p.href !== href);
        // Prepend new entry
        list.unshift({ href, label });
        // Cap at max
        if (list.length > FC_NAV_RECENT_MAX) list = list.slice(0, FC_NAV_RECENT_MAX);
        localStorage.setItem(FC_NAV_RECENT_KEY, JSON.stringify(list));
    } catch { /* non-fatal */ }
};

/**
 * Removes a specific page from the recent-pages list.
 * @param {string} href
 */
window.portalNavRemovePage = function (href) {
    try {
        const raw = localStorage.getItem(FC_NAV_RECENT_KEY);
        let list = raw ? JSON.parse(raw) : [];
        list = list.filter(p => p.href !== href);
        localStorage.setItem(FC_NAV_RECENT_KEY, JSON.stringify(list));
    } catch { /* non-fatal */ }
};

// ── Command Palette ───────────────────────────────────────────────────────────

window.portalCommandPalette = (function () {
    let _ref = null;

    function isEditing() {
        const el = document.activeElement;
        return el && (
            el.tagName === 'INPUT' ||
            el.tagName === 'TEXTAREA' ||
            el.tagName === 'SELECT' ||
            el.isContentEditable
        );
    }

    function onKeyDown(e) {
        if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
            e.preventDefault();
            e.stopPropagation();
            _ref?.invokeMethodAsync('OpenPalette').catch(() => {});
        }
    }

    return {
        init(dotNetRef) {
            _ref = dotNetRef;
            document.addEventListener('keydown', onKeyDown, true);
        },

        dispose() {
            document.removeEventListener('keydown', onKeyDown, true);
            _ref = null;
        },

        /** Focus the search input and install Tab-prevention listener */
        focusInput(id) {
            requestAnimationFrame(() => {
                const el = document.getElementById(id);
                if (!el) return;
                el.focus();
                el.select();
                // Prevent Tab from moving focus out of the palette dialog
                if (!el._cmdTabGuard) {
                    el._cmdTabGuard = (e) => {
                        if (e.key === 'Tab') e.preventDefault();
                    };
                    el.addEventListener('keydown', el._cmdTabGuard, { capture: true });
                }
            });
        },

        /** Scroll a result item into view without disrupting scroll position */
        scrollItemIntoView(id) {
            const el = document.getElementById(id);
            if (el) el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        }
    };
})();

// ── Shortcuts Overlay ─────────────────────────────────────────────────────────

window.portalShortcutsOverlay = (function () {
    let _ref = null;

    function isEditing() {
        const el = document.activeElement;
        return el && (
            el.tagName === 'INPUT' ||
            el.tagName === 'TEXTAREA' ||
            el.tagName === 'SELECT' ||
            el.isContentEditable
        );
    }

    function onKeyDown(e) {
        if (e.key === '?' && !isEditing() && !e.ctrlKey && !e.metaKey && !e.altKey) {
            e.preventDefault();
            _ref?.invokeMethodAsync('OpenOverlay').catch(() => {});
        }
    }

    return {
        init(dotNetRef) {
            _ref = dotNetRef;
            document.addEventListener('keydown', onKeyDown);
        },

        dispose() {
            document.removeEventListener('keydown', onKeyDown);
            _ref = null;
        }
    };
})();

// ── Calendar Reminder Storage ────────────────────────────────
window.portalCalendar = {
    loadReminders: function () {
        return localStorage.getItem('fc_portal_calendar_reminders') || null;
    },
    saveReminders: function (json) {
        localStorage.setItem('fc_portal_calendar_reminders', json);
    }
};

// ── Onboarding Wizard ─────────────────────────────────────────────
window.portalWizard = (() => {
    // ── Confetti particle system ─────────────────────────────────
    function launchConfetti() {
        const canvas = document.getElementById('wiz-confetti');
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        canvas.width  = window.innerWidth;
        canvas.height = window.innerHeight;

        const COLORS = [
            '#006B3F', '#22c55e', '#C8A415', '#f59e0b',
            '#3b82f6', '#a855f7', '#ef4444', '#06b6d4',
        ];
        const COUNT  = 160;
        const GRAVITY = 0.35;
        const DRAG    = 0.97;

        const particles = Array.from({ length: COUNT }, () => ({
            x:    Math.random() * canvas.width,
            y:    -Math.random() * canvas.height * 0.4,
            vx:   (Math.random() - 0.5) * 6,
            vy:   Math.random() * 4 + 2,
            w:    Math.random() * 9 + 5,
            h:    Math.random() * 5 + 3,
            rot:  Math.random() * Math.PI * 2,
            rotV: (Math.random() - 0.5) * 0.25,
            col:  COLORS[Math.floor(Math.random() * COLORS.length)],
            op:   1,
        }));

        let frame;
        let elapsed = 0;

        function tick() {
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            elapsed++;

            let alive = 0;
            for (const p of particles) {
                p.vy  += GRAVITY;
                p.vx  *= DRAG;
                p.vy  *= DRAG;
                p.x   += p.vx;
                p.y   += p.vy;
                p.rot += p.rotV;
                if (elapsed > 100) p.op = Math.max(0, p.op - 0.012);

                if (p.y < canvas.height + 20 && p.op > 0) {
                    alive++;
                    ctx.save();
                    ctx.translate(p.x, p.y);
                    ctx.rotate(p.rot);
                    ctx.globalAlpha = p.op;
                    ctx.fillStyle   = p.col;
                    ctx.fillRect(-p.w / 2, -p.h / 2, p.w, p.h);
                    ctx.restore();
                }
            }

            if (alive > 0) {
                frame = requestAnimationFrame(tick);
            } else {
                ctx.clearRect(0, 0, canvas.width, canvas.height);
            }
        }

        frame = requestAnimationFrame(tick);

        // Auto-cancel after 8s
        setTimeout(() => {
            cancelAnimationFrame(frame);
            ctx.clearRect(0, 0, canvas.width, canvas.height);
        }, 8000);
    }

    // ── Video helper ─────────────────────────────────────────────
    function openVideo() {
        // Open a getting-started video in a new tab (URL configurable)
        const videoUrl = document.documentElement.dataset.wizVideoUrl
            || 'https://www.youtube.com/results?search_query=FCEngine+getting+started';
        window.open(videoUrl, '_blank', 'noopener,noreferrer');
    }

    return { launchConfetti, openVideo };
})();

// ── Reporting Calendar helpers ──────────────────────────────────────

/**
 * Download a text file (used for iCal export).
 * Called from ReportingCalendar.razor via JS.InvokeVoidAsync("portalDownloadText", ...)
 */
window.portalDownloadText = function (fileName, content, mimeType) {
    const blob = new Blob([content], { type: mimeType || 'text/plain' });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 5000);
};

// ═══════════════════════════════════════════════════════════════════════════════
// §63 — Notification Intelligence Hub
// Bell swing, badge bounce, 30s polling, IntersectionObserver mark-as-read,
// snooze context menu, keyboard shortcuts for filter tabs.
// ═══════════════════════════════════════════════════════════════════════════════

window.portalNotifHub = (function () {
    'use strict';

    var _pollTimer = null;
    var _snoozeBackdrop = null;
    var _snoozeMenu = null;
    var _readTimers = {};
    var _intersectionObservers = [];
    var _kbHandler = null;

    // ── Bell Swing ──────────────────────────────────────────────────────────
    function animateBell() {
        var wrapper = document.querySelector('.portal-notif-bell-wrapper');
        if (!wrapper) return;
        var icon = wrapper.querySelector('.portal-notif-bell-icon');
        if (!icon) return;
        icon.classList.remove('portal-notif-bell-swinging');
        void icon.offsetWidth; // force reflow to restart animation
        icon.classList.add('portal-notif-bell-swinging');
        icon.addEventListener('animationend', function handler() {
            icon.classList.remove('portal-notif-bell-swinging');
            icon.removeEventListener('animationend', handler);
        }, { once: true });
    }

    // ── Badge Bounce ────────────────────────────────────────────────────────
    function animateBadge() {
        var wrapper = document.querySelector('.portal-notif-bell-wrapper');
        if (!wrapper) return;
        var badge = wrapper.querySelector('.portal-notif-bell-badge');
        if (!badge) return;
        badge.classList.remove('portal-notif-badge-bouncing');
        void badge.offsetWidth;
        badge.classList.add('portal-notif-badge-bouncing');
        badge.addEventListener('animationend', function handler() {
            badge.classList.remove('portal-notif-badge-bouncing');
            badge.removeEventListener('animationend', handler);
        }, { once: true });
    }

    // ── Poll Dot Flash ──────────────────────────────────────────────────────
    function flashPollDot() {
        var dot = document.querySelector('.portal-notif-poll-dot');
        if (!dot) return;
        dot.classList.remove('portal-notif-poll-dot--active');
        void dot.offsetWidth;
        dot.classList.add('portal-notif-poll-dot--active');
        setTimeout(function () {
            dot.classList.remove('portal-notif-poll-dot--active');
        }, 1200);
    }

    // ── 30s Polling ─────────────────────────────────────────────────────────
    function startPolling(dotnetRef, intervalMs) {
        stopPolling();
        var ms = intervalMs || 30000;
        _pollTimer = setInterval(function () {
            flashPollDot();
            try { dotnetRef.invokeMethodAsync('PollForUpdates'); } catch (e) { /* non-fatal */ }
        }, ms);
    }

    function stopPolling() {
        if (_pollTimer !== null) {
            clearInterval(_pollTimer);
            _pollTimer = null;
        }
    }

    // ── IntersectionObserver: Mark-as-Read on Scroll ────────────────────────
    // observeAllUnreadCards — bulk observe after page render
    function observeAllUnreadCards(dotnetRef) {
        disposeObservers();
        document.querySelectorAll('[data-notif-id][data-is-read="false"]').forEach(function (el) {
            observeNotifCard(el, dotnetRef);
        });
    }

    function observeNotifCard(el, dotnetRef) {
        // Accept either a DOM element or a CSS selector string
        if (typeof el === 'string') { el = document.querySelector(el); }
        if (!el) return;
        var notifId = parseInt(el.getAttribute('data-notif-id'), 10);
        if (!notifId || isNaN(notifId)) return;
        if (el.getAttribute('data-is-read') === 'true') return;

        var obs = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting && entry.intersectionRatio >= 0.5) {
                    if (!_readTimers[notifId]) {
                        _readTimers[notifId] = setTimeout(function () {
                            delete _readTimers[notifId];
                            obs.disconnect();
                            try { dotnetRef.invokeMethodAsync('OnScrollRead', notifId); } catch (e) { }
                        }, 2000);
                    }
                } else {
                    if (_readTimers[notifId]) {
                        clearTimeout(_readTimers[notifId]);
                        delete _readTimers[notifId];
                    }
                }
            });
        }, { threshold: 0.5 });

        obs.observe(el);
        _intersectionObservers.push(obs);
    }

    function disposeObservers() {
        _intersectionObservers.forEach(function (obs) { try { obs.disconnect(); } catch (e) { } });
        _intersectionObservers = [];
        Object.keys(_readTimers).forEach(function (k) { clearTimeout(_readTimers[k]); });
        _readTimers = {};
    }

    // ── Snooze Context Menu ─────────────────────────────────────────────────
    function showSnoozeMenu(notifId, x, y, dotnetRef) {
        hideSnoozeMenu();

        _snoozeBackdrop = document.createElement('div');
        _snoozeBackdrop.style.cssText = 'position:fixed;inset:0;z-index:9998;';
        _snoozeBackdrop.addEventListener('click', hideSnoozeMenu);
        _snoozeBackdrop.addEventListener('contextmenu', function (e) { e.preventDefault(); hideSnoozeMenu(); });
        document.body.appendChild(_snoozeBackdrop);

        var menuLeft = Math.min(x, window.innerWidth - 210);
        var menuTop = Math.min(y, window.innerHeight - 220);

        _snoozeMenu = document.createElement('div');
        _snoozeMenu.className = 'portal-notif-snooze-menu';
        _snoozeMenu.style.left = menuLeft + 'px';
        _snoozeMenu.style.top = menuTop + 'px';

        var items = [
            { isHeader: true, label: 'Snooze Options' },
            { icon: '⏰', label: 'Snooze for 1 hour',       action: 'snooze_1h' },
            { icon: '🌙', label: 'Snooze until tomorrow',    action: 'snooze_tomorrow' },
            { isDivider: true },
            { icon: '🔇', label: 'Mute this type',          action: 'mute_type',   danger: true },
            { icon: '✓',  label: 'Mark as read',            action: 'mark_read' },
            { icon: '✕',  label: 'Dismiss',                 action: 'dismiss',     danger: true },
        ];

        items.forEach(function (item) {
            if (item.isHeader) {
                var lbl = document.createElement('div');
                lbl.className = 'portal-notif-snooze-label';
                lbl.textContent = item.label;
                _snoozeMenu.appendChild(lbl);
            } else if (item.isDivider) {
                var div = document.createElement('div');
                div.className = 'portal-notif-snooze-divider';
                _snoozeMenu.appendChild(div);
            } else {
                var btn = document.createElement('button');
                btn.className = 'portal-notif-snooze-item' + (item.danger ? ' portal-notif-snooze-item--danger' : '');
                btn.innerHTML = '<span style="font-size:0.875rem;width:1.25em;text-align:center">' + item.icon + '</span><span>' + item.label + '</span>';
                (function (action) {
                    btn.addEventListener('click', function () {
                        hideSnoozeMenu();
                        try { dotnetRef.invokeMethodAsync('OnSnoozeAction', notifId, action); } catch (e) { }
                    });
                })(item.action);
                _snoozeMenu.appendChild(btn);
            }
        });

        document.body.appendChild(_snoozeMenu);
    }

    function hideSnoozeMenu() {
        if (_snoozeMenu) { _snoozeMenu.remove(); _snoozeMenu = null; }
        if (_snoozeBackdrop) { _snoozeBackdrop.remove(); _snoozeBackdrop = null; }
    }

    // ── Snooze Persistence (localStorage) ──────────────────────────────────
    function setSnooze(notifId, untilIso) {
        try {
            if (untilIso) {
                localStorage.setItem('notifSnooze_' + notifId, untilIso);
            } else {
                localStorage.removeItem('notifSnooze_' + notifId);
            }
        } catch (e) { }
    }

    function isSnoozed(notifId) {
        try {
            var val = localStorage.getItem('notifSnooze_' + notifId);
            if (!val) return false;
            return new Date(val) > new Date();
        } catch (e) { return false; }
    }

    function getMutedTypes() {
        try {
            var val = localStorage.getItem('notifMutedTypes');
            return val ? JSON.parse(val) : [];
        } catch (e) { return []; }
    }

    function muteType(typeName) {
        try {
            var muted = getMutedTypes();
            if (muted.indexOf(typeName) === -1) {
                muted.push(typeName);
                localStorage.setItem('notifMutedTypes', JSON.stringify(muted));
            }
        } catch (e) { }
    }

    function isTypeMuted(typeName) {
        return getMutedTypes().indexOf(typeName) !== -1;
    }

    // ── Keyboard Shortcuts for Notifications Page ────────────────────────────
    function initKeyboardShortcuts(dotnetRef) {
        disposeKeyboardShortcuts();
        _kbHandler = function (e) {
            // Skip if user is typing
            var tag = (document.activeElement || {}).tagName || '';
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
            if ((document.activeElement || {}).isContentEditable) return;

            var key = e.key;
            if (key === 's' || key === 'S') { e.preventDefault(); try { dotnetRef.invokeMethodAsync('OnKeyboardFilter', 'submissions'); } catch (ex) { } }
            else if (key === 'a' || key === 'A') { e.preventDefault(); try { dotnetRef.invokeMethodAsync('OnKeyboardFilter', 'approvals'); } catch (ex) { } }
            else if (key === 'd' || key === 'D') { e.preventDefault(); try { dotnetRef.invokeMethodAsync('OnKeyboardFilter', 'deadlines'); } catch (ex) { } }
        };
        document.addEventListener('keydown', _kbHandler);
    }

    function disposeKeyboardShortcuts() {
        if (_kbHandler) {
            document.removeEventListener('keydown', _kbHandler);
            _kbHandler = null;
        }
    }

    // ── Long-press detection for mobile snooze ──────────────────────────────
    var _lpTimers = {};
    function initLongPress(el, notifId, dotnetRef) {
        if (!el) return;
        var started = null;
        el.addEventListener('touchstart', function (e) {
            started = Date.now();
            _lpTimers[notifId] = setTimeout(function () {
                var t = e.touches[0];
                if (t) showSnoozeMenu(notifId, t.clientX, t.clientY, dotnetRef);
            }, 600);
        }, { passive: true });
        el.addEventListener('touchend', function () {
            if (_lpTimers[notifId]) { clearTimeout(_lpTimers[notifId]); delete _lpTimers[notifId]; }
        });
        el.addEventListener('touchcancel', function () {
            if (_lpTimers[notifId]) { clearTimeout(_lpTimers[notifId]); delete _lpTimers[notifId]; }
        });
    }

    return {
        animateBell:               animateBell,
        animateBadge:              animateBadge,
        flashPollDot:              flashPollDot,
        startPolling:              startPolling,
        stopPolling:               stopPolling,
        observeNotifCard:          observeNotifCard,
        observeAllUnreadCards:     observeAllUnreadCards,
        disposeObservers:          disposeObservers,
        showSnoozeMenu:            showSnoozeMenu,
        hideSnoozeMenu:            hideSnoozeMenu,
        setSnooze:                 setSnooze,
        isSnoozed:                 isSnoozed,
        getMutedTypes:             getMutedTypes,
        muteType:                  muteType,
        isTypeMuted:               isTypeMuted,
        initKeyboardShortcuts:     initKeyboardShortcuts,
        disposeKeyboardShortcuts:  disposeKeyboardShortcuts,
        initLongPress:             initLongPress,
    };
})();

// ── Filter Preset localStorage helpers ──────────────────────────────────────
window.portalLoadFilterPresets = function (key) {
    try { return localStorage.getItem(key) || '[]'; } catch { return '[]'; }
};
window.portalSaveFilterPresets = function (key, json) {
    try { localStorage.setItem(key, json); } catch { }
};

// ============================================================
//  portalOnboarding — contextual spotlight & page-visit helpers
// ============================================================
window.portalOnboarding = (function () {
    'use strict';

    /** Return the bounding rect (plus Bottom) of the first element matching selector, or null. */
    function getRect(selector) {
        try {
            var el = document.querySelector(selector);
            if (!el) return null;
            var r = el.getBoundingClientRect();
            if (r.width === 0 && r.height === 0) return null;
            return {
                top:    r.top,
                left:   r.left,
                bottom: r.bottom,
                right:  r.right,
                width:  r.width,
                height: r.height
            };
        } catch (e) { return null; }
    }

    /** Add a visible spotlight ring class to the target element and raise its z-index. */
    function addSpotlightRing(selector) {
        try {
            var el = document.querySelector(selector);
            if (el) el.classList.add('portal-spotlight-ring');
        } catch (e) {}
    }

    /** Remove the spotlight ring class. */
    function removeSpotlightRing(selector) {
        try {
            var el = document.querySelector(selector);
            if (el) el.classList.remove('portal-spotlight-ring');
        } catch (e) {}
    }

    return { getRect: getRect, addSpotlightRing: addSpotlightRing, removeSpotlightRing: removeSpotlightRing };
}());

// ═══════════════════════════════════════════════════════════════════
// TEMPLATE BROWSER — localStorage utilities
// ═══════════════════════════════════════════════════════════════════
window.portalTmpl = {
    _k: {
        favs: 'fc_tmpl_favorites',
        recent: 'fc_tmpl_recently_used',
        searches: 'fc_tmpl_recent_searches'
    },
    _get: function (key) {
        try { return JSON.parse(localStorage.getItem(key) || '[]'); } catch { return []; }
    },
    _set: function (key, arr) {
        try { localStorage.setItem(key, JSON.stringify(arr)); } catch { }
    },

    /** @returns {string[]} */
    getFavorites: function () { return window.portalTmpl._get(window.portalTmpl._k.favs); },

    /** Toggle a returnCode in favorites; returns updated array */
    toggleFavorite: function (code) {
        var list = window.portalTmpl.getFavorites();
        var idx = list.indexOf(code);
        if (idx >= 0) list.splice(idx, 1);
        else list.push(code);
        window.portalTmpl._set(window.portalTmpl._k.favs, list);
        return list;
    },

    /** @returns {string[]} */
    getRecentlyUsed: function () { return window.portalTmpl._get(window.portalTmpl._k.recent); },

    /** Move code to front of recently-used list (capped at 12) */
    markRecentlyUsed: function (code) {
        var list = window.portalTmpl.getRecentlyUsed().filter(function (c) { return c !== code; });
        list.unshift(code);
        window.portalTmpl._set(window.portalTmpl._k.recent, list.slice(0, 12));
    },

    /** @returns {string[]} */
    getRecentSearches: function () { return window.portalTmpl._get(window.portalTmpl._k.searches); },

    /** Add a query to front of recent-searches list (capped at 8; ignores short queries) */
    addRecentSearch: function (q) {
        if (!q || q.trim().length < 2) return;
        var trimmed = q.trim();
        var list = window.portalTmpl.getRecentSearches().filter(function (s) { return s !== trimmed; });
        list.unshift(trimmed);
        window.portalTmpl._set(window.portalTmpl._k.searches, list.slice(0, 8));
    }
};

// ═══════════════════════════════════════════════════════════════════════════
// §IP — Institution Profile: Logo Cropper
// ═══════════════════════════════════════════════════════════════════════════

window.portalLogoCropper = (function () {
    var _container = null;
    var _img = null;
    var _state = { x: 0, y: 0, zoom: 1, isDragging: false, startX: 0, startY: 0 };
    var _handlers = {};

    function applyTransform() {
        if (!_img) return;
        _img.style.transform =
            'translate(calc(-50% + ' + _state.x + 'px), calc(-50% + ' + _state.y + 'px)) scale(' + _state.zoom + ')';
    }

    function destroy() {
        if (_container) {
            _container.removeEventListener('mousedown', _handlers.mousedown);
            _container.removeEventListener('touchstart', _handlers.touchstart, { passive: false });
        }
        document.removeEventListener('mousemove', _handlers.mousemove);
        document.removeEventListener('mouseup', _handlers.mouseup);
        document.removeEventListener('touchmove', _handlers.touchmove);
        document.removeEventListener('touchend', _handlers.touchend);
        _container = null; _img = null;
        _handlers = {};
    }

    return {
        init: function () {
            // No-op on first render; called once after first render to satisfy Blazor interop
        },

        load: function (containerId, imgSrc) {
            destroy();
            _container = document.getElementById(containerId);
            if (!_container) return;
            _img = _container.querySelector('img');
            if (!_img) return;

            _state = { x: 0, y: 0, zoom: 1, isDragging: false, startX: 0, startY: 0 };

            _handlers.mousedown = function (e) {
                _state.isDragging = true;
                _state.startX = e.clientX - _state.x;
                _state.startY = e.clientY - _state.y;
                e.preventDefault();
            };
            _handlers.mousemove = function (e) {
                if (!_state.isDragging) return;
                _state.x = e.clientX - _state.startX;
                _state.y = e.clientY - _state.startY;
                applyTransform();
            };
            _handlers.mouseup = function () { _state.isDragging = false; };

            _handlers.touchstart = function (e) {
                if (e.touches.length !== 1) return;
                _state.isDragging = true;
                _state.startX = e.touches[0].clientX - _state.x;
                _state.startY = e.touches[0].clientY - _state.y;
                e.preventDefault();
            };
            _handlers.touchmove = function (e) {
                if (!_state.isDragging || e.touches.length !== 1) return;
                _state.x = e.touches[0].clientX - _state.startX;
                _state.y = e.touches[0].clientY - _state.startY;
                applyTransform();
                e.preventDefault();
            };
            _handlers.touchend = function () { _state.isDragging = false; };

            _container.addEventListener('mousedown', _handlers.mousedown);
            document.addEventListener('mousemove', _handlers.mousemove);
            document.addEventListener('mouseup', _handlers.mouseup);
            _container.addEventListener('touchstart', _handlers.touchstart, { passive: false });
            document.addEventListener('touchmove', _handlers.touchmove, { passive: false });
            document.addEventListener('touchend', _handlers.touchend);

            applyTransform();
        },

        setZoom: function (zoomFactor) {
            _state.zoom = zoomFactor;
            applyTransform();
        },

        reset: function () {
            _state.x = 0; _state.y = 0; _state.zoom = 1;
            applyTransform();
        },

        destroy: destroy
    };
})();

// ── Dark Mode Theme Toggle ────────────────────────────────────────────────
window.portalTheme = {
    _dotNet: null,
    _mq: null,

    init: function (dotNetRef) {
        this._dotNet = dotNetRef;
        var saved = localStorage.getItem('portal-theme');
        if (saved) {
            this._apply(saved === 'dark');
        } else {
            this._mq = window.matchMedia('(prefers-color-scheme: dark)');
            this._apply(this._mq.matches);
            var self = this;
            this._mq.addEventListener('change', function (e) {
                if (!localStorage.getItem('portal-theme')) self._apply(e.matches);
            });
        }
    },

    toggle: function () {
        var isDark = document.documentElement.getAttribute('data-theme') === 'dark';
        var next = !isDark;
        localStorage.setItem('portal-theme', next ? 'dark' : 'light');
        this._apply(next);
    },

    _apply: function (isDark) {
        if (isDark) {
            document.documentElement.setAttribute('data-theme', 'dark');
        } else {
            document.documentElement.removeAttribute('data-theme');
        }
        if (typeof window.portalUpdateChartsTheme === 'function') {
            window.portalUpdateChartsTheme(isDark);
        }
        if (this._dotNet) {
            this._dotNet.invokeMethodAsync('OnThemeChanged', isDark);
        }
    },

    isDark: function () {
        return document.documentElement.getAttribute('data-theme') === 'dark';
    }
};

// ── Team Management Hub helpers ──────────────────────────────────────────
window.portalTeam = (function () {
    'use strict';

    function parseCsv(text) {
        var lines = text.trim().split(/\r?\n/);
        if (lines.length < 2) return [];
        var rows = [];
        for (var i = 1; i < lines.length; i++) {
            var cols = lines[i].split(',').map(function (c) { return c.trim().replace(/^"|"$/g, ''); });
            if (cols.length >= 2) {
                rows.push({
                    email: cols[0] || '',
                    name: cols[1] || '',
                    role: cols[2] || 'Maker',
                    error: (!cols[0] || !cols[0].includes('@')) ? 'Invalid email' : ''
                });
            }
        }
        return rows;
    }

    function downloadTemplate() {
        var csv = 'email,name,role\nuser@example.com,Jane Smith,Maker\nuser2@example.com,John Doe,Checker\n';
        var blob = new Blob([csv], { type: 'text/csv' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url; a.download = 'team-import-template.csv';
        document.body.appendChild(a); a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    function readCsvFile(inputId, dotNetRef) {
        var input = document.getElementById(inputId);
        if (!input || !input.files || !input.files[0]) return;
        var reader = new FileReader();
        reader.onload = function (e) {
            var rows = parseCsv(e.target.result);
            dotNetRef.invokeMethodAsync('OnCsvParsed', rows);
        };
        reader.readAsText(input.files[0]);
    }

    function timeAgo(isoString) {
        if (!isoString) return 'Never';
        var diff = Date.now() - new Date(isoString).getTime();
        var m = Math.floor(diff / 60000);
        if (m < 1) return 'Just now';
        if (m < 60) return m + ' min ago';
        var h = Math.floor(m / 60);
        if (h < 24) return h + ' hour' + (h > 1 ? 's' : '') + ' ago';
        var d = Math.floor(h / 24);
        if (d < 30) return d + ' day' + (d > 1 ? 's' : '') + ' ago';
        return new Date(isoString).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
    }

    return { downloadTemplate: downloadTemplate, readCsvFile: readCsvFile, timeAgo: timeAgo };
})();

// ── Period Picker keyboard navigation ────────────────────────────────────
window.portalPeriodPicker = {
    /**
     * Move focus within the period picker grid using arrow keys.
     * Cells carry data-pp-row (1-12) and data-pp-col (0-2) attributes.
     */
    moveFocus: function (key) {
        const focused = document.activeElement;
        if (!focused || !focused.dataset.ppRow) return;

        const row = parseInt(focused.dataset.ppRow, 10);
        const col = parseInt(focused.dataset.ppCol, 10);
        const grid = focused.closest('[data-pp-grid]');
        if (!grid) return;

        let nr = row, nc = col;
        if (key === 'ArrowRight')     nc = Math.min(nc + 1, 2);
        else if (key === 'ArrowLeft') nc = Math.max(nc - 1, 0);
        else if (key === 'ArrowDown') nr = Math.min(nr + 1, 12);
        else if (key === 'ArrowUp')   nr = Math.max(nr - 1, 1);

        const target = grid.querySelector(
            `[data-pp-row="${nr}"][data-pp-col="${nc}"]:not([disabled])`
        );
        if (target) target.focus();
    }
};

// ── Login Page Interactivity ───────────────────────────────────────────────────
window.portalLogin = (function () {

    function initPasswordToggles() {
        document.querySelectorAll('[data-pw-toggle]').forEach(function (btn) {
            var targetId = btn.getAttribute('data-pw-toggle');
            var input = document.getElementById(targetId);
            if (!input) return;

            btn.addEventListener('click', function () {
                var showing = input.type === 'text';
                input.type = showing ? 'password' : 'text';
                btn.setAttribute('aria-label', showing ? 'Show password' : 'Hide password');
                btn.setAttribute('title',      showing ? 'Show password' : 'Hide password');
                var showIcon = btn.querySelector('.portal-pw-show-icon');
                var hideIcon = btn.querySelector('.portal-pw-hide-icon');
                if (showIcon) showIcon.style.display = showing ? '' : 'none';
                if (hideIcon) hideIcon.style.display = showing ? 'none' : '';
            });
        });
    }

    function initCapsLock() {
        var pwInput    = document.getElementById('password');
        var capsWarn   = document.getElementById('capsLockWarning');
        if (!pwInput || !capsWarn) return;

        function check(e) {
            if (typeof e.getModifierState !== 'function') return;
            capsWarn.classList.toggle('is-visible', e.getModifierState('CapsLock'));
        }

        pwInput.addEventListener('keydown', check);
        pwInput.addEventListener('keyup',   check);
    }

    function initMfaDigits() {
        var container    = document.getElementById('mfaDigitsContainer');
        var fallback     = document.getElementById('mfaFallback');
        var hiddenInput  = document.getElementById('mfaCodeHidden');
        if (!container || !hiddenInput) return;

        // Reveal 6-digit UX, disable fallback input
        container.style.display = 'flex';
        if (fallback) {
            var fallbackInput = fallback.querySelector('input');
            if (fallbackInput) fallbackInput.disabled = true;
            fallback.style.display = 'none';
        }

        var digits = container.querySelectorAll('.portal-mfa-digit');

        function syncHidden() {
            var val = '';
            digits.forEach(function (d) { val += d.value; });
            hiddenInput.value = val;
        }

        function markFilled(digit) {
            digit.classList.toggle('portal-mfa-digit--filled', digit.value.length > 0);
        }

        digits.forEach(function (digit, i) {
            digit.addEventListener('input', function () {
                // Keep only last single digit
                digit.value = digit.value.replace(/\D/g, '').slice(-1);
                markFilled(digit);
                syncHidden();
                if (digit.value && i < digits.length - 1) {
                    digits[i + 1].focus();
                }
            });

            digit.addEventListener('keydown', function (e) {
                if (e.key === 'Backspace' && !digit.value && i > 0) {
                    digits[i - 1].focus();
                }
            });

            digit.addEventListener('paste', function (e) {
                e.preventDefault();
                var text = (e.clipboardData || window.clipboardData).getData('text').replace(/\D/g, '');
                digits.forEach(function (d, j) {
                    d.value = text[j] || '';
                    markFilled(d);
                });
                syncHidden();
                // Focus first empty or last
                var firstEmpty = -1;
                digits.forEach(function (d, j) { if (firstEmpty < 0 && !d.value) firstEmpty = j; });
                (firstEmpty >= 0 ? digits[firstEmpty] : digits[digits.length - 1]).focus();
            });
        });

        // Auto-focus first digit
        if (digits.length) digits[0].focus();
    }

    function initSubmitLoading() {
        var forms = document.querySelectorAll('.portal-login-form');
        forms.forEach(function (form) {
            var btn      = form.querySelector('#loginSubmitBtn');
            var textEl   = btn && btn.querySelector('.portal-btn-submit-text');
            var spinner  = btn && btn.querySelector('.portal-btn-submit-spinner');
            if (!btn) return;

            form.addEventListener('submit', function () {
                btn.classList.add('is-loading');
                btn.disabled = true;
                if (spinner) spinner.style.display = 'block';

                // After short delay, update label to "Verifying…"
                setTimeout(function () {
                    if (textEl) textEl.textContent = 'Verifying\u2026';
                }, 600);
            });
        });
    }

    function init() {
        if (!document.querySelector('.portal-login-layout')) return;
        initPasswordToggles();
        initCapsLock();
        initMfaDigits();
        initSubmitLoading();
    }

    // Auto-init on DOMContentLoaded (full-page loads)
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    return { init: init };
})();

// ── Certificate page helpers ──────────────────────────────────────────────────

/**
 * Triggers the browser's print dialog for the current page.
 * Used by the Print button on the certificate page.
 */
window.portalCertPrint = function () {
    window.print();
};

// ── Cross-Sheet Comparator ────────────────────────────────────────────────────
window.xsComparator = (function () {
    var _handler = null;
    return {
        init: function () {
            if (_handler) return;
            _handler = function (e) {
                if (e.key !== 'f' && e.key !== 'F') return;
                var active = document.activeElement;
                if (active && (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' ||
                    active.tagName === 'SELECT' || active.isContentEditable)) return;
                var el = document.getElementById('xs-search');
                if (el) { e.preventDefault(); el.focus(); el.select(); }
            };
            document.addEventListener('keydown', _handler);
        },
        dispose: function () {
            if (_handler) { document.removeEventListener('keydown', _handler); _handler = null; }
        },
        exportPdf: function () { window.print(); }
    };
})();

// ── Financial Input Helper (FIH) ─────────────────────────────────────────
window.portalFIH = (function () {
    'use strict';
    var _handlers = [];

    function init(containerId, dotNetRef) {
        dispose();
        var container = document.getElementById(containerId) || document.body;
        var inputs = container.querySelectorAll('[data-fih-numeric="true"]');
        inputs.forEach(function (input) {
            var handler = function (e) { handlePaste(e, input, dotNetRef); };
            input.addEventListener('paste', handler);
            _handlers.push({ input: input, handler: handler });
        });
    }

    function handlePaste(e, input, dotNetRef) {
        var text = (e.clipboardData || window.clipboardData).getData('text');
        var result = parsePastedValue(text);
        if (result === null) return; // no special handling — let default paste proceed
        e.preventDefault();
        var displayVal = result.toLocaleString('en-NG', { maximumFractionDigits: 2 });
        input.value = displayVal;
        // Fire synthetic input event so Blazor picks up the new value
        input.dispatchEvent(new InputEvent('input', { bubbles: true, cancelable: true }));
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnPasteConverted', displayVal).catch(function () { });
        }
    }

    function parsePastedValue(text) {
        if (!text) return null;
        var t = text.trim();
        // Shorthand: 1.2M, 1.5B, 500K, 2.1T (with optional ₦ prefix)
        var shorthand = t.match(/^[₦#\$£€]?\s*([\d,]+\.?\d*)\s*([KMBT])\b/i);
        if (shorthand) {
            var n = parseFloat(shorthand[1].replace(/,/g, ''));
            var mult = { K: 1e3, M: 1e6, B: 1e9, T: 1e12 }[shorthand[2].toUpperCase()];
            if (!isNaN(n) && mult) return n * mult;
        }
        // Detect formatted values: strip ₦, commas, trailing unit text, spaces
        var hadFormatting = /[₦#\$£€,]/.test(t) || /thousands?|'000/i.test(t);
        if (!hadFormatting) return null;
        var stripped = t
            .replace(/[₦#\$£€]/g, '')
            .replace(/'?000s?|thousands?/gi, '')
            .replace(/,/g, '')
            .replace(/\s/g, '');
        var parsed = parseFloat(stripped);
        if (isNaN(parsed)) return null;
        return parsed;
    }

    function dispose() {
        _handlers.forEach(function (h) {
            h.input.removeEventListener('paste', h.handler);
        });
        _handlers = [];
    }

    return { init: init, dispose: dispose };
})();
