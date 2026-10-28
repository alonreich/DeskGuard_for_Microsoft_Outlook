window.escapeHtml = window.escapeHtml || function(str) {
    if (str === null || str === undefined) return '';
    if (typeof str !== 'string') str = String(str);
    return str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
};
window.escapeHTML = window.escapeHtml;

class VirtualList {
    constructor(options) {
        this.container = options.container;
        this.rowHeight = options.rowHeight || 44;
        this.buffer = options.buffer !== undefined ? options.buffer : 5;
        this.createRow = options.createRow;
        this.updateRow = options.updateRow;
        this.emptyRenderer = options.emptyRenderer;
        
        this.items = [];
        this.nodePool = [];
        this.activeNodes = new Map();
        this.rafPending = false;
        this.scrollTop = 0;
        
        this.initDOM();
        this.initScrollListener();
    }

    initDOM() {
        this.container.innerHTML = '';
        this.container.style.position = 'relative';
        this.container.style.overflowY = 'auto';
        
        this.spacer = document.createElement('div');
        this.spacer.className = 'virtual-spacer';
        this.spacer.style.width = '1px';
        this.spacer.style.pointerEvents = 'none';
        this.spacer.style.opacity = '0';
        this.spacer.style.position = 'absolute';
        this.spacer.style.top = '0';
        this.spacer.style.left = '0';
        
        this.contentLayer = document.createElement('div');
        this.contentLayer.className = 'virtual-content';
        this.contentLayer.style.position = 'absolute';
        this.contentLayer.style.top = '0';
        this.contentLayer.style.left = '0';
        this.contentLayer.style.width = '100%';
        this.contentLayer.style.pointerEvents = 'auto';
        this.contentLayer.style.willChange = 'transform';
        
        this.container.appendChild(this.spacer);
        this.container.appendChild(this.contentLayer);
    }

    initScrollListener() {
        this.onScroll = () => {
            this.scrollTop = this.container.scrollTop;
            if (!this.rafPending) {
                this.rafPending = true;
                requestAnimationFrame(() => {
                    this.rafPending = false;
                    this.renderSlice();
                });
            }
        };
        this.container.addEventListener('scroll', this.onScroll, { passive: true });
    }

    setItems(items, resetScroll = false) {
        this.items = items || [];
        if (resetScroll) {
            this.container.scrollTop = 0;
            this.scrollTop = 0;
        } else {
            this.scrollTop = this.container.scrollTop;
        }

        const totalHeight = this.items.length * this.rowHeight;
        this.spacer.style.height = `${totalHeight}px`;

        if (this.items.length === 0) {
            for (const [idx, node] of this.activeNodes.entries()) {
                node.style.display = 'none';
                this.nodePool.push(node);
            }
            this.activeNodes.clear();
            if (this.emptyRenderer) {
                this.contentLayer.innerHTML = this.emptyRenderer();
            } else {
                this.contentLayer.innerHTML = '';
            }
            return;
        }

        if (this.contentLayer.querySelector('.empty-state')) {
            this.contentLayer.innerHTML = '';
            this.nodePool = [];
            this.activeNodes.clear();
        }

        this.renderSlice();
    }

    renderSlice() {
        if (this.items.length === 0) return;

        const viewportHeight = this.container.clientHeight || 500;
        const scrollTop = this.scrollTop;
        const total = this.items.length;

        const startIndex = Math.max(0, Math.floor(scrollTop / this.rowHeight) - this.buffer);
        const endIndex = Math.min(total, Math.ceil((scrollTop + viewportHeight) / this.rowHeight) + this.buffer);

        const newActiveNodes = new Map();
        const neededIndexes = [];

        for (let i = startIndex; i < endIndex; i++) {
            if (this.activeNodes.has(i)) {
                newActiveNodes.set(i, this.activeNodes.get(i));
            } else {
                neededIndexes.push(i);
            }
        }

        for (const [idx, node] of this.activeNodes.entries()) {
            if (idx < startIndex || idx >= endIndex) {
                node.style.display = 'none';
                this.nodePool.push(node);
            }
        }

        for (const i of neededIndexes) {
            let node;
            if (this.nodePool.length > 0) {
                node = this.nodePool.pop();
                node.style.display = '';
            } else {
                node = this.createRow();
                this.contentLayer.appendChild(node);
            }
            newActiveNodes.set(i, node);
        }

        this.activeNodes = newActiveNodes;

        for (const [i, node] of this.activeNodes.entries()) {
            const y = i * this.rowHeight;
            node.style.transform = `translate3d(0, ${y}px, 0)`;
            this.updateRow(node, this.items[i], i);
        }
    }

    scrollToIndex(index) {
        if (index < 0 || index >= this.items.length) return;
        const targetTop = index * this.rowHeight;
        const viewportHeight = this.container.clientHeight || 500;
        const currentScroll = this.container.scrollTop;

        if (targetTop < currentScroll) {
            this.container.scrollTop = targetTop;
        } else if (targetTop + this.rowHeight > currentScroll + viewportHeight) {
            this.container.scrollTop = targetTop + this.rowHeight - viewportHeight;
        }
    }

    refreshCurrentSlice() {
        for (const [i, node] of this.activeNodes.entries()) {
            if (this.items[i]) {
                this.updateRow(node, this.items[i], i);
            }
        }
    }

    destroy() {
        if (this.onScroll) {
            this.container.removeEventListener('scroll', this.onScroll);
        }
        this.activeNodes.clear();
        this.nodePool = [];
    }
}
window.VirtualList = VirtualList;

window.incidentVirtualList = null;
window.currentRenderedCategory = null;

function loadSavedDashColumnWidths() {
    try {
        const raw = localStorage.getItem('deskguard_dash_col_widths');
        if (raw) {
            const widths = JSON.parse(raw);
            if (widths && typeof widths === 'object') {
                for (const [k, v] of Object.entries(widths)) {
                    if (v) document.documentElement.style.setProperty(`--dash-col-${k}`, v);
                }
            }
        }
    } catch (e) {}
}

function saveDashColumnWidth(colKey, widthPx) {
    try {
        let widths = {};
        const raw = localStorage.getItem('deskguard_dash_col_widths');
        if (raw) widths = JSON.parse(raw) || {};
        widths[colKey] = `${Math.max(45, widthPx)}px`;
        localStorage.setItem('deskguard_dash_col_widths', JSON.stringify(widths));
    } catch (e) {}
}

function initDashColumnResizers() {
    const headerEl = document.getElementById('list-header');
    if (!headerEl || headerEl._resizersInitialized) return;
    headerEl._resizersInitialized = true;
    const resizers = headerEl.querySelectorAll('.col-resizer');
    resizers.forEach(resizer => {
        resizer.addEventListener('mousedown', (e) => {
            e.preventDefault();
            e.stopPropagation();
            const colKey = resizer.getAttribute('data-col');
            const colHeader = resizer.parentElement;
            const startX = e.pageX;
            const startWidth = colHeader.offsetWidth;

            resizer.classList.add('resizing');
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';

            function onMouseMove(moveEvent) {
                const diff = moveEvent.pageX - startX;
                const newWidth = Math.max(50, startWidth + diff);
                document.documentElement.style.setProperty(`--dash-col-${colKey}`, `${newWidth}px`);
            }

            function onMouseUp(upEvent) {
                const diff = upEvent.pageX - startX;
                const finalWidth = Math.max(50, startWidth + diff);
                document.documentElement.style.setProperty(`--dash-col-${colKey}`, `${finalWidth}px`);
                saveDashColumnWidth(colKey, finalWidth);
                resizer.classList.remove('resizing');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
                window.removeEventListener('mousemove', onMouseMove);
                window.removeEventListener('mouseup', onMouseUp);
            }

            window.addEventListener('mousemove', onMouseMove);
            window.addEventListener('mouseup', onMouseUp);
        });
    });
}

loadSavedDashColumnWidths();

window.openIncidentInOutlook = async (item) => {
    if (!item) return;
    try {
        if (typeof window.showNotification === 'function') {
            window.showNotification("Opening email in Microsoft Outlook...");
        }
        const res = await window.securityApi.openEmail({
            entryId: item.entryId || item.id,
            storeId: item.storeId || '',
            fingerprint: item.fingerprint || '',
            messageId: item.messageId || ''
        });
        if (res && (!res.success && !res.ok)) {
            if (typeof window.showNotification === 'function') {
                window.showNotification("Could not open in Outlook: " + ((res && res.error) || "Email not found in mailbox."), true);
            }
        }
    } catch (err) {
        if (typeof window.showNotification === 'function') {
            window.showNotification("Error launching Outlook: " + err.message, true);
        }
    }
};

