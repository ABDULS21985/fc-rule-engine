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
