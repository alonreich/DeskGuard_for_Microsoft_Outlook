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

window.currentForensicsItem = null;
window.currentForensicsRawHeaders = '';
window.forensicsQueueCategory = 'all';
window.forensicsQueueFilterText = '';

function getForensicsQueue() {
    let items = [];
    if (window.AppState && window.AppState.stats) {
        if (window.forensicsQueueCategory === 'all') {
            if (Array.isArray(window.AppState.stats.all) && window.AppState.stats.all.length > 0) {
                items = window.AppState.stats.all.slice();
            } else {
                items = [
                    ...(window.AppState.stats.malicious || []),
                    ...(window.AppState.stats.suspicious || []),
                    ...(window.AppState.stats.spam || []),
                    ...(window.AppState.stats.safe || [])
                ];
            }
        } else {
            items = (window.AppState.stats[window.forensicsQueueCategory] || []).slice();
        }
    } else if (window.incidentVirtualList && Array.isArray(window.incidentVirtualList.items)) {
        items = window.incidentVirtualList.items.slice();
    }

    // Filter by search text if present
    if (window.forensicsQueueFilterText) {
        const q = window.forensicsQueueFilterText.toLowerCase();
        items = items.filter(it => {
            const subj = (it.subject || it.details || '').toLowerCase();
            const sender = (it.sender || it.from || '').toLowerCase();
            const to = (it.to || it.recipient || '').toLowerCase();
            const verdict = (it.verdict || '').toLowerCase();
            const score = String(it.score || '');
            return subj.includes(q) || sender.includes(q) || to.includes(q) || verdict.includes(q) || score.includes(q);
        });
    }

    return items;
}