window.openForensicsTab = (item) => {
    if (!item) return;
    if (typeof window.switchTab === 'function') {
        window.switchTab('forensics');
    }
    if (typeof window.renderForensicsTab === 'function') {
        window.renderForensicsTab(item);
    } else if (typeof window.renderForensics === 'function') {
        window.renderForensics(item);
    }
};

function createIncidentRowElement() {
    const div = document.createElement('div');
    div.className = 'list-item incident-grid virtual-row';

    const dateEl = document.createElement('div');
    dateEl.style.fontFamily = 'monospace';
    dateEl.style.fontSize = '0.7rem';
    dateEl.style.color = 'var(--muted)';
    dateEl.style.overflow = 'hidden';
    dateEl.style.textOverflow = 'ellipsis';
    dateEl.style.whiteSpace = 'nowrap';
    dateEl.style.textAlign = 'center';

    const timeEl = document.createElement('div');
    timeEl.style.fontFamily = 'monospace';
    timeEl.style.fontSize = '0.7rem';
    timeEl.style.color = 'var(--muted)';
    timeEl.style.overflow = 'hidden';
    timeEl.style.textOverflow = 'ellipsis';
    timeEl.style.whiteSpace = 'nowrap';
    timeEl.style.textAlign = 'center';

    const fromEl = document.createElement('div');
    fromEl.style.overflow = 'hidden';
    fromEl.style.textOverflow = 'ellipsis';
    fromEl.style.whiteSpace = 'nowrap';
    fromEl.style.fontSize = '0.75rem';
    fromEl.style.color = 'var(--text)';
    fromEl.style.textAlign = 'center';

    const toEl = document.createElement('div');
    toEl.style.overflow = 'hidden';
    toEl.style.textOverflow = 'ellipsis';
    toEl.style.whiteSpace = 'nowrap';
    toEl.style.fontSize = '0.75rem';
    toEl.style.color = 'var(--muted)';
    toEl.style.textAlign = 'center';

    const subjEl = document.createElement('div');
    subjEl.style.fontWeight = '600';
    subjEl.style.color = 'var(--text)';
    subjEl.style.overflow = 'hidden';
    subjEl.style.textOverflow = 'ellipsis';
    subjEl.style.whiteSpace = 'nowrap';
    subjEl.style.textAlign = 'center';

    const badgeCol = document.createElement('div');
    badgeCol.style.display = 'flex';
    badgeCol.style.alignItems = 'center';
    badgeCol.style.justifyContent = 'center';
    badgeCol.style.gap = '4px';
    badgeCol.style.overflow = 'hidden';

    const badgeSpan = document.createElement('span');
    badgeSpan.className = 'badge';
    badgeCol.appendChild(badgeSpan);

    const movedBadgeSpan = document.createElement('span');
    movedBadgeSpan.className = 'badge badge-user-moved';
    movedBadgeSpan.style.display = 'none';
    badgeCol.appendChild(movedBadgeSpan);

    const scoreEl = document.createElement('div');
    scoreEl.style.fontFamily = 'monospace';
    scoreEl.style.fontSize = '0.75rem';
    scoreEl.style.fontWeight = '700';
    scoreEl.style.textAlign = 'center';

    // 8th cell: 5 Row Action Buttons in requested order:
    // 1. Open in Outlook, 2. Forensic Report, 3. AV Scan (Defender/ClamAV), 4. delete item, 5. Quarantine
    const actionsEl = document.createElement('div');
    actionsEl.className = 'row-actions-cell';
    actionsEl.style.justifyContent = 'center';

    const btnOutlook = document.createElement('button');
    btnOutlook.type = 'button';
    btnOutlook.className = 'row-action-btn';
    btnOutlook.title = 'Open in Outlook';
    btnOutlook.innerHTML = `<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><polyline points="15 3 21 3 21 9"></polyline><line x1="10" y1="14" x2="21" y2="3"></line></svg> Outlook`;

    const btnForensic = document.createElement('button');
    btnForensic.type = 'button';
    btnForensic.className = 'row-action-btn';
    btnForensic.title = 'View Full Forensic Report';
    btnForensic.innerHTML = `<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line></svg> Forensic`;

    const btnVt = document.createElement('button');
    btnVt.type = 'button';
    btnVt.className = 'row-action-btn';
    btnVt.title = 'Multi-Engine AV Threat Scan (Microsoft Defender & ClamAV)';
    btnVt.innerHTML = `<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path></svg> AV Scan`;

    const btnDel = document.createElement('button');
    btnDel.type = 'button';
    btnDel.className = 'row-action-btn btn-delete';
    btnDel.title = 'Delete Email from Outlook';
    btnDel.innerHTML = `<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg> Delete`;

    const btnQuar = document.createElement('button');
    btnQuar.type = 'button';
    btnQuar.className = 'row-action-btn btn-quarantine';
    btnQuar.title = 'Move Email to Quarantine';
    btnQuar.innerHTML = `<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg> Quarantine`;

    actionsEl.appendChild(btnOutlook);
    actionsEl.appendChild(btnForensic);
    actionsEl.appendChild(btnVt);
    actionsEl.appendChild(btnDel);
    actionsEl.appendChild(btnQuar);

    div.appendChild(dateEl);
    div.appendChild(timeEl);
    div.appendChild(fromEl);
    div.appendChild(toEl);
    div.appendChild(subjEl);
    div.appendChild(badgeCol);
    div.appendChild(scoreEl);
    div.appendChild(actionsEl);

    div._cells = {
        dateEl, timeEl, fromEl, toEl, subjEl, badgeSpan, movedBadgeSpan, scoreEl,
        btnOutlook, btnForensic, btnVt, btnDel, btnQuar
    };
    return div;
}

