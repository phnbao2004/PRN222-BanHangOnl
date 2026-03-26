// ── Chat scroll helpers ────────────────────────────────────────────────
window.scrollChatBottom = () => {
  const el = document.getElementById('chatMessages');
  if (el) el.scrollTop = el.scrollHeight;
};

window.scrollElementBottom = (id) => {
  const el = document.getElementById(id);
  if (el) el.scrollTop = el.scrollHeight;
};

// ── Chart.js helpers ──────────────────────────────────────────────────
window._msCharts = {};

window.renderReportCharts = function(payload) {
  window.destroyReportCharts();

  // Set global Chart.js defaults
  Chart.defaults.font.family = "'Plus Jakarta Sans', sans-serif";
  Chart.defaults.color = '#9393aa';

  const brand   = '#e85d26';
  const brandBg = 'rgba(232,93,38,.12)';
  const blue    = '#2563eb';
  const green   = '#16a34a';
  const yellow  = '#ca8a04';
  const red     = '#dc2626';
  const purple  = '#7c3aed';

  const gridColor = '#e2e2ea';

  // 1. Revenue line chart (monthly)
  const revEl = document.getElementById('chartRevenue');
  if (revEl) {
    window._msCharts.revenue = new Chart(revEl, {
      type: 'line',
      data: {
        labels: payload.months,
        datasets: [{
          label: 'Doanh thu',
          data: payload.monthlyRevenue,
          borderColor: brand,
          backgroundColor: brandBg,
          borderWidth: 2.5,
          fill: true,
          tension: 0.42,
          pointBackgroundColor: brand,
          pointRadius: 4,
          pointHoverRadius: 7,
          pointBorderWidth: 0,
        }]
      },
      options: {
        responsive: true, maintainAspectRatio: true,
        plugins: {
          legend: { display: false },
          tooltip: {
            backgroundColor: '#0d0d17',
            titleColor: '#fff',
            bodyColor: 'rgba(255,255,255,.7)',
            padding: 10, cornerRadius: 8,
            callbacks: {
              label: ctx => fmtVnd(ctx.raw)
            }
          }
        },
        scales: {
          x: { grid: { display: false }, border: { display: false }, ticks: { font: { size: 12 } } },
          y: {
            grid: { color: gridColor },
            border: { display: false, dash: [4,4] },
            ticks: { font: { size: 12 }, callback: v => fmtShort(v) }
          }
        }
      }
    });
  }

  // 2. Order status doughnut
  const statusEl = document.getElementById('chartStatus');
  if (statusEl) {
    window._msCharts.status = new Chart(statusEl, {
      type: 'doughnut',
      data: {
        labels: ['Hoàn thành', 'Chờ xử lý', 'Đang giao', 'Đã hủy'],
        datasets: [{
          data: payload.orderStatus,
          backgroundColor: [green, yellow, blue, red],
          borderWidth: 0, hoverOffset: 8,
        }]
      },
      options: {
        responsive: true, cutout: '68%',
        plugins: {
          legend: { position: 'bottom', labels: { padding: 18, usePointStyle: true, pointStyleWidth: 9, font: { size: 12 } } },
          tooltip: { backgroundColor: '#0d0d17', padding: 10, cornerRadius: 8 }
        }
      }
    });
  }

  // 3. User breakdown bar chart
  const userEl = document.getElementById('chartUsers');
  if (userEl) {
    window._msCharts.users = new Chart(userEl, {
      type: 'bar',
      data: {
        labels: ['Khách thường', 'Khách VIP', 'Bị khóa'],
        datasets: [{
          data: payload.userBreakdown,
          backgroundColor: [blue, purple, red],
          borderRadius: 9, borderSkipped: false,
          hoverBackgroundColor: [blue, purple, red].map(c => c + 'cc')
        }]
      },
      options: {
        responsive: true,
        plugins: { legend: { display: false }, tooltip: { backgroundColor: '#0d0d17', padding: 10, cornerRadius: 8 } },
        scales: {
          x: { grid: { display: false }, border: { display: false } },
          y: { grid: { color: gridColor }, border: { display: false }, beginAtZero: true, ticks: { precision: 0 } }
        }
      }
    });
  }

  // 4. Stock health pie
  const stockEl = document.getElementById('chartStock');
  if (stockEl) {
    window._msCharts.stock = new Chart(stockEl, {
      type: 'pie',
      data: {
        labels: ['Còn hàng tốt', 'Sắp hết hàng', 'Hết hàng'],
        datasets: [{
          data: payload.stockStatus,
          backgroundColor: [green, yellow, red],
          borderWidth: 0, hoverOffset: 6,
        }]
      },
      options: {
        responsive: true,
        plugins: {
          legend: { position: 'bottom', labels: { padding: 14, usePointStyle: true, pointStyleWidth: 9, font: { size: 12 } } },
          tooltip: { backgroundColor: '#0d0d17', padding: 10, cornerRadius: 8 }
        }
      }
    });
  }
};

window.destroyReportCharts = function() {
  Object.values(window._msCharts).forEach(c => { try { c.destroy(); } catch {} });
  window._msCharts = {};
};

function fmtVnd(v) {
  if (v >= 1_000_000) return (v/1_000_000).toFixed(1) + 'M đ';
  if (v >= 1_000) return (v/1_000).toFixed(0) + 'K đ';
  return v + 'đ';
}
function fmtShort(v) {
  if (v >= 1_000_000) return (v/1_000_000).toFixed(0) + 'M';
  if (v >= 1_000) return (v/1_000).toFixed(0) + 'K';
  return v;
}
