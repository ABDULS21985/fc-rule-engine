// FC Engine Portal — JS interop functions

window.portalCopyToClipboard = function (text) {
    return navigator.clipboard.writeText(text);
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

window.portalApplyFieldValidation = function (containerId, fieldStatuses) {
    var container = document.getElementById(containerId);
    if (!container) return;
    container.querySelectorAll(".portal-field-status").forEach(function (el) { el.remove(); });
    container.querySelectorAll(".portal-field-valid, .portal-field-error, .portal-field-warning")
        .forEach(function (el) {
            el.classList.remove("portal-field-valid", "portal-field-error", "portal-field-warning");
        });
    fieldStatuses.forEach(function (fs) {
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