function updateIncidentRowElement(div, item, index) {
    const cells = div._cells;
    
    let dateStr = item.date || '';
    let timeStr = item.time || '';
    if (!dateStr || !timeStr) {
        if (item.timestamp) {
            const d = new Date(item.timestamp);
            if (!isNaN(d.getTime())) {
                if (!dateStr) dateStr = d.toISOString().slice(0, 10);
                if (!timeStr) timeStr = d.toTimeString().slice(0, 8);
            }
        }
    }
    dateStr = dateStr || '----/--/--';
    timeStr = timeStr || '--:--:--';

    if (cells.dateEl.textContent !== dateStr) cells.dateEl.textContent = dateStr;
    if (cells.timeEl.textContent !== timeStr) cells.timeEl.textContent = timeStr;

    const fromText = item.sender || item.from || 'Unknown';
    let trustBadgeHtml = '';
    const vLower = (item.verdict || '').toLowerCase();
    const scoreVal = typeof item.score === 'number' ? item.score : 100;
    const isWhitelisted = window.AppState && window.AppState.config && Array.isArray(window.AppState.config.whitelist) &&
        window.AppState.config.whitelist.some(w => fromText.toLowerCase().includes(w.toLowerCase()));

    if (vLower.includes('malicious') || scoreVal < 30) {
        trustBadgeHtml = '<span class="trust-badge-threat" title="High Threat Sender">🚫 Threat</span>';
    } else if (isWhitelisted || scoreVal >= 90) {
        trustBadgeHtml = '<span class="trust-badge-org" title="Trusted Sender / Known Organization">⭐ Trusted Org</span>';
    } else {
        trustBadgeHtml = '<span class="trust-badge-ext" title="External Domain / First Contact">⚠️ External</span>';
    }

    const currentFromKey = `${trustBadgeHtml}:${fromText}`;
    if (cells.fromEl._lastKey !== currentFromKey) {
        cells.fromEl._lastKey = currentFromKey;
        cells.fromEl.innerHTML = `${trustBadgeHtml}<span>${window.escapeHtml ? window.escapeHtml(fromText) : fromText}</span>`;
        cells.fromEl.title = fromText;
    }

    const toText = item.to || item.recipient || item.recips || '';
    const displayTo = toText || '—';
    if (cells.toEl.textContent !== displayTo) {
        cells.toEl.textContent = displayTo;
        cells.toEl.title = toText;
    }

    const subjText = item.subject || item.details || '(No Subject)';
    if (cells.subjEl.textContent !== subjText) {
        cells.subjEl.textContent = subjText;
        cells.subjEl.title = subjText;
    }

    const verdict = item.verdict || 'Pending';
    const vLower = verdict.toLowerCase();
    const verdictClass = vLower.includes('malicious') ? 'badge-malicious' : (vLower.includes('spam') ? 'badge-spam' : 'badge-safe');

    if (cells.badgeSpan.textContent !== verdict) cells.badgeSpan.textContent = verdict;
    const targetBadgeClass = `badge ${verdictClass}`;
    if (cells.badgeSpan.className !== targetBadgeClass) cells.badgeSpan.className = targetBadgeClass;

    if (item.userMoved) {
        cells.movedBadgeSpan.style.display = 'inline-flex';
        cells.movedBadgeSpan.innerHTML = `Moved by User <span style="font-weight:900;margin-left:3px;font-size:0.75rem;">?</span>`;
        const folderName = item.currentFolder || 'another folder';
        const explStory = item.userMovedStory || `You manually moved this email to folder "${folderName}" inside Outlook. DeskGuard respects your choice and will never automatically move or quarantine it again.`;
        cells.movedBadgeSpan.title = explStory;
        cells.movedBadgeSpan.onclick = (e) => {
            e.stopPropagation();
            if (window.showConfirmModal) {
                window.showConfirmModal(
                    'EMAIL MOVED BY USER IN OUTLOOK',
                    `This email was originally flagged or quarantined by DeskGuard, but you manually moved it to folder "${folderName}" in Microsoft Outlook.\n\nDeskGuard respects your manual override. It will NEVER automatically move or quarantine this email again.`,
                    () => {}
                );
            } else {
                if (typeof window.showNotification === "function") window.showNotification(explStory); else alert(explStory);
            }
        };
    } else {
        cells.movedBadgeSpan.style.display = 'none';
        cells.movedBadgeSpan.onclick = null;
    }

    const numScore = parseInt(item.score);
    const displayScore = isNaN(numScore) ? '--' : `${numScore}%`;
    if (cells.scoreEl.textContent !== displayScore) {
        cells.scoreEl.textContent = displayScore;
        cells.scoreEl.style.color = (numScore < 50) ? 'var(--danger)' : (numScore < 80 ? 'var(--warn)' : 'var(--ok)');
    }

    const isSelected = window.AppState.selectedId === (item.entryId || item.fingerprint || item.id);
    if (isSelected) {
        if (!div.classList.contains('active')) div.classList.add('active');
    } else {
        if (div.classList.contains('active')) div.classList.remove('active');
    }

    // Attach row button actions
    cells.btnOutlook.onclick = (e) => {
        e.stopPropagation();
        window.openIncidentInOutlook(item);
    };
    cells.btnForensic.onclick = (e) => {
        e.stopPropagation();
        window.openForensicsTab(item);
    };
    cells.btnVt.onclick = (e) => {
        e.stopPropagation();
        window.scanEmailAv(item, cells.btnVt);
    };
    cells.btnDel.onclick = (e) => {
        e.stopPropagation();
        window.deleteIncident(item.entryId || item.id);
    };
    cells.btnQuar.onclick = (e) => {
        e.stopPropagation();
        window.quarantineIncident(item.entryId || item.id, item.fingerprint, item.storeId);
    };

    div.ondblclick = () => window.openIncidentInOutlook(item);
    div.onclick = () => window.selectIncident(item);
}

window.renderList = (items, category, forceResetScroll = false) => {
    initDashColumnResizers();
    const container = document.getElementById('security-list');
    if (!container) return;

    document.querySelectorAll('.stat-card').forEach(c => c.classList.remove('active'));
    const activeCard = document.getElementById(`card-${category}`);
    if (activeCard) activeCard.classList.add('active');

    if (!window.incidentVirtualList) {
        window.incidentVirtualList = new VirtualList({
            container: container,
            rowHeight: 44,
            buffer: 5,
            createRow: createIncidentRowElement,
            updateRow: updateIncidentRowElement,
            emptyRenderer: () => `
                <div class="empty-state">
                    <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--border)" stroke-width="1.5"><circle cx="12" cy="12" r="10"></circle><path d="M12 8v4l3 3"></path></svg>
                    <h3>Clean Environment</h3>
                    <p>No incidents detected in this category.</p>
                </div>`
        });
    }

    const categoryChanged = (window.currentRenderedCategory !== category);
    window.currentRenderedCategory = category;
    const shouldReset = forceResetScroll || categoryChanged;

    const sorted = [...(items || [])];
    const { key, asc } = window.sortOrder;
    sorted.sort((a, b) => {
        if (key === 'date' || key === 'timestamp') {
            const timeA = new Date(a.date ? `${a.date} ${a.time || '00:00:00'}` : (a.timestamp || 0)).getTime() || 0;
            const timeB = new Date(b.date ? `${b.date} ${b.time || '00:00:00'}` : (b.timestamp || 0)).getTime() || 0;
            return asc ? timeA - timeB : timeB - timeA;
        } else if (key === 'time') {
            const tA = String(a.time || '');
            const tB = String(b.time || '');
            return asc ? tA.localeCompare(tB) : tB.localeCompare(tA);
        } else if (key === 'score') {
            const sA = parseInt(a.score) || 0;
            const sB = parseInt(b.score) || 0;
            return asc ? sA - sB : sB - sA;
        } else if (key === 'from') {
            const fA = String(a.sender || a.from || '').toLowerCase();
            const fB = String(b.sender || b.from || '').toLowerCase();
            return asc ? fA.localeCompare(fB) : fB.localeCompare(fA);
        } else if (key === 'to') {
            const toA = String(a.to || a.recipient || a.recips || '').toLowerCase();
            const toB = String(b.to || b.recipient || b.recips || '').toLowerCase();
            return asc ? toA.localeCompare(toB) : toB.localeCompare(toA);
        } else if (key === 'subject') {
            const subA = String(a.subject || a.details || '').toLowerCase();
            const subB = String(b.subject || b.details || '').toLowerCase();
            return asc ? subA.localeCompare(subB) : subB.localeCompare(subA);
        } else if (key === 'verdict') {
            const vA = String(a.verdict || '').toLowerCase();
            const vB = String(b.verdict || '').toLowerCase();
            return asc ? vA.localeCompare(vB) : vB.localeCompare(vA);
        } else {
            const valA = String(a[key] || '').toLowerCase();
            const valB = String(b[key] || '').toLowerCase();
            return asc ? valA.localeCompare(valB) : valB.localeCompare(valA);
        }
    });

    window.incidentVirtualList.setItems(sorted, shouldReset);
};

