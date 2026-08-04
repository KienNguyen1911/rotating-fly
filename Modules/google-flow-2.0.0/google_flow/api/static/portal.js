const REVEALED_API_KEY_STORAGE_KEY = "fcs_portal_revealed_api_keys";
const DEFAULT_PAGE_LIMIT = 20;

const state = {
  summary: null,
  user: null,
  workspace: null,
  page: "dashboard",
  revealedApiKeys: {},
  apiKeyModal: {
    name: "",
    value: "",
  },
  transactions: {
    items: [],
    limit: DEFAULT_PAGE_LIMIT,
    offset: 0,
    total: 0,
    has_prev: false,
    has_next: false,
    loaded: false,
  },
  logs: {
    items: [],
    limit: DEFAULT_PAGE_LIMIT,
    offset: 0,
    total: 0,
    has_prev: false,
    has_next: false,
    loaded: false,
  },
};

const pageMetaMap = {
  dashboard: {
    eyebrow: "Overview",
    title: "My times and usage",
    desc: "Centrally check the remaining times of the current account, the call success rate and the latest recharge result.",
    shortTitle: "Overview",
  },
  leaderboard: {
    eyebrow: "Ranking list",
    title: "Site usage rankings",
    desc: "Separately view the current site's user request volume, number of successes, activity in the past 7 days, and used quota rankings.",
    shortTitle: "Ranking list",
  },
  apiKeys: {
    eyebrow: "API Key",
    title: "Personal API Key workspace",
    desc: "Apply for, start, stop, and copy your own API Key. The complete Key will only be returned after creation and cached on this page.",
    shortTitle: "API Key",
  },
  redeem: {
    eyebrow: "Recharge Center",
    title: "Recharge and consumption records",
    desc: "Redeem the CDK issued by the administrator, and review the recent recharge results and each limit change.",
    shortTitle: "Recharge Center",
  },
  logs: {
    eyebrow: "call record",
    title: "Interface call log",
    desc: "Filter and view the call status, project identification and failure reasons under the current account to facilitate troubleshooting access problems.",
    shortTitle: "call record",
  },
  account: {
    eyebrow: "Account information",
    title: "Current login account",
    desc: "View the current user name, remaining times, and basic account information. Administrator-side data is not involved.",
    shortTitle: "Account information",
  },
};

const dom = {
  byId(id) {
    return document.getElementById(id);
  },
};

function setPortalMenuOpen(open) {
  const sidebar = dom.byId("portalSidebar");
  const backdrop = dom.byId("portalMenuBackdrop");
  const menuButton = dom.byId("portalMenuBtn");
  const shouldOpen = !!open && isAuthenticated();

  document.body.classList.toggle("portal-menu-open", shouldOpen);
  sidebar?.classList.toggle("show", shouldOpen);
  showBlock("portalMenuBackdrop", shouldOpen);

  if (backdrop) {
    backdrop.setAttribute("aria-hidden", shouldOpen ? "false" : "true");
  }
  if (menuButton) {
    menuButton.setAttribute("aria-expanded", shouldOpen ? "true" : "false");
  }
}

function closePortalMenu() {
  setPortalMenuOpen(false);
}

function setText(id, value) {
  const element = dom.byId(id);
  if (element) {
    element.textContent = value;
  }
}

function abbreviateUsername(value, maxLength = 24) {
  const text = String(value || "").trim();
  if (!text) {
    return "--";
  }
  if (text.length <= maxLength) {
    return text;
  }
  const headLength = Math.max(8, Math.ceil((maxLength - 1) / 2));
  const tailLength = Math.max(6, Math.floor((maxLength - 1) / 2));
  return `${text.slice(0, headLength)}…${text.slice(-tailLength)}`;
}

function showBlock(id, visible) {
  const element = dom.byId(id);
  if (!element) {
    return;
  }
  element.classList.toggle("hidden-block", !visible);
}

function showNotice(id, message, kind = "info") {
  const element = dom.byId(id);
  if (!element) {
    return;
  }
  element.className = `notice ${kind}`;
  element.textContent = message;
  element.classList.remove("hidden-block");
}

function formatDateTime(value) {
  if (!value) {
    return "--";
  }
  const normalized = String(value).replace(" ", "T");
  const date = new Date(normalized.endsWith("Z") ? normalized : `${normalized}Z`);
  if (Number.isNaN(date.getTime())) {
    return String(value);
  }
  return date.toLocaleString("zh-CN", { hour12: false });
}

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function statusClass(status) {
  const text = String(status || "").toLowerCase();
  if (text.includes("success")) {
    return "success";
  }
  if (text.includes("fail") || text.includes("error")) {
    return "error";
  }
  if (text.includes("cancel") || text.includes("timeout")) {
    return "warning";
  }
  return "info";
}

function isAuthenticated() {
  return !!state.user;
}

function resetTransactionsState() {
  state.transactions = {
    items: [],
    limit: DEFAULT_PAGE_LIMIT,
    offset: 0,
    total: 0,
    has_prev: false,
    has_next: false,
    loaded: false,
  };
}

function resetLogsState() {
  state.logs = {
    items: [],
    limit: DEFAULT_PAGE_LIMIT,
    offset: 0,
    total: 0,
    has_prev: false,
    has_next: false,
    loaded: false,
  };
}

