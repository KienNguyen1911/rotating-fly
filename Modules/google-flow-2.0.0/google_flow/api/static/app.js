const API = "";
const DASHBOARD_HOUR_OPTIONS = [6, 24, 72, 168];
const DASHBOARD_VIEW_OPTIONS = ["overview", "profiles", "activity"];

const state = {
    token: localStorage.getItem("t") || "",
    dashboard: null,
    refreshTimer: null,
    modal: null,
    loading: false,
    pendingRefresh: false,
    operationLock: null,
    selectedHours: normalizeDashboardHours(Number(localStorage.getItem("dashboard-hours") || 24)),
    currentView: normalizeDashboardView(localStorage.getItem("dashboard-view") || "overview"),
    stream: null,
    streamStatus: "idle",
    streamLastEventAt: 0,
    scheduledRealtimeRefresh: null,
    scheduledStreamReconnect: null,
};

const elements = {
    loginRoot: document.getElementById("login-root"),
    appRoot: document.getElementById("app-root"),
    modalRoot: document.getElementById("modal-root"),
    toastRoot: document.getElementById("toast-root"),
};

document.addEventListener("DOMContentLoaded", init);

function normalizeDashboardHours(value) {
    const hours = Number(value);
    return DASHBOARD_HOUR_OPTIONS.includes(hours) ? hours : 24;
}

function normalizeDashboardView(value) {
    return DASHBOARD_VIEW_OPTIONS.includes(String(value)) ? String(value) : "overview";
}

function bucketHoursForRange(hours) {
    if (hours <= 24) {
        return 1;
    }
    if (hours <= 72) {
        return 3;
    }
    return 6;
}

function getHourOptions(options) {
    if (Array.isArray(options) && options.length) {
        return options.map((item) => Number(item)).filter((item) => !Number.isNaN(item));
    }
    return [...DASHBOARD_HOUR_OPTIONS];
}

function getStreamStatusMeta() {
    if (state.pendingRefresh) {
        return {
            label: "There is new data to be applied",
            tone: "warning",
            copy: "Background updates are detected and will automatically refresh after completing the current edit.",
        };
    }

    switch (state.streamStatus) {
        case "live":
            return {
                label: "Connected in real time",
                tone: "success",
                copy: state.streamLastEventAt
                    ? `Recent events: ${formatDate(new Date(state.streamLastEventAt).toISOString())}`
                    : "Receiving server push.",
            };
        case "connecting":
            return {label: "Connecting in real time", tone: "info", copy: "Establishing SSE connection."};
        case "reconnecting":
            return {label: "Reconnecting in real time", tone: "warning", copy: "The connection was interrupted and is reconnecting automatically."};
        case "offline":
            return {label: "Polling", tone: "danger", copy: "SSE is not currently established, and the system will refresh periodically."};
        default:
            return {label: "Waiting for connection", tone: "info", copy: "A real-time connection will be automatically established after logging in."};
    }
}

function updateStreamBadge() {
    const meta = getStreamStatusMeta();
    const pill = document.getElementById("stream-status-pill");
    const copy = document.getElementById("stream-status-copy");
    if (pill) {
        pill.className = `tag ${meta.tone}`;
        pill.textContent = meta.label;
    }
    if (copy) {
        copy.textContent = meta.copy;
    }
}

function getExecutionState() {
    if (state.operationLock?.busy) {
        return state.operationLock;
    }
    const execution = state.dashboard?.execution;
    if (execution?.busy) {
        return execution.current ? {busy: true, ...execution.current} : execution;
    }
    return {busy: false};
}

function getExecutionMessage(execution = getExecutionState()) {
    if (!execution?.busy) {
        return "Browser-related operations will be automatically executed serially, and the relevant buttons will be temporarily locked during execution.";
    }
    const label = execution.label || "Task";
    const profileName = String(execution.profile_name || "").trim();
    return profileName ? `Executing ${label}:${profileName}, other browser related operations have been temporarily locked.` : `${label} is being executed, and other browser-related operations have been temporarily locked.`;
}

function getBrowserLockAttrs() {
    const execution = getExecutionState();
    const attrs = ['data-browser-lock="true"'];
    if (execution.busy) {
        attrs.push("disabled");
        attrs.push(`title="${escapeAttr(getExecutionMessage(execution))}"`);
    }
    return attrs.join(" ");
}

function syncExecutionButtons() {
    const execution = getExecutionState();
    const message = getExecutionMessage(execution);
    document.querySelectorAll("[data-browser-lock='true']").forEach((button) => {
        const pending = button.dataset.pending === "true";
        button.disabled = execution.busy || pending;
        if (execution.busy) {
            button.title = message;
        } else {
            button.removeAttribute("title");
        }
    });
    const notice = document.getElementById("execution-lock-copy");
    if (notice) {
        notice.textContent = message;
    }
}

function setOperationLock(lockState = null) {
    state.operationLock = lockState ? {busy: true, ...lockState} : null;
    syncExecutionButtons();
}

function isInteractionLocked() {
    if (state.modal) {
        return true;
    }
    const active = document.activeElement;
    if (!active) {
        return false;
    }
    const tag = String(active.tagName || "").toUpperCase();
    return elements.appRoot.contains(active) && ["INPUT", "TEXTAREA", "SELECT"].includes(tag);
}

async function init() {
    elements.toastRoot.className = "toast-stack";

    document.addEventListener("focusout", () => {
        window.setTimeout(() => {
            if (state.pendingRefresh && !isInteractionLocked() && !state.loading && state.token) {
                refreshDashboard(true, true).catch(() => {});
            } else {
                updateStreamBadge();
            }
        }, 0);
    });

    try {
        const auth = await publicJson(`${API}/api/auth/check`);
        if (!auth.need_password) {
            showAppShell();
            await refreshDashboard(false, true);
            connectDashboardStream();
        } else if (state.token && await verifySession()) {
            showAppShell();
            await refreshDashboard(false, true);
            connectDashboardStream();
        } else {
            showLogin();
        }
    } catch (error) {
        showLogin();
        toast(error.message || "Initialization failed", "error");
    }

    state.refreshTimer = window.setInterval(async () => {
        if (state.loading || elements.appRoot.classList.contains("hidden")) {
            return;
        }
        const streamHealthy = state.dashboard?.realtime?.sse_supported
            && state.streamStatus === "live"
            && Date.now() - state.streamLastEventAt < 45000;
        if (streamHealthy) {
            return;
        }
        if (isInteractionLocked()) {
            state.pendingRefresh = true;
            updateStreamBadge();
            return;
        }
        try {
            await refreshDashboard(true);
        } catch (_) {
            // Fails silently when polling for the bottom line.
        }
    }, 45000);

    window.addEventListener("beforeunload", () => disconnectDashboardStream(false));
}

function showLogin() {
    disconnectDashboardStream();
    closeModal(true);
    state.dashboard = null;
    state.pendingRefresh = false;
    elements.appRoot.className = "hidden";
    elements.appRoot.innerHTML = "";
    elements.loginRoot.className = "screen-center login-shell";
    elements.loginRoot.innerHTML = `
        <div class="login-card">
            <div class="login-icon">${renderIcon("lock")}</div>
            <span class="login-badge">Flow2API Console</span>
            <h1 class="login-title">Flow2API Console</h1>
            <p class="login-subtitle">Please enter your administrator password to continue. </p>
            <form class="login-form" onsubmit="event.preventDefault(); doLogin(this.querySelector('button'))">
                <div class="field">
                    <label for="login-password">Administrator password</label>
                    <input id="login-password" type="password" placeholder="Please enter the administrator password">
                </div>
                <button class="btn primary login-submit" type="submit">
                    Login
                    ${renderIcon("arrow-right")}
                </button>
            </form>
        </div>
    `;
    document.getElementById("login-password")?.focus();
}

function showAppShell() {
    elements.loginRoot.className = "hidden";
    elements.loginRoot.innerHTML = "";
    elements.appRoot.className = "page-shell";
}

async function verifySession() {
    try {
        const response = await request(`${API}/api/status`, {}, {allowError: true});
        return response.ok;
    } catch (_) {
        return false;
    }
}

function setStreamStatus(status) {
    state.streamStatus = status;
    updateStreamBadge();
}

function disconnectDashboardStream(resetStatus = true) {
    if (state.stream) {
        state.stream.close();
        state.stream = null;
    }
    if (state.scheduledRealtimeRefresh) {
        window.clearTimeout(state.scheduledRealtimeRefresh);
        state.scheduledRealtimeRefresh = null;
    }
    if (state.scheduledStreamReconnect) {
        window.clearTimeout(state.scheduledStreamReconnect);
        state.scheduledStreamReconnect = null;
    }
    if (resetStatus) {
        setStreamStatus("idle");
    }
}