window.selectIncident = async (item) => {
    window.AppState.selectedId = item.entryId || item.fingerprint || item.id;
    if (window.incidentVirtualList) {
        window.incidentVirtualList.refreshCurrentSlice();
    }
    
    const panel = document.getElementById('detail-panel');
    if (!panel) return;

    const verdict = item.verdict || 'Pending';
    const isQuarantined = verdict.toLowerCase().includes('malicious') || verdict.toLowerCase().includes('spam') || verdict.toLowerCase().includes('quarantin');
    const isSafe = verdict.toLowerCase().includes('safe');
    
    const safeSubject = window.escapeHtml(item.subject || item.details || '(No Subject)');
    const safeSender = window.escapeHtml(item.sender || item.from || 'Unknown');
    const safeRecipient = window.escapeHtml(item.to || item.recipient || item.recips || 'N/A');
    const reasons = typeof window.explainTier === 'function' ? window.explainTier(item.tier) : [];
    const safeTier = reasons.length
        ? reasons.map(r => `<div>${window.escapeHtml(r)}</div>`).join('')
        : window.escapeHtml(item.tier && !/^Clean \(/.test(item.tier) ? item.tier : 'Passed every active check');
    const safeIp = (!item.ip || item.ip === 'N/A') ? 'Internal Network' : window.escapeHtml(item.ip);
    const safeVerdict = window.escapeHtml(verdict);
    const verdictClass = verdict.toLowerCase().includes('malicious') ? 'badge-malicious' : (verdict.toLowerCase().includes('spam') ? 'badge-spam' : 'badge-safe');

    let actionButtons = `
        <button class="primary" id="btn-open-outlook" title="Open this email directly in native Microsoft Outlook inspector">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><polyline points="15 3 21 3 21 9"></polyline><line x1="10" y1="14" x2="21" y2="3"></line></svg>
            Open in Outlook
        </button>
        <button id="btn-open-forensics">Full Forensics</button>
        <button id="btn-vt-scan">VirusTotal Audit</button>
    `;

    if (isQuarantined || window.AppState.currentCategory === 'malicious' || window.AppState.currentCategory === 'spam' || window.AppState.currentCategory === 'suspicious') {
        actionButtons += `<button class="success" id="btn-release">Release to Inbox</button>`;
        actionButtons += `<button class="success" id="btn-release-trust" title="Move back to Inbox and always trust this sender">Release + Trust Sender</button>`;
    }
    if (isSafe || window.AppState.currentCategory === 'safe' || window.AppState.currentCategory === 'suspicious') {
        actionButtons += `<button class="warning" id="btn-quarantine">Quarantine</button>`;
    }
    actionButtons += `<button class="danger" id="btn-delete">Delete Item</button>`;

    let displayTime = '';
    if (item.date && item.time) {
        displayTime = `${item.date} ${item.time}`;
    } else if (item.timestamp) {
        displayTime = new Date(item.timestamp).toLocaleString();
    } else {
        displayTime = 'N/A';
    }

    const userMovedBanner = item.userMoved ? `
        <div style="background: rgba(235, 0, 41, 0.12); border: 1px solid var(--danger); border-radius: 6px; padding: 10px 14px; margin-bottom: 15px; display: flex; align-items: flex-start; gap: 10px;">
            <span style="font-size: 1.1rem; color: var(--danger); line-height: 1;">⚠️</span>
            <div>
                <div style="font-weight: 700; font-size: 0.82rem; color: var(--danger); text-transform: uppercase;">Manually Relocated by User in Outlook</div>
                <div style="font-size: 0.78rem; color: var(--text); margin-top: 2px; line-height: 1.4;">
                    ${window.escapeHtml(item.userMovedStory || `You manually moved this email to folder "${item.currentFolder || 'another folder'}" inside Microsoft Outlook. DeskGuard respects your manual override and will never automatically move or quarantine it again.`)}
                </div>
            </div>
        </div>
    ` : '';

    panel.innerHTML = `
        <div class="detail-header">
            <h2 style="margin: 0; font-size: 1.1rem; font-weight: 500;">${safeSubject}</h2>
            <div style="margin-top: 8px; display: flex; align-items: center; gap: 6px;">
                <span class="badge ${verdictClass}">${safeVerdict}</span>
                ${item.userMoved ? `<span class="badge badge-user-moved" title="Relocated by user to ${window.escapeHtml(item.currentFolder || 'another folder')}">Moved by User ?</span>` : ''}
            </div>
        </div>
        <div class="detail-content">
            ${userMovedBanner}
            <div class="detail-meta">
                <label>Sender</label><div style="word-break: break-all;">${safeSender}</div>
                <label>Recipient</label><div style="word-break: break-all;">${safeRecipient}</div>
                <label>Received</label><div>${window.escapeHtml(displayTime)}</div>
                <label>IP Origin</label><div style="font-family: monospace; color: ${!item.ip || item.ip === 'N/A' ? 'var(--muted)' : 'var(--accent)'}">${safeIp}</div>
                <label>Score</label><div style="font-weight: 700; color: ${item.score < 50 ? 'var(--danger)' : 'var(--ok)'}">${parseInt(item.score) || 0}% Integrity</div>
                <label>Why</label><div style="font-size: 0.75rem; color: var(--muted);">${safeTier}</div>
            </div>
            <div style="margin-bottom: 12px; display: flex; flex-wrap: wrap; gap: 8px;">
                ${actionButtons}
            </div>
            <div class="detail-body" id="detail-body-content">Loading message content...</div>
        </div>
    `;

    const btnOpenOutlook = document.getElementById('btn-open-outlook');
    if (btnOpenOutlook) {
        btnOpenOutlook.onclick = async () => {
            btnOpenOutlook.disabled = true;
            const origContent = btnOpenOutlook.innerHTML;
            btnOpenOutlook.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spin"><path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg> Launching...`;
            try {
                const res = await window.securityApi.openEmail({
                    entryId: item.entryId || item.id,
                    storeId: item.storeId || '',
                    fingerprint: item.fingerprint || '',
                    messageId: item.messageId || ''
                });
                if (res && !res.success) {
                    if (typeof window.showNotification === "function") window.showNotification("Could not open in Outlook: " + ((res && res.error) || "Item not found in store or Outlook COM unavailable."), true); else alert("Could not open in Outlook: " + ((res && res.error) || "Item not found in store or Outlook COM unavailable."));
                }
            } catch (err) {
                if (typeof window.showNotification === "function") window.showNotification("Error launching email in Outlook: " + err.message, true); else alert("Error launching email in Outlook: " + err.message);
            } finally {
                btnOpenOutlook.disabled = false;
                btnOpenOutlook.innerHTML = origContent;
            }
        };
    }

    const btnForensics = document.getElementById('btn-open-forensics');
    if (btnForensics) btnForensics.onclick = () => window.openForensicsModal(item.entryId || item.fingerprint || item.id);
    const btnVt = document.getElementById('btn-vt-scan');
    if (btnVt) btnVt.onclick = () => window.cloudScanIncident(item.entryId || item.id || '');
    const btnRel = document.getElementById('btn-release');
    if (btnRel) btnRel.onclick = () => window.releaseIncident(item.entryId || item.id || '', item.fingerprint || '');
    const btnRelTrust = document.getElementById('btn-release-trust');
    if (btnRelTrust) btnRelTrust.onclick = () => window.releaseIncident(item.entryId || item.id || '', item.fingerprint || '', item.sender || item.from || '');
    const btnQuar = document.getElementById('btn-quarantine');
    if (btnQuar) btnQuar.onclick = () => window.quarantineIncident(item.entryId || item.id || '', item.fingerprint || '');
    const btnDel = document.getElementById('btn-delete');
    if (btnDel) btnDel.onclick = () => window.deleteIncident(item.entryId || item.id || '');

    const bodyEl = document.getElementById('detail-body-content');
    let displayBody = '';
    if (item.body && item.body !== 'N/A') {
        displayBody = item.body;
    }
    if (displayBody && typeof displayBody === 'string') {
        const clean = displayBody.trim();
        if (/^[A-Za-z0-9+/]+={0,2}$/.test(clean) && clean.length % 4 === 0 && !clean.includes(' ')) {
            try {
                const dec = atob(clean);
                if (dec && dec.length > 0) displayBody = dec;
            } catch {}
        }
    }

    if (!displayBody || displayBody === 'N/A' || displayBody === 'Unavailable') {
        try {
            const forensics = await window.securityApi.getForensics({
                entryId: item.entryId || item.id,
                originalEntryId: item.originalEntryId,
                fingerprint: item.fingerprint
            });
            if (forensics && forensics.body && forensics.body !== 'N/A' && forensics.body !== 'Unavailable') {
                displayBody = forensics.body;
            }
        } catch {}
    }

    if (bodyEl) {
        bodyEl.textContent = displayBody || "(No message body content)";
    }
};

window.handleListKeyNavigation = (e) => {
    // Ignore keystrokes inside input / textarea / select or contentEditable elements
    const activeTag = document.activeElement ? document.activeElement.tagName.toUpperCase() : '';
    if (activeTag === 'INPUT' || activeTag === 'TEXTAREA' || activeTag === 'SELECT' || (document.activeElement && document.activeElement.isContentEditable)) {
        return;
    }

    // Toggle cheat sheet with '?'
    if (e.key === '?') {
        e.preventDefault();
        window.openKeyboardShortcutsHelp();
        return;
    }

    const cat = window.AppState.currentCategory;
    const items = (window.incidentVirtualList && window.incidentVirtualList.items) || window.AppState.stats[cat] || [];
    if (items.length === 0) return;

    const currentIndex = items.findIndex(i => (i.entryId || i.fingerprint) === window.AppState.selectedId);
    const selectedItem = currentIndex !== -1 ? items[currentIndex] : items[0];

    // Navigation: J or ArrowDown
    if (e.key === 'ArrowDown' || e.key === 'j' || e.key === 'J') {
        e.preventDefault();
        const nextIndex = currentIndex === -1 ? 0 : Math.min(items.length - 1, currentIndex + 1);
        if (items[nextIndex]) {
            window.selectIncident(items[nextIndex]);
            if (window.incidentVirtualList) window.incidentVirtualList.scrollToIndex(nextIndex);
        }
        return;
    }

    // Navigation: K or ArrowUp
    if (e.key === 'ArrowUp' || e.key === 'k' || e.key === 'K') {
        e.preventDefault();
        const prevIndex = currentIndex === -1 ? 0 : Math.max(0, currentIndex - 1);
        if (items[prevIndex]) {
            window.selectIncident(items[prevIndex]);
            if (window.incidentVirtualList) window.incidentVirtualList.scrollToIndex(prevIndex);
        }
        return;
    }

    if (!selectedItem) return;

    // Action: Open in Outlook (Enter or O)
    if (e.key === 'Enter' || e.key === 'o' || e.key === 'O') {
        e.preventDefault();
        if (typeof window.openItemInOutlook === 'function') {
            window.openItemInOutlook(selectedItem.entryId, selectedItem.storeId, selectedItem.fingerprint, selectedItem.messageId);
        }
        return;
    }

    // Action: Forensic Report (F)
    if (e.key === 'f' || e.key === 'F') {
        e.preventDefault();
        if (typeof window.openForensics === 'function') {
            window.openForensics(selectedItem);
        }
        return;
    }

    // Action: AV Scan (S)
    if (e.key === 's' || e.key === 'S') {
        e.preventDefault();
        if (typeof window.scanEmailAv === 'function') {
            window.scanEmailAv(selectedItem.entryId, selectedItem.fingerprint);
        }
        return;
    }

    // Action: Quarantine (Q)
    if (e.key === 'q' || e.key === 'Q') {
        e.preventDefault();
        if (typeof window.quarantineIncident === 'function') {
            window.quarantineIncident(selectedItem.entryId, selectedItem.fingerprint);
        }
        return;
    }

    // Action: Delete (D)
    if (e.key === 'd' || e.key === 'D') {
        e.preventDefault();
        if (typeof window.deleteIncident === 'function') {
            window.deleteIncident(selectedItem.entryId, selectedItem.fingerprint);
        }
        return;
    }
};

window.openKeyboardShortcutsHelp = function() {
    if (typeof window.openModal === 'function') {
        window.openModal('keyboard-shortcuts-modal');
    }
};

window.activeFilterChips = new Set();
window.toggleFilterChip = function(syntaxToken) {
    const searchInput = document.getElementById('vault-search-input');
    if (!searchInput) return;

    let queryToken = syntaxToken;
    if (syntaxToken === 'recent:7d') {
        const d = new Date();
        d.setDate(d.getDate() - 7);
        const yyyy = d.getFullYear();
        const mm = String(d.getMonth() + 1).padStart(2, '0');
        const dd = String(d.getDate()).padStart(2, '0');
        queryToken = `after:${yyyy}-${mm}-${dd}`;
    }

    let val = searchInput.value.trim();
    const tokenRegex = new RegExp(`(^|\\s)${queryToken.replace(/[-[\]{}()*+?.,\\^$|#\s]/g, '\\$&')}(\\s|$)`, 'i');

    if (tokenRegex.test(val)) {
        val = val.replace(tokenRegex, ' ').trim();
        window.activeFilterChips.delete(syntaxToken);
    } else {
        val = val ? `${val} ${queryToken}` : queryToken;
        window.activeFilterChips.add(syntaxToken);
    }

    searchInput.value = val;
    window.syncFilterChipStyles();
    if (typeof window.executeVaultSearch === 'function') {
        window.executeVaultSearch();
    }
};

window.syncFilterChipStyles = function() {
    const searchInput = document.getElementById('vault-search-input');
    const val = (searchInput ? searchInput.value : '').toLowerCase();

    const chipMap = {
        'chip-attach': /has:attach/i,
        'chip-unread': /is:unread/i,
        'chip-malicious': /is:malicious/i,
        'chip-spam': /is:spam/i,
        'chip-highrisk': /score:<50/i,
        'chip-safe': /is:(?:clean|safe)/i
    };

    for (const [id, regex] of Object.entries(chipMap)) {
        const chip = document.getElementById(id);
        if (chip) {
            chip.classList.toggle('active', regex.test(val));
        }
    }
};

window.openForensicsModal = (id) => {
    const all = [
        ...(window.AppState.stats.malicious || []),
        ...(window.AppState.stats.suspicious || []),
        ...(window.AppState.stats.spam || []),
        ...(window.AppState.stats.safe || []),
        ...((window.incidentVirtualList && window.incidentVirtualList.items) || [])
    ];
    const item = all.find(i => (i.entryId || i.fingerprint || i.id) === id);
    if (item) window.openForensics(item);
};

window.releaseIncident = async (entryId, fingerprint, trustSenderAddress) => {
    const btn = document.getElementById('btn-release');
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spin"><path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg> Releasing...`;
    }
    try {
        const known = [
            ...(window.AppState.stats.malicious || []),
            ...(window.AppState.stats.suspicious || []),
            ...(window.AppState.stats.spam || [])
        ].find(i => i.entryId === entryId || (fingerprint && i.fingerprint === fingerprint));
        const res = await window.securityApi.releaseEmail({ entryId, fingerprint, storeId: known ? known.storeId : undefined, targetFolder: 6 });
        if (res && (res.success || res.ok)) {
            let trusted = false;
            if (trustSenderAddress && typeof window.trustSender === 'function') {
                try { trusted = await window.trustSender(trustSenderAddress); } catch (e) {}
            }
            const panel = document.getElementById('detail-panel');
            if (panel) {
                panel.innerHTML = `
                    <div style="display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%; color: var(--ok); text-align: center; padding: 20px;">
                        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="margin-bottom: 12px;"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>
                        <h3 style="margin: 0 0 8px 0; color: var(--text);">Email Restored to Inbox</h3>
                        <p style="color: var(--muted); margin: 0; font-size: 0.85rem;">The message is back in your Outlook Inbox. DeskGuard will not move it again.${trusted ? ` ${window.escapeHtml(trustSenderAddress)} is now on your trusted list.` : ''}</p>
                    </div>`;
            }

            let actionedItem = null;
            let actionedCat = null;
            ['malicious', 'suspicious', 'spam', 'safe'].forEach(cat => {
                if (window.AppState.stats && window.AppState.stats[cat]) {
                    const idx = window.AppState.stats[cat].findIndex(item => (item.entryId || item.id) === entryId || (fingerprint && item.fingerprint === fingerprint));
                    if (idx !== -1) {
                        actionedItem = window.AppState.stats[cat][idx];
                        window.AppState.stats[cat].splice(idx, 1);
                        actionedCat = cat;
                    }
                }
            });

            if (actionedItem) {
                actionedItem.verdict = 'Safe';
                actionedItem.currentFolder = 'Inbox';
                window.AppState.stats.safe = window.AppState.stats.safe || [];
                window.AppState.stats.safe.unshift(actionedItem);
            }

            if (actionedCat && actionedCat !== 'safe' && window.AppState.counts) {
                if (window.AppState.counts[actionedCat] > 0) window.AppState.counts[actionedCat]--;
                window.AppState.counts.safe = (window.AppState.counts.safe || 0) + 1;
            }

            window.AppState.renderDashboard(false);
        } else {
            if (typeof window.showNotification === "function") window.showNotification("Release failed: " + ((res && res.error) || "Outlook could not locate or move the item."), true); else alert("Release failed: " + ((res && res.error) || "Outlook could not locate or move the item."));
            if (btn) {
                btn.disabled = false;
                btn.textContent = "Release to Inbox";
            }
        }
    } catch (err) {
        if (typeof window.showNotification === "function") window.showNotification("Release error: " + err.message, true); else alert("Release error: " + err.message);
        if (btn) {
            btn.disabled = false;
            btn.textContent = "Release to Inbox";
        }
    }
};

