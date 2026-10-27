// Universal Popup Modal Window Manager (Draggable, Resizable, Maximizable, Persistent)
// Enables dragging via .modal-header, 8-directional border/corner resizing,
// double-click header to maximize/unmaximize, and per-modal geometry persistence in config.

(function () {
    const STORAGE_PREFIX = 'deskguard_modal_geom_';

    function getStorageKey(modalId) {
        return STORAGE_PREFIX + (modalId || 'default');
    }

    function loadSavedState(modalId) {
        try {
            const raw = localStorage.getItem(getStorageKey(modalId));
            if (raw) return JSON.parse(raw);
        } catch (e) { }
        return null;
    }

    function saveState(modalId, state) {
        try {
            localStorage.setItem(getStorageKey(modalId), JSON.stringify(state));
            if (window.securityApi && window.securityApi.saveModalConfig) {
                window.securityApi.saveModalConfig({ id: modalId, state: JSON.stringify(state) }).catch(() => { });
            }
        } catch (e) { }
    }

    function clamp(val, min, max) {
        return Math.max(min, Math.min(max, val));
    }

    function makeModalDraggableAndResizable(overlay) {
        if (!overlay || overlay.dataset.modalManagerAttached === 'true') return;
        overlay.dataset.modalManagerAttached = 'true';

        const modal = overlay.querySelector('.modal');
        if (!modal) return;

        const modalId = overlay.id || modal.id || 'unnamed-modal';
        const header = modal.querySelector('.modal-header');

        // 1. Add Maximize / Restore button in header before close button
        if (header && !header.querySelector('.modal-max-btn')) {
            const closeBtn = header.querySelector('.modal-close-btn') || header.querySelector('button[aria-label="Close"]') || header.querySelector('button:last-child');
            const maxBtn = document.createElement('button');
            maxBtn.type = 'button';
            maxBtn.className = 'modal-max-btn';
            maxBtn.title = 'Maximize / Restore (or double click titlebar)';
            maxBtn.setAttribute('aria-label', 'Maximize / Restore');
            maxBtn.innerHTML = '<svg width="10" height="10" viewBox="0 0 10 10" fill="none" stroke="currentColor" stroke-width="1.2"><rect x="0.5" y="0.5" width="9" height="9"/></svg>';
            maxBtn.onclick = (e) => {
                e.stopPropagation();
                toggleModalMaximize(overlay, modal, modalId);
            };

            if (closeBtn && closeBtn.parentNode === header) {
                header.insertBefore(maxBtn, closeBtn);
            } else {
                header.appendChild(maxBtn);
            }
        }

        // 2. Add 8 Resize Handles to the modal
        const handles = [
            { cls: 'handle-e', dir: 'e' },
            { cls: 'handle-s', dir: 's' },
            { cls: 'handle-w', dir: 'w' },
            { cls: 'handle-n', dir: 'n' },
            { cls: 'handle-se', dir: 'se' },
            { cls: 'handle-sw', dir: 'sw' },
            { cls: 'handle-ne', dir: 'ne' },
            { cls: 'handle-nw', dir: 'nw' }
        ];

        handles.forEach(({ cls, dir }) => {
            if (!modal.querySelector('.modal-resize-handle.' + cls)) {
                const h = document.createElement('div');
                h.className = 'modal-resize-handle ' + cls;
                h.dataset.dir = dir;
                modal.appendChild(h);
                setupResizeHandle(h, overlay, modal, modalId);
            }
        });

        // 3. Setup Draggable Header
        if (header) {
            header.style.cursor = 'grab';

            header.addEventListener('mousedown', (e) => {
                if (e.target.closest('button') || e.target.closest('input') || e.target.closest('select') || e.target.closest('a')) {
                    return;
                }
                if (e.button !== 0) return;

                if (modal.classList.contains('modal-maximized')) {
                    return; // Dragging while maximized is restricted
                }

                e.preventDefault();
                header.style.cursor = 'grabbing';

                // Ensure modal has absolute positioning
                const overlayRect = overlay.getBoundingClientRect();
                const modalRect = modal.getBoundingClientRect();

                modal.style.position = 'absolute';
                modal.style.margin = '0';
                modal.style.transform = 'none';

                const startLeft = modalRect.left - overlayRect.left;
                const startTop = modalRect.top - overlayRect.top;
                const startX = e.clientX;
                const startY = e.clientY;

                modal.style.left = startLeft + 'px';
                modal.style.top = startTop + 'px';
                modal.style.width = modalRect.width + 'px';
                modal.style.height = modalRect.height + 'px';

                function onMouseMove(ev) {
                    const dx = ev.clientX - startX;
                    const dy = ev.clientY - startY;

                    const maxLeft = Math.max(10, window.innerWidth - modal.offsetWidth - 10);
                    const maxTop = Math.max(10, window.innerHeight - modal.offsetHeight - 10);

                    const newLeft = clamp(startLeft + dx, 10, maxLeft);
                    const newTop = clamp(startTop + dy, 10, maxTop);

                    modal.style.left = newLeft + 'px';
                    modal.style.top = newTop + 'px';
                }

                function onMouseUp() {
                    header.style.cursor = 'grab';
                    window.removeEventListener('mousemove', onMouseMove);
                    window.removeEventListener('mouseup', onMouseUp);

                    saveCurrentModalGeometry(overlay, modal, modalId);
                }

                window.addEventListener('mousemove', onMouseMove);
                window.addEventListener('mouseup', onMouseUp);
            });

            // 4. Double click header to Maximize / Restore
            header.addEventListener('dblclick', (e) => {
                if (e.target.closest('button') || e.target.closest('input') || e.target.closest('select')) {
                    return;
                }
                toggleModalMaximize(overlay, modal, modalId);
            });
        }
    }

    function toggleModalMaximize(overlay, modal, modalId) {
        if (!modal) return;
        const isMax = modal.classList.contains('modal-maximized');

        if (!isMax) {
            // Store restore rect
            const overlayRect = overlay.getBoundingClientRect();
            const modalRect = modal.getBoundingClientRect();
            modal.dataset.restoreLeft = (modalRect.left - overlayRect.left) + 'px';
            modal.dataset.restoreTop = (modalRect.top - overlayRect.top) + 'px';
            modal.dataset.restoreWidth = modalRect.width + 'px';
            modal.dataset.restoreHeight = modalRect.height + 'px';

            modal.classList.add('modal-maximized');
            modal.style.position = 'absolute';
            modal.style.left = '10px';
            modal.style.top = '10px';
            modal.style.width = 'calc(100vw - 20px)';
            modal.style.height = 'calc(100vh - 52px)';
            modal.style.maxWidth = 'none';
            modal.style.maxHeight = 'none';
            modal.style.margin = '0';
            modal.style.transform = 'none';

            saveState(modalId, {
                isMaximized: true,
                restoreLeft: modal.dataset.restoreLeft,
                restoreTop: modal.dataset.restoreTop,
                restoreWidth: modal.dataset.restoreWidth,
                restoreHeight: modal.dataset.restoreHeight
            });
        } else {
            modal.classList.remove('modal-maximized');
            modal.style.left = modal.dataset.restoreLeft || '50px';
            modal.style.top = modal.dataset.restoreTop || '50px';
            modal.style.width = modal.dataset.restoreWidth || '750px';
            modal.style.height = modal.dataset.restoreHeight || '600px';

            saveCurrentModalGeometry(overlay, modal, modalId);
        }
    }

    function setupResizeHandle(handle, overlay, modal, modalId) {
        const dir = handle.dataset.dir;

        handle.addEventListener('mousedown', (e) => {
            if (e.button !== 0) return;
            if (modal.classList.contains('modal-maximized')) return;

            e.preventDefault();
            e.stopPropagation();

            const overlayRect = overlay.getBoundingClientRect();
            const modalRect = modal.getBoundingClientRect();

            modal.style.position = 'absolute';
            modal.style.margin = '0';
            modal.style.transform = 'none';

            const startLeft = modalRect.left - overlayRect.left;
            const startTop = modalRect.top - overlayRect.top;
            const startW = modalRect.width;
            const startH = modalRect.height;
            const startX = e.clientX;
            const startY = e.clientY;

            const minW = 340;
            const minH = 220;
            const maxW = window.innerWidth - 20;
            const maxH = window.innerHeight - 52;

            function onMouseMove(ev) {
                const dx = ev.clientX - startX;
                const dy = ev.clientY - startY;

                let newW = startW;
                let newH = startH;
                let newL = startLeft;
                let newT = startTop;

                if (dir.includes('e')) {
                    newW = clamp(startW + dx, minW, maxW - startLeft);
                }
                if (dir.includes('s')) {
                    newH = clamp(startH + dy, minH, maxH - startTop);
                }
                if (dir.includes('w')) {
                    const proposedW = startW - dx;
                    if (proposedW >= minW && startLeft + dx >= 10) {
                        newW = proposedW;
                        newL = startLeft + dx;
                    }
                }
                if (dir.includes('n')) {
                    const proposedH = startH - dy;
                    if (proposedH >= minH && startTop + dy >= 10) {
                        newH = proposedH;
                        newT = startTop + dy;
                    }
                }

                modal.style.width = newW + 'px';
                modal.style.height = newH + 'px';
                modal.style.left = newL + 'px';
                modal.style.top = newT + 'px';
            }

            function onMouseUp() {
                window.removeEventListener('mousemove', onMouseMove);
                window.removeEventListener('mouseup', onMouseUp);

                saveCurrentModalGeometry(overlay, modal, modalId);
            }

            window.addEventListener('mousemove', onMouseMove);
            window.addEventListener('mouseup', onMouseUp);
        });
    }

    function saveCurrentModalGeometry(overlay, modal, modalId) {
        if (!modal || modal.classList.contains('modal-maximized')) return;
        const overlayRect = overlay.getBoundingClientRect();
        const modalRect = modal.getBoundingClientRect();

        const state = {
            isMaximized: false,
            left: modalRect.left - overlayRect.left,
            top: modalRect.top - overlayRect.top,
            width: modalRect.width,
            height: modalRect.height
        };
        saveState(modalId, state);
    }

    function restoreModalGeometry(overlay) {
        if (!overlay) return;
        const modal = overlay.querySelector('.modal');
        if (!modal) return;

        const modalId = overlay.id || modal.id;
        const saved = loadSavedState(modalId);
        if (!saved) return;

        if (saved.isMaximized) {
            modal.dataset.restoreLeft = saved.restoreLeft || '50px';
            modal.dataset.restoreTop = saved.restoreTop || '50px';
            modal.dataset.restoreWidth = saved.restoreWidth || '750px';
            modal.dataset.restoreHeight = saved.restoreHeight || '600px';

            modal.classList.add('modal-maximized');
            modal.style.position = 'absolute';
            modal.style.left = '10px';
            modal.style.top = '10px';
            modal.style.width = 'calc(100vw - 20px)';
            modal.style.height = 'calc(100vh - 52px)';
            modal.style.maxWidth = 'none';
            modal.style.maxHeight = 'none';
            modal.style.margin = '0';
            modal.style.transform = 'none';
        } else if (saved.width && saved.height) {
            modal.classList.remove('modal-maximized');
            modal.style.position = 'absolute';
            modal.style.margin = '0';
            modal.style.transform = 'none';

            // Safe viewport clamping
            const maxW = window.innerWidth - 20;
            const maxH = window.innerHeight - 52;
            const w = clamp(saved.width, 340, maxW);
            const h = clamp(saved.height, 220, maxH);

            const maxLeft = Math.max(10, window.innerWidth - w - 10);
            const maxTop = Math.max(10, window.innerHeight - h - 10);
            const l = clamp(saved.left !== undefined ? saved.left : 50, 10, maxLeft);
            const t = clamp(saved.top !== undefined ? saved.top : 40, 10, maxTop);

            modal.style.width = w + 'px';
            modal.style.height = h + 'px';
            modal.style.left = l + 'px';
            modal.style.top = t + 'px';
        }
    }

    function initAllModals() {
        document.querySelectorAll('.modal-overlay').forEach(overlay => {
            makeModalDraggableAndResizable(overlay);
        });

        // Watch for dynamically opened modals to restore geometry
        const observer = new MutationObserver(mutations => {
            mutations.forEach(mut => {
                if (mut.type === 'attributes' && (mut.attributeName === 'class' || mut.attributeName === 'style')) {
                    const target = mut.target;
                    if (target.classList && target.classList.contains('modal-overlay')) {
                        if (target.classList.contains('active') || target.style.display === 'flex') {
                            makeModalDraggableAndResizable(target);
                            restoreModalGeometry(target);
                        }
                    }
                }
            });
        });

        observer.observe(document.body, { attributes: true, subtree: true, attributeFilter: ['class', 'style'] });
    }

    // Window Resize Strips for Native Host Resizing
    function initWindowResizeStrips() {
        if (document.getElementById('win-resize-container')) return;

        const cont = document.createElement('div');
        cont.id = 'win-resize-container';

        const strips = [
            { cls: 'win-resize-strip edge-t', edge: 3 },
            { cls: 'win-resize-strip edge-b', edge: 6 },
            { cls: 'win-resize-strip edge-l', edge: 1 },
            { cls: 'win-resize-strip edge-r', edge: 2 },
            { cls: 'win-resize-strip corner-tl', edge: 4 },
            { cls: 'win-resize-strip corner-tr', edge: 5 },
            { cls: 'win-resize-strip corner-bl', edge: 7 },
            { cls: 'win-resize-strip corner-br', edge: 8 }
        ];

        strips.forEach(({ cls, edge }) => {
            const el = document.createElement('div');
            el.className = cls;
            el.addEventListener('mousedown', (e) => {
                if (e.button === 0 && window.winResize) {
                    e.preventDefault();
                    window.winResize(edge);
                }
            });
            cont.appendChild(el);
        });

        document.body.appendChild(cont);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            initAllModals();
            initWindowResizeStrips();
        });
    } else {
        initAllModals();
        initWindowResizeStrips();
    }

    window.restoreModalGeometry = restoreModalGeometry;
})();
