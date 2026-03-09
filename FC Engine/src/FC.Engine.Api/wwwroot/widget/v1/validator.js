/**
 * RegOS™ Embedded Validator Widget v1.0
 * Zero external dependencies. Self-contained.
 * Usage:
 *   <script src="https://cdn.regos.app/widget/v1/validator.js"></script>
 *   <div id="regos-validator"
 *        data-module="PSP_FINTECH"
 *        data-period="2026-03"
 *        data-api-key="regos_live_..."
 *        data-api-base="https://api.regos.app"
 *        data-theme="#006AFF">
 *   </div>
 */
(function (window) {
    'use strict';

    const WIDGET_VERSION = '1.0.0';

    // ── Utilities ────────────────────────────────────────────────────────
    function debounce(fn, delayMs) {
        let timer;
        return function (...args) {
            clearTimeout(timer);
            timer = setTimeout(() => fn.apply(this, args), delayMs);
        };
    }

    function sanitizeHtml(str) {
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // ── API client ────────────────────────────────────────────────────────
    function CaaSClient(apiBase, apiKey) {
        this.apiBase = apiBase.replace(/\/$/, '');
        this.apiKey  = apiKey;
    }

    CaaSClient.prototype.get = async function (path) {
        const resp = await fetch(this.apiBase + path, {
            headers: { 'Authorization': 'Bearer ' + this.apiKey,
                       'Accept': 'application/json' }
        });
        if (!resp.ok) throw new Error('API error: ' + resp.status);
        return resp.json();
    };

    CaaSClient.prototype.post = async function (path, body) {
        const resp = await fetch(this.apiBase + path, {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + this.apiKey,
                       'Content-Type': 'application/json',
                       'Accept': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!resp.ok) throw new Error('API error: ' + resp.status);
        return resp.json();
    };

    // ── Widget renderer ───────────────────────────────────────────────────
    function RegOSValidator(container, options) {
        this.container  = container;
        this.options    = options;
        this.client     = new CaaSClient(options.apiBase, options.apiKey);
        this.template   = null;
        this.fieldValues = {};
        this.sessionToken = null;
        this._init();
    }

    RegOSValidator.prototype._init = async function () {
        this._renderSkeleton();
        try {
            const data = await this.client.get(
                '/api/v1/caas/templates/' + encodeURIComponent(this.options.module));
            this.template = data;
            this._renderForm(data);
        } catch (err) {
            this._renderError('Failed to load template: ' + err.message);
        }
    };

    RegOSValidator.prototype._renderSkeleton = function () {
        const theme = this.options.theme || '#006AFF';
        this.container.innerHTML =
            '<div class="regos-widget" style="--regos-primary:' + sanitizeHtml(theme) + '">' +
            '  <div class="regos-header">' +
            '    <span class="regos-logo">RegOS\u2122</span>' +
            '    <span class="regos-module-name">Loading\u2026</span>' +
            '  </div>' +
            '  <div class="regos-body regos-loading">' +
            '    <div class="regos-spinner"></div>' +
            '  </div>' +
            '</div>' +
            '<style>' + this._getStyles() + '</style>';
    };

    RegOSValidator.prototype._renderForm = function (template) {
        const self = this;
        const fields = template.fields.map(f => self._renderField(f)).join('');

        const body = this.container.querySelector('.regos-body');
        body.className = 'regos-body';
        body.innerHTML =
            '<form class="regos-form" id="regos-form-' + this.options.module + '">' +
            fields +
            '  <div class="regos-actions">' +
            '    <button type="button" class="regos-btn regos-btn-validate" id="regos-validate-btn">' +
            '      Validate' +
            '    </button>' +
            '    <button type="button" class="regos-btn regos-btn-submit" ' +
            '            id="regos-submit-btn" disabled>Submit</button>' +
            '  </div>' +
            '  <div class="regos-result" id="regos-result"></div>' +
            '</form>';

        // Bind field change events
        body.querySelectorAll('[data-field]').forEach(function (input) {
            input.addEventListener('input', debounce(function () {
                self.fieldValues[input.dataset.field] = input.value;
                self._liveValidate();
            }, 600));
        });

        // Validate button
        body.querySelector('#regos-validate-btn').addEventListener('click', function () {
            self._validate(true);
        });

        // Submit button
        body.querySelector('#regos-submit-btn').addEventListener('click', function () {
            self._submit();
        });

        // Update header
        this.container.querySelector('.regos-module-name').textContent = template.moduleName;
    };

    RegOSValidator.prototype._renderField = function (field) {
        const required = field.isRequired ? '<span class="regos-required">*</span>' : '';
        const inputType = field.dataType === 'DATE' ? 'date'
            : (field.dataType === 'BOOLEAN' ? 'checkbox' : 'number');

        return '<div class="regos-field" id="regos-field-' + sanitizeHtml(field.fieldCode) + '">' +
               '  <label class="regos-label">' +
               sanitizeHtml(field.fieldLabel) + required +
               '  </label>' +
               '  <input type="' + inputType + '"' +
               '         class="regos-input"' +
               '         data-field="' + sanitizeHtml(field.fieldCode) + '"' +
               (field.isRequired ? ' required' : '') +
               (field.minValue !== null ? ' min="' + field.minValue + '"' : '') +
               (field.maxValue !== null ? ' max="' + field.maxValue + '"' : '') +
               '  />' +
               '  <span class="regos-field-error" id="regos-err-' +
               sanitizeHtml(field.fieldCode) + '"></span>' +
               '</div>';
    };

    RegOSValidator.prototype._liveValidate = async function () {
        if (Object.keys(this.fieldValues).length < 2) return;
        try {
            const result = await this.client.post('/api/v1/caas/validate', {
                moduleCode: this.options.module,
                periodCode: this.options.period,
                fields: this.fieldValues,
                persistSession: false
            });
            this._updateFieldErrors(result.errors);
            this._updateScorePreview(result.complianceScore);
        } catch (_) { /* Live validation failures are non-blocking */ }
    };

    RegOSValidator.prototype._validate = async function (persistSession) {
        const resultDiv = this.container.querySelector('#regos-result');
        resultDiv.innerHTML = '<div class="regos-spinner"></div>';

        try {
            const result = await this.client.post('/api/v1/caas/validate', {
                moduleCode: this.options.module,
                periodCode: this.options.period,
                fields: this.fieldValues,
                persistSession: persistSession === true
            });

            this.sessionToken = result.sessionToken || null;
            this._updateFieldErrors(result.errors);

            if (result.isValid) {
                resultDiv.innerHTML =
                    '<div class="regos-alert regos-alert-success">' +
                    '\u2713 Validation passed. Score: ' + result.complianceScore.toFixed(1) + '/100' +
                    '</div>';
                const submitBtn = this.container.querySelector('#regos-submit-btn');
                if (submitBtn) submitBtn.disabled = false;
            } else {
                resultDiv.innerHTML =
                    '<div class="regos-alert regos-alert-error">' +
                    '\u2717 ' + result.errorCount + ' error(s) found. Please correct and re-validate.' +
                    '</div>';
            }

            // Emit custom event for host page integration
            this.container.dispatchEvent(new CustomEvent('regos:validated', {
                bubbles: true, detail: result
            }));
        } catch (err) {
            resultDiv.innerHTML =
                '<div class="regos-alert regos-alert-error">Validation failed: ' +
                sanitizeHtml(err.message) + '</div>';
        }
    };

    RegOSValidator.prototype._submit = async function () {
        if (!this.sessionToken && !this.options.autoSubmitRegulator) {
            this._validate(true).then(() => {
                if (this.sessionToken) this._submit();
            });
            return;
        }

        const resultDiv = this.container.querySelector('#regos-result');
        resultDiv.innerHTML = '<div class="regos-spinner"></div> Submitting\u2026';

        try {
            const result = await this.client.post('/api/v1/caas/submit', {
                sessionToken: this.sessionToken,
                regulatorCode: this.options.regulatorCode || 'CBN',
                submittedByExternalUserId: 0
            });

            if (result.success) {
                resultDiv.innerHTML =
                    '<div class="regos-alert regos-alert-success">' +
                    '\u2713 Submitted successfully. Receipt: ' +
                    sanitizeHtml(result.receiptReference || 'pending') + '</div>';
                this.container.querySelector('#regos-submit-btn').disabled = true;

                this.container.dispatchEvent(new CustomEvent('regos:submitted', {
                    bubbles: true, detail: result
                }));
            } else {
                resultDiv.innerHTML =
                    '<div class="regos-alert regos-alert-error">' +
                    sanitizeHtml(result.errorMessage || 'Submission failed.') + '</div>';
            }
        } catch (err) {
            resultDiv.innerHTML =
                '<div class="regos-alert regos-alert-error">Submit error: ' +
                sanitizeHtml(err.message) + '</div>';
        }
    };

    RegOSValidator.prototype._updateFieldErrors = function (errors) {
        // Clear all existing errors
        this.container.querySelectorAll('.regos-field-error').forEach(function (el) {
            el.textContent = '';
            el.closest('.regos-field').classList.remove('regos-field-invalid');
        });

        errors.forEach(function (err) {
            const errEl = document.getElementById('regos-err-' + err.fieldCode);
            if (errEl) {
                errEl.textContent = err.message;
                errEl.closest('.regos-field').classList.add('regos-field-invalid');
            }
        });
    };

    RegOSValidator.prototype._updateScorePreview = function (score) {
        let preview = this.container.querySelector('.regos-score-preview');
        if (!preview) {
            preview = document.createElement('div');
            preview.className = 'regos-score-preview';
            this.container.querySelector('.regos-actions').before(preview);
        }
        const colour = score >= 80 ? '#22c55e' : score >= 60 ? '#f59e0b' : '#ef4444';
        preview.innerHTML =
            '<span style="color:' + colour + '">Score: ' + score.toFixed(1) + '/100</span>';
    };

    RegOSValidator.prototype._renderError = function (msg) {
        this.container.innerHTML =
            '<div class="regos-widget"><div class="regos-alert regos-alert-error">' +
            sanitizeHtml(msg) + '</div></div>';
    };

    RegOSValidator.prototype._getStyles = function () {
        return `.regos-widget{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;
border:1px solid #e2e8f0;border-radius:8px;overflow:hidden;background:#fff}
.regos-header{background:var(--regos-primary,#006AFF);color:#fff;padding:12px 16px;
display:flex;justify-content:space-between;align-items:center}
.regos-logo{font-weight:700;font-size:14px}
.regos-module-name{font-size:12px;opacity:.85}
.regos-body{padding:16px}
.regos-field{margin-bottom:14px}
.regos-label{display:block;font-size:13px;font-weight:500;color:#374151;margin-bottom:4px}
.regos-required{color:#ef4444;margin-left:2px}
.regos-input{width:100%;padding:8px 10px;border:1px solid #d1d5db;border-radius:6px;
font-size:13px;box-sizing:border-box;transition:border-color .15s}
.regos-input:focus{outline:none;border-color:var(--regos-primary,#006AFF)}
.regos-field-invalid .regos-input{border-color:#ef4444}
.regos-field-error{color:#ef4444;font-size:11px;display:block;margin-top:3px}
.regos-actions{display:flex;gap:8px;margin-top:16px}
.regos-btn{padding:9px 20px;border:none;border-radius:6px;font-size:13px;
cursor:pointer;font-weight:500;transition:opacity .15s}
.regos-btn-validate{background:var(--regos-primary,#006AFF);color:#fff}
.regos-btn-submit{background:#10b981;color:#fff}
.regos-btn:disabled{opacity:.45;cursor:not-allowed}
.regos-alert{padding:10px 14px;border-radius:6px;font-size:13px;margin-top:12px}
.regos-alert-success{background:#ecfdf5;color:#065f46;border:1px solid #a7f3d0}
.regos-alert-error{background:#fef2f2;color:#991b1b;border:1px solid #fecaca}
.regos-spinner{display:inline-block;width:20px;height:20px;border:2px solid #e2e8f0;
border-top-color:var(--regos-primary,#006AFF);border-radius:50%;
animation:regos-spin .7s linear infinite}
.regos-loading{display:flex;justify-content:center;padding:32px}
.regos-score-preview{font-size:13px;font-weight:600;margin-bottom:8px}
@keyframes regos-spin{to{transform:rotate(360deg)}}`;
    };

    // ── Auto-initialise all matching containers ───────────────────────────
    function autoInit() {
        document.querySelectorAll('[id="regos-validator"], [data-regos-widget]')
            .forEach(function (container) {
                const d = container.dataset;
                if (!d.apiKey || !d.module) {
                    console.warn('RegOS Widget: data-api-key and data-module are required.');
                    return;
                }
                new RegOSValidator(container, {
                    module:           d.module,
                    period:           d.period || new Date().toISOString().slice(0, 7),
                    apiKey:           d.apiKey,
                    apiBase:          d.apiBase || 'https://api.regos.app',
                    theme:            d.theme || '#006AFF',
                    regulatorCode:    d.regulatorCode || 'CBN'
                });
            });
    }

    // Expose global API for manual initialisation
    window.RegOSValidator = RegOSValidator;

    // Auto-init on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', autoInit);
    } else {
        autoInit();
    }

})(window);