function connectDashboardStream() {
    if (!state.dashboard?.realtime?.sse_supported) {
        setStreamStatus("offline");
        return;
    }

    disconnectDashboardStream(false);
    setStreamStatus("connecting");

    const sessionToken = state.token || "";
    const streamUrl = `${API}/api/dashboard/stream?session_token=${encodeURIComponent(sessionToken)}`;
    const stream = new EventSource(streamUrl);
    state.stream = stream;

    const touch = () => {
        state.streamLastEventAt = Date.now();
    };

    stream.addEventListener("ready", () => {
        touch();
        setStreamStatus("live");
    });

    stream.addEventListener("heartbeat", () => {
        touch();
        if (state.streamStatus !== "live") {
            setStreamStatus("live");
        }
    });

    stream.addEventListener("dashboard", () => {
        touch();
        setStreamStatus("live");
        scheduleRealtimeRefresh();
    });

    stream.onerror = () => {
        if (state.stream !== stream) {
            return;
        }
        stream.close();
        state.stream = null;
        setStreamStatus("reconnecting");
        if (state.scheduledStreamReconnect) {
            window.clearTimeout(state.scheduledStreamReconnect);
        }
        const delayMs = Math.min(20000, 1500 * (2 ** Math.min(4, (state.streamLastEventAt ? 1 : 0) + 1)));
        state.scheduledStreamReconnect = window.setTimeout(() => {
            state.scheduledStreamReconnect = null;
            connectDashboardStream();
        }, delayMs);
    };
}

function scheduleRealtimeRefresh() {
    if (state.scheduledRealtimeRefresh) {
        return;
    }
    state.scheduledRealtimeRefresh = window.setTimeout(async () => {
        state.scheduledRealtimeRefresh = null;
        if (isInteractionLocked()) {
            state.pendingRefresh = true;
            updateStreamBadge();
            return;
        }
        try {
            await refreshDashboard(true);
        } catch (_) {
            // When real-time refresh fails, it is handled by reconnection and polling.
        }
    }, 320);
}

async function refreshDashboard(silent = false, force = false) {
    if (!force && isInteractionLocked() && state.dashboard) {
        state.pendingRefresh = true;
        updateStreamBadge();
        return state.dashboard;
    }

    state.loading = true;
    try {
        state.dashboard = await fetchDashboard();
        const selectedHours = normalizeDashboardHours(state.dashboard?.filters?.hours || state.selectedHours);
        state.selectedHours = selectedHours;
        localStorage.setItem("dashboard-hours", String(selectedHours));
        state.pendingRefresh = false;
        renderApp();
        syncExecutionButtons();
        updateStreamBadge();
        return state.dashboard;
    } catch (error) {
        if (!silent && error.message !== "expired") {
            toast(error.message || "Loading failed", "error");
        }
        throw error;
    } finally {
        state.loading = false;
    }
}

async function fetchDashboard() {
    const dashboardResponse = await request(`${API}/api/dashboard?hours=${state.selectedHours}`, {}, {allowError: true});
    if (dashboardResponse.ok) {
        return await safeJson(dashboardResponse);
    }
    if (dashboardResponse.status !== 404) {
        throw new Error(await parseError(dashboardResponse));
    }

    const [status, config, profiles] = await Promise.all([
        json(`${API}/api/status`),
        json(`${API}/api/config`),
        json(`${API}/api/profiles`),
    ]);
    return buildFallbackDashboard(status, config, profiles, state.selectedHours);
}

function buildFallbackDashboard(status, config, profiles, selectedHours) {
    const recentActivity = [...profiles]
        .filter((profile) => profile.last_check_time || profile.last_sync_time)
        .sort((left, right) => new Date(left.last_check_time || left.last_sync_time) - new Date(right.last_check_time || right.last_sync_time))
        .slice(-18)
        .map((profile) => {
            const lastResult = profile.last_check_result || profile.last_sync_result || "";
            return {
                profile_name: profile.name,
                message: lastResult || "No sync records yet",
                status: getResultStatus(lastResult),
                target_url: profile.effective_flow2api_url || config.flow2api_url,
                target_label: profile.target_label || profile.effective_flow2api_url || config.flow2api_url || "Not configured",
                created_at: profile.last_check_time || profile.last_sync_time,
            };
        });

    const summary = {
        total: profiles.length,
        logged_in: profiles.filter((profile) => profile.is_logged_in).length,
        active: profiles.filter((profile) => profile.is_active).length,
        custom_targets: profiles.filter((profile) => profile.flow2api_url).length,
        token_overrides: profiles.filter((profile) => profile.has_connection_token_override).length,
        proxy_enabled: profiles.filter((profile) => profile.proxy_url).length,
        window_success: recentActivity.filter((item) => item.status === "success").length,
        window_error: recentActivity.filter((item) => item.status === "error").length,
    };

    return {
        browser: status.browser,
        syncer: status.syncer,
        config,
        profiles,
        summary,
        charts: {
            sync_activity: buildSyntheticActivity(profiles, selectedHours),
            top_profiles: [...profiles]
                .sort((left, right) => (right.sync_count + right.error_count) - (left.sync_count + left.error_count))
                .slice(0, 6),
            status_breakdown: {
                active: summary.active,
                inactive: summary.total - summary.active,
                logged_in: summary.logged_in,
                not_logged_in: summary.total - summary.logged_in,
            },
            failure_reasons: buildFallbackFailureReasons(recentActivity),
            target_distribution: buildFallbackTargetDistribution(profiles, recentActivity),
        },
        recent_activity: recentActivity,
        filters: {
            hours: selectedHours,
            hour_options: getHourOptions(config.available_chart_ranges),
        },
        realtime: {
            sse_supported: false,
        },
        version: status.version || "fallback",
    };
}