window.quarantineIncident = async (entryId, fingerprint, storeId) => {
    const btn = document.getElementById('btn-quarantine');
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spin"><path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg> Quarantining...`;
    }
    try {
        const res = await window.securityApi.quarantineEmail({ entryId, storeId: storeId || '', fingerprint, targetFolder: 23 });
        if (res && (res.success || res.ok)) {
            const panel = document.getElementById('detail-panel');
            if (panel) {
                panel.innerHTML = `
                    <div style="display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%; color: var(--warn); text-align: center; padding: 20px;">
                        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="margin-bottom: 12px;"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>
                        <h3 style="margin: 0 0 8px 0; color: var(--text);">Email Quarantined</h3>
                        <p style="color: var(--muted); margin: 0; font-size: 0.85rem;">The message was successfully moved to Outlook Junk Email.</p>
                    </div>`;
            }

            if (window.currentForensicsItem && ((window.currentForensicsItem.entryId || window.currentForensicsItem.id) === entryId || (fingerprint && window.currentForensicsItem.fingerprint === fingerprint))) {
                window.currentForensicsItem.verdict = 'Quarantined';
                window.currentForensicsItem.currentFolder = 'Junk Email';
                if (typeof window.showNotification === 'function') {
                    window.showNotification("Email successfully quarantined and moved to Junk Email.");
                }
            }

            let actionedItem = null;
            let actionedCat = null;
            ['malicious', 'suspicious', 'spam', 'safe'].forEach(cat => {
                if (window.AppState.stats && window.AppState.stats[cat]) {
                    const idx = window.AppState.stats[cat].findIndex(item => (item.entryId || item.id) === entryId || (fingerprint && item.fingerprint === fingerprint));
                    if (idx !== -1) {
                        actionedItem = window.AppState.stats[cat][idx];
                        window.AppState.stats[cat].splice(idx, 1);
                        actionedCat = cat;
                    }
                }
            });

            if (actionedItem) {
                actionedItem.verdict = 'Spam';
                actionedItem.currentFolder = 'Junk Email';
                window.AppState.stats.spam = window.AppState.stats.spam || [];
                window.AppState.stats.spam.unshift(actionedItem);
            }

            if (actionedCat && actionedCat !== 'spam' && window.AppState.counts) {
                if (window.AppState.counts[actionedCat] > 0) window.AppState.counts[actionedCat]--;
                window.AppState.counts.spam = (window.AppState.counts.spam || 0) + 1;
            }

            window.AppState.renderDashboard(false);
        } else {
            if (typeof window.showNotification === "function") window.showNotification("Quarantine failed: " + ((res && res.error) || "Outlook could not locate or move the item."), true); else alert("Quarantine failed: " + ((res && res.error) || "Outlook could not locate or move the item."));
            if (btn) {
                btn.disabled = false;
                btn.textContent = "Quarantine";
            }
        }
    } catch (err) {
        if (typeof window.showNotification === "function") window.showNotification("Quarantine error: " + err.message, true); else alert("Quarantine error: " + err.message);
        if (btn) {
            btn.disabled = false;
            btn.textContent = "Quarantine";
        }
    }
};

