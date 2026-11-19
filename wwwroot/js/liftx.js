// Browser glue for LiftX: storage, saving to Files, sounds, wake lock, PWA hints.
(function () {
    'use strict';
    const L = {};

    // ── localStorage (namespaced "liftx:" because every github.io repo shares one origin) ──
    L.get = function (key) {
        try { return localStorage.getItem(key); } catch (e) { return null; }
    };
    L.set = function (key, value) {
        try { localStorage.setItem(key, value); return true; }
        catch (e) { console.error('LiftX: could not save', key, e); return false; }
    };
    L.remove = function (key) {
        try { localStorage.removeItem(key); } catch (e) { }
    };
    L.requestPersist = function () {
        try { if (navigator.storage && navigator.storage.persist) navigator.storage.persist(); } catch (e) { }
    };

    L.applyTheme = function (bg, accent) {
        window.__liftxTheme(bg, accent);
    };

    // ── Save to Files ──
    // iOS web apps can't overwrite a file directly, so we hand the file to the share sheet,
    // where "Save to Files" lets you pick the folder and replace the old copy.
    // Must run inside the tap that triggered it.
    L.saveFile = async function (storageKey, fileName) {
        const json = L.get(storageKey);
        if (!json) return 'error';
        const name = fileName || 'LiftX-data.json';
        const file = new File([json], name, { type: 'application/json' });

        if (navigator.canShare && navigator.share) {
            let canShareFile = false;
            try { canShareFile = navigator.canShare({ files: [file] }); } catch (e) { }
            if (canShareFile) {
                try {
                    await navigator.share({ files: [file] });
                    return 'shared';
                } catch (e) {
                    if (e && e.name === 'AbortError') return 'cancelled';
                    console.warn('LiftX: share failed, falling back to download', e);
                }
            }
        }

        try {
            const url = URL.createObjectURL(file);
            const a = document.createElement('a');
            a.href = url;
            a.download = name;
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
            return 'downloaded';
        } catch (e) {
            console.error(e);
            return 'error';
        }
    };

    // ── Rest timer beep ──
    // iOS only plays audio after a tap. Turning audio on is slow the first time (it made the first
    // check of a lift stutter), so it happens on your first tap anywhere — usually START — instead
    // of in the middle of the check animation. Once it's on, unlockAudio() returns straight away.
    let audioCtx = null;
    function createAudio() {
        if (audioCtx) return;
        try {
            const AC = window.AudioContext || window.webkitAudioContext;
            if (AC) audioCtx = new AC();
        } catch (e) { audioCtx = null; }
    }
    L.unlockAudio = function () {
        try {
            createAudio();
            if (!audioCtx || audioCtx.state === 'running') return;
            audioCtx.resume();
            const o = audioCtx.createOscillator();
            const g = audioCtx.createGain();
            g.gain.value = 0;
            o.connect(g).connect(audioCtx.destination);
            o.start();
            o.stop(audioCtx.currentTime + 0.01);
        } catch (e) { }
    };
    document.addEventListener('touchend', L.unlockAudio, { capture: true, passive: true });
    document.addEventListener('click', L.unlockAudio, true);
    // Build it (still silent and paused) shortly after the app opens, so even the first tap only has to resume it
    setTimeout(createAudio, 2500);
    L.beep = function () {
        try { if (navigator.vibrate) navigator.vibrate([200, 100, 200]); } catch (e) { }
        if (!audioCtx) return;
        try {
            if (audioCtx.state === 'suspended') audioCtx.resume();
            const t0 = audioCtx.currentTime + 0.02;
            [0, 0.22, 0.44].forEach(function (dt, i) {
                const o = audioCtx.createOscillator();
                const g = audioCtx.createGain();
                o.type = 'sine';
                o.frequency.value = i === 2 ? 1320 : 880;
                g.gain.setValueAtTime(0.0001, t0 + dt);
                g.gain.exponentialRampToValueAtTime(0.6, t0 + dt + 0.02);
                g.gain.exponentialRampToValueAtTime(0.0001, t0 + dt + 0.18);
                o.connect(g).connect(audioCtx.destination);
                o.start(t0 + dt);
                o.stop(t0 + dt + 0.2);
            });
        } catch (e) { }
    };

    // ── Keep the screen awake during a lift ──
    let wantLock = false;
    let lock = null;
    async function acquireLock() {
        if (!wantLock || lock || !('wakeLock' in navigator) || document.visibilityState !== 'visible') return;
        try {
            lock = await navigator.wakeLock.request('screen');
            lock.addEventListener('release', function () { lock = null; });
        } catch (e) { lock = null; }
    }
    L.setWakeLock = function (on) {
        wantLock = !!on;
        if (wantLock) acquireLock();
        else if (lock) { lock.release().catch(function () { }); lock = null; }
    };
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') acquireLock();
    });

    // ── Custom keypad: scroll the box being edited so it sits above the keypad ──
    L.revealAboveKeypad = function () {
        requestAnimationFrame(function () {
            const el = document.querySelector('.set-input.editing');
            const pad = document.querySelector('.numpad');
            if (!el || !pad) return;
            const r = el.getBoundingClientRect();
            const limit = window.innerHeight - pad.offsetHeight - 16;
            const header = document.querySelector('.wk-header');
            const top = header ? header.getBoundingClientRect().bottom + 12 : 12;
            if (r.bottom > limit) window.scrollBy({ top: r.bottom - limit, behavior: 'smooth' });
            else if (r.top < top) window.scrollBy({ top: r.top - top, behavior: 'smooth' });
        });
    };

    // ── Misc UI helpers ──
    L.scrollTop = function () { window.scrollTo(0, 0); };
    L.scrollToBottom = function () {
        requestAnimationFrame(function () { window.scrollTo(0, document.documentElement.scrollHeight); });
    };
    L.focusEnd = function (id) {
        const el = document.getElementById(id);
        if (!el) return;
        el.focus();
        const n = (el.value || '').length;
        try { el.setSelectionRange(n, n); } catch (e) { }
    };
    L.focus = function (id) {
        const el = document.getElementById(id);
        if (!el) return;
        el.focus();
        if (el.select) el.select();
    };

    // ── PWA install hint ──
    L.isStandalone = function () {
        return window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
    };
    L.isMobile = function () { return /Android|iPhone|iPad|iPod/i.test(navigator.userAgent); };
    L.getBrowser = function () {
        const ua = navigator.userAgent;
        if (/iPad|iPhone|iPod/.test(ua)) {
            if (/CriOS/.test(ua)) return 'ios-chrome';
            if (/FxiOS/.test(ua)) return 'ios-firefox';
            if (/EdgiOS/.test(ua)) return 'ios-edge';
            return 'ios-safari';
        }
        if (/Android/.test(ua)) return 'android';
        return 'desktop';
    };

    window.liftx = L;
})();
