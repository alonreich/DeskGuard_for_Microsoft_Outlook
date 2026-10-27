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

window.renderForensics = async (item) => {
    const body = document.getElementById('forensics-body');
    if (!body || !item) return;
    
    body.innerHTML = '<div style="display: flex; align-items: center; justify-content: center; height: 100%; color: var(--muted);">Deconstructing forensic snapshots...</div>';

    const data = await window.securityApi.getForensics({
        entryId: item.entryId || item.id,
        originalEntryId: item.originalEntryId,
        fingerprint: item.fingerprint
    });
    
    let rawHeaders = (data && data.fullHeaders && data.fullHeaders !== 'N/A' && data.fullHeaders !== 'Unavailable')
        ? data.fullHeaders
        : (item.fullHeaders || item.headers || '');

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
            const keyEsc = window.escapeHtml(key);
            const valEsc = window.escapeHtml(val);
            return `<div style="display: flex; gap: 10px; margin-bottom: 4px; border-bottom: 1px solid #222; padding: 4px 0;">
                <span style="color: var(--accent); font-weight: 600; min-width: 150px; font-family: monospace; font-size: 0.75rem;">${keyEsc}:</span>
                <span style="color: var(--text); font-family: monospace; font-size: 0.75rem; word-break: break-all;">${valEsc}</span>
            </div>`;
        }
        return `<div style="color: var(--muted); font-size: 0.75rem;">${window.escapeHtml(line)}</div>`;
    }).join('');

    const safeSubject = window.escapeHtml(item.subject || item.details || 'No Subject');
    const safeSender = window.escapeHtml(item.sender || 'Unknown');
    const safeVerdict = window.escapeHtml(item.verdict || 'Safe');
    const safeTier = window.escapeHtml(item.tier || 'Standard Heuristics');
    const verdictClass = (item.verdict || '').toLowerCase().includes('malicious') ? 'badge-malicious' : ((item.verdict || '').toLowerCase().includes('spam') ? 'badge-spam' : 'badge-safe');

    // Parse structured penalties if present
    let penalties = [];
    if (Array.isArray(item.penalties)) penalties = item.penalties;
    else if (typeof item.penalties_json === 'string') {
        try { penalties = JSON.parse(item.penalties_json); } catch {}
    } else if (item.tier) {
        // Fallback parse tier tokens
        const tokens = (item.tier || '').split('|').map(t => t.trim());
        penalties = tokens.map(t => {
            const m = t.match(/^([^\[]+)\[-(\d+(?:\.\d+)?)\]/);
            return m ? { rule: m[1], points: parseFloat(m[2]), evidence: t } : { rule: t, points: 0, evidence: t };
        }).filter(p => p.points > 0);
    }

    // Stacked bars calculation
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

    body.innerHTML = `
        <div style="display: flex; flex-direction: column; gap: 16px; height: 100%;">
            <div style="background: var(--surface-low); padding: 20px; border: 1px solid var(--border); border-radius: 8px;">
                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px;">
                    <h3 style="margin: 0; color: var(--accent); font-size: 0.9rem; text-transform: uppercase; letter-spacing: 1px;">Security Incident Profile</h3>
                    <button id="forensic-btn-open-outlook" class="primary" style="font-size: 0.75rem; padding: 5px 12px;">
                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><polyline points="15 3 21 3 21 9"></polyline><line x1="10" y1="14" x2="21" y2="3"></line></svg>
                        Open in Outlook
                    </button>
                </div>
                <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px;">
                    <div>
                        <label style="font-size: 0.65rem; color: var(--muted); font-weight: 700;">SUBJECT</label>
                        <div style="font-weight: 600; font-size: 0.9rem; color: var(--text);">${safeSubject}</div>
                    </div>
                    <div>
                        <label style="font-size: 0.65rem; color: var(--muted); font-weight: 700;">SENDER ORIGIN</label>
                        <div style="font-weight: 600; font-size: 0.9rem; color: var(--text);">${safeSender}</div>
                    </div>
                    <div>
                        <label style="font-size: 0.65rem; color: var(--muted); font-weight: 700;">VERDICT</label>
                        <div><span class="badge ${verdictClass}">${safeVerdict}</span></div>
                    </div>
                    <div>
                        <label style="font-size: 0.65rem; color: var(--muted); font-weight: 700;">DETECTION SOURCE</label>
                        <div style="font-weight: 600; font-family: monospace; font-size: 0.8rem; color: var(--muted);">${safeTier}</div>
                    </div>
                </div>
            </div>

            <div style="flex: 1; display: flex; flex-direction: column; min-height: 0; background: var(--surface-low); border: 1px solid var(--border); border-radius: 8px; overflow: hidden;">
                <div style="display: flex; border-bottom: 1px solid var(--border); background: var(--surface-mid);">
                    <div id="tab-btn-headers" onclick="switchForensicTab('headers')" style="padding: 12px 24px; cursor: pointer; border-bottom: 2px solid var(--accent); color: var(--accent); font-weight: 600; font-size: 0.75rem; letter-spacing: 0.5px;">TRANSPORT HEADERS</div>
                    <div id="tab-btn-body" onclick="switchForensicTab('body')" style="padding: 12px 24px; cursor: pointer; color: var(--muted); font-size: 0.75rem; letter-spacing: 0.5px;">MESSAGE CONTENT</div>
                    <div id="tab-btn-scores" onclick="switchForensicTab('scores')" style="padding: 12px 24px; cursor: pointer; color: var(--muted); font-size: 0.75rem; letter-spacing: 0.5px;">SCORE BREAKDOWN & WHAT-IF</div>
                </div>
                <div id="forensic-tab-headers" style="flex: 1; overflow: auto; padding: 16px; background: #080a11;">
                    ${headersHtml}
                </div>
                <div id="forensic-tab-body" style="display: none; flex: 1; overflow: auto; padding: 20px; background: #080a11; font-family: 'Consolas', monospace; font-size: 0.8rem; color: #a0a0a0; white-space: pre-wrap; line-height: 1.6;"></div>
                <div id="forensic-tab-scores" style="display: none; flex: 1; overflow: auto; padding: 20px; background: #080a11;">
                    <div style="margin-bottom: 20px;">
                        <div style="display: flex; justify-content: space-between; margin-bottom: 8px;">
                            <span style="font-size: 0.85rem; font-weight: 600; color: var(--text);">Penalty Stack Bar Chart</span>
                            <span id="whatif-score-display" style="font-weight: 700; color: var(--accent); font-size: 0.95rem;">Score: ${item.score !== undefined ? item.score : 100}% (${safeVerdict})</span>
                        </div>
                        <div style="display: flex; height: 16px; border-radius: 4px; overflow: hidden; background: #222; border: 1px solid var(--border);">
                            ${barSegmentsHtml}
                        </div>
                    </div>

                    <div style="margin-bottom: 24px;">
                        <h4 style="margin: 0 0 10px 0; font-size: 0.8rem; text-transform: uppercase; color: var(--muted); letter-spacing: 0.5px;">Applied Security Penalties</h4>
                        <div style="display: flex; flex-direction: column; gap: 8px;">
                            ${penalties.length > 0 ? penalties.map((p, idx) => `
                                <div style="display: flex; justify-content: space-between; align-items: center; background: var(--surface-low); padding: 8px 12px; border-radius: 4px; border-left: 3px solid ${palette[idx % palette.length]};">
                                    <div style="display: flex; flex-direction: column; gap: 2px;">
                                        <span style="font-weight: 600; font-size: 0.85rem; color: var(--text);">${window.escapeHtml(p.rule)}</span>
                                        <span style="font-size: 0.75rem; color: var(--muted);">${window.escapeHtml(p.evidence || 'Heuristic check triggered')}</span>
                                    </div>
                                    <span style="font-weight: 700; color: var(--danger); font-size: 0.85rem;">-${p.points} pts</span>
                                </div>
                            `).join('') : '<div style="color: var(--safe); font-size: 0.85rem;">No penalties applied. Clean security profile.</div>'}
                        </div>
                    </div>

                    <div style="background: var(--surface-low); padding: 16px; border-radius: 6px; border: 1px solid var(--border);">
                        <h4 style="margin: 0 0 8px 0; font-size: 0.8rem; text-transform: uppercase; color: var(--accent); letter-spacing: 0.5px;">Live "What-If" Simulation</h4>
                        <p style="font-size: 0.75rem; color: var(--muted); margin-top: 0;">Adjust rule slider values to test how threshold tuning affects this email without altering production rules.</p>
                        <div style="display: flex; gap: 16px; align-items: center; margin-top: 10px;">
                            <label style="font-size: 0.8rem; color: var(--text); min-width: 140px;">Test Rule Sensitivity:</label>
                            <input type="range" id="whatif-multiplier-slider" min="0" max="200" value="100" style="flex: 1;" />
                            <span id="whatif-slider-val" style="font-size: 0.8rem; color: var(--accent); min-width: 45px;">1.0x</span>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    `;

    const bodyTabEl = document.getElementById('forensic-tab-body');
    if (bodyTabEl) {
        let bText = (data && data.body && data.body !== 'N/A' && data.body !== 'Unavailable')
            ? data.body
            : (item.body || '');
        if (bText && typeof bText === 'string') {
            const clean = bText.trim();
            if (/^[A-Za-z0-9+/]+={0,2}$/.test(clean) && clean.length % 4 === 0 && !clean.includes(' ')) {
                try {
                    const dec = atob(clean);
                    if (dec && dec.length > 0) bText = dec;
                } catch {}
            }
        }
        bodyTabEl.textContent = (bText && bText.trim().length > 0) ? bText : '(No message body content)';
    }

    // What-If live simulation slider listener
    const whatIfSlider = document.getElementById('whatif-multiplier-slider');
    const whatIfDisplay = document.getElementById('whatif-score-display');
    const whatIfLabel = document.getElementById('whatif-slider-val');
    if (whatIfSlider && whatIfDisplay && whatIfLabel) {
        whatIfSlider.oninput = async () => {
            const multiplier = whatIfSlider.value / 100;
            whatIfLabel.textContent = `${multiplier.toFixed(1)}x`;
            const baseDeduction = totalPoints;
            const newDeduction = Math.round(baseDeduction * multiplier);
            const newScore = Math.max(0, Math.min(100, 100 - newDeduction));
            let newVerdict = 'Safe';
            if (newScore <= 20) newVerdict = 'Malicious';
            else if (newScore <= 40) newVerdict = 'Spam';
            else if (newScore <= 55) newVerdict = 'Suspicious';

            whatIfDisplay.textContent = `Score: ${newScore}% (${newVerdict})`;
        };
    }

    const fOpenOutlook = document.getElementById('forensic-btn-open-outlook');
    if (fOpenOutlook) {
        fOpenOutlook.onclick = async () => {
            fOpenOutlook.disabled = true;
            const origText = fOpenOutlook.innerHTML;
            fOpenOutlook.innerHTML = `<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spin"><path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg> Launching...`;
            try {
                await window.securityApi.openEmail({
                    entryId: item.entryId || item.id,
                    storeId: item.storeId || ''
                });
            } catch (e) {}
            fOpenOutlook.disabled = false;
            fOpenOutlook.innerHTML = origText;
        };
    }
};

