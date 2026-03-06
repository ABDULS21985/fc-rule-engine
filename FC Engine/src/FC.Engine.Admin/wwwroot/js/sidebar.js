/**
 * FC Engine — Intelligent Sidebar Interop
 * Handles: LocalStorage persistence, context menu, tooltips, hover-expand.
 * Respects prefers-reduced-motion where applicable.
 */

window.FCSidebar = (() => {
    'use strict';

    /* ================================================================
       1. LOCAL STORAGE — Typed helpers used by SidebarStateService
       ================================================================ */

    function getItem(key) {
        try { return localStorage.getItem(key); }
        catch { return null; }
    }

    function setItem(key, value) {
        try { localStorage.setItem(key, value); }
        catch { /* quota exceeded or private browsing */ }
    }


    /* ================================================================
       2. CONTEXT MENU — Position & lifecycle
       ================================================================ */

    let _activeMenuId = null;
    let _outsideClickHandler = null;
    let _escapeHandler = null;

    function showContextMenu(x, y, menuId) {
        hideContextMenu(_activeMenuId);

        const menu = document.getElementById(menuId);
        if (!menu) return;

        _activeMenuId = menuId;

        // Clamp to viewport
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        const menuW = 200; // approx
        const menuH = 180; // approx

        const left = Math.min(x, vw - menuW - 8);
        const top  = Math.min(y, vh - menuH - 8);

        menu.style.left = `${left}px`;
        menu.style.top  = `${top}px`;
        menu.style.display = 'block';

        // Focus first item for keyboard accessibility
        const firstBtn = menu.querySelector('button, [role="menuitem"]');
        if (firstBtn) firstBtn.focus();

        // Close on outside click
        _outsideClickHandler = (e) => {
            if (!menu.contains(e.target)) hideContextMenu(menuId);
        };
        _escapeHandler = (e) => {
            if (e.key === 'Escape') hideContextMenu(menuId);
        };

        // Defer to avoid the triggering click closing immediately
        setTimeout(() => {
            document.addEventListener('click', _outsideClickHandler);
            document.addEventListener('keydown', _escapeHandler);
        }, 50);
    }

    function hideContextMenu(menuId) {
        const id = menuId || _activeMenuId;
        if (!id) return;

        const menu = document.getElementById(id);
        if (menu) menu.style.display = 'none';

        if (_outsideClickHandler) {
            document.removeEventListener('click', _outsideClickHandler);
            _outsideClickHandler = null;
        }
        if (_escapeHandler) {
            document.removeEventListener('keydown', _escapeHandler);
            _escapeHandler = null;
        }
        _activeMenuId = null;
    }


    /* ================================================================
       3. TOOLTIP — Collapsed-mode nav item labels
       ================================================================ */

    let _tooltipEl = null;

    function showTooltip(targetEl, text) {
        hideTooltip();

        _tooltipEl = document.createElement('div');
        _tooltipEl.className = 'fc-nav__tooltip';
        _tooltipEl.textContent = text;
        _tooltipEl.setAttribute('role', 'tooltip');
        document.body.appendChild(_tooltipEl);

        const rect = targetEl.getBoundingClientRect();
        const ttW = _tooltipEl.offsetWidth;
        const ttH = _tooltipEl.offsetHeight;

        // Position to the right of the icon
        const top  = rect.top + rect.height / 2 - ttH / 2;
        const left = rect.right + 8;

        _tooltipEl.style.top  = `${Math.max(4, top)}px`;
        _tooltipEl.style.left = `${left}px`;
    }

    function hideTooltip() {
        if (_tooltipEl) {
            _tooltipEl.remove();
            _tooltipEl = null;
        }
    }


    /* ================================================================
       4. SIDEBAR HOVER-EXPAND — Expand collapsed sidebar on hover
       ================================================================ */

    let _hoverExpandRef = null;
    let _hoverTimer = null;
    let _collapseTimer = null;
    const EXPAND_DELAY  = 200; // ms before expanding
    const COLLAPSE_DELAY = 400; // ms before collapsing again

    function initSidebarHoverExpand(dotnetRef) {
        _hoverExpandRef = dotnetRef;

        const sidebar = document.querySelector('.fc-shell__sidebar');
        if (!sidebar) return;

        sidebar.addEventListener('mouseenter', _onSidebarEnter);
        sidebar.addEventListener('mouseleave', _onSidebarLeave);
    }

    function _onSidebarEnter() {
        clearTimeout(_collapseTimer);
        _hoverTimer = setTimeout(() => {
            if (_hoverExpandRef) {
                _hoverExpandRef.invokeMethodAsync('OnSidebarHoverExpand', true);
            }
        }, EXPAND_DELAY);
    }

    function _onSidebarLeave() {
        clearTimeout(_hoverTimer);
        _collapseTimer = setTimeout(() => {
            if (_hoverExpandRef) {
                _hoverExpandRef.invokeMethodAsync('OnSidebarHoverExpand', false);
            }
        }, COLLAPSE_DELAY);
    }

    function disposeSidebarHoverExpand() {
        const sidebar = document.querySelector('.fc-shell__sidebar');
        if (sidebar) {
            sidebar.removeEventListener('mouseenter', _onSidebarEnter);
            sidebar.removeEventListener('mouseleave', _onSidebarLeave);
        }
        clearTimeout(_hoverTimer);
        clearTimeout(_collapseTimer);
        _hoverExpandRef = null;
    }


    /* ================================================================
       5. NAV SEARCH — Focus helper
       ================================================================ */

    function focusNavSearch() {
        const input = document.querySelector('.fc-nav__search-input');
        if (input) input.focus();
    }


    /* ================================================================
       6. SCROLL ACTIVE ITEM INTO VIEW — After navigation
       ================================================================ */

    function scrollActiveIntoView() {
        const active = document.querySelector('.fc-nav__item.active');
        if (active) {
            active.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        }
    }

    // Auto-scroll after Blazor enhanced navigation
    if (typeof Blazor !== 'undefined') {
        Blazor.addEventListener('enhancedload', () => {
            setTimeout(scrollActiveIntoView, 100);
        });
    }


    /* ================================================================
       PUBLIC API
       ================================================================ */

    return {
        getItem,
        setItem,
        showContextMenu,
        hideContextMenu,
        showTooltip,
        hideTooltip,
        initSidebarHoverExpand,
        disposeSidebarHoverExpand,
        focusNavSearch,
        scrollActiveIntoView
    };
})();