function loadRevealedApiKeys() {
  try {
    const raw = sessionStorage.getItem(REVEALED_API_KEY_STORAGE_KEY);
    const parsed = raw ? JSON.parse(raw) : {};
    state.revealedApiKeys = parsed && typeof parsed === "object" ? parsed : {};
  } catch (_) {
    state.revealedApiKeys = {};
  }
}

function persistRevealedApiKeys() {
  sessionStorage.setItem(REVEALED_API_KEY_STORAGE_KEY, JSON.stringify(state.revealedApiKeys || {}));
}

function cacheRevealedApiKey(id, rawKey) {
  const normalizedId = String(id || "").trim();
  const normalizedKey = String(rawKey || "").trim();
  if (!normalizedId || !normalizedKey) {
    return;
  }
  state.revealedApiKeys[normalizedId] = normalizedKey;
  persistRevealedApiKeys();
}

function getCachedApiKey(id) {
  const normalizedId = String(id || "").trim();
  if (!normalizedId) {
    return "";
  }
  return String(state.revealedApiKeys?.[normalizedId] || "");
}

async function copyText(text) {
  const normalized = String(text || "");
  if (!normalized) {
    throw new Error("No content to copy");
  }
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(normalized);
    return;
  }
  const textarea = document.createElement("textarea");
  textarea.value = normalized;
  textarea.setAttribute("readonly", "readonly");
  textarea.style.position = "fixed";
  textarea.style.opacity = "0";
  document.body.appendChild(textarea);
  textarea.select();
  document.execCommand("copy");
  document.body.removeChild(textarea);
}

async function requestJson(url, options = {}) {
  const headers = {
    Accept: "application/json",
    ...(options.headers || {}),
  };
  let body;
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(options.body);
  }

  const response = await fetch(url, {
    method: options.method || "GET",
    headers,
    body,
    credentials: "same-origin",
  });

  const rawText = await response.text();
  let payload = {};
  if (rawText) {
    try {
      payload = JSON.parse(rawText);
    } catch (_) {
      payload = { raw: rawText };
    }
  }

  if (!response.ok) {
    throw new Error(payload.detail || payload.message || rawText || `HTTP ${response.status}`);
  }
  return payload;
}

function renderAuthTab(tab) {
  document.querySelectorAll(".auth-tab").forEach((button) => {
    button.classList.toggle("active", button.dataset.authTab === tab);
  });
  dom.byId("loginPane")?.classList.toggle("active", tab === "login");
  dom.byId("registerPane")?.classList.toggle("active", tab === "register");
}

function closeApiKeyModal() {
  const modal = dom.byId("apiKeyModal");
  if (!modal) {
    return;
  }
  document.body.classList.remove("modal-open");
  modal.classList.add("hidden-block");
  modal.classList.remove("show");
  modal.setAttribute("aria-hidden", "true");
}

function openApiKeyModal(name, value) {
  state.apiKeyModal.name = String(name || "--");
  state.apiKeyModal.value = String(value || "");
  setText("apiKeyModalName", state.apiKeyModal.name);
  setText("apiKeyModalValue", state.apiKeyModal.value || "--");

  const modal = dom.byId("apiKeyModal");
  if (!modal) {
    return;
  }
  document.body.classList.add("modal-open");
  modal.classList.remove("hidden-block");
  modal.classList.add("show");
  modal.setAttribute("aria-hidden", "false");
}

function switchPage(page) {
  state.page = page;
  document.querySelectorAll(".nav-btn").forEach((button) => {
    button.classList.toggle("active", button.dataset.page === page);
  });
  ["dashboard", "leaderboard", "apiKeys", "redeem", "logs", "account"].forEach((name) => {
    dom.byId(`page${name.charAt(0).toUpperCase()}${name.slice(1)}`)?.classList.toggle("active", name === page);
  });
  const meta = pageMetaMap[page] || pageMetaMap.dashboard;
  setText("workspaceEyebrow", meta.eyebrow);
  setText("workspaceTitle", meta.title);
  setText("workspaceDesc", meta.desc);
  setText("workspacePage", meta.shortTitle);
  closePortalMenu();
}

function renderShell() {
  const authenticated = isAuthenticated();
  showBlock("guestView", !authenticated);
  showBlock("appView", authenticated);
  document.body.classList.toggle("portal-authenticated", authenticated);
  if (!authenticated) {
    closeApiKeyModal();
    showBlock("appNotice", false);
    closePortalMenu();
  }
}

function getRegisterLocation() {
  return window.location.pathname === "/" ? "/" : "master-portal";
}

function isOidcEnabled() {
  return !!(state.summary?.auth?.oidc?.enabled || state.summary?.capabilities?.user_login_oidc);
}

function isOauthOnly() {
  return !!state.summary?.auth?.oauth_only;
}

