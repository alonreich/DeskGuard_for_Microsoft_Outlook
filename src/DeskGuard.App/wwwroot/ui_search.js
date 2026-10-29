// Dedicated Forensic Vault Search UI (F4)
// Provides query filtering, date ranges, folder scoping, attachment filter,
// cursor-based infinite scroll, snippet() highlight rendering, and "Open in Outlook".

(function() {
    let currentSearchState = {
        query: '',
        dateFrom: '',
        dateTo: '',
        folder: 'all',
        verdict: 'all',
        hasAttachment: false,
        offset: 0,
        limit: 50,
        loading: false,
        hasMore: true
    };

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    async function executeSearch(reset = true) {
        if (currentSearchState.loading) return;
        if (reset) {
            currentSearchState.offset = 0;
            currentSearchState.hasMore = true;
            const resEl = document.getElementById('vault-search-results');
            if (resEl) resEl.innerHTML = '<div style="color: var(--muted); padding: 20px; text-align: center;">Searching vault...</div>';
        }

        const queryBox = document.getElementById('vault-search-box');
        const dateFromBox = document.getElementById('vault-search-date-from');
        const dateToBox = document.getElementById('vault-search-date-to');
        const folderSel = document.getElementById('vault-search-folder');
        const verdictSel = document.getElementById('vault-search-verdict');
        const attCheckbox = document.getElementById('vault-search-has-attachment');

        if (reset) {
            currentSearchState.query = queryBox ? queryBox.value.trim() : '';
            currentSearchState.dateFrom = dateFromBox ? dateFromBox.value : '';
            currentSearchState.dateTo = dateToBox ? dateToBox.value : '';
            currentSearchState.folder = folderSel ? folderSel.value : 'all';
            currentSearchState.verdict = verdictSel ? verdictSel.value : 'all';
            currentSearchState.hasAttachment = attCheckbox ? attCheckbox.checked : false;
        }

        const params = {
            query: currentSearchState.query,
            dateFrom: currentSearchState.dateFrom,
            dateTo: currentSearchState.dateTo,
            folder: currentSearchState.folder !== 'all' ? currentSearchState.folder : '',
            verdict: currentSearchState.verdict !== 'all' ? currentSearchState.verdict : '',
            hasAttachment: currentSearchState.hasAttachment,
            limit: currentSearchState.limit,
            offset: currentSearchState.offset
        };

        currentSearchState.loading = true;
        try {
            const result = await window.securityApi.searchVault(params);
            const total = (result && (result.total ?? (result.results && result.results.total))) || 0;
            const rows = (result && (result.rows || result.items || (result.results && result.results.rows))) || [];

            const statsEl = document.getElementById('vault-search-stats');
            if (statsEl) {
                statsEl.textContent = `Found ${total} matching email${total === 1 ? '' : 's'} in vault. Showing ${currentSearchState.offset + rows.length} of ${total}.`;
            }

            const resEl = document.getElementById('vault-search-results');
            if (resEl) {
                if (reset) resEl.innerHTML = '';
                if (rows.length === 0 && reset) {
                    resEl.innerHTML = '<div style="color: var(--muted); padding: 30px; text-align: center;">No emails matched the search criteria.</div>';
                    currentSearchState.hasMore = false;
                } else {
                    for (const row of rows) {
                        const card = document.createElement('div');
                        card.className = 'vault-search-item';
                        card.style.cssText = 'background: var(--surface-low); border: 1px solid var(--border); border-radius: 6px; padding: 12px; margin-bottom: 8px; display: flex; flex-direction: column; gap: 6px;';

                        const verdictLower = (row.verdict || 'safe').toLowerCase();
                        const verdictBadgeClass = verdictLower.includes('malicious') ? 'badge-malicious' : (verdictLower.includes('spam') ? 'badge-spam' : 'badge-safe');

                        const snippetHtml = row.snippet ? `<div style="font-size: 0.8rem; color: #a0a0a0; font-family: monospace; background: #000; padding: 6px 10px; border-radius: 4px; border-left: 3px solid var(--accent);">${row.snippet}</div>` : '';

                        card.innerHTML = `
                            <div style="display: flex; justify-content: space-between; align-items: center;">
                                <div style="display: flex; gap: 8px; align-items: center;">
                                    <span class="badge ${verdictBadgeClass}" style="font-size: 0.7rem; padding: 2px 6px;">${escapeHtml(row.verdict || 'Safe')}</span>
                                    <span style="font-weight: 600; font-size: 0.9rem; color: var(--text);">${escapeHtml(row.subject || '(No Subject)')}</span>
                                </div>
                                <div style="display: flex; gap: 10px; align-items: center;">
                                    <span style="font-size: 0.75rem; color: var(--muted);">${escapeHtml(row.date || '')} ${escapeHtml(row.time || '')}</span>
                                    <button class="open-outlook-btn primary" style="font-size: 0.75rem; padding: 4px 10px;">Open in Outlook</button>
                                </div>
                            </div>
                            <div style="display: flex; justify-content: space-between; font-size: 0.75rem; color: var(--muted);">
                                <span>From: <b style="color: var(--text);">${escapeHtml(row.sender || 'Unknown')}</b></span>
                                <span>Folder: <b style="color: var(--accent);">${escapeHtml(row.currentFolder || 'Inbox')}</b></span>
                            </div>
                            ${snippetHtml}
                        `;

                        const openBtn = card.querySelector('.open-outlook-btn');
                        if (openBtn) {
                            openBtn.onclick = async () => {
                                openBtn.disabled = true;
                                openBtn.textContent = 'Opening...';
                                try {
                                    await window.securityApi.openEmail({
                                        entryId: row.entryId || row.id,
                                        storeId: row.storeId || '',
                                        fingerprint: row.fingerprint || '',
                                        messageId: row.messageId || ''
                                    });
                                } catch (e) {}
                                openBtn.disabled = false;
                                openBtn.textContent = 'Open in Outlook';
                            };
                        }

                        resEl.appendChild(card);
                    }

                    currentSearchState.offset += rows.length;
                    if (currentSearchState.offset >= total || rows.length < currentSearchState.limit) {
                        currentSearchState.hasMore = false;
                    }
                }
            }
        } catch (err) {
            console.error('[SearchUI] Search failed:', err);
        } finally {
            currentSearchState.loading = false;
        }
    }

    function initSearchUI() {
        const btn = document.getElementById('vault-tab-search-btn') || document.getElementById('vault-search-btn');
        if (btn) btn.onclick = () => executeSearch(true);

        const queryBox = document.getElementById('vault-search-box');
        if (queryBox) {
            queryBox.onkeydown = (e) => {
                if (e.key === 'Enter') executeSearch(true);
            };
        }

        const resEl = document.getElementById('vault-search-results');
        if (resEl) {
            resEl.onscroll = () => {
                if (resEl.scrollTop + resEl.clientHeight >= resEl.scrollHeight - 50) {
                    if (currentSearchState.hasMore && !currentSearchState.loading) {
                        executeSearch(false);
                    }
                }
            };
        }
    }

    window.renderSearchView = () => {
        executeSearch(true);
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initSearchUI);
    } else {
        initSearchUI();
    }
})();