function buildSyntheticActivity(profiles, hours) {
    const bucketHours = bucketHoursForRange(hours);
    const bucketCount = Math.max(1, Math.floor(hours / bucketHours));
    const now = new Date();
    now.setMinutes(0, 0, 0);
    now.setHours(now.getHours() - (now.getHours() % bucketHours));

    const buckets = [];
    const bucketMap = new Map();
    for (let index = bucketCount - 1; index >= 0; index -= 1) {
        const bucketTime = new Date(now);
        bucketTime.setHours(bucketTime.getHours() - (index * bucketHours));
        const key = bucketTime.toISOString().slice(0, 13);
        const label = hours <= 24
            ? bucketTime.toLocaleTimeString([], {hour: "2-digit", minute: "2-digit"})
            : bucketTime.toLocaleString("zh-CN", {month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit"});
        const bucket = {bucket: key, label, success: 0, error: 0};
        buckets.push(bucket);
        bucketMap.set(key, bucket);
    }

    profiles.forEach((profile) => {
        if (!profile.last_sync_time) {
            return;
        }
        const syncTime = new Date(profile.last_sync_time);
        syncTime.setMinutes(0, 0, 0);
        syncTime.setHours(syncTime.getHours() - (syncTime.getHours() % bucketHours));
        const bucket = bucketMap.get(syncTime.toISOString().slice(0, 13));
        if (!bucket) {
            return;
        }
        const resultStatus = getResultStatus(profile.last_check_result || profile.last_sync_result || "");
        if (resultStatus === "success") {
            bucket.success += 1;
        } else if (resultStatus === "error") {
            bucket.error += 1;
        }
    });

    return {bucket_hours: bucketHours, points: buckets};
}

function buildFallbackFailureReasons(events) {
    const counts = new Map();
    events.forEach((event) => {
        if (event.status !== "error") {
            return;
        }
        const label = String(event.message || "unknown error").slice(0, 28);
        counts.set(label, (counts.get(label) || 0) + 1);
    });
    return [...counts.entries()]
        .sort((left, right) => right[1] - left[1])
        .slice(0, 6)
        .map(([label, count]) => ({label, count, sample: label}));
}

function buildFallbackTargetDistribution(profiles, recentActivity) {
    const grouped = new Map();
    profiles.forEach((profile) => {
        const targetUrl = profile.effective_flow2api_url || "";
        const targetLabel = profile.target_label || targetUrl || "Not configured";
        const entry = grouped.get(targetLabel) || {
            target_url: targetUrl,
            target_label: targetLabel,
            profile_count: 0,
            logged_in: 0,
            custom_count: 0,
            success: 0,
            error: 0,
        };
        entry.profile_count += 1;
        entry.logged_in += profile.is_logged_in ? 1 : 0;
        entry.custom_count += profile.flow2api_url ? 1 : 0;
        grouped.set(targetLabel, entry);
    });
    recentActivity.forEach((event) => {
        const targetLabel = event.target_label || event.target_url || "Not configured";
        const entry = grouped.get(targetLabel) || {
            target_url: event.target_url || "",
            target_label: targetLabel,
            profile_count: 0,
            logged_in: 0,
            custom_count: 0,
            success: 0,
            error: 0,
        };
        if (event.status === "success") {
            entry.success += 1;
        } else if (event.status === "error") {
            entry.error += 1;
        }
        grouped.set(targetLabel, entry);
    });
    return [...grouped.values()].sort((left, right) => (right.profile_count + right.success + right.error) - (left.profile_count + left.success + left.error));
}

function getResultStatus(resultText) {
    const text = String(resultText || "").toLowerCase();
    if (text.startsWith("success")) {
        return "success";
    }
    if (text.startsWith("skipped")) {
        return "skipped";
    }
    if (text) {
        return "error";
    }
    return "info";
}

function getStatusTone(status) {
    if (status === "success") {
        return "success";
    }
    if (status === "skipped") {
        return "warning";
    }
    if (status === "error") {
        return "danger";
    }
    return "info";
}

function getStatusLabel(status) {
    if (status === "success") {
        return "success";
    }
    if (status === "skipped") {
        return "jump over";
    }
    if (status === "error") {
        return "fail";
    }
    return "state";
}

const ICONS = {
    lock: '<svg viewBox="0 0 24 24"><rect x="5" y="11" width="14" height="10" rx="2"></rect><path d="M8 11V8a4 4 0 1 1 8 0v3"></path></svg>',
    "arrow-right": '<svg viewBox="0 0 24 24"><path d="M5 12h14"></path><path d="m13 6 6 6-6 6"></path></svg>',
    activity: '<svg viewBox="0 0 24 24"><path d="M3 12h4l3-8 4 16 3-8h4"></path></svg>',
    monitor: '<svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="12" rx="2"></rect><path d="M8 20h8"></path><path d="M12 16v4"></path></svg>',
    refresh: '<svg viewBox="0 0 24 24"><path d="M21 12a9 9 0 1 1-3.4-7"></path><path d="M21 3v6h-6"></path></svg>',
    logout: '<svg viewBox="0 0 24 24"><path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"></path><path d="M10 17 15 12 10 7"></path><path d="M15 12H3"></path></svg>',
    info: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"></circle><path d="M12 10v6"></path><path d="M12 7h.01"></path></svg>',
    users: '<svg viewBox="0 0 24 24"><path d="M16 21v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2"></path><circle cx="9.5" cy="7" r="3.5"></circle><path d="M20 21v-2a4 4 0 0 0-3-3.87"></path><path d="M16 3.13a3.5 3.5 0 0 1 0 6.74"></path></svg>',
    "check-circle": '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"></circle><path d="m8.5 12.5 2.3 2.3 4.7-5.3"></path></svg>',
    target: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="7"></circle><circle cx="12" cy="12" r="3"></circle><path d="M12 2v3"></path><path d="M12 19v3"></path><path d="M2 12h3"></path><path d="M19 12h3"></path></svg>',
    "trending-up": '<svg viewBox="0 0 24 24"><path d="M3 17 9 11 13 15 21 7"></path><path d="M14 7h7v7"></path></svg>',
    "trending-down": '<svg viewBox="0 0 24 24"><path d="m3 7 6 6 4-4 8 8"></path><path d="M14 17h7v-7"></path></svg>',
    server: '<svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="6" rx="2"></rect><rect x="3" y="14" width="18" height="6" rx="2"></rect><path d="M7 7h.01"></path><path d="M7 17h.01"></path></svg>',
    save: '<svg viewBox="0 0 24 24"><path d="M5 21h14V7l-4-4H5z"></path><path d="M9 21v-6h6v6"></path><path d="M9 3v4h4"></path></svg>',
    upload: '<svg viewBox="0 0 24 24"><path d="M12 16V4"></path><path d="m7 9 5-5 5 5"></path><path d="M4 20h16"></path></svg>',
    plus: '<svg viewBox="0 0 24 24"><path d="M12 5v14"></path><path d="M5 12h14"></path></svg>',
    play: '<svg viewBox="0 0 24 24"><path d="m8 5 11 7-11 7z"></path></svg>',
    square: '<svg viewBox="0 0 24 24"><rect x="6" y="6" width="12" height="12" rx="2"></rect></svg>',
    shield: '<svg viewBox="0 0 24 24"><path d="M12 3 6 6v5c0 4.5 2.9 8.6 6 10 3.1-1.4 6-5.5 6-10V6z"></path><path d="m9.5 12 1.7 1.7 3.3-3.7"></path></svg>',
    download: '<svg viewBox="0 0 24 24"><path d="M12 4v10"></path><path d="m7 10 5 5 5-5"></path><path d="M4 20h16"></path></svg>',
    cookie: '<svg viewBox="0 0 24 24"><path d="M20 13.5A6.5 6.5 0 1 1 10.5 4a3.5 3.5 0 0 0 4.5 4.5 3.5 3.5 0 0 0 4.5 5Z"></path><path d="M8.5 10h.01"></path><path d="M12 14h.01"></path><path d="M15.5 11h.01"></path></svg>',
    key: '<svg viewBox="0 0 24 24"><circle cx="7.5" cy="15.5" r="3.5"></circle><path d="M11 15.5h10"></path><path d="M18 12.5v6"></path><path d="M14.5 12.5v6"></path></svg>',
    edit: '<svg viewBox="0 0 24 24"><path d="M12 20h9"></path><path d="m16.5 3.5 4 4L8 20l-5 1 1-5z"></path></svg>',
    trash: '<svg viewBox="0 0 24 24"><path d="M3 6h18"></path><path d="M8 6V4h8v2"></path><path d="m19 6-1 14H6L5 6"></path><path d="M10 11v6"></path><path d="M14 11v6"></path></svg>',
    globe: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"></circle><path d="M3 12h18"></path><path d="M12 3a15 15 0 0 1 0 18"></path><path d="M12 3a15 15 0 0 0 0 18"></path></svg>',
    clock: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"></circle><path d="M12 7v5l3 2"></path></svg>',
    "x-circle": '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"></circle><path d="m9 9 6 6"></path><path d="m15 9-6 6"></path></svg>',
    x: '<svg viewBox="0 0 24 24"><path d="m6 6 12 12"></path><path d="m18 6-12 12"></path></svg>',
};

function renderIcon(name, className = "ui-icon") {
    return `<span class="${className}" aria-hidden="true">${ICONS[name] || ""}</span>`;
}

function setDashboardView(view) {
    const nextView = normalizeDashboardView(view);
    if (nextView === state.currentView) {
        return;
    }
    state.currentView = nextView;
    localStorage.setItem("dashboard-view", nextView);
    if (!state.dashboard) {
        return;
    }
    renderApp();
    syncExecutionButtons();
    updateStreamBadge();
}

function renderViewNavigation(currentView, profiles, recentActivity) {
    const items = [
        {key: "overview", label: "Data panel", count: null},
        {key: "profiles", label: "Account list", count: profiles.length},
        {key: "activity", label: "Recent news", count: recentActivity.length},
    ];

    return `
        <nav class="view-nav" aria-label="Page navigation">
            ${items.map((item) => `
                <button
                    class="view-nav-btn ${item.key === currentView ? "active" : ""}"
                    onclick="setDashboardView('${item.key}')"
                    type="button"
                >
                    <span>${escapeHtml(item.label)}</span>
                    ${item.count === null ? "" : `<span class="view-nav-count">${escapeHtml(String(item.count))}</span>`}
                </button>
            `).join("")}
        </nav>
    `;
}

function renderOverviewPanel(summary, config, targetDistribution, selectedHours) {
    return `
        <section class="stats-grid">
            ${renderMetricCard("Total number of accounts", summary.total || 0, `${summary.active || 0} active`, "primary", "users")}
            ${renderMetricCard("Logged in", summary.logged_in || 0, `Not logged in ${(summary.total || 0) - (summary.logged_in || 0)}`, "success", "check-circle")}
            ${renderMetricCard("Custom targets", summary.custom_targets || 0, `Token overrides ${summary.token_overrides || 0}`, "info", "target")}
            ${renderMetricCard("Window success", summary.window_success || 0, `Last ${selectedHours} hours`, "success", "trending-up")}
            ${renderMetricCard("Window failed", summary.window_error || 0, `Last ${selectedHours} hours`, "danger", "trending-down")}
            ${renderMetricCard("target instance", targetDistribution.length || summary.target_count || summary.target_instances || 0, `Proxy enabled ${summary.proxy_enabled || 0}`, "warning", "server")}
        </section>

        <section class="section-card">
            <div class="card-head">
                <div>
                    <h2 class="card-title title-with-icon">${renderIcon("target")} default target configuration</h2>
                </div>
            </div>
            <div class="config-grid">
                <div class="field span-2">
                    <label for="config-url">Default Flow2API address</label>
                    <input id="config-url" value="${escapeAttr(config.flow2api_url || "")}" placeholder="http://host.docker.internal:8000">
                </div>
                <div class="field">
                    <label for="config-token">Default connection token</label>
                    <input id="config-token" type="password" placeholder="${escapeAttr(config.connection_token_preview || "Leave blank and do not modify")}">
                    <span class="field-hint">Leave blank to keep the current default token unchanged. </span>
                </div>
                <div class="field">
                    <label for="config-interval">Refresh interval (minutes)</label>
                    <div class="inline-field-row">
                        <input id="config-interval" type="number" min="1" max="1440" value="${escapeAttr(String(config.refresh_interval || 60))}">
                        <button class="btn primary" onclick="saveConfig(this)">${renderIcon("save")} Save</button>
                    </div>
                </div>
            </div>
        </section>
    `;
}

function renderProfilesPanel(profiles) {
    return `
        <section class="section-card">
            <div class="card-head">
                <div>
                    <h2 class="card-title title-with-icon">${renderIcon("users")} account list</h2>
                </div>
                <div class="button-row">
                    <button class="btn secondary" ${getBrowserLockAttrs()} onclick="syncAll(this)">${renderIcon("refresh")} Sync all</button>
                    <button class="btn outline" onclick="openCredentialImportModal()">${renderIcon("upload")} Import account</button>
                    <button class="btn primary" onclick="openProfileModal()">${renderIcon("plus")} New account</button>
                </div>
            </div>
            <div class="profiles-grid">
                ${profiles.length ? profiles.map(renderProfileCard).join("") : `
                    <div class="empty-state">
                        No account yet. Create an account first, or click "Import Account" above to import in batches, and then complete the login through automatic login, remote login or importing session data.
                    </div>`}
            </div>
        </section>
    `;
}

function renderActivityPanel(recentActivity) {
    return `
        <section class="activity-card">
            <div class="card-head">
                <div>
                    <h2 class="card-title title-with-icon">${renderIcon("activity")} Recent updates</h2>
                </div>
                <span class="tag info">Last ${Math.min(recentActivity.length, 18)} items</span>
            </div>
            ${renderRecentActivity(recentActivity)}
        </section>
    `;
}

function renderApp() {
    const dashboard = state.dashboard;
    const summary = dashboard.summary || {};
    const config = dashboard.config || {};
    const charts = dashboard.charts || {};
    const browser = dashboard.browser || {};
    const profiles = dashboard.profiles || [];
    const filters = dashboard.filters || {hours: state.selectedHours, hour_options: [...DASHBOARD_HOUR_OPTIONS]};
    const targetDistribution = charts.target_distribution || [];
    const streamMeta = getStreamStatusMeta();

    const vncRunning = Boolean(browser.vnc_stack_running);
    const vncEnabled = Boolean(config.enable_vnc);
    const selectedHours = normalizeDashboardHours(filters.hours || state.selectedHours);
    const currentView = normalizeDashboardView(state.currentView);
    const execution = getExecutionState();
    const vncCopy = vncEnabled
        ? `Accounts with configured credentials can directly click "Automatic Login"; when manual takeover is required, click "Login" and complete the Google login in the remote window. Current ${vncRunning ? "Remote login is available" : "Remote login will be pulled up on demand after clicking login"}.`
        : "Remote login is currently disabled. Accounts with configured credentials can still try to log in automatically in the background.";
    const recentActivity = dashboard.recent_activity || [];

    state.currentView = currentView;

    elements.appRoot.innerHTML = `
        <header class="topbar">
            <div class="topbar-inner">
                <div class="topbar-brand">
                    <div class="title-with-icon">
                        <span class="metric-icon primary">${renderIcon("activity")}</span>
                        <div>
                            <h1 class="hero-title">Console <span class="version-tag">· v${escapeHtml(dashboard.version || "-")}</span></h1>
                            <p class="hero-subtitle">Account management, synchronization status and target configuration. </p>
                        </div>
                    </div>
                </div>
                <div class="toolbar">
                    <span id="stream-status-pill" class="tag ${streamMeta.tone}">${escapeHtml(streamMeta.label)}</span>
                    ${vncEnabled? `<button class="btn outline" onclick="openVnc()" ${vncRunning ? "" : "disabled"}>${renderIcon("monitor")} ${vncRunning ? "Remote login" : "Remote not started"}</button>` : ""}
                    <button class="btn ghost icon-only" onclick="refreshDashboardAction(this)" title="Refresh">${renderIcon("refresh")}</button>
                    <button class="btn ghost icon-only danger-text" onclick="doLogout(this)" title="Exit">${renderIcon("logout")}</button>
                </div>
            </div>
        </header>

        <main class="page-main">
            ${renderViewNavigation(currentView, profiles, recentActivity)}

            <section class="notice ${execution.busy ? "is-busy" : ""}">
                <div class="notice-icon">${renderIcon(execution.busy ? "refresh" : "info")}</div>
                <div>
                    <div class="notice-title">${execution.busy ? "Task execution" : "Run instructions"}</div>
                    <div class="notice-body-text">${escapeHtml(vncCopy)}</div>
                    <div id="stream-status-copy" class="notice-inline">${escapeHtml(streamMeta.copy)}</div>
                    <div id="execution-lock-copy" class="notice-inline">${escapeHtml(getExecutionMessage(execution))}</div>
                </div>
            </section>

            ${currentView === "overview" ? renderOverviewPanel(summary, config, targetDistribution, selectedHours) : ""}
            ${currentView === "profiles" ? renderProfilesPanel(profiles) : ""}
            ${currentView === "activity" ? renderActivityPanel(recentActivity) : ""}
        </main>
    `;
}

function renderHourFilterButtons(options, selectedHours) {
    return `
        <div class="button-row wrap-row">
            ${options.map((hours) => `
                <button class="btn ${hours === selectedHours ? "primary" : "ghost"} small" onclick="setChartRange(${hours}, this)">${hours >= 168 ? "7 days" : `${hours} hours`}</button>
            `).join("")}
        </div>
    `;
}

function renderMetricCard(label, value, foot, tone = "", iconName = "") {
    return `
        <article class="metric-card ${escapeHtml(tone || "default")}">
            <div class="metric-head">
                <div class="metric-label">${escapeHtml(label)}</div>
                ${iconName ? `<span class="metric-icon ${escapeHtml(tone || "default")}">${renderIcon(iconName)}</span>` : ""}
            </div>
            <div class="metric-value ${escapeHtml(tone)}">${escapeHtml(String(value))}</div>
            <div class="metric-foot">${escapeHtml(foot || "-")}</div>
        </article>
    `;
}

function renderActivityChart(chart, selectedHours) {
    const data = chart?.points?.length ? chart.points : buildSyntheticActivity([], selectedHours).points;
    const bucketHours = Number(chart?.bucket_hours || bucketHoursForRange(selectedHours));
    const maxValue = Math.max(1, ...data.map((point) => Number(point.success || 0) + Number(point.error || 0)));
    const labelStep = data.length > 36 ? 6 : data.length > 24 ? 4 : data.length > 12 ? 2 : 1;

    return `
        <div class="chart-wrap">
            <div class="button-row wrap-row compact-row">
                <span class="tag success">Success</span>
                <span class="tag danger">Failed</span>
                <span class="tag info">Granularity ${bucketHours} hours</span>
            </div>
            <div class="activity-bars" style="grid-template-columns: repeat(${Math.max(1, data.length)}, minmax(0, 1fr));">
                ${data.map((point, index) => {
                    const total = Number(point.success || 0) + Number(point.error || 0);
                    const successHeight = Number(point.success || 0) ? Math.max(4, Math.round((Number(point.success || 0) / maxValue) * 180)) : 0;
                    const errorHeight = Number(point.error || 0) ? Math.max(4, Math.round((Number(point.error || 0) / maxValue) * 180)) : 0;
                    const label = index % labelStep === 0 ? point.label : "";
                    return `
                        <div class="activity-col" title="${escapeAttr(`${point.label} · Success ${point.success} / Failure ${point.error}`)}">
                            <div class="activity-stack">
                                ${Number(point.error || 0) ? `<div class="activity-bar error" style="height:${errorHeight}px"></div>` : ""}
                                ${Number(point.success || 0) ? `<div class="activity-bar success" style="height:${successHeight}px"></div>` : total === 0 ? `<div class="activity-bar ghost-bar"></div>` : ""}
                            </div>
                            <span class="axis-label">${escapeHtml(label)}</span>
                        </div>`;
                }).join("")}
            </div>
        </div>
    `;
}

function renderStatusAndRanking(breakdown, topProfiles) {
    const loggedIn = Number(breakdown.logged_in || 0);
    const notLoggedIn = Number(breakdown.not_logged_in || 0);
    const active = Number(breakdown.active || 0);
    const inactive = Number(breakdown.inactive || 0);
    const total = Math.max(1, loggedIn + notLoggedIn);
    const ratio = Math.round((loggedIn / total) * 100);
    const donutStyle = `background: conic-gradient(var(--success) 0 ${ratio}%, rgba(148, 163, 184, 0.14) ${ratio}% 100%)`;
    const maxProfileTotal = Math.max(1, ...topProfiles.map((profile) => (profile.sync_count || 0) + (profile.error_count || 0)));

    const statusItems = [
        { label: "Logged in", value: loggedIn, tone: "success" },
        { label: "Not logged in", value: notLoggedIn, tone: "warning" },
        { label: "Activating", value: active, tone: "primary" },
        { label: "Deactivated", value: inactive, tone: "danger" },
    ];

    return `
        <div class="status-ranking-shell">
            <div class="status-panel">
                <div class="donut-wrap compact-donut-wrap">
                    <div style="position:relative;">
                        <div class="donut compact-donut" style="${donutStyle}"></div>
                        <div class="donut-center">
                            <div class="donut-value">${ratio}%</div>
                            <div class="muted">Login rate</div>
                        </div>
                    </div>
                </div>
                <div class="status-summary-list">
                    ${statusItems.map((item) => `
                        <div class="status-row ${item.tone}">
                            <span class="status-row-label">${item.label}</span>
                            <strong class="status-row-value">${item.value}</strong>
                        </div>
                    `).join("")}
                </div>
            </div>
            <div class="ranking-list">
                ${(topProfiles.length ? topProfiles : []).map((profile, index) => {
                    const totalOps = (profile.sync_count || 0) + (profile.error_count || 0);
                    const percent = Math.max(8, Math.round((totalOps / maxProfileTotal) * 100));
                    return `
                        <div class="ranking-row">
                            <span class="ranking-index">#${index + 1}</span>
                            <div class="ranking-body">
                                <div class="split-line">
                                    <strong>${escapeHtml(profile.name || "Unnamed")}</strong>
                                    <span class="mini-tag ${profile.is_logged_in ? "success" : "warning"}">${profile.is_logged_in ? "Logged in" : "Waiting to log in"}</span>
                                </div>
                                <div class="progress-line ranking-progress">
                                    <div class="progress-fill" style="width:${percent}%"></div>
                                </div>
                                <div class="split-line muted ranking-meta">
                                    <span>Total ${totalOps}</span>
                                    <span>Success ${profile.sync_count || 0} · Failure ${profile.error_count || 0}</span>
                                </div>
                            </div>
                        </div>`;
                }).join("") || `<div class="empty-state">No ranking data</div>`}
            </div>
        </div>
    `;
}

function renderLegend(label, value, tone) {
    return `
        <div class="legend-item">
            <span class="tag ${tone}">${escapeHtml(label)}</span>
            <strong>${escapeHtml(String(value || 0))}</strong>
        </div>
    `;
}

function renderFailureReasons(items) {
    if (!items.length) {
        return `<div class="empty-state">There are no failure records within the current time window. </div>`;
    }

    const maxCount = Math.max(1, ...items.map((item) => Number(item.count || 0)));
    return `
        <div class="data-list">
            ${items.map((item) => {
                const width = Math.max(10, Math.round((Number(item.count || 0) / maxCount) * 100));
                return `
                    <div class="data-row">
                        <div class="split-line">
                            <strong>${escapeHtml(item.label || item.reason || "unknown reason")}</strong>
                            <span class="mini-tag danger">${escapeHtml(String(item.count || 0))}</span>
                        </div>
                        <div class="progress-line subtle-progress">
                            <div class="progress-fill danger-fill" style="width:${width}%"></div>
                        </div>
                        <div class="profile-meta">${escapeHtml(item.sample || item.label || item.reason || "")}</div>
                    </div>
                `;
            }).join("")}
        </div>
    `;
}

function renderTargetDistribution(items) {
    if (!items.length) {
        return `<div class="empty-state">There is no target instance distribution data yet. </div>`;
    }

    const maxProfiles = Math.max(1, ...items.map((item) => Number(item.profile_count || item.total || 0)));
    return `
        <div class="data-list">
            ${items.map((item) => {
                const totalProfiles = Number(item.profile_count || item.total || 0);
                const width = Math.max(10, Math.round((totalProfiles / maxProfiles) * 100));
                return `
                    <div class="data-row">
                        <div class="split-line">
                            <strong>${escapeHtml(item.target_label || item.label || item.target_url || "Not configured")}</strong>
                            <span class="mini-tag primary">${escapeHtml(String(totalProfiles))} accounts</span>
                        </div>
                        <div class="progress-line subtle-progress">
                            <div class="progress-fill" style="width:${width}%"></div>
                        </div>
                        <div class="split-line muted">
                            <span>Logged in ${escapeHtml(String(item.logged_in || item.logged_in_count || 0))}</span>
                            <span>Success ${escapeHtml(String(item.success || item.success_count || 0))} / Failure ${escapeHtml(String(item.error || item.error_count || 0))}</span>
                        </div>
                    </div>
                `;
            }).join("")}
        </div>
    `;
}

function renderProfileCard(profile) {
    const profileId = escapeJs(String(profile.id ?? ""));
    const lastResult = String(profile.last_check_result || profile.last_sync_result || "");
    const resultStatus = getResultStatus(lastResult);
    const resultTone = getStatusTone(resultStatus);
    const usesDefaultTarget = profile.uses_default_target !== false && !profile.flow2api_url;
    const targetLabel = usesDefaultTarget ? "default target" : "independent target";
    const lastProcessedAt = profile.last_check_time || profile.last_sync_time;
    const profileIdentity = String(
        profile.email
        || profile.login_account
        || (profile.is_logged_in ? "Logged in/Email to be recognized" : "Not logged in/email not recognized"),
    ).trim();
    const isBrowserActive = Boolean(profile.is_browser_active || profile.browser_running);
    const isActive = profile.is_active !== false && profile.is_enabled !== false;
    const hasLoginCredentials = Boolean(profile.has_login_credentials || profile.has_login_password);
    const vncEnabled = Boolean(state.dashboard?.config?.enable_vnc);
    const effectiveTarget = profile.effective_flow2api_url || profile.flow2api_url || state.dashboard?.config?.flow2api_url || "Not configured";
    const successCount = profile.sync_count || profile.success_count || 0;
    const errorCount = profile.error_count || 0;

    return `
        <article class="profile-card">
            <div class="profile-top">
                <div>
                    <h3 class="profile-name">${escapeHtml(profile.name || "Unnamed")}</h3>
                    <div class="profile-meta-row">
                        <span class="truncate-text" title="${escapeAttr(profileIdentity)}">${escapeHtml(profileIdentity)}</span>
                        <span class="profile-dot">•</span>
                        <span class="${profile.is_logged_in ? "meta-success" : "meta-muted"}">${profile.is_logged_in ? "Logged in" : "Not logged in"}</span>
                    </div>
                    ${profile.remark? `<div class="profile-meta">${escapeHtml(profile.remark)}</div>` : ""}
                </div>
                <span class="badge ${isBrowserActive ? "warning" : isActive ? "success" : "default"}">
                    ${isBrowserActive ? "The browser is running" : isActive ? "Enabled" : "Disabled"}
                </span>
            </div>

            <div class="profile-tags">
                <span class="badge ${profile.is_logged_in ? "success" : "default"}">${profile.is_logged_in ? "Logged in" : "Not logged in"}</span>
                ${profile.login_method? `<span class="badge ${profile.login_method === "protocol" ? "info" : "primary"}">${profile.login_method === "protocol" ? "Agreement login" : "Browser login"}</span>` : ""}
                <span class="badge ${resultTone === "info" ? "default" : resultTone} is-truncate" title="${escapeAttr(lastResult || "No processing result")}">${escapeHtml(lastResult || "No processing result")}</span>
                <span class="badge ${usesDefaultTarget ? "default" : "info"}">${escapeHtml(targetLabel)}</span>
                ${hasLoginCredentials? `<span class="badge primary">Credentials configured</span>`: ""}
                ${profile.has_connection_token_override ? `<span class="badge warning">Token covered</span>`: ""}
                ${profile.proxy_url ? `<span class="badge info">Agent configured</span>`: ""}
            </div>

            <div class="profile-body">
                <div class="detail-line">
                    ${renderIcon("globe")}
                    <span class="truncate-text" title="${escapeAttr(effectiveTarget)}">${escapeHtml(effectiveTarget)}</span>
                </div>
                <div class="detail-line">
                    ${renderIcon("clock")}
                    <span>${escapeHtml(formatDate(lastProcessedAt))}</span>
                </div>
                <div class="detail-stats">
                    <span class="detail-stat success">${renderIcon("check-circle")} ${escapeHtml(String(successCount))}</span>
                    <span class="detail-stat danger">${renderIcon("x-circle")} ${escapeHtml(String(errorCount))}</span>
                </div>
            </div>

            <div class="profile-footer">
                <div class="button-row wrap-row">
                    ${!isBrowserActive && hasLoginCredentials
                        ? `<button class="btn primary small" ${getBrowserLockAttrs()} onclick="autoLogin('${profileId}', this)">${renderIcon("play")} Automatic login</button>`
                        : ""}
                    ${vncEnabled
                        ? (isBrowserActive
                            ? `<button class="btn danger small" ${getBrowserLockAttrs()} onclick="closeBrowser('${profileId}', this)">${renderIcon("square")} Close browser</button>`
                            : `<button class="btn secondary small" ${getBrowserLockAttrs()} onclick="launchBrowser('${profileId}', this)">${renderIcon("monitor")} Login</button>`)
                        : ""}
                    <button class="btn outline small" ${getBrowserLockAttrs()} onclick="checkLogin('${profileId}', this)">${renderIcon("shield")} detection</button>
                    <button class="btn outline small" ${getBrowserLockAttrs()} onclick="syncProfile('${profileId}', this)">${renderIcon("refresh")} Sync</button>
                    <button class="btn outline small" onclick="openCookieModal('${profileId}')">${renderIcon("cookie")} session data</button>
                    <button class="btn outline small" onclick="openProtocolLoginModal('${profileId}')">${renderIcon("key")} Protocol Login</button>
                </div>
                <div class="button-row end-row">
                    <button class="btn ghost small" onclick="openProfileModal('${profileId}')">${renderIcon("edit")} Edit</button>
                    <button class="btn ghost small danger-text" ${getBrowserLockAttrs()} onclick="deleteProfile('${profileId}', '${escapeJs(profile.name || "")}', this)">${renderIcon("trash")} Delete</button>
                </div>
            </div>
        </article>
    `;
}

function renderRecentActivity(events) {
    if (!events.length) {
        return `<div class="empty-state">There are no synchronized records yet, please synchronize manually first to check. </div>`;
    }

    return `
        <div class="activity-list">
            ${events.map((event) => `
                <div class="activity-item">
                    <div class="activity-main">
                        <span class="badge ${getStatusTone(event.status)} activity-badge">${escapeHtml(getStatusLabel(event.status))}</span>
                        <div class="activity-copy">
                            <div class="activity-line">
                                <strong>${escapeHtml(event.profile_name || "system")}</strong>
                                <span>${escapeHtml(event.message || event.action || "No description yet")}</span>
                                ${event.reason_category ? `<span class="mini-tag info">${escapeHtml(event.reason_category)}</span>` : ""}
                            </div>
                            <div class="activity-meta">${escapeHtml(event.target_label || event.target_url || "Destination address not recorded")}</div>
                        </div>
                    </div>
                    <div class="activity-time">${escapeHtml(formatDate(event.created_at))}</div>
                </div>`).join("")}
        </div>
    `;
}

async function setChartRange(hours, button) {
    const nextHours = normalizeDashboardHours(hours);
    if (nextHours === state.selectedHours && state.dashboard) {
        return;
    }

    state.selectedHours = nextHours;
    localStorage.setItem("dashboard-hours", String(nextHours));
    await withButton(button, "Switching...", async () => {
        await refreshDashboard(false, true);
    });
}

async function doLogin(button) {
    const password = (document.getElementById("login-password")?.value || "").trim();
    if (!password) {
        toast("Please enter administrator password", "error");
        return;
    }

    await withButton(button, "Login...", async () => {
        const response = await publicRequest(`${API}/api/login`, {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({password}),
        }, {allowError: true});
        const data = await safeJson(response);
        if (!response.ok || !data.success) {
            throw new Error(data.detail || data.error || "Wrong password");
        }
        state.token = data.token;
        localStorage.setItem("t", data.token);
        showAppShell();
        await refreshDashboard(false, true);
        toast("Login successful", "success");
    });
}

async function doLogout(button) {
    await withButton(button, "Exiting...", async () => {
        try {
            await request(`${API}/api/logout`, {method: "POST"}, {allowError: true});
        } catch (_) {
            // Ignore network jitter on exit.
        }
        handleExpiredSession();
        toast("Exited", "success");
    });
}

async function refreshDashboardAction(button) {
    await withButton(button, "Refreshing...", async () => {
        await refreshDashboard(false, true);
        toast("Refreshed", "success");
    });
}

async function saveConfig(button) {
    const url = (document.getElementById("config-url")?.value || "").trim();
    const connectionToken = document.getElementById("config-token")?.value || "";
    const intervalValue = (document.getElementById("config-interval")?.value || "").trim();

    if (!url) {
        toast("Please enter the default Flow2API address", "error");
        return;
    }

    const refreshInterval = Number(intervalValue || 60);
    if (!Number.isInteger(refreshInterval) || refreshInterval < 1 || refreshInterval > 1440) {
        toast("The refresh interval needs to be between 1-1440 minutes", "error");
        return;
    }

    const payload = {flow2api_url: url, refresh_interval: refreshInterval};
    if (connectionToken) {
        payload.connection_token = connectionToken;
    }

    await withButton(button, "Saving...", async () => {
        await json(`${API}/api/config`, {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify(payload),
        });
        await refreshDashboard(false, true);
        document.getElementById("config-token").value = "";
        toast("Default configuration saved", "success");
    });
}

function openProfileModal(profileId = null) {
    if (profileId) {
        loadProfileModal(profileId);
        return;
    }
    renderProfileModal({is_active: true, proxy_enabled: false}, false);
}

async function loadProfileModal(profileId) {
    try {
        const profile = await json(`${API}/api/profiles/${profileId}`);
        renderProfileModal(profile, true);
    } catch (error) {
        toast(error.message || "Failed to read account", "error");
    }
}

function renderProfileModal(profile, editing) {
    state.modal = {type: "profile", profileId: profile.id || null, editing};
    const hasOverride = Boolean(profile.connection_token_override || profile.connection_token_override_preview);
    const hasLoginCredentials = Boolean(profile.has_login_credentials || profile.has_login_password);
    showModal(`
        <div class="modal-card modal-wide">
            <div class="modal-head">
                <div>
                    <span class="eyebrow">${editing ? "Edit Account" : "New Account"}</span>
                    <h3 class="modal-title">${editing ? "Editing account" : "New account"}</h3>
                    <p class="modal-copy">${editing ? "Adjust account login, proxy and target configuration." : "Create a new Flow2API account."}</p>
                </div>
                <button class="btn ghost icon-only" onclick="closeModal()" title="Close">${renderIcon("x")}</button>
            </div>
            <div class="form-grid">
                <div class="field span-2">
                    <label for="profile-name">Account name</label>
                    <input id="profile-name" value="${escapeAttr(profile.name || "")}" placeholder="For example: main account-A">
                </div>
                <div class="field span-2">
                    <label for="profile-remark">Remarks</label>
                    <input id="profile-remark" value="${escapeAttr(profile.remark || "")}" placeholder="Write some notes to find them faster later">
                </div>
                <div class="field">
                    <label for="profile-login-account">Login account</label>
                    <input id="profile-login-account" value="${escapeAttr(profile.login_account || "")}" placeholder="Email, mobile phone number or Workspace account">
                </div>
                <div class="field">
                    <label for="profile-login-password">Login password</label>
                    <input id="profile-login-password" type="password" placeholder="${escapeAttr(hasLoginCredentials ? "Saved, leave blank to not modify" : "Leave blank to not configure automatic login")}">
                    <span class="field-hint">After saving, it will be used for automatic login in the account card. </span>
                </div>
                <div class="field span-2">
                    <label>Enabled status</label>
                    <label class="switch">
                        <input id="profile-active" type="checkbox" ${profile.is_active === false ? "" : "checked"}>
                        <span>This account participates in automatic synchronization</span>
                    </label>
                </div>
                <div class="field">
                    <label for="profile-proxy">Proxy address</label>
                    <input id="profile-proxy" value="${escapeAttr(profile.proxy_url || "")}" placeholder="http://user:pass@host:port">
                    <span class="field-hint">Leave it blank to indicate that no proxy will be used. </span>
                </div>
                <div class="field">
                    <label for="profile-target-url">Flow2API address override</label>
                    <input id="profile-target-url" value="${escapeAttr(profile.flow2api_url || "")}" placeholder="Leave blank to use the global default address">
                    <span class="field-hint">Suitable for pushing an account to another set of Flow2API. </span>
                </div>
                <div class="field">
                    <label for="profile-target-token">Connection token override</label>
                    <input id="profile-target-token" type="password" placeholder="${escapeAttr(profile.connection_token_override_preview || "Leave blank to use the global default token")}">
                    <span class="field-hint">Entering a new value will overwrite it; leaving it blank will not modify the current value by default. </span>
                </div>
            </div>
            ${hasOverride? `
                <div class="field span-2">
                    <label class="switch">
                        <input id="profile-clear-token-override" type="checkbox">
                        <span>Clear the current connection token override and change back to the global default value</span>
                    </label>
                </div>` : ""}
            ${editing && hasLoginCredentials ? `
                <div class="field span-2">
                    <label class="switch">
                        <input id="profile-clear-login-credentials" type="checkbox">
                        <span>Clear the current login account and password, and turn off automatic login</span>
                    </label>
                </div>` : ""}
            <div class="modal-actions">
                <button class="btn outline" onclick="closeModal()">Cancel</button>
                <button class="btn primary" onclick="saveProfile(this)">${editing ? "Save changes" : "Create account"}</button>
            </div>
        </div>
    `);
}

async function saveProfile(button) {
    const modal = state.modal || {};
    const name = (document.getElementById("profile-name")?.value || "").trim();
    const remark = (document.getElementById("profile-remark")?.value || "").trim();
    const loginAccount = (document.getElementById("profile-login-account")?.value || "").trim();
    const loginPassword = document.getElementById("profile-login-password")?.value || "";
    const clearLoginCredentials = Boolean(document.getElementById("profile-clear-login-credentials")?.checked);
    const proxyUrl = (document.getElementById("profile-proxy")?.value || "").trim();
    const flow2apiUrl = (document.getElementById("profile-target-url")?.value || "").trim();
    const tokenOverride = document.getElementById("profile-target-token")?.value || "";
    const clearOverride = Boolean(document.getElementById("profile-clear-token-override")?.checked);
    const isActive = Boolean(document.getElementById("profile-active")?.checked);

    if (!name) {
        toast("Please enter account name", "error");
        return;
    }

    const payload = {
        name,
        remark,
        is_active: isActive,
        login_account: loginAccount,
        proxy_url: proxyUrl,
        flow2api_url: flow2apiUrl,
    };
    if (!modal.editing || loginPassword) {
        payload.login_password = loginPassword;
    }
    if (modal.editing && clearLoginCredentials) {
        payload.clear_login_credentials = true;
    }
    if (!modal.editing || tokenOverride) {
        payload.connection_token_override = tokenOverride;
    }
    if (modal.editing && clearOverride) {
        payload.connection_token_override = "";
    }

    await withButton(button, modal.editing ? "Saving..." : "Creating...", async () => {
        if (modal.editing) {
            await json(`${API}/api/profiles/${modal.profileId}`, {
                method: "PUT",
                headers: {"Content-Type": "application/json"},
                body: JSON.stringify(payload),
            });
        } else {
            await json(`${API}/api/profiles`, {
                method: "POST",
                headers: {"Content-Type": "application/json"},
                body: JSON.stringify(payload),
            });
        }
        closeModal();
        await refreshDashboard(false, true);
        toast(modal.editing ? "Account has been saved" : "Account has been created", "success");
    });
}

function openCredentialImportModal() {
    state.modal = {type: "import-accounts"};
    showModal(`
        <div class="modal-card modal-compact">
            <div class="modal-head">
                <div>
                    <span class="eyebrow">Import account password</span>
                    <h3 class="modal-title">Batch import automatic login credentials</h3>
                    <p class="modal-copy">Paste the account text line by line, and the system will automatically parse and import it. </p>
                </div>
                <button class="btn ghost icon-only" onclick="closeModal()" title="Close">${renderIcon("x")}</button>
            </div>
            <div class="info-panel">
                <div class="info-panel-head">
                    ${renderIcon("info")}
                    <strong>Supported formats</strong>
                </div>
                <ul class="info-list">
                    <li>Three columns: name, account number, password, supported by commas, Tab, |, and ----. </li>
                    <li>Two columns: account number, password, and name will automatically use the account number. </li>
                </ul>
            </div>
            <div class="field">
                <label for="accounts-import-content">Account text</label>
                <textarea id="accounts-import-content" placeholder="name, account number, password
Alternate number, foo@gmail.com, pass123

Also supports two columns: account number, password"></textarea>
            </div>
            <div class="field">
                <label class="switch">
                    <input id="accounts-import-update-existing" type="checkbox" checked>
                    <span>Update the login credentials of an existing account when the name is duplicate</span>
                </label>
            </div>
            <div class="modal-actions">
                <button class="btn outline" onclick="closeModal()">Cancel</button>
                <button class="btn primary" onclick="submitCredentialImport(this)">Start import</button>
            </div>
        </div>
    `);
}

async function submitCredentialImport(button) {
    const content = (document.getElementById("accounts-import-content")?.value || "").trim();
    const updateExisting = Boolean(document.getElementById("accounts-import-update-existing")?.checked);
    if (!content) {
        toast("Please enter the account text to be imported", "error");
        return;
    }

    await withButton(button, "Importing...", async () => {
        const result = await json(`${API}/api/profiles/import-accounts`, {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({content, update_existing: updateExisting}),
        });
        closeModal(true);
        await refreshDashboard(false, true);
        toast(`Import completed: added ${result.created || 0}, updated ${result.updated || 0}, skipped ${result.skipped || 0}`, "success");
    });
}

function openCookieModal(profileId) {
    const profile = (state.dashboard?.profiles || []).find((item) => String(item.id) === String(profileId)) || {};
    state.modal = {type: "cookie", profileId};
    showModal(`
        <div class="modal-card modal-compact">
            <div class="modal-head">
                <div>
                    <span class="eyebrow">Session data management</span>
                    <h3 class="modal-title">Import or export the current login status</h3>
                    <p class="modal-copy">After importing Cookie JSON for <strong>${escapeHtml(profile.name || "Current Account")}</strong>, the system will write persistent browser data and automatically refresh the session. You can also directly export the labs.google Cookie of the current account as a backup. </p>
                </div>
                <button class="btn ghost icon-only" onclick="closeModal()" title="Close">${renderIcon("x")}</button>
            </div>
            <div class="field">
                <label for="cookie-json">Session data text</label>
                <textarea id="cookie-json" placeholder='[{"name":"...","value":"...","domain":".labs.google","path":"/","secure":true}]'></textarea>
            </div>
            <div class="modal-actions">
                <button class="btn outline" onclick="closeModal()">Cancel</button>
                <button class="btn secondary" ${getBrowserLockAttrs()} onclick="exportCookies(this, 'session')">${renderIcon("download")} Export current session</button>
                <button class="btn primary" ${getBrowserLockAttrs()} onclick="submitCookies(this)">Import session data</button>
            </div>
        </div>
    `);
}

function openProtocolLoginModal(profileId) {
    const profile = (state.dashboard?.profiles || []).find((item) => String(item.id) === String(profileId)) || {};
    state.modal = {type: "protocol-login", profileId};
    showModal(`
        <div class="modal-card modal-compact">
            <div class="modal-head">
                <div>
                    <span class="eyebrow">Protocol Cookie Management</span>
                    <h3 class="modal-title">Import or export Google Cookies</h3>
                    <p class="modal-copy">Performs a pure HTTP login for <strong>${escapeHtml(profile.name || "current account")}</strong> without launching a browser. You can also export the Google Cookies saved in the current account for backup or migration. </p>
                </div>
                <button class="btn ghost icon-only" onclick="closeModal()" title="Close">${renderIcon("x")}</button>
            </div>
            <div class="field">
                <label for="google-cookies">Google Cookies</label>
                <textarea id="google-cookies" placeholder='Paste the cookies of your Google account. The following formats are supported:

JSON: [{"name":"SID","value":"xxx"}, ...]
Plain text: SID=xxx; HSID=xxx; SSID=xxx'></textarea>
                <span class="field-hint">You need to export cookies from both <strong>.google.com</strong> and <strong>accounts.google.com</strong> domains (including SID/HSID/SSID/APISID/SAPISID and GAPS/LSID, etc.). You can use a browser plug-in (such as EditThisCookie) to export the two domains separately and then merge and paste them together. </span>
            </div>
            <div class="modal-actions">
                <button class="btn outline" onclick="closeModal()">Cancel</button>
                <button class="btn secondary" onclick="exportCookies(this, 'google')">${renderIcon("download")} Export current Google Cookies</button>
                <button class="btn primary" ${getBrowserLockAttrs()} onclick="submitProtocolLogin(this)">Protocol Login</button>
            </div>
        </div>
    `);
}

async function submitCookies(button) {
    const modal = state.modal || {};
    const cookiesJson = (document.getElementById("cookie-json")?.value || "").trim();
    if (!cookiesJson) {
        toast("Please enter session data text", "error");
        return;
    }

    await withOperationLock(button, "Importing...", {action: "import_cookies", label: "Import session data"}, async () => {
        const data = await json(`${API}/api/profiles/${modal.profileId}/import-cookies`, {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({cookies_json: cookiesJson}),
        });
        closeModal(true);
        await refreshDashboard(false, true);
        toast(data.has_token ? "Import successful, session token detected" : "Imported, but no session token detected yet", data.has_token ? "success" : "error");
    });
}

async function exportCookies(button, kind = "google") {
    const modal = state.modal || {};
    const textareaId = kind === "google" ? "google-cookies" : "cookie-json";

    await withOperationLock(button, "Exporting...", {action: "export_cookies", label: "Export cookies", profile_id: modal.profileId}, async () => {
        const data = await json(`${API}/api/profiles/${modal.profileId}/export-cookies?kind=${encodeURIComponent(kind)}`);
        const formatted = String(data.cookies_json || "");
        if (!formatted) {
            throw new Error("No exportable cookie content was obtained");
        }
        const textarea = document.getElementById(textareaId);
        if (textarea) {
            textarea.value = formatted;
        }
        const filename = data.filename || `profile-${modal.profileId}-${kind}-cookies.json`;
        downloadTextFile(filename, formatted);
        const count = Number(data.cookie_count ?? data.count ?? 0);
        const successMessage = kind === "google"
            ? `Exported ${count} Google Cookies`
            : (data.has_token ? `Exported ${count} cookies, including session tokens` : `Exported ${count} cookies`);
        toast(successMessage, "success");
    });
}

async function submitProtocolLogin(button) {
    const modal = state.modal || {};
    const googleCookies = (document.getElementById("google-cookies")?.value || "").trim();
    if (!googleCookies) {
        toast("Please enter Google Cookies", "error");
        return;
    }

    await withOperationLock(button, "Login...", {action: "protocol_login", label: "Agreement login", profile_id: modal.profileId}, async () => {
        const data = await json(`${API}/api/profiles/${modal.profileId}/protocol-login`, {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({google_cookies: googleCookies}),
        });
        closeModal(true);
        await refreshDashboard(false, true);
        toast(data.success ? "The protocol login is successful and the session token has been obtained" : (data.error || "Protocol login failed"), data.success ? "success" : "error");
    });
}

async function syncAll(button) {
    await withOperationLock(button, "Syncing...", {action: "sync_all", label: "Sync all accounts"}, async () => {
        const result = await json(`${API}/api/sync-all`, {method: "POST"});
        await refreshDashboard(false, true);
        toast(`Completed: Success ${result.success_count || 0}, Failure ${result.error_count || 0}, Skip ${result.skipped || 0}`, "success");
    });
}

async function syncProfile(profileId, button) {
    await withOperationLock(button, "Syncing...", {action: "sync_profile", label: "Sync accounts", profile_id: profileId}, async () => {
        const result = await json(`${API}/api/profiles/${profileId}/sync`, {method: "POST"});
        await refreshDashboard(false, true);
        toast(result.success ? "Synchronization successful" : result.error || "Sync failed", result.success ? "success" : "error");
    });
}

async function checkLogin(profileId, button) {
    await withOperationLock(button, "Under detection...", {action: "check_login", label: "Check login status", profile_id: profileId}, async () => {
        const result = await json(`${API}/api/profiles/${profileId}/check-login`, {method: "POST"});
        await refreshDashboard(false, true);
        toast(result.is_logged_in ? "Logged in" : "Not logged in or expired", result.is_logged_in ? "success" : "error");
    });
}

async function autoLogin(profileId, button) {
    await withOperationLock(button, "Login...", {action: "auto_login", label: "Automatic login", profile_id: profileId}, async () => {
        const result = await json(`${API}/api/profiles/${profileId}/auto-login`, {method: "POST"});
        await refreshDashboard(false, true);
        toast(result.has_token ? "Automatic login successful, session token obtained" : "Automatic login successful", "success");
    });
}

async function launchBrowser(profileId, button) {
    await withOperationLock(button, "Starting...", {action: "launch_browser", label: "Start browser login", profile_id: profileId}, async () => {
        await json(`${API}/api/profiles/${profileId}/launch`, {method: "POST"});
        await waitVncReady();
        await refreshDashboard(false, true);
        openVnc();
        toast("The browser has been started, please complete the login in the remote login window", "success");
    });
}

async function closeBrowser(profileId, button) {
    await withOperationLock(button, "Closed...", {action: "close_browser", label: "Close browser", profile_id: profileId}, async () => {
        const result = await json(`${API}/api/profiles/${profileId}/close`, {method: "POST"});
        await refreshDashboard(false, true);
        toast(result.is_logged_in ? "Browser closed, login status saved" : "Browser is closed", "success");
    });
}

async function deleteProfile(profileId, profileName, button) {
    if (!window.confirm(`Are you sure you want to delete "${profileName}"?`)) {
        return;
    }

    await withOperationLock(button, "Deleting...", {action: "delete_profile", label: "Delete account", profile_id: profileId, profile_name: profileName}, async () => {
        await request(`${API}/api/profiles/${profileId}`, {method: "DELETE"});
        await refreshDashboard(false, true);
        toast("Account has been deleted", "success");
    });
}
async function waitVncReady(timeoutMs = 10000) {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
        try {
            const status = await json(`${API}/api/status`);
            if (status?.browser?.vnc_stack_running) {
                return true;
            }
        } catch (_) {
            // Just poll and wait.
        }
        await delay(500);
    }
    return false;
}

function openVnc() {
    const url = `${location.protocol}//${location.hostname}:6080/vnc.html`;
    window.open(url, "_blank", "noopener");
}

function showModal(content) {
    elements.modalRoot.className = "modal-layer";
    elements.modalRoot.innerHTML = content;
}

function closeModal(skipRefresh = false) {
    state.modal = null;
    elements.modalRoot.className = "hidden";
    elements.modalRoot.innerHTML = "";
    if (!skipRefresh && state.pendingRefresh && !state.loading && state.token) {
        refreshDashboard(true, true).catch(() => {});
    } else {
        updateStreamBadge();
    }
}

elements.modalRoot.addEventListener("click", (event) => {
    if (event.target === elements.modalRoot) {
        closeModal();
    }
});

function toast(message, type = "success") {
    const toastElement = document.createElement("div");
    toastElement.className = `toast ${type}`;
    toastElement.textContent = message;
    elements.toastRoot.appendChild(toastElement);
    window.setTimeout(() => toastElement.remove(), 3200);
}

async function withOperationLock(button, pendingText, lockState, action) {
    setOperationLock(lockState);
    try {
        await withButton(button, pendingText, action);
    } finally {
        setOperationLock(null);
    }
}

async function withButton(button, pendingText, action) {
    const original = button ? button.innerHTML : "";
    if (button) {
        button.dataset.pending = "true";
        button.disabled = true;
        button.innerHTML = pendingText;
    }
    try {
        await action();
    } catch (error) {
        if (error.message !== "expired") {
            toast(error.message || "Operation failed", "error");
        }
    } finally {
        if (button) {
            delete button.dataset.pending;
            button.disabled = false;
            button.innerHTML = original;
        }
        syncExecutionButtons();
    }
}

async function request(url, options = {}, {allowError = false, auth = true} = {}) {
    const headers = new Headers(options.headers || {});
    if (auth && state.token) {
        headers.set("Authorization", `Bearer ${state.token}`);
    }

    const response = await fetch(url, {...options, headers});
    if (auth && response.status === 401) {
        handleExpiredSession();
        throw new Error("expired");
    }
    if (!allowError && !response.ok) {
        throw new Error(await parseError(response));
    }
    return response;
}

async function publicRequest(url, options = {}, {allowError = false} = {}) {
    const response = await fetch(url, options);
    if (!allowError && !response.ok) {
        throw new Error(await parseError(response));
    }
    return response;
}

async function json(url, options = {}, requestOptions = {}) {
    const response = await request(url, options, requestOptions);
    return safeJson(response);
}

async function publicJson(url, options = {}) {
    const response = await publicRequest(url, options);
    return safeJson(response);
}

async function safeJson(response) {
    try {
        return await response.json();
    } catch (_) {
        return {};
    }
}

async function parseError(response) {
    const data = await safeJson(response);
    return data.detail || data.error || data.message || `Request failed (status code ${response.status})`;
}

function handleExpiredSession() {
    disconnectDashboardStream();
    state.token = "";
    localStorage.removeItem("t");
    showLogin();
}

function delay(ms) {
    return new Promise((resolve) => window.setTimeout(resolve, ms));
}

function downloadTextFile(filename, content) {
    const blob = new Blob([content], {type: "application/json;charset=utf-8"});
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 0);
}