function renderSummaryHints() {
  const role = state.summary?.service?.role || state.summary?.meta?.role || "unknown";
  const registerButton = dom.byId("registerSubmitBtn");
  const locationHint = dom.byId("registerLocationHint");
  const oidcWrap = dom.byId("oidcLoginWrap");
  const oidcHint = dom.byId("oidcLoginHint");
  const authModeHint = dom.byId("authModeHint");
  const localLoginForm = dom.byId("loginForm");
  const registerTabBtn = dom.byId("tabRegisterBtn");

  if (locationHint) {
    locationHint.textContent = role === "master"
      ? "Currently checks are being made to register from the master node portal."
      : "It is not currently the master node portal and self-registration may be rejected by the backend.";
  }
  if (registerButton) {
    registerButton.disabled = isOauthOnly();
  }
  showBlock("loginForm", !isOauthOnly());
  showBlock("tabRegisterBtn", !isOauthOnly());
  if (isOauthOnly()) {
    renderAuthTab("login");
  }
  showBlock("oidcLoginWrap", isOidcEnabled());
  if (oidcHint) {
    oidcHint.textContent = isOidcEnabled()
      ? `Standard OAuth2/OIDC login enabled, default scope: ${state.summary?.auth?.oidc?.scope || "openid profile email"}。`
      : "";
  }
  showBlock("authModeHint", isOauthOnly());
  if (authModeHint) {
    authModeHint.textContent = isOauthOnly()
      ? "The current site has enabled only OAuth/OIDC login, username and password login and self-registration are closed."
      : "";
  }
  if (localLoginForm && isOauthOnly()) {
    localLoginForm.reset();
  }
  if (registerTabBtn && isOauthOnly()) {
    registerTabBtn.classList.remove("active");
  }
}

async function loadSummary() {
  try {
    state.summary = await requestJson("/api/portal/summary");
  } catch (_) {
    state.summary = null;
  }
  renderSummaryHints();
}

function renderHeader() {
  const user = state.user || {};
  const username = String(user.username || "--");
  const shortUsername = abbreviateUsername(username, 22);
  setText("headerTitle", shortUsername || "Current account");
  setText("headerSubtitle", "Only the information and operations of the current account are displayed.");
  setText("headerUsername", shortUsername);
  setText("headerQuota", `Remaining times ${user.quota_remaining ?? 0}`);
  setText("workspaceUser", shortUsername);
  const headerUsernameEl = dom.byId("headerUsername");
  if (headerUsernameEl) {
    headerUsernameEl.title = username;
  }
  const headerTitleEl = dom.byId("headerTitle");
  if (headerTitleEl) {
    headerTitleEl.title = username;
  }
}

function renderDashboard() {
  const user = state.workspace?.user || state.user || {};
  const usage = state.workspace?.usage || {};
  const recentRedeems = Array.isArray(state.workspace?.recent_redeems) ? state.workspace.recent_redeems : [];
  const latestRedeem = recentRedeems.length > 0 ? recentRedeems[0] : null;
  const checkin = state.workspace?.checkin || {};

  setText("statQuotaRemaining", user.quota_remaining ?? 0);
  setText("statQuotaUsed", user.quota_used ?? 0);
  setText("statSolveSuccess", usage.solve_success_total ?? 0);
  setText("statSolveFailed", usage.solve_failed_total ?? 0);
  setText(
    "statSuccessRate",
    usage.success_rate == null ? "--" : `${Number(usage.success_rate).toFixed(2)}%`,
  );
  setText("statLastRequest", formatDateTime(usage.last_request_at));
  const registerBonus = Number(state.summary?.auth?.register_bonus_quota || 0);
  setText("usageRuleText", registerBonus > 0
    ? `One time will be deducted only if the final generation is successful; the number will be returned if it fails, cancels or reports an error. New user registration will receive ${registerBonus} times. `
    : "Only one time will be deducted if the final generation is successful; the number of times will be refunded if it fails, cancels or reports an error.");
  const checkinButton = dom.byId("checkinBtn");
  if (checkinButton) {
    checkinButton.disabled = !!checkin.checked_in_today || Number(state.summary?.auth?.checkin_max_quota || 0) <= 0;
  }
  if (Number(state.summary?.auth?.checkin_max_quota || 0) <= 0) {
    showNotice("checkinNotice", "Sign-in rewards are currently not enabled.", "info");
  } else if (checkin.checked_in_today) {
    showNotice("checkinNotice", `You have checked in today and received ${checkin.today_reward || 0} rewards. `, "success");
  } else {
    showNotice("checkinNotice", `You can check in today, and the reward range is ${state.summary?.auth?.checkin_min_quota || 0}-${state.summary?.auth?.checkin_max_quota || 0} times. `, "info");
  }

  showBlock("latestRedeemEmpty", !latestRedeem);
  showBlock("latestRedeemCard", !!latestRedeem);
  if (latestRedeem) {
    setText("latestRedeemCode", latestRedeem.code || "--");
    setText("latestRedeemQuota", latestRedeem.quota_times ?? "--");
    setText("latestRedeemTime", formatDateTime(latestRedeem.redeemed_at));
  }
}

function renderLeaderboard() {
  const tbody = dom.byId("leaderboardTableBody");
  if (!tbody) {
    return;
  }
  const items = Array.isArray(state.workspace?.leaderboard) ? state.workspace.leaderboard : [];
  if (!items.length) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty-cell">No ranking data yet. </td></tr>';
    return;
  }
  tbody.innerHTML = items.map((item) => `
    <tr>
      <td>${escapeHtml(String(item.rank ?? "--"))}</td>
      <td>${escapeHtml(item.display_name || item.username || "--")}</td>
      <td>${escapeHtml(String(item.request_total ?? 0))}</td>
      <td>${escapeHtml(String(item.solve_success_total ?? 0))}</td>
      <td>${escapeHtml(String(item.recent_7d_total ?? 0))}</td>
      <td>${escapeHtml(String(item.quota_used ?? 0))}</td>
    </tr>
  `).join("");
}

