window.escapeHtml = function(str) {
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

const api = window.securityApi;

window.AppState = {
    currentTab: 'dashboard',
    currentCategory: 'all',
    stats: { all: [], malicious: [], suspicious: [], spam: [], safe: [] },
    // Exact totals from the database. The lists above are capped, so their length is not a count.
    counts: null,
    config: null,
    
    updateStats(data) {
        if (!data) return;
        if (data.full && data.stats) {
            this.stats = data.stats;
        } else if (data.stats) {
            this.stats = data.stats;
        } else if (data.malicious || data.safe || data.suspicious || data.spam) {
            this.stats = data;
        }
        if (data.counts) this.counts = data.counts;
        this.renderDashboard(false);
    },
    
    renderDashboard(resetScroll = false) {
        const len = cat => (this.stats[cat] ? this.stats[cat].length : 0);
        const c = this.counts || { malicious: len('malicious'), suspicious: len('suspicious'), spam: len('spam'), safe: len('safe') };
        const totalAll = this.counts ? this.counts.total : c.malicious + c.suspicious + c.spam + c.safe;

        const elAll = document.getElementById('stat-all');
        if (elAll) elAll.textContent = totalAll.toLocaleString();
        for (const cat of ['malicious', 'suspicious', 'spam', 'safe']) {
            document.getElementById('stat-' + cat).textContent = (c[cat] || 0).toLocaleString();
        }
        
        if (window.vaultSearchActive && typeof window.executeVaultSearch === 'function') {
            window.executeVaultSearch();
            return;
        }

        let list;
        let title;
        if (this.currentCategory === 'all') {
            list = (this.stats.all && this.stats.all.length > 0)
                ? this.stats.all
                : [
                    ...(this.stats.malicious || []),
                    ...(this.stats.suspicious || []),
                    ...(this.stats.spam || []),
                    ...(this.stats.safe || [])
                ];
            title = "All Scanned Emails";
        } else {
            list = this.stats[this.currentCategory] || [];
            title = this.currentCategory.charAt(0).toUpperCase() + this.currentCategory.slice(1) + " Incidents";
        }

        window.renderList(list, this.currentCategory, resetScroll);
        const titleEl = document.getElementById('current-list-title');
        if (titleEl) titleEl.textContent = title;
    }
};

function switchTab(tabId) {
    if (window.AppState.currentTab === tabId) return;
    
    const oldTab = document.getElementById('tab-' + window.AppState.currentTab);
    const newTab = document.getElementById('tab-' + tabId);
    if (!newTab) return;
    
    if (oldTab) oldTab.classList.remove('active');
    setTimeout(() => {
        window.AppState.currentTab = tabId;
        document.querySelectorAll('.nav-item').forEach(p => {
            p.classList.remove('active');
            p.setAttribute('aria-selected', 'false');
        });
        const navItem = document.querySelector(`.nav-item[data-tab="${tabId}"]`);
        if (navItem) {
            navItem.classList.add('active');
            navItem.setAttribute('aria-selected', 'true');
        }
        
        newTab.classList.add('active');

        if (tabId === 'dashboard') {
            if (window.AppState.selectedId && window.incidentVirtualList && Array.isArray(window.incidentVirtualList.items)) {
                const idx = window.incidentVirtualList.items.findIndex(i => (i.entryId || i.fingerprint || i.id) === window.AppState.selectedId);
                if (idx !== -1) {
                    window.incidentVirtualList.scrollToIndex(idx);
                }
                window.incidentVirtualList.refreshCurrentSlice();
            }
        } else if (tabId === 'forensics') {
            if (typeof window.initForensicsWorkspace === 'function') {
                window.initForensicsWorkspace();
            }
        } else if (tabId === 'duplicates') {
            const dupView = document.getElementById('duplicate-view');
            if (dupView && (!dupView.children.length || dupView.innerHTML.trim() === '')) {
                if (typeof window.renderDuplicateInitialView === 'function') {
                    window.renderDuplicateInitialView();
                }
            }
        } else if (tabId === 'search') {
            if (typeof window.renderSearchView === 'function') {
                window.renderSearchView();
            }
        } else if (tabId === 'activity') {
            if (typeof window.loadActivityFeed === 'function') {
                window.loadActivityFeed();
            }
        }
    }, 50);
}

function switchCategory(cat) {
    const changed = (window.AppState.currentCategory !== cat);
    window.AppState.currentCategory = cat;

    const catSelect = document.getElementById('vault-search-category');
    if (catSelect) catSelect.value = cat;

    document.querySelectorAll('.stat-card').forEach(c => c.classList.remove('active'));
    const activeCard = document.getElementById(`card-${cat}`);
    if (activeCard) activeCard.classList.add('active');

    window.AppState.renderDashboard(changed);
}

let modalStack = [];

function openModal(id) {
    const m = document.getElementById(id);
    if (!m) return;
    const prevActive = document.activeElement;
    m.style.display = 'flex';
    setTimeout(() => {
        m.classList.add('active');
        const focusable = m.querySelectorAll('button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])');
        if (focusable.length > 0) {
            const input = m.querySelector('input:not([disabled])') || focusable[0];
            if (input && typeof input.focus === 'function') input.focus();
        }
    }, 10);

    if (modalStack.length > 0) {
        const top = modalStack[modalStack.length - 1];
        if (top.trapHandler) {
            document.removeEventListener('keydown', top.trapHandler);
        }
    }

    const trapHandler = (e) => {
        if (e.key !== 'Tab') return;
        const focusable = Array.from(m.querySelectorAll('button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')).filter(el => el.offsetParent !== null);
        if (focusable.length === 0) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (e.shiftKey) {
            if (document.activeElement === first || !m.contains(document.activeElement)) {
                e.preventDefault();
                last.focus();
            }
        } else {
            if (document.activeElement === last || !m.contains(document.activeElement)) {
                e.preventDefault();
                first.focus();
            }
        }
    };
    document.addEventListener('keydown', trapHandler);
    modalStack.push({ id, lastActiveElement: prevActive, trapHandler });
}

function closeModal(id) {
    const m = document.getElementById(id);
    if (!m) return;

    let entry = null;
    const idx = modalStack.findLastIndex ? modalStack.findLastIndex(e => e.id === id) : modalStack.map(e => e.id).lastIndexOf(id);
    if (idx !== -1) {
        entry = modalStack.splice(idx, 1)[0];
        if (entry.trapHandler) {
            document.removeEventListener('keydown', entry.trapHandler);
        }
    }

    if (modalStack.length > 0) {
        const top = modalStack[modalStack.length - 1];
        if (top.trapHandler) {
            document.addEventListener('keydown', top.trapHandler);
        }
    }

    m.classList.remove('active');
    setTimeout(() => {
        m.style.display = 'none';
        if (entry && entry.lastActiveElement && typeof entry.lastActiveElement.focus === 'function') {
            try { entry.lastActiveElement.focus(); } catch (e) {}
        }
    }, 300);
}

function handleOverlayClick(e, id) {
    if (e.target === document.getElementById(id)) {
        if (id === 'settings-modal') {
            closeSettings();
        } else if (id === 'confirm-modal') {
            closeConfirm();
        } else {
            closeModal(id);
        }
    }
}

function openSettings() {
    openModal('settings-modal');
    window.syncSettingsUI();
}

function closeSettings(force = false) {
    if (!force && window.settingsDirty) {
        showConfirm(
            "Discard Unsaved Changes?",
            "You have modified configuration settings that have not been saved. Are you sure you want to discard your changes and close?",
            () => {
                window.settingsDirty = false;
                closeModal('settings-modal');
            }
        );
        return;
    }
    window.settingsDirty = false;
    closeModal('settings-modal');
}

function closeForensics() {
    closeModal('forensics-modal');
}

function closeVirusTotalModal() {
    closeModal('virustotal-modal');
}
window.closeVirusTotalModal = closeVirusTotalModal;

let confirmCallback = null;
function showConfirm(title, msg, keyword, onExecute) {
    if (typeof keyword === 'function' && onExecute === undefined) {
        onExecute = keyword;
        keyword = null;
    }
    document.getElementById('confirm-title').textContent = title;
    document.getElementById('confirm-msg').textContent = msg;
    const inputCont = document.getElementById('confirm-input-container');
    const input = document.getElementById('confirm-input');
    const kwLabel = document.getElementById('confirm-keyword');
    const executeBtn = document.getElementById('confirm-execute-btn');
    
    input.value = '';
    input.style.borderColor = '';
    if (keyword) {
        inputCont.style.display = 'block';
        kwLabel.textContent = keyword;
        executeBtn.textContent = "Confirm Action";
    } else {
        inputCont.style.display = 'none';
        executeBtn.textContent = "Yes, Proceed";
    }
    
    openModal('confirm-modal');
    confirmCallback = () => {
        if (keyword && input.value.trim().toUpperCase() !== keyword.toUpperCase()) {
            input.style.borderColor = 'var(--danger)';
            input.focus();
            return;
        }
        if (typeof onExecute === 'function') {
            onExecute();
        }
        closeConfirm();
    };
    executeBtn.onclick = confirmCallback;
    input.onkeydown = (e) => {
        if (e.key === 'Enter') {
            confirmCallback();
        }
    };
}

function closeConfirm() {
    closeModal('confirm-modal');
    confirmCallback = null;
}

window.showConfirm = showConfirm;
window.showConfirmModal = function(title, msg, onExecute, keyword) {
    showConfirm(title, msg, keyword, onExecute);
};

async function requestFullScan() {
    window.showConfirm("Full Mailbox Audit", "This will perform a deep forensic scan of ALL items in your mailbox across all folders and archives. Depending on mailbox size, this may take several minutes. Proceed?", async () => {
        const btn = document.getElementById('full-scan-btn');
        if (btn) {
            btn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spin"><path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg> Initializing...`;
            btn.disabled = true;
        }
        await api.setEnabled(true);
        await api.setHistoryEnabled(true);
        if (api.startFullScan) {
            await api.startFullScan();
        }
    });
}

async function factoryReset() {
    showConfirm(
        "DANGER: Factory Reset",
        "This will permanently wipe all security databases, settings, and logs. This action cannot be undone. Are you sure?",
        null,
        async () => {
            try { localStorage.removeItem('deskguard_first_run_completed'); } catch (e) {}
            await api.resetApp();
        }
    );
}

window.addActivity = (msg, type = 'info') => {
    const stream = document.getElementById('activity-stream');
    if (!stream) return;
    
    const line = document.createElement('div');
    line.className = 'activity-line';
    
    const ts = new Date().toLocaleTimeString([], { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' });
    let msgClass = '';
    if (msg.includes('CRITICAL') || msg.includes('ERROR') || msg.includes('BLOCKED')) msgClass = 'danger';
    else if (msg.includes('Success') || msg.includes('Verified') || msg.includes('CLEAN')) msgClass = 'success';
    else if (msg.includes('Audit') || msg.includes('Engine') || msg.includes('Analyzing')) msgClass = 'highlight';

    const tsSpan = document.createElement('span');
    tsSpan.className = 'activity-ts';
    tsSpan.textContent = ts;

    const msgSpan = document.createElement('span');
    msgSpan.className = `activity-msg ${msgClass}`.trim();
    msgSpan.textContent = msg;

    line.appendChild(tsSpan);
    line.appendChild(msgSpan);
    
    stream.appendChild(line);
    stream.scrollTop = stream.scrollHeight;
    if (stream.children.length > 100) stream.removeChild(stream.firstChild);
};

window.addLog = (msg) => {
    window.addActivity(msg);
    const container = document.getElementById('live-logs');
    if (container) {
        const line = document.createElement('div');
        line.textContent = `[${new Date().toLocaleTimeString()}] ${msg}`;
        container.appendChild(line);
        container.scrollTop = container.scrollHeight;
        if (container.children.length > 500) container.removeChild(container.firstChild);
    }
};

api.onScanUpdate(data => {
    if (data.item) {
        const item = data.item;
        let cat = (item.verdict || 'safe').toLowerCase();
        if (cat === 'clean') cat = 'safe';
        if (!window.AppState.stats.all) window.AppState.stats.all = [];
        if (!window.AppState.stats.all.some(x => x.entryId === item.entryId)) {
            window.AppState.stats.all.unshift(item);
        }
        if (window.AppState.stats[cat]) {
            if (!window.AppState.stats[cat].some(x => x.entryId === item.entryId)) {
                window.AppState.stats[cat].unshift(item);
            }
        }
        if (data.stats) {
            window.AppState.counts = {
                total: data.stats.total,
                malicious: data.stats.malicious,
                suspicious: data.stats.suspicious,
                spam: data.stats.spam,
                safe: data.stats.safe
            };
        }
        window.AppState.renderDashboard(false);
    }

    const btn = document.getElementById('full-scan-btn');
    const progMini = document.getElementById('scan-progress-mini');
    const progBar = document.getElementById('scan-progress-bar');
    const progText = document.getElementById('scan-progress-text');
    const progDetails = document.getElementById('scan-progress-details');

    if (data.status === 'SCANNING' || data.status === 'History') {
        if (btn) {
            btn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spin"><path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg> Auditing...`;
            btn.disabled = true;
        }
        if (progMini) progMini.style.display = 'flex';
        if (data.total > 0 && progBar && progText) {
            const pct = Math.round((data.count / data.total) * 100);
            progBar.style.width = pct + '%';
            progText.textContent = pct + '%';
            if (progDetails) {
                const remaining = Math.max(0, data.total - data.count);
                progDetails.textContent = `${data.count.toLocaleString()} / ${data.total.toLocaleString()} emails (${remaining.toLocaleString()} left)`;
            }
        }
    } else if (data.status === 'Finished' && !data.verdict) {
        if (btn) {
            btn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"></circle><path d="M12 8v4l3 3"></path></svg> Full Audit`;
            btn.disabled = false;
        }
        if (progMini) progMini.style.display = 'none';
    }

    if (data.status === 'THREAT BLOCKED' || data.status === 'SPAM FILTERED' || data.status === 'Finished' || data.status === 'SCANNING' || data.status === 'INFO' || data.status === 'ERROR') {
        const prefix = data.status === 'THREAT BLOCKED' ? 'BLOCKED: ' : (data.status === 'SPAM FILTERED' ? 'SPAM: ' : (data.status === 'ERROR' ? 'CRITICAL: ' : ''));
        if (data.details) window.addActivity(prefix + data.details);
    }

    if (data.status === 'THREAT BLOCKED' || data.status === 'SPAM FILTERED') {
        window.showActionToast(data);
    }
});

api.onIndexStatus && api.onIndexStatus(info => {
    const el = document.getElementById('index-status');
    if (!el || !info) return;
    const count = Number(info.indexed || 0).toLocaleString();
    el.textContent = info.running ? `Indexing… ${count} emails searchable` : `${count} emails searchable`;
});

// Plain-English reasons for the short rule codes the engine reports.
const REASON_TEXT = [
    [/^SPF:/, 'Sending server is not allowed to send for this domain (SPF)'],
    [/^DKIM:/, 'Missing or broken digital signature (DKIM)'],
    [/^DMARC:/, 'Domain owner policy check failed (DMARC)'],
    [/^SENDER_MISALIGNMENT/, 'Sender address and bounce address belong to different companies'],
    [/^RDNS_MISSING/, 'Sending server has no name on the internet'],
    [/^RDNS_GENERIC/, 'Sent from a home or dynamic internet line'],
    [/^GLOBAL_RBL_HIT/, 'Sender is on a public spam blacklist'],
    [/^HEURISTICS:/, 'Contains typical spam words'],
    [/^HIDDEN_BODY_ENTROPY/, 'Contains hidden text or scripts'],
    [/^HEADER:X-Spam-Flag/, 'Already marked as spam by a mail server'],
    [/^MIME:DangerousAttachment/, 'Has a dangerous attachment type'],
    [/^ADVANCED_INTEL/, 'Matched a known malware signature'],
    [/^MS_SPAM_SCORE/, "Microsoft's own filter already rated it as spam"],
    [/^LINK_MISMATCH/, 'A link shows one web address but opens another site'],
    [/^DISPLAY_NAME_SPOOF/, 'Sender name pretends to be another person or company'],
    [/^LOOKALIKE_DOMAIN/, 'Sender domain imitates a known one (one or two letters different)'],
    [/^REPLYTO_MISMATCH/, 'Your reply would go to a different address than the sender'],
    [/^FIRST_CONTACT/, 'First email ever from this sender domain'],
    [/^Blacklisted/, 'Sender is on your block list']
];
// "SPF:fail[-22]" -> "-22 pts: Sending server is not allowed to send for this domain (SPF)"
window.explainTier = function(tier) {
    if (!tier || /^Clean \(/.test(tier)) return [];
    return String(tier).split('|').map(t => t.trim()).filter(Boolean).map(entry => {
        const m = entry.match(/^(.*?)\[(-?[\d.]+)\]$/);
        const code = m ? m[1] : entry;
        const hit = REASON_TEXT.find(([re]) => re.test(code));
        const text = hit ? hit[1] : code;
        return m ? `${m[2]} pts: ${text}` : text;
    });
};

// Adds a sender address to the whitelist (used by "Release + Trust sender").
window.trustSender = async function(sender) {
    if (!sender || !String(sender).includes('@')) return false;
    const cleanSender = String(sender).trim().toLowerCase();
    const cfg = await api.getConfig();
    let wlList = [];
    if (cfg && Array.isArray(cfg.whitelist)) {
        wlList = [...cfg.whitelist];
    } else if (cfg && cfg.whitelist && Array.isArray(cfg.whitelist.emails)) {
        wlList = [...cfg.whitelist.emails];
    }
    if (!wlList.map(e => String(e).toLowerCase()).includes(cleanSender)) {
        wlList.push(cleanSender);
    }
    await api.setWhitelist(wlList);
    if (typeof api.trustSender === 'function') {
        try { await api.trustSender(cleanSender); } catch (e) {}
    }
    return true;
};

// Every automatic move gets a visible card with the reason and one-click Undo / Trust sender.
window.showActionToast = function(data) {
    let stack = document.getElementById('action-toast-stack');
    if (!stack) {
        stack = document.createElement('div');
        stack.id = 'action-toast-stack';
        stack.style.cssText = 'position:fixed; bottom:20px; right:20px; z-index:99998; display:flex; flex-direction:column; gap:10px; width:380px; max-width:calc(100vw - 32px);';
        document.body.appendChild(stack);
    }
    while (stack.children.length >= 4) stack.removeChild(stack.firstChild);

    const isThreat = data.status === 'THREAT BLOCKED';
    const folder = data.currentFolder || (isThreat ? 'DeskGuard Quarantine' : 'Junk Email');
    const reasons = window.explainTier(data.tier);

    const card = document.createElement('div');
    card.style.cssText = `background:var(--surface-high, #1f2129); border:1px solid ${isThreat ? 'var(--danger, #d83b01)' : 'var(--warn, #d29200)'}; border-left-width:4px; border-radius:8px; padding:12px 14px; color:var(--text, #fff); font-size:0.8rem; box-shadow:0 8px 24px rgba(0,0,0,0.5);`;

    const title = document.createElement('div');
    title.style.cssText = 'font-weight:700; margin-bottom:4px;';
    title.textContent = `${isThreat ? 'Dangerous email' : 'Spam'} moved to "${folder}"`;
    const subj = document.createElement('div');
    subj.style.cssText = 'white-space:nowrap; overflow:hidden; text-overflow:ellipsis;';
    subj.textContent = `"${data.subject || data.details || '(No Subject)'}" from ${data.sender || 'unknown sender'}`;
    const why = document.createElement('div');
    why.style.cssText = 'color:var(--muted, #aaa); margin:6px 0 8px;';
    why.textContent = `Score ${data.score}%. Why: ${reasons.length ? reasons.join('; ') : 'low overall score'}.`;

    const actions = document.createElement('div');
    actions.style.cssText = 'display:flex; gap:8px; justify-content:flex-end;';
    const mkBtn = (label, primary) => {
        const b = document.createElement('button');
        b.textContent = label;
        b.style.cssText = `font-size:0.75rem; padding:4px 10px; border-radius:4px; cursor:pointer; border:1px solid var(--border, #444); background:${primary ? 'var(--accent, #0078d4)' : 'transparent'}; color:#fff;`;
        return b;
    };
    const undoBtn = mkBtn('Undo', true);
    const trustBtn = mkBtn('Undo + Trust sender', false);
    const closeBtn = mkBtn('×', false);
    closeBtn.title = 'Dismiss';
    actions.append(undoBtn, trustBtn, closeBtn);
    card.append(title, subj, why, actions);
    stack.appendChild(card);

    let timer = setTimeout(() => card.remove(), 20000);
    card.addEventListener('mouseenter', () => clearTimeout(timer));
    card.addEventListener('mouseleave', () => { timer = setTimeout(() => card.remove(), 8000); });
    closeBtn.onclick = () => card.remove();

    const release = async (trust) => {
        undoBtn.disabled = trustBtn.disabled = true;
        why.textContent = 'Moving back to Inbox…';
        const res = await api.releaseEmail({ entryId: data.entryId, fingerprint: data.fingerprint, storeId: data.storeId, subject: data.subject, targetFolder: 6 });
        if (!res || (!res.success && !res.ok)) {
            why.textContent = `Could not move it back: ${(res && res.error) || 'unknown error'}.`;
            undoBtn.disabled = trustBtn.disabled = false;
            return;
        }
        let msg = 'Moved back to Inbox. DeskGuard will not move it again.';
        if (trust) {
            try { if (await window.trustSender(data.sender)) msg = `Moved back to Inbox. ${data.sender} is now trusted.`; } catch (e) {}
        }
        why.textContent = msg;
        window.addActivity(`UNDO: "${data.subject || ''}" released to Inbox${trust ? ' and sender trusted' : ''}.`);
        clearTimeout(timer);
        timer = setTimeout(() => card.remove(), 5000);
    };
    undoBtn.onclick = () => release(false);
    trustBtn.onclick = () => release(true);
};

window.toggleProtectionPower = async function() {
    const btn = document.getElementById('dashboard-power-btn');
    const currentActive = btn ? btn.classList.contains('active') : true;
    const newState = !currentActive;

    if (btn) {
        btn.disabled = true;
        btn.style.opacity = '0.6';
    }

    try {
        await api.setEnabled(newState);
        window.updatePowerButtonUI(newState);
    } catch (e) {
        console.error('Failed to toggle protection:', e);
    } finally {
        if (btn) {
            btn.disabled = false;
            btn.style.opacity = '1';
        }
    }
};

window.updatePowerButtonUI = function(enabled) {
    const btn = document.getElementById('dashboard-power-btn');
    const text = document.getElementById('dashboard-power-text');
    if (!btn) return;
    btn.setAttribute('aria-checked', enabled ? 'true' : 'false');
    if (enabled) {
        btn.className = 'protection-toggle-btn active';
        if (text) text.textContent = 'Protection Active';
        btn.title = 'Real-Time Mailbox Protection is Active. Click to turn off.';
    } else {
        btn.className = 'protection-toggle-btn inactive';
        if (text) text.textContent = 'Protection Disabled';
        btn.title = 'Real-Time Mailbox Protection is Disabled. Click to turn on.';
    }
};

api.onStatusSync(enabled => {
    window.updatePowerButtonUI(enabled);
    const status = document.getElementById('engine-status');
    if (enabled) {
        status.className = 'badge badge-safe';
        status.textContent = 'Monitoring Active';
        window.updatePowerButtonUI(true);
        window.addActivity("System: Security Protection has been ACTIVATED.");
    } else {
        status.className = 'badge badge-malicious';
        status.textContent = 'Protection Off';
        window.addActivity("System: Security Protection is currently DISABLED.", "danger");
    }
});

api.onStatsUpdate(data => window.AppState.updateStats(data));

api.onLiveLog(msg => window.addLog(msg));

api.onOutlookStatus(statusData => {
    const statusEl = document.getElementById('engine-status');
    let state = 'Running';
    if (typeof statusData === 'string') {
        state = statusData;
    } else if (statusData && typeof statusData === 'object') {
        if (statusData.status) state = statusData.status;
        else if (statusData.running !== undefined) state = statusData.running ? 'Running' : (statusData.standby ? 'Standby' : 'Stopped');
    }

    const stateLower = (state || '').toLowerCase();
    const isRunning = stateLower === 'running' || stateLower === 'active';
    const isStandby = stateLower === 'standby' || stateLower.includes('idle') || stateLower.includes('waiting');

    if (statusEl) {
        if (isRunning) {
            statusEl.className = 'badge badge-safe';
            statusEl.textContent = 'Monitoring Active';
        } else if (isStandby) {
            statusEl.className = 'badge badge-spam';
            statusEl.textContent = 'Standby (Outlook Idle)';
        } else {
            statusEl.className = 'badge badge-malicious';
            statusEl.textContent = 'Protection Stopped';
        }
    }

    // Keep power button Active unless user explicitly disabled protection in config
    const isGloballyEnabled = !window.AppState || !window.AppState.config || window.AppState.config.enabled !== false;
    if (isGloballyEnabled) {
        window.updatePowerButtonUI(true);
    } else {
        window.updatePowerButtonUI(isRunning);
    }
});

window.winMinimize = () => api.minimizeWindow();
window.winMaximize = () => api.maximizeWindow ? api.maximizeWindow() : null;
window.winHide = () => api.hideWindow();
window.winClose = () => api.closeWindow ? api.closeWindow() : api.hideWindow();

function initFeedSplitter() {
    const splitter = document.getElementById('feed-splitter');
    const feed = document.getElementById('activity-feed-container');
    if (!splitter || !feed) return;

    try {
        const savedH = localStorage.getItem('deskguard_activity_feed_height');
        if (savedH) {
            const hNum = parseInt(savedH, 10);
            if (hNum >= 50 && hNum <= 600) {
                feed.style.height = `${hNum}px`;
            }
        }
    } catch {}

    splitter.addEventListener('mousedown', (e) => {
        e.preventDefault();
        const startY = e.pageY;
        const startH = feed.offsetHeight;
        splitter.classList.add('dragging');
        document.body.style.cursor = 'row-resize';
        document.body.style.userSelect = 'none';

        function onMouseMove(moveEvent) {
            const diff = startY - moveEvent.pageY;
            const newH = Math.max(50, Math.min(window.innerHeight * 0.6, startH + diff));
            feed.style.height = `${newH}px`;
        }

        function onMouseUp(upEvent) {
            const diff = startY - upEvent.pageY;
            const finalH = Math.max(50, Math.min(window.innerHeight * 0.6, startH + diff));
            feed.style.height = `${finalH}px`;
            try { localStorage.setItem('deskguard_activity_feed_height', String(finalH)); } catch {}
            splitter.classList.remove('dragging');
            document.body.style.cursor = '';
            document.body.style.userSelect = '';
            window.removeEventListener('mousemove', onMouseMove);
            window.removeEventListener('mouseup', onMouseUp);
        }

        window.addEventListener('mousemove', onMouseMove);
        window.addEventListener('mouseup', onMouseUp);
    });
}

async function init() {
    initFeedSplitter();
    const stats = await api.getStats();
    window.AppState.updateStats(stats);
    try {
        const cfg = await api.getConfig();
        window.AppState.config = cfg;
        if (cfg) {
            window.updatePowerButtonUI(cfg.enabled !== false);
            if (cfg.theme === 'light') {
                document.documentElement.setAttribute('data-theme', 'light');
            } else if (cfg.theme === 'system') {
                const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
                if (!prefersDark) document.documentElement.setAttribute('data-theme', 'light');
            } else {
                document.documentElement.removeAttribute('data-theme');
            }
            if (cfg.columnWidths && typeof cfg.columnWidths === 'object') {
                for (const [col, width] of Object.entries(cfg.columnWidths)) {
                    if (width) {
                        document.documentElement.style.setProperty(`--dash-col-${col}`, width);
                    }
                }
            }
        }
        let localCompleted = false;
        try {
            localCompleted = localStorage.getItem('deskguard_first_run_completed') === 'true';
        } catch (e) {}

        if (!localCompleted && cfg && cfg.firstRun === true) {
            setTimeout(() => {
                openModal('first-run-wizard-modal');
            }, 300);
        }
    } catch (e) {}
}

window.useRecommendedSettings = async function() {
    try {
        try { localStorage.setItem('deskguard_first_run_completed', 'true'); } catch (e) {}
        const recommendedRubrics = JSON.parse(JSON.stringify(window.DESKGUARD_DEFAULT_RUBRICS));
        await api.setRubrics(recommendedRubrics);
        await api.setThreatIntelLevel(1);
        await api.setFirstRun(false);
        closeModal('first-run-wizard-modal');
        window.showNotification('Recommended protection activated: 8-engine security & zero-API threat intelligence enabled.');
        addLog('First-time setup completed: Recommended security policy applied.');
    } catch (err) {
        console.error('Wizard error:', err);
        try { localStorage.setItem('deskguard_first_run_completed', 'true'); } catch (e) {}
        closeModal('first-run-wizard-modal');
    }
};

window.customizeWizardSettings = async function() {
    try {
        try { localStorage.setItem('deskguard_first_run_completed', 'true'); } catch (e) {}
        await api.setFirstRun(false);
        closeModal('first-run-wizard-modal');
        setTimeout(() => {
            if (window.SecurityUI && window.SecurityUI.Engines && window.SecurityUI.Engines.open) {
                window.SecurityUI.Engines.open();
            } else {
                openSettings();
            }
        }, 350);
    } catch (err) {
        try { localStorage.setItem('deskguard_first_run_completed', 'true'); } catch (e) {}
        closeModal('first-run-wizard-modal');
    }
};

window.showNotification = function(msg, isError = false, type = null) {
    if (typeof window.addLog === 'function') {
        window.addLog(msg);
    }
    let container = document.getElementById('toast-container');
    if (!container) {
        container = document.createElement('div');
        container.id = 'toast-container';
        document.body.appendChild(container);
    }

    const toast = document.createElement('div');
    const toastType = type || (isError ? 'error' : 'info');
    toast.className = `toast-item toast-${toastType}`;

    let iconSvg = '';
    if (toastType === 'error') {
        iconSvg = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="var(--danger)" stroke-width="2.5" style="flex-shrink:0;"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>';
    } else if (toastType === 'success') {
        iconSvg = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="var(--ok)" stroke-width="2.5" style="flex-shrink:0;"><path d="M20 6L9 17l-5-5"></path></svg>';
    } else if (toastType === 'warning') {
        iconSvg = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="var(--warn)" stroke-width="2.5" style="flex-shrink:0;"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>';
    } else {
        iconSvg = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="var(--accent)" stroke-width="2.5" style="flex-shrink:0;"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>';
    }

    const safeMsg = (typeof window.escapeHtml === 'function') ? window.escapeHtml(msg) : msg;
    toast.innerHTML = `
        <div style="display: flex; align-items: center; gap: 8px; flex: 1; min-width: 0;">
            ${iconSvg}
            <span style="word-break: break-word; line-height: 1.35;">${safeMsg}</span>
        </div>
        <button type="button" class="toast-close" title="Dismiss">&times;</button>
    `;

    const closeBtn = toast.querySelector('.toast-close');
    let timer = null;
    const dismiss = () => {
        clearTimeout(timer);
        toast.classList.remove('toast-show');
        setTimeout(() => {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
        }, 300);
    };

    if (closeBtn) closeBtn.onclick = dismiss;
    container.appendChild(toast);

    requestAnimationFrame(() => {
        toast.classList.add('toast-show');
    });

    const duration = isError ? 5000 : 3500;
    timer = setTimeout(dismiss, duration);
};

window.openModal = openModal;
window.closeModal = closeModal;
window.switchTab = switchTab;
window.switchCategory = switchCategory;
window.openSettings = openSettings;
window.closeSettings = closeSettings;
window.closeForensics = closeForensics;
window.requestFullScan = requestFullScan;
window.factoryReset = factoryReset;
window.handleOverlayClick = handleOverlayClick;
window.closeConfirm = closeConfirm;

init();

const originalOpenForensics = window.openForensics;
window.openForensics = (item) => {
    if (typeof window.openForensicsTab === 'function') {
        window.openForensicsTab(item);
    } else {
        openModal('forensics-modal');
        if (window.renderForensics) window.renderForensics(item);
    }
};

document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
        const confirmModal = document.getElementById('confirm-modal');
        if (confirmModal && confirmModal.classList.contains('active')) {
            closeConfirm();
            return;
        }
        const settingsModal = document.getElementById('settings-modal');
        if (settingsModal && settingsModal.classList.contains('active')) {
            closeSettings();
            return;
        }
        const activeModal = document.querySelector('.modal-overlay.active');
        if (activeModal) {
            closeModal(activeModal.id);
            return;
        }
    }
    if ((e.key === 'Enter' || e.key === ' ') && e.target && e.target.classList && e.target.classList.contains('nav-item')) {
        e.preventDefault();
        e.target.click();
        return;
    }
    const triageKeys = ['ArrowDown', 'ArrowUp', 'j', 'J', 'k', 'K', 'Enter', 'o', 'O', 'f', 'F', 's', 'S', 'q', 'Q', 'd', 'D', '?', '/'];
    if (window.AppState.currentTab === 'dashboard' && triageKeys.includes(e.key)) {
        if (['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName)) return;
        if (document.querySelector('.modal-overlay.active')) return;
        if (typeof window.handleListKeyNavigation === 'function') {
            window.handleListKeyNavigation(e);
        }
    }
    if (window.AppState.currentTab === 'forensics') {
        if (['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName)) {
            if (e.key === 'Escape') {
                document.activeElement.blur();
                return;
            }
            return;
        }
        if (document.querySelector('.modal-overlay.active')) return;
        if (e.key === 'Escape' || e.key === 'Backspace') {
            e.preventDefault();
            window.switchTab('dashboard');
            return;
        }
        if (e.key === 'ArrowUp' || e.key === 'k' || e.key === 'K') {
            e.preventDefault();
            if (typeof window.forensicsNavigatePrev === 'function') window.forensicsNavigatePrev();
            return;
        }
        if (e.key === 'ArrowDown' || e.key === 'j' || e.key === 'J') {
            e.preventDefault();
            if (typeof window.forensicsNavigateNext === 'function') window.forensicsNavigateNext();
            return;
        }
        if (e.key === 'ArrowLeft' || e.key === '[' || e.key === 'p' || e.key === 'P') {
            e.preventDefault();
            if (typeof window.forensicsNavigatePrev === 'function') window.forensicsNavigatePrev();
            return;
        }
        if (e.key === 'ArrowRight' || e.key === ']' || e.key === 'n' || e.key === 'N') {
            e.preventDefault();
            if (typeof window.forensicsNavigateNext === 'function') window.forensicsNavigateNext();
            return;
        }
        if (e.key === 'o' || e.key === 'O') {
            e.preventDefault();
            if (typeof window.forensicsOpenInOutlook === 'function') window.forensicsOpenInOutlook();
            return;
        }
        if (e.key === 's' || e.key === 'S') {
            e.preventDefault();
            if (typeof window.forensicsScanAv === 'function') window.forensicsScanAv();
            return;
        }
        if (e.key === 'q' || e.key === 'Q') {
            e.preventDefault();
            if (typeof window.forensicsQuarantine === 'function') window.forensicsQuarantine();
            return;
        }
        if (e.key === 'd' || e.key === 'D') {
            e.preventDefault();
            if (typeof window.forensicsDelete === 'function') window.forensicsDelete();
            return;
        }
    }
});

window.vaultSearchActive = false;
let vaultSearchDebounceTimer = null;
let senderSuggestDebounceTimer = null;

window.executeVaultSearch = async function() {
    const searchInput = document.getElementById('vault-search-input');
    const fromInput = document.getElementById('vault-from-input');
    const scopeSelect = document.getElementById('vault-search-scope');
    const clearBtn = document.getElementById('vault-search-clear');
    const searchBtn = document.getElementById('vault-search-btn');

    const query = searchInput ? searchInput.value.trim() : '';
    const fromFilter = fromInput ? fromInput.value.trim() : '';
    const scope = scopeSelect ? scopeSelect.value : 'all';
    const categorySelect = document.getElementById('vault-search-category');
    const category = categorySelect ? categorySelect.value : 'all';

    const hasQuery = query.length > 0 || fromFilter.length > 0;
    if (clearBtn) clearBtn.style.display = hasQuery ? 'inline-flex' : 'none';

    if (!hasQuery) {
        if (window.vaultSearchActive) {
            window.vaultSearchActive = false;
            window.AppState.renderDashboard(false);
        }
        return;
    }

    window.vaultSearchActive = true;
    if (searchBtn) searchBtn.textContent = 'Searching...';

    try {
        const sortBy = window.sortOrder ? window.sortOrder.key : 'date';
        const sortOrder = (window.sortOrder && window.sortOrder.asc) ? 'asc' : 'desc';

        const res = await api.searchVault({
            query,
            scope,
            category,
            fromFilter,
            sortBy,
            sortOrder,
            limit: 500
        });

        if (res && (res.ok || res.success)) {
            const rows = (res.results && res.results.rows) || res.rows || res.items || [];
            const total = (res.results && res.results.total) ?? res.total ?? rows.length;
            window.renderList(rows, category, false);
            const titleEl = document.getElementById('current-list-title');
            if (titleEl) {
                titleEl.textContent = `Vault Search (${total} matching)`;
            }
        }
    } catch (err) {
        console.error('[Vault Search Error]:', err);
    } finally {
        if (searchBtn) searchBtn.innerHTML = '<span>🔍</span> Search';
    }
};

window.clearVaultSearch = function() {
    const searchInput = document.getElementById('vault-search-input');
    const fromInput = document.getElementById('vault-from-input');
    const clearBtn = document.getElementById('vault-search-clear');
    if (searchInput) searchInput.value = '';
    if (fromInput) fromInput.value = '';
    if (clearBtn) clearBtn.style.display = 'none';
    if (typeof window.syncFilterChipStyles === 'function') window.syncFilterChipStyles();
    window.vaultSearchActive = false;
    window.AppState.renderDashboard(true);
};

function initVaultSearchToolbar() {
    const searchInput = document.getElementById('vault-search-input');
    const fromInput = document.getElementById('vault-from-input');
    const scopeSelect = document.getElementById('vault-search-scope');
    const datalist = document.getElementById('from-suggestions');

    if (searchInput) {
        searchInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                clearTimeout(vaultSearchDebounceTimer);
                window.executeVaultSearch();
            }
        });
        searchInput.addEventListener('input', () => {
            if (typeof window.syncFilterChipStyles === 'function') window.syncFilterChipStyles();
            clearTimeout(vaultSearchDebounceTimer);
            vaultSearchDebounceTimer = setTimeout(() => {
                window.executeVaultSearch();
            }, 250);
        });
    }

    if (fromInput) {
        fromInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                clearTimeout(vaultSearchDebounceTimer);
                window.executeVaultSearch();
            }
        });
    }

    const categorySelect = document.getElementById('vault-search-category');
    if (categorySelect) {
        categorySelect.addEventListener('change', () => {
            if (window.vaultSearchActive || (searchInput && searchInput.value.trim())) {
                window.executeVaultSearch();
            }
        });
    }

    if (scopeSelect) {
        scopeSelect.addEventListener('change', () => {
            if (window.vaultSearchActive || (searchInput && searchInput.value.trim())) {
                window.executeVaultSearch();
            }
        });
    }

    if (fromInput) {
        fromInput.addEventListener('input', () => {
            const val = fromInput.value.trim();
            clearTimeout(senderSuggestDebounceTimer);
            senderSuggestDebounceTimer = setTimeout(async () => {
                if (val.length >= 1 && datalist) {
                    try {
                        const res = await api.getSenderSuggestions(val);
                        const list = Array.isArray(res) ? res : (res && (res.suggestions || res.items)) || [];
                        if (list.length > 0) {
                            datalist.innerHTML = list
                                .map(s => `<option value="${window.escapeHtml(s)}"></option>`)
                                .join('');
                        }
                    } catch (e) {}
                }
            }, 150);

            clearTimeout(vaultSearchDebounceTimer);
            vaultSearchDebounceTimer = setTimeout(() => {
                window.executeVaultSearch();
            }, 250);
        });
    }
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initVaultSearchToolbar);
} else {
    initVaultSearchToolbar();
}