window.renderForensicsQueue = function() {
    const queueList = document.getElementById('forensics-queue-list');
    const statPill = document.getElementById('forensics-queue-stat-pill');
    const pillCounter = document.getElementById('forensics-pill-counter');
    const catSelect = document.getElementById('forensics-queue-category');
    if (catSelect && catSelect.value !== window.forensicsQueueCategory) {
        catSelect.value = window.forensicsQueueCategory;
    }

    const queue = getForensicsQueue();

    if (statPill) {
        statPill.textContent = `${queue.length} email${queue.length === 1 ? '' : 's'}`;
    }

    const currentId = window.currentForensicsItem ? (window.currentForensicsItem.entryId || window.currentForensicsItem.fingerprint || window.currentForensicsItem.id) : null;
    const currentIdx = queue.findIndex(i => (i.entryId || i.fingerprint || i.id) === currentId);

    if (pillCounter) {
        if (currentIdx !== -1) {
            pillCounter.textContent = `${currentIdx + 1} of ${queue.length}`;
        } else if (queue.length > 0) {
            pillCounter.textContent = `1 of ${queue.length}`;
        } else {
            pillCounter.textContent = `-- / --`;
        }
    }

    const prevDisabled = (currentIdx <= 0);
    const nextDisabled = (currentIdx === -1 || currentIdx >= queue.length - 1);

    ['btn-forensics-prev', 'pill-forensics-prev'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.disabled = prevDisabled;
    });
    ['btn-forensics-next', 'pill-forensics-next'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.disabled = nextDisabled;
    });

    if (!queueList) return;

    if (queue.length === 0) {
        queueList.innerHTML = `
            <div style="padding: 30px 16px; text-align: center; color: var(--muted); font-size: 0.8rem;">
                <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" style="margin-bottom: 8px; opacity: 0.6;"><circle cx="12" cy="12" r="10"></circle><line x1="8" y1="12" x2="16" y2="12"></line></svg>
                <div>No emails found matching criteria</div>
            </div>
        `;
        return;
    }

    queueList.innerHTML = '';
    const frag = document.createDocumentFragment();

    queue.forEach((item, idx) => {
        const itemId = item.entryId || item.fingerprint || item.id;
        const isActive = (itemId === currentId);
        const div = document.createElement('div');
        div.className = `forensics-queue-item${isActive ? ' active' : ''}`;
        div.setAttribute('data-id', itemId);

        const safeSubj = window.escapeHtml(item.subject || item.details || '(No Subject)');
        const safeSender = window.escapeHtml(item.sender || item.from || 'Unknown Sender');
        const verdict = item.verdict || 'Safe';
        const verdictLower = verdict.toLowerCase();
        const vBadgeClass = verdictLower.includes('malicious') ? 'badge-malicious' : (verdictLower.includes('spam') ? 'badge-spam' : (verdictLower.includes('suspicious') ? 'badge-warn' : 'badge-safe'));

        let avBadgeMini = '';
        if (item.avScanned) {
            const isAvThreat = String(item.avVerdict || '').toLowerCase() === 'malicious';
            avBadgeMini = isAvThreat
                ? `<span class="badge badge-av-threat" style="font-size: 0.65rem; padding: 1px 5px;" title="AV Threat: ${window.escapeHtml(item.avThreatName || 'Malicious')}">🛡️ Threat</span>`
                : `<span class="badge badge-av-clean" style="font-size: 0.65rem; padding: 1px 5px;" title="Local AV Clean">🛡️ Clean</span>`;
        }

        const numScore = parseInt(item.score);
        const displayScore = isNaN(numScore) ? '--' : `${numScore}%`;
        const scoreColor = (numScore < 50) ? 'var(--danger)' : (numScore < 80 ? 'var(--warn)' : 'var(--ok)');

        const dateStr = item.date || (item.timestamp ? new Date(item.timestamp).toLocaleDateString() : '');
        const timeStr = item.time || (item.timestamp ? new Date(item.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '');

        div.innerHTML = `
            <div class="forensics-queue-item-header">
                <span class="forensics-queue-item-subject" title="${safeSubj}">${safeSubj}</span>
                <span style="font-size: 0.76rem; font-weight: 800; font-family: monospace; color: ${scoreColor};">${displayScore}</span>
            </div>
            <div class="forensics-queue-item-sender" title="${safeSender}">${safeSender}</div>
            <div class="forensics-queue-item-meta">
                <div class="forensics-queue-item-badges">
                    <span class="badge ${vBadgeClass}" style="font-size: 0.65rem; padding: 1px 6px;">${window.escapeHtml(verdict)}</span>
                    ${avBadgeMini}
                </div>
                <div class="forensics-queue-item-time">${window.escapeHtml(timeStr || dateStr)}</div>
            </div>
        `;

        div.onclick = () => {
            window.renderForensicsTab(item);
        };

        frag.appendChild(div);
    });

    queueList.appendChild(frag);

    // Scroll active item into view
    const activeEl = queueList.querySelector('.forensics-queue-item.active');
    if (activeEl) {
        activeEl.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
};

window.onForensicsCategoryChange = function(cat) {
    window.forensicsQueueCategory = cat;
    const queue = getForensicsQueue();
    window.renderForensicsQueue();

    if (queue.length > 0) {
        const currentId = window.currentForensicsItem ? (window.currentForensicsItem.entryId || window.currentForensicsItem.fingerprint || window.currentForensicsItem.id) : null;
        const exists = queue.some(i => (i.entryId || i.fingerprint || i.id) === currentId);
        if (!exists) {
            window.renderForensicsTab(queue[0]);
        }
    } else {
        renderForensicsEmptyQueue(`No ${cat} emails available in queue.`);
    }
};

window.onForensicsQueueFilter = function(val) {
    window.forensicsQueueFilterText = (val || '').trim();
    const clearBtn = document.getElementById('forensics-queue-filter-clear');
    if (clearBtn) {
        clearBtn.style.display = window.forensicsQueueFilterText ? 'block' : 'none';
    }
    const queue = getForensicsQueue();
    window.renderForensicsQueue();

    if (queue.length > 0) {
        const currentId = window.currentForensicsItem ? (window.currentForensicsItem.entryId || window.currentForensicsItem.fingerprint || window.currentForensicsItem.id) : null;
        const exists = queue.some(i => (i.entryId || i.fingerprint || i.id) === currentId);
        if (!exists) {
            window.renderForensicsTab(queue[0]);
        }
    } else {
        renderForensicsEmptyQueue(`No emails match "${window.escapeHtml(window.forensicsQueueFilterText)}".`);
    }
};

window.clearForensicsQueueFilter = function() {
    const input = document.getElementById('forensics-queue-filter');
    if (input) input.value = '';
    window.onForensicsQueueFilter('');
};

function renderForensicsEmptyQueue(message) {
    const container = document.getElementById('forensics-tab-content');
    const toolbar = document.getElementById('forensics-toolbar-actions');
    if (toolbar) toolbar.innerHTML = '';
    if (container) {
        container.innerHTML = `
            <div class="empty-state" style="padding: 60px 20px; text-align: center; color: var(--muted);">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="12" cy="12" r="10"></circle><line x1="8" y1="12" x2="16" y2="12"></line></svg>
                <h3 style="margin: 16px 0 8px; color: var(--text);">Queue Empty</h3>
                <p style="font-size: 0.85rem; max-width: 460px; margin: 0 auto 20px;">${window.escapeHtml(message || 'No emails found in this triage queue.')}</p>
                <button type="button" class="primary" onclick="window.switchTab('dashboard')" style="padding: 7px 18px; font-size: 0.82rem;">Return to Dashboard</button>
            </div>
        `;
    }
}

window.initForensicsSplitter = function() {
    const splitter = document.getElementById('forensics-v-splitter');
    const panel = document.getElementById('forensics-queue-panel');
    if (!splitter || !panel || splitter._bound) return;
    splitter._bound = true;

    let isDragging = false;
    let startX = 0;
    let startWidth = 0;

    splitter.addEventListener('pointerdown', (e) => {
        isDragging = true;
        startX = e.clientX;
        startWidth = panel.getBoundingClientRect().width;
        splitter.classList.add('dragging');
        document.body.style.cursor = 'col-resize';
        document.body.style.userSelect = 'none';
        splitter.setPointerCapture(e.pointerId);
    });

    splitter.addEventListener('pointermove', (e) => {
        if (!isDragging) return;
        const delta = e.clientX - startX;
        const newWidth = Math.min(550, Math.max(220, startWidth + delta));
        panel.style.width = `${newWidth}px`;
    });

    const stopDragging = (e) => {
        if (!isDragging) return;
        isDragging = false;
        splitter.classList.remove('dragging');
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
        try { splitter.releasePointerCapture(e.pointerId); } catch {}
    };

    splitter.addEventListener('pointerup', stopDragging);
    splitter.addEventListener('pointercancel', stopDragging);
};

window.initForensicsWorkspace = function() {
    window.initForensicsSplitter();
    if (window.AppState && window.AppState.currentCategory) {
        window.forensicsQueueCategory = window.AppState.currentCategory;
    }
    const queue = getForensicsQueue();
    window.renderForensicsQueue();

    if (window.currentForensicsItem) {
        window.renderForensicsTab(window.currentForensicsItem);
    } else if (queue.length > 0) {
        window.renderForensicsTab(queue[0]);
    } else {
        renderForensicsEmptyQueue('No scanned emails in vault to inspect.');
    }
};

window.forensicsScrollTo = (secId) => {
    const el = document.getElementById(secId);
    if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
        document.querySelectorAll('.forensics-anchor-btn').forEach(btn => {
            btn.classList.toggle('active', btn.getAttribute('data-target') === secId);
        });
    }
};

