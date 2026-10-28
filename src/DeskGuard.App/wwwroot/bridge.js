(function () {
    let reqId = 1;
    const pending = new Map();
    const listeners = new Map();

    function initBridge() {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.addEventListener('message', event => {
                const msg = event.data;
                if (typeof msg === 'string') {
                    try { handleMessage(JSON.parse(msg)); } catch (e) { }
                } else if (msg && typeof msg === 'object') {
                    handleMessage(msg);
                }
            });
        }
    }

    function handleMessage(msg) {
        if (!msg) return;
        if (msg.type === 'response') {
            const p = pending.get(msg.id);
            if (p) {
                pending.delete(msg.id);
                if (msg.error) p.reject(new Error(msg.error));
                else p.resolve(msg.result);
            }
        } else if (msg.type === 'event') {
            const list = listeners.get(msg.channel) || [];
            list.forEach(cb => {
                try { cb(msg.data); } catch (e) { console.error('Listener error in ' + msg.channel, e); }
            });
        }
    }

    initBridge();

    function invoke(channel, payload) {
        return new Promise((resolve, reject) => {
            const id = reqId++;
            pending.set(id, { resolve, reject });
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({ id, channel, payload });
            } else {
                reject(new Error('WebView2 bridge not ready'));
            }
        });
    }

    function on(channel, callback) {
        if (!listeners.has(channel)) listeners.set(channel, []);
        listeners.get(channel).push(callback);
    }

    window.winMinimize = () => invoke('window-minimize');
    window.winMaximize = () => invoke('window-maximize');
    window.winClose = () => invoke('window-hide');
    window.winDrag = () => invoke('window-drag');
    window.winResize = (edge) => invoke('window-resize', { edge });

    window.securityApi = {
        getStats: () => invoke('get-stats'),
        getConfig: () => invoke('get-config'),
        startFullScan: () => invoke('start-full-scan'),
        setEnabled: value => invoke('set-enabled', value),
        setHistoryEnabled: value => invoke('set-history-enabled', value),
        setProcessedIds: value => invoke('set-processed-ids', value),
        setVTKey: value => invoke('set-vt-key', value),
        setSpamKeywords: value => invoke('set-spam-keywords', value),
        setRubrics: value => invoke('set-rubrics', value),
        setWhitelist: value => invoke('set-whitelist', value),
        setBlacklist: value => invoke('set-blacklist', value),
        saveColumnWidths: value => invoke('save-column-widths', value),
        setScanningSpeed: value => invoke('set-scanning-speed', value),
        setStartup: value => invoke('set-startup', value),
        checkStartup: () => invoke('check-startup'),
        setThreatIntelLevel: value => invoke('set-threat-intel-level', value),
        setFirstRun: value => invoke('set-first-run', value),
        testVTKey: key => invoke('test-vt-key', { key }),
        setTheme: theme => invoke('set-theme', { theme }),
        restoreDefaults: () => invoke('restore-defaults'),
        exportConfig: async () => {
            const res = await invoke('export-config');
            if (res && (res.json || res.config)) {
                const text = res.json || JSON.stringify(res.config, null, 2);
                const blob = new Blob([text], { type: 'application/json' });
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `deskguard-policy-${new Date().toISOString().slice(0, 10)}.json`;
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                URL.revokeObjectURL(url);
                if (typeof window.showNotification === 'function') window.showNotification('Security policy exported successfully.');
            }
            return res;
        },
        importConfig: async (cfg) => {
            if (cfg) {
                return await invoke('import-config', cfg);
            }
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = '.json,application/json';
            input.style.display = 'none';
            document.body.appendChild(input);
            input.onchange = async (e) => {
                try {
                    const file = e.target.files && e.target.files[0];
                    if (file) {
                        const text = await file.text();
                        const parsed = JSON.parse(text);
                        const res = await invoke('import-config', parsed);
                        if (res && res.ok) {
                            if (typeof window.showNotification === 'function') window.showNotification('Security policy imported successfully. Reloading...');
                            setTimeout(() => window.location.reload(), 800);
                        } else {
                            if (typeof window.showNotification === 'function') window.showNotification('Import failed: ' + (res.error || 'Invalid policy file'), true);
                        }
                    }
                } catch (err) {
                    if (typeof window.showNotification === 'function') window.showNotification('Import error: ' + err.message, true);
                } finally {
                    document.body.removeChild(input);
                }
            };
            input.click();
        },
        openLogsFolder: () => invoke('open-logs-folder'),
        resetApp: mode => invoke('app-reset', mode),
        minimizeWindow: () => invoke('window-minimize'),
        maximizeWindow: () => invoke('window-maximize'),
        hideWindow: () => invoke('window-hide'),
        closeWindow: () => invoke('window-close'),
        releaseEmail: value => invoke('release-email', value),
        cleanupListeners: () => listeners.clear(),
        onScanUpdate: callback => on('outlook-scan-update', callback),
        onStatusSync: callback => on('status-sync', callback),
        onStatsUpdate: callback => on('stats-update', callback),
        onLiveLog: callback => on('live-log', callback),
        onOutlookStatus: callback => on('outlook-status', callback),
        onIndexStatus: callback => on('index-status', callback),
        quarantineEmail: data => invoke('quarantine-email', data),
        deleteEmail: data => invoke('delete-email', data),
        verifyExistence: data => invoke('verify-existence', data),
        getForensics: id => invoke('get-forensics', id),
        scanDuplicates: opts => invoke('scan-duplicates', opts),
        pauseDuplicateScan: () => invoke('pause-duplicate-scan'),
        resumeDuplicateScan: () => invoke('resume-duplicate-scan'),
        deleteDuplicates: data => invoke('delete-duplicates', data),
        resetDuplicateEngine: () => invoke('reset-duplicate-engine'),
        getSavedDuplicates: () => invoke('get-saved-duplicates'),
        onDuplicateUpdate: callback => on('duplicate-update', callback),
        scanForViruses: id => invoke('scan-virus', id),
        scanAvLocal: id => invoke('scan-av-local', { id }),
        onAvScanProgress: callback => on('av-scan-progress', callback),
        searchVault: params => invoke('search-vault', params),
        getSenderSuggestions: prefix => invoke('get-sender-suggestions', prefix),
        openEmail: data => invoke('open-email', data),
        setScanModes: data => invoke('set-scan-modes', data),
        getEvents: opts => invoke('get-events', opts),
        scoreEmailWhatIf: data => invoke('score-what-if', data),
        trustSender: sender => invoke('trust-sender', sender),
        saveModalConfig: data => invoke('save-modal-config', data),
        getModalConfig: id => invoke('get-modal-config', { id })
    };
})();
