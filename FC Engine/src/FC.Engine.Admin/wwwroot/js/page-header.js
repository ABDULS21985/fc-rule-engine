/**
 * fc-page-header.js
 * Scroll-aware sticky header: compact mode + blur backdrop
 */
window.fcPageHeader = (() => {
    const observers = new WeakMap();

    function initScroll(headerEl, dotNetRef) {
        if (!headerEl) return;

        // Use IntersectionObserver on a sentinel element placed above the header
        // to detect when the page has scrolled past the natural header position.
        // We watch the shell__content scroll container.
        const scrollContainer = headerEl.closest('.fc-shell__content') || document.documentElement;

        let ticking = false;
        let compact = false;

        function onScroll() {
            if (ticking) return;
            ticking = true;
            requestAnimationFrame(() => {
                const scrollTop = scrollContainer.scrollTop ?? window.scrollY;
                const newCompact = scrollTop > 40;
                if (newCompact !== compact) {
                    compact = newCompact;
                    dotNetRef.invokeMethodAsync('SetCompact', compact);
                }
                ticking = false;
            });
        }

        scrollContainer.addEventListener('scroll', onScroll, { passive: true });

        // Store cleanup fn on the element
        observers.set(headerEl, () => {
            scrollContainer.removeEventListener('scroll', onScroll);
            dotNetRef.dispose();
        });
    }

    function destroy(headerEl) {
        const cleanup = observers.get(headerEl);
        if (cleanup) {
            cleanup();
            observers.delete(headerEl);
        }
    }

    return { initScroll, destroy };
})();
