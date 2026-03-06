/**
 * FC Engine — Advanced Form System (JS Interop)
 * Handles: Currency formatting, outside-click detection, drag-drop,
 *          rich text editing, calendar positioning, conditional field animations.
 * All interactions respect prefers-reduced-motion.
 */

window.FCForms = (() => {
    'use strict';

    const prefersReducedMotion = () =>
        window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    /* ================================================================
       1. CURRENCY INPUT — Real-time formatting with thousand separators
       ================================================================ */

    function initCurrencyInput(elementId, dotnetRef, symbol, decimals) {
        const el = document.getElementById(elementId);
        if (!el) return;

        el.addEventListener('input', () => {
            let raw = el.value.replace(/[^0-9.]/g, '');
            const parts = raw.split('.');
            if (parts.length > 2) raw = parts[0] + '.' + parts.slice(1).join('');

            const numParts = raw.split('.');
            numParts[0] = numParts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ',');
            if (numParts[1] !== undefined) {
                numParts[1] = numParts[1].substring(0, decimals || 2);
            }

            const formatted = numParts.join('.');
            const cursorPos = el.selectionStart;
            const prevLen = el.value.length;
            el.value = formatted;

            // Adjust cursor for added/removed commas
            const diff = el.value.length - prevLen;
            el.setSelectionRange(cursorPos + diff, cursorPos + diff);

            // Send numeric value back to Blazor
            const numericValue = parseFloat(raw) || 0;
            dotnetRef.invokeMethodAsync('OnJsValueChanged', numericValue);
        });

        el.addEventListener('blur', () => {
            let raw = el.value.replace(/[^0-9.]/g, '');
            const num = parseFloat(raw) || 0;
            el.value = num.toLocaleString('en-US', {
                minimumFractionDigits: decimals || 2,
                maximumFractionDigits: decimals || 2
            });
            dotnetRef.invokeMethodAsync('OnJsValueChanged', num);
        });
    }

    function setCurrencyValue(elementId, value, decimals) {
        const el = document.getElementById(elementId);
        if (!el) return;
        const num = parseFloat(value) || 0;
        el.value = num === 0 ? '' : num.toLocaleString('en-US', {
            minimumFractionDigits: decimals || 2,
            maximumFractionDigits: decimals || 2
        });
    }

    /* ================================================================
       2. OUTSIDE-CLICK — Close dropdowns when clicking elsewhere
       ================================================================ */

    const outsideClickHandlers = new Map();

    function onOutsideClick(elementId, dotnetRef, methodName) {
        dispose_outsideClick(elementId);

        const handler = (e) => {
            const el = document.getElementById(elementId);
            if (!el) return;
            if (!el.contains(e.target)) {
                dotnetRef.invokeMethodAsync(methodName || 'CloseDropdown');
            }
        };

        // Delay to avoid triggering on the click that opened the dropdown
        setTimeout(() => {
            document.addEventListener('mousedown', handler);
            outsideClickHandlers.set(elementId, handler);
        }, 0);
    }

    function dispose_outsideClick(elementId) {
        const handler = outsideClickHandlers.get(elementId);
        if (handler) {
            document.removeEventListener('mousedown', handler);
            outsideClickHandlers.delete(elementId);
        }
    }

    /* ================================================================
       3. DRAG AND DROP — File upload zone
       ================================================================ */

    function initDropZone(elementId, dotnetRef) {
        const el = document.getElementById(elementId);
        if (!el) return;

        let dragCounter = 0;

        el.addEventListener('dragenter', (e) => {
            e.preventDefault();
            dragCounter++;
            el.classList.add('fc-file-upload--dragover');
        });

        el.addEventListener('dragleave', (e) => {
            e.preventDefault();
            dragCounter--;
            if (dragCounter === 0) {
                el.classList.remove('fc-file-upload--dragover');
            }
        });

        el.addEventListener('dragover', (e) => {
            e.preventDefault();
        });

        el.addEventListener('drop', (e) => {
            e.preventDefault();
            dragCounter = 0;
            el.classList.remove('fc-file-upload--dragover');

            const files = e.dataTransfer.files;
            if (files.length > 0) {
                // Trigger the hidden file input
                const input = el.querySelector('input[type="file"]');
                if (input) {
                    // DataTransfer can be assigned to input.files in modern browsers
                    input.files = files;
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }
        });
    }

    function getFilePreview(file) {
        return new Promise((resolve) => {
            if (!file.type.startsWith('image/')) {
                resolve(null);
                return;
            }
            const reader = new FileReader();
            reader.onload = (e) => resolve(e.target.result);
            reader.readAsDataURL(file);
        });
    }

    /* ================================================================
       4. RICH TEXT EDITOR — contenteditable commands
       ================================================================ */

    function execCommand(elementId, command, value) {
        const el = document.getElementById(elementId);
        if (!el) return;

        el.focus();
        document.execCommand(command, false, value || null);
    }

    function getEditorHtml(elementId) {
        const el = document.getElementById(elementId);
        return el ? el.innerHTML : '';
    }

    function setEditorHtml(elementId, html) {
        const el = document.getElementById(elementId);
        if (el) el.innerHTML = html || '';
    }

    function initEditor(elementId, dotnetRef) {
        const el = document.getElementById(elementId);
        if (!el) return;

        el.addEventListener('input', () => {
            dotnetRef.invokeMethodAsync('OnContentChanged', el.innerHTML);
        });

        el.addEventListener('keydown', (e) => {
            // Tab inserts spaces instead of changing focus
            if (e.key === 'Tab') {
                e.preventDefault();
                document.execCommand('insertText', false, '    ');
            }
        });

        // Paste as plain text
        el.addEventListener('paste', (e) => {
            e.preventDefault();
            const text = e.clipboardData.getData('text/plain');
            document.execCommand('insertText', false, text);
        });
    }

    function queryCommandState(command) {
        return document.queryCommandState(command);
    }

    /* ================================================================
       5. DATE PICKER — Calendar positioning & keyboard nav
       ================================================================ */

    function positionDropdown(triggerId, dropdownId) {
        const trigger = document.getElementById(triggerId);
        const dropdown = document.getElementById(dropdownId);
        if (!trigger || !dropdown) return;

        const rect = trigger.getBoundingClientRect();
        const viewportH = window.innerHeight;
        const spaceBelow = viewportH - rect.bottom;
        const dropH = dropdown.offsetHeight || 320;

        if (spaceBelow < dropH && rect.top > dropH) {
            dropdown.style.bottom = '100%';
            dropdown.style.top = 'auto';
            dropdown.style.marginBottom = '4px';
            dropdown.style.marginTop = '0';
        } else {
            dropdown.style.top = '100%';
            dropdown.style.bottom = 'auto';
            dropdown.style.marginTop = '4px';
            dropdown.style.marginBottom = '0';
        }
    }

    /* ================================================================
       6. CONDITIONAL FIELD — Smooth height animation
       ================================================================ */

    function animateHeight(elementId, show) {
        const el = document.getElementById(elementId);
        if (!el) return;

        if (prefersReducedMotion()) {
            el.style.display = show ? '' : 'none';
            return;
        }

        if (show) {
            el.style.display = '';
            el.style.overflow = 'hidden';
            const height = el.scrollHeight;
            el.style.maxHeight = '0px';
            el.style.opacity = '0';

            requestAnimationFrame(() => {
                el.style.transition = 'max-height 300ms cubic-bezier(0.4,0,0.2,1), opacity 200ms ease';
                el.style.maxHeight = height + 'px';
                el.style.opacity = '1';
            });

            el.addEventListener('transitionend', function handler() {
                el.style.maxHeight = '';
                el.style.overflow = '';
                el.style.transition = '';
                el.removeEventListener('transitionend', handler);
            }, { once: true });
        } else {
            el.style.overflow = 'hidden';
            el.style.maxHeight = el.scrollHeight + 'px';

            requestAnimationFrame(() => {
                el.style.transition = 'max-height 250ms cubic-bezier(0.4,0,0.2,1), opacity 150ms ease';
                el.style.maxHeight = '0px';
                el.style.opacity = '0';
            });

            el.addEventListener('transitionend', function handler() {
                if (!el.style.maxHeight || el.style.maxHeight === '0px') {
                    el.style.display = 'none';
                }
                el.style.overflow = '';
                el.style.transition = '';
                el.removeEventListener('transitionend', handler);
            }, { once: true });
        }
    }

    /* ================================================================
       7. MULTI-SELECT — Keyboard navigation helpers
       ================================================================ */

    function focusElement(elementId) {
        const el = document.getElementById(elementId);
        if (el) el.focus();
    }

    function scrollIntoView(elementId) {
        const el = document.getElementById(elementId);
        if (el) el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }

    /* ================================================================
       8. FORM FIELD — Validation animation
       ================================================================ */

    function slideInError(elementId) {
        const el = document.getElementById(elementId);
        if (!el || prefersReducedMotion()) return;

        el.style.maxHeight = '0';
        el.style.opacity = '0';
        el.style.display = '';

        requestAnimationFrame(() => {
            el.style.transition = 'max-height 200ms ease, opacity 150ms ease';
            el.style.maxHeight = el.scrollHeight + 'px';
            el.style.opacity = '1';
        });

        el.addEventListener('transitionend', function handler() {
            el.style.maxHeight = '';
            el.style.transition = '';
            el.removeEventListener('transitionend', handler);
        }, { once: true });
    }

    function slideOutError(elementId) {
        const el = document.getElementById(elementId);
        if (!el || prefersReducedMotion()) {
            if (el) el.style.display = 'none';
            return;
        }

        el.style.maxHeight = el.scrollHeight + 'px';
        el.style.overflow = 'hidden';

        requestAnimationFrame(() => {
            el.style.transition = 'max-height 150ms ease, opacity 100ms ease';
            el.style.maxHeight = '0';
            el.style.opacity = '0';
        });

        el.addEventListener('transitionend', function handler() {
            el.style.display = 'none';
            el.style.overflow = '';
            el.style.transition = '';
            el.removeEventListener('transitionend', handler);
        }, { once: true });
    }

    /* ================================================================
       CLEANUP
       ================================================================ */

    function dispose(elementId) {
        dispose_outsideClick(elementId);
    }

    /* ================================================================
       PUBLIC API
       ================================================================ */

    return {
        // Currency
        initCurrencyInput,
        setCurrencyValue,
        // Outside click
        onOutsideClick,
        dispose_outsideClick,
        // Drag & drop
        initDropZone,
        getFilePreview,
        // Rich text
        execCommand,
        getEditorHtml,
        setEditorHtml,
        initEditor,
        queryCommandState,
        // Date picker
        positionDropdown,
        // Conditional field
        animateHeight,
        // Multi-select
        focusElement,
        scrollIntoView,
        // Form field
        slideInError,
        slideOutError,
        // Cleanup
        dispose
    };
})();