function renderApiKeys() {
  const tbody = dom.byId("apiKeysTableBody");
  if (!tbody) {
    return;
  }
  const items = Array.isArray(state.workspace?.api_keys) ? state.workspace.api_keys : [];
  if (!items.length) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty-cell">No API Key yet. </td></tr>';
    return;
  }
  tbody.innerHTML = items.map((item) => {
    const rawKey = getCachedApiKey(item.id);
    const copyLabel = rawKey? "Copy Key" : "Copy prefix";
    return `
      <tr>
        <td>${escapeHtml(item.name || "--")}</td>
        <td>${escapeHtml(item.key_prefix || "--")}</td>
        <td><span class="status-chip ${item.enabled ? "success" : "warning"}">${item.enabled ? "Activating" : "Disabled"}</span></td>
        <td>${escapeHtml(String(item.quota_used ?? 0))}</td>
        <td>${escapeHtml(formatDateTime(item.last_used_at))}</td>
        <td>
          <div class="action-row">
            <button class="btn subtle mini-btn" type="button" data-action="toggle-api-key" data-id="${item.id}" data-enabled="${item.enabled ? 1 : 0}">${item.enabled ? "Disable" : "enable"}</button>
            <button class="btn subtle mini-btn" type="button" data-action="copy-api-key" data-id="${item.id}" data-prefix="${escapeHtml(item.key_prefix || "")}">${copyLabel}</button>
            <button class="btn ghost mini-btn" type="button" data-action="delete-api-key" data-id="${item.id}">Soft delete</button>
          </div>
        </td>
      </tr>
    `;
  }).join("");
}

function renderTransactionsPager() {
  const infoEl = dom.byId("transactionsPagerInfo");
  const prevEl = dom.byId("transactionsPrevBtn");
  const nextEl = dom.byId("transactionsNextBtn");
  if (!infoEl || !prevEl || !nextEl) {
    return;
  }
  const limit = Math.max(1, Number(state.transactions.limit || DEFAULT_PAGE_LIMIT));
  const offset = Math.max(0, Number(state.transactions.offset || 0));
  const total = Math.max(0, Number(state.transactions.total || 0));
  const page = Math.floor(offset / limit) + 1;
  const totalPages = total > 0 ? Math.ceil(total / limit) : 1;
  const from = total > 0 ? offset + 1 : 0;
  const to = total > 0 ? Math.min(offset + limit, total) : 0;
  infoEl.textContent = total > 0
    ? `Page ${page}/${totalPages}, showing ${from}-${to}, total ${total} items`
    : "There is no consumption record yet.";
  prevEl.disabled = !state.transactions.has_prev;
  nextEl.disabled = !state.transactions.has_next;
}

function renderTransactions() {
  const tbody = dom.byId("transactionsTableBody");
  if (!tbody) {
    return;
  }
  const items = state.transactions.loaded
    ? state.transactions.items
    : (Array.isArray(state.workspace?.recent_transactions) ? state.workspace.recent_transactions : []);
  if (!items.length) {
    tbody.innerHTML = '<tr><td colspan="5" class="empty-cell">No consumption record yet. </td></tr>';
    renderTransactionsPager();
    return;
  }
  tbody.innerHTML = items.map((item) => {
    const amount = Number(item.change_amount || 0);
    return `
      <tr>
        <td>${escapeHtml(formatDateTime(item.created_at))}</td>
        <td>${escapeHtml(amount > 0 ? `+${amount}` : String(amount))}</td>
        <td>${escapeHtml(String(item.balance_after ?? "--"))}</td>
        <td>${escapeHtml(item.source_type || "--")}</td>
        <td>${escapeHtml(item.note || item.source_ref || "-")}</td>
      </tr>
    `;
  }).join("");
  renderTransactionsPager();
}

function renderRedeems() {
  const tbody = dom.byId("redeemTableBody");
  if (!tbody) {
    return;
  }
  const items = Array.isArray(state.workspace?.recent_redeems) ? state.workspace.recent_redeems : [];
  if (!items.length) {
    tbody.innerHTML = '<tr><td colspan="4" class="empty-cell">There is no redemption record yet. </td></tr>';
    return;
  }
  tbody.innerHTML = items.map((item) => `
    <tr>
      <td>${escapeHtml(item.code || "--")}</td>
      <td>${escapeHtml(String(item.quota_times ?? "--"))}</td>
      <td>${escapeHtml(formatDateTime(item.redeemed_at))}</td>
      <td>${escapeHtml(item.note || "-")}</td>
    </tr>
  `).join("");
}

function renderLogsPager() {
  const infoEl = dom.byId("logsPagerInfo");
  const prevEl = dom.byId("logsPrevBtn");
  const nextEl = dom.byId("logsNextBtn");
  if (!infoEl || !prevEl || !nextEl) {
    return;
  }
  const limit = Math.max(1, Number(state.logs.limit || DEFAULT_PAGE_LIMIT));
  const offset = Math.max(0, Number(state.logs.offset || 0));
  const total = Math.max(0, Number(state.logs.total || 0));
  const page = Math.floor(offset / limit) + 1;
  const totalPages = total > 0 ? Math.ceil(total / limit) : 1;
  const from = total > 0 ? offset + 1 : 0;
  const to = total > 0 ? Math.min(offset + limit, total) : 0;
  infoEl.textContent = total > 0
    ? `Page ${page}/${totalPages}, showing ${from}-${to}, total ${total} items`
    : "There are no log records yet.";
  prevEl.disabled = !state.logs.has_prev;
  nextEl.disabled = !state.logs.has_next;
}

