/**
 * FC Engine Portal — Presence System
 * Handles field-focus tracking so other users can see who is editing which field.
 * The .NET side handles polling, avatar rendering, and toast notifications.
 */
window.portalPresence = (() => {
    let _dotNetRef = null;
    let _fieldListeners = [];   // { el, focus, blur }
    let _currentFieldId = null;
    let _cursorOverlays = {};   // fieldId → DOM element (ghost lock badge)

    /**
     * Attach focus/blur listeners to all form inputs inside a given selector.
     * On focus, notifies .NET so it can update the active field in the presence service.
     */
    function initFieldTracking(dotNetRef, formSelector) {
        _dotNetRef = dotNetRef;
        disposeFieldListeners();

        const container = document.querySelector(formSelector);
        if (!container) return;

        const inputs = container.querySelectorAll(
            'input[data-field-id], textarea[data-field-id], select[data-field-id]'
        );

        inputs.forEach(el => {
            const fieldId = el.dataset.fieldId;

            const onFocus = () => {
                _currentFieldId = fieldId;
                try { dotNetRef.invokeMethodAsync('OnFieldFocused', fieldId); } catch (_) {}
            };

            const onBlur = () => {
                if (_currentFieldId === fieldId) _currentFieldId = null;
                try { dotNetRef.invokeMethodAsync('OnFieldBlurred', fieldId); } catch (_) {}
            };

            el.addEventListener('focus', onFocus);
            el.addEventListener('blur', onBlur);
            _fieldListeners.push({ el, onFocus, onBlur });
        });
    }

    /**
     * Re-initialise field tracking (call after dynamic form sections are added).
     */
    function reinitFieldTracking(dotNetRef, formSelector) {
        initFieldTracking(dotNetRef, formSelector);
    }

    /**
     * Render lock badges on fields being edited by other users.
     * viewers: [{ fieldId, displayName, initials, role }]
     */
    function renderFieldLocks(viewers) {
        // Remove stale overlays
        Object.keys(_cursorOverlays).forEach(fieldId => {
            const isStillLocked = viewers.some(v => v.activeFieldId === fieldId);
            if (!isStillLocked) {
                const el = _cursorOverlays[fieldId];
                if (el?.parentNode) el.parentNode.removeChild(el);
                delete _cursorOverlays[fieldId];
            }
        });

        // Add / update overlays for locked fields
        viewers.forEach(v => {
            if (!v.activeFieldId) return;

            const input = document.querySelector(`[data-field-id="${CSS.escape(v.activeFieldId)}"]`);
            if (!input) return;

            let badge = _cursorOverlays[v.activeFieldId];
            if (!badge) {
                badge = document.createElement('div');
                badge.className = 'portal-field-lock-badge';
                badge.setAttribute('aria-live', 'polite');
                badge.setAttribute('role', 'status');

                // Position after input
                const parent = input.closest('.portal-form-group') || input.parentElement;
                if (parent) {
                    parent.style.position = 'relative';
                    parent.appendChild(badge);
                }
                _cursorOverlays[v.activeFieldId] = badge;
            }

            badge.innerHTML = `
                <span class="portal-field-lock-avatar" title="${escHtml(v.displayName)} (${escHtml(v.role)})" aria-hidden="true">
                    ${escHtml(v.initials)}
                </span>
                <span class="portal-field-lock-label">
                    <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" aria-hidden="true">
                        <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
                        <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                    </svg>
                    ${escHtml(v.displayName)} editing…
                </span>`;
        });
    }

    /**
     * Show a ghost cursor label for users editing a field (visual only, no mouse tracking).
     * This is a simplified version — shows a floating badge near the field.
     */
    function showConcurrentEditToast(userName) {
        // Delegated to .NET via toast service — this is just a JS helper for the .NET side.
        // The .NET presence component calls Toast.Info() directly.
    }

    function disposeFieldListeners() {
        _fieldListeners.forEach(({ el, onFocus, onBlur }) => {
            el.removeEventListener('focus', onFocus);
            el.removeEventListener('blur', onBlur);
        });
        _fieldListeners = [];
    }

    function disposeOverlays() {
        Object.values(_cursorOverlays).forEach(el => {
            if (el?.parentNode) el.parentNode.removeChild(el);
        });
        _cursorOverlays = {};
    }

    function dispose() {
        disposeFieldListeners();
        disposeOverlays();
        if (_dotNetRef) { try { _dotNetRef.dispose(); } catch (_) {} _dotNetRef = null; }
    }

    function escHtml(str) {
        return String(str ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    return { initFieldTracking, reinitFieldTracking, renderFieldLocks, dispose };
})();
