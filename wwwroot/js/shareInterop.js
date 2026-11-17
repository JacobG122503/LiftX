// Ported from LiftoffVisualizer: builds the "My Lifting Journey" share image, in your LiftX colors.
window.shareInterop = (function () {
    let last = null;

    function cssVar(name, fallback) {
        return (getComputedStyle(document.documentElement).getPropertyValue(name) || '').trim() || fallback;
    }
    function rgb(hex) {
        const h = (hex || '').replace('#', '');
        return [parseInt(h.slice(0, 2), 16) || 0, parseInt(h.slice(2, 4), 16) || 0, parseInt(h.slice(4, 6), 16) || 0];
    }
    function rgba(hex, a) { const [r, g, b] = rgb(hex); return `rgba(${r},${g},${b},${a})`; }
    function mix(hex, toward, t) {
        const a = rgb(hex), b = rgb(toward);
        return '#' + a.map((v, i) => Math.round(v + (b[i] - v) * t).toString(16).padStart(2, '0')).join('');
    }
    function clip(ctx, text, maxW) {
        let v = text || '';
        if (ctx.measureText(v).width <= maxW) return v;
        while (ctx.measureText(v + '…').width > maxW && v.length > 3) v = v.slice(0, -1);
        return v + '…';
    }

    function buildCanvas(stats) {
        const W = 1080, H = 1350;
        const accent = cssVar('--accent', '#3CB371');
        const onAccent = cssVar('--on-accent', '#000');
        // The share image is always dark so it looks good on any feed
        const base = '#050505';
        const canvas = document.createElement('canvas');
        canvas.width = W; canvas.height = H;
        const ctx = canvas.getContext('2d');
        const font = (w, s) => `${w} ${s}px -apple-system, BlinkMacSystemFont, system-ui, sans-serif`;

        const bg = ctx.createLinearGradient(0, 0, W, H);
        bg.addColorStop(0, base);
        bg.addColorStop(1, mix(accent, base, 0.82));
        ctx.fillStyle = bg;
        ctx.fillRect(0, 0, W, H);

        ctx.strokeStyle = 'rgba(255,255,255,0.04)';
        ctx.lineWidth = 1;
        for (let x = 0; x < W; x += 90) { ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, H); ctx.stroke(); }
        for (let y = 0; y < H; y += 90) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke(); }

        const glow = ctx.createRadialGradient(W * 0.75, H * 0.2, 0, W * 0.75, H * 0.2, 520);
        glow.addColorStop(0, rgba(accent, 0.38));
        glow.addColorStop(1, rgba(accent, 0));
        ctx.fillStyle = glow;
        ctx.fillRect(0, 0, W, H);

        ctx.fillStyle = accent;
        ctx.beginPath(); ctx.roundRect(80, 80, 200, 52, 26); ctx.fill();
        ctx.fillStyle = onAccent;
        ctx.font = font('bold', 28);
        ctx.textBaseline = 'middle';
        ctx.fillText('🏋️ LiftX', 104, 106);

        ctx.fillStyle = '#fff';
        ctx.font = font('bold', 92);
        ctx.textBaseline = 'alphabetic';
        ctx.fillText('My Lifting', 80, 260);
        ctx.fillStyle = accent;
        ctx.fillText('Journey', 80, 365);
        ctx.fillStyle = 'rgba(255,255,255,0.5)';
        ctx.font = font('', 32);
        ctx.fillText(`Since ${stats.sinceDate}`, 80, 420);

        const div = ctx.createLinearGradient(80, 0, W - 80, 0);
        div.addColorStop(0, rgba(accent, 0.9));
        div.addColorStop(1, rgba(accent, 0));
        ctx.fillStyle = div;
        ctx.fillRect(80, 455, W - 160, 2);

        const primary = [
            { label: 'Total Workouts', value: stats.totalWorkouts.toLocaleString() },
            { label: 'Exercises Tracked', value: stats.exerciseCount.toLocaleString() },
            { label: 'Total Lbs Lifted', value: Math.round(stats.totalVolume).toLocaleString() },
            { label: 'Total Sets', value: stats.totalSets.toLocaleString() },
        ];
        const colW = (W - 160) / 2, rowH = 160, startY = 475;
        primary.forEach((item, i) => {
            const x = 80 + (i % 2) * colW, y = startY + Math.floor(i / 2) * rowH;
            ctx.fillStyle = 'rgba(255,255,255,0.07)';
            ctx.beginPath(); ctx.roundRect(x + 8, y + 8, colW - 16, rowH - 16, 14); ctx.fill();
            ctx.fillStyle = '#fff';
            ctx.font = font('bold', 52);
            ctx.fillText(item.value, x + 26, y + 76);
            ctx.fillStyle = 'rgba(255,255,255,0.45)';
            ctx.font = font('', 24);
            ctx.fillText(item.label, x + 26, y + 114);
        });

        ctx.fillStyle = 'rgba(255,255,255,0.08)';
        ctx.fillRect(80, 815, W - 160, 1);

        const secondary = [
            { label: 'Longest Streak', value: `${stats.longestStreak}d 🔥` },
            { label: 'Longest Break', value: `${stats.longestBreak}d 😴` },
            { label: 'Total Reps', value: stats.totalReps.toLocaleString() + ' 💪' },
            { label: 'Heaviest Lift', value: `${stats.heaviestLift} lbs` },
            { label: 'Avg / Week', value: `${stats.avgWorkoutsPerWeek}x` },
            { label: 'Busiest Day', value: stats.busiestDay },
        ];
        const secColW = (W - 160) / 3, secRowH = 120, secY = 835;
        secondary.forEach((item, i) => {
            const x = 80 + (i % 3) * secColW, y = secY + Math.floor(i / 3) * secRowH;
            ctx.fillStyle = 'rgba(255,255,255,0.05)';
            ctx.beginPath(); ctx.roundRect(x + 6, y + 6, secColW - 12, secRowH - 12, 12); ctx.fill();
            ctx.fillStyle = accent;
            ctx.font = font('bold', 36);
            ctx.fillText(clip(ctx, item.value, secColW - 28), x + 18, y + 58);
            ctx.fillStyle = 'rgba(255,255,255,0.4)';
            ctx.font = font('', 20);
            ctx.fillText(item.label, x + 18, y + 90);
        });

        ctx.fillStyle = rgba(accent, 0.15);
        ctx.beginPath(); ctx.roundRect(80, 1085, W - 160, 100, 16); ctx.fill();
        ctx.strokeStyle = rgba(accent, 0.4);
        ctx.lineWidth = 1.5;
        ctx.beginPath(); ctx.roundRect(80, 1085, W - 160, 100, 16); ctx.stroke();
        ctx.textBaseline = 'middle';
        ctx.fillStyle = 'rgba(255,255,255,0.4)';
        ctx.font = font('', 22);
        ctx.fillText('Favorite exercise', 108, 1118);
        ctx.fillText('Most improved', 600, 1118);
        ctx.fillStyle = '#fff';
        ctx.font = font('bold', 30);
        ctx.fillText(clip(ctx, '⭐ ' + stats.favoriteExercise, 460), 108, 1152);
        ctx.font = font('bold', 28);
        ctx.fillText(clip(ctx, '📈 ' + stats.mostImprovedExercise, 380), 600, 1152);

        ctx.fillStyle = accent;
        const daysStr = `${stats.daysSinceStart} days strong 💪`;
        ctx.font = font('bold', 30);
        const daysW = ctx.measureText(daysStr).width + 48;
        ctx.beginPath(); ctx.roundRect(W - daysW - 80, H - 110, daysW, 58, 29); ctx.fill();
        ctx.fillStyle = onAccent;
        ctx.fillText(daysStr, W - daysW - 56, H - 81);

        return canvas;
    }

    return {
        generatePreview: function (stats) {
            return new Promise((resolve) => {
                buildCanvas(stats).toBlob((blob) => {
                    if (last) URL.revokeObjectURL(last.url);
                    const url = URL.createObjectURL(blob);
                    last = { blob, url, stats };
                    resolve(url);
                }, 'image/png');
            });
        },

        shareFromPreview: async function () {
            if (!last) return;
            const file = new File([last.blob], 'my-lifting-journey.png', { type: 'image/png' });
            if (navigator.share && navigator.canShare && navigator.canShare({ files: [file] })) {
                try {
                    await navigator.share({ files: [file], title: 'My Lifting Journey' });
                    return;
                } catch (e) { if (e && e.name === 'AbortError') return; }
            }
            const a = document.createElement('a');
            a.href = last.url;
            a.download = 'my-lifting-journey.png';
            a.click();
        }
    };
})();