function renderLogs() {
  const tbody = dom.byId("logsTableBody");
  if (!tbody) {
    return;
  }
  const items = state.logs.items || [];
  if (!items.length) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty-cell">No log records yet. </td></tr>';
    renderLogsPager();
    return;
  }
  tbody.innerHTML = items.map((item) => `
    <tr>
      <td>${escapeHtml(formatDateTime(item.created_at))}</td>
      <td><span class="status-chip ${statusClass(item.status)}">${escapeHtml(item.status || "--")}</span></td>
      <td>${escapeHtml(item.project_id || "--")}</td>
      <td>${escapeHtml(item.action || "--")}</td>
      <td>${escapeHtml(item.api_key_prefix || item.api_key_name || "--")}</td>
      <td>${escapeHtml(item.error_reason || "-")}</td>
    </tr>
  `).join("");
  renderLogsPager();
}

function renderAccount() {
  const user = state.workspace?.user || state.user || {};
  const username = String(user.username || "--");
  setText("accountUsernameValue", abbreviateUsername(username, 30));
  setText("accountRegisteredValue", isAuthenticated() ? "Registered" : "Not registered");
  setText("accountLocationValue", user.register_location || "--");
  setText("accountEnabledValue", user.enabled ? "Activating" : "Disabled");
  setText("accountCreatedAtValue", formatDateTime(user.created_at));
  setText("accountLastLoginValue", formatDateTime(user.last_login_at));
  const accountUsernameEl = dom.byId("accountUsernameValue");
  if (accountUsernameEl) {
    accountUsernameEl.title = username;
  }
}

function renderWorkspace() {
  renderShell();
  if (!isAuthenticated()) {
    return;
  }
  renderHeader();
  renderDashboard();
  renderApiKeys();
  renderRedeems();
  renderTransactions();
  renderLogs();
  renderAccount();
  renderLeaderboard();
}

function applyPagedPayload(target, payload, fallbackLimit, fallbackOffset) {
  const items = Array.isArray(payload?.items) ? payload.items : [];
  const limit = Math.max(1, Number(payload?.limit || fallbackLimit || DEFAULT_PAGE_LIMIT));
  const offset = Math.max(0, Number(payload?.offset || fallbackOffset || 0));
  const totalRaw = Number(payload?.total);
  const total = Number.isFinite(totalRaw)
    ? Math.max(0, totalRaw)
    : Math.max(offset + items.length, items.length);
  target.items = items;
  target.limit = limit;
  target.offset = offset;
  target.total = total;
  target.has_prev = Boolean(payload?.has_prev) || offset > 0;
  target.has_next = Boolean(payload?.has_next) || offset + limit < total;
  target.loaded = true;
}

async function loadAuthMe() {
  try {
    const payload = await requestJson("/api/portal/auth/me");
    if (payload.authenticated) {
      state.workspace = payload;
      state.user = payload.user || null;
    } else {
      state.workspace = null;
      state.user = null;
      resetTransactionsState();
      resetLogsState();
    }
  } catch (_) {
    state.workspace = null;
    state.user = null;
    resetTransactionsState();
    resetLogsState();
  }
  renderWorkspace();
}

async function loadWorkspace(showNoticeMessage = false) {
  if (!isAuthenticated()) {
    return;
  }
  state.workspace = await requestJson("/api/portal/user/overview");
  state.user = state.workspace.user || state.user;
  renderWorkspace();
  if (showNoticeMessage) {
    showNotice("appNotice", "Data has been refreshed.", "success");
  }
}

async function loadTransactions(showNoticeMessage = false) {
  if (!isAuthenticated()) {
    resetTransactionsState();
    renderTransactions();
    return;
  }
  const limit = Math.max(1, Number(state.transactions.limit || DEFAULT_PAGE_LIMIT));
  const offset = Math.max(0, Number(state.transactions.offset || 0));
  const payload = await requestJson(`/api/portal/user/transactions?limit=${limit}&offset=${offset}`);
  applyPagedPayload(state.transactions, payload, limit, offset);
  renderTransactions();
  if (showNoticeMessage) {
    showNotice("redeemNotice", "The number change details have been refreshed.", "success");
  }
}

async function loadLogs(showNoticeMessage = false) {
  if (!isAuthenticated()) {
    resetLogsState();
    renderLogs();
    return;
  }
  const status = String(dom.byId("logStatusFilter")?.value || "").trim();
  const projectId = String(dom.byId("logProjectFilter")?.value || "").trim();
  const limit = Math.max(1, Number(state.logs.limit || DEFAULT_PAGE_LIMIT));
  const offset = Math.max(0, Number(state.logs.offset || 0));
  const params = new URLSearchParams({
    limit: String(limit),
    offset: String(offset),
  });
  if (status) {
    params.set("status", status);
  }
  if (projectId) {
    params.set("project_id", projectId);
  }
  const payload = await requestJson(`/api/portal/user/logs?${params.toString()}`);
  applyPagedPayload(state.logs, payload, limit, offset);
  renderLogs();
  if (showNoticeMessage) {
    showNotice("appNotice", "The log has been refreshed.", "success");
  }
}

