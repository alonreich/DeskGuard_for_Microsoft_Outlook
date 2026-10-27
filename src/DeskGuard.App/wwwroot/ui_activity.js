// Plain English "What Happened" Security Activity Feed (U1)
// Renders human-readable event timelines with instant Release and Trust actions.

(function() {
    let currentFilter = 'all';

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function formatEventSentence(event) {
        const scoreStr = event.score !== null && event.score !== undefined ? ` (Score: ${event.score}/100)` : '';
        const subj = escapeHtml(event.subject || '(No Subject)');
        const sender = escapeHtml(event.sender || 'Unknown Sender');
        const fromF = escapeHtml(event.from_folder || 'Inbox');
        const toF = escapeHtml(event.to_folder || 'Quarantine');

        switch (event.kind) {
            case 'quarantine':
                return `Quarantined email from <b style="color:var(--text);">${sender}</b>: <i>"${subj}"</i>${scoreStr} — moved from <b>${fromF}</b> to <b>${toF}</b>.`;
            case 'release':
                return `Restored false-positive email <i>"${subj}"</i> back to <b>${toF || 'Inbox'}</b>.`;
            case 'trust':
                return `Added <b style="color:var(--text);">${sender}</b> to Trusted Senders list. Future emails will bypass heuristic quarantine.`;
            case 'delete':
                return `Permanently purged spam/malicious email <i>"${subj}"</i> from mailbox.`;
            case 'label':
                return `Flagged suspicious email from <b>${sender}</b>: <i>"${subj}"</i>${scoreStr} in folder <b>${fromF}</b>.`;
            default:
                return `Security event (${escapeHtml(event.kind)}) recorded on email <i>"${subj}"</i>.`;
        }
    }

    async function loadActivityFeed(filter = currentFilter) {
        currentFilter = filter;
        const listEl = document.getElementById('activity-feed-list');
        if (!listEl) return;

        // Update active filter button styling
        document.querySelectorAll('.activity-filter-btn').forEach(btn => {
            if (btn.getAttribute('data-filter') === filter) btn.classList.add('active');
            else btn.classList.remove('active');
        });

        listEl.innerHTML = '<div style="color: var(--muted); padding: 20px; text-align: center;">Refreshing activity events...</div>';

        try {
            const opts = { limit: 100 };
            if (filter !== 'all') opts.kind = filter;
            const events = await window.securityApi.getEvents(opts);

            if (!events || events.length === 0) {
                listEl.innerHTML = '<div style="color: var(--muted); padding: 30px; text-align: center;">No security events recorded yet.</div>';
                return;
            }

            listEl.innerHTML = '';
            for (const ev of events) {
                const card = document.createElement('div');
                card.className = 'activity-event-card';
                card.style.cssText = 'background: var(--surface-low); border: 1px solid var(--border); border-radius: 6px; padding: 12px 16px; display: flex; justify-content: space-between; align-items: center; gap: 12px;';

                let kindColor = 'var(--accent)';
                if (ev.kind === 'quarantine') kindColor = 'var(--danger)';
                else if (ev.kind === 'release' || ev.kind === 'trust') kindColor = 'var(--safe)';

                const sentenceHtml = formatEventSentence(ev);
                const showActionButtons = ev.kind === 'quarantine' || ev.kind === 'label';

                card.innerHTML = `
                    <div style="display: flex; gap: 12px; align-items: flex-start; flex: 1;">
                        <span style="display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: ${kindColor}; margin-top: 6px; flex-shrink: 0;"></span>
                        <div style="display: flex; flex-direction: column; gap: 4px;">
                            <div style="font-size: 0.85rem; color: var(--text); line-height: 1.4;">${sentenceHtml}</div>
                            <span style="font-size: 0.7rem; color: var(--muted);">${escapeHtml(ev.ts || '')}</span>
                        </div>
                    </div>
                    ${showActionButtons ? `
                        <div style="display: flex; gap: 8px; flex-shrink: 0;">
                            <button class="activity-release-btn primary" style="font-size: 0.75rem; padding: 4px 10px;">Release</button>
                            <button class="activity-trust-btn" style="font-size: 0.75rem; padding: 4px 10px;">Trust</button>
                        </div>
                    ` : ''}
                `;

                if (showActionButtons) {
                    const releaseBtn = card.querySelector('.activity-release-btn');
                    if (releaseBtn) {
                        releaseBtn.onclick = async () => {
                            releaseBtn.disabled = true;
                            releaseBtn.textContent = 'Releasing...';
                            try {
                                await window.securityApi.releaseEmail({
                                    entryId: ev.entry_id,
                                    fingerprint: ev.message_key
                                });
                                releaseBtn.textContent = 'Released';
                                setTimeout(() => loadActivityFeed(), 800);
                            } catch (e) {
                                releaseBtn.disabled = false;
                                releaseBtn.textContent = 'Release';
                            }
                        };
                    }

                    const trustBtn = card.querySelector('.activity-trust-btn');
                    if (trustBtn) {
                        trustBtn.onclick = async () => {
                            trustBtn.disabled = true;
                            trustBtn.textContent = 'Trusting...';
                            try {
                                await window.securityApi.trustSender(ev.sender);
                                trustBtn.textContent = 'Trusted';
                                setTimeout(() => loadActivityFeed(), 800);
                            } catch (e) {
                                trustBtn.disabled = false;
                                trustBtn.textContent = 'Trust';
                            }
                        };
                    }
                }

                listEl.appendChild(card);
            }
        } catch (err) {
            console.error('[ActivityUI] Failed to load activity feed:', err);
            listEl.innerHTML = '<div style="color: var(--danger); padding: 20px; text-align: center;">Error loading activity events.</div>';
        }
    }

    window.filterActivityFeed = (filter) => {
        loadActivityFeed(filter);
    };

    window.loadActivityFeed = loadActivityFeed;
})();
