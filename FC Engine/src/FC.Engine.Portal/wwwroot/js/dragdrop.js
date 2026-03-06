(function () {
    'use strict';

    const prefersReducedMotion = () =>
        window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // ── ARIA live region for drag announcements ────────────────────────
    let _announcer = null;
    function announce(msg) {
        if (!_announcer) {
            _announcer = document.createElement('div');
            _announcer.setAttribute('aria-live', 'assertive');
            _announcer.setAttribute('aria-atomic', 'true');
            _announcer.className = 'portal-sr-only';
            _announcer.style.cssText = 'position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;';
            document.body.appendChild(_announcer);
        }
        _announcer.textContent = '';
        requestAnimationFrame(() => { _announcer.textContent = msg; });
    }

    // ── Registry of active instances (for cleanup) ────────────────────
    const registry = {};

    // ══════════════════════════════════════════════════════════════════
    // FCDragDrop.sortable — reorder items within a container
    // ══════════════════════════════════════════════════════════════════
    function sortable(containerSelector, itemSelector, options) {
        const opts = Object.assign({
            storageKey: null,
            onReorder: null,   // function(fromIdx, toIdx, dotNetRef)
            dotNetRef: null,
            handle: null,      // selector for drag handle, null = whole item
            ghostClass: 'portal-sortable-ghost',
            placeholderClass: 'portal-sortable-placeholder',
            draggingClass: 'portal-sortable-dragging',
            direction: 'vertical'
        }, options || {});

        const containers = document.querySelectorAll(containerSelector);
        if (!containers.length) return;

        containers.forEach(function (container) {
            const id = containerSelector + '||' + (opts.storageKey || '');
            if (registry[id]) { registry[id].destroy(); }

            const items = () => Array.from(container.querySelectorAll(itemSelector));
            let dragSrc = null;
            let placeholder = null;
            let kbActive = null;    // item in keyboard pick-up mode

            // ── Drag handle elements ──
            function getHandle(item) {
                return opts.handle ? item.querySelector(opts.handle) : item;
            }

            // ── Setup ──
            function setup() {
                items().forEach(wireItem);
                container.addEventListener('dragover', onContainerDragOver);
                container.addEventListener('drop', onContainerDrop);
            }

            function wireItem(item) {
                item.setAttribute('draggable', 'true');
                item.setAttribute('aria-roledescription', 'sortable item');
                item.addEventListener('dragstart', onDragStart);
                item.addEventListener('dragend', onDragEnd);
                item.addEventListener('dragenter', onDragEnter);
                item.addEventListener('dragleave', onDragLeave);

                // Keyboard support
                const handle = getHandle(item);
                handle.setAttribute('tabindex', '0');
                handle.setAttribute('role', 'button');
                handle.setAttribute('aria-label', 'Drag to reorder');
                handle.addEventListener('keydown', onKeyDown.bind(null, item));
            }

            // ── Mouse / touch drag ──
            function onDragStart(e) {
                dragSrc = this;
                this.classList.add(opts.draggingClass);
                this.setAttribute('aria-grabbed', 'true');
                e.dataTransfer.effectAllowed = 'move';
                e.dataTransfer.setData('text/plain', '');
                if (prefersReducedMotion()) return;
                // show ghost (transparent clone follows cursor natively)
                this.style.opacity = '0.5';
                announce('Picked up. Use arrow keys or drag to reorder, then release to drop.');
            }

            function onDragEnd() {
                if (dragSrc) {
                    dragSrc.classList.remove(opts.draggingClass);
                    dragSrc.setAttribute('aria-grabbed', 'false');
                    dragSrc.style.opacity = '';
                }
                removePlaceholder();
                dragSrc = null;
            }

            function onDragEnter(e) {
                e.preventDefault();
                if (!dragSrc || dragSrc === this) return;
                insertPlaceholderBefore(this);
            }

            function onDragLeave() { /* placeholder stays until next enter */ }

            function onContainerDragOver(e) {
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
            }

            function onContainerDrop(e) {
                e.preventDefault();
                if (!dragSrc || !placeholder || !placeholder.parentNode) return;
                placeholder.parentNode.insertBefore(dragSrc, placeholder);
                removePlaceholder();
                persistAndNotify();
            }

            // ── Placeholder ──
            function insertPlaceholderBefore(target) {
                if (!placeholder) {
                    placeholder = document.createElement('div');
                    placeholder.className = opts.placeholderClass;
                    placeholder.setAttribute('aria-hidden', 'true');
                    // match height of dragged item
                    if (dragSrc) {
                        placeholder.style.height = dragSrc.offsetHeight + 'px';
                        placeholder.style.width = dragSrc.offsetWidth + 'px';
                    }
                }
                target.parentNode && target.parentNode.insertBefore(placeholder, target);
            }

            function removePlaceholder() {
                if (placeholder && placeholder.parentNode) {
                    placeholder.parentNode.removeChild(placeholder);
                }
                placeholder = null;
            }

            // ── Keyboard drag ──
            function onKeyDown(item, e) {
                const currentItems = items();
                const idx = currentItems.indexOf(item);

                if (e.key === ' ' || e.key === 'Enter') {
                    e.preventDefault();
                    if (kbActive === item) {
                        // Drop
                        item.classList.remove('portal-sortable-kbd-active');
                        item.setAttribute('aria-grabbed', 'false');
                        kbActive = null;
                        persistAndNotify();
                        announce('Dropped.');
                    } else {
                        // Pick up
                        if (kbActive) {
                            kbActive.classList.remove('portal-sortable-kbd-active');
                            kbActive.setAttribute('aria-grabbed', 'false');
                        }
                        kbActive = item;
                        item.classList.add('portal-sortable-kbd-active');
                        item.setAttribute('aria-grabbed', 'true');
                        announce('Picked up item ' + (idx + 1) + ' of ' + currentItems.length + '. Press arrow keys to move, Space or Enter to drop, Escape to cancel.');
                    }
                } else if (e.key === 'Escape') {
                    e.preventDefault();
                    if (kbActive) {
                        kbActive.classList.remove('portal-sortable-kbd-active');
                        kbActive.setAttribute('aria-grabbed', 'false');
                        kbActive = null;
                        announce('Cancelled.');
                    }
                } else if (kbActive === item) {
                    if ((e.key === 'ArrowDown' || e.key === 'ArrowRight') && idx < currentItems.length - 1) {
                        e.preventDefault();
                        swapItems(item, currentItems[idx + 1]);
                        item.querySelector('[tabindex="0"]')?.focus();
                        announce('Moved to position ' + (idx + 2) + ' of ' + currentItems.length);
                    } else if ((e.key === 'ArrowUp' || e.key === 'ArrowLeft') && idx > 0) {
                        e.preventDefault();
                        swapItems(currentItems[idx - 1], item);
                        item.querySelector('[tabindex="0"]')?.focus();
                        announce('Moved to position ' + idx + ' of ' + currentItems.length);
                    }
                }
            }

            function swapItems(before, after) {
                const parent = before.parentNode;
                const nextSibling = after.nextSibling;
                parent.insertBefore(after, before);
                if (nextSibling) {
                    parent.insertBefore(before, nextSibling);
                } else {
                    parent.appendChild(before);
                }
            }

            // ── Persist + notify ──
            function persistAndNotify() {
                const currentItems = items();
                const order = currentItems.map(function (el) {
                    return el.dataset.widgetId || el.dataset.itemId || el.id || '';
                });
                const fromIdx = dragSrc ? currentItems.indexOf(dragSrc) : -1;

                if (opts.storageKey) {
                    try { localStorage.setItem(opts.storageKey, JSON.stringify(order)); } catch (ex) { /* ignore */ }
                }
                if (opts.dotNetRef && fromIdx !== -1) {
                    const toIdx = order.indexOf(order[fromIdx]);
                    opts.dotNetRef.invokeMethodAsync('OnWidgetReordered', fromIdx, toIdx).catch(function () {});
                }
                if (typeof opts.onReorder === 'function') {
                    opts.onReorder(order);
                }
            }

            function destroy() {
                items().forEach(function (item) {
                    item.removeAttribute('draggable');
                    item.removeAttribute('aria-roledescription');
                    item.removeEventListener('dragstart', onDragStart);
                    item.removeEventListener('dragend', onDragEnd);
                    item.removeEventListener('dragenter', onDragEnter);
                    item.removeEventListener('dragleave', onDragLeave);
                    const handle = getHandle(item);
                    handle.removeAttribute('tabindex');
                    handle.removeAttribute('role');
                    handle.removeAttribute('aria-label');
                });
                container.removeEventListener('dragover', onContainerDragOver);
                container.removeEventListener('drop', onContainerDrop);
                removePlaceholder();
            }

            setup();
            registry[id] = { destroy };
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // FCDragDrop.kanban — within-column card reorder
    // ══════════════════════════════════════════════════════════════════
    function kanban(boardSelector, columnSelector, cardSelector, options) {
        const opts = Object.assign({
            storageKey: null,
            cardClass: 'portal-kanban-card',
            draggingClass: 'portal-kanban-card-dragging',
            placeholderClass: 'portal-sortable-placeholder'
        }, options || {});

        const board = document.querySelector(boardSelector);
        if (!board) return;

        const id = 'kanban||' + boardSelector;
        if (registry[id]) { registry[id].destroy(); }

        let dragSrc = null;
        let srcColumn = null;
        let placeholder = null;
        const cleanupFns = [];

        board.querySelectorAll(columnSelector).forEach(function (col) {
            const onDragStart = function (e) {
                dragSrc = this;
                srcColumn = col;
                this.classList.add(opts.draggingClass);
                this.setAttribute('aria-grabbed', 'true');
                this.style.opacity = '0.5';
                e.dataTransfer.effectAllowed = 'move';
                e.dataTransfer.setData('text/plain', '');
                announce('Picked up card. Drag within this column to reorder.');
            };
            const onDragEnd = function () {
                if (dragSrc) {
                    dragSrc.classList.remove(opts.draggingClass);
                    dragSrc.setAttribute('aria-grabbed', 'false');
                    dragSrc.style.opacity = '';
                }
                removePlaceholder();
                dragSrc = null;
                srcColumn = null;
            };
            const onCardDragEnter = function (e) {
                e.preventDefault();
                if (!dragSrc || dragSrc === this || srcColumn !== col) return;
                insertPlaceholderBefore(this);
            };
            const onColDragOver = function (e) {
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
            };
            const onColDrop = function (e) {
                e.preventDefault();
                if (!dragSrc || srcColumn !== col) return;
                if (placeholder && placeholder.parentNode) {
                    placeholder.parentNode.insertBefore(dragSrc, placeholder);
                }
                removePlaceholder();
                if (opts.storageKey) {
                    const colId = col.dataset.columnId || col.id || '';
                    const cards = Array.from(col.querySelectorAll(cardSelector));
                    const order = cards.map(function (c) { return c.dataset.cardId || c.id || ''; });
                    try { localStorage.setItem(opts.storageKey + '-' + colId, JSON.stringify(order)); } catch (ex) {}
                }
            };

            col.querySelectorAll(cardSelector).forEach(function (card) {
                card.setAttribute('draggable', 'true');
                card.setAttribute('aria-roledescription', 'kanban card');
                card.addEventListener('dragstart', onDragStart);
                card.addEventListener('dragend', onDragEnd);
                card.addEventListener('dragenter', onCardDragEnter);
                cleanupFns.push(function () {
                    card.removeAttribute('draggable');
                    card.removeEventListener('dragstart', onDragStart);
                    card.removeEventListener('dragend', onDragEnd);
                    card.removeEventListener('dragenter', onCardDragEnter);
                });
            });

            col.addEventListener('dragover', onColDragOver);
            col.addEventListener('drop', onColDrop);
            cleanupFns.push(function () {
                col.removeEventListener('dragover', onColDragOver);
                col.removeEventListener('drop', onColDrop);
            });
        });

        function insertPlaceholderBefore(target) {
            if (!placeholder) {
                placeholder = document.createElement('div');
                placeholder.className = opts.placeholderClass;
                placeholder.setAttribute('aria-hidden', 'true');
                if (dragSrc) { placeholder.style.height = dragSrc.offsetHeight + 'px'; }
            }
            target.parentNode && target.parentNode.insertBefore(placeholder, target);
        }

        function removePlaceholder() {
            if (placeholder && placeholder.parentNode) {
                placeholder.parentNode.removeChild(placeholder);
            }
            placeholder = null;
        }

        function destroy() {
            cleanupFns.forEach(function (fn) { fn(); });
            removePlaceholder();
        }

        registry[id] = { destroy };
    }

    // ══════════════════════════════════════════════════════════════════
    // FCDragDrop.columnReorder — drag <th> to reorder table columns
    // ══════════════════════════════════════════════════════════════════
    function columnReorder(tableSelector, options) {
        const opts = Object.assign({
            storageKey: null,
            draggingClass: 'portal-th-dragging',
            dropIndicatorClass: 'portal-th-drop-indicator'
        }, options || {});

        const table = document.querySelector(tableSelector);
        if (!table) return;

        const id = 'colreorder||' + tableSelector;
        if (registry[id]) { registry[id].destroy(); }

        const headers = Array.from(table.querySelectorAll('thead tr th'));
        if (!headers.length) return;

        let dragIdx = null;
        let dropIdx = null;
        const cleanupFns = [];

        headers.forEach(function (th, idx) {
            th.setAttribute('draggable', 'true');
            th.classList.add('portal-th-draggable');
            th.setAttribute('aria-roledescription', 'sortable column header');

            const onDragStart = function (e) {
                dragIdx = idx;
                th.classList.add(opts.draggingClass);
                e.dataTransfer.effectAllowed = 'move';
                e.dataTransfer.setData('text/plain', String(idx));
                announce('Column ' + th.textContent.trim() + ' picked up. Drag to reorder.');
            };
            const onDragEnd = function () {
                th.classList.remove(opts.draggingClass);
                headers.forEach(function (h) { h.classList.remove(opts.dropIndicatorClass); });
                dragIdx = null;
                dropIdx = null;
            };
            const onDragEnter = function (e) {
                e.preventDefault();
                if (dragIdx === null || dragIdx === idx) return;
                headers.forEach(function (h) { h.classList.remove(opts.dropIndicatorClass); });
                th.classList.add(opts.dropIndicatorClass);
                dropIdx = idx;
            };
            const onDragOver = function (e) {
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
            };
            const onDrop = function (e) {
                e.preventDefault();
                if (dragIdx === null || dragIdx === idx) return;
                reorderColumns(dragIdx, idx);
                headers.forEach(function (h) { h.classList.remove(opts.dropIndicatorClass); });
            };

            th.addEventListener('dragstart', onDragStart);
            th.addEventListener('dragend', onDragEnd);
            th.addEventListener('dragenter', onDragEnter);
            th.addEventListener('dragover', onDragOver);
            th.addEventListener('drop', onDrop);

            cleanupFns.push(function () {
                th.removeAttribute('draggable');
                th.classList.remove('portal-th-draggable');
                th.removeEventListener('dragstart', onDragStart);
                th.removeEventListener('dragend', onDragEnd);
                th.removeEventListener('dragenter', onDragEnter);
                th.removeEventListener('dragover', onDragOver);
                th.removeEventListener('drop', onDrop);
            });
        });

        function reorderColumns(from, to) {
            // Move TH
            const thead = table.querySelector('thead tr');
            const ths = Array.from(thead.querySelectorAll('th'));
            const movedTh = ths.splice(from, 1)[0];
            ths.splice(to, 0, movedTh);
            // Re-append in new order
            ths.forEach(function (th) { thead.appendChild(th); });

            // Move TDs in each body row
            table.querySelectorAll('tbody tr').forEach(function (row) {
                const tds = Array.from(row.querySelectorAll('td'));
                if (tds.length < ths.length) return;
                const movedTd = tds.splice(from, 1)[0];
                tds.splice(to, 0, movedTd);
                tds.forEach(function (td) { row.appendChild(td); });
            });

            // Persist
            if (opts.storageKey) {
                const order = Array.from(table.querySelectorAll('thead tr th'))
                    .map(function (th) { return th.dataset.colId || th.textContent.trim(); });
                try { localStorage.setItem(opts.storageKey, JSON.stringify(order)); } catch (ex) {}
            }
            announce('Column moved to position ' + (to + 1));
        }

        registry[id] = {
            destroy: function () { cleanupFns.forEach(function (fn) { fn(); }); }
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // FCDragDrop.dropZone — enhanced file drop zone
    // ══════════════════════════════════════════════════════════════════
    function dropZone(selector, options) {
        const opts = Object.assign({
            accept: [],          // accepted MIME types, empty = all
            onDrop: null,        // function(files)
            activeClass: 'portal-drop-zone-active',
            pulseClass: 'portal-bulk-dropzone-pulse',
            invalidClass: 'portal-drop-zone-invalid'
        }, options || {});

        const zones = document.querySelectorAll(selector);
        if (!zones.length) return;

        zones.forEach(function (zone) {
            let dragEnterCount = 0;
            const id = 'dropzone||' + selector;

            function isValid(files) {
                if (!opts.accept.length) return true;
                return Array.from(files).every(function (f) {
                    return opts.accept.some(function (t) {
                        return t === f.type || (t.endsWith('/*') && f.type.startsWith(t.slice(0, -1)));
                    });
                });
            }

            function onDragEnter(e) {
                e.preventDefault();
                dragEnterCount++;
                if (dragEnterCount === 1) {
                    const files = e.dataTransfer && e.dataTransfer.items
                        ? Array.from(e.dataTransfer.items).filter(function (i) { return i.kind === 'file'; })
                        : [];
                    const valid = !files.length || !opts.accept.length ||
                        files.every(function (i) {
                            return opts.accept.some(function (t) {
                                return i.type === t || (t.endsWith('/*') && i.type.startsWith(t.slice(0, -1)));
                            });
                        });
                    zone.classList.remove(opts.invalidClass);
                    if (valid) {
                        zone.classList.add(opts.activeClass);
                        if (!prefersReducedMotion()) zone.classList.add(opts.pulseClass);
                    } else {
                        zone.classList.add(opts.invalidClass);
                    }
                    // Show count indicator
                    const counter = zone.querySelector('.portal-dropzone-count');
                    if (counter && files.length) {
                        counter.textContent = files.length + ' file' + (files.length !== 1 ? 's' : '');
                        counter.hidden = false;
                    }
                }
            }

            function onDragLeave(e) {
                dragEnterCount--;
                if (dragEnterCount <= 0) {
                    dragEnterCount = 0;
                    zone.classList.remove(opts.activeClass, opts.pulseClass, opts.invalidClass);
                    const counter = zone.querySelector('.portal-dropzone-count');
                    if (counter) counter.hidden = true;
                }
            }

            function onDragOver(e) { e.preventDefault(); }

            function onDrop(e) {
                e.preventDefault();
                dragEnterCount = 0;
                zone.classList.remove(opts.activeClass, opts.pulseClass, opts.invalidClass);
                const counter = zone.querySelector('.portal-dropzone-count');
                if (counter) counter.hidden = true;
                if (e.dataTransfer && e.dataTransfer.files.length && typeof opts.onDrop === 'function') {
                    opts.onDrop(e.dataTransfer.files);
                }
            }

            zone.addEventListener('dragenter', onDragEnter);
            zone.addEventListener('dragleave', onDragLeave);
            zone.addEventListener('dragover', onDragOver);
            zone.addEventListener('drop', onDrop);

            if (registry[id]) { registry[id].destroy(); }
            registry[id] = {
                destroy: function () {
                    zone.removeEventListener('dragenter', onDragEnter);
                    zone.removeEventListener('dragleave', onDragLeave);
                    zone.removeEventListener('dragover', onDragOver);
                    zone.removeEventListener('drop', onDrop);
                    zone.classList.remove(opts.activeClass, opts.pulseClass, opts.invalidClass);
                }
            };
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // FCDragDrop.getSavedOrder — read persisted order from localStorage
    // ══════════════════════════════════════════════════════════════════
    function getSavedOrder(storageKey) {
        try { return localStorage.getItem(storageKey); } catch (ex) { return null; }
    }

    // ══════════════════════════════════════════════════════════════════
    // FCDragDrop.destroy — tear down all listeners for a container
    // ══════════════════════════════════════════════════════════════════
    function destroy(containerId) {
        Object.keys(registry).forEach(function (key) {
            if (key.includes(containerId)) {
                registry[key].destroy();
                delete registry[key];
            }
        });
    }

    // ── Backwards-compat wrapper for existing initDragDrop ────────────
    // (charts.js has its own initDragDrop — keep that working)
    window.FCDragDrop = { sortable, kanban, columnReorder, dropZone, getSavedOrder, destroy };

})();
