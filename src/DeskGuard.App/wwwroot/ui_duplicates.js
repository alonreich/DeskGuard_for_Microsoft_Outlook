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
    let duplicateItems = [];
    let selectedEntryIds = new Set();
    let isScanning = false;
    let includeJunk = false;
    let searchQuery = '';

    window.lastDuplicates = [];
    window.setDuplicateIncludeJunk = (v) => { includeJunk = !!v; };

    function formatSize(bytes) {
        if (!bytes || bytes <= 0) return '0 KB';
        if (bytes >= 1073741824) return (bytes / 1073741824).toFixed(2) + ' GB';
        if (bytes >= 1048576) return (bytes / 1048576).toFixed(1) + ' MB';
        return (bytes / 1024).toFixed(1) + ' KB';
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

    function buildClusters(items) {
        const clusters = new Map();
        for (const item of items) {
            const clusterKey = item.dna || item.survivorId || (item.subject + '_' + item.sender);
            if (!clusters.has(clusterKey)) {
                clusters.set(clusterKey, {
                    key: clusterKey,
                    subject: item.subject || '(No Subject)',
                    sender: item.sender || 'Unknown',
                    survivor: {
                        id: item.survivorId || '',
                        folder: item.survivorFolder || 'Inbox',
                        store: item.survivorStore || '',
                        timestamp: item.timestamp || ''
                    },
                    copies: []
                });
            }
            clusters.get(clusterKey).copies.push(item);
        }
        return Array.from(clusters.values());
    }

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
                        Detect redundant email copies across all Outlook folders. One authentic copy (Inbox) is always protected as the Sole Survivor. You can jump to inspect each copy directly in Outlook before cleaning.
                    </p>

                    <div style="display: flex; flex-direction: column; align-items: center; gap: 16px;">
                        <label style="display: inline-flex; align-items: center; gap: 8px; font-size: 0.85rem; color: var(--text); cursor: pointer;" title="Inspect Junk Email folder for duplicate copies as well">
                            <input type="checkbox" id="dup-include-junk" ${includeJunk ? 'checked' : ''} onchange="window.setDuplicateIncludeJunk(this.checked)" style="cursor: pointer; width: 15px; height: 15px; accent-color: var(--accent);">
                            Include Junk Email folder in search
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
                        <span>🛡️ Zero accidental loss &bull; Survivor locked</span>
                        <span>•</span>
                        <button onclick="window.resetDuplicateStack()" style="background: transparent; border: none; color: var(--muted); font-size: 0.76rem; text-decoration: underline; cursor: pointer; padding: 0;">Clear saved results</button>
                    </div>
                </div>
            </div>
        `;
    }

    function updateMetrics() {
        const totalCount = duplicateItems.length;
        const totalBytes = duplicateItems.reduce((acc, i) => acc + (i.size || 0), 0);
        const selectedList = duplicateItems.filter(i => selectedEntryIds.has(i.entryId));
        const selectedCount = selectedList.length;
        const selectedBytes = selectedList.reduce((acc, i) => acc + (i.size || 0), 0);

        const safeBtn = document.getElementById('btn-clean-safe');
        const purgeBtn = document.getElementById('btn-clean-purge');
        const subtitleEl = document.getElementById('dup-header-subtitle');
        const selectAllCheck = document.getElementById('dup-select-all');

        if (subtitleEl) {
            subtitleEl.textContent = `${selectedCount.toLocaleString()} of ${totalCount.toLocaleString()} redundant copies selected (${formatSize(selectedBytes)}). The 1 authentic survivor email per cluster is locked and NEVER deleted.`;
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
            if (totalCount === 0) {
                selectAllCheck.checked = false;
                selectAllCheck.indeterminate = false;
            } else if (selectedCount === totalCount) {
                selectAllCheck.checked = true;
                selectAllCheck.indeterminate = false;
            } else if (selectedCount > 0) {
                selectAllCheck.checked = false;
                selectAllCheck.indeterminate = true;
            } else {
                selectAllCheck.checked = false;
                selectAllCheck.indeterminate = false;
            }
        }
    }

    function renderClusterCards() {
        const container = document.getElementById('duplicate-clusters-container');
        if (!container) return;

        let filteredItems = duplicateItems;
        if (searchQuery) {
            const q = searchQuery.toLowerCase().trim();
            filteredItems = duplicateItems.filter(i =>
                (i.subject && i.subject.toLowerCase().includes(q)) ||
                (i.sender && i.sender.toLowerCase().includes(q)) ||
                (i.folder && i.folder.toLowerCase().includes(q)) ||
                (i.survivorFolder && i.survivorFolder.toLowerCase().includes(q))
            );
        }

        const clusters = buildClusters(filteredItems);

        if (clusters.length === 0) {
            container.innerHTML = `
                <div class="empty-state" style="padding: 50px 20px; text-align: center; color: var(--muted);">
                    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>
                    <h3 style="margin: 12px 0 6px; color: var(--text);">No Matching Duplicates</h3>
                    <p style="font-size: 0.85rem;">No duplicates match "${window.escapeHtml(searchQuery)}".</p>
                </div>
            `;
            return;
        }

        container.innerHTML = clusters.map((cluster, cIdx) => {
            const copiesCount = cluster.copies.length;
            const clusterBytes = cluster.copies.reduce((acc, c) => acc + (c.size || 0), 0);
            const totalCopiesInCluster = copiesCount + 1;

            return `
                <div class="dup-cluster-card">
                    <!-- Cluster Header -->
                    <div class="dup-cluster-header">
                        <div style="flex: 1; min-width: 0;">
                            <div style="font-weight: 700; font-size: 0.92rem; color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                                ${window.escapeHtml(cluster.subject)}
                            </div>
                            <div style="font-size: 0.75rem; color: var(--muted); margin-top: 3px;">
                                Sender: <b style="color: var(--text);">${window.escapeHtml(cluster.sender)}</b>
                            </div>
                        </div>
                        <div style="display: flex; align-items: center; gap: 10px; flex-shrink: 0;">
                            <span class="badge" style="background: rgba(0, 120, 212, 0.15); color: var(--accent); border: 1px solid rgba(0, 120, 212, 0.3); font-size: 0.75rem; padding: 3px 8px;">
                                ${totalCopiesInCluster} Copies Found (1 Survivor + ${copiesCount} Redundant)
                            </span>
                            <span style="font-size: 0.78rem; font-family: monospace; font-weight: 700; color: var(--warn);">
                                ${formatSize(clusterBytes)} Reclaimable
                            </span>
                        </div>
                    </div>

                    <!-- Sole Survivor Protected Row -->
                    <div class="dup-survivor-banner">
                        <div style="display: flex; align-items: center; gap: 10px;">
                            <span class="dup-survivor-locked">
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor"><path d="M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm-2 16l-4-4 1.41-1.41L10 14.17l6.59-6.59L18 9l-8 8z"/></svg>
                                SOLE SURVIVOR (PROTECTED)
                            </span>
                            <span style="font-size: 0.78rem; color: var(--text); font-weight: 600;">
                                Preserved in folder: <span class="badge badge-safe" style="font-size: 0.74rem;">📁 ${window.escapeHtml(cluster.survivor.folder || 'Inbox')}</span>
                            </span>
                        </div>
                        <div style="display: flex; align-items: center; gap: 12px;">
                            <span style="font-size: 0.74rem; color: var(--muted); font-style: italic;">
                                🔒 Locked — Never deleted
                            </span>
                            ${cluster.survivor.id ? `
                                <button type="button" class="duplicate-open-btn" onclick="openDuplicateInOutlook('${cluster.survivor.id}')" title="Inspect original email in Outlook">
                                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><polyline points="15 3 21 3 21 9"></polyline><line x1="10" y1="14" x2="21" y2="3"></line></svg>
                                    Open Survivor in Outlook
                                </button>
                            ` : ''}
                        </div>
                    </div>

                    <!-- Redundant Copies Section -->
                    <div style="background: var(--surface-low);">
                        ${cluster.copies.map((copy, cpIdx) => {
                            const isChecked = selectedEntryIds.has(copy.entryId);
                            return `
                                <div class="dup-copy-row">
                                    <div style="display: flex; align-items: center; gap: 12px; flex: 1; min-width: 0;">
                                        <input type="checkbox" class="dup-row-checkbox" data-entryid="${window.escapeHtml(copy.entryId)}" ${isChecked ? 'checked' : ''} onchange="SecurityUI.Duplicates.onCopyCheck('${window.escapeHtml(copy.entryId)}', this.checked)" title="Select this redundant copy for removal" style="width: 16px; height: 16px; accent-color: var(--accent); cursor: pointer;" />
                                        <div>
                                            <span style="font-size: 0.75rem; color: var(--muted); margin-right: 8px;">Copy #${cpIdx + 1}:</span>
                                            <span class="badge" style="background: rgba(255, 170, 68, 0.12); color: var(--warn); border: 1px solid rgba(255, 170, 68, 0.3); font-size: 0.74rem;">
                                                📁 ${window.escapeHtml(copy.folder || 'Mailbox')}
                                            </span>
                                            <span style="font-size: 0.75rem; color: var(--muted); margin-left: 10px; font-family: monospace;">
                                                ${window.escapeHtml(copy.timestamp || '')}
                                            </span>
                                        </div>
                                    </div>
                                    <div style="display: flex; align-items: center; gap: 14px; flex-shrink: 0;">
                                        <span style="font-size: 0.75rem; color: var(--muted); font-family: monospace;">
                                            ${formatSize(copy.size || 0)}
                                        </span>
                                        <button type="button" class="duplicate-open-btn" onclick="openDuplicateInOutlook('${window.escapeHtml(copy.entryId)}')" title="Open this duplicate copy in Outlook">
                                            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><polyline points="15 3 21 3 21 9"></polyline><line x1="10" y1="14" x2="21" y2="3"></line></svg>
                                            Open Copy in Outlook
                                        </button>
                                    </div>
                                </div>
                            `;
                        }).join('')}
                    </div>
                </div>
            `;
        }).join('');

        updateMetrics();
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
            view.innerHTML = `
                <div style="padding: 40px; text-align: center; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%;">
                    <div style="width: 44px; height: 44px; border: 3px solid var(--border); border-top-color: var(--accent); border-radius: 50%; animation: spin 0.8s linear infinite; margin-bottom: 18px;"></div>
                    <div class="stat-value" style="color: var(--accent); font-size: 1.5rem;">${Number(scannedCount).toLocaleString()}</div>
                    <div class="stat-label">Emails Inspected</div>
                    <div style="margin-top: 8px; font-size: 0.85rem; color: var(--warn); font-weight: 600;">
                        ${Number(foundCount).toLocaleString()} Redundant Copies Detected
                    </div>
                    <div style="margin-top: 16px; font-size: 0.85rem; color: var(--muted); line-height: 1.5; max-width: 480px;">
                        ${data.store ? `Inspecting store: <b>${window.escapeHtml(data.store)}</b><br>` : ''}
                        ${data.details ? `<span>${window.escapeHtml(data.details)}</span><br>` : ''}
                    </div>
                    <div style="margin-top: 26px; display: flex; gap: 12px;">
                        <button onclick="window.securityApi.pauseDuplicateScan()">Pause Discovery</button>
                        <button onclick="window.resetDuplicateStack()" style="background: var(--surface-mid); border: 1px solid var(--border);">Cancel &amp; Reset</button>
                    </div>
                </div>
            `;
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
                        <h3 style="margin: 16px 0 8px; font-size: 1.15rem; color: var(--text);">Clean Mailbox &bull; No Duplicates Found</h3>
                        <p style="color: var(--muted); max-width: 440px; font-size: 0.85rem; line-height: 1.6; margin: 0 0 24px;">No redundant or duplicate email copies detected across your inspected Outlook folders.</p>
                        <button class="primary" onclick="window.startDuplicateDiscovery()" style="padding: 8px 20px;">Run Duplicate Scan Again</button>
                    </div>
                `;
                return;
            }

            const totalBytes = items.reduce((acc, i) => acc + (i.size || 0), 0);
            const clusters = buildClusters(items);

            view.innerHTML = `
                <div style="display: flex; flex-direction: column; height: 100%; min-height: 0; padding: 16px;">
                    <!-- Top Action Toolbar -->
                    <div class="section-header" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 14px; flex-shrink: 0; gap: 16px; flex-wrap: wrap; background: var(--surface-low); padding: 12px 18px; border-radius: 8px; border: 1px solid var(--border);">
                        <div>
                            <h2 id="dup-header-title" style="margin: 0; font-size: 1.1rem; color: var(--text);">
                                ${clusters.length.toLocaleString()} Duplicate Groups &bull; ${items.length.toLocaleString()} Redundant Copies &bull; ${formatSize(totalBytes)} Reclaimable
                            </h2>
                            <div id="dup-header-subtitle" style="font-size: 0.75rem; color: var(--muted); margin-top: 3px;">
                                1 authentic copy (Inbox/Mailbox) is permanently preserved per group. Survivors cannot be marked or deleted.
                            </div>
                        </div>
                        <div style="display: flex; gap: 10px; align-items: center; flex-wrap: wrap;">
                            <div style="display: flex; align-items: center; gap: 8px; margin-right: 8px;">
                                <input type="checkbox" id="dup-select-all" checked onchange="SecurityUI.Duplicates.toggleSelectAll(this.checked)" style="width: 16px; height: 16px; accent-color: var(--accent); cursor: pointer;" />
                                <label for="dup-select-all" style="font-size: 0.78rem; font-weight: 600; cursor: pointer; user-select: none;">Select Redundant Copies</label>
                            </div>
                            <div style="position: relative; display: flex; align-items: center; width: 220px;">
                                <input type="text" id="dup-search-input" oninput="SecurityUI.Duplicates.onSearch(this.value)" placeholder="Filter duplicates..." style="width: 100%; padding: 6px 10px; font-size: 0.78rem; background: var(--surface-mid); border: 1px solid var(--border); border-radius: 6px; color: var(--text);" />
                            </div>
                            <button onclick="window.startDuplicateDiscovery()" style="background: var(--surface-mid); border: 1px solid var(--border); font-size: 0.8rem; padding: 6px 14px;">Re-Scan</button>
                            <button id="btn-clean-safe" class="primary" onclick="cleanDuplicatesSafe()" style="font-size: 0.8rem; padding: 6px 14px;">Move to Deleted Items (Safe)</button>
                            <button id="btn-clean-purge" class="danger" onclick="cleanDuplicatesPurge()" style="font-size: 0.8rem; padding: 6px 14px;">Permanent Purge</button>
                        </div>
                    </div>

                    <!-- Duplicate Clusters Container -->
                    <div id="duplicate-clusters-container" style="flex: 1; overflow-y: auto; padding-right: 4px;">
                    </div>
                </div>
            `;

            renderClusterCards();
        } else if (data.status === 'Paused') {
            isScanning = false;
            view.innerHTML = `
                <div style="padding: 40px; text-align: center; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%;">
                    <h3 style="margin: 0 0 10px; font-size: 1.2rem;">Discovery Paused</h3>
                    <p style="color: var(--muted); margin-bottom: 24px; font-size: 0.9rem;">
                        Paused after ${Number(data.scanned || 0).toLocaleString()} emails inspected; ${Number(data.found || (data.items ? data.items.length : 0)).toLocaleString()} duplicate copies found.
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
                <div style="flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%; min-height: 300px; text-align: center; padding: 24px;">
                    <div style="width: 44px; height: 44px; border: 3px solid var(--border); border-top-color: var(--accent); border-radius: 50%; animation: spin 0.8s linear infinite; margin-bottom: 16px;"></div>
                    <div id="dup-progress-title" class="stat-value" style="color: var(--accent); font-size: 1.25rem; margin-bottom: 8px;">Connecting to Outlook MAPI Session...</div>
                    <div id="dup-progress-sub" style="font-size: 0.88rem; color: var(--text); font-weight: 600; margin-bottom: 8px;">
                        Acquiring mailbox session and traversing folder hierarchy...
                    </div>
                    <div id="dup-progress-detail" style="font-size: 0.78rem; color: var(--muted); max-width: 480px; line-height: 1.5; font-family: monospace;">
                        Safeguarding primary Inbox copies • Computing cryptographic DNA hashes
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
            `This will safely move ${targets.length.toLocaleString()} selected redundant email copies to Deleted Items. Pre-flight validation strictly protects the sole survivor copy of each email so it remains safe in your Inbox. Proceed?`,
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
                    let msg = `Cleanup Complete: Successfully isolated ${success.toLocaleString()} redundant copies.`;
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
                        const titleEl = document.getElementById('dup-header-title');
                        const subEl = document.getElementById('dup-header-subtitle');
                        const remCount = duplicateItems.length;
                        const remBytes = duplicateItems.reduce((acc, c) => acc + (c.size || 0), 0);
                        if (titleEl) titleEl.textContent = `Found ${remCount.toLocaleString()} redundant copies across mailboxes (${formatSize(remBytes)})`;
                        if (subEl) subEl.textContent = `Protected sole survivor preserved in original folder • ${remCount.toLocaleString()} duplicates ready for removal`;
                        renderClusterCards();
                        updateMetrics();
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

        window.showConfirm(
            "Permanent Purge",
            `This will permanently purge ${targets.length.toLocaleString()} selected redundant email copies. The authentic sole survivor of each email will NOT be affected. Proceed?`,
            "PURGE",
            async () => {
                const btn = document.getElementById('btn-clean-purge');
                if (btn) {
                    btn.disabled = true;
                    btn.textContent = "Purging...";
                }
                try {
                    const res = await api.deleteDuplicates({ items: targets, mode: 'purge' });
                    const success = (res && res.successCount !== undefined) ? res.successCount : ((res && res.count) || 0);
                    if (typeof window.showNotification === 'function') {
                        window.showNotification(`Purge complete: ${success.toLocaleString()} redundant copies permanently deleted.`);
                    }

                    const targetSet = new Set(targets.map(t => t.entryId));
                    duplicateItems = duplicateItems.filter(i => !targetSet.has(i.entryId));
                    targetSet.forEach(id => selectedEntryIds.delete(id));
                    window.lastDuplicates = duplicateItems.map(i => i.entryId);

                    if (duplicateItems.length === 0) {
                        render({ status: 'Finished', items: [] });
                    } else {
                        const titleEl = document.getElementById('dup-header-title');
                        const subEl = document.getElementById('dup-header-subtitle');
                        const remCount = duplicateItems.length;
                        const remBytes = duplicateItems.reduce((acc, c) => acc + (c.size || 0), 0);
                        if (titleEl) titleEl.textContent = `Found ${remCount.toLocaleString()} redundant copies across mailboxes (${formatSize(remBytes)})`;
                        if (subEl) subEl.textContent = `Protected sole survivor preserved in original folder • ${remCount.toLocaleString()} duplicates ready for removal`;
                        renderClusterCards();
                        updateMetrics();
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
            }
        );
    };

    function onCopyCheck(entryId, checked) {
        if (checked) {
            selectedEntryIds.add(entryId);
        } else {
            selectedEntryIds.delete(entryId);
        }
        updateMetrics();
    }

    function toggleSelectAll(checked) {
        duplicateItems.forEach(i => {
            if (checked) selectedEntryIds.add(i.entryId);
            else selectedEntryIds.delete(i.entryId);
        });
        document.querySelectorAll('.dup-row-checkbox').forEach(cb => {
            cb.checked = checked;
        });
        updateMetrics();
    }

    function onSearch(val) {
        searchQuery = (val || '').trim();
        renderClusterCards();
    }

    if (api && api.onDuplicateUpdate) {
        api.onDuplicateUpdate(data => {
            if (!data) return;
            if (data.status === 'Progress') {
                const titleEl = document.getElementById('dup-progress-title');
                const subEl = document.getElementById('dup-progress-sub');
                const detailEl = document.getElementById('dup-progress-detail');
                if (titleEl) titleEl.textContent = `Scanning Mailboxes... (${(data.scanned || 0).toLocaleString()} emails inspected)`;
                if (subEl) subEl.textContent = `Folder: ${data.currentFolder || 'Root'} • Store: ${data.store || 'Primary'}`;
                if (detailEl && data.details) detailEl.textContent = data.details;
            } else if (data.status === 'Scanned') {
                const titleEl = document.getElementById('dup-progress-title');
                const subEl = document.getElementById('dup-progress-sub');
                if (titleEl) titleEl.textContent = "Clustering & Analyzing Duplicates...";
                if (subEl && data.details) subEl.textContent = data.details;
            } else if (data.status === 'Finished' || data.status === 'Paused') {
                isScanning = false;
                render(data);
            }
        });
    }

    return {
        render: render,
        renderInitialView: renderInitialView,
        onCopyCheck: onCopyCheck,
        toggleSelectAll: toggleSelectAll,
        onSearch: onSearch
    };
})();

window.renderDuplicates = SecurityUI.Duplicates.render;
window.renderDuplicateInitialView = SecurityUI.Duplicates.renderInitialView;
