// Ported from LiftoffVisualizer. Colors now come from the LiftX theme CSS variables.
function cssVar(name, fallback) {
    return (getComputedStyle(document.documentElement).getPropertyValue(name) || '').trim() || fallback;
}
function getPrimary() { return cssVar('--accent', '#3CB371'); }
function hexToRgb(hex) {
    const h = (hex || '').replace('#', '');
    return [parseInt(h.slice(0, 2), 16) || 0, parseInt(h.slice(2, 4), 16) || 0, parseInt(h.slice(4, 6), 16) || 0];
}
function primaryBg(alpha) {
    const [r, g, b] = hexToRgb(getPrimary());
    return `rgba(${r},${g},${b},${alpha})`;
}
function themeColors() {
    return {
        grid: cssVar('--border', 'rgba(255,255,255,0.1)'),
        text: cssVar('--muted', '#999'),
        tooltipBg: cssVar('--surface-3', '#333'),
        tooltipTitle: cssVar('--text', '#fff'),
        tooltipBody: cssVar('--text-2', '#ccc'),
        pointBorder: cssVar('--bg', '#000'),
    };
}

window.chartInterop = {
    charts: {},

    createChart: function (canvasId, config) {
        const existing = this.charts[canvasId];
        if (existing) existing.destroy();

        const ctx = document.getElementById(canvasId);
        if (!ctx || !window.Chart) return;
        const c = themeColors();
        const font = "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI', sans-serif";

        const chartConfig = {
            type: config.type || 'line',
            data: {
                labels: config.labels,
                datasets: config.datasets.map(ds => ({
                    ...ds,
                    borderColor: ds.borderColor || getPrimary(),
                    backgroundColor: ds.backgroundColor || primaryBg(0.15),
                    borderWidth: ds.borderWidth ?? 2.5,
                    pointRadius: ds.pointRadius ?? 3,
                    pointHoverRadius: ds.pointHoverRadius ?? 6,
                    pointBackgroundColor: ds.pointBackgroundColor || getPrimary(),
                    pointHoverBackgroundColor: ds.pointHoverBackgroundColor || getPrimary(),
                    pointHoverBorderColor: ds.pointHoverBorderColor || c.pointBorder,
                    pointBorderColor: c.pointBorder,
                    pointBorderWidth: ds.pointBorderWidth ?? 1.5,
                    tension: ds.tension ?? 0.3,
                    fill: ds.fill ?? true
                }))
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { intersect: false, mode: 'index' },
                plugins: {
                    legend: {
                        display: config.datasets.filter(d => !d.tooltipExtras).length > 1,
                        labels: {
                            color: c.text,
                            font: { family: font, size: 11 },
                            boxWidth: 14,
                            filter: function (item, chart) { return !chart.datasets[item.datasetIndex].tooltipExtras; }
                        }
                    },
                    tooltip: {
                        backgroundColor: c.tooltipBg,
                        titleColor: c.tooltipTitle,
                        bodyColor: c.tooltipBody,
                        borderColor: getPrimary(),
                        borderWidth: 1,
                        cornerRadius: 8,
                        padding: 10,
                        titleFont: { family: font, weight: '600' },
                        bodyFont: { family: font },
                        callbacks: {
                            title: function (items) {
                                if (!items.length) return '';
                                const raw = items[0].chart.data.labels[items[0].dataIndex];
                                const date = new Date(raw + 'T00:00:00');
                                if (isNaN(date)) return raw;
                                return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
                            },
                            label: function (item) {
                                const ds = item.chart.data.datasets[item.datasetIndex];
                                if (ds.tooltipExtras) return ds.tooltipExtras[item.dataIndex] || null;
                                return `${ds.label}: ${item.formattedValue}`;
                            },
                            labelColor: function (item) {
                                const ds = item.chart.data.datasets[item.datasetIndex];
                                if (ds.tooltipExtras) return { backgroundColor: 'transparent', borderColor: 'transparent' };
                                return undefined;
                            },
                            labelTextColor: function (item) {
                                const ds = item.chart.data.datasets[item.datasetIndex];
                                if (ds.tooltipExtras) return getPrimary();
                                return undefined;
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        grid: { color: c.grid },
                        ticks: {
                            color: c.text,
                            font: { family: font, size: 10 },
                            maxRotation: 45,
                            autoSkip: true,
                            maxTicksLimit: 8,
                            callback: function (value) {
                                const label = this.getLabelForValue(value);
                                const date = new Date(label + 'T00:00:00');
                                if (isNaN(date)) return label;
                                return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
                            }
                        }
                    },
                    y: {
                        grid: { color: c.grid },
                        ticks: { color: c.text, font: { family: font, size: 10 } },
                        beginAtZero: config.beginAtZero ?? false
                    }
                },
                animation: { duration: 500, easing: 'easeOutQuart' }
            },
            plugins: [{
                id: 'dynamicAccent',
                beforeUpdate(chart) {
                    const p = getPrimary();
                    const bg = primaryBg(0.15);
                    chart.data.datasets.forEach(ds => {
                        if (ds.tooltipExtras || ds.borderDash || ds.borderColor === 'transparent') return;
                        ds.borderColor = p;
                        ds.backgroundColor = bg;
                        ds.pointBackgroundColor = p;
                        ds.pointHoverBackgroundColor = p;
                    });
                },
                beforeDraw(chart) {
                    const p = getPrimary();
                    chart.options.plugins.tooltip.borderColor = p;
                    if (chart.tooltip) chart.tooltip.options.borderColor = p;
                }
            }]
        };

        this.charts[canvasId] = new Chart(ctx, chartConfig);
    },

    destroyChart: function (canvasId) {
        const chart = this.charts[canvasId];
        if (chart) {
            chart.destroy();
            delete this.charts[canvasId];
        }
    },

    refreshAllCharts: function () {
        const c = themeColors();
        Object.keys(this.charts).forEach(id => {
            const chart = this.charts[id];
            if (!chart) return;
            chart.options.plugins.tooltip.backgroundColor = c.tooltipBg;
            chart.options.plugins.tooltip.titleColor = c.tooltipTitle;
            chart.options.plugins.tooltip.bodyColor = c.tooltipBody;
            chart.options.scales.x.grid.color = c.grid;
            chart.options.scales.x.ticks.color = c.text;
            chart.options.scales.y.grid.color = c.grid;
            chart.options.scales.y.ticks.color = c.text;
            if (chart.options.plugins.legend.labels) chart.options.plugins.legend.labels.color = c.text;
            chart.update();
        });
    }
};