async function checkRegisterStatus(showSuccess = false) {
  const username = String(dom.byId("registerUsername")?.value || "").trim();
  if (!username) {
    showNotice("registerCheckHint", "After entering the username, it will automatically check whether it has been registered.", "info");
    return null;
  }
  const result = await requestJson(`/api/portal/auth/check?username=${encodeURIComponent(username)}`);
  if (result.registered) {
    showNotice("registerCheckHint", result.message || "This username has been registered, please log in directly.", "warning");
  } else if (showSuccess) {
    showNotice("registerCheckHint", result.message || "This username has not been registered yet, you can continue to create an account.", "success");
  } else {
    showNotice("registerCheckHint", result.message || "This username has not been registered yet.", "info");
  }
  return result;
}

async function handleLogin(event) {
  event.preventDefault();
  const username = String(dom.byId("loginUsername")?.value || "").trim();
  const password = String(dom.byId("loginPassword")?.value || "");
  if (!username || !password) {
    throw new Error("Please enter username and password");
  }
  await requestJson("/api/portal/auth/login", {
    method: "POST",
    body: { username, password },
  });
  await loadAuthMe();
  await loadWorkspace(false);
  resetTransactionsState();
  resetLogsState();
  await loadTransactions(false);
  await loadLogs(false);
  switchPage("dashboard");
  showNotice("appNotice", "Login successful.", "success");
}

async function handleRegister(event) {
  event.preventDefault();
  const username = String(dom.byId("registerUsername")?.value || "").trim();
  const password = String(dom.byId("registerPassword")?.value || "");
  if (!username || !password) {
    throw new Error("Username and password cannot be empty");
  }
  if ((state.summary?.service?.role || state.summary?.meta?.role) === "subnode") {
    throw new Error("The current entrance is not open for user self-registration.");
  }
  const checked = await checkRegisterStatus(false);
  if (checked?.registered) {
    throw new Error(checked.message || "This username has been registered, please log in directly");
  }
  await requestJson("/api/portal/auth/register", {
    method: "POST",
    body: {
      username,
      password,
      register_location: getRegisterLocation(),
    },
  });
  await loadAuthMe();
  await loadWorkspace(false);
  resetTransactionsState();
  resetLogsState();
  await loadTransactions(false);
  await loadLogs(false);
  switchPage("dashboard");
  showNotice("appNotice", `Registration successful, current account ${username} has logged in. `, "success");
}

async function handleLogout() {
  await requestJson("/api/portal/auth/logout", { method: "POST" });
  state.user = null;
  state.workspace = null;
  resetTransactionsState();
  resetLogsState();
  closeApiKeyModal();
  closePortalMenu();
  renderShell();
  renderAuthTab("login");
  showNotice("guestNotice", "You are logged out.", "info");
}

async function handleCheckin() {
  const result = await requestJson("/api/portal/user/checkin", { method: "POST" });
  state.workspace = result;
  state.user = result.user || state.user;
  renderWorkspace();
  showNotice("checkinNotice", result.message || `The check-in was successful and ${result.checkin?.granted_quota || 0} rewards were obtained. `, "success");
  showNotice("appNotice", result.message || "Sign in successfully.", "success");
}

function handleOidcLogin() {
  window.location.href = "/api/portal/auth/oidc/start";
}

function consumeOidcResultParams() {
  const url = new URL(window.location.href);
  const success = url.searchParams.get("oidc");
  const error = url.searchParams.get("oidc_error");
  if (!success && !error) {
    return;
  }

  url.searchParams.delete("oidc");
  url.searchParams.delete("oidc_error");
  window.history.replaceState({}, document.title, `${url.pathname}${url.search}${url.hash}`);

  if (error) {
    showNotice("guestNotice", error, "error");
    return;
  }
  if (success === "success") {
    showNotice(isAuthenticated() ? "appNotice" : "guestNotice", "OIDC login successful.", "success");
  }
}

async function handleCreateApiKey(event) {
  event.preventDefault();
  const name = String(dom.byId("newApiKeyName")?.value || "").trim();
  if (!name) {
    throw new Error("Please fill in the API Key name");
  }
  const result = await requestJson("/api/portal/user/api-keys", {
    method: "POST",
    body: { name },
  });
  dom.byId("newApiKeyName").value = "";
  cacheRevealedApiKey(result.item?.id, result.api_key || "");
  await loadWorkspace(false);
  openApiKeyModal(result.item?.name || name, result.api_key || "");
  showNotice("apiKeyNotice", result.message || "API Key has been created.", "success");
}

async function handleApiKeyAction(action, apiKeyId, enabled) {
  if (action === "toggle") {
    const result = await requestJson(`/api/portal/user/api-keys/${apiKeyId}`, {
      method: "PATCH",
      body: { enabled: !enabled },
    });
    await loadWorkspace(false);
    showNotice("apiKeyNotice", `API Key ${result.item?.enabled ? "Enabled" : "Disabled"}。`, "success");
    return;
  }
  if (action === "delete") {
    await requestJson(`/api/portal/user/api-keys/${apiKeyId}`, { method: "DELETE" });
    await loadWorkspace(false);
    showNotice("apiKeyNotice", `API Key #${apiKeyId} has been soft deleted. `, "warning");
  }
}

