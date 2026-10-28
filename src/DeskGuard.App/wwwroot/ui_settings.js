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
SecurityUI.Settings = (function() {
    const api = window.securityApi;
    let activeTab = 'general';
    let currentConfig = null;
    let localWhitelist = [];
    let localBlacklist = [];
    let localSpamKeywords = [];

    const intelExplanations = {
        0: {
            title: "Level 0: Local PC Only (Maximum Privacy)",
            badge: "Offline / Private",
            badgeClass: "badge-safe",
            summary: "All email security checks happen 100% inside your computer. DeskGuard inspects sender authentication (SPF, DKIM, DMARC), email headers, and heuristic patterns strictly on your PC. No file hashes, sender information, or queries are ever sent over the internet.",
            bestFor: "Best for: High-privacy environments, offline computers, and strict no-cloud policies."
        },
        1: {
            title: "Level 1: Smart Community Defense (Recommended)",
            badge: "Smart Hybrid",
            badgeClass: "badge-safe",
            summary: "DeskGuard checks your email locally first. Only if an email looks suspicious or unverified does it query global threat intelligence (Team Cymru DNS hash lookup and VirusTotal if an API key is connected). Safe emails from regular senders are never checked against the cloud.",
            bestFor: "Best for: 99% of users. Maximum protection, instant speeds, and zero unnecessary data sharing."
        },
        2: {
            title: "Level 2: Maximum Cloud Inspection (Aggressive)",
            badge: "Aggressive",
            badgeClass: "badge-spam",
            summary: "Every incoming attachment, link, and file hash is rigorously checked against 70+ cloud antivirus engines via VirusTotal and community databases, regardless of whether the email looks safe. Delivers zero-tolerance malware protection.",
            bestFor: "Best for: High-risk environments, finance/exec accounts, and users frequently opening unknown files."
        }
    };

    function updateIntelExplainer(val) {
        const explainerEl = document.getElementById('intel-explainer-card');
        if (!explainerEl) return;
        const info = intelExplanations[val] || intelExplanations[1];
        explainerEl.innerHTML = `
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px;">
                <div style="font-weight: 700; font-size: 0.8rem; color: var(--text);">${info.title}</div>
                <span class="badge ${info.badgeClass}" style="font-size: 0.65rem;">${info.badge}</span>
            </div>
            <div style="font-size: 0.74rem; color: var(--muted); line-height: 1.45; margin-bottom: 6px;">
                ${info.summary}
            </div>
            <div style="font-size: 0.72rem; color: var(--accent); font-weight: 600;">
                ${info.bestFor}
            </div>
        `;
    }

    function switchSettingsTab(tabName) {
        activeTab = tabName;
        document.querySelectorAll('.settings-tab-btn').forEach(btn => {
            btn.classList.toggle('active', btn.getAttribute('data-tab') === tabName);
        });
        document.querySelectorAll('.settings-tab-content').forEach(pane => {
            pane.classList.toggle('active', pane.getAttribute('data-pane') === tabName);
        });
    }

    function filterSettingsSearch(query) {
        const q = (query || '').toLowerCase().trim();
        const cards = document.querySelectorAll('.setting-item-card');
        cards.forEach(card => {
            if (!q) {
                card.style.display = '';
            } else {
                const text = card.textContent.toLowerCase();
                card.style.display = text.includes(q) ? '' : 'none';
            }
        });
    }

    function renderTagList(containerId, list, onRemoveFnName) {
        const el = document.getElementById(containerId);
        if (!el) return;
        if (list.length === 0) {
            el.innerHTML = '<span style="color: var(--muted); font-size: 0.75rem; font-style: italic;">None configured</span>';
            return;
        }
        el.innerHTML = list.map((item, idx) => `
            <span class="tag-pill">
                ${window.escapeHtml(item)}
                <span class="tag-remove" onclick="${onRemoveFnName}(${idx})" title="Remove item">&times;</span>
            </span>
        `).join('');
    }

    window.removeWhitelistItem = (idx) => {
        localWhitelist.splice(idx, 1);
        renderTagList('whitelist-tags', localWhitelist, 'window.removeWhitelistItem');
        markDirty();
    };

    window.addWhitelistItem = () => {
        const input = document.getElementById('new-whitelist-input');
        if (!input) return;
        const val = input.value.trim().toLowerCase();
        if (val && !localWhitelist.includes(val)) {
            localWhitelist.push(val);
            renderTagList('whitelist-tags', localWhitelist, 'window.removeWhitelistItem');
            input.value = '';
            markDirty();
        }
    };

    window.removeBlacklistItem = (idx) => {
        localBlacklist.splice(idx, 1);
        renderTagList('blacklist-tags', localBlacklist, 'window.removeBlacklistItem');
        markDirty();
    };

    window.addBlacklistItem = () => {
        const input = document.getElementById('new-blacklist-input');
        if (!input) return;
        const val = input.value.trim().toLowerCase();
        if (val && !localBlacklist.includes(val)) {
            localBlacklist.push(val);
            renderTagList('blacklist-tags', localBlacklist, 'window.removeBlacklistItem');
            input.value = '';
            markDirty();
        }
    };

    window.removeSpamKeywordItem = (idx) => {
        localSpamKeywords.splice(idx, 1);
        renderTagList('spam-keyword-tags', localSpamKeywords, 'window.removeSpamKeywordItem');
        markDirty();
    };

    window.addSpamKeywordItem = () => {
        const input = document.getElementById('new-spam-keyword-input');
        if (!input) return;
        const val = input.value.trim().toLowerCase();
        if (val && !localSpamKeywords.includes(val)) {
            localSpamKeywords.push(val);
            renderTagList('spam-keyword-tags', localSpamKeywords, 'window.removeSpamKeywordItem');
            input.value = '';
            markDirty();
        }
    };

    function markDirty() {
        window.settingsDirty = true;
        const ind = document.getElementById('settings-unsaved-indicator');
        if (ind) ind.style.display = 'inline-block';
    }

    async function sync() {
        const config = await api.getConfig();
        currentConfig = config;
        localWhitelist = [...(config.whitelist || [])];
        localBlacklist = [...(config.blacklist || [])];
        localSpamKeywords = [...(config.spamKeywords || [])];

        let startupInfo = { configEnabled: !!config.launchAtStartup, systemActive: !!config.launchAtStartup };
        try {
            if (api.checkStartup) startupInfo = await api.checkStartup();
        } catch (e) {}

        const startupActive = startupInfo.systemActive !== undefined ? startupInfo.systemActive : !!config.launchAtStartup;
        const hasVtKey = !!(config.vtApiKey && config.vtApiKey !== 'MASKED_FOR_SECURITY');
        const currentIntelLevel = (config.threatIntelligenceLevel !== undefined && config.threatIntelligenceLevel !== null) ? parseInt(config.threatIntelligenceLevel) : 1;
        const currentTheme = config.theme || 'dark';
        const currentScope = config.historyScanScope || (config.deepHistoryScanEnabled ? 'all' : 'past7days');

        const body = document.getElementById('settings-body');
        if (!body) return;

        body.innerHTML = `
            <!-- Top Search Bar -->
            <div class="settings-search-bar" style="margin: -22px -24px 16px -24px;">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="var(--muted)" stroke-width="2"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>
                <input type="text" id="settings-search-input" placeholder="Search settings (e.g. startup, theme, virustotal, engines, whitelist)..." oninput="SecurityUI.Settings.onSearch(this.value)" autocomplete="off">
            </div>

            <!-- 5-Tab Navigation -->
            <div class="settings-tab-nav" style="margin: 0 -24px 20px -24px;">
                <button type="button" class="settings-tab-btn ${activeTab === 'general' ? 'active' : ''}" data-tab="general" onclick="SecurityUI.Settings.switchTab('general')">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M12 15.5A3.5 3.5 0 0 1 8.5 12 3.5 3.5 0 0 1 12 8.5a3.5 3.5 0 0 1 3.5 3.5 3.5 3.5 0 0 1-3.5 3.5m7.43-2.53c.04-.32.07-.64.07-.97 0-.33-.03-.66-.07-1l2.11-1.63c.19-.15.24-.42.12-.64l-2-3.46c-.12-.22-.39-.31-.61-.22l-2.49 1c-.52-.39-1.06-.73-1.69-.98l-.37-2.65A.506.506 0 0 0 14 2h-4c-.25 0-.46.18-.5.42l-.37 2.65c-.63.25-1.17.59-1.69.98l-2.49-1c-.22-.09-.49 0-.61.22l-2 3.46c-.13.22-.07.49.12.64L4.57 11c-.04.34-.07.67-.07 1 0 .33.03.65.07.97l-2.11 1.66c-.19.15-.25.42-.12.64l2 3.46c.12.22.39.3.61.22l2.49-1.01c.52.4 1.06.74 1.69.99l.37 2.65c.04.24.25.42.5.42h4c.25 0 .46-.18.5-.42l.37-2.65c.63-.26 1.17-.59 1.69-.99l2.49 1.01c.22.08.49 0 .61-.22l2-3.46c.12-.22.07-.49-.12-.64l-2.11-1.66Z"/></svg>
                    General &amp; Startup
                </button>
                <button type="button" class="settings-tab-btn ${activeTab === 'engines' ? 'active' : ''}" data-tab="engines" onclick="SecurityUI.Settings.switchTab('engines')">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z"/></svg>
                    Anti-Spam Engines
                </button>
                <button type="button" class="settings-tab-btn ${activeTab === 'threatcloud' ? 'active' : ''}" data-tab="threatcloud" onclick="SecurityUI.Settings.switchTab('threatcloud')">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z"/></svg>
                    Threat Cloud &amp; VirusTotal
                </button>
                <button type="button" class="settings-tab-btn ${activeTab === 'rules' ? 'active' : ''}" data-tab="rules" onclick="SecurityUI.Settings.switchTab('rules')">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-5 14H7v-2h7v2zm3-4H7v-2h10v2zm0-4H7V7h10v2z"/></svg>
                    Senders &amp; Rules
                </button>
                <button type="button" class="settings-tab-btn ${activeTab === 'maintenance' ? 'active' : ''}" data-tab="maintenance" onclick="SecurityUI.Settings.switchTab('maintenance')">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M22.7 19l-9.1-9.1c.9-2.3.4-5-1.5-6.9-2-2-5-2.4-7.4-1.3L9 6 6 9 1.6 4.7C.4 7.1.9 10.1 2.9 12.1c1.9 1.9 4.6 2.4 6.9 1.5l9.1 9.1c.4.4 1 .4 1.4 0l2.3-2.3c.5-.4.5-1.1.1-1.4z"/></svg>
                    Maintenance &amp; Danger Zone
                </button>
            </div>

            <!-- TAB 1: GENERAL & STARTUP -->
            <div class="settings-tab-content ${activeTab === 'general' ? 'active' : ''}" data-pane="general">
                <div style="display: flex; flex-direction: column; gap: 16px;">
                    <!-- Theme Selector -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 16px 20px; border-radius: 8px; border: 1px solid var(--border); display: flex; align-items: center; justify-content: space-between;">
                        <div>
                            <div style="font-weight: 600; font-size: 0.88rem; color: var(--text);">Interface Appearance (Theme)</div>
                            <div style="font-size: 0.75rem; color: var(--muted); margin-top: 2px;">Select dark or light color scheme for the application window</div>
                        </div>
                        <select id="cfg-theme" onchange="SecurityUI.Settings.onThemeChange(this.value)" style="font-size: 0.8rem; padding: 6px 12px; border-radius: 6px; background: var(--surface-mid); color: var(--text); border: 1px solid var(--border);">
                            <option value="dark" ${currentTheme === 'dark' ? 'selected' : ''}>Dark Mode (Default)</option>
                            <option value="light" ${currentTheme === 'light' ? 'selected' : ''}>Light Mode</option>
                            <option value="system" ${currentTheme === 'system' ? 'selected' : ''}>Match Windows System</option>
                        </select>
                    </div>

                    <!-- Windows Startup -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 16px 20px; border-radius: 8px; border: 1px solid var(--border); display: flex; align-items: center; justify-content: space-between;">
                        <div>
                            <div style="font-weight: 600; font-size: 0.88rem; color: var(--text);">Run on Windows Startup (Boot)</div>
                            <div style="font-size: 0.75rem; color: var(--muted); margin-top: 2px;">Automatically launch DeskGuard in background minimized to system tray when Windows boots</div>
                            <div id="startup-status-badge" style="margin-top: 6px;">
                                <span class="badge ${startupActive ? 'badge-safe' : 'badge-spam'}" style="font-size: 0.7rem; padding: 3px 8px;">
                                    ${startupActive ? '● Auto-Start Active' : '○ Auto-Start Disabled'}
                                </span>
                            </div>
                        </div>
                        <label class="switch">
                            <input type="checkbox" id="cfg-startup" ${startupActive ? 'checked' : ''} onchange="SecurityUI.Settings.onStartupToggle(this)">
                            <span class="slider"></span>
                        </label>
                    </div>

                    <!-- Real-Time Protection Switch -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 16px 20px; border-radius: 8px; border: 1px solid var(--border); display: flex; align-items: center; justify-content: space-between;">
                        <div>
                            <div style="font-weight: 600; font-size: 0.88rem; color: var(--text);">Real-Time Protection (Master Switch)</div>
                            <div style="font-size: 0.75rem; color: var(--muted); margin-top: 2px;">Continuous live monitoring of inbound emails arriving in your Outlook folders</div>
                        </div>
                        <label class="switch">
                            <input type="checkbox" id="cfg-enabled" ${config.enabled ? 'checked' : ''}>
                            <span class="slider"></span>
                        </label>
                    </div>

                    <!-- On-Access Scan Mode -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 16px 20px; border-radius: 8px; border: 1px solid var(--border); display: flex; align-items: center; justify-content: space-between;">
                        <div>
                            <div style="font-weight: 600; font-size: 0.88rem; color: var(--text);">On-Access Scan Mode</div>
                            <div style="font-size: 0.75rem; color: var(--muted); margin-top: 2px;">Only scan unread and newly arriving messages immediately upon arrival</div>
                        </div>
                        <label class="switch">
                            <input type="checkbox" id="cfg-onaccess" ${config.onAccessEnabled !== false ? 'checked' : ''}>
                            <span class="slider"></span>
                        </label>
                    </div>

                    <!-- Deep Local Antivirus Inspection (Scan Every Email) -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 16px 20px; border-radius: 8px; border: 1px solid var(--border); display: flex; align-items: center; justify-content: space-between;">
                        <div style="flex: 1; padding-right: 16px;">
                            <div style="display: flex; align-items: center; gap: 8px;">
                                <div style="font-weight: 600; font-size: 0.88rem; color: var(--text);">Scan Every Email with Local Antivirus (No Exceptions)</div>
                                <span class="badge badge-safe" style="font-size: 0.65rem;">Defender &amp; ClamAV</span>
                            </div>
                            <div style="font-size: 0.75rem; color: var(--muted); margin-top: 3px; line-height: 1.4;">
                                Disassembles emails into discrete components; attachments are isolated and scanned individually via Microsoft Defender and ClamAV. Newly arriving and unread emails are scanned on-access with highest priority; historical mail is progressively scanned in the background and tracked so no email is ever scanned twice.
                            </div>
                        </div>
                        <label class="switch" style="flex-shrink: 0;">
                            <input type="checkbox" id="cfg-deep-local-av-all" ${config.deepLocalAvScanAll ? 'checked' : ''}>
                            <span class="slider"></span>
                        </label>
                    </div>

                    <!-- Historical Mailbox Scan Scope -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 16px 20px; border-radius: 8px; border: 1px solid var(--border); display: flex; align-items: center; justify-content: space-between;">
                        <div>
                            <div style="font-weight: 600; font-size: 0.88rem; color: var(--text);">Historical Mailbox Scan Depth</div>
                            <div style="font-size: 0.75rem; color: var(--muted); margin-top: 2px;">Choose whether background scanning sweeps entire mailbox history or recent items only</div>
                        </div>
                        <select id="cfg-scan-scope" style="font-size: 0.8rem; padding: 6px 12px; border-radius: 6px; background: var(--surface-mid); color: var(--text); border: 1px solid var(--border);">
                            <option value="all" ${currentScope === 'all' ? 'selected' : ''}>All Historical Items (Entire Mailbox &amp; Archives)</option>
                            <option value="past7days" ${currentScope === 'past7days' ? 'selected' : ''}>Past 7 Days Only (Faster Startup)</option>
                            <option value="unread" ${currentScope === 'unread' ? 'selected' : ''}>Unread Emails Only</option>
                        </select>
                    </div>

                    <!-- Scan Speed Slider -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 16px 20px; border-radius: 8px; border: 1px solid var(--border);">
                        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
                            <label for="cfg-speed" style="font-weight: 600; font-size: 0.88rem; color: var(--text); margin: 0;">Background Scan Speed</label>
                            <span id="speed-display-label" style="font-size: 0.75rem; color: var(--accent); font-weight: 600;">Balanced</span>
                        </div>
                        <input type="range" id="cfg-speed" min="10" max="100" value="${config.scanningSpeed !== undefined ? config.scanningSpeed : 50}" style="width: 100%;" oninput="SecurityUI.Settings.onSpeedInput(this.value)">
                        <div style="display: flex; justify-content: space-between; font-size: 0.7rem; color: var(--muted); margin-top: 4px;">
                            <span>Gentle (Low CPU)</span>
                            <span>Balanced</span>
                            <span>High Throughput</span>
                        </div>
                    </div>

                    <!-- Spam Destination & Outlook Tags -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 16px 20px; border-radius: 8px; border: 1px solid var(--border); display: flex; flex-direction: column; gap: 14px;">
                        <div style="display: flex; align-items: center; justify-content: space-between;">
                            <div>
                                <div style="font-weight: 600; font-size: 0.88rem; color: var(--text);">Where caught spam goes</div>
                                <div style="font-size: 0.75rem; color: var(--muted); margin-top: 2px;">DeskGuard Quarantine is never emptied automatically. Outlook Junk folder may be purged by Exchange.</div>
                            </div>
                            <select id="cfg-spam-dest" style="font-size: 0.8rem; padding: 6px 12px; border-radius: 6px; background: var(--surface-mid); color: var(--text); border: 1px solid var(--border);">
                                <option value="quarantine" ${config.spamDestination !== 'junk' ? 'selected' : ''}>DeskGuard Quarantine (Recommended)</option>
                                <option value="junk" ${config.spamDestination === 'junk' ? 'selected' : ''}>Outlook Junk Email Folder</option>
                            </select>
                        </div>
                        <div style="display: flex; align-items: center; justify-content: space-between; padding-top: 10px; border-top: 1px solid var(--border);">
                            <div>
                                <div style="font-weight: 600; font-size: 0.88rem; color: var(--text);">Show colored category tags in Outlook</div>
                                <div style="font-size: 0.75rem; color: var(--muted); margin-top: 2px;">Attaches color categories (Threat / Spam / Suspicious) to messages inside Outlook mail list</div>
                            </div>
                            <label class="switch">
                                <input type="checkbox" id="cfg-label-outlook" ${config.labelInOutlook ? 'checked' : ''}>
                                <span class="slider"></span>
                            </label>
                        </div>
                    </div>
                </div>
            </div>

            <!-- TAB 2: ANTI-SPAM ENGINES -->
            <div class="settings-tab-content ${activeTab === 'engines' ? 'active' : ''}" data-pane="engines">
                <div style="display: flex; flex-direction: column; gap: 16px;">
                    <div style="background: rgba(0, 120, 212, 0.08); border: 1px solid rgba(0, 120, 212, 0.3); border-radius: 8px; padding: 14px 18px;">
                        <div style="font-weight: 600; font-size: 0.88rem; color: var(--accent); margin-bottom: 4px;">8-Engine Defense Matrix</div>
                        <div style="font-size: 0.75rem; color: var(--muted); line-height: 1.5;">
                            DeskGuard inspects inbound messages using 8 independent verification layers. Every email starts at 100% integrity score, and deductions are subtracted if checks fail.
                        </div>
                    </div>

                    <div style="display: flex; gap: 10px; justify-content: center; align-items: center; margin: 4px 0; flex-wrap: wrap;">
                        <button type="button" class="btn-ui" onclick="SecurityUI.Settings.applyEnginePreset('balanced')" style="padding: 8px 16px; font-weight: 600; font-size: 0.8rem;">
                            Preset: Balanced (Recommended)
                        </button>
                        <button type="button" class="btn-ui" onclick="SecurityUI.Settings.applyEnginePreset('strict')" style="padding: 8px 16px; font-weight: 600; font-size: 0.8rem;">
                            Preset: Strict (Zero-Tolerance)
                        </button>
                        <button type="button" class="btn-ui" onclick="SecurityUI.Settings.applyEnginePreset('relaxed')" style="padding: 8px 16px; font-weight: 600; font-size: 0.8rem;">
                            Preset: Relaxed (Fewer Alerts)
                        </button>
                        <button type="button" class="btn-ui" onclick="SecurityUI.Settings.restoreFactoryDefaults()" style="padding: 8px 16px; font-weight: 700; font-size: 0.8rem; background: rgba(241, 112, 123, 0.12); border: 1px solid var(--danger); color: var(--danger); display: inline-flex; align-items: center; gap: 5px;" title="Reset all engine weights and thresholds to factory defaults">
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"></path><path d="M3 3v5h5"></path></svg>
                            Reset to Defaults
                        </button>
                    </div>

                    <div style="background: var(--surface-low); border: 1px solid var(--border); border-radius: 8px; padding: 18px 20px;">
                        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 14px;">
                            <div>
                                <div style="font-weight: 700; font-size: 0.9rem; color: var(--text);">Engine Scoring Weights &amp; Thresholds</div>
                                <div style="font-size: 0.74rem; color: var(--muted); margin-top: 2px;">Fine-tune exact deduction weights across all 8 security rules</div>
                            </div>
                            <button type="button" class="primary" onclick="SecurityUI.Engines.open()" style="font-size: 0.8rem; padding: 7px 16px; display: inline-flex; align-items: center; gap: 6px;">
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 20v-6M6 20V10M18 20V4M3 10h6M9 14h6M15 4h6"/></svg>
                                Open Interactive Tuner &rarr;
                            </button>
                        </div>
                        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 10px; font-size: 0.78rem;">
                            <div style="display: flex; justify-content: space-between; padding: 8px 12px; background: var(--surface-mid); border-radius: 4px; border: 1px solid var(--border);">
                                <span>DMARC Authentication</span>
                                <b style="color: var(--accent);">10%</b>
                            </div>
                            <div style="display: flex; justify-content: space-between; padding: 8px 12px; background: var(--surface-mid); border-radius: 4px; border: 1px solid var(--border);">
                                <span>Sender Alignment</span>
                                <b style="color: var(--accent);">5%</b>
                            </div>
                            <div style="display: flex; justify-content: space-between; padding: 8px 12px; background: var(--surface-mid); border-radius: 4px; border: 1px solid var(--border);">
                                <span>DKIM Cryptographic Seals</span>
                                <b style="color: var(--accent);">5%</b>
                            </div>
                            <div style="display: flex; justify-content: space-between; padding: 8px 12px; background: var(--surface-mid); border-radius: 4px; border: 1px solid var(--border);">
                                <span>SPF Route Authorization</span>
                                <b style="color: var(--accent);">10%</b>
                            </div>
                            <div style="display: flex; justify-content: space-between; padding: 8px 12px; background: var(--surface-mid); border-radius: 4px; border: 1px solid var(--border);">
                                <span>Reverse DNS / PTR Check</span>
                                <b style="color: var(--accent);">4%</b>
                            </div>
                            <div style="display: flex; justify-content: space-between; padding: 8px 12px; background: var(--surface-mid); border-radius: 4px; border: 1px solid var(--border);">
                                <span>Anti-Phishing Hidden Text</span>
                                <b style="color: var(--accent);">5%</b>
                            </div>
                            <div style="display: flex; justify-content: space-between; padding: 8px 12px; background: var(--surface-mid); border-radius: 4px; border: 1px solid var(--border);">
                                <span>Spam &amp; Scam Words</span>
                                <b style="color: var(--accent);">6%</b>
                            </div>
                            <div style="display: flex; justify-content: space-between; padding: 8px 12px; background: var(--surface-mid); border-radius: 4px; border: 1px solid var(--border);">
                                <span>Global RBL Blacklists</span>
                                <b style="color: var(--accent);">10%</b>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            <!-- TAB 3: THREAT CLOUD & VIRUSTOTAL -->
            <div class="settings-tab-content ${activeTab === 'threatcloud' ? 'active' : ''}" data-pane="threatcloud">
                <div style="display: flex; flex-direction: column; gap: 16px;">
                    <!-- Threat Intelligence Level -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 18px 20px; border-radius: 8px; border: 1px solid var(--border);">
                        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
                            <label for="cfg-intel-level" style="font-weight: 700; font-size: 0.9rem; color: var(--text); margin: 0;">Threat Intelligence Privacy &amp; Cloud Level</label>
                            <span class="badge ${currentIntelLevel === 1 ? 'badge-safe' : (currentIntelLevel === 2 ? 'badge-spam' : 'badge-safe')}" style="font-size: 0.7rem;">
                                Level ${currentIntelLevel}
                            </span>
                        </div>
                        <select id="cfg-intel-level" style="width: 100%; font-size: 0.82rem; padding: 8px 12px; margin-bottom: 12px; border-radius: 6px; background: var(--surface-mid); color: var(--text); border: 1px solid var(--border);" onchange="SecurityUI.Settings.onIntelChange(this.value)">
                            <option value="0" ${currentIntelLevel === 0 ? 'selected' : ''}>Level 0: Local PC Only (Conservative - No Cloud Queries)</option>
                            <option value="1" ${currentIntelLevel === 1 ? 'selected' : ''}>Level 1: Smart Hybrid Defense (Balanced - Recommended)</option>
                            <option value="2" ${currentIntelLevel === 2 ? 'selected' : ''}>Level 2: Maximum Cloud Inspection (Aggressive - Cloud All)</option>
                        </select>
                        <div id="intel-explainer-card" style="background: var(--surface-mid); border: 1px solid var(--border); border-radius: 6px; padding: 12px 14px;"></div>
                    </div>

                    <!-- VirusTotal API Key -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 18px 20px; border-radius: 8px; border: 1px solid var(--border);">
                        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
                            <div>
                                <div style="font-weight: 700; font-size: 0.9rem; color: var(--text);">VirusTotal Multi-Engine API Key</div>
                                <div style="font-size: 0.74rem; color: var(--muted); margin-top: 2px;">Enables live multi-vendor antivirus scanning across 70+ security providers</div>
                            </div>
                            <span class="badge ${hasVtKey ? 'badge-safe' : 'badge-spam'}" style="font-size: 0.7rem;">
                                ${hasVtKey ? '🛡️ Protected via DPAPI' : 'Not Connected'}
                            </span>
                        </div>

                        <div style="display: flex; gap: 8px; margin-top: 10px; align-items: center;">
                            <div style="position: relative; flex: 1;">
                                <input type="password" id="cfg-vtkey" value="${window.escapeHtml(config.vtApiKey || '')}" placeholder="Paste your 64-character VirusTotal API key..." style="width: 100%; padding: 8px 36px 8px 12px; font-size: 0.82rem; background: var(--surface-mid); color: var(--text); border: 1px solid var(--border); border-radius: 6px;" autocomplete="off" />
                                <button type="button" onclick="SecurityUI.Settings.toggleVtKeyVisibility()" title="Show/Hide API key" style="position: absolute; right: 8px; top: 50%; transform: translateY(-50%); background: transparent; border: none; color: var(--muted); cursor: pointer; padding: 4px;">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path><circle cx="12" cy="12" r="3"></circle></svg>
                                </button>
                            </div>
                            <button type="button" id="btn-test-vt" class="btn-ui" onclick="SecurityUI.Settings.testVtKeyConnection()" style="padding: 8px 14px; font-size: 0.8rem; font-weight: 600; white-space: nowrap;">
                                Test Connection
                            </button>
                        </div>
                        <div id="vt-test-status" style="display: none; margin-top: 8px; font-size: 0.78rem; padding: 6px 12px; border-radius: 4px;"></div>

                        <div style="font-size: 0.74rem; color: var(--muted); margin-top: 10px; line-height: 1.4;">
                            Don't have an API key? You can get a free one in seconds: 
                            <a href="https://www.virustotal.com/gui/my-apikey" target="_blank" style="color: var(--accent); text-decoration: underline; margin-left: 4px;">Get Free VirusTotal Key &rarr;</a>
                        </div>
                    </div>
                </div>
            </div>

            <!-- TAB 4: SENDERS & RULES -->
            <div class="settings-tab-content ${activeTab === 'rules' ? 'active' : ''}" data-pane="rules">
                <div style="display: flex; flex-direction: column; gap: 16px;">
                    <!-- Whitelist -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 18px 20px; border-radius: 8px; border: 1px solid var(--border);">
                        <div style="font-weight: 700; font-size: 0.9rem; color: var(--ok); margin-bottom: 4px;">Trusted Senders &amp; Domains (Whitelist)</div>
                        <div style="font-size: 0.74rem; color: var(--muted); margin-bottom: 10px;">Emails from these senders bypass spam filtering and will never be quarantined</div>
                        <div id="whitelist-tags" class="tag-input-container" style="margin-bottom: 10px;"></div>
                        <div style="display: flex; gap: 8px;">
                            <input type="text" id="new-whitelist-input" placeholder="e.g. partner@company.com or company.com" style="flex: 1; padding: 6px 12px; font-size: 0.8rem; background: var(--surface-mid); border: 1px solid var(--border); border-radius: 6px; color: var(--text);" onkeydown="if(event.key==='Enter') window.addWhitelistItem()" />
                            <button type="button" class="btn-ui" onclick="window.addWhitelistItem()" style="font-size: 0.8rem; padding: 6px 14px;">Add Trusted</button>
                        </div>
                    </div>

                    <!-- Blacklist -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 18px 20px; border-radius: 8px; border: 1px solid var(--border);">
                        <div style="font-weight: 700; font-size: 0.9rem; color: var(--danger); margin-bottom: 4px;">Blocked Senders &amp; Domains (Blacklist)</div>
                        <div style="font-size: 0.74rem; color: var(--muted); margin-bottom: 10px;">Emails from these senders are instantly treated as malicious and moved to Quarantine</div>
                        <div id="blacklist-tags" class="tag-input-container" style="margin-bottom: 10px;"></div>
                        <div style="display: flex; gap: 8px;">
                            <input type="text" id="new-blacklist-input" placeholder="e.g. spammer@badsite.com or badsite.com" style="flex: 1; padding: 6px 12px; font-size: 0.8rem; background: var(--surface-mid); border: 1px solid var(--border); border-radius: 6px; color: var(--text);" onkeydown="if(event.key==='Enter') window.addBlacklistItem()" />
                            <button type="button" class="btn-ui" onclick="window.addBlacklistItem()" style="font-size: 0.8rem; padding: 6px 14px;">Block Sender</button>
                        </div>
                    </div>

                    <!-- Spam Keywords -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 18px 20px; border-radius: 8px; border: 1px solid var(--border);">
                        <div style="font-weight: 700; font-size: 0.9rem; color: var(--warn); margin-bottom: 4px;">Custom Spam &amp; Phishing Phrases</div>
                        <div style="font-size: 0.74rem; color: var(--muted); margin-bottom: 10px;">Trigger heuristic penalties when matching phrases appear in incoming message bodies</div>
                        <div id="spam-keyword-tags" class="tag-input-container" style="margin-bottom: 10px;"></div>
                        <div style="display: flex; gap: 8px;">
                            <input type="text" id="new-spam-keyword-input" placeholder="e.g. wire transfer, urgent payment, account suspended" style="flex: 1; padding: 6px 12px; font-size: 0.8rem; background: var(--surface-mid); border: 1px solid var(--border); border-radius: 6px; color: var(--text);" onkeydown="if(event.key==='Enter') window.addSpamKeywordItem()" />
                            <button type="button" class="btn-ui" onclick="window.addSpamKeywordItem()" style="font-size: 0.8rem; padding: 6px 14px;">Add Phrase</button>
                        </div>
                    </div>
                </div>
            </div>

            <!-- TAB 5: MAINTENANCE & DANGER ZONE -->
            <div class="settings-tab-content ${activeTab === 'maintenance' ? 'active' : ''}" data-pane="maintenance">
                <div style="display: flex; flex-direction: column; gap: 16px;">
                    <!-- Backup, Export & Logs -->
                    <div class="setting-item-card" style="background: var(--surface-low); padding: 18px 20px; border-radius: 8px; border: 1px solid var(--border);">
                        <div style="font-weight: 700; font-size: 0.9rem; color: var(--text); margin-bottom: 4px;">Policy Management &amp; System Logs</div>
                        <div style="font-size: 0.74rem; color: var(--muted); margin-bottom: 14px;">Export configurations for backup or import custom organization policies</div>
                        <div style="display: flex; gap: 10px; flex-wrap: wrap;">
                            <button type="button" onclick="api.openLogsFolder()" class="btn-ui" style="padding: 7px 14px; font-size: 0.8rem;">
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline></svg>
                                Open Logs Folder
                            </button>
                            <button type="button" onclick="api.exportConfig()" class="btn-ui" style="padding: 7px 14px; font-size: 0.8rem;">
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="7 10 12 15 17 10"></polyline><line x1="12" y1="15" x2="12" y2="3"></line></svg>
                                Backup / Export Policy
                            </button>
                            <button type="button" onclick="api.importConfig()" class="btn-ui" style="padding: 7px 14px; font-size: 0.8rem;">
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="17 8 12 3 7 8"></polyline><line x1="12" y1="3" x2="12" y2="15"></line></svg>
                                Import Policy
                            </button>
                            <button type="button" onclick="SecurityUI.Settings.restoreFactoryDefaults()" class="btn-ui" style="padding: 7px 14px; font-size: 0.8rem;">
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"></path><path d="M3 3v5h5"></path></svg>
                                Restore Defaults
                            </button>
                        </div>
                    </div>

                    <!-- Isolated Danger Zone -->
                    <div class="danger-zone setting-item-card">
                        <div class="danger-zone-title">
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="var(--danger)"><path d="M12 2L1 21h22L12 2zm0 3.99L19.53 19H4.47L12 5.99zM11 10v4h2v-4h-2zm0 6v2h2v-2h-2z"/></svg>
                            DANGER ZONE: DESTRUCTIVE STORAGE OPERATIONS
                        </div>
                        <div style="font-size: 0.75rem; color: var(--muted); margin-bottom: 14px; line-height: 1.5;">
                            These actions wipe local forensic logs and incident databases. Never proceed autonomously without user confirmation.
                        </div>
                        <div style="display: flex; gap: 10px; flex-wrap: wrap;">
                            <button type="button" onclick="window.safeUpgrade()" class="btn-ui" style="background: rgba(255, 170, 68, 0.12); border: 1px solid var(--warn); color: var(--warn); font-weight: 600; padding: 8px 14px; font-size: 0.8rem;">
                                Clear Incident History &amp; Cache
                            </button>
                            <button type="button" onclick="window.nuclearReset()" class="btn-ui" style="background: rgba(241, 112, 123, 0.15); border: 1px solid var(--danger); color: var(--danger); font-weight: 600; padding: 8px 14px; font-size: 0.8rem;">
                                Scorched Earth Factory Reset
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        renderTagList('whitelist-tags', localWhitelist, 'window.removeWhitelistItem');
        renderTagList('blacklist-tags', localBlacklist, 'window.removeBlacklistItem');
        renderTagList('spam-keyword-tags', localSpamKeywords, 'window.removeSpamKeywordItem');
        updateIntelExplainer(currentIntelLevel);

        window.settingsDirty = false;
        const unsavedInd = document.getElementById('settings-unsaved-indicator');
        if (unsavedInd) unsavedInd.style.display = 'none';

        // Bind input dirty watchers
        ['cfg-theme', 'cfg-startup', 'cfg-enabled', 'cfg-onaccess', 'cfg-deep-local-av-all', 'cfg-scan-scope', 'cfg-speed', 'cfg-spam-dest', 'cfg-label-outlook', 'cfg-vtkey', 'cfg-intel-level'].forEach(id => {
            const el = document.getElementById(id);
            if (el) {
                el.addEventListener('input', markDirty);
                el.addEventListener('change', markDirty);
            }
        });

        // Wire save buttons
        async function doSave() {
            const btnBottom = document.getElementById('save-settings-btn');
            const btnTop = document.getElementById('save-settings-btn-top');
            const oldBottom = btnBottom ? btnBottom.innerHTML : '';
            const oldTop = btnTop ? btnTop.innerHTML : '';
            if (btnBottom) { btnBottom.textContent = 'Saving...'; btnBottom.disabled = true; }
            if (btnTop) { btnTop.textContent = 'Saving...'; btnTop.disabled = true; }

            try {
                const themeVal = document.getElementById('cfg-theme').value;
                const startupEnabled = document.getElementById('cfg-startup').checked;
                const isEnabled = document.getElementById('cfg-enabled').checked;
                const onAccessEnabled = document.getElementById('cfg-onaccess').checked;
                const deepLocalAvScanAll = document.getElementById('cfg-deep-local-av-all') ? document.getElementById('cfg-deep-local-av-all').checked : false;
                const scanScope = document.getElementById('cfg-scan-scope').value;
                const deepHistoryScan = (scanScope === 'all');
                const historyScanEnabled = (scanScope !== 'unread');
                const speedVal = parseInt(document.getElementById('cfg-speed').value, 10);
                const spamDest = document.getElementById('cfg-spam-dest').value;
                const labelOutlook = document.getElementById('cfg-label-outlook').checked;
                const vtKeyVal = document.getElementById('cfg-vtkey').value;
                const intelLevel = parseInt(document.getElementById('cfg-intel-level').value, 10);

                if (api.setTheme) await api.setTheme(themeVal);
                await api.setEnabled(isEnabled);
                await api.setStartup(startupEnabled);
                await api.setScanningSpeed(speedVal);
                await api.setHistoryEnabled(historyScanEnabled);

                if (api.setScanModes) {
                    await api.setScanModes({
                        onAccessEnabled,
                        deepLocalAvScanAll,
                        onDemandLimit: 0,
                        deepHistoryScanEnabled: deepHistoryScan,
                        historyScanScope: scanScope,
                        spamDestination: spamDest,
                        labelInOutlook: labelOutlook
                    });
                }

                await api.setVTKey(vtKeyVal);
                await api.setThreatIntelLevel(intelLevel);
                await api.setWhitelist(localWhitelist);
                await api.setBlacklist(localBlacklist);
                await api.setSpamKeywords(localSpamKeywords);

                window.settingsDirty = false;
                if (unsavedInd) unsavedInd.style.display = 'none';
                if (typeof window.showNotification === 'function') {
                    window.showNotification("Settings saved successfully.");
                }
                window.closeSettings(true);
            } catch (err) {
                console.error("Save Error:", err);
                if (typeof window.showNotification === 'function') {
                    window.showNotification("Failed to save settings: " + err.message, true);
                } else {
                    alert("Failed to save settings: " + err.message);
                }
            } finally {
                if (btnBottom) { btnBottom.innerHTML = oldBottom; btnBottom.disabled = false; }
                if (btnTop) { btnTop.innerHTML = oldTop; btnTop.disabled = false; }
            }
        }

        const btnBottom = document.getElementById('save-settings-btn');
        if (btnBottom) btnBottom.onclick = doSave;
        const btnTop = document.getElementById('save-settings-btn-top');
        if (btnTop) btnTop.onclick = doSave;
    }

    function onThemeChange(val) {
        if (val === 'light') {
            document.documentElement.setAttribute('data-theme', 'light');
        } else if (val === 'dark') {
            document.documentElement.removeAttribute('data-theme');
        } else {
            // System match
            const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
            if (prefersDark) document.documentElement.removeAttribute('data-theme');
            else document.documentElement.setAttribute('data-theme', 'light');
        }
    }

    function onStartupToggle(checkbox) {
        const badge = document.getElementById('startup-status-badge');
        if (!badge) return;
        if (checkbox.checked) {
            badge.innerHTML = `<span class="badge badge-safe" style="font-size: 0.7rem; padding: 3px 8px;">● Auto-Start Active</span>`;
        } else {
            badge.innerHTML = `<span class="badge badge-spam" style="font-size: 0.7rem; padding: 3px 8px;">○ Auto-Start Disabled</span>`;
        }
    }

    function onSpeedInput(val) {
        const lbl = document.getElementById('speed-display-label');
        if (!lbl) return;
        const n = parseInt(val, 10);
        if (n <= 30) lbl.textContent = 'Gentle (Low CPU)';
        else if (n <= 70) lbl.textContent = 'Balanced';
        else lbl.textContent = 'High Throughput';
    }

    function toggleVtKeyVisibility() {
        const input = document.getElementById('cfg-vtkey');
        if (!input) return;
        input.type = (input.type === 'password') ? 'text' : 'password';
    }

    async function testVtKeyConnection() {
        const input = document.getElementById('cfg-vtkey');
        const statusEl = document.getElementById('vt-test-status');
        const btn = document.getElementById('btn-test-vt');
        if (!input || !statusEl || !btn) return;

        const key = input.value.trim();
        if (!key || key.length < 16) {
            statusEl.style.display = 'block';
            statusEl.style.background = 'rgba(241, 112, 123, 0.15)';
            statusEl.style.color = 'var(--danger)';
            statusEl.style.border = '1px solid var(--danger)';
            statusEl.textContent = 'Please enter a valid 64-character VirusTotal API key before testing.';
            return;
        }

        btn.disabled = true;
        btn.textContent = 'Testing...';
        statusEl.style.display = 'block';
        statusEl.style.background = 'rgba(0, 120, 212, 0.1)';
        statusEl.style.color = 'var(--accent)';
        statusEl.style.border = '1px solid var(--accent)';
        statusEl.textContent = 'Connecting to VirusTotal threat intelligence endpoint...';

        try {
            const res = await api.testVTKey(key);
            if (res && res.ok && res.success) {
                statusEl.style.background = 'rgba(107, 183, 0, 0.15)';
                statusEl.style.color = 'var(--ok)';
                statusEl.style.border = '1px solid var(--ok)';
                statusEl.textContent = '✔ Connection verified successfully: VirusTotal API is responding normally.';
            } else {
                statusEl.style.background = 'rgba(241, 112, 123, 0.15)';
                statusEl.style.color = 'var(--danger)';
                statusEl.style.border = '1px solid var(--danger)';
                statusEl.textContent = '✕ ' + ((res && res.error) || 'Failed to authenticate with VirusTotal. Verify your API key.');
            }
        } catch (err) {
            statusEl.style.background = 'rgba(241, 112, 123, 0.15)';
            statusEl.style.color = 'var(--danger)';
            statusEl.style.border = '1px solid var(--danger)';
            statusEl.textContent = '✕ Network error: ' + err.message;
        } finally {
            btn.disabled = false;
            btn.textContent = 'Test Connection';
        }
    }

    function applyEnginePreset(preset) {
        if (!currentConfig) return;
        currentConfig.weights = currentConfig.weights || {};
        currentConfig.thresholds = currentConfig.thresholds || {};

        if (preset === 'strict') {
            currentConfig.weights = { spf: 12, dmarc: 12, dkim: 5, alignment: 5, rdns: 4, rbl: 12, body: 5, heuristics: 8, spamflag: 4, mime: 8, msscl: 10, linkmismatch: 8, displayspoof: 6, lookalike: 4, replyto: 3, firstcontact: 2 };
            currentConfig.thresholds.spamThresholdPercent = 55;
            currentConfig.thresholds.maliciousThresholdPercent = 35;
            currentConfig.thresholds.suspiciousThresholdPercent = 70;
        } else if (preset === 'relaxed') {
            currentConfig.weights = { spf: 8, dmarc: 8, dkim: 5, alignment: 5, rdns: 3, rbl: 8, body: 5, heuristics: 5, spamflag: 4, mime: 5, msscl: 10, linkmismatch: 8, displayspoof: 7, lookalike: 5, replyto: 3, firstcontact: 2 };
            currentConfig.thresholds.spamThresholdPercent = 25;
            currentConfig.thresholds.maliciousThresholdPercent = 10;
            currentConfig.thresholds.suspiciousThresholdPercent = 40;
        } else {
            // balanced (default 100 pt rubric)
            currentConfig.weights = { spf: 10, dmarc: 10, dkim: 5, alignment: 5, rdns: 4, rbl: 10, body: 5, heuristics: 6, spamflag: 4, mime: 6, msscl: 10, linkmismatch: 8, displayspoof: 7, lookalike: 5, replyto: 3, firstcontact: 2 };
            currentConfig.thresholds.spamThresholdPercent = 40;
            currentConfig.thresholds.maliciousThresholdPercent = 20;
            currentConfig.thresholds.suspiciousThresholdPercent = 55;
        }

        if (typeof window.showNotification === 'function') {
            window.showNotification(`Applied engine preset: ${preset.toUpperCase()} (Weights & Thresholds updated)`);
        }
        markDirty();
        renderSettings();
    }

    function restoreFactoryDefaults() {
        const confirmFn = window.showConfirmModal || ((title, msg, fn) => {
            if (window.showConfirm) window.showConfirm(title, msg, fn);
        });

        confirmFn(
            'Restore Factory Defaults',
            'This will reset your rules, engine weights, thresholds, and scan settings to factory defaults. Your saved emails and window positions will NOT be erased. Proceed?',
            async () => {
                try {
                    await api.restoreDefaults();
                    if (typeof window.showNotification === 'function') {
                        window.showNotification('Factory defaults restored.');
                    }
                    await sync();
                } catch (e) {
                    alert('Error restoring defaults: ' + e.message);
                }
            }
        );
    }

    return {
        sync: sync,
        switchTab: switchSettingsTab,
        onSearch: filterSettingsSearch,
        onThemeChange: onThemeChange,
        onStartupToggle: onStartupToggle,
        onSpeedInput: onSpeedInput,
        onIntelChange: updateIntelExplainer,
        toggleVtKeyVisibility: toggleVtKeyVisibility,
        testVtKeyConnection: testVtKeyConnection,
        applyEnginePreset: applyEnginePreset,
        restoreFactoryDefaults: restoreFactoryDefaults
    };
})();

window.syncSettingsUI = SecurityUI.Settings.sync;
