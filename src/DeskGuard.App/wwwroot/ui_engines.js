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

// Every rule's weight is the most points it can take off an email's 100-point score. Active weights always add up to 100.
window.DESKGUARD_DEFAULT_RUBRICS = {
    version: 2,
    weights: { spf: 10, dmarc: 10, dkim: 5, alignment: 5, rdns: 4, rbl: 10, body: 5, heuristics: 6, spamflag: 4, mime: 6, msscl: 10, linkmismatch: 8, displayspoof: 7, lookalike: 5, replyto: 3, firstcontact: 2 },
    toggles: { spf: true, dmarc: true, dkim: true, alignment: true, rdns: true, rbl: true, body: true, heuristics: true, spamflag: true, mime: true, msscl: true, linkmismatch: true, displayspoof: true, lookalike: true, replyto: true, firstcontact: true },
    maliciousThresholdPercent: 20,
    spamThresholdPercent: 40,
    suspiciousThresholdPercent: 55
};

window.SecurityUI = window.SecurityUI || {};
SecurityUI.Engines = (function() {
    const api = window.securityApi;
    const DEFAULTS = window.DESKGUARD_DEFAULT_RUBRICS;
    let currentWeights = { ...DEFAULTS.weights };
    let currentToggles = { ...DEFAULTS.toggles };
    let thresholds = { malicious: DEFAULTS.maliciousThresholdPercent, spam: DEFAULTS.spamThresholdPercent, suspicious: DEFAULTS.suspiciousThresholdPercent };
    let tooltipTimer = null;

    const labels = {
        spf: 'SPF AUTHORIZATION', dmarc: 'DMARC AUTHENTICATION', dkim: 'DKIM SIGNATURES', alignment: 'SENDER ALIGNMENT',
        rdns: 'REVERSE DNS CHECK', rbl: 'GLOBAL BLACKLISTS', body: 'HIDDEN TEXT SHIELD', heuristics: 'SPAM WORDS',
        spamflag: 'SERVER SPAM FLAG', mime: 'DANGEROUS ATTACHMENTS',
        msscl: "MICROSOFT'S SPAM SCORE", linkmismatch: 'FAKE LINKS', displayspoof: 'FAKE SENDER NAME',
        lookalike: 'LOOK-ALIKE DOMAIN', replyto: 'REPLIES GO ELSEWHERE', firstcontact: 'FIRST-TIME SENDER'
    };
    const tips = {
        dmarc: "WHAT IT IS: The domain owner's published rule saying how to treat mail that fails SPF/DKIM. \n\nCOSTS: the full weight on 'fail', half the weight on 'none' (domain has no policy). \n\nWHEN TO LOWER: Small senders often have no DMARC policy at all.",
        alignment: "WHAT IT IS: Checks that the sender's address and the bounce (Return-Path) address belong to the same company. \n\nCOSTS: the full weight on a mismatch. Skipped when DMARC already passed. \n\nWHEN TO LOWER: If you get many newsletters sent through mailing services.",
        dkim: "WHAT IT IS: A digital 'wax seal' that proves the email was not changed after it was sent. \n\nCOSTS: the full weight when the seal is missing or broken. \n\nWHEN TO LOWER: Only for mail from very old systems that cannot sign.",
        spf: "WHAT IT IS: The domain's list of servers allowed to send its mail. \n\nCOSTS: the full weight on fail / softfail / none. \n\nWHEN TO LOWER: Rarely. Keep this one high.",
        rdns: "WHAT IT IS: Checks that the sending server has a proper internet name, not a home or dynamic line. \n\nCOSTS: the full weight when the name is missing or looks like a home connection. \n\nWHEN TO LOWER: If small businesses on cheap hosting write to you.",
        body: "WHAT IT IS: Looks in the email's HTML for hidden text, invisible fonts and scripts used to fool filters. \n\nCOSTS: the full weight when such tricks are found. \n\nWHEN TO LOWER: If fancy marketing mail gets flagged.",
        heuristics: "WHAT IT IS: Looks for phrases scammers love (your 'Spam Words' list in Settings). \n\nCOSTS: a third of the weight per phrase found, the full weight at 3 or more. \n\nWHEN TO LOWER: If your work uses these phrases normally.",
        rbl: "WHAT IT IS: Worldwide watch lists of known spam servers and domains (Spamhaus, SpamCop, Barracuda). \n\nCOSTS: the full weight when listed. \n\nWHEN TO LOWER: Almost never.",
        spamflag: "WHAT IT IS: A mail server along the way already marked this email as spam (X-Spam-Flag: YES). \n\nCOSTS: the full weight when present. \n\nWHEN TO LOWER: If your provider's own filter is too eager.",
        mime: "WHAT IT IS: Attachments that can run programs (.exe, .js, .vbs, .bat, .iso, .lnk and similar). \n\nCOSTS: the full weight when present. \n\nWHEN TO LOWER: If colleagues legitimately send you scripts or installers.",
        msscl: "WHAT IT IS: Microsoft's own spam rating, already written into every email by Exchange / Outlook.com (SCL 0-9, bulk BCL 0-9). \n\nCOSTS: the full weight at SCL 7+, 70% at SCL 5-6, half at bulk level 7+. \n\nWHEN TO LOWER: If your mailbox is not on Exchange/Outlook.com (the rating is then missing and this rule does nothing).",
        linkmismatch: "WHAT IT IS: A link whose visible text shows one web address (\"www.paypal.com\") but really opens another site. Safe Links and newsletter click-trackers are recognised and ignored. \n\nCOSTS: 60% of the weight for one such link, the full weight for two or more. \n\nWHEN TO LOWER: Rarely. This is a classic phishing trick.",
        displayspoof: "WHAT IT IS: The sender's display name pretends to be someone else: it contains another email address, or a big brand (PayPal, Microsoft, DHL...) while the real address is not from that company. \n\nCOSTS: the full weight. \n\nWHEN TO LOWER: If resellers or partners write to you in a brand's name.",
        lookalike: "WHAT IT IS: A new sender domain that is one or two letters away from a known one (paypa1.com, rnicrosoft.com) or uses look-alike foreign letters. \n\nCOSTS: the full weight. \n\nWHEN TO LOWER: Almost never.",
        replyto: "WHAT IT IS: Answers to this email would go to a different company than the sender's, worst when it is a free Gmail/Outlook.com account. \n\nCOSTS: the full weight for a free webmail reply address, half otherwise. \n\nWHEN TO LOWER: If you get many newsletters or ticket systems with separate reply addresses.",
        firstcontact: "WHAT IT IS: You have never received mail from this sender's domain before (based on your search vault). \n\nCOSTS: the full (small) weight. Alone it never makes an email spam. \n\nWHEN TO LOWER: If you often get mail from new contacts."
    };
    const thresholdInfo = {
        malicious: { title: 'DANGEROUS (moved to DeskGuard Quarantine) at or below', color: 'var(--danger)' },
        spam: { title: 'SPAM (moved to Junk Email) at or below', color: 'var(--warn, #d29200)' },
        suspicious: { title: 'SUSPICIOUS (labelled only, stays in place) at or below', color: 'var(--accent)' }
    };

    function stepWeight(key, delta) {
        if (!currentToggles[key]) return;
        let newVal = (currentWeights[key] || 0) + delta;
        if (newVal < 1) newVal = 1;
        if (newVal > 100) newVal = 100;
        rebalanceWeights(key, newVal);
        render();
    }

    function rebalanceWeights(key, newVal) {
        if (!currentToggles[key]) { currentWeights[key] = 0; } else {
            const oldVal = currentWeights[key] || 0; const diff = newVal - oldVal; currentWeights[key] = newVal;
            const activeKeys = Object.keys(currentWeights).filter(k => k !== key && currentToggles[k]);
            if (activeKeys.length > 0) { let rem = diff; activeKeys.forEach(k => { const share = Math.round(rem / activeKeys.length); currentWeights[k] -= share; rem -= share; }); currentWeights[activeKeys[0]] -= rem; }
        }
        Object.keys(currentWeights).forEach(k => { if (!currentToggles[k]) currentWeights[k] = 0; else if (currentWeights[k] < 1) currentWeights[k] = 1; });
        let total = 0; const activeKeys = Object.keys(currentWeights).filter(k => currentToggles[k]); activeKeys.forEach(k => total += currentWeights[k]);
        if (total !== 100 && activeKeys.length > 0) { const adjustKey = activeKeys.find(k => k !== key) || activeKeys[0]; currentWeights[adjustKey] += (100 - total); }
    }

    function attachTooltip(row) {
        const tooltipEl = document.getElementById('tooltip');
        row.onmouseenter = () => {
            if (!tooltipEl) return;
            const text = row.dataset.tip;
            clearTimeout(tooltipTimer);
            tooltipTimer = setTimeout(() => {
                tooltipEl.textContent = text; tooltipEl.style.display = 'block';
                const rect = row.getBoundingClientRect();
                tooltipEl.style.left = Math.max(10, rect.left - 370) + 'px';
                tooltipEl.style.top = Math.max(10, rect.top) + 'px';
            }, 400);
        };
        row.onmouseleave = () => {
            clearTimeout(tooltipTimer);
            if (tooltipEl) tooltipEl.style.display = 'none';
        };
    }

    function render() {
        const container = document.getElementById('sensitivity-container');
        if (!container) return;

        const total = Object.keys(currentWeights).reduce((a, k) => a + (currentToggles[k] ? currentWeights[k] : 0), 0);
        container.innerHTML = Object.keys(labels).map(key => {
            const val = currentWeights[key] || 0; const en = !!currentToggles[key];
            return `<div class="sensitivity-row" data-tip="${window.escapeHTML(tips[key])}" style="display:flex; align-items:center; gap:15px; opacity:${en?1:0.4}; cursor:help;"><input type="checkbox" class="w-tog" data-key="${key}" ${en?'checked':''}> <div style="flex:1.2; font-size:0.75rem; font-weight:bold;">${labels[key]}</div> <input type="range" class="w-slider" data-key="${key}" min="1" max="100" value="${val}" ${en?'':'disabled'} style="flex:2; cursor:pointer;"> <div style="display:flex; align-items:center; gap:8px;"> <button class="btn-step btn-minus" data-key="${key}">-</button> <div style="width:52px; text-align:center; font-family:monospace; color:var(--accent); font-weight:900;" title="Most points this rule can take off">-${val} pts</div> <button class="btn-step btn-plus" data-key="${key}">+</button> </div> </div>`;
        }).join('') + `<div style="text-align:right; font-size:0.72rem; color:var(--muted);">Active rules total: <b style="color:${total === 100 ? 'var(--ok)' : 'var(--danger)'}">${total} / 100 points</b>. An email that fails every active rule scores 0%.</div>`;

        container.querySelectorAll('.sensitivity-row').forEach(attachTooltip);
        container.querySelectorAll('.w-tog').forEach(c => c.onchange = () => { currentToggles[c.dataset.key] = c.checked; rebalanceWeights(c.dataset.key, c.checked ? 10 : 0); render(); });
        container.querySelectorAll('.w-slider').forEach(s => s.oninput = (e) => { rebalanceWeights(e.target.dataset.key, parseInt(e.target.value)); render(); });
        container.querySelectorAll('.btn-minus').forEach(b => b.onclick = () => stepWeight(b.dataset.key, -1));
        container.querySelectorAll('.btn-plus').forEach(b => b.onclick = () => stepWeight(b.dataset.key, 1));
        renderThresholds();
    }

    // Three lines on the 0-100 score: dangerous <= spam <= suspicious. Moving one pushes the others to keep that order.
    function renderThresholds() {
        const box = document.getElementById('threshold-container');
        if (!box) return;
        const zones = `
            <div style="display:flex; height:10px; border-radius:5px; overflow:hidden; margin:4px 0 14px;" title="Score bar: 0% on the left, 100% on the right">
                <div style="width:${thresholds.malicious}%; background:var(--danger);"></div>
                <div style="width:${thresholds.spam - thresholds.malicious}%; background:var(--warn, #d29200);"></div>
                <div style="width:${thresholds.suspicious - thresholds.spam}%; background:var(--accent);"></div>
                <div style="flex:1; background:var(--ok);"></div>
            </div>`;
        box.innerHTML = zones + ['malicious', 'spam', 'suspicious'].map(key => `
            <div style="display:flex; align-items:center; gap:12px; margin-bottom:8px;">
                <div style="flex:1.6; font-size:0.72rem; font-weight:700; color:${thresholdInfo[key].color};">${thresholdInfo[key].title}</div>
                <input type="range" class="t-slider" data-key="${key}" min="0" max="100" value="${thresholds[key]}" style="flex:2; cursor:pointer;">
                <div style="width:44px; text-align:right; font-family:monospace; font-weight:900; color:${thresholdInfo[key].color};">${thresholds[key]}%</div>
            </div>`).join('') + `
            <div style="font-size:0.72rem; color:var(--muted); margin-top:6px;">Emails above ${thresholds.suspicious}% are Safe and never touched.</div>`;
        box.querySelectorAll('.t-slider').forEach(s => s.oninput = (e) => {
            const key = e.target.dataset.key; const v = parseInt(e.target.value);
            thresholds[key] = v;
            if (key === 'malicious') { thresholds.spam = Math.max(thresholds.spam, v); thresholds.suspicious = Math.max(thresholds.suspicious, thresholds.spam); }
            if (key === 'spam') { thresholds.malicious = Math.min(thresholds.malicious, v); thresholds.suspicious = Math.max(thresholds.suspicious, v); }
            if (key === 'suspicious') { thresholds.spam = Math.min(thresholds.spam, v); thresholds.malicious = Math.min(thresholds.malicious, thresholds.spam); }
            renderThresholds();
        });
    }

    function loadFrom(rubrics) {
        const r = rubrics || DEFAULTS;
        currentWeights = {}; currentToggles = {};
        Object.keys(labels).forEach(k => {
            currentToggles[k] = r.toggles && r.toggles[k] !== undefined ? !!r.toggles[k] : true;
            currentWeights[k] = r.weights && r.weights[k] !== undefined ? Number(r.weights[k]) : DEFAULTS.weights[k];
        });
        thresholds = {
            malicious: r.maliciousThresholdPercent !== undefined ? r.maliciousThresholdPercent : DEFAULTS.maliciousThresholdPercent,
            spam: r.spamThresholdPercent !== undefined ? r.spamThresholdPercent : DEFAULTS.spamThresholdPercent,
            suspicious: r.suspiciousThresholdPercent !== undefined ? r.suspiciousThresholdPercent : DEFAULTS.suspiciousThresholdPercent
        };
    }

    async function open() {
        const cfg = await api.getConfig();
        loadFrom(cfg && cfg.rubrics);
        render();
        if (typeof window.openModal === 'function') {
            window.openModal('sensitivity-modal');
        } else {
            const m = document.getElementById('sensitivity-modal');
            if (m) m.style.display = 'flex';
        }
    }

    function close() {
        if (typeof window.closeModal === 'function') window.closeModal('sensitivity-modal');
        else document.getElementById('sensitivity-modal').style.display = 'none';
    }

    function initListeners() {
        const btn = document.getElementById('sensitivity-btn');
        if (btn) btn.onclick = open;

        const closeBtn = document.getElementById('close-sensitivity');
        if (closeBtn) closeBtn.onclick = close;

        const cancelBtn = document.getElementById('cancel-sensitivity');
        if (cancelBtn) cancelBtn.onclick = close;

        const saveBtn = document.getElementById('save-sensitivity');
        if (saveBtn) saveBtn.onclick = async () => {
            const cfg = await api.getConfig();
            await api.setRubrics({
                ...(cfg ? cfg.rubrics : {}),
                version: 2,
                weights: currentWeights,
                toggles: currentToggles,
                maliciousThresholdPercent: thresholds.malicious,
                spamThresholdPercent: thresholds.spam,
                suspiciousThresholdPercent: thresholds.suspicious
            });
            if (typeof window.showNotification === 'function') {
                window.showNotification('Anti-Spam Engine weights and thresholds updated.');
            }
            close();
        };

        const resetBtn = document.getElementById('reset-sensitivity-defaults');
        if (resetBtn) resetBtn.onclick = () => {
            loadFrom(DEFAULTS);
            render();
            if (typeof window.showNotification === 'function') {
                window.showNotification('Anti-spam engine weights reset to factory defaults.');
            }
        };
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initListeners);
    } else {
        setTimeout(initListeners, 50);
    }

    return { render: render, open: open };
})();