let pendingVtScanEntryId = null;

window.openVirusTotalSetupWizard = (entryId) => {
    pendingVtScanEntryId = entryId;
    const input = document.getElementById('wizard-vt-key');
    const status = document.getElementById('wizard-vt-status');
    if (input) input.value = '';
    if (status) status.style.display = 'none';
    openModal('virustotal-setup-modal');
    setTimeout(() => { if (input) input.focus(); }, 100);
};

window.closeVirusTotalSetupModal = () => {
    closeModal('virustotal-setup-modal');
    pendingVtScanEntryId = null;
};

window.saveVirusTotalKeyFromWizard = async () => {
    const input = document.getElementById('wizard-vt-key');
    const status = document.getElementById('wizard-vt-status');
    const btn = document.getElementById('wizard-vt-save-btn');
    const key = input ? input.value.trim() : '';

    if (!key || key.length < 16) {
        if (status) {
            status.style.display = 'block';
            status.style.background = 'rgba(241, 112, 123, 0.15)';
            status.style.color = 'var(--danger)';
            status.textContent = 'Please enter a valid VirusTotal API key (64 hex characters).';
        }
        return;
    }

    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Saving...';
    }

    try {
        await window.securityApi.setVTKey(key);
        if (status) {
            status.style.display = 'block';
            status.style.background = 'rgba(107, 183, 0, 0.15)';
            status.style.color = 'var(--ok)';
            status.textContent = 'Key connected and encrypted! Starting audit...';
        }
        window.addLog("VirusTotal API key successfully connected and encrypted via DPAPI.");
        const targetId = pendingVtScanEntryId;
        setTimeout(() => {
            window.closeVirusTotalSetupModal();
            if (targetId) {
                window.cloudScanIncident(targetId);
            }
        }, 600);
    } catch (err) {
        if (status) {
            status.style.display = 'block';
            status.style.background = 'rgba(241, 112, 123, 0.15)';
            status.style.color = 'var(--danger)';
            status.textContent = 'Error saving API key: ' + err.message;
        }
    } finally {
        if (btn) {
            btn.disabled = false;
            btn.textContent = 'Save Key & Audit';
        }
    }
};

window.scanEmailAv = async (item, triggerBtn) => {
    if (!item) return;
    const entryId = item.entryId || item.id;

    const modal = document.getElementById('virustotal-modal');
    const titleEl = document.getElementById('virustotal-title');
    const bodyEl = document.getElementById('virustotal-body');
    if (modal) modal.classList.add('active');
    if (titleEl) titleEl.textContent = 'Multi-Engine Antivirus & Threat Audit (Defender & ClamAV)';

    if (bodyEl) {
        bodyEl.innerHTML = `
            <div style="padding: 28px 20px; text-align: center;">
                <div style="width: 48px; height: 48px; border: 3px solid var(--border); border-top-color: var(--accent); border-radius: 50%; animation: spin 0.8s linear infinite; margin: 0 auto 16px auto;"></div>
                <div id="av-live-step" style="font-weight: 700; font-size: 1.15rem; color: var(--accent); margin-bottom: 8px;">Disassembling Email Components...</div>
                <div id="av-live-msg" style="font-size: 0.85rem; color: var(--muted); margin-bottom: 20px; max-width: 500px; margin-left: auto; margin-right: auto; line-height: 1.5;">
                    Extracting message headers, body payload, and file attachments for isolated antivirus inspection...
                </div>
                <div style="width: 100%; height: 8px; background: rgba(0,0,0,0.3); border-radius: 4px; overflow: hidden; max-width: 450px; margin: 0 auto; border: 1px solid var(--border);">
                    <div id="av-live-bar" style="width: 15%; height: 100%; background: linear-gradient(90deg, var(--accent), #00d2ff); transition: width 0.3s ease;"></div>
                </div>
            </div>
        `;
    }

    if (triggerBtn) {
        triggerBtn.disabled = true;
        triggerBtn.innerHTML = `<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" class="spin"><path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg> Scanning...`;
    }

    try {
        const res = await window.securityApi.scanAvLocal(entryId);
        if (res && res.success && res.report) {
            window.renderAvScanReport(res.report, item);
        } else {
            if (bodyEl) {
                bodyEl.innerHTML = `
                    <div style="padding: 24px; text-align: center;">
                        <div style="color: var(--danger); font-size: 1rem; font-weight: 700; margin-bottom: 8px;">Antivirus Scan Notice</div>
                        <div style="color: var(--muted); font-size: 0.85rem; margin-bottom: 16px;">${window.escapeHtml((res && res.error) || 'Failed to complete local antivirus scan.')}</div>
                        <button type="button" class="btn-ui" onclick="window.closeVirusTotalModal()">Close</button>
                    </div>
                `;
            }
        }
    } catch (err) {
        if (bodyEl) {
            bodyEl.innerHTML = `<div style="padding: 24px; color: var(--danger); text-align: center;">Scan Error: ${window.escapeHtml(err.message)}</div>`;
        }
    } finally {
        if (triggerBtn) {
            triggerBtn.disabled = false;
            triggerBtn.innerHTML = `<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path></svg> AV Scan`;
        }
    }
};

