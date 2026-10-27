import { ChartConfiguration } from 'chart.js';

// Canvas charts cannot read CSS variables, so these mirror the tokens in styles.scss
export const CHART = {
  font: 'IBM Plex Sans',
  tick: '#646c79',
  grid: '#eeebe5',
  zero: '#b9b2a5',
  line: '#102036',
  lineFill: 'rgba(16, 32, 54, 0.06)',
  positive: '#3f9a6c',
  negative: '#c95a50',
  tooltipBg: '#102036'
};

// Shared look for every chart: quiet grid, readable ticks, no legend for single series
export function baseChartOptions(): ChartConfiguration<'line'>['options'] {
  return {
    responsive: true,
    maintainAspectRatio: false,
    interaction: { mode: 'index', intersect: false },
    plugins: {
      legend: { display: false },
      tooltip: {
        backgroundColor: CHART.tooltipBg, padding: 10, cornerRadius: 6, displayColors: false,
        titleFont: { family: CHART.font, weight: 500 }, bodyFont: { family: CHART.font }
      }
    },
    scales: {
      x: {
        grid: { display: false },
        border: { color: CHART.grid },
        ticks: { color: CHART.tick, font: { family: CHART.font, size: 12 }, maxRotation: 0, autoSkip: true, maxTicksLimit: 8 }
      },
      y: {
        border: { display: false },
        grid: { color: ctx => (ctx.tick.value === 0 ? CHART.zero : CHART.grid) },
        ticks: { color: CHART.tick, font: { family: CHART.font, size: 12 }, maxTicksLimit: 6 }
      }
    }
  };
}