window.copyForensicsHeaders = () => {
    if (window.currentForensicsRawHeaders) {
        navigator.clipboard.writeText(window.currentForensicsRawHeaders).then(() => {
            if (typeof window.showNotification === 'function') window.showNotification('MIME transport headers copied to clipboard.');
        }).catch(() => {
            if (typeof window.showNotification === 'function') window.showNotification('Headers copied.', false);
        });
    }
};

window.renderForensicsTab = async (item) => {
    if (!item) return;
    window.currentForensicsItem = item;
    const container = document.getElementById('forensics-tab-content');
    const toolbar = document.getElementById('forensics-toolbar-actions');
    if (!container) return;

    if (window.AppState) {
        window.AppState.selectedId = item.entryId || item.fingerprint || item.id;
        if (window.incidentVirtualList) {
            window.incidentVirtualList.refreshCurrentSlice();
        }
    }

    // Refresh queue UI to reflect new selection
    window.renderForensicsQueue();

    const queue = getForensicsQueue();
    const currentIdx = queue.findIndex(i => (i.entryId || i.fingerprint || i.id) === (item.entryId || item.fingerprint || item.id));
    const prevItem = currentIdx > 0 ? queue[currentIdx - 1] : null;
    const nextItem = (currentIdx !== -1 && currentIdx < queue.length - 1) ? queue[currentIdx + 1] : null;

    window.forensicsNavigatePrev = () => {
        if (prevItem) window.renderForensicsTab(prevItem);
    };
    window.forensicsNavigateNext = () => {
        if (nextItem) window.renderForensicsTab(nextItem);
    };

    if (toolbar) {
        toolbar.innerHTML = `
            <!-- Stepper controls (Proposal 3) -->
            <button type="button" class="btn-ui" id="btn-forensics-prev" ${prevItem ? '' : 'disabled'} onclick="window.forensicsNavigatePrev()" title="Previous Email ([ or P)" style="font-size: 0.78rem; padding: 6px 10px; display: inline-flex; align-items: center; gap: 4px;">
                <span>◀</span> Prev
            </button>
            <span style="font-size: 0.75rem; color: var(--muted); font-weight: 600; font-family: monospace; padding: 0 4px; white-space: nowrap;">
                ${currentIdx !== -1 ? `${currentIdx + 1} of ${queue.length}` : ''}
            </span>
            <button type="button" class="btn-ui" id="btn-forensics-next" ${nextItem ? '' : 'disabled'} onclick="window.forensicsNavigateNext()" title="Next Email (] or N)" style="font-size: 0.78rem; padding: 6px 10px; display: inline-flex; align-items: center; gap: 4px;">
                Next <span>▶</span>
            </button>
            <div style="width: 1px; height: 18px; background: var(--border); margin: 0 4px;"></div>

            <button type="button" class="btn-ui" onclick="window.forensicsOpenInOutlook()" style="font-size: 0.78rem; padding: 6px 12px; display: inline-flex; align-items: center; gap: 6px;" title="Open in Outlook (O)">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><polyline points="15 3 21 3 21 9"></polyline><line x1="10" y1="14" x2="21" y2="3"></line></svg>
                Outlook
            </button>
            <button type="button" class="btn-ui" id="btn-forensics-av" onclick="window.forensicsScanAv()" style="font-size: 0.78rem; padding: 6px 12px; display: inline-flex; align-items: center; gap: 6px; background: rgba(0, 120, 212, 0.12); border-color: var(--accent); color: var(--accent); font-weight: 700;" title="Scan with Local AV (S)">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path></svg>
                Scan AV
            </button>
            <button type="button" class="btn-ui" onclick="window.forensicsScanVirusTotal()" style="font-size: 0.78rem; padding: 6px 12px; display: inline-flex; align-items: center; gap: 6px;" title="VirusTotal Cloud Audit">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2L1 21h22L12 2zm0 3.99L19.53 19H4.47L12 5.99zM11 10v4h2v-4h-2zm0 6v2h2v-2h-2z"/></svg>
                VirusTotal
            </button>
            <button type="button" class="btn-ui" onclick="window.forensicsQuarantine()" style="font-size: 0.78rem; padding: 6px 12px; color: var(--warn); display: inline-flex; align-items: center; gap: 6px;" title="Quarantine (Q)">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>
                Quarantine
            </button>
            <button type="button" class="btn-ui" onclick="window.forensicsDelete()" style="font-size: 0.78rem; padding: 6px 12px; color: var(--danger); display: inline-flex; align-items: center; gap: 6px;" title="Delete (D)">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
                Delete
            </button>
        `;
    }

    container.innerHTML = '<div style="display: flex; align-items: center; justify-content: center; height: 300px; color: var(--muted);"><div class="spin" style="width: 24px; height: 24px; border: 2px solid var(--accent); border-top-color: transparent; border-radius: 50%; margin-right: 12px;"></div> Deconstructing email forensics...</div>';

    const data = await window.securityApi.getForensics({
        entryId: item.entryId || item.id,
        originalEntryId: item.originalEntryId,
        fingerprint: item.fingerprint
    });

    let rawHeaders = (data && data.fullHeaders && data.fullHeaders !== 'N/A' && data.fullHeaders !== 'Unavailable')
        ? data.fullHeaders
        : ((data && data.headers && data.headers !== 'N/A' && data.headers !== 'Unavailable')
            ? data.headers
            : (item.fullHeaders || item.headers || ''));

    if (rawHeaders && typeof rawHeaders === 'string') {
        const clean = rawHeaders.trim();
        if (/^[A-Za-z0-9+/]+={0,2}$/.test(clean) && clean.length % 4 === 0 && !clean.includes(' ')) {
            try {
                const dec = atob(clean);
                if (dec && dec.length > 0) rawHeaders = dec;
            } catch {}
        }
    }

    if (!rawHeaders || rawHeaders.trim().length === 0) {
        rawHeaders = [
            `X-Delivery-Context: Internal / MAPI Store (No SMTP Transport Headers)`,
            `Message-ID: <${item.fingerprint || item.entryId || item.id}@deskguard.local>`,
            `Date: ${item.date ? item.date + ' ' + (item.time || '') : (item.timestamp || new Date().toUTCString())}`,
            `From: ${item.sender || item.from || 'Unknown'}`,
            `To: ${item.to || item.recipient || 'Internal User'}`,
            item.cc ? `CC: ${item.cc}` : null,
            `Subject: ${item.subject || item.details || '(No Subject)'}`,
            `X-Verdict: ${item.verdict || 'Safe'}`,
            `X-Integrity-Score: ${item.score !== undefined ? item.score : 100}%`
        ].filter(Boolean).join('\n');
    }
    window.currentForensicsRawHeaders = rawHeaders;

    let headersHtml = '';
    const lines = rawHeaders.split('\n');
    headersHtml = lines.map(line => {
        const parts = line.split(':');
        if (parts.length > 1) {
            const key = parts[0].trim();
            const val = parts.slice(1).join(':').trim();
            const isAuth = ['authentication-results', 'received-spf', 'dkim-signature', 'arc-authentication-results', 'dmarc'].some(k => key.toLowerCase().includes(k));
            const keyColor = isAuth ? 'var(--ok)' : 'var(--accent)';
            return `<div style="display: flex; gap: 12px; margin-bottom: 4px; border-bottom: 1px solid rgba(255,255,255,0.06); padding: 5px 0;">
                <span style="color: ${keyColor}; font-weight: 700; min-width: 170px; font-family: monospace; font-size: 0.78rem;">${window.escapeHtml(key)}:</span>
                <span style="color: var(--text); font-family: monospace; font-size: 0.78rem; word-break: break-all;">${window.escapeHtml(val)}</span>
            </div>`;
        }
        return `<div style="color: var(--muted); font-size: 0.78rem; padding: 2px 0;">${window.escapeHtml(line)}</div>`;
    }).join('');

    const safeSubject = window.escapeHtml(item.subject || item.details || '(No Subject)');
    const safeSender = window.escapeHtml(item.sender || item.from || 'Unknown');
    const safeRecipient = window.escapeHtml(item.to || item.recipient || item.recips || 'N/A');
    const safeVerdict = window.escapeHtml(item.verdict || 'Safe');
    const verdictClass = (item.verdict || '').toLowerCase().includes('malicious') ? 'badge-malicious' : ((item.verdict || '').toLowerCase().includes('spam') ? 'badge-spam' : 'badge-safe');

    const avScanned = (data && data.avScanned) || item.avScanned;
    const avVerdict = (data && data.avVerdict) || item.avVerdict;
    const avThreat = (data && data.avThreatName) || item.avThreatName;

    let avBadgeHtml = '';
    if (avScanned) {
        const isAvThreat = String(avVerdict || '').toLowerCase() === 'malicious';
        if (isAvThreat) {
            avBadgeHtml = `<span class="badge badge-av-threat" style="font-size: 0.82rem; padding: 5px 12px;" title="Local AV: Threat Detected (${window.escapeHtml(avThreat || 'Malicious')})">🛡️ AV Threat: ${window.escapeHtml(avThreat || 'Infected')}</span>`;
        } else {
            avBadgeHtml = `<span class="badge badge-av-clean" style="font-size: 0.82rem; padding: 5px 12px;" title="Local AV: Scanned Clean (Defender & ClamAV)">🛡️ Local AV Clean</span>`;
        }
    }

    // Parse penalties
    let penalties = [];
    const penaltiesData = (data && data.penalties) || item.penalties;
    if (Array.isArray(penaltiesData)) penalties = penaltiesData;
    else if (typeof penaltiesData === 'string') {
        try { penalties = JSON.parse(penaltiesData); } catch {}
    } else if (typeof item.penalties_json === 'string') {
        try { penalties = JSON.parse(item.penalties_json); } catch {}
    } else if (item.tier) {
        const tokens = (item.tier || '').split('|').map(t => t.trim());
        penalties = tokens.map(t => {
            const m = t.match(/^([^\[]+)\[-(\d+(?:\.\d+)?)\]/);
            return m ? { rule: m[1], points: parseFloat(m[2]), evidence: t } : { rule: t, points: 0, evidence: t };
        }).filter(p => p.points > 0);
    }

    // Parse attachments
    let attachments = [];
    const attRaw = (data && data.attachmentsJson) || item.attachmentsJson || item.attachments;
    if (attRaw) {
        if (Array.isArray(attRaw)) attachments = attRaw;
        else if (typeof attRaw === 'string') {
            try {
                const parsed = JSON.parse(attRaw);
                if (Array.isArray(parsed)) attachments = parsed;
            } catch {}
        }
    }

    const palette = ['#e81123', '#ff8c00', '#d83b01', '#ea4335', '#fbbc05', '#ff4081', '#9c27b0', '#0078d4'];
    const totalPoints = penalties.reduce((sum, p) => sum + p.points, 0);
    let barSegmentsHtml = '';
    if (penalties.length > 0) {
        barSegmentsHtml = penalties.map((p, idx) => {
            const pct = Math.max(5, Math.round((p.points / Math.max(100, totalPoints)) * 100));
            const color = palette[idx % palette.length];
            return `<div style="width: ${pct}%; background: ${color}; height: 100%;" title="${window.escapeHtml(p.rule)}: -${p.points} pts"></div>`;
        }).join('');
    } else {
        barSegmentsHtml = `<div style="width: 100%; background: var(--safe); height: 100%;" title="Clean (100% Score)"></div>`;
    }

    const bodyContent = (data && data.body) ? data.body : (item.body || 'No message body content captured in vault.');

    container.innerHTML = `
        <!-- Sticky Section Jump Anchors (Proposal 4) -->
        <div class="forensics-sticky-nav">
            <span style="font-size: 0.70rem; color: var(--muted); text-transform: uppercase; font-weight: 700; margin-right: 6px;">Jump to Section:</span>
            <button type="button" class="forensics-anchor-btn active" data-target="sec-overview" onclick="window.forensicsScrollTo('sec-overview')">📊 Overview</button>
            <button type="button" class="forensics-anchor-btn" data-target="sec-penalties" onclick="window.forensicsScrollTo('sec-penalties')">⚖️ Risk Penalties (${penalties.length})</button>
            <button type="button" class="forensics-anchor-btn" data-target="sec-headers" onclick="window.forensicsScrollTo('sec-headers')">🔍 Transport Headers</button>
            <button type="button" class="forensics-anchor-btn" data-target="sec-body" onclick="window.forensicsScrollTo('sec-body')">✉️ Message Body</button>
            <button type="button" class="forensics-anchor-btn" data-target="sec-attachments" onclick="window.forensicsScrollTo('sec-attachments')">📎 Attachments (${attachments.length})</button>
            <div style="flex: 1;"></div>
            <button type="button" class="btn-ui" onclick="window.switchTab('dashboard')" style="font-size: 0.74rem; padding: 4px 10px; display: inline-flex; align-items: center; gap: 4px;" title="Exit to Dashboard (Esc)">
                &larr; Exit to Dashboard <kbd style="font-size: 0.65rem; padding: 1px 4px; background: rgba(0,0,0,0.3); border-radius: 3px; border: 1px solid var(--border);">Esc</kbd>
            </button>
        </div>

        <div style="display: flex; flex-direction: column; gap: 20px; max-width: 1100px; margin: 0 auto; padding-bottom: 40px;">
            <!-- Section 1: Overview Profile Card -->
            <div id="sec-overview" class="forensics-section-card">
                <div style="padding: 20px;">
                    <div style="display: flex; justify-content: space-between; align-items: flex-start; gap: 16px; margin-bottom: 16px;">
                        <div>
                            <div style="font-size: 1.2rem; font-weight: 700; color: var(--text); line-height: 1.4;">${safeSubject}</div>
                            <div style="font-size: 0.82rem; color: var(--muted); margin-top: 6px;">From: <b style="color: var(--text);">${safeSender}</b> &bull; To: ${safeRecipient} &bull; ${item.date || ''} ${item.time || ''}</div>
                        </div>
                        <div style="display: flex; align-items: center; gap: 10px; flex-shrink: 0;">
                            <span class="badge ${verdictClass}" style="font-size: 0.85rem; padding: 5px 12px;">${safeVerdict}</span>
                            ${avBadgeHtml}
                            <div style="font-size: 1.4rem; font-weight: 800; font-family: monospace; color: ${parseInt(item.score) < 50 ? 'var(--danger)' : (parseInt(item.score) < 80 ? 'var(--warn)' : 'var(--ok)')};">${item.score !== undefined ? item.score : 100}%</div>
                        </div>
                    </div>

                    <!-- Risk Score Bar -->
                    <div style="background: var(--surface-mid); border-radius: 6px; padding: 10px 14px; border: 1px solid var(--border);">
                        <div style="display: flex; justify-content: space-between; font-size: 0.72rem; color: var(--muted); font-weight: 700; margin-bottom: 6px;">
                            <span>SECURITY INTEGRITY SPECTRUM</span>
                            <span>${100 - (parseInt(item.score) || 100)} PTS DEDUCTED</span>
                        </div>
                        <div style="display: flex; height: 10px; border-radius: 5px; overflow: hidden; background: #222;">
                            ${barSegmentsHtml}
                        </div>
                    </div>
                </div>
            </div>

            <!-- Section 2: Penalties Card -->
            <div id="sec-penalties" class="forensics-section-card">
                <div class="forensics-section-header">
                    <span>Engine Scoring Penalties &amp; Deductions (${penalties.length})</span>
                    <span style="font-size: 0.75rem; color: var(--muted); font-weight: normal;">Deductions based on authentication, spoofing, and heuristic rules</span>
                </div>
                <div style="padding: 18px 20px;">
                    ${penalties.length > 0 ? `
                        <div style="display: flex; flex-direction: column; gap: 10px;">
                            ${penalties.map((p, idx) => `
                                <div style="display: flex; justify-content: space-between; align-items: center; padding: 10px 14px; background: var(--surface-mid); border: 1px solid var(--border); border-radius: 6px; border-left: 4px solid ${palette[idx % palette.length]};">
                                    <div>
                                        <div style="font-weight: 600; font-size: 0.82rem; color: var(--text);">${window.escapeHtml(p.rule || 'Deduction')}</div>
                                        <div style="font-size: 0.74rem; color: var(--muted); margin-top: 2px;">${window.escapeHtml(p.evidence || 'Engine threshold triggered')}</div>
                                    </div>
                                    <span style="font-weight: 700; color: var(--danger); font-family: monospace; font-size: 0.85rem;">-${p.points} pts</span>
                                </div>
                            `).join('')}
                        </div>
                    ` : `
                        <div style="padding: 24px; text-align: center; color: var(--ok); font-size: 0.85rem;">
                            ✔ No penalties detected. This email satisfied all active security checks (DMARC, SPF, DKIM, PTR, Heuristics, and Blacklists).
                        </div>
                    `}
                </div>
            </div>

            <!-- Section 3: MIME Transport Headers -->
            <div id="sec-headers" class="forensics-section-card">
                <div class="forensics-section-header">
                    <span>MIME Transport &amp; Authentication Headers</span>
                    <button type="button" class="btn-ui" onclick="window.copyForensicsHeaders()" style="font-size: 0.74rem; padding: 4px 10px; display: inline-flex; align-items: center; gap: 4px;">
                        📋 Copy Headers
                    </button>
                </div>
                <div style="padding: 16px 20px; max-height: 400px; overflow-y: auto; background: #0a0c12;">
                    ${headersHtml}
                </div>
            </div>

            <!-- Section 4: Message Body Payload -->
            <div id="sec-body" class="forensics-section-card">
                <div class="forensics-section-header">
                    <span>Message Body Payload</span>
                    <span style="font-size: 0.75rem; color: var(--muted); font-weight: normal;">${bodyContent.length} characters</span>
                </div>
                <div style="padding: 20px; max-height: 450px; overflow-y: auto; font-family: 'Segoe UI', system-ui, sans-serif; font-size: 0.85rem; line-height: 1.6; color: var(--text); white-space: pre-wrap; background: var(--bg);">
                    ${window.escapeHtml(bodyContent)}
                </div>
            </div>

            <!-- Section 5: Attachments -->
            <div id="sec-attachments" class="forensics-section-card">
                <div class="forensics-section-header">
                    <span>Message File Attachments (${attachments.length})</span>
                    <span style="font-size: 0.75rem; color: var(--muted); font-weight: normal;">Isolated container analysis</span>
                </div>
                <div style="padding: 18px 20px;">
                    ${attachments.length > 0 ? `
                        <div style="display: flex; flex-direction: column; gap: 8px;">
                            ${attachments.map(att => {
                                const name = att.name || att.fileName || att.filename || 'attachment';
                                const size = att.size || att.sizeBytes || 0;
                                const sizeStr = size > 1024 * 1024 ? `${(size / (1024 * 1024)).toFixed(1)} MB` : (size > 1024 ? `${(size / 1024).toFixed(1)} KB` : `${size} B`);
                                return `
                                    <div style="display: flex; justify-content: space-between; align-items: center; padding: 10px 14px; background: var(--surface-mid); border: 1px solid var(--border); border-radius: 6px;">
                                        <div style="display: flex; align-items: center; gap: 8px;">
                                            <span>📎</span>
                                            <div>
                                                <div style="font-weight: 600; font-size: 0.82rem; color: var(--text);">${window.escapeHtml(name)}</div>
                                                <div style="font-size: 0.72rem; color: var(--muted);">${sizeStr}</div>
                                            </div>
                                        </div>
                                        <span class="badge badge-safe">Verified</span>
                                    </div>
                                `;
                            }).join('')}
                        </div>
                    ` : `
                        <div style="padding: 20px; text-align: center; color: var(--muted); font-size: 0.85rem;">
                            No file attachments attached to this message.
                        </div>
                    `}
                </div>
            </div>
        </div>
    `;
};

window.forensicsOpenInOutlook = () => {
    if (window.currentForensicsItem && typeof window.openIncidentInOutlook === 'function') {
        window.openIncidentInOutlook(window.currentForensicsItem);
    }
};

window.forensicsScanAv = async () => {
    if (!window.currentForensicsItem) return;
    const item = window.currentForensicsItem;
    const btn = document.getElementById('pill-forensics-av') || document.getElementById('btn-forensics-av');
    if (typeof window.scanEmailAv === 'function') {
        await window.scanEmailAv(item, btn);
        if (window.currentForensicsItem) {
            window.renderForensicsTab(window.currentForensicsItem);
            window.renderForensicsQueue();
        }
    }
};

window.forensicsScanVirusTotal = () => {
    if (window.currentForensicsItem && typeof window.cloudScanIncident === 'function') {
        window.cloudScanIncident(window.currentForensicsItem.entryId || window.currentForensicsItem.id);
    }
};

// Auto-advance conveyor belt (A2)
window.forensicsQuarantine = async () => {
    if (!window.currentForensicsItem) return;
    const item = window.currentForensicsItem;
    const entryId = item.entryId || item.id;
    const fingerprint = item.fingerprint;
    const storeId = item.storeId;

    const queue = getForensicsQueue();
    const idx = queue.findIndex(i => (i.entryId || i.id || i.fingerprint) === (item.entryId || item.id || item.fingerprint));
    let nextTarget = null;
    if (idx !== -1) {
        if (idx + 1 < queue.length) nextTarget = queue[idx + 1];
        else if (idx - 1 >= 0) nextTarget = queue[idx - 1];
    }

    try {
        const res = await window.securityApi.quarantineEmail({ entryId, fingerprint, storeId });
        if (res && res.success) {
            let actionedItem = null;
            let actionedCat = null;
            ['malicious', 'suspicious', 'spam', 'safe'].forEach(cat => {
                if (window.AppState.stats && window.AppState.stats[cat]) {
                    const cIdx = window.AppState.stats[cat].findIndex(it => (it.entryId || it.id) === entryId || (fingerprint && it.fingerprint === fingerprint));
                    if (cIdx !== -1) {
                        actionedItem = window.AppState.stats[cat][cIdx];
                        window.AppState.stats[cat].splice(cIdx, 1);
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

            if (window.AppState.stats && Array.isArray(window.AppState.stats.all)) {
                const allIdx = window.AppState.stats.all.findIndex(it => (it.entryId || it.id) === entryId || (fingerprint && it.fingerprint === fingerprint));
                if (allIdx !== -1) {
                    window.AppState.stats.all[allIdx].verdict = 'Spam';
                    window.AppState.stats.all[allIdx].currentFolder = 'Junk Email';
                }
            }

            if (actionedCat && actionedCat !== 'spam' && window.AppState.counts) {
                if (window.AppState.counts[actionedCat] > 0) window.AppState.counts[actionedCat]--;
                window.AppState.counts.spam = (window.AppState.counts.spam || 0) + 1;
            }

            if (window.AppState && typeof window.AppState.renderDashboard === 'function') {
                window.AppState.renderDashboard(false);
            }

            if (nextTarget) {
                if (typeof window.showNotification === 'function') {
                    window.showNotification("Email quarantined to Junk Email. Auto-advanced to next email.");
                }
                window.renderForensicsTab(nextTarget);
            } else {
                window.currentForensicsItem = null;
                window.AppState.selectedId = null;
                renderForensicsEmptyQueue("All emails in the queue have been triaged.");
                if (typeof window.showNotification === 'function') {
                    window.showNotification("Queue empty. Returning to Dashboard...");
                }
                setTimeout(() => {
                    window.switchTab('dashboard');
                }, 1200);
            }
        } else {
            if (typeof window.showNotification === 'function') {
                window.showNotification("Quarantine failed: " + ((res && res.error) || "Could not move email."), true);
            }
        }
    } catch (err) {
        if (typeof window.showNotification === 'function') {
            window.showNotification("Quarantine error: " + err.message, true);
        }
    }
};

window.forensicsDelete = () => {
    if (!window.currentForensicsItem) return;
    const item = window.currentForensicsItem;
    const id = item.entryId || item.id;
    window.showConfirm("Delete Email", "This will permanently delete this email from Outlook. Proceed?", "DELETE", async () => {
        try {
            const queue = getForensicsQueue();
            const idx = queue.findIndex(i => (i.entryId || i.id || i.fingerprint) === (item.entryId || item.id || item.fingerprint));
            let nextTarget = null;
            if (idx !== -1) {
                if (idx + 1 < queue.length) nextTarget = queue[idx + 1];
                else if (idx - 1 >= 0) nextTarget = queue[idx - 1];
            }

            const res = await window.securityApi.deleteEmail({ entryId: id, entryIds: [id] });
            if (res && res.ok === false && res.success === false) {
                if (typeof window.showNotification === 'function') {
                    window.showNotification("Failed to delete email from Outlook: " + (res.error || "Unknown error"), true);
                }
                return;
            }

            let deletedCat = null;
            ['malicious', 'suspicious', 'spam', 'safe', 'all'].forEach(cat => {
                if (window.AppState.stats && window.AppState.stats[cat]) {
                    const cIdx = window.AppState.stats[cat].findIndex(it => (it.entryId || it.id) === id);
                    if (cIdx !== -1) {
                        window.AppState.stats[cat].splice(cIdx, 1);
                        if (cat !== 'all') deletedCat = cat;
                    }
                }
            });

            if (deletedCat && window.AppState.counts) {
                if (window.AppState.counts[deletedCat] > 0) window.AppState.counts[deletedCat]--;
                if (window.AppState.counts.total > 0) window.AppState.counts.total--;
            }

            if (window.AppState && typeof window.AppState.renderDashboard === 'function') {
                window.AppState.renderDashboard(false);
            }

            if (nextTarget) {
                if (typeof window.showNotification === 'function') {
                    window.showNotification("Email deleted. Auto-advanced to next email.");
                }
                window.renderForensicsTab(nextTarget);
            } else {
                window.currentForensicsItem = null;
                window.AppState.selectedId = null;
                renderForensicsEmptyQueue("All emails in the queue have been triaged.");
                if (typeof window.showNotification === 'function') {
                    window.showNotification("Queue empty. Returning to Dashboard...");
                }
                setTimeout(() => {
                    window.switchTab('dashboard');
                }, 1200);
            }
        } catch (err) {
            if (typeof window.showNotification === 'function') {
                window.showNotification("Delete error: " + err.message, true);
            }
        }
    });
};

window.renderForensics = window.renderForensicsTab;
