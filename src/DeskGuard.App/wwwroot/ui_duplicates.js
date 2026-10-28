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

window.SecurityUI = window.SecurityUI || {};
SecurityUI.Duplicates = (function() {
    const api = window.securityApi;
    let duplicateVirtualList = null;
    let duplicateItems = [];
    let visibleItems = [];
    let selectedEntryIds = new Set();
    let isScanning = false;
    let includeJunk = false;
    let searchQuery = '';
    let sortField = 'date';
    let sortDirection = 'desc';

    const DEFAULT_WIDTHS = {
        check: '38px',
        date: '110px',
        subj: '2.4fr',
        sender: '1.2fr',
        size: '72px',
        folder: '110px',
        survivor: '120px'
    };

    window.lastDuplicates = [];
    window.setDuplicateIncludeJunk = (v) => { includeJunk = !!v; };

    function formatSize(bytes) {
        if (!bytes || bytes <= 0) return '0 KB';
        if (bytes >= 1073741824) return (bytes / 1073741824).toFixed(2) + ' GB';
        if (bytes >= 1048576) return (bytes / 1048576).toFixed(1) + ' MB';
        return (bytes / 1024).toFixed(1) + ' KB';
    }

    function loadSavedColumnWidths(container) {
        try {
            const raw = localStorage.getItem('deskguard_dup_col_widths');
            if (raw) {
                const widths = JSON.parse(raw);
                if (widths && typeof widths === 'object') {
                    for (const [k, v] of Object.entries(widths)) {
                        if (v && container) {
                            container.style.setProperty(`--dup-col-${k}`, v);
                        }
                    }
                }
            }
        } catch (e) {}
    }

    function saveColumnWidth(colKey, widthPx) {
        try {
            let widths = {};
            const raw = localStorage.getItem('deskguard_dup_col_widths');
            if (raw) widths = JSON.parse(raw) || {};
            widths[colKey] = `${Math.max(40, widthPx)}px`;
            localStorage.setItem('deskguard_dup_col_widths', JSON.stringify(widths));
        } catch (e) {}
    }

    function initColumnResizers(headerEl, containerEl) {
        if (!headerEl || !containerEl) return;
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
                    containerEl.style.setProperty(`--dup-col-${colKey}`, `${newWidth}px`);
                }

                function onMouseUp(upEvent) {
                    const diff = upEvent.pageX - startX;
                    const finalWidth = Math.max(50, startWidth + diff);
                    containerEl.style.setProperty(`--dup-col-${colKey}`, `${finalWidth}px`);
                    saveColumnWidth(colKey, finalWidth);
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

    window.openDuplicateInOutlook = async (entryId) => {
        if (!entryId) return;
        try {
            if (typeof window.showNotification === 'function') {
                window.showNotification("Opening email in Microsoft Outlook...");
            }
            const res = await api.openEmail({ entryId: entryId });
            if (!res || (!res.ok && !res.success)) {
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

    function createDuplicateRow() {
        const div = document.createElement('div');
        div.className = 'list-item duplicate-grid virtual-row';
        div.style.cursor = 'pointer';

        const checkCell = document.createElement('div');
        checkCell.className = 'dup-check-cell';
        const checkEl = document.createElement('input');
        checkEl.type = 'checkbox';
        checkEl.className = 'dup-row-check';
        checkEl.title = 'Select this copy for cleanup';
        checkCell.appendChild(checkEl);

        const timeEl = document.createElement('div');
        timeEl.style.fontSize = '0.75rem';
        timeEl.style.color = 'var(--muted)';
        timeEl.style.fontFamily = 'monospace';
        timeEl.style.overflow = 'hidden';
        timeEl.style.textOverflow = 'ellipsis';
        timeEl.style.whiteSpace = 'nowrap';

        const subjEl = document.createElement('div');
        subjEl.style.fontWeight = '500';
        subjEl.style.whiteSpace = 'nowrap';
        subjEl.style.overflow = 'hidden';
        subjEl.style.textOverflow = 'ellipsis';
        subjEl.style.color = 'var(--text)';

        const senderEl = document.createElement('div');
        senderEl.style.fontSize = '0.75rem';
        senderEl.style.whiteSpace = 'nowrap';
        senderEl.style.overflow = 'hidden';
        senderEl.style.textOverflow = 'ellipsis';
        senderEl.style.color = 'var(--muted)';

        const sizeEl = document.createElement('div');
        sizeEl.style.fontSize = '0.75rem';
        sizeEl.style.color = 'var(--muted)';

        const folderEl = document.createElement('div');
        folderEl.style.fontSize = '0.75rem';
        folderEl.style.whiteSpace = 'nowrap';
        folderEl.style.overflow = 'hidden';
        folderEl.style.textOverflow = 'ellipsis';

        const survivorEl = document.createElement('div');
        survivorEl.style.fontSize = '0.75rem';
        survivorEl.style.whiteSpace = 'nowrap';
        survivorEl.style.overflow = 'hidden';
        survivorEl.style.textOverflow = 'ellipsis';

        div.appendChild(checkCell);
        div.appendChild(timeEl);
        div.appendChild(subjEl);
        div.appendChild(senderEl);
        div.appendChild(sizeEl);
        div.appendChild(folderEl);
        div.appendChild(survivorEl);

        div._cells = { checkEl, timeEl, subjEl, senderEl, sizeEl, folderEl, survivorEl };

        checkEl.onclick = (e) => {
            e.stopPropagation();
        };

        checkEl.onchange = (e) => {
            e.stopPropagation();
            const id = div.getAttribute('data-id');
            if (!id) return;
            if (checkEl.checked) {
                selectedEntryIds.add(id);
            } else {
                selectedEntryIds.delete(id);
            }
            updateMetrics();
        };

        div.onclick = (e) => {
            if (e.target === checkEl || e.target.tagName === 'INPUT') return;
            const id = div.getAttribute('data-id');
            if (!id) return;
            if (selectedEntryIds.has(id)) {
                selectedEntryIds.delete(id);
                checkEl.checked = false;
            } else {
                selectedEntryIds.add(id);
                checkEl.checked = true;
            }
            updateMetrics();
        };

        div.ondblclick = () => {
            const id = div.getAttribute('data-id');
            if (id) window.openDuplicateInOutlook(id);
        };

        return div;
    }

    function updateDuplicateRow(div, item, index) {
        const cells = div._cells;
        const entryId = item.entryId || '';
        div.setAttribute('data-id', entryId);

        const isChecked = selectedEntryIds.has(entryId);
        if (cells.checkEl.checked !== isChecked) {
            cells.checkEl.checked = isChecked;
        }

        const ts = item.timestamp || '';
        if (cells.timeEl.textContent !== ts) {
            cells.timeEl.textContent = ts;
            cells.timeEl.title = ts;
        }

        const subj = item.subject || '(No Subject)';
        if (cells.subjEl.textContent !== subj) {
            cells.subjEl.textContent = subj;
            cells.subjEl.title = subj;
        }

        const sender = item.sender || '(Unknown Sender)';
        if (cells.senderEl.textContent !== sender) {
            cells.senderEl.textContent = sender;
            cells.senderEl.title = sender;
        }

        const sz = formatSize(item.size || 0);
        if (cells.sizeEl.textContent !== sz) cells.sizeEl.textContent = sz;

        const fld = item.folder || 'Mailbox';
        cells.folderEl.innerHTML = `<span class="badge" style="background: rgba(255, 170, 68, 0.12); color: var(--warn); border: 1px solid rgba(255, 170, 68, 0.3); font-size: 0.72rem; max-width: 100%; overflow: hidden; text-overflow: ellipsis; display: inline-block;" title="Duplicate copy located in: ${window.escapeHtml(fld)}">${window.escapeHtml(fld)}</span>`;

        const survFld = item.survivorFolder || (item.survivor && item.survivor.folder) || 'Inbox';
        cells.survivorEl.innerHTML = `<span class="badge badge-safe" style="font-size: 0.72rem; display: inline-flex; align-items: center; gap: 4px;" title="Original survivor kept safe in: ${window.escapeHtml(survFld)}">🛡️ ${window.escapeHtml(survFld)}</span>`;

        // Visual cluster connection: highlight multiple duplicates belonging to the same cluster
        const prevItem = index > 0 ? visibleItems[index - 1] : null;
        const nextItem = index < visibleItems.length - 1 ? visibleItems[index + 1] : null;
        const isCluster = item.dna && ((prevItem && prevItem.dna === item.dna) || (nextItem && nextItem.dna === item.dna));

        if (isCluster) {
            div.classList.add('dup-cluster-item');
        } else {
            div.classList.remove('dup-cluster-item');
        }

        div.title = `Duplicate copy in ${fld} (Original kept safe in ${survFld}). Double-click to open in Outlook.`;
    }

    function updateMetrics() {
        const totalCount = duplicateItems.length;
        const totalBytes = duplicateItems.reduce((acc, i) => acc + (i.size || 0), 0);
        const selectedList = duplicateItems.filter(i => selectedEntryIds.has(i.entryId));
        const selectedCount = selectedList.length;
        const selectedBytes = selectedList.reduce((acc, i) => acc + (i.size || 0), 0);

        const titleEl = document.getElementById('dup-header-title');
        const subtitleEl = document.getElementById('dup-header-subtitle');
        const safeBtn = document.getElementById('btn-clean-safe');
        const purgeBtn = document.getElementById('btn-clean-purge');
        const selectAllCheck = document.getElementById('dup-select-all');

        if (titleEl) {
            if (searchQuery) {
                titleEl.textContent = `Showing ${visibleItems.length} of ${totalCount.toLocaleString()} Copies · ${formatSize(totalBytes)} Reclaimable`;
            } else {
                titleEl.textContent = `${totalCount.toLocaleString()} Redundant Copies · ${formatSize(totalBytes)} Reclaimable`;
            }
        }

        if (subtitleEl) {
            if (selectedCount === totalCount) {
                subtitleEl.textContent = `All ${selectedCount.toLocaleString()} copies selected (${formatSize(selectedBytes)}). Original survivor emails are never deleted. Double-click any row to view in Outlook.`;
            } else if (selectedCount === 0) {
                subtitleEl.textContent = `0 of ${totalCount.toLocaleString()} selected. Check the boxes to choose which copies to clean. Original emails are preserved.`;
            } else {
                subtitleEl.textContent = `${selectedCount.toLocaleString()} of ${totalCount.toLocaleString()} selected (${formatSize(selectedBytes)}). Original emails in safe folders are never deleted.`;
            }
        }

        if (safeBtn) {
            safeBtn.disabled = selectedCount === 0;
            safeBtn.textContent = selectedCount > 0
                ? `Move Selected (${selectedCount}) to Deleted Items`
                : 'Move to Deleted Items (Safe)';
        }

        if (purgeBtn) {
            purgeBtn.disabled = selectedCount === 0;
            purgeBtn.textContent = selectedCount > 0
                ? `Permanent Purge (${selectedCount})`
                : 'Permanent Purge';
        }

        if (selectAllCheck) {
            if (visibleItems.length === 0) {
                selectAllCheck.checked = false;
                selectAllCheck.indeterminate = false;
            } else {
                const visibleSelected = visibleItems.filter(i => selectedEntryIds.has(i.entryId)).length;
                if (visibleSelected === visibleItems.length) {
                    selectAllCheck.checked = true;
                    selectAllCheck.indeterminate = false;
                } else if (visibleSelected > 0) {
                    selectAllCheck.checked = false;
                    selectAllCheck.indeterminate = true;
                } else {
                    selectAllCheck.checked = false;
                    selectAllCheck.indeterminate = false;
                }
            }
        }
    }

    function applyFilterAndSort() {
        let items = duplicateItems.slice();

        if (searchQuery) {
            const q = searchQuery.toLowerCase().trim();
            items = items.filter(i =>
                (i.subject && i.subject.toLowerCase().includes(q)) ||
                (i.sender && i.sender.toLowerCase().includes(q)) ||
                (i.folder && i.folder.toLowerCase().includes(q)) ||
                (i.survivorFolder && i.survivorFolder.toLowerCase().includes(q))
            );
        }

        items.sort((a, b) => {
            let res = 0;
            if (sortField === 'size') {
                const sA = a.size || 0;
                const sB = b.size || 0;
                res = sortDirection === 'asc' ? (sA - sB) : (sB - sA);
            } else if (sortField === 'date') {
                const dA = a.timestamp || '';
                const dB = b.timestamp || '';
                res = sortDirection === 'asc' ? dA.localeCompare(dB) : dB.localeCompare(dA);
            } else if (sortField === 'subject') {
                const subA = (a.subject || '').toLowerCase();
                const subB = (b.subject || '').toLowerCase();
                res = sortDirection === 'asc' ? subA.localeCompare(subB) : subB.localeCompare(subA);
            } else if (sortField === 'sender') {
                const sndA = (a.sender || '').toLowerCase();
                const sndB = (b.sender || '').toLowerCase();
                res = sortDirection === 'asc' ? sndA.localeCompare(sndB) : sndB.localeCompare(sndA);
            } else if (sortField === 'folder') {
                const fA = (a.folder || '').toLowerCase();
                const fB = (b.folder || '').toLowerCase();
                res = sortDirection === 'asc' ? fA.localeCompare(fB) : fB.localeCompare(fA);
            } else if (sortField === 'survivor') {
                const surA = (a.survivorFolder || (a.survivor && a.survivor.folder) || '').toLowerCase();
                const surB = (b.survivorFolder || (b.survivor && b.survivor.folder) || '').toLowerCase();
                res = sortDirection === 'asc' ? surA.localeCompare(surB) : surB.localeCompare(surA);
            }

            // Secondary sort: keep clusters grouped together if primary sort is tied
            if (res === 0 && a.dna && b.dna) {
                res = a.dna.localeCompare(b.dna);
            }
            return res;
        });

        visibleItems = items;
        updateHeaderSortArrows();
        updateMetrics();

        if (duplicateVirtualList) {
            duplicateVirtualList.setItems(visibleItems, false);
        }
    }

    function updateHeaderSortArrows() {
        const fields = ['date', 'subject', 'sender', 'size', 'folder', 'survivor'];
        fields.forEach(f => {
            const arrowEl = document.getElementById(`dup-sort-${f}`);
            if (!arrowEl) return;
            if (sortField === f) {
                arrowEl.textContent = sortDirection === 'asc' ? ' ▲' : ' ▼';
                arrowEl.style.color = 'var(--accent)';
            } else {
                arrowEl.textContent = ' ↕';
                arrowEl.style.color = 'var(--muted)';
            }
        });
    }

    window.setDuplicateSort = (field) => {
        if (sortField === field) {
            sortDirection = sortDirection === 'asc' ? 'desc' : 'asc';
        } else {
            sortField = field;
            // When sorting size or date, default to descending (largest / newest first)
            sortDirection = (field === 'size' || field === 'date') ? 'desc' : 'asc';
        }
        applyFilterAndSort();
    };

    window.toggleSelectAllDuplicates = (checked) => {
        visibleItems.forEach(i => {
            if (checked) {
                selectedEntryIds.add(i.entryId);
            } else {
                selectedEntryIds.delete(i.entryId);
            }
        });
        updateMetrics();
        if (duplicateVirtualList) {
            duplicateVirtualList.refreshCurrentSlice();
        }
    };

    window.handleDuplicateSearchInput = (e) => {
        searchQuery = (e.target.value || '').trim();
        const clearBtn = document.getElementById('dup-search-clear');
        if (clearBtn) {
            clearBtn.style.display = searchQuery ? 'block' : 'none';
        }
        applyFilterAndSort();
    };

    window.clearDuplicateSearch = () => {
        searchQuery = '';
        const input = document.getElementById('dup-search-input');
        if (input) input.value = '';
        const clearBtn = document.getElementById('dup-search-clear');
        if (clearBtn) clearBtn.style.display = 'none';
        applyFilterAndSort();
    };

    function renderInitialView(errorMsg = null) {
        const view = document.getElementById('duplicate-view');
        if (!view) return;
        if (isScanning) return;

        view.innerHTML = `
            <div style="display: flex; flex-direction: column; height: 100%; min-height: 0; box-sizing: border-box; align-items: center; justify-content: center; padding: 24px;">
                ${errorMsg ? `
                    <div style="margin-bottom: 20px; max-width: 580px; width: 100%; padding: 12px 16px; background: rgba(241, 112, 123, 0.15); border: 1px solid var(--danger); border-radius: 8px; color: var(--danger); font-size: 0.85rem; display: flex; align-items: center; justify-content: space-between;">
                        <span>${window.escapeHtml(errorMsg)}</span>
                        <button onclick="window.renderDuplicateInitialView()" style="background: transparent; border: none; color: var(--danger); cursor: pointer; font-weight: bold; font-size: 1rem;">✕</button>
                    </div>
                ` : ''}

                <div style="background: var(--surface-low); border: 1px solid var(--border); border-radius: 12px; padding: 40px; max-width: 580px; width: 100%; text-align: center; box-shadow: 0 4px 24px rgba(0,0,0,0.2);">
                    <div style="width: 56px; height: 56px; border-radius: 50%; background: rgba(0, 120, 212, 0.12); display: flex; align-items: center; justify-content: center; margin: 0 auto 16px; color: var(--accent);">
                        <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor">
                            <path d="M15,9H5V5H15M12,19A7,7 0 1,1 19,12A7,7 0 0,1 12,19M12,3A9,9 0 0,0 3,12V21H12A9,9 0 0,0 21,12A9,9 0 0,0 12,3Z"/>
                        </svg>
                    </div>
                    <h2 style="margin: 0 0 8px; font-size: 1.3rem; color: var(--text); font-weight: 600;">Duplicate Email Finder</h2>
                    <p style="color: var(--muted); font-size: 0.86rem; line-height: 1.6; margin: 0 0 24px;">
                        Quickly reclaim mailbox storage by finding and cleaning identical copies of emails across your Outlook folders. Original emails are always kept safe.
                    </p>

                    <div style="display: flex; flex-direction: column; align-items: center; gap: 16px;">
                        <label style="display: inline-flex; align-items: center; gap: 8px; font-size: 0.85rem; color: var(--text); cursor: pointer;" title="Inspect Junk Email folder for duplicate copies as well">
                            <input type="checkbox" id="dup-include-junk" ${includeJunk ? 'checked' : ''} onchange="window.setDuplicateIncludeJunk(this.checked)" style="cursor: pointer; width: 15px; height: 15px; accent-color: var(--accent);">
                            Include Junk Email folder
                        </label>

                        <button class="primary" onclick="window.startDuplicateDiscovery()" style="padding: 11px 30px; font-weight: 600; font-size: 0.92rem; display: inline-flex; align-items: center; gap: 8px;">
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                                <circle cx="11" cy="11" r="8"></circle>
                                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
                            </svg>
                            Scan Mailbox for Duplicates
                        </button>
                    </div>

                    <div style="margin-top: 24px; padding-top: 16px; border-top: 1px solid var(--border); font-size: 0.76rem; color: var(--muted); display: flex; justify-content: center; align-items: center; gap: 16px;">
                        <span>🛡️ Pre-flight verified &amp; safe</span>
                        <span>•</span>
                        <button onclick="window.resetDuplicateStack()" style="background: transparent; border: none; color: var(--muted); font-size: 0.76rem; text-decoration: underline; cursor: pointer; padding: 0;">Clear saved results</button>
                    </div>
                </div>
            </div>
        `;
    }

    function render(data) {
        const view = document.getElementById('duplicate-view');
        if (!data || !view) return;

        if (data.error) {
            isScanning = false;
            renderInitialView(`Scan error: ${data.error}`);
            return;
        }

        if (data.status === 'Scanned' || data.status === 'Progress' || data.status === 'Found' || data.status === 'StoreStart') {
            isScanning = true;
            const scannedCount = data.scanned !== undefined ? data.scanned : (data.current || 0);
            const foundCount = data.found !== undefined ? data.found : (duplicateItems.length || 0);
            const html = `
                <div style="padding: 40px; text-align: center; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%;">
                    <div style="width: 44px; height: 44px; border: 3px solid var(--border); border-top-color: var(--accent); border-radius: 50%; animation: spin 0.8s linear infinite; margin-bottom: 18px;"></div>
                    <div class="stat-value" style="color: var(--accent); font-size: 1.5rem;">${Number(scannedCount).toLocaleString()}</div>
                    <div class="stat-label">Emails Scanned</div>
                    <div style="margin-top: 8px; font-size: 0.85rem; color: var(--warn); font-weight: 600;">
                        ${Number(foundCount).toLocaleString()} Redundant Copies Found
                    </div>
                    <div style="margin-top: 16px; font-size: 0.85rem; color: var(--muted); line-height: 1.5; max-width: 480px;">
                        ${data.store ? `Scanning: <b>${window.escapeHtml(data.store)}</b> / ${window.escapeHtml(data.currentFolder || data.folder || '')}<br>` : ''}
                        ${data.details ? `<span>${window.escapeHtml(data.details)}</span><br>` : ''}
                    </div>
                    <div style="margin-top: 26px; display: flex; gap: 12px;">
                        <button onclick="window.securityApi.pauseDuplicateScan()">Pause Discovery</button>
                        <button onclick="window.resetDuplicateStack()" style="background: var(--surface-mid); border: 1px solid var(--border);">Cancel &amp; Reset</button>
                    </div>
                </div>
            `;
            if (view.innerHTML !== html) view.innerHTML = html;
        } else if (data.status === 'Finished') {
            isScanning = false;
            const items = data.items || [];
            duplicateItems = items;
            selectedEntryIds = new Set(items.map(i => i.entryId));
            window.lastDuplicates = items.map(i => i.entryId);

            if (items.length === 0) {
                view.innerHTML = `
                    <div style="flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; background: var(--surface-low); border: 1px solid var(--border); border-radius: 8px; padding: 40px; text-align: center; height: 100%;">
                        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--ok, #4ade80)" stroke-width="2"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>
                        <h3 style="margin: 16px 0 8px; font-size: 1.15rem; color: var(--text);">Clean Mailbox</h3>
                        <p style="color: var(--muted); max-width: 440px; font-size: 0.85rem; line-height: 1.6; margin: 0 0 24px;">No redundant or duplicate email copies detected across all inspected Outlook folders.</p>
                        <button class="primary" onclick="window.startDuplicateDiscovery()" style="padding: 8px 20px;">Run Discovery Again</button>
                    </div>
                `;
                return;
            }

            const totalBytes = items.reduce((acc, i) => acc + (i.size || 0), 0);

            view.innerHTML = `
                <div style="display: flex; flex-direction: column; height: 100%; min-height: 0;">
                    <div class="section-header" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 14px; flex-shrink: 0; gap: 16px; flex-wrap: wrap;">
                        <div>
                            <h2 id="dup-header-title" style="margin: 0; font-size: 1.1rem; color: var(--text);">
                                ${items.length.toLocaleString()} Redundant Copies · ${formatSize(totalBytes)} Reclaimable
                            </h2>
                            <div id="dup-header-subtitle" style="font-size: 0.75rem; color: var(--muted); margin-top: 3px;">
                                All ${items.length.toLocaleString()} copies selected. Original survivor emails are never deleted. Double-click any row to view in Outlook.
                            </div>
                        </div>
                        <div style="display: flex; gap: 10px; align-items: center;">
                            <div style="position: relative; display: flex; align-items: center; width: 240px;">
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="position: absolute; left: 10px; color: var(--muted); pointer-events: none;"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>
                                <input type="text" id="dup-search-input" oninput="window.handleDuplicateSearchInput(event)" placeholder="Filter duplicates..." style="width: 100%; padding: 6px 26px 6px 28px; font-size: 0.78rem; background: var(--surface-low); border: 1px solid var(--border); border-radius: 6px; color: var(--text);" />
                                <button id="dup-search-clear" onclick="window.clearDuplicateSearch()" style="position: absolute; right: 6px; background: transparent; border: none; color: var(--muted); cursor: pointer; padding: 2px 4px; display: none; font-size: 0.85rem;" title="Clear filter" aria-label="Clear filter">&times;</button>
                            </div>
                            <button onclick="window.startDuplicateDiscovery()" style="background: var(--surface-mid); border: 1px solid var(--border); font-size: 0.8rem; padding: 6px 14px;">Re-Scan</button>
                            <button id="btn-clean-safe" class="primary" onclick="cleanDuplicatesSafe()" style="font-size: 0.8rem; padding: 6px 14px;">Move to Deleted Items (Safe)</button>
                            <button id="btn-clean-purge" class="danger" onclick="cleanDuplicatesPurge()" style="font-size: 0.8rem; padding: 6px 14px;">Permanent Purge</button>
                        </div>
                    </div>
                    <div id="dup-list-header" class="list-header duplicate-grid" style="flex-shrink: 0;">
                        <div class="dup-check-cell" style="cursor: default;">
                            <input type="checkbox" id="dup-select-all" checked onchange="window.toggleSelectAllDuplicates(this.checked)" title="Select / Deselect all visible duplicates" style="cursor: pointer; width: 15px; height: 15px; accent-color: var(--accent); margin: 0;">
                        </div>
                        <div class="col-header" onclick="window.setDuplicateSort('date')" title="Sort by Date Received">
                            Date<span id="dup-sort-date"> ▼</span>
                            <div class="col-resizer" data-col="date" title="Drag to resize column"></div>
                        </div>
                        <div class="col-header" onclick="window.setDuplicateSort('subject')" title="Sort by Subject">
                            Subject<span id="dup-sort-subject"> ↕</span>
                            <div class="col-resizer" data-col="subj" title="Drag to resize column"></div>
                        </div>
                        <div class="col-header" onclick="window.setDuplicateSort('sender')" title="Sort by Sender">
                            Sender<span id="dup-sort-sender"> ↕</span>
                            <div class="col-resizer" data-col="sender" title="Drag to resize column"></div>
                        </div>
                        <div class="col-header" onclick="window.setDuplicateSort('size')" title="Sort by Email Size">
                            Size<span id="dup-sort-size"> ↕</span>
                            <div class="col-resizer" data-col="size" title="Drag to resize column"></div>
                        </div>
                        <div class="col-header" onclick="window.setDuplicateSort('folder')" title="Sort by Duplicate Location">
                            Duplicate Copy In<span id="dup-sort-folder"> ↕</span>
                            <div class="col-resizer" data-col="folder" title="Drag to resize column"></div>
                        </div>
                        <div class="col-header" onclick="window.setDuplicateSort('survivor')" title="Sort by Protected Survivor Folder">
                            Original Safe In<span id="dup-sort-survivor"> ↕</span>
                            <div class="col-resizer" data-col="survivor" title="Drag to resize column"></div>
                        </div>
                    </div>
                    <div id="duplicate-list" class="list-container" style="flex: 1; min-height: 0; position: relative; overflow-y: auto;">
                    </div>
                </div>
            `;

            loadSavedColumnWidths(view);
            const headerEl = document.getElementById('dup-list-header');
            initColumnResizers(headerEl, view);

            const container = document.getElementById('duplicate-list');
            if (container && window.VirtualList) {
                duplicateVirtualList = new window.VirtualList({
                    container: container,
                    rowHeight: 44,
                    buffer: 5,
                    createRow: createDuplicateRow,
                    updateRow: updateDuplicateRow,
                    emptyRenderer: () => `
                        <div class="empty-state" style="padding: 40px; text-align: center; color: var(--muted);">
                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>
                            <h3 style="margin: 12px 0 6px; color: var(--text);">No Matching Duplicates</h3>
                            <p style="font-size: 0.85rem;">No duplicate emails match "${window.escapeHtml(searchQuery)}".</p>
                        </div>`
                });
                applyFilterAndSort();
            }
        } else if (data.status === 'Paused') {
            isScanning = false;
            if (Array.isArray(data.items)) {
                duplicateItems = data.items;
                selectedEntryIds = new Set(data.items.map(i => i.entryId));
            }
            view.innerHTML = `
                <div style="padding: 40px; text-align: center; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%;">
                    <h3 style="margin: 0 0 10px; font-size: 1.2rem;">Discovery Paused</h3>
                    <p style="color: var(--muted); margin-bottom: 24px; font-size: 0.9rem;">
                        Paused after ${Number(data.scanned || 0).toLocaleString()} emails scanned; ${Number(data.found || (data.items ? data.items.length : 0)).toLocaleString()} duplicate copies found so far.
                    </p>
                    <div style="display: flex; gap: 12px;">
                        <button class="primary" onclick="window.securityApi.resumeDuplicateScan()" style="padding: 8px 20px;">Resume Discovery</button>
                        <button onclick="window.resetDuplicateStack()" style="background: var(--surface-mid); border: 1px solid var(--border); padding: 8px 18px;">Reset Engine</button>
                    </div>
                </div>
            `;
        }
    }

    window.startDuplicateDiscovery = async () => {
        const view = document.getElementById('duplicate-view');
        if (view) {
            view.innerHTML = `
                <div style="flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%; min-height: 300px; text-align: center;">
                    <div style="width: 40px; height: 40px; border: 3px solid var(--border); border-top-color: var(--accent); border-radius: 50%; animation: spin 0.8s linear infinite; margin-bottom: 16px;"></div>
                    <div class="stat-value" style="color: var(--accent); font-size: 1.2rem; margin-bottom: 8px;">Initializing Mailbox Crawler...</div>
                    <div style="font-size: 0.85rem; color: var(--muted); max-width: 460px; line-height: 1.5;">
                        Connecting to Outlook MAPI session, acquiring store handles, and preparing recursive folder traversal...
                    </div>
                </div>
            `;
        }
        isScanning = true;
        try {
            const res = await api.scanDuplicates({ includeJunk });
            if (!res || !res.ok) {
                isScanning = false;
                renderInitialView(res && res.error ? res.error : 'Failed to initialize duplicate scan engine.');
            }
        } catch (err) {
            isScanning = false;
            renderInitialView(err.message || 'Error communicating with duplicate scanner.');
        }
    };

    window.resetDuplicateStack = async () => {
        isScanning = false;
        try {
            await api.resetDuplicateEngine();
        } catch (e) {}
        duplicateItems = [];
        visibleItems = [];
        selectedEntryIds.clear();
        window.lastDuplicates = [];
        renderInitialView();
    };

    window.cleanDuplicatesSafe = async () => {
        const targets = duplicateItems.filter(i => selectedEntryIds.has(i.entryId));
        if (targets.length === 0) return;

        const confirmFn = window.showConfirmModal || ((title, msg, fn) => {
            if (window.showConfirm) window.showConfirm(title, msg, fn);
        });

        confirmFn(
            "Move Duplicates to Deleted Items",
            `This will safely move ${targets.length.toLocaleString()} selected redundant email copies to Deleted Items. Pre-flight validation guarantees that each original survivor email remains untouched in its safe folder. Proceed?`,
            async () => {
                const btn = document.getElementById('btn-clean-safe');
                if (btn) {
                    btn.disabled = true;
                    btn.textContent = "Processing...";
                }
                try {
                    const res = await api.deleteDuplicates({ items: targets, mode: 'safe' });
                    const success = (res && res.successCount !== undefined) ? res.successCount : ((res && res.count) || 0);
                    const skipped = (res && res.skippedCount) || 0;
                    let msg = `Clean-up Complete: Successfully isolated ${success.toLocaleString()} redundant copies.`;
                    if (skipped > 0) {
                        msg += ` ${skipped.toLocaleString()} items protected (survivors missing or modified).`;
                    }
                    if (typeof window.showNotification === 'function') {
                        window.showNotification(msg, false);
                    }

                    const targetSet = new Set(targets.map(t => t.entryId));
                    duplicateItems = duplicateItems.filter(i => !targetSet.has(i.entryId));
                    targetSet.forEach(id => selectedEntryIds.delete(id));
                    window.lastDuplicates = duplicateItems.map(i => i.entryId);

                    if (duplicateItems.length === 0) {
                        render({ status: 'Finished', items: [] });
                    } else {
                        applyFilterAndSort();
                    }
                } catch (err) {
                    if (typeof window.showNotification === 'function') {
                        window.showNotification("Error isolating duplicates: " + err.message, true);
                    }
                    if (btn) {
                        btn.disabled = false;
                        btn.textContent = "Move to Deleted Items (Safe)";
                    }
                }
            }
        );
    };

    window.cleanDuplicatesPurge = async () => {
        const targets = duplicateItems.filter(i => selectedEntryIds.has(i.entryId));
        if (targets.length === 0) return;

        const confirmFn = window.showConfirmModal || ((title, msg, fn, kw) => {
            if (window.showConfirm) window.showConfirm(title, msg, kw, fn);
        });

        confirmFn(
            "Permanent Purge of Duplicates",
            `WARNING: This will permanently delete ${targets.length.toLocaleString()} selected redundant email copies from Outlook. Pre-flight validation strictly verifies that each original survivor exists before any duplicate is deleted. Type PURGE to confirm.`,
            async () => {
                const btn = document.getElementById('btn-clean-purge');
                if (btn) {
                    btn.disabled = true;
                    btn.textContent = "Purging...";
                }
                try {
                    const res = await api.deleteDuplicates({ items: targets, mode: 'purge' });
                    const success = (res && res.successCount !== undefined) ? res.successCount : ((res && res.count) || 0);
                    const skipped = (res && res.skippedCount) || 0;
                    let msg = `Permanent Purge Complete: Successfully deleted ${success.toLocaleString()} redundant copies.`;
                    if (skipped > 0) {
                        msg += ` ${skipped.toLocaleString()} items protected (survivors missing or modified).`;
                    }
                    if (typeof window.showNotification === 'function') {
                        window.showNotification(msg, false);
                    }

                    const targetSet = new Set(targets.map(t => t.entryId));
                    duplicateItems = duplicateItems.filter(i => !targetSet.has(i.entryId));
                    targetSet.forEach(id => selectedEntryIds.delete(id));
                    window.lastDuplicates = duplicateItems.map(i => i.entryId);

                    if (duplicateItems.length === 0) {
                        render({ status: 'Finished', items: [] });
                    } else {
                        applyFilterAndSort();
                    }
                } catch (err) {
                    if (typeof window.showNotification === 'function') {
                        window.showNotification("Error purging duplicates: " + err.message, true);
                    }
                    if (btn) {
                        btn.disabled = false;
                        btn.textContent = "Permanent Purge";
                    }
                }
            },
            "PURGE"
        );
    };

    window.deleteDuplicates = window.cleanDuplicatesSafe;
    window.renderDuplicateInitialView = renderInitialView;

    api.onDuplicateUpdate(data => render(data));

    async function showStartView() {
        if (duplicateItems.length > 0 || isScanning) return;
        renderInitialView();
        try {
            const saved = api.getSavedDuplicates ? await api.getSavedDuplicates() : null;
            if (saved && Array.isArray(saved.items) && saved.items.length > 0 && duplicateItems.length === 0 && !isScanning) {
                render({ status: 'Finished', items: saved.items, savedAt: saved.savedAt, partial: saved.status === 'Paused' });
            }
        } catch (e) {}
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', showStartView);
    } else {
        showStartView();
    }

    return {
        renderInitialView,
        render
    };
})();