function initTitlebarDrag() {
    const titlebar = document.getElementById('app-titlebar') || document.querySelector('.titlebar');
    if (titlebar) {
        titlebar.addEventListener('mousedown', (e) => {
            if (e.target.closest('.titlebar-controls') || e.target.closest('button')) return;
            if (e.button === 0 && window.winDrag) {
                window.winDrag();
            }
        });
        titlebar.addEventListener('dblclick', (e) => {
            if (e.target.closest('.titlebar-controls') || e.target.closest('button')) return;
            if (window.winMaximize) {
                window.winMaximize();
            }
        });
    }
}

function initStatCardsKeyboard() {
    document.querySelectorAll('.stat-card').forEach(card => {
        card.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                card.click();
            }
        });
    });
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        initTitlebarDrag();
        initStatCardsKeyboard();
        initUpdateSystem();
    });
} else {
    initTitlebarDrag();
    initStatCardsKeyboard();
    initUpdateSystem();
}

// Software Update & What's New System
window.__latestDownloadUrl = '';

function formatChangelogToHtml(markdown) {
    if (!markdown) return '<p>No details available.</p>';
    const lines = markdown.split('\n');
    let html = '';
    let inList = false;

    for (let rawLine of lines) {
        let line = rawLine.trim();
        if (!line) {
            if (inList) { html += '</ul>'; inList = false; }
            continue;
        }

        if (line.startsWith('## ')) {
            if (inList) { html += '</ul>'; inList = false; }
            html += `<h3 style="color: var(--accent); margin: 16px 0 8px 0; font-size: 1.05rem; border-bottom: 1px solid var(--border); padding-bottom: 4px;">${escapeHtml(line.slice(3))}</h3>`;
        } else if (line.startsWith('### ')) {
            if (inList) { html += '</ul>'; inList = false; }
            html += `<h4 style="color: var(--ok); margin: 14px 0 6px 0; font-size: 0.9rem;">${escapeHtml(line.slice(4))}</h4>`;
        } else if (line.startsWith('- ')) {
            if (!inList) { html += '<ul style="margin: 0 0 10px 18px; padding: 0;">'; inList = true; }
            let itemText = line.slice(2);
            itemText = itemText.replace(/\*\*(.*?)\*\*/g, '<strong style="color: var(--text);">$1</strong>');
            itemText = itemText.replace(/`([^`]+)`/g, '<code style="background: rgba(255,255,255,0.08); padding: 1px 4px; border-radius: 4px; font-size: 0.82em;">$1</code>');
            html += `<li style="margin-bottom: 5px; color: var(--muted);">${itemText}</li>`;
        } else {
            if (inList) { html += '</ul>'; inList = false; }
            html += `<p style="margin: 0 0 8px 0; color: var(--muted);">${escapeHtml(line)}</p>`;
        }
    }

    if (inList) html += '</ul>';
    return html;
}

window.showWhatsNewModal = async function() {
    try {
        const res = await api.getWhatsNew();
        if (res) {
            const verSub = document.getElementById('whats-new-version-sub');
            if (verSub) verSub.textContent = `DeskGuard v${res.version} - What's New & Security Improvements`;
            const contentEl = document.getElementById('whats-new-content');
            if (contentEl) contentEl.innerHTML = formatChangelogToHtml(res.notes);
            const chkAuto = document.getElementById('chk-auto-check-updates');
            if (chkAuto && typeof res.autoCheck === 'boolean') chkAuto.checked = res.autoCheck;
            openModal('whats-new-modal');
        }
    } catch (e) {
        console.error('Error fetching what\'s new:', e);
    }
};

