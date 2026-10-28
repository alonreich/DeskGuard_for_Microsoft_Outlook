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

window.renderForensicsTab = async (item) => {
    window.currentForensicsItem = item;
    const container = document.getElementById('forensics-tab-content');
    const toolbar = document.getElementById('forensics-toolbar-actions');
    if (!container || !item) return;

    if (toolbar) {
        toolbar.innerHTML = `
            <button type="button" class="btn-ui" onclick="window.forensicsOpenInOutlook()" style="font-size: 0.78rem; padding: 6px 12px; display: inline-flex; align-items: center; gap: 6px;">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><polyline points="15 3 21 3 21 9"></polyline><line x1="10" y1="14" x2="21" y2="3"></line></svg>
                Open in Outlook
            </button>
            <button type="button" class="btn-ui" onclick="window.forensicsScanAv()" style="font-size: 0.78rem; padding: 6px 12px; display: inline-flex; align-items: center; gap: 6px; background: rgba(0, 120, 212, 0.12); border-color: var(--accent); color: var(--accent); font-weight: 700;">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path></svg>
                Scan AV (Defender &amp; ClamAV)
            </button>
            <button type="button" class="btn-ui" onclick="window.forensicsScanVirusTotal()" style="font-size: 0.78rem; padding: 6px 12px; display: inline-flex; align-items: center; gap: 6px;">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2L1 21h22L12 2zm0 3.99L19.53 19H4.47L12 5.99zM11 10v4h2v-4h-2zm0 6v2h2v-2h-2z"/></svg>
                VirusTotal
            </button>
            <button type="button" class="btn-ui" onclick="window.forensicsQuarantine()" style="font-size: 0.78rem; padding: 6px 12px; color: var(--warn); display: inline-flex; align-items: center; gap: 6px;">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>
                Quarantine
            </button>
            <button type="button" class="btn-ui" onclick="window.forensicsDelete()" style="font-size: 0.78rem; padding: 6px 12px; color: var(--danger); display: inline-flex; align-items: center; gap: 6px;">
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

    let headersHtml = '';
    const lines = rawHeaders.split('\n');
    headersHtml = lines.map(line => {
        const parts = line.split(':');
        if (parts.length > 1) {
            const key = parts[0].trim();
            const val = parts.slice(1).join(':').trim();
            return `<div style="display: flex; gap: 12px; margin-bottom: 4px; border-bottom: 1px solid var(--border); padding: 5px 0;">
                <span style="color: var(--accent); font-weight: 600; min-width: 170px; font-family: monospace; font-size: 0.78rem;">${window.escapeHtml(key)}:</span>
                <span style="color: var(--text); font-family: monospace; font-size: 0.78rem; word-break: break-all;">${window.escapeHtml(val)}</span>
            </div>`;
        }
        return `<div style="color: var(--muted); font-size: 0.78rem;">${window.escapeHtml(line)}</div>`;
    }).join('');

    const safeSubject = window.escapeHtml(item.subject || item.details || '(No Subject)');
    const safeSender = window.escapeHtml(item.sender || item.from || 'Unknown');
    const safeRecipient = window.escapeHtml(item.to || item.recipient || item.recips || 'N/A');
    const safeVerdict = window.escapeHtml(item.verdict || 'Safe');
    const verdictClass = (item.verdict || '').toLowerCase().includes('malicious') ? 'badge-malicious' : ((item.verdict || '').toLowerCase().includes('spam') ? 'badge-spam' : 'badge-safe');

    // Parse penalties
    let penalties = [];
    if (Array.isArray(item.penalties)) penalties = item.penalties;
    else if (typeof item.penalties_json === 'string') {
        try { penalties = JSON.parse(item.penalties_json); } catch {}
    } else if (item.tier) {
        const tokens = (item.tier || '').split('|').map(t => t.trim());
        penalties = tokens.map(t => {
            const m = t.match(/^([^\[]+)\[-(\d+(?:\.\d+)?)\]/);
            return m ? { rule: m[1], points: parseFloat(m[2]), evidence: t } : { rule: t, points: 0, evidence: t };
        }).filter(p => p.points > 0);
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
        <div style="display: flex; flex-direction: column; gap: 18px; max-width: 1100px; margin: 0 auto;">
            <!-- Profile Card -->
            <div style="background: var(--surface-low); border: 1px solid var(--border); border-radius: 8px; padding: 20px;">
                <div style="display: flex; justify-content: space-between; align-items: flex-start; gap: 16px; margin-bottom: 16px;">
                    <div>
                        <div style="font-size: 1.15rem; font-weight: 700; color: var(--text); line-height: 1.4;">${safeSubject}</div>
                        <div style="font-size: 0.8rem; color: var(--muted); margin-top: 4px;">From: <b style="color: var(--text);">${safeSender}</b> &bull; To: ${safeRecipient} &bull; ${item.date || ''} ${item.time || ''}</div>
                    </div>
                    <div style="display: flex; align-items: center; gap: 10px; flex-shrink: 0;">
                        <span class="badge ${verdictClass}" style="font-size: 0.85rem; padding: 5px 12px;">${safeVerdict}</span>
                        <div style="font-size: 1.4rem; font-weight: 800; font-family: monospace; color: ${parseInt(item.score) < 50 ? 'var(--danger)' : (parseInt(item.score) < 80 ? 'var(--warn)' : 'var(--ok)')};">${item.score !== undefined ? item.score : 100}%</div>
                    </div>
                </div>

                <!-- Risk Score Bar -->
                <div style="background: var(--surface-mid); border-radius: 4px; padding: 10px 14px; border: 1px solid var(--border);">
                    <div style="display: flex; justify-content: space-between; font-size: 0.72rem; color: var(--muted); font-weight: 700; margin-bottom: 6px;">
                        <span>SECURITY INTEGRITY SPECTRUM</span>
                        <span>${100 - (parseInt(item.score) || 100)} PTS DEDUCTED</span>
                    </div>
                    <div style="display: flex; height: 10px; border-radius: 5px; overflow: hidden; background: #222;">
                        ${barSegmentsHtml}
                    </div>
                </div>
            </div>

            <!-- Tabbed diagnostic panes -->
            <div style="background: var(--surface-low); border: 1px solid var(--border); border-radius: 8px; overflow: hidden; display: flex; flex-direction: column;">
                <div style="display: flex; border-bottom: 1px solid var(--border); background: var(--surface-mid); padding: 0 16px;">
                    <button type="button" class="settings-tab-btn active" id="forensic-nav-body" onclick="window.switchForensicsSubTab('body')">Message Body</button>
                    <button type="button" class="settings-tab-btn" id="forensic-nav-scores" onclick="window.switchForensicsSubTab('scores')">Engine Penalties (${penalties.length})</button>
                    <button type="button" class="settings-tab-btn" id="forensic-nav-headers" onclick="window.switchForensicsSubTab('headers')">MIME Transport Headers</button>
                </div>

                <div id="forensic-pane-body" style="padding: 20px; min-height: 250px; max-height: 500px; overflow-y: auto; font-family: 'Segoe UI', system-ui, sans-serif; font-size: 0.85rem; line-height: 1.6; color: var(--text); white-space: pre-wrap; background: var(--bg);">
                    ${window.escapeHtml(bodyContent)}
                </div>

                <div id="forensic-pane-scores" style="display: none; padding: 20px; max-height: 500px; overflow-y: auto;">
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
                        <div style="padding: 30px; text-align: center; color: var(--ok); font-size: 0.85rem;">
                            ✔ No penalties detected. This email satisfied all active security checks (DMARC, SPF, DKIM, PTR, Heuristics, and Blacklists).
                        </div>
                    `}
                </div>

                <div id="forensic-pane-headers" style="display: none; padding: 16px 20px; max-height: 500px; overflow-y: auto; background: #0a0c12;">
                    ${headersHtml}
                </div>
            </div>
        </div>
    `;
};

window.switchForensicsSubTab = (tab) => {
    ['body', 'scores', 'headers'].forEach(t => {
        const btn = document.getElementById(`forensic-nav-${t}`);
        const pane = document.getElementById(`forensic-pane-${t}`);
        if (btn) btn.classList.toggle('active', t === tab);
        if (pane) pane.style.display = (t === tab) ? 'block' : 'none';
    });
};

window.forensicsOpenInOutlook = () => {
    if (window.currentForensicsItem && typeof window.openIncidentInOutlook === 'function') {
        window.openIncidentInOutlook(window.currentForensicsItem);
    }
};

window.forensicsScanAv = () => {
    if (window.currentForensicsItem && typeof window.scanEmailAv === 'function') {
        const btn = document.getElementById('btn-forensics-av');
        window.scanEmailAv(window.currentForensicsItem, btn);
    }
};

window.forensicsScanVirusTotal = () => {
    if (window.currentForensicsItem && typeof window.cloudScanIncident === 'function') {
        window.cloudScanIncident(window.currentForensicsItem.entryId || window.currentForensicsItem.id);
    }
};

window.forensicsQuarantine = () => {
    if (window.currentForensicsItem && typeof window.quarantineIncident === 'function') {
        window.quarantineIncident(window.currentForensicsItem.entryId || window.currentForensicsItem.id, window.currentForensicsItem.fingerprint, window.currentForensicsItem.storeId);
    }
};

window.forensicsDelete = () => {
    if (window.currentForensicsItem && typeof window.deleteIncident === 'function') {
        window.deleteIncident(window.currentForensicsItem.entryId || window.currentForensicsItem.id);
    }
};

window.renderForensics = window.renderForensicsTab;
