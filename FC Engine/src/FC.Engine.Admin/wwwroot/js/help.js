/**
 * fcHelp — JS interop for the FC Engine help system.
 *
 * Responsibilities:
 *   • Provide element bounding-rect queries for feature tour spotlight positioning.
 *   • Read/write localStorage flags for "What's New" and "first visit" tour suppression.
 *
 * Note: The "?" keyboard shortcut is handled by FCAccessibility.initShortcuts (accessibility.js)
 * to avoid duplicate listeners with the KeyboardShortcutsOverlay component.
 */
window.fcHelp = (() => {
    'use strict';

    let _dotNetRef = null;

    /**
     * Initialise the help JS layer (stores dotNetRef for future async use).
     * The "?" key listener is owned by FCAccessibility — not registered here.
     */
    function init(dotNetRef) {
        _dotNetRef = dotNetRef;
    }

    /**
     * Returns the bounding rect of the first element matching the CSS selector.
     * Used by FeatureTour to position the spotlight overlay.
     * Returns null if the element is not found.
     */
    function getElementRect(selector) {
        try {
            const el = selector ? document.querySelector(selector) : null;
            if (!el) return null;
            const r = el.getBoundingClientRect();
            return { top: r.top, left: r.left, width: r.width, height: r.height };
        } catch {
            return null;
        }
    }

    /**
     * Smoothly scrolls the target element into view (centered).
     */
    function scrollToElement(selector) {
        try {
            const el = selector ? document.querySelector(selector) : null;
            if (el) el.scrollIntoView({ behavior: 'smooth', block: 'center' });
        } catch { /* ignore */ }
    }

    /**
     * Returns true if the "What's New" modal should be shown for the given version
     * (i.e. the user has not dismissed it yet).
     */
    function checkWhatsNew(version) {
        try {
            return localStorage.getItem('fc-whats-new-seen') !== String(version);
        } catch {
            return false;
        }
    }

    /**
     * Marks the given version of "What's New" as seen.
     */
    function markWhatsNewSeen(version) {
        try {
            localStorage.setItem('fc-whats-new-seen', String(version));
        } catch { /* ignore */ }
    }

    /**
     * Returns true if the tour with the given ID has not yet been completed by the user.
     */
    function checkFirstVisit(tourId) {
        try {
            return !localStorage.getItem('fc-tour-' + tourId);
        } catch {
            return true;
        }
    }

    /**
     * Marks a tour as completed so it is not auto-started on subsequent visits.
     */
    function markTourSeen(tourId) {
        try {
            localStorage.setItem('fc-tour-' + tourId, '1');
        } catch { /* ignore */ }
    }

    /**
     * Clean up (release dotNetRef reference).
     */
    function destroy() {
        _dotNetRef = null;
    }

    return { init, getElementRect, scrollToElement, checkWhatsNew, markWhatsNewSeen, checkFirstVisit, markTourSeen, destroy };
})();
