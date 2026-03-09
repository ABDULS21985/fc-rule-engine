// RegOS™ Portal — Board Pack Generator JS interop
(function () {
    'use strict';

    const prefersReducedMotion = () =>
        window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    let _dotNetRef = null;
    let _sortableDestroy = null;

    // ── Section drag-to-reorder ──────────────────────────────────────
    function initSortable(dotNetRef) {
        _dotNetRef = dotNetRef;
        destroySortable();

        const container = document.getElementById('bp-section-list');
        if (!container) return;

        const items = () => Array.from(container.querySelectorAll('.bp-section-item'));
        let dragSrc = null;
        let placeholder = null;

        function setup() {
            items().forEach(wireItem);
            container.addEventListener('dragover', onOver);
            container.addEventListener('drop', onDrop);
        }

        function wireItem(item) {
            item.setAttribute('draggable', 'true');
            const handle = item.querySelector('.bp-section-handle');
            if (handle) {
                handle.addEventListener('mousedown', function () { item.setAttribute('draggable', 'true'); });
            }
            item.addEventListener('dragstart', onStart);
            item.addEventListener('dragend', onEnd);
            item.addEventListener('dragenter', onEnter);
        }

        function onStart(e) {
            dragSrc = this;
            this.classList.add('bp-section-dragging');
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', '');
            if (!prefersReducedMotion()) this.style.opacity = '0.4';
        }

        function onEnd() {
            if (dragSrc) {
                dragSrc.classList.remove('bp-section-dragging');
                dragSrc.style.opacity = '';
            }
            removePlaceholder();
            dragSrc = null;
        }

        function onEnter(e) {
            e.preventDefault();
            if (!dragSrc || dragSrc === this) return;
            insertPlaceholder(this);
        }

        function onOver(e) {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
        }

        function onDrop(e) {
            e.preventDefault();
            if (!dragSrc) return;

            if (placeholder && placeholder.parentNode) {
                placeholder.parentNode.insertBefore(dragSrc, placeholder);
            }
            removePlaceholder();

            // Collect new order
            const order = items().map(el => el.dataset.sectionId);
            if (_dotNetRef) {
                _dotNetRef.invokeMethodAsync('OnSectionsReordered', order).catch(() => {});
            }
            dragSrc = null;
        }

        function insertPlaceholder(before) {
            if (!placeholder) {
                placeholder = document.createElement('div');
                placeholder.className = 'bp-section-placeholder';
                placeholder.setAttribute('aria-hidden', 'true');
                if (dragSrc) placeholder.style.height = dragSrc.offsetHeight + 'px';
            }
            before.parentNode && before.parentNode.insertBefore(placeholder, before);
        }

        function removePlaceholder() {
            if (placeholder && placeholder.parentNode) placeholder.parentNode.removeChild(placeholder);
            placeholder = null;
        }

        setup();
        _sortableDestroy = function () {
            items().forEach(item => {
                item.removeAttribute('draggable');
                item.removeEventListener('dragstart', onStart);
                item.removeEventListener('dragend', onEnd);
                item.removeEventListener('dragenter', onEnter);
            });
            container.removeEventListener('dragover', onOver);
            container.removeEventListener('drop', onDrop);
            removePlaceholder();
        };
    }

    function destroySortable() {
        if (_sortableDestroy) { _sortableDestroy(); _sortableDestroy = null; }
    }

    // ── Progress animation ───────────────────────────────────────────
    function animateProgress(stageIndex, totalStages) {
        const bar = document.getElementById('bp-progress-fill');
        const stageEls = document.querySelectorAll('.bp-progress-stage');
        if (!bar) return;

        const pct = Math.round(((stageIndex + 1) / totalStages) * 100);
        bar.style.width = pct + '%';

        stageEls.forEach(function (el, i) {
            el.classList.remove('bp-progress-stage--active', 'bp-progress-stage--done');
            if (i < stageIndex) el.classList.add('bp-progress-stage--done');
            else if (i === stageIndex) el.classList.add('bp-progress-stage--active');
        });
    }

    function resetProgress() {
        const bar = document.getElementById('bp-progress-fill');
        if (bar) bar.style.width = '0%';
        document.querySelectorAll('.bp-progress-stage').forEach(el => {
            el.classList.remove('bp-progress-stage--active', 'bp-progress-stage--done');
        });
    }

    // ── Saved configs in localStorage ────────────────────────────────
    function saveConfig(key, json) {
        try { localStorage.setItem('bp_config_' + key, json); } catch (e) {}
    }

    function loadConfig(key) {
        try { return localStorage.getItem('bp_config_' + key) || null; } catch (e) { return null; }
    }

    function listConfigKeys() {
        var keys = [];
        try {
            for (var i = 0; i < localStorage.length; i++) {
                var k = localStorage.key(i);
                if (k && k.startsWith('bp_config_')) {
                    keys.push(k.substring(10));
                }
            }
        } catch (e) {}
        return keys;
    }

    function deleteConfig(key) {
        try { localStorage.removeItem('bp_config_' + key); } catch (e) {}
    }

    // ── Public API ───────────────────────────────────────────────────
    window.fcBoardPack = {
        initSortable: initSortable,
        destroySortable: destroySortable,
        animateProgress: animateProgress,
        resetProgress: resetProgress,
        saveConfig: saveConfig,
        loadConfig: loadConfig,
        listConfigKeys: listConfigKeys,
        deleteConfig: deleteConfig
    };

})();
