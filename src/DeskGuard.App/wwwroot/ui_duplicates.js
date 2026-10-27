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
    let isScanning = false;
    let includeJunk = false;
    window.lastDuplicates = [];
    window.setDuplicateIncludeJunk = (v) => { includeJunk = !!v; };

    window.openDuplicateInOutlook = async (entryId) => {
        if (!entryId) return;
        try {
            if (typeof window.showNotification === 'function') {
                window.showNotification("Opening original email in Microsoft Outlook...");
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
        div.title = 'Double-click to open in Microsoft Outlook';

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

        const actionEl = document.createElement('div');
        actionEl.style.textAlign = 'center';
        const openBtn = document.createElement('button');
        openBtn.className = 'duplicate-open-btn';
        openBtn.textContent = 'Open';
        openBtn.title = 'Open in Microsoft Outlook';
        actionEl.appendChild(openBtn);

        div.appendChild(timeEl);
        div.appendChild(subjEl);
        div.appendChild(senderEl);
        div.appendChild(sizeEl);
        div.appendChild(folderEl);
        div.appendChild(survivorEl);
        div.appendChild(actionEl);

        div._cells = { timeEl, subjEl, senderEl, sizeEl, folderEl, survivorEl, openBtn };

        div.ondblclick = () => {
            const id = div.getAttribute('data-id');
            if (id) window.openDuplicateInOutlook(id);
        };

        openBtn.onclick = (e) => {
            e.stopPropagation();
            const id = div.getAttribute('data-id');
            if (id) window.openDuplicateInOutlook(id);
        };

        return div;
    }

    function updateDuplicateRow(div, item) {
        const cells = div._cells;
        div.setAttribute('data-id', item.entryId || '');

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

        const sz = item.size ? `${(item.size / 1024).toFixed(1)} KB` : '0 KB';
        if (cells.sizeEl.textContent !== sz) cells.sizeEl.textContent = sz;

        const fld = item.folder || 'Mailbox';
        cells.folderEl.innerHTML = `<span class="badge" style="background: rgba(255, 170, 68, 0.12); color: var(--warn); border: 1px solid rgba(255, 170, 68, 0.3); font-size: 0.72rem; max-width: 100%; overflow: hidden; text-overflow: ellipsis; display: inline-block;" title="Duplicate copy in: ${window.escapeHtml(fld)}">${window.escapeHtml(fld)}</span>`;

        const survFld = item.survivorFolder || (item.survivor && item.survivor.folder) || 'Inbox';
        cells.survivorEl.innerHTML = `<span class="badge badge-safe" style="font-size: 0.72rem; display: inline-flex; align-items: center; gap: 4px;" title="Original survivor preserved in: ${window.escapeHtml(survFld)}">🛡️ ${window.escapeHtml(survFld)} (Protected)</span>`;
    }

    function renderInitialView(errorMsg = null) {
        const view = document.getElementById('duplicate-view');
        if (!view) return;
        if (isScanning) return;

        view.innerHTML = `
            <div style="display: flex; flex-direction: column; height: 100%; min-height: 0; box-sizing: border-box;">
                ${errorMsg ? `
                    <div style="margin-bottom: 16px; padding: 12px 16px; background: rgba(241, 112, 123, 0.15); border: 1px solid var(--danger); border-radius: 6px; color: var(--danger); font-size: 0.85rem; display: flex; align-items: center; justify-content: space-between;">
                        <span>${window.escapeHtml(errorMsg)}</span>
                        <button onclick="window.renderDuplicateInitialView()" style="background: transparent; border: none; color: var(--danger); cursor: pointer; font-weight: bold; font-size: 1rem;">✕</button>
                    </div>
                ` : ''}

                <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 16px; margin-bottom: 24px; flex-shrink: 0;">
                    <div class="stat-card" style="padding: 16px;">
                        <div class="stat-label">How copies are matched</div>
                        <div class="stat-value" style="color: var(--accent); font-size: 1.15rem;">Same Message-ID</div>
                        <div style="font-size: 0.75rem; color: var(--muted); margin-top: 6px;">Checked across MAPI stores & local vault</div>
                    </div>
                    <div class="stat-card" style="padding: 16px;">
                        <div class="stat-label">Survivor Protection</div>
                        <div class="stat-value" style="color: var(--ok, #4ade80); font-size: 1.15rem;">Pre-Flight Verified</div>
                        <div style="font-size: 0.75rem; color: var(--muted); margin-top: 6px;">Survivors validated via MAPI before action</div>
                    </div>
                    <div class="stat-card" style="padding: 16px;">
                        <div class="stat-label">Coverage Scope</div>
                        <div class="stat-value" style="color: var(--text); font-size: 1.15rem;">All Mailboxes</div>
                        <div style="font-size: 0.75rem; color: var(--muted); margin-top: 6px;">Primary, Sent Items, Archives & Subfolders</div>
                    </div>
                </div>

                <div style="flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; background: var(--surface-low); border: 1px dashed var(--border); border-radius: 8px; padding: 36px 24px; text-align: center;">
                    <div style="width: 56px; height: 56px; border-radius: 12px; background: rgba(0, 120, 212, 0.12); display: flex; align-items: center; justify-content: center; margin-bottom: 18px; color: var(--accent);">
                        <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor">
                            <path d="M15,9H5V5H15M12,19A7,7 0 1,1 19,12A7,7 0 0,1 12,19M12,3A9,9 0 0,0 3,12V21H12A9,9 0 0,0 21,12A9,9 0 0,0 12,3Z"/>
                        </svg>
                    </div>
                    <h3 style="margin: 0 0 8px; font-size: 1.15rem; color: var(--text); font-weight: 600;">Mailbox Redundancy Discovery</h3>
                    <p style="color: var(--muted); max-width: 540px; font-size: 0.85rem; line-height: 1.6; margin: 0 0 24px;">
                        Scan your Outlook stores to detect identical email copies cluttering your mailbox. Candidate duplicates are coupled with their definitive survivors so you can safely isolate or purge redundant items without risking original emails. Double-click any row to jump to the original email in Outlook.
                    </p>
                    <label style="display: inline-flex; align-items: center; gap: 8px; font-size: 0.85rem; color: var(--muted); margin-bottom: 16px;" title="Junk Email copies are usually not worth keeping, so they are left out unless you tick this.">
                        <input type="checkbox" id="dup-include-junk" ${includeJunk ? 'checked' : ''} onchange="window.setDuplicateIncludeJunk(this.checked)">
                        Also look in Junk Email
                    </label>
                    <div style="display: flex; gap: 12px; flex-wrap: wrap; justify-content: center;">
                        <button class="primary" onclick="window.startDuplicateDiscovery()" style="padding: 10px 24px; font-weight: 600; font-size: 0.9rem; display: inline-flex; align-items: center; gap: 8px;">
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <circle cx="11" cy="11" r="8"></circle>
                                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
                            </svg>
                            Start Duplicate Discovery Scan
                        </button>
                        <button onclick="window.resetDuplicateStack()" style="padding: 10px 18px; font-size: 0.85rem; background: var(--surface-mid); border: 1px solid var(--border); color: var(--text);">
                            Reset Engine Stack
                        </button>
                    </div>
                </div>
            </div>
        `;
    }

    function render(data) {
        const view = document.getElementById('duplicate-view');
        if (!data || !view) return;

        if (data.status === 'Scanned' || data.status === 'Progress' || data.status === 'Found' || data.status === 'StoreStart') {
            isScanning = true;
            const scannedCount = data.scanned !== undefined ? data.scanned : (data.current || 0);
            const html = `
                <div style="padding: 40px; text-align: center; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%;">
                    <div style="width: 42px; height: 42px; border: 3px solid var(--border); border-top-color: var(--accent); border-radius: 50%; animation: spin 0.8s linear infinite; margin-bottom: 16px;"></div>
                    <div class="stat-value" style="color: var(--accent);">${scannedCount}</div>
                    <div class="stat-label">Total Items Scanned</div>
                    <div style="margin-top: 20px; font-size: 0.9rem; color: var(--muted); line-height: 1.6; max-width: 500px;">
                        ${data.store ? `Scanning: <b>${window.escapeHtml(data.store)}</b> / ${window.escapeHtml(data.currentFolder || data.folder || '')}<br>` : ''}
                        ${data.details ? `<span>${window.escapeHtml(data.details)}</span><br>` : ''}
                    </div>
                    <div style="margin-top: 30px; display: flex; gap: 12px;">
                        <button onclick="window.securityApi.pauseDuplicateScan()">Pause Discovery</button>
                        <button onclick="window.resetDuplicateStack()" style="background: var(--surface-mid); border: 1px solid var(--border);">Cancel & Reset</button>
                    </div>
                </div>
            `;
            if (view.innerHTML !== html) view.innerHTML = html;
        } else if (data.status === 'Finished') {
            isScanning = false;
            const items = data.items || [];
            duplicateItems = items;
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

            view.innerHTML = `
                <div class="section-header" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; flex-shrink: 0;">
                    <div>
                        <h2 style="margin: 0; font-size: 1.1rem;">${items.length} Redundant Emails Found</h2>
                        <div style="font-size: 0.74rem; color: var(--muted); margin-top: 3px;">
                            Double-click any row to open the original email in Outlook. The survivor copy is preserved untouched.
                        </div>
                    </div>
                    <div style="display: flex; gap: 10px;">
                        <button onclick="window.startDuplicateDiscovery()" style="background: var(--surface-mid); border: 1px solid var(--border);">Re-Scan</button>
                        <button id="btn-clean-safe" class="primary" onclick="cleanDuplicatesSafe()" ${items.length === 0 ? 'disabled' : ''}>Move to Deleted Items (Safe)</button>
                        <button id="btn-clean-purge" class="danger" onclick="cleanDuplicatesPurge()" ${items.length === 0 ? 'disabled' : ''}>Permanent Purge</button>
                    </div>
                </div>
                <div class="list-header duplicate-grid" style="flex-shrink: 0;">
                    <div>Date Received</div>
                    <div>Subject</div>
                    <div>Sender</div>
                    <div>Size</div>
                    <div>Duplicate Location</div>
                    <div>Survivor Protection</div>
                    <div style="text-align: center;">Action</div>
                </div>
                <div id="duplicate-list" class="list-container" style="flex: 1; min-height: 0; position: relative; overflow-y: auto;">
                </div>
            `;

            const container = document.getElementById('duplicate-list');
            if (container && window.VirtualList) {
                duplicateVirtualList = new window.VirtualList({
                    container: container,
                    rowHeight: 44,
                    buffer: 5,
                    createRow: createDuplicateRow,
                    updateRow: updateDuplicateRow,
                    emptyRenderer: () => `
                        <div class="empty-state">
                            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--border)" stroke-width="1.5"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>
                            <h3>Clean Mailbox</h3>
                            <p>No redundant or duplicate email copies detected.</p>
                        </div>`
                });
                duplicateVirtualList.setItems(items, true);
            }
        } else if (data.status === 'Paused') {
            isScanning = false;
            if (Array.isArray(data.items)) duplicateItems = data.items;
            view.innerHTML = `
                <div style="padding: 40px; text-align: center; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%;">
                    <h3>Discovery Paused</h3>
                    <p style="color: var(--muted); margin-bottom: 24px;">Paused after ${Number(data.scanned || 0).toLocaleString()} emails; ${Number(data.found || 0).toLocaleString()} duplicate copies found so far. Resume continues from the same email.</p>
                    <div style="display: flex; gap: 12px;">
                        <button class="primary" onclick="window.securityApi.resumeDuplicateScan()">Resume Discovery</button>
                        <button onclick="window.resetDuplicateStack()" style="background: var(--surface-mid); border: 1px solid var(--border);">Reset Engine</button>
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
                    <div style="width: 38px; height: 38px; border: 3px solid var(--border); border-top-color: var(--accent); border-radius: 50%; animation: spin 0.8s linear infinite; margin-bottom: 16px;"></div>
                    <div class="stat-value" style="color: var(--accent); font-size: 1.2rem; margin-bottom: 8px;">Initializing Mailbox Crawler...</div>
                    <div style="font-size: 0.85rem; color: var(--muted); max-width: 460px; line-height: 1.5;">
                        Querying Outlook MAPI session, acquiring store handles, and preparing recursive folder traversal...
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
        window.lastDuplicates = [];
        renderInitialView();
    };

    window.cleanDuplicatesSafe = async () => {
        if (!duplicateItems || duplicateItems.length === 0) return;
        const confirmFn = window.showConfirmModal || ((title, msg, fn) => {
            if (window.showConfirm) window.showConfirm(title, msg, fn);
        });

        confirmFn(
            "Move Duplicates to Deleted Items",
            `This will safely move ${duplicateItems.length} redundant email copies to Deleted Items. Pre-flight validation will ensure that original survivor emails remain untouched in their current folders. Proceed?`,
            async () => {
                const btn = document.getElementById('btn-clean-safe');
                if (btn) {
                    btn.disabled = true;
                    btn.textContent = "Processing...";
                }
                try {
                    const res = await api.deleteDuplicates({ items: duplicateItems, mode: 'safe' });
                    const success = (res && res.successCount !== undefined) ? res.successCount : ((res && res.count) || 0);
                    const skipped = (res && res.skippedCount) || 0;
                    let msg = `Clean-up Complete: Successfully isolated ${success} redundant copies.`;
                    if (skipped > 0) {
                        msg += ` ${skipped} items protected (survivors missing or modified).`;
                    }
                    if (typeof window.showNotification === 'function') {
                        window.showNotification(msg, false);
                    }
                    window.lastDuplicates = [];
                    duplicateItems = [];
                    if (duplicateVirtualList) duplicateVirtualList.setItems([], true);
                    render({ status: 'Finished', items: [] });
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
        if (!duplicateItems || duplicateItems.length === 0) return;
        const confirmFn = window.showConfirmModal || ((title, msg, fn, kw) => {
            if (window.showConfirm) window.showConfirm(title, msg, kw, fn);
        });

        confirmFn(
            "Permanent Purge of Duplicates",
            `WARNING: This will permanently delete ${duplicateItems.length} redundant email copies from Outlook. Pre-flight validation will strictly verify that each original survivor exists before any duplicate is deleted. Type PURGE to confirm.`,
            async () => {
                const btn = document.getElementById('btn-clean-purge');
                if (btn) {
                    btn.disabled = true;
                    btn.textContent = "Purging...";
                }
                try {
                    const res = await api.deleteDuplicates({ items: duplicateItems, mode: 'purge' });
                    const success = (res && res.successCount !== undefined) ? res.successCount : ((res && res.count) || 0);
                    const skipped = (res && res.skippedCount) || 0;
                    let msg = `Permanent Purge Complete: Successfully deleted ${success} redundant copies.`;
                    if (skipped > 0) {
                        msg += ` ${skipped} items protected (survivors missing or modified).`;
                    }
                    if (typeof window.showNotification === 'function') {
                        window.showNotification(msg, false);
                    }
                    window.lastDuplicates = [];
                    duplicateItems = [];
                    if (duplicateVirtualList) duplicateVirtualList.setItems([], true);
                    render({ status: 'Finished', items: [] });
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

    // Shows the last saved scan result (kept in the vault) instead of an empty screen after a restart.
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
