/**
 * Google Flow Extended - Unified Skeuomorphic Frontend Controller
 */

document.addEventListener('DOMContentLoaded', () => {
  // Active tab highlighting
  const currentPath = window.location.pathname;
  document.querySelectorAll('.nav-btn').forEach(btn => {
    const href = btn.getAttribute('href');
    if (href === currentPath || (currentPath === '/' && href === '/')) {
      btn.classList.add('active');
    } else {
      btn.classList.remove('active');
    }
  });

  // Global Status Polling
  initStatusPoller();
});

function initStatusPoller() {
  refreshSystemStatus();
  setInterval(refreshSystemStatus, 4000);
}

async function refreshSystemStatus() {
  try {
    const res = await fetch('/setup/status');
    if (!res.ok) return;
    const data = await res.json();

    // Update status LEDs
    updateLed('ledApiHealth', true, 'cyan');
    updateLed('ledTokenLive', data.live_login_detected, 'green');
    updateLed('ledTokenPersisted', data.persisted_login_detected, 'amber');
    updateLed('ledProjectReady', data.project_ready, 'green');

    // Update Metrics
    const stEl = document.getElementById('metricStStatus');
    if (stEl) {
      stEl.textContent = data.has_st ? 'ACTIVE' : 'EXPIRED';
      stEl.style.color = data.has_st ? '#10b981' : '#ef4444';
    }

    const atEl = document.getElementById('metricAtStatus');
    if (atEl) {
      atEl.textContent = data.has_at ? 'VALID' : 'REFRESHING';
      atEl.style.color = data.has_at ? '#10b981' : '#f59e0b';
    }

    const projEl = document.getElementById('metricProjectId');
    if (projEl && data.project_ready) {
      projEl.textContent = 'READY';
      projEl.style.color = '#06b6d4';
    }

    // Update API Key Display if present
    const apiKeyEl = document.getElementById('displayApiKey');
    if (apiKeyEl && data.api && data.api.api_key) {
      apiKeyEl.value = data.api.api_key;
    }

    // Auto-update Setup Wizard Step LEDs & Text
    const step1Led = document.getElementById('step1Led');
    const step1Text = document.getElementById('step1Text');
    if (step1Led && step1Text) {
      updateLed('step1Led', data.browser_open, 'green');
      step1Text.textContent = data.browser_open ? 'BROWSER ACTIVE' : 'READY TO LAUNCH';
      step1Text.style.color = data.browser_open ? '#10b981' : '#64748b';
    }

    const step2Led = document.getElementById('step2Led');
    const step2Text = document.getElementById('step2Text');
    if (step2Led && step2Text) {
      const loggedIn = data.login_detected || data.has_st || data.live_login_detected;
      if (loggedIn) {
        updateLed('step2Led', true, 'green');
        step2Text.textContent = 'LOGGED IN (ST COOKIE DETECTED)';
        step2Text.style.color = '#10b981';
      } else if (data.browser_open) {
        updateLed('step2Led', true, 'amber');
        step2Text.textContent = 'AWAITING LOGIN IN BROWSER...';
        step2Text.style.color = '#f59e0b';
      } else {
        updateLed('step2Led', false, 'amber');
        step2Text.textContent = 'BROWSER NOT STARTED';
        step2Text.style.color = '#64748b';
      }
    }

    const setupStepLed = document.getElementById('setupStepLed');
    if (setupStepLed) {
      const isComplete = data.has_st && data.project_ready;
      updateLed('setupStepLed', true, isComplete ? 'green' : 'amber');
    }

  } catch (err) {
    updateLed('ledApiHealth', false, 'red');
  }
}

function updateLed(id, active, colorClass) {
  const el = document.getElementById(id);
  if (!el) return;
  el.className = `led led-${colorClass} ${active ? 'active' : ''}`;
}

// Global helper for logging to CRT screen
function appendLog(elementId, message, type = 'info') {
  const crt = document.getElementById(elementId);
  if (!crt) return;

  const timeStr = new Date().toLocaleTimeString();
  const line = document.createElement('div');
  line.style.marginBottom = '4px';

  let color = '#38bdf8';
  if (type === 'success') color = '#34d399';
  if (type === 'warn') color = '#fbbf24';
  if (type === 'error') color = '#f87171';

  line.innerHTML = `<span style="color: #64748b;">[${timeStr}]</span> <span style="color: ${color};">${message}</span>`;
  crt.appendChild(line);
  crt.scrollTop = crt.scrollHeight;
}