window.renderAvScanReport = (rep, item) => {
    const body = document.getElementById('virustotal-body');
    if (!body || !rep) return;

    const isMalicious = rep.overallVerdict === 'Malicious';
    const statusBg = isMalicious ? 'rgba(241, 112, 123, 0.12)' : 'rgba(35, 134, 54, 0.12)';
    const statusBorder = isMalicious ? 'var(--danger)' : 'var(--ok)';
    const statusColor = isMalicious ? 'var(--danger)' : 'var(--ok)';
    const statusIcon = isMalicious ? '⚠️ THREAT DETECTED' : '🛡️ VERIFIED CLEAN (ALL COMPONENTS SAFE)';

    let piecesHtml = '';
    if (rep.pieces && rep.pieces.length > 0) {
        piecesHtml = rep.pieces.map(p => {
            const isInf = p.verdict === 'Infected';
            const pieceColor = isInf ? 'var(--danger)' : 'var(--ok)';
            const pieceIcon = isInf ? '✕ Threat' : '✔ Clean';
            const sizeStr = p.sizeBytes > 0 ? ` (${(p.sizeBytes / 1024).toFixed(1)} KB)` : '';
            return `
                <div style="background: var(--surface-low); border: 1px solid var(--border); border-left: 4px solid ${pieceColor}; border-radius: 6px; padding: 12px 16px; margin-bottom: 8px;">
                    <div style="display: flex; justify-content: space-between; align-items: center;">
                        <div>
                            <div style="font-weight: 700; font-size: 0.88rem; color: var(--text);">
                                ${window.escapeHtml(p.name)}${sizeStr}
                            </div>
                            <div style="font-size: 0.74rem; color: var(--muted); margin-top: 3px;">
                                Component: <b>${window.escapeHtml(p.pieceType)}</b> • Scanned by: <b style="color: var(--accent);">${window.escapeHtml(p.engine)}</b>
                            </div>
                        </div>
                        <div style="text-align: right;">
                            <span style="font-weight: 700; font-size: 0.82rem; color: ${pieceColor};">
                                ${pieceIcon}
                            </span>
                            ${p.threatName ? `<div style="font-size: 0.72rem; color: var(--danger); font-family: monospace; margin-top: 2px;">${window.escapeHtml(p.threatName)}</div>` : ''}
                        </div>
                    </div>
                </div>
            `;
        }).join('');
    } else {
        piecesHtml = '<div style="color: var(--muted); padding: 12px;">No individual disassembled pieces evaluated.</div>';
    }

    body.innerHTML = `
        <div style="display: flex; flex-direction: column; gap: 14px;">
            <div style="background: ${statusBg}; border: 1px solid ${statusBorder}; border-radius: 8px; padding: 16px 20px;">
                <div style="display: flex; justify-content: space-between; align-items: center;">
                    <div style="font-weight: 800; font-size: 1.05rem; color: ${statusColor};">${statusIcon}</div>
                    <div style="display: flex; gap: 8px;">
                        <span class="badge" style="background: rgba(0, 120, 212, 0.15); color: var(--accent); border: 1px solid var(--accent); font-size: 0.72rem;">
                            Microsoft Defender: ${rep.defenderAvailable ? 'Active' : 'Offline'}
                        </span>
                        <span class="badge" style="background: rgba(255, 255, 255, 0.05); color: var(--muted); border: 1px solid var(--border); font-size: 0.72rem;">
                            ClamAV: ${rep.clamAvAvailable ? 'Active' : 'Not Installed'}
                        </span>
                    </div>
                </div>
                <div style="font-size: 0.82rem; color: var(--text); margin-top: 6px;">
                    ${window.escapeHtml(rep.summary)}
                </div>
            </div>

            <div style="font-size: 0.76rem; font-weight: 700; color: var(--muted); text-transform: uppercase; letter-spacing: 0.5px; margin-top: 4px;">
                Disassembled Email Components Breakdown (${rep.pieces ? rep.pieces.length : 0} items)
            </div>

            <div style="max-height: 320px; overflow-y: auto;">
                ${piecesHtml}
            </div>

            <div style="display: flex; justify-content: space-between; align-items: center; padding-top: 8px; border-top: 1px solid var(--border); margin-top: 4px;">
                <div style="font-size: 0.75rem; color: var(--muted);">
                    Subject: <b style="color: var(--text);">${window.escapeHtml(item.subject || '(No Subject)')}</b>
                </div>
                <div style="display: flex; gap: 8px;">
                    <button type="button" class="btn-ui" onclick="window.cloudScanIncident('${item.entryId || item.id}')" style="font-size: 0.78rem; padding: 6px 14px;">
                        Check VirusTotal Cloud
                    </button>
                    ${isMalicious ? `
                        <button type="button" class="btn-ui btn-quarantine" onclick="window.quarantineIncident('${item.entryId || item.id}', '${item.fingerprint || ''}', '${item.storeId || ''}'); window.closeVirusTotalModal();" style="font-size: 0.78rem; padding: 6px 14px; background: var(--warn); color: #000; font-weight: 700;">
                            Quarantine Email
                        </button>
                    ` : ''}
                </div>
            </div>
        </div>
    `;
};

if (window.securityApi && window.securityApi.onAvScanProgress) {
    window.securityApi.onAvScanProgress(ev => {
        if (!ev) return;
        const stepEl = document.getElementById('av-live-step');
        const msgEl = document.getElementById('av-live-msg');
        const barEl = document.getElementById('av-live-bar');
        if (stepEl && ev.engine) stepEl.textContent = `${ev.engine} Inspecting...`;
        if (msgEl && ev.message) msgEl.textContent = ev.message;
        if (barEl && ev.totalSteps > 0) {
            const pct = Math.min(100, Math.round((ev.step / ev.totalSteps) * 100));
            barEl.style.width = `${pct}%`;
        }
    });
}

window.cloudScanIncident = async (entryId) => {
    const btn = document.getElementById('btn-vt-scan');
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spin"><path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg> Scanning...`;
    }
    try {
        const res = await window.securityApi.scanForViruses(entryId);
        if (btn) {
            btn.disabled = false;
            btn.textContent = "VirusTotal Audit";
        }
        if (res && res.success && res.data) {
            window.showVirusTotalReport(res.data);
        } else if (res && (res.code === 'NO_API_KEY' || (res.error && res.error.includes('API key')))) {
            window.openVirusTotalSetupWizard(entryId);
        } else {
            if (typeof window.showNotification === "function") window.showNotification("VirusTotal notice: " + ((res && (res.error || (res.data && res.data.error))) || "No threat data available or item not found in database. Check your VirusTotal API key in Settings."), true); else alert("VirusTotal notice: " + ((res && (res.error || (res.data && res.data.error))) || "No threat data available or item not found in database. Check your VirusTotal API key in Settings."));
        }
    } catch (err) {
        if (btn) {
            btn.disabled = false;
            btn.textContent = "VirusTotal Audit";
        }
        if (typeof window.showNotification === "function") window.showNotification("VirusTotal query error: " + err.message, true); else alert("VirusTotal query error: " + err.message);
    }
};