window.closeWhatsNewModal = async function() {
    try {
        await api.dismissWhatsNew();
    } catch (e) { }
    closeModal('whats-new-modal');
};

window.toggleAutoCheckUpdates = async function(enabled) {
    try {
        await api.setAutoCheckUpdates(enabled);
        const chkModal = document.getElementById('chk-auto-check-updates');
        if (chkModal) chkModal.checked = enabled;
        const chkSettings = document.getElementById('cfg-autoupdate');
        if (chkSettings) chkSettings.checked = enabled;
        if (typeof showToast === 'function') {
            showToast(enabled ? 'Automatic update checks enabled.' : 'Automatic update checks disabled.', 'info');
        }
    } catch (e) { }
};

window.manualCheckForUpdates = async function() {
    const btn = document.getElementById('btn-check-updates-settings');
    const statusEl = document.getElementById('settings-update-status');
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Checking...';
    }
    if (statusEl) {
        statusEl.style.display = 'block';
        statusEl.style.background = 'rgba(0, 120, 212, 0.12)';
        statusEl.style.color = 'var(--accent)';
        statusEl.style.border = '1px solid var(--accent)';
        statusEl.textContent = 'Querying GitHub for latest releases...';
    }

    try {
        const res = await api.checkForUpdates();
        if (res && res.hasUpdate) {
            if (statusEl) {
                statusEl.style.background = 'rgba(107, 183, 0, 0.15)';
                statusEl.style.color = 'var(--ok)';
                statusEl.style.border = '1px solid var(--ok)';
                statusEl.textContent = `New version v${res.latestVersion} is available!`;
            }
            window.showUpdateModal(res);
        } else if (res && res.error) {
            if (statusEl) {
                statusEl.style.background = 'rgba(241, 112, 123, 0.15)';
                statusEl.style.color = 'var(--danger)';
                statusEl.style.border = '1px solid var(--danger)';
                statusEl.textContent = res.error;
            }
            if (typeof showToast === 'function') showToast('Update check: ' + res.error, 'warning');
        } else {
            if (statusEl) {
                statusEl.style.background = 'rgba(107, 183, 0, 0.12)';
                statusEl.style.color = 'var(--ok)';
                statusEl.style.border = '1px solid var(--ok)';
                statusEl.textContent = `DeskGuard is up to date (v${res ? res.currentVersion : ''}).`;
            }
            if (typeof showToast === 'function') showToast(`DeskGuard is up to date (v${res ? res.currentVersion : ''}).`, 'success');
        }
    } catch (err) {
        if (statusEl) {
            statusEl.style.background = 'rgba(241, 112, 123, 0.15)';
            statusEl.style.color = 'var(--danger)';
            statusEl.style.border = '1px solid var(--danger)';
            statusEl.textContent = 'Failed to check: ' + err.message;
        }
    } finally {
        if (btn) {
            btn.disabled = false;
            btn.textContent = 'Check for Updates';
        }
    }
};