async function handleCopyApiKey(apiKeyId, prefix) {
  const rawKey = getCachedApiKey(apiKeyId);
  if (rawKey) {
    await copyText(rawKey);
    showNotice("apiKeyNotice", `API Key #${apiKeyId} has been copied. `, "success");
    return;
  }
  if (prefix) {
    await copyText(prefix);
    showNotice("apiKeyNotice", `Full Key not cached, prefix ${prefix} copied. `, "warning");
    return;
  }
  throw new Error("There is currently no Key content to copy");
}

async function handleRedeem(event) {
  event.preventDefault();
  const code = String(dom.byId("redeemCodeInput")?.value || "").trim();
  if (!code) {
    throw new Error("Please enter the redemption code");
  }
  const result = await requestJson("/api/portal/redeem", {
    method: "POST",
    body: { code },
  });
  dom.byId("redeemCodeInput").value = "";
  await loadWorkspace(false);
  resetTransactionsState();
  await loadTransactions(false);
  showNotice("redeemNotice", result.message || "Redemption successful.", "success");
  showNotice("appNotice", result.message || "Redemption successful.", "success");
}

async function handleCopyTransactionsCurrentPage() {
  const items = state.transactions.items || [];
  if (!items.length) {
    throw new Error("There are no details to copy on the current page");
  }
  const text = items.map((item) => {
    const amount = Number(item.change_amount || 0);
    const formattedAmount = amount > 0 ? `+${amount}` : String(amount);
    return [
      formatDateTime(item.created_at),
      `Change:${formattedAmount}`,
      `Balance:${item.balance_after ?? "--"}`,
      `Source:${item.source_type || "--"}`,
      `Description:${item.note || item.source_ref || "-"}`,
    ].join(" | ");
  }).join("\n");
  await copyText(text);
  showNotice("redeemNotice", "The current page count change details have been copied.", "success");
}