window.showVirusTotalReport = (rep) => {
    const body = document.getElementById('virustotal-body');
    if (!body || !rep) return;
    
    let itemsHtml = '';
    if (rep.items && rep.items.length > 0) {
        itemsHtml = rep.items.map(it => {
            const malCount = parseInt(it.malicious) || 0;
            const suspCount = parseInt(it.suspicious) || 0;
            const isBad = (malCount > 0) || (suspCount > 0);
            const statusColor = isBad ? 'var(--danger)' : ((it.status && it.status.includes('Clean')) ? 'var(--ok)' : 'var(--muted)');
            const safeName = window.escapeHtml(it.name || 'Artifact');
            const safeType = window.escapeHtml(it.type || 'file');
            const safeStatus = window.escapeHtml(it.status || 'Audited');
            const safeHash = window.escapeHtml(it.hash || 'N/A');
            return `
                <div style="background: var(--surface-low); border: 1px solid var(--border); border-radius: 6px; padding: 12px; margin-bottom: 10px;">
                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px;">
                        <span style="font-weight: 600; font-size: 0.85rem; color: var(--text);">${safeName} (${safeType})</span>
                        <span style="font-weight: 700; font-size: 0.75rem; color: ${statusColor};">${safeStatus}</span>
                    </div>
                    <div style="font-family: monospace; font-size: 0.7rem; color: var(--muted); word-break: break-all;">
                        SHA256: ${safeHash}
                    </div>
                    ${(it.malicious !== undefined) ? `
                    <div style="margin-top: 6px; font-size: 0.75rem; display: flex; gap: 12px;">
                        <span style="color: var(--danger);">Malicious: ${malCount}</span>
                        <span style="color: var(--warn);">Suspicious: ${suspCount}</span>
                    </div>` : ''}
                </div>
            `;
        }).join('');
    } else {
        itemsHtml = '<div style="color: var(--muted); padding: 12px;">No hashable artifacts extracted for this message.</div>';
    }

    const safeSubject = window.escapeHtml(rep.subject || 'Incident Audit Report');
    const safeStatus = window.escapeHtml(rep.status || 'Report Ready');
    const safeSender = window.escapeHtml(rep.sender || 'Unknown');
    const safeIp = window.escapeHtml(rep.ip || 'Internal');
    const threatsCount = parseInt(rep.threatsFound) || 0;

    body.innerHTML = `
        <div style="margin-bottom: 16px;">
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px;">
                <h3 style="margin: 0; font-size: 1rem; color: var(--text);">${safeSubject}</h3>
                <span class="badge ${threatsCount > 0 ? 'badge-malicious' : 'badge-safe'}">${safeStatus}</span>
            </div>
            <div style="font-size: 0.75rem; color: var(--muted); display: grid; grid-template-columns: 1fr 1fr; gap: 8px;">
                <div>Sender: <span style="color: var(--text);">${safeSender}</span></div>
                <div>IP Origin: <span style="font-family: monospace; color: var(--accent);">${safeIp}</span></div>
                <div>API Integration: <span style="color: ${rep.vtQueried ? 'var(--ok)' : 'var(--warn)'};">${rep.vtQueried ? 'Connected to VirusTotal v3' : 'Local Hashes (No API Key)'}</span></div>
                <div>Threat Detections: <span style="font-weight: 700; color: ${threatsCount > 0 ? 'var(--danger)' : 'var(--ok)'};">${threatsCount}</span></div>
            </div>
        </div>
        <div style="margin-top: 16px;">
            <h4 style="margin: 0 0 10px 0; font-size: 0.8rem; text-transform: uppercase; color: var(--muted); letter-spacing: 0.5px;">Cryptographic Hashes & Signatures</h4>
            ${itemsHtml}
        </div>
    `;

    openModal('virustotal-modal');
};

window.verifyCurrentList = async () => {
    const btn = document.getElementById('verify-list-btn');
    const cat = window.AppState.currentCategory;
    const items = window.AppState.stats[cat] || [];
    if (items.length === 0) {
        if (typeof window.showNotification === "function") window.showNotification("No items in " + cat + " to verify."); else alert("No items in " + cat + " to verify.");
        return;
    }
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = `<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spin"><path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg> Verifying...`;
    }
    try {
        const res = await window.securityApi.verifyExistence({ items, category: cat });
        if (btn) {
            btn.disabled = false;
            btn.innerHTML = `<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 6L9 17l-5-5"></path></svg> Verify List`;
        }
        if (res && res.removedCount > 0) {
            if (typeof window.showNotification === "function") window.showNotification(`Verification complete: Pruned ${res.removedCount} missing item(s) from Outlook.`); else alert(`Verification complete: Pruned ${res.removedCount} missing item(s) from Outlook.`);
        } else {
            if (typeof window.showNotification === "function") window.showNotification(`Verification complete: All ${items.length} items in ${cat} confirmed present in Outlook.`); else alert(`Verification complete: All ${items.length} items in ${cat} confirmed present in Outlook.`);
        }
    } catch (err) {
        if (btn) {
            btn.disabled = false;
            btn.innerHTML = `<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 6L9 17l-5-5"></path></svg> Verify List`;
        }
        if (typeof window.showNotification === "function") window.showNotification("Verification error: " + err.message, true); else alert("Verification error: " + err.message);
    }
};

window.deleteIncident = (id) => {
    window.showConfirm("Delete Email", "This will permanently delete this email from Outlook. Proceed?", "DELETE", async () => {
        try {
            const res = await window.securityApi.deleteEmail({ entryId: id, entryIds: [id] });
            if (res && res.ok === false && res.success === false) {
                if (typeof window.showNotification === "function") window.showNotification("Delete failed: Item could not be deleted from Outlook.", true);
                return;
            }

            // Remove from AppState.stats
            let deletedCat = null;
            ['malicious', 'suspicious', 'spam', 'safe'].forEach(cat => {
                if (window.AppState.stats && window.AppState.stats[cat]) {
                    const idx = window.AppState.stats[cat].findIndex(item => (item.entryId || item.id) === id);
                    if (idx !== -1) {
                        window.AppState.stats[cat].splice(idx, 1);
                        deletedCat = cat;
                    }
                }
            });

            // Decrement counts
            if (deletedCat && window.AppState.counts) {
                if (window.AppState.counts[deletedCat] > 0) window.AppState.counts[deletedCat]--;
                if (window.AppState.counts.total > 0) window.AppState.counts.total--;
            }

            // Clear detail panel or forensics tab if this incident was selected
            if (window.AppState.selectedId === id || window.selectedIncidentId === id || (window.currentForensicsItem && ((window.currentForensicsItem.entryId || window.currentForensicsItem.id) === id))) {
                window.AppState.selectedId = null;
                window.selectedIncidentId = null;
                window.currentForensicsItem = null;
                const detailPanel = document.getElementById('detail-panel');
                if (detailPanel) {
                    detailPanel.innerHTML = `
                        <div class="empty-state">
                            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--border)" stroke-width="1.5"><path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path><polyline points="13 2 13 9 20 9"></polyline></svg>
                            <p>Select an incident to view forensic details</p>
                        </div>
                    `;
                }
                const foreContent = document.getElementById('forensics-tab-content');
                if (foreContent) {
                    foreContent.innerHTML = `
                        <div class="empty-state" style="padding: 60px 20px; text-align: center; color: var(--muted);">
                            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line><polyline points="10 9 9 9 8 9"></polyline></svg>
                            <h3 style="margin: 16px 0 8px; color: var(--text);">Email Deleted</h3>
                            <p style="font-size: 0.85rem; max-width: 460px; margin: 0 auto 20px;">The email was permanently removed from Outlook and the database.</p>
                            <button type="button" class="primary" onclick="switchTab('dashboard')" style="padding: 7px 18px; font-size: 0.82rem;">Go to Dashboard</button>
                        </div>
                    `;
                }
                const foreToolbar = document.getElementById('forensics-toolbar-actions');
                if (foreToolbar) foreToolbar.innerHTML = '';
            }

            // Re-render current list & counters
            window.AppState.renderDashboard(false);

            if (typeof window.showNotification === 'function') {
                window.showNotification("Email deleted successfully.");
            }
        } catch (err) {
            if (typeof window.showNotification === 'function') {
                window.showNotification("Failed to delete email: " + err.message, true);
            }
        }
    });
};

window.sortOrder = { key: 'date', asc: false };
window.sortList = (key) => {
    if (window.sortOrder.key === key) {
        window.sortOrder.asc = !window.sortOrder.asc;
    } else {
        window.sortOrder.key = key;
        window.sortOrder.asc = (key === 'score') ? false : (key === 'date' || key === 'time' ? false : true);
    }

    ['date', 'time', 'from', 'to', 'subject', 'verdict', 'score'].forEach(col => {
        const el = document.getElementById(`sort-${col}`);
        if (el) {
            if (col === window.sortOrder.key) {
                el.textContent = window.sortOrder.asc ? '▲' : '▼';
            } else {
                el.textContent = '';
            }
        }
    });

    if (window.vaultSearchActive && typeof window.executeVaultSearch === 'function') {
        window.executeVaultSearch();
    } else {
        const cat = window.AppState.currentCategory;
        const items = (cat === 'all')
            ? [
                ...(window.AppState.stats.malicious || []),
                ...(window.AppState.stats.suspicious || []),
                ...(window.AppState.stats.spam || []),
                ...(window.AppState.stats.safe || [])
              ]
            : (window.AppState.stats[cat] || []);
        window.renderList(items, cat, false);
    }
};