window.switchForensicTab = (tab) => {
    const hBtn = document.getElementById('tab-btn-headers');
    const bBtn = document.getElementById('tab-btn-body');
    const sBtn = document.getElementById('tab-btn-scores');
    const hTab = document.getElementById('forensic-tab-headers');
    const bTab = document.getElementById('forensic-tab-body');
    const sTab = document.getElementById('forensic-tab-scores');
    if (!hBtn || !bBtn || !sBtn || !hTab || !bTab || !sTab) return;

    hBtn.style.borderBottom = 'none'; hBtn.style.color = 'var(--muted)';
    bBtn.style.borderBottom = 'none'; bBtn.style.color = 'var(--muted)';
    sBtn.style.borderBottom = 'none'; sBtn.style.color = 'var(--muted)';
    hTab.style.display = 'none';
    bTab.style.display = 'none';
    sTab.style.display = 'none';

    if (tab === 'headers') {
        hBtn.style.borderBottom = '2px solid var(--accent)';
        hBtn.style.color = 'var(--accent)';
        hTab.style.display = 'block';
    } else if (tab === 'body') {
        bBtn.style.borderBottom = '2px solid var(--accent)';
        bBtn.style.color = 'var(--accent)';
        bTab.style.display = 'block';
    } else if (tab === 'scores') {
        sBtn.style.borderBottom = '2px solid var(--accent)';
        sBtn.style.color = 'var(--accent)';
        sTab.style.display = 'block';
    }
};
