/**
 * fc-drag-drop.js
 * Drag-and-drop system with:
 * - Ghost element following cursor (reduced opacity)
 * - Visual placeholder showing drop position
 * - Smooth reflow animation
 * - Keyboard alternative (select → arrow keys → Enter to place)
 * - CSS cursor: grab / grabbing
 */
window.fcDragDrop = (() => {

    /* ─────────────────────────────────────────────────────────
       SORTABLE LIST
       Makes a container's children sortable by drag-and-drop.
    ───────────────────────────────────────────────────────── */

    class SortableList {
        constructor(container, dotNetRef, options = {}) {
            this.container = container;
            this.dotNetRef = dotNetRef;
            this.options = { itemSelector: '[data-drag-item]', handleSelector: '[data-drag-handle]', ...options };
            this.dragging = null;          // currently dragged element
            this.ghost = null;            // floating ghost
            this.placeholder = null;      // slot placeholder in list
            this.keyboardItem = null;     // item selected for keyboard move
            this.startIndex = -1;
            this.currentIndex = -1;
            this._onPointerDown = this._onPointerDown.bind(this);
            this._onPointerMove = this._onPointerMove.bind(this);
            this._onPointerUp   = this._onPointerUp.bind(this);
            this._onKeyDown     = this._onKeyDown.bind(this);
            container.addEventListener('pointerdown', this._onPointerDown);
            container.addEventListener('keydown', this._onKeyDown);
        }

        _getItems() {
            return Array.from(this.container.querySelectorAll(this.options.itemSelector));
        }

        _getHandle(item) {
            return item.querySelector(this.options.handleSelector) || item;
        }

        _closestItem(el) {
            return el.closest(this.options.itemSelector);
        }

        /* ── Pointer events ──────────────────────────────────── */

        _onPointerDown(e) {
            if (e.button !== 0) return;
            const handle = e.target.closest(this.options.handleSelector) || e.target.closest(this.options.itemSelector);
            if (!handle) return;
            const item = this._closestItem(handle);
            if (!item) return;

            e.preventDefault();

            const items = this._getItems();
            this.startIndex = items.indexOf(item);
            this.currentIndex = this.startIndex;
            this.dragging = item;

            // Create ghost
            const rect = item.getBoundingClientRect();
            this.ghost = item.cloneNode(true);
            this.ghost.classList.add('fc-dnd-ghost');
            this.ghost.style.cssText = `
                position: fixed;
                left: ${rect.left}px;
                top: ${rect.top}px;
                width: ${rect.width}px;
                height: ${rect.height}px;
                pointer-events: none;
                z-index: 9999;
                opacity: 0.6;
                transform: rotate(1.5deg) scale(1.02);
                transition: transform 0.1s ease;
                box-shadow: 0 8px 32px rgba(0,0,0,0.18);
            `;
            document.body.appendChild(this.ghost);

            this._offsetX = e.clientX - rect.left;
            this._offsetY = e.clientY - rect.top;

            // Create placeholder
            this.placeholder = document.createElement('div');
            this.placeholder.className = 'fc-dnd-placeholder';
            this.placeholder.style.height = rect.height + 'px';
            item.classList.add('fc-dnd-dragging');
            item.after(this.placeholder);

            document.addEventListener('pointermove', this._onPointerMove);
            document.addEventListener('pointerup', this._onPointerUp);
            document.body.style.cursor = 'grabbing';
            document.body.style.userSelect = 'none';
        }

        _onPointerMove(e) {
            if (!this.ghost) return;
            const x = e.clientX - this._offsetX;
            const y = e.clientY - this._offsetY;
            this.ghost.style.left = x + 'px';
            this.ghost.style.top  = y + 'px';

            // Find the item we're hovering over
            this.ghost.style.display = 'none';
            const el = document.elementFromPoint(e.clientX, e.clientY);
            this.ghost.style.display = '';

            if (!el) return;
            const target = this._closestItem(el);
            if (!target || target === this.dragging) return;
            if (!this.container.contains(target)) return;

            const items = this._getItems();
            const targetIndex = items.indexOf(target);
            this.currentIndex = targetIndex;

            // Move placeholder
            const rect = target.getBoundingClientRect();
            const mid = rect.top + rect.height / 2;
            if (e.clientY < mid) {
                target.before(this.placeholder);
            } else {
                target.after(this.placeholder);
            }
        }

        _onPointerUp() {
            if (!this.dragging) return;

            // Remove ghost
            this.ghost?.remove();
            this.ghost = null;

            // Move item to placeholder position
            this.placeholder.replaceWith(this.dragging);
            this.dragging.classList.remove('fc-dnd-dragging');

            // Notify .NET of new order
            const newOrder = this._getItems().map(el => el.dataset.dragId);
            const newIndex = newOrder.indexOf(this.dragging.dataset.dragId);

            if (this.startIndex !== newIndex) {
                this.dotNetRef.invokeMethodAsync('OnReorder', newOrder, newIndex, this.startIndex);
            }

            this.dragging = null;
            this.placeholder?.remove();
            this.placeholder = null;

            document.removeEventListener('pointermove', this._onPointerMove);
            document.removeEventListener('pointerup', this._onPointerUp);
            document.body.style.cursor = '';
            document.body.style.userSelect = '';
        }

        /* ── Keyboard nav ──────────────────────────────────── */

        _onKeyDown(e) {
            const item = e.target.closest(this.options.itemSelector);
            if (!item) return;

            // Space/Enter to select/deselect for keyboard move
            if (e.key === ' ' || e.key === 'Enter') {
                e.preventDefault();
                if (this.keyboardItem === item) {
                    // Confirm placement — nothing to do, already moved
                    item.classList.remove('fc-dnd-kb-selected');
                    this.keyboardItem = null;
                } else {
                    this.keyboardItem?.classList.remove('fc-dnd-kb-selected');
                    this.keyboardItem = item;
                    item.classList.add('fc-dnd-kb-selected');
                }
                return;
            }

            if (!this.keyboardItem) return;

            const items = this._getItems();
            const idx = items.indexOf(this.keyboardItem);

            if (e.key === 'ArrowUp' && idx > 0) {
                e.preventDefault();
                items[idx - 1].before(this.keyboardItem);
                this.keyboardItem.focus();
                this._notifyKeyboardReorder();
            } else if (e.key === 'ArrowDown' && idx < items.length - 1) {
                e.preventDefault();
                items[idx + 1].after(this.keyboardItem);
                this.keyboardItem.focus();
                this._notifyKeyboardReorder();
            } else if (e.key === 'Escape') {
                this.keyboardItem.classList.remove('fc-dnd-kb-selected');
                this.keyboardItem = null;
            }
        }

        _notifyKeyboardReorder() {
            const newOrder = this._getItems().map(el => el.dataset.dragId);
            const movedItem = this.keyboardItem;
            const newIndex  = newOrder.indexOf(movedItem?.dataset.dragId ?? '');
            this.dotNetRef.invokeMethodAsync('OnReorder', newOrder, newIndex, -1);
        }

        destroy() {
            this.container.removeEventListener('pointerdown', this._onPointerDown);
            this.container.removeEventListener('keydown', this._onKeyDown);
            this._onPointerUp();
        }
    }

    /* ─────────────────────────────────────────────────────────
       FILE DROP ZONE
       Handles drag-over and drop for file upload areas.
    ───────────────────────────────────────────────────────── */

    class FileDropZone {
        constructor(el, dotNetRef) {
            this.el = el;
            this.dotNetRef = dotNetRef;
            this._counter = 0;
            this._onDragEnter = this._onDragEnter.bind(this);
            this._onDragOver  = this._onDragOver.bind(this);
            this._onDragLeave = this._onDragLeave.bind(this);
            this._onDrop      = this._onDrop.bind(this);
            el.addEventListener('dragenter', this._onDragEnter);
            el.addEventListener('dragover',  this._onDragOver);
            el.addEventListener('dragleave', this._onDragLeave);
            el.addEventListener('drop',      this._onDrop);
        }

        _onDragEnter(e) {
            e.preventDefault();
            this._counter++;
            this.el.classList.add('fc-drop-active');
        }

        _onDragOver(e) {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'copy';
        }

        _onDragLeave(e) {
            this._counter--;
            if (this._counter === 0) {
                this.el.classList.remove('fc-drop-active');
            }
        }

        _onDrop(e) {
            e.preventDefault();
            this._counter = 0;
            this.el.classList.remove('fc-drop-active');
            // Notify Blazor – pass file info we can extract
            const files = Array.from(e.dataTransfer.files).map(f => ({
                name: f.name,
                size: f.size,
                type: f.type
            }));
            this.dotNetRef.invokeMethodAsync('OnFilesDropped', files);
        }

        destroy() {
            this.el.removeEventListener('dragenter', this._onDragEnter);
            this.el.removeEventListener('dragover',  this._onDragOver);
            this.el.removeEventListener('dragleave', this._onDragLeave);
            this.el.removeEventListener('drop',      this._onDrop);
        }
    }

    /* ─────────────────────────────────────────────────────────
       KANBAN BOARD
       Handles drag between columns (e.g., status columns).
    ───────────────────────────────────────────────────────── */

    class KanbanBoard {
        constructor(boardEl, dotNetRef) {
            this.board = boardEl;
            this.dotNetRef = dotNetRef;
            this.dragging = null;
            this.ghost = null;
            this._onPointerDown = this._onPointerDown.bind(this);
            this._onPointerMove = this._onPointerMove.bind(this);
            this._onPointerUp   = this._onPointerUp.bind(this);
            boardEl.addEventListener('pointerdown', this._onPointerDown);
        }

        _onPointerDown(e) {
            const card = e.target.closest('[data-kanban-card]');
            if (!card) return;
            e.preventDefault();

            this.dragging = card;
            const rect = card.getBoundingClientRect();

            this.ghost = card.cloneNode(true);
            this.ghost.classList.add('fc-dnd-ghost');
            this.ghost.style.cssText = `
                position: fixed;
                left: ${rect.left}px;
                top: ${rect.top}px;
                width: ${rect.width}px;
                pointer-events: none;
                z-index: 9999;
                opacity: 0.65;
                transform: rotate(2deg) scale(1.03);
                box-shadow: 0 12px 40px rgba(0,0,0,0.2);
            `;
            document.body.appendChild(this.ghost);

            this._offsetX = e.clientX - rect.left;
            this._offsetY = e.clientY - rect.top;
            card.classList.add('fc-dnd-dragging');

            // Add column highlight target class
            this.board.querySelectorAll('[data-kanban-col]').forEach(col => col.classList.add('fc-kanban-drop-target'));

            document.addEventListener('pointermove', this._onPointerMove);
            document.addEventListener('pointerup',   this._onPointerUp);
            document.body.style.cursor = 'grabbing';
        }

        _onPointerMove(e) {
            if (!this.ghost) return;
            this.ghost.style.left = (e.clientX - this._offsetX) + 'px';
            this.ghost.style.top  = (e.clientY - this._offsetY) + 'px';

            // Highlight active column
            this.ghost.style.display = 'none';
            const el = document.elementFromPoint(e.clientX, e.clientY);
            this.ghost.style.display = '';
            const col = el?.closest('[data-kanban-col]');
            this.board.querySelectorAll('[data-kanban-col]').forEach(c => c.classList.toggle('fc-kanban-col-over', c === col));
        }

        _onPointerUp(e) {
            if (!this.dragging) return;

            this.ghost?.remove();
            this.ghost = null;
            this.dragging.classList.remove('fc-dnd-dragging');
            this.board.querySelectorAll('[data-kanban-col]').forEach(c => {
                c.classList.remove('fc-kanban-col-over');
                c.classList.remove('fc-kanban-drop-target');
            });

            // Detect target column
            const ghost2 = document.elementFromPoint(e.clientX, e.clientY);
            const targetCol = ghost2?.closest('[data-kanban-col]');
            const targetStatus = targetCol?.dataset.kanbanCol;
            const cardId = this.dragging.dataset.kanbanCard;

            if (targetStatus && cardId) {
                this.dotNetRef.invokeMethodAsync('OnCardMoved', cardId, targetStatus);
            }

            this.dragging = null;
            document.removeEventListener('pointermove', this._onPointerMove);
            document.removeEventListener('pointerup',   this._onPointerUp);
            document.body.style.cursor = '';
        }

        destroy() {
            this.board.removeEventListener('pointerdown', this._onPointerDown);
            this._onPointerUp();
        }
    }

    /* ─────────────────────────────────────────────────────────
       PUBLIC API — called from Blazor components
    ───────────────────────────────────────────────────────── */

    const _sortables = new Map();
    const _dropZones = new Map();
    const _kanbans   = new Map();

    return {
        // Sortable list
        initSortable(containerId, dotNetRef, options) {
            const el = document.getElementById(containerId) || document.querySelector(`[data-sortable="${containerId}"]`);
            if (!el) return;
            _sortables.get(el)?.destroy();
            _sortables.set(el, new SortableList(el, dotNetRef, options || {}));
        },

        destroySortable(containerId) {
            const el = document.getElementById(containerId) || document.querySelector(`[data-sortable="${containerId}"]`);
            if (!el) return;
            _sortables.get(el)?.destroy();
            _sortables.delete(el);
        },

        // File drop zone
        initDropZone(el, dotNetRef) {
            if (!el) return;
            _dropZones.get(el)?.destroy();
            _dropZones.set(el, new FileDropZone(el, dotNetRef));
        },

        destroyDropZone(el) {
            _dropZones.get(el)?.destroy();
            _dropZones.delete(el);
        },

        // Kanban board
        initKanban(el, dotNetRef) {
            if (!el) return;
            _kanbans.get(el)?.destroy();
            _kanbans.set(el, new KanbanBoard(el, dotNetRef));
        },

        destroyKanban(el) {
            _kanbans.get(el)?.destroy();
            _kanbans.delete(el);
        }
    };

})();
