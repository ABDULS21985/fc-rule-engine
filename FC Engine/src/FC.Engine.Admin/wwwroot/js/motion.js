/**
 * FC Engine — Motion Design System (JS Interop)
 * Handles: Ripple effects, number counters, card tilt, toast dismiss, form shake.
 * All interactions respect prefers-reduced-motion.
 */

window.FCMotion = (() => {
    'use strict';

    const prefersReducedMotion = () =>
        window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    /* ================================================================
       1. RIPPLE EFFECT — Click position-based ripple on buttons
       ================================================================ */

    function initRipple() {
        document.addEventListener('click', (e) => {
            if (prefersReducedMotion()) return;

            const btn = e.target.closest('.fc-btn');
            if (!btn || btn.disabled) return;

            const rect = btn.getBoundingClientRect();
            const size = Math.max(rect.width, rect.height) * 2;
            const x = e.clientX - rect.left - size / 2;
            const y = e.clientY - rect.top - size / 2;

            const ripple = document.createElement('span');
            ripple.className = 'fc-ripple';
            ripple.style.cssText = `width:${size}px;height:${size}px;left:${x}px;top:${y}px;`;

            btn.appendChild(ripple);
            ripple.addEventListener('animationend', () => ripple.remove());
        });
    }


    /* ================================================================
       2. ANIMATED COUNTER — Smooth counting for dashboard stats
       ================================================================ */

    const activeCounters = new Map();

    function animateCounter(elementId, targetValue, duration, prefix, suffix, decimals) {
        const el = document.getElementById(elementId);
        if (!el) return;

        // Cancel any running animation
        if (activeCounters.has(elementId)) {
            cancelAnimationFrame(activeCounters.get(elementId));
        }

        if (prefersReducedMotion()) {
            el.textContent = formatNumber(targetValue, prefix, suffix, decimals);
            return;
        }

        const startValue = parseFloat(el.dataset.currentValue) || 0;
        const startTime = performance.now();
        duration = duration || 800;
        prefix = prefix || '';
        suffix = suffix || '';
        decimals = decimals || 0;

        function update(currentTime) {
            const elapsed = currentTime - startTime;
            const progress = Math.min(elapsed / duration, 1);

            // Ease-out cubic
            const eased = 1 - Math.pow(1 - progress, 3);
            const current = startValue + (targetValue - startValue) * eased;

            el.textContent = formatNumber(current, prefix, suffix, decimals);
            el.dataset.currentValue = current;

            if (progress < 1) {
                activeCounters.set(elementId, requestAnimationFrame(update));
            } else {
                el.textContent = formatNumber(targetValue, prefix, suffix, decimals);
                el.dataset.currentValue = targetValue;
                activeCounters.delete(elementId);

                // Pop effect
                el.classList.add('fc-counter-pop');
                el.addEventListener('animationend', () => {
                    el.classList.remove('fc-counter-pop');
                }, { once: true });
            }
        }

        activeCounters.set(elementId, requestAnimationFrame(update));
    }

    function formatNumber(value, prefix, suffix, decimals) {
        const formatted = decimals > 0
            ? value.toFixed(decimals)
            : Math.round(value).toLocaleString();
        return `${prefix}${formatted}${suffix}`;
    }


    /* ================================================================
       3. CARD 3D TILT — Mouse-tracking perspective tilt
       ================================================================ */

    function initCardTilt() {
        document.addEventListener('mousemove', (e) => {
            if (prefersReducedMotion()) return;

            const cards = document.querySelectorAll('.fc-motion-tilt-card, .fc-motion-tilt > .fc-card');
            cards.forEach(card => {
                const rect = card.getBoundingClientRect();
                const isHovered = (
                    e.clientX >= rect.left && e.clientX <= rect.right &&
                    e.clientY >= rect.top && e.clientY <= rect.bottom
                );

                if (isHovered) {
                    const x = (e.clientX - rect.left) / rect.width;
                    const y = (e.clientY - rect.top) / rect.height;
                    const tiltX = (y - 0.5) * 3;  // max 1.5deg
                    const tiltY = (0.5 - x) * 3;  // max 1.5deg

                    card.style.transform = `translateY(-4px) rotateX(${tiltX}deg) rotateY(${tiltY}deg)`;
                    card.style.setProperty('--mouse-x', `${x * 100}%`);
                    card.style.setProperty('--mouse-y', `${y * 100}%`);
                }
            });
        });

        document.addEventListener('mouseleave', (e) => {
            const card = e.target.closest && e.target.closest('.fc-motion-tilt-card, .fc-motion-tilt > .fc-card');
            if (card) {
                card.style.transform = '';
                card.style.removeProperty('--mouse-x');
                card.style.removeProperty('--mouse-y');
            }
        }, true);
    }


    /* ================================================================
       4. TOAST DISMISS — Slide-out animation before removal
       ================================================================ */

    function dismissToast(toastElement) {
        if (!toastElement) return;

        toastElement.classList.add('fc-toast-exiting');
        toastElement.addEventListener('animationend', () => {
            toastElement.remove();
        }, { once: true });
    }


    /* ================================================================
       5. FORM SHAKE — Trigger shake on validation error
       ================================================================ */

    function shakeElement(elementId) {
        const el = document.getElementById(elementId);
        if (!el) return;

        el.classList.remove('fc-motion-shake');
        // Force reflow
        void el.offsetWidth;
        el.classList.add('fc-motion-shake');

        el.addEventListener('animationend', () => {
            el.classList.remove('fc-motion-shake');
        }, { once: true });
    }


    /* ================================================================
       6. BUTTON LOADING — Morph button to loading state
       ================================================================ */

    function setButtonLoading(elementId, isLoading) {
        const btn = document.getElementById(elementId);
        if (!btn) return;

        if (isLoading) {
            btn.classList.add('fc-btn--loading');
            btn.setAttribute('aria-busy', 'true');
        } else {
            btn.classList.remove('fc-btn--loading');
            btn.removeAttribute('aria-busy');
        }
    }


    /* ================================================================
       7. INTERSECTION OBSERVER — Animate elements on scroll into view
       ================================================================ */

    function initScrollAnimations() {
        if (prefersReducedMotion()) return;

        const observer = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    entry.target.classList.add('fc-motion-visible');
                    observer.unobserve(entry.target);
                }
            });
        }, {
            threshold: 0.1,
            rootMargin: '0px 0px -40px 0px'
        });

        document.querySelectorAll('.fc-motion-on-scroll').forEach(el => {
            observer.observe(el);
        });
    }


    /* ================================================================
       8. MODAL ENHANCED EXIT — Scale-down + fade before removal
       ================================================================ */

    function animateModalExit(backdropSelector, modalSelector) {
        const backdrop = document.querySelector(backdropSelector || '.fc-modal-backdrop-active');
        const modal = document.querySelector(modalSelector || '.fc-modal-active');

        if (modal) modal.classList.add('fc-modal-exit');
        if (backdrop) backdrop.classList.add('fc-modal-backdrop-exit');

        return new Promise(resolve => {
            setTimeout(resolve, 300);
        });
    }


    /* ================================================================
       INITIALIZATION
       ================================================================ */

    function init() {
        initRipple();
        initCardTilt();
        initScrollAnimations();
    }

    // Auto-init on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Re-init on Blazor enhanced navigation
    if (typeof Blazor !== 'undefined') {
        Blazor.addEventListener('enhancedload', () => {
            initScrollAnimations();
        });
    }

    // Public API for Blazor interop
    return {
        animateCounter,
        dismissToast,
        shakeElement,
        setButtonLoading,
        animateModalExit,
        initScrollAnimations
    };
})();
