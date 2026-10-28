window.SecurityUI = window.SecurityUI || {};
SecurityUI.Reset = (function() {
    const api = window.securityApi;

    async function nuclear() {
        const confirmFn = window.showConfirmModal || ((title, msg, fn, kw) => {
            if (window.showConfirm) window.showConfirm(title, msg, kw, fn);
        });

        confirmFn(
            'SCORCHED EARTH FACTORY RESET',
            'This action will permanently wipe ALL security configurations, VirusTotal API keys, blacklists, forensic snapshots, whitelists, and scan database. The Windows startup run key will also be uninstalled. To proceed, type RESET below.',
            async () => {
                window.addLog('Initiating Scorched Earth Factory Reset...');
                if (window.showNotification) window.showNotification('SCORCHED EARTH WIPE INITIATED: All configuration and data erased. Application will restart now...', true);
                await api.resetApp('scorched');
                setTimeout(() => window.location.reload(), 1000);
            },
            'RESET'
        );
    }

    async function safeUpgrade() {
        const confirmFn = window.showConfirmModal || ((title, msg, fn, kw) => {
            if (window.showConfirm) window.showConfirm(title, msg, kw, fn);
        });

        confirmFn(
            'SAFE UPGRADE & CACHE CLEANUP',
            'This safely clears the cached email incident list and refreshes the database, while completely preserving your settings, VirusTotal API keys, custom whitelists, and security engines. The application will restart clean. Proceed?',
            async () => {
                window.addLog('Initiating Safe Upgrade / Cache Refresh...');
                if (window.showNotification) window.showNotification('SAFE UPGRADE INITIATED: Resetting cache while preserving your settings. Application restarting...', false);
                await api.resetApp('safe');
                setTimeout(() => window.location.reload(), 1000);
            }
        );
    }

    return { 
        nuclear: nuclear,
        safeUpgrade: safeUpgrade
    };
})();

window.nuclearReset = SecurityUI.Reset.nuclear;
window.safeUpgrade = SecurityUI.Reset.safeUpgrade;
