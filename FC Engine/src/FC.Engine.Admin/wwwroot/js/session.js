/**
 * FCSession — Session lifecycle manager for RegOS™ Admin Portal.
 *
 * Responsibilities:
 *   • Inactivity detection (throttled document events)
 *   • Warning + auto-logout countdown
 *   • Multi-tab synchronisation via BroadcastChannel
 *   • Silent server ping to extend the sliding auth cookie
 *   • Blazor Server circuit disconnect monitoring
 *   • Periodic remaining-time updates for the admin topbar indicator
 */
window.FCSession = (() => {
    const CHANNEL_NAME        = 'fc-session-sync';
    const ACTIVITY_THROTTLE   = 500;   // ms between activity processing
    const INDICATOR_INTERVAL  = 60000; // ms between pre-warning topbar updates

    // ── State ─────────────────────────────────────────────────────────────────
    let _dotnetRef          = null;
    let _warningMs          = 25 * 60 * 1000;  // configurable
    let _countdownTotal     = 5 * 60;           // configurable (seconds)
    let _inactivityTimer    = null;
    let _countdownInterval  = null;
    let _minuteInterval     = null;
    let _throttleTimer      = null;
    let _broadcastChannel   = null;
    let _warningActive      = false;
    let _disposed           = false;
    let _remainingSec       = 0;
    let _startMs            = 0;

    const EVENTS = ['mousemove', 'keydown', 'click', 'scroll', 'touchstart', 'pointerdown'];

    // ── Activity tracking ─────────────────────────────────────────────────────

    function onActivity() {
        if (_throttleTimer || _warningActive || _disposed) return;
        _throttleTimer = setTimeout(() => {
            _throttleTimer = null;
            resetTimer();
        }, ACTIVITY_THROTTLE);
    }

    function resetTimer() {
        clearTimeout(_inactivityTimer);
        _startMs = Date.now();
        _inactivityTimer = setTimeout(beginWarning, _warningMs);
    }

    // ── Warning + countdown ───────────────────────────────────────────────────

    function beginWarning() {
        if (_disposed || _warningActive) return;
        _warningActive = true;
        _remainingSec  = _countdownTotal;

        // Stop the minute-interval; countdown will tick every second instead
        clearInterval(_minuteInterval);
        _minuteInterval = null;

        _dotnetRef?.invokeMethodAsync('OnInactivityWarning', _remainingSec).catch(() => {});
        _countdownInterval = setInterval(tick, 1000);
    }

    function tick() {
        if (_disposed) return;
        _remainingSec = Math.max(0, _remainingSec - 1);
        _dotnetRef?.invokeMethodAsync('OnCountdownTick', _remainingSec).catch(() => {});

        if (_remainingSec <= 0) {
            clearInterval(_countdownInterval);
            _countdownInterval = null;
            broadcastExpired();
            _dotnetRef?.invokeMethodAsync('OnSessionTimeout').catch(() => {});
        }
    }

    // ── Session extend ────────────────────────────────────────────────────────

    function extend() {
        if (_countdownInterval) { clearInterval(_countdownInterval); _countdownInterval = null; }
        _warningActive = false;
        _remainingSec  = 0;

        try { _broadcastChannel?.postMessage({ type: 'extended', ts: Date.now() }); } catch {}

        resetTimer();
        startMinuteInterval();          // Resume topbar updates
        notifyRemaining();              // Immediate update
    }

    // ── Silent server ping (extends the sliding auth cookie) ─────────────────

    async function ping(baseUri) {
        try {
            await fetch(baseUri + 'api/session/ping', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
        } catch { /* network issues are non-fatal; JS timer still resets */ }
    }

    // ── Multi-tab BroadcastChannel ────────────────────────────────────────────

    function broadcastExpired() {
        try { _broadcastChannel?.postMessage({ type: 'expired', ts: Date.now() }); } catch {}
    }

    function handleBroadcast({ data }) {
        if (!data) return;

        if (data.type === 'extended') {
            if (_countdownInterval) { clearInterval(_countdownInterval); _countdownInterval = null; }
            _warningActive = false;
            _remainingSec  = 0;
            resetTimer();
            startMinuteInterval();
            _dotnetRef?.invokeMethodAsync('OnSessionExtendedByOtherTab').catch(() => {});

        } else if (data.type === 'expired') {
            _dotnetRef?.invokeMethodAsync('OnSessionTimeout').catch(() => {});
        }
    }

    // ── Admin topbar indicator (periodic remaining-time updates) ──────────────

    function getFullRemainingSec() {
        if (_warningActive) return _remainingSec;
        const elapsed = Date.now() - _startMs;
        return Math.max(0, Math.floor((_warningMs - elapsed) / 1000)) + _countdownTotal;
    }

    function notifyRemaining() {
        if (_warningActive || !_dotnetRef || _disposed) return;
        _dotnetRef.invokeMethodAsync('OnRemainingUpdate', getFullRemainingSec()).catch(() => {});
    }

    function startMinuteInterval() {
        if (_minuteInterval) return;
        _minuteInterval = setInterval(notifyRemaining, INDICATOR_INTERVAL);
    }

    // ── Blazor Server circuit monitoring ──────────────────────────────────────

    function watchCircuit() {
        const modal = document.getElementById('components-reconnect-modal');
        if (!modal) return;

        let lastClass = '';
        const observer = new MutationObserver(() => {
            const cls = modal.getAttribute('class') || '';
            if (cls === lastClass) return;
            lastClass = cls;

            if (cls.includes('components-reconnect-show')) {
                _dotnetRef?.invokeMethodAsync('OnCircuitDisconnected').catch(() => {});
            } else if (cls.includes('components-reconnect-hide')) {
                _dotnetRef?.invokeMethodAsync('OnCircuitReconnected').catch(() => {});
            } else if (cls.includes('components-reconnect-failed')) {
                _dotnetRef?.invokeMethodAsync('OnCircuitFailed').catch(() => {});
            }
        });

        observer.observe(modal, { attributes: true, attributeFilter: ['class'] });
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    function init(dotnetRef, warningMinutes, countdownSeconds) {
        _dotnetRef      = dotnetRef;
        _warningMs      = (warningMinutes ?? 25) * 60 * 1000;
        _countdownTotal = countdownSeconds ?? 300;
        _disposed       = false;
        _warningActive  = false;
        _remainingSec   = 0;
        _startMs        = Date.now();

        try {
            _broadcastChannel = new BroadcastChannel(CHANNEL_NAME);
            _broadcastChannel.addEventListener('message', handleBroadcast);
        } catch { /* BroadcastChannel not available (private browsing on Safari < 15.4) */ }

        EVENTS.forEach(ev => document.addEventListener(ev, onActivity, { passive: true }));

        _inactivityTimer = setTimeout(beginWarning, _warningMs);

        // Send an immediate update so the topbar indicator appears right away
        setTimeout(notifyRemaining, 200);
        startMinuteInterval();

        watchCircuit();
    }

    function dispose() {
        _disposed = true;
        clearTimeout(_inactivityTimer);
        clearTimeout(_throttleTimer);
        if (_countdownInterval) clearInterval(_countdownInterval);
        if (_minuteInterval)    clearInterval(_minuteInterval);
        EVENTS.forEach(ev => document.removeEventListener(ev, onActivity));
        try { _broadcastChannel?.close(); } catch {}
        _broadcastChannel = null;
        _dotnetRef        = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return { init, dispose, extend, ping, getFullRemainingSec };
})();
