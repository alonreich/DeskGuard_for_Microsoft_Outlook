window.SecurityUI = window.SecurityUI || {};
SecurityUI.History = (function() {
    const api = window.securityApi;

    async function init() {
        const toggle = document.getElementById('history-toggle');
        if (!toggle || !api || !api.getConfig) return;
        try {
            const cfg = await api.getConfig();
            if (cfg && cfg.historyScanEnabled) {
                toggle.classList.add('active');
            }
        } catch {}
    }

    const toggle = document.getElementById('history-toggle');
    if (toggle) {
        toggle.onclick = async () => { 
            if (!api || !api.getConfig) return;
            const cfg = await api.getConfig(); 
            const newState = !(cfg && cfg.historyScanEnabled);
            
            if (api.setHistoryEnabled) await api.setHistoryEnabled(newState); 
            toggle.classList.toggle('active', newState); 
            
            if (newState) {
                if (window.addLog) window.addLog("HISTORY SCAN MODE: Enabled. The engine will retrospectively audit ALL emails (Read & Unread).");
            } else {
                if (window.addLog) window.addLog("ON-ACCESS SCAN MODE: Enabled. The engine will now only audit new incoming unread emails.");
            }
            
            if (cfg && cfg.enabled && window.addLog) {
                window.addLog("Restarting security engine to apply scan mode changes...");
            }
        };
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
