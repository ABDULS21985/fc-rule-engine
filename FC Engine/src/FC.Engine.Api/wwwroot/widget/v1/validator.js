/**
 * RegOS™ Embeddable Validation Widget v1
 *
 * Usage:
 *   <script src="https://cdn.regos.app/widget/v1/validator.js"></script>
 *   <div id="regos-validator" data-module="PSP_FINTECH" data-api-key="regos_live_..."></div>
 *
 * The widget auto-discovers elements with id="regos-validator" and renders
 * a real-time validation form with compliance score preview.
 */
(function () {
    'use strict';

    const WIDGET_VERSION = '1.0.0';
    const DEFAULT_API_BASE = '/api/v1';
    const DEBOUNCE_MS = 600;

    // ── Styles ──────────────────────────────────────────────────
    const STYLES = `
        .regos-widget {
            font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: #ffffff;
            border: 1px solid #e5e7eb;
            border-radius: 12px;
            padding: 24px;
            max-width: 640px;
            box-shadow: 0 1px 3px rgba(0,0,0,0.08);
        }
        .regos-widget * { box-sizing: border-box; }
        .regos-widget-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            margin-bottom: 20px;
            padding-bottom: 16px;
            border-bottom: 1px solid #f3f4f6;
        }
        .regos-widget-title {
            font-size: 16px;
            font-weight: 700;
            color: #111827;
            display: flex;
            align-items: center;
            gap: 8px;
        }
        .regos-widget-badge {
            font-size: 11px;
            font-weight: 600;
            padding: 2px 8px;
            border-radius: 9999px;
            background: #e0e7ff;
            color: #3730a3;
        }
        .regos-score-ring {
            width: 56px;
            height: 56px;
            position: relative;
        }
        .regos-score-ring svg {
            transform: rotate(-90deg);
            width: 56px;
            height: 56px;
        }
        .regos-score-ring .bg {
            fill: none;
            stroke: #f3f4f6;
            stroke-width: 5;
        }
        .regos-score-ring .fg {
            fill: none;
            stroke-width: 5;
            stroke-linecap: round;
            transition: stroke-dashoffset 0.5s ease, stroke 0.3s ease;
        }
        .regos-score-value {
            position: absolute;
            inset: 0;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 14px;
            font-weight: 700;
            color: #111827;
        }
        .regos-fields {
            display: flex;
            flex-direction: column;
            gap: 14px;
            margin-bottom: 20px;
        }
        .regos-field-group {
            display: flex;
            flex-direction: column;
            gap: 4px;
        }
        .regos-field-group label {
            font-size: 13px;
            font-weight: 600;
            color: #374151;
        }
        .regos-field-group input,
        .regos-field-group select {
            padding: 8px 12px;
            border: 1px solid #d1d5db;
            border-radius: 8px;
            font-size: 14px;
            color: #111827;
            background: #ffffff;
            transition: border-color 0.2s, box-shadow 0.2s;
            outline: none;
        }
        .regos-field-group input:focus,
        .regos-field-group select:focus {
            border-color: #6366f1;
            box-shadow: 0 0 0 3px rgba(99,102,241,0.1);
        }
        .regos-field-group input.regos-error {
            border-color: #ef4444;
            box-shadow: 0 0 0 3px rgba(239,68,68,0.1);
        }
        .regos-field-group input.regos-valid {
            border-color: #10b981;
        }
        .regos-field-hint {
            font-size: 12px;
            color: #ef4444;
            min-height: 16px;
        }
        .regos-results {
            background: #f9fafb;
            border: 1px solid #e5e7eb;
            border-radius: 8px;
            padding: 16px;
        }
        .regos-results-header {
            font-size: 13px;
            font-weight: 600;
            color: #374151;
            margin-bottom: 8px;
        }
        .regos-error-item {
            display: flex;
            align-items: flex-start;
            gap: 8px;
            padding: 6px 0;
            font-size: 13px;
            color: #6b7280;
            border-bottom: 1px solid #f3f4f6;
        }
        .regos-error-item:last-child { border-bottom: none; }
        .regos-error-icon {
            flex-shrink: 0;
            width: 16px;
            height: 16px;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 10px;
            font-weight: 700;
            margin-top: 2px;
        }
        .regos-error-icon.error { background: #fee2e2; color: #dc2626; }
        .regos-error-icon.warning { background: #fef3c7; color: #d97706; }
        .regos-loading {
            text-align: center;
            padding: 20px;
            color: #9ca3af;
            font-size: 13px;
        }
        .regos-powered {
            text-align: right;
            font-size: 11px;
            color: #9ca3af;
            margin-top: 12px;
        }
        .regos-powered a {
            color: #6366f1;
            text-decoration: none;
            font-weight: 600;
        }
    `;

    // ── Widget Class ────────────────────────────────────────────
    class RegOSValidator {
        constructor(container) {
            this.container = container;
            this.module = container.getAttribute('data-module') || '';
            this.apiKey = container.getAttribute('data-api-key') || '';
            this.apiBase = container.getAttribute('data-api-base') || DEFAULT_API_BASE;
            this.returnCode = container.getAttribute('data-return-code') || '';
            this.fields = [];
            this.errors = [];
            this.score = null;
            this.debounceTimer = null;

            this.init();
        }

        async init() {
            this.injectStyles();
            this.renderLoading();

            try {
                await this.loadTemplate();
                this.render();
            } catch (err) {
                this.renderError('Failed to load template: ' + err.message);
            }
        }

        injectStyles() {
            if (document.getElementById('regos-widget-styles')) return;
            const style = document.createElement('style');
            style.id = 'regos-widget-styles';
            style.textContent = STYLES;
            document.head.appendChild(style);
        }

        async loadTemplate() {
            const url = `${this.apiBase}/caas/templates/${encodeURIComponent(this.module)}`;
            const res = await fetch(url, {
                headers: { 'X-Api-Key': this.apiKey }
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();

            // Use the first return's fields, or match by returnCode
            const ret = this.returnCode
                ? data.returns?.find(r => r.returnCode === this.returnCode)
                : data.returns?.[0];

            this.returnCode = ret?.returnCode || this.returnCode;
            this.fields = ret?.fields || [];
        }

        render() {
            const circumference = 2 * Math.PI * 22;
            const scoreVal = this.score != null ? Math.round(this.score) : '—';
            const offset = this.score != null
                ? circumference - (this.score / 100) * circumference
                : circumference;
            const strokeColor = this.score >= 75 ? '#10b981' : this.score >= 50 ? '#f59e0b' : '#ef4444';

            this.container.innerHTML = `
                <div class="regos-widget">
                    <div class="regos-widget-header">
                        <div class="regos-widget-title">
                            Compliance Validator
                            <span class="regos-widget-badge">${this.escapeHtml(this.module)}</span>
                        </div>
                        <div class="regos-score-ring">
                            <svg viewBox="0 0 50 50">
                                <circle class="bg" cx="25" cy="25" r="22"/>
                                <circle class="fg" cx="25" cy="25" r="22"
                                    stroke="${this.score != null ? strokeColor : '#e5e7eb'}"
                                    stroke-dasharray="${circumference}"
                                    stroke-dashoffset="${offset}"/>
                            </svg>
                            <div class="regos-score-value">${scoreVal}</div>
                        </div>
                    </div>
                    <div class="regos-fields">
                        ${this.fields.map(f => this.renderField(f)).join('')}
                    </div>
                    ${this.renderResults()}
                    <div class="regos-powered">Powered by <a href="https://regos.app" target="_blank">RegOS™</a></div>
                </div>
            `;

            // Attach input listeners
            this.fields.forEach(f => {
                const input = this.container.querySelector(`[data-field="${f.fieldName}"]`);
                if (input) {
                    input.addEventListener('input', () => this.onFieldChange());
                }
            });
        }

        renderField(field) {
            const err = this.errors.find(e => e.field === field.fieldName);
            const cls = err ? 'regos-error' : (this.score != null ? 'regos-valid' : '');
            const type = this.mapFieldType(field.dataType);

            return `
                <div class="regos-field-group">
                    <label>${this.escapeHtml(field.displayName)}${field.isRequired ? ' *' : ''}</label>
                    <input type="${type}"
                           data-field="${this.escapeHtml(field.fieldName)}"
                           class="${cls}"
                           placeholder="${this.escapeHtml(field.description || field.displayName)}"
                           ${field.minValue ? `min="${field.minValue}"` : ''}
                           ${field.maxValue ? `max="${field.maxValue}"` : ''}
                           ${field.maxLength ? `maxlength="${field.maxLength}"` : ''} />
                    <div class="regos-field-hint">${err ? this.escapeHtml(err.message) : ''}</div>
                </div>
            `;
        }

        renderResults() {
            if (this.errors.length === 0 && this.score == null) return '';

            if (this.errors.length === 0) {
                return `<div class="regos-results">
                    <div class="regos-results-header" style="color:#059669;">✓ All validations passed</div>
                </div>`;
            }

            return `
                <div class="regos-results">
                    <div class="regos-results-header">
                        ${this.errors.length} issue${this.errors.length > 1 ? 's' : ''} found
                    </div>
                    ${this.errors.slice(0, 10).map(e => `
                        <div class="regos-error-item">
                            <div class="regos-error-icon ${e.severity === 'Error' ? 'error' : 'warning'}">
                                ${e.severity === 'Error' ? '✕' : '!'}
                            </div>
                            <div><strong>${this.escapeHtml(e.field)}</strong>: ${this.escapeHtml(e.message)}</div>
                        </div>
                    `).join('')}
                </div>
            `;
        }

        renderLoading() {
            this.container.innerHTML = `<div class="regos-widget"><div class="regos-loading">Loading template…</div></div>`;
        }

        renderError(msg) {
            this.container.innerHTML = `<div class="regos-widget"><div class="regos-loading" style="color:#ef4444;">${this.escapeHtml(msg)}</div></div>`;
        }

        onFieldChange() {
            clearTimeout(this.debounceTimer);
            this.debounceTimer = setTimeout(() => this.validate(), DEBOUNCE_MS);
        }

        async validate() {
            const record = {};
            this.fields.forEach(f => {
                const input = this.container.querySelector(`[data-field="${f.fieldName}"]`);
                if (input && input.value !== '') {
                    const val = (f.dataType === 'Money' || f.dataType === 'Decimal' || f.dataType === 'Integer' || f.dataType === 'Percentage')
                        ? parseFloat(input.value)
                        : input.value;
                    record[f.fieldName] = val;
                }
            });

            try {
                const res = await fetch(`${this.apiBase}/caas/validate`, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'X-Api-Key': this.apiKey
                    },
                    body: JSON.stringify({
                        moduleCode: this.module,
                        returnCode: this.returnCode,
                        records: [record]
                    })
                });

                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                const data = await res.json();

                this.errors = data.errors || [];
                this.score = data.complianceScorePreview;
                this.render();
            } catch (err) {
                console.error('[RegOS Widget] Validation failed:', err);
            }
        }

        mapFieldType(dataType) {
            switch (dataType) {
                case 'Money': case 'Decimal': case 'Integer': case 'Percentage':
                    return 'number';
                case 'Date':
                    return 'date';
                default:
                    return 'text';
            }
        }

        escapeHtml(str) {
            if (!str) return '';
            return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
        }
    }

    // ── Auto-Init ───────────────────────────────────────────────
    function initWidgets() {
        document.querySelectorAll('#regos-validator, [data-regos-validator]').forEach(el => {
            if (!el.__regosInit) {
                el.__regosInit = true;
                new RegOSValidator(el);
            }
        });
    }

    // Initialize on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initWidgets);
    } else {
        initWidgets();
    }

    // Expose for manual initialization
    window.RegOSValidator = RegOSValidator;
    window.RegOS = { version: WIDGET_VERSION, init: initWidgets };
})();