function wireEvents() {
  dom.byId("tabLoginBtn")?.addEventListener("click", () => renderAuthTab("login"));
  dom.byId("tabRegisterBtn")?.addEventListener("click", () => renderAuthTab("register"));
  dom.byId("portalMenuBtn")?.addEventListener("click", () => {
    const isOpen = dom.byId("portalSidebar")?.classList.contains("show");
    setPortalMenuOpen(!isOpen);
  });
  dom.byId("portalMenuBackdrop")?.addEventListener("click", closePortalMenu);
  window.addEventListener("resize", () => {
    if (window.innerWidth > 1080) {
      closePortalMenu();
    }
  });
  window.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      closePortalMenu();
    }
  });

  dom.byId("loginForm")?.addEventListener("submit", async (event) => {
    try {
      await handleLogin(event);
    } catch (error) {
      showNotice("guestNotice", error.message || "Login failed", "error");
    }
  });

  dom.byId("oidcLoginBtn")?.addEventListener("click", () => {
    try {
      handleOidcLogin();
    } catch (error) {
      showNotice("guestNotice", error.message || "OIDC login failed", "error");
    }
  });

  dom.byId("checkinBtn")?.addEventListener("click", async () => {
    try {
      await handleCheckin();
    } catch (error) {
      showNotice("checkinNotice", error.message || "Sign in failed", "error");
    }
  });

  dom.byId("registerUsername")?.addEventListener("blur", async () => {
    try {
      await checkRegisterStatus(false);
    } catch (_) {}
  });

  dom.byId("registerForm")?.addEventListener("submit", async (event) => {
    try {
      await handleRegister(event);
    } catch (error) {
      showNotice("guestNotice", error.message || "Registration failed", "error");
    }
  });

  dom.byId("logoutBtn")?.addEventListener("click", async () => {
    try {
      await handleLogout();
    } catch (error) {
      showNotice("appNotice", error.message || "Exit failed", "error");
    }
  });

  document.querySelectorAll(".nav-btn[data-page]").forEach((button) => {
    button.addEventListener("click", async () => {
      const page = button.dataset.page || "dashboard";
      switchPage(page);
      if (page === "logs") {
        try {
          await loadLogs(false);
        } catch (error) {
          showNotice("appNotice", error.message || "Log loading failed", "error");
        }
      }
      if (page === "redeem") {
        try {
          await loadTransactions(false);
        } catch (error) {
          showNotice("redeemNotice", error.message || "Detail loading failed", "error");
        }
      }
    });
  });

  dom.byId("refreshUserBtn")?.addEventListener("click", async () => {
    try {
      await loadWorkspace(true);
      if (state.page === "redeem") {
        await loadTransactions(false);
      }
      if (state.page === "logs") {
        await loadLogs(false);
      }
    } catch (error) {
      showNotice("appNotice", error.message || "Refresh failed", "error");
    }
  });

  dom.byId("refreshApiKeysBtn")?.addEventListener("click", async () => {
    try {
      await loadWorkspace(true);
      switchPage("apiKeys");
      showNotice("apiKeyNotice", "The API Key list has been refreshed.", "success");
    } catch (error) {
      showNotice("apiKeyNotice", error.message || "Failed to refresh API Key", "error");
    }
  });

  dom.byId("createApiKeyForm")?.addEventListener("submit", async (event) => {
    try {
      await handleCreateApiKey(event);
      switchPage("apiKeys");
    } catch (error) {
      showNotice("apiKeyNotice", error.message || "Failed to apply for API Key", "error");
    }
  });

  dom.byId("redeemForm")?.addEventListener("submit", async (event) => {
    try {
      await handleRedeem(event);
    } catch (error) {
      showNotice("redeemNotice", error.message || "Redemption failed", "error");
    }
  });

  dom.byId("copyTransactionsBtn")?.addEventListener("click", async () => {
    try {
      await handleCopyTransactionsCurrentPage();
    } catch (error) {
      showNotice("redeemNotice", error.message || "Copy failed", "error");
    }
  });

  const refreshLogs = async () => {
    state.logs.offset = 0;
    try {
      await loadLogs(true);
    } catch (error) {
      showNotice("appNotice", error.message || "Log refresh failed", "error");
    }
  };
  dom.byId("refreshLogsBtn")?.addEventListener("click", refreshLogs);
  dom.byId("logStatusFilter")?.addEventListener("change", refreshLogs);
  dom.byId("logProjectFilter")?.addEventListener("keydown", async (event) => {
    if (event.key !== "Enter") {
      return;
    }
    event.preventDefault();
    await refreshLogs();
  });

  dom.byId("transactionsPrevBtn")?.addEventListener("click", async () => {
    if (!state.transactions.has_prev) {
      return;
    }
    state.transactions.offset = Math.max(0, state.transactions.offset - state.transactions.limit);
    try {
      await loadTransactions(false);
    } catch (error) {
      state.transactions.offset += state.transactions.limit;
      showNotice("redeemNotice", error.message || "Previous page failed to load", "error");
    }
  });

  dom.byId("transactionsNextBtn")?.addEventListener("click", async () => {
    if (!state.transactions.has_next) {
      return;
    }
    state.transactions.offset += state.transactions.limit;
    try {
      await loadTransactions(false);
    } catch (error) {
      state.transactions.offset = Math.max(0, state.transactions.offset - state.transactions.limit);
      showNotice("redeemNotice", error.message || "Next page failed to load", "error");
    }
  });

  dom.byId("logsPrevBtn")?.addEventListener("click", async () => {
    if (!state.logs.has_prev) {
      return;
    }
    state.logs.offset = Math.max(0, state.logs.offset - state.logs.limit);
    try {
      await loadLogs(false);
    } catch (error) {
      state.logs.offset += state.logs.limit;
      showNotice("appNotice", error.message || "Previous page failed to load", "error");
    }
  });

  dom.byId("logsNextBtn")?.addEventListener("click", async () => {
    if (!state.logs.has_next) {
      return;
    }
    state.logs.offset += state.logs.limit;
    try {
      await loadLogs(false);
    } catch (error) {
      state.logs.offset = Math.max(0, state.logs.offset - state.logs.limit);
      showNotice("appNotice", error.message || "Next page failed to load", "error");
    }
  });

  dom.byId("apiKeyModalCloseBtn")?.addEventListener("click", closeApiKeyModal);
  dom.byId("apiKeyModal")?.addEventListener("click", (event) => {
    if (event.target === dom.byId("apiKeyModal")) {
      closeApiKeyModal();
    }
  });
  dom.byId("apiKeyModalCopyBtn")?.addEventListener("click", async () => {
    try {
      await copyText(state.apiKeyModal.value || "");
      showNotice("apiKeyNotice", "The complete key has been copied.", "success");
    } catch (error) {
      showNotice("apiKeyNotice", error.message || "Copy failed", "error");
    }
  });
}

async function bootstrap() {
  loadRevealedApiKeys();
  renderShell();
  renderAuthTab("login");
  renderTransactionsPager();
  renderLogsPager();
  wireEvents();
  await loadSummary();
  await loadAuthMe();
  if (isAuthenticated()) {
    await loadWorkspace(false);
    resetTransactionsState();
    resetLogsState();
    await loadTransactions(false);
    await loadLogs(false);
    switchPage("dashboard");
  }
  consumeOidcResultParams();
}

document.addEventListener("click", async (event) => {
  const target = event.target;
  if (!(target instanceof HTMLElement)) {
    return;
  }
  if (target.dataset.action === "toggle-api-key") {
    try {
      await handleApiKeyAction("toggle", Number(target.dataset.id), target.dataset.enabled === "1");
    } catch (error) {
      showNotice("apiKeyNotice", error.message || "Failed to update API Key", "error");
    }
  }
  if (target.dataset.action === "copy-api-key") {
    try {
      await handleCopyApiKey(Number(target.dataset.id), String(target.dataset.prefix || "").trim());
    } catch (error) {
      showNotice("apiKeyNotice", error.message || "Copying API Key failed", "error");
    }
  }
  if (target.dataset.action === "delete-api-key") {
    const apiKeyId = Number(target.dataset.id);
    const ok = window.confirm(`Are you sure to soft delete API Key #${apiKeyId}? The Key will be disabled after soft deletion.`);
    if (!ok) {
      return;
    }
    try {
      await handleApiKeyAction("delete", apiKeyId, false);
    } catch (error) {
      showNotice("apiKeyNotice", error.message || "Soft deletion of API Key failed", "error");
    }
  }
});

window.addEventListener("DOMContentLoaded", bootstrap);