function formatDate(value) {
    if (!value) {
        return "No record yet";
    }
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return value;
    }
    return date.toLocaleString("zh-CN", {
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
    });
}

function escapeHtml(value) {
    return String(value ?? "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/\"/g, "&quot;")
        .replace(/'/g, "&#39;");
}

function escapeAttr(value) {
    return escapeHtml(value).replace(/`/g, "&#96;");
}

function escapeJs(value) {
    return String(value ?? "").replace(/\\/g, "\\\\").replace(/'/g, "\\'");
}

window.doLogin = doLogin;
window.doLogout = doLogout;
window.refreshDashboardAction = refreshDashboardAction;
window.setDashboardView = setDashboardView;
window.setChartRange = setChartRange;
window.saveConfig = saveConfig;
window.openProfileModal = openProfileModal;
window.saveProfile = saveProfile;
window.closeModal = closeModal;
window.openCredentialImportModal = openCredentialImportModal;
window.submitCredentialImport = submitCredentialImport;
window.openCookieModal = openCookieModal;
window.openProtocolLoginModal = openProtocolLoginModal;
window.submitCookies = submitCookies;
window.exportCookies = exportCookies;
window.submitProtocolLogin = submitProtocolLogin;
window.syncAll = syncAll;
window.syncProfile = syncProfile;
window.checkLogin = checkLogin;
window.autoLogin = autoLogin;
window.launchBrowser = launchBrowser;
window.closeBrowser = closeBrowser;
window.deleteProfile = deleteProfile;
window.openVnc = openVnc;