window.showUpdateModal = function(updateData) {
    if (!updateData) return;
    const verEl = document.getElementById('update-modal-ver');
    if (verEl) verEl.textContent = `v${updateData.latestVersion}`;
    const curEl = document.getElementById('update-modal-cur-ver');
    if (curEl) curEl.textContent = `v${updateData.currentVersion}`;
    const notesEl = document.getElementById('update-modal-notes');
    if (notesEl) notesEl.textContent = updateData.releaseNotes || 'Security maintenance and stability update.';
    window.__latestDownloadUrl = updateData.downloadUrl || '';
    openModal('update-available-modal');
};

window.downloadAndApplyUpdate = function() {
    if (window.__latestDownloadUrl) {
        api.openExternalUrl(window.__latestDownloadUrl);
        closeModal('update-available-modal');
        if (typeof showToast === 'function') {
            showToast('Opening update download in your browser...', 'info');
        }
    }
};

function initUpdateSystem() {
    if (api && api.getWhatsNew) {
        api.getWhatsNew().then(res => {
            if (res && res.isFirstRun) {
                setTimeout(() => window.showWhatsNewModal(), 600);
            }
        }).catch(() => {});
    }

    if (api && api.onUpdateAvailable) {
        api.onUpdateAvailable(res => {
            if (res && res.hasUpdate) {
                if (typeof showToast === 'function') {
                    showToast(`DeskGuard v${res.latestVersion} is available! Click Settings to update.`, 'info');
                }
            }
        });
    }
}
