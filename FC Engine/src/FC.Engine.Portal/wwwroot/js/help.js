/**
 * FC Portal — Intelligent Help System
 * Fuzzy search, contextual help drawer, guide progress, helpfulness ratings, interactive examples
 */
window.fcHelp = (function () {
    'use strict';

    /* ─── Fuzzy search index ─── */
    let _index = [];
    let _dotNetRef = null;

    function buildIndex(articles) {
        _index = (articles || []).map(function (a, i) {
            var text = (a.title + ' ' + a.description + ' ' + (a.keywords || '')).toLowerCase();
            return { id: i, title: a.title, description: a.description, url: a.url, category: a.category, readTime: a.readTime || '2 min read', text: text };
        });
    }

    function fuzzyMatch(query, text) {
        query = query.toLowerCase();
        var words = query.split(/\s+/).filter(Boolean);
        var score = 0;
        for (var i = 0; i < words.length; i++) {
            var idx = text.indexOf(words[i]);
            if (idx === -1) return -1;
            score += (idx === 0 ? 10 : 1);
        }
        // Bonus for exact substring match
        if (text.indexOf(query) !== -1) score += 20;
        return score;
    }

    function search(query) {
        if (!query || query.length < 2) return [];
        var q = query.toLowerCase();
        var results = [];
        for (var i = 0; i < _index.length; i++) {
            var s = fuzzyMatch(q, _index[i].text);
            if (s > 0) results.push({ score: s, item: _index[i] });
        }
        results.sort(function (a, b) { return b.score - a.score; });
        return results.slice(0, 8).map(function (r) { return r.item; });
    }

    /* ─── Contextual Help Drawer ─── */
    let _drawerOpen = false;

    function openDrawer() {
        var el = document.getElementById('fc-help-drawer');
        if (!el) return;
        _drawerOpen = true;
        el.classList.add('fc-hd--open');
        el.setAttribute('aria-hidden', 'false');
        document.body.style.overflow = 'hidden';
        // Focus the search input inside
        var input = el.querySelector('.fc-hd-search-input');
        if (input) setTimeout(function () { input.focus(); }, 150);
    }

    function closeDrawer() {
        var el = document.getElementById('fc-help-drawer');
        if (!el) return;
        _drawerOpen = false;
        el.classList.remove('fc-hd--open');
        el.setAttribute('aria-hidden', 'true');
        document.body.style.overflow = '';
    }

    function toggleDrawer() {
        _drawerOpen ? closeDrawer() : openDrawer();
    }

    /* ─── Guide Progress ─── */
    function getGuideProgress(guideId) {
        try {
            var key = 'fc_guide_progress_' + guideId;
            var data = localStorage.getItem(key);
            return data ? JSON.parse(data) : {};
        } catch { return {}; }
    }

    function toggleStepComplete(guideId, stepIndex) {
        var key = 'fc_guide_progress_' + guideId;
        var progress = getGuideProgress(guideId);
        if (progress[stepIndex]) {
            delete progress[stepIndex];
        } else {
            progress[stepIndex] = true;
        }
        localStorage.setItem(key, JSON.stringify(progress));
        return progress;
    }

    function getCompletedSteps(guideId) {
        var progress = getGuideProgress(guideId);
        return Object.keys(progress).filter(function (k) { return progress[k]; }).map(Number);
    }

    /* ─── Helpfulness Rating ─── */
    function rateArticle(articleId, helpful) {
        try {
            var key = 'fc_help_ratings';
            var ratings = JSON.parse(localStorage.getItem(key) || '{}');
            ratings[articleId] = helpful;
            localStorage.setItem(key, JSON.stringify(ratings));
        } catch { }
    }

    function getArticleRating(articleId) {
        try {
            var ratings = JSON.parse(localStorage.getItem('fc_help_ratings') || '{}');
            return ratings[articleId] !== undefined ? ratings[articleId] : null;
        } catch { return null; }
    }

    /* ─── Video Embed ─── */
    function playVideo(containerId) {
        var container = document.getElementById(containerId);
        if (!container) return;
        var iframe = container.querySelector('iframe');
        var poster = container.querySelector('.fc-hv-poster');
        if (iframe && poster) {
            poster.style.display = 'none';
            iframe.style.display = 'block';
            // Add autoplay to src
            var src = iframe.getAttribute('data-src') || iframe.src;
            if (src && src.indexOf('autoplay') === -1) {
                src += (src.indexOf('?') === -1 ? '?' : '&') + 'autoplay=1';
            }
            iframe.src = src;
        }
    }

    /* ─── Interactive Examples ─── */
    function showInteractiveExample(exampleId) {
        var el = document.getElementById(exampleId);
        if (!el) return;
        el.classList.add('fc-hx--visible');
        el.setAttribute('aria-hidden', 'false');
        // Highlight the error cell
        var errorCell = el.querySelector('.fc-hx-error-cell');
        if (errorCell) {
            errorCell.classList.add('fc-hx-error-cell--flash');
            setTimeout(function () { errorCell.classList.remove('fc-hx-error-cell--flash'); }, 2000);
        }
    }

    function hideInteractiveExample(exampleId) {
        var el = document.getElementById(exampleId);
        if (!el) return;
        el.classList.remove('fc-hx--visible');
        el.setAttribute('aria-hidden', 'true');
    }

    /* ─── Support Diagnostics ─── */
    function collectDiagnostics() {
        var diag = {
            currentUrl: window.location.href,
            userAgent: navigator.userAgent,
            screenSize: window.innerWidth + 'x' + window.innerHeight,
            pixelRatio: window.devicePixelRatio || 1,
            timestamp: new Date().toISOString(),
            theme: document.documentElement.getAttribute('data-theme') || 'light',
            online: navigator.onLine
        };
        // Try to get last error from session
        try {
            var lastErr = sessionStorage.getItem('fc_last_error');
            if (lastErr) diag.lastError = lastErr;
        } catch { }
        return diag;
    }

    function storeLastError(message) {
        try { sessionStorage.setItem('fc_last_error', message); } catch { }
    }

    /* ─── Keyboard shortcut ─── */
    function initHelpShortcut() {
        document.addEventListener('keydown', function (e) {
            // F1 opens help drawer (prevent default browser help)
            if (e.key === 'F1' && !e.ctrlKey && !e.metaKey && !e.altKey) {
                var el = document.getElementById('fc-help-drawer');
                if (el) {
                    e.preventDefault();
                    toggleDrawer();
                }
            }
            // Escape closes drawer
            if (e.key === 'Escape' && _drawerOpen) {
                closeDrawer();
            }
        });
    }

    // Init on load
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initHelpShortcut);
    } else {
        initHelpShortcut();
    }

    return {
        buildIndex: buildIndex,
        search: search,
        openDrawer: openDrawer,
        closeDrawer: closeDrawer,
        toggleDrawer: toggleDrawer,
        getGuideProgress: getGuideProgress,
        toggleStepComplete: toggleStepComplete,
        getCompletedSteps: getCompletedSteps,
        rateArticle: rateArticle,
        getArticleRating: getArticleRating,
        playVideo: playVideo,
        showInteractiveExample: showInteractiveExample,
        hideInteractiveExample: hideInteractiveExample,
        collectDiagnostics: collectDiagnostics,
        storeLastError: storeLastError
    };
})();
