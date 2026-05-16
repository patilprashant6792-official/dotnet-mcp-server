// State
let packages = [];

// DOM Elements
const pkgList       = document.getElementById('pkg-list');
const nugetTable    = document.getElementById('nuget-table-wrap');
const emptyState    = document.getElementById('empty-state');
const pkgCount      = document.getElementById('pkg-count');
const clearAllBtn   = document.getElementById('clear-all-btn');
const errorBanner   = document.getElementById('error-banner');
const successBanner = document.getElementById('success-banner');

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    loadPackages();
    clearAllBtn.addEventListener('click', clearAll);
});

// API calls
async function loadPackages() {
    try {
        const res = await fetch('/api/nuget-cache');
        if (!res.ok) throw new Error('Failed to load cache');
        packages = await res.json();
        renderPackages();
    } catch (err) {
        showError('Failed to load NuGet cache: ' + err.message);
    }
}

async function deleteEntry(key) {
    try {
        const res = await fetch(`/api/nuget-cache/${encodeURIComponent(key)}`, { method: 'DELETE' });
        if (res.status === 404) { showError('Entry not found — already evicted?'); return; }
        if (!res.ok) {
            const err = await res.json();
            throw new Error(err.error || 'Delete failed');
        }
        showSuccess('Cache entry removed');
        loadPackages();
    } catch (err) {
        showError(err.message);
    }
}

async function clearAll() {
    if (!confirm(`Clear all ${packages.length} cached NuGet package(s)?\nThey will be re-fetched from NuGet.org on next use.`)) return;
    try {
        const res = await fetch('/api/nuget-cache', { method: 'DELETE' });
        if (!res.ok) throw new Error('Clear all failed');
        const data = await res.json();
        showSuccess(`Cleared ${data.cleared} cached package(s)`);
        loadPackages();
    } catch (err) {
        showError(err.message);
    }
}

// UI
function renderPackages() {
    pkgCount.textContent = packages.length;
    clearAllBtn.classList.toggle('hidden', packages.length === 0);

    if (packages.length === 0) {
        nugetTable.classList.add('hidden');
        emptyState.classList.remove('hidden');
        return;
    }

    nugetTable.classList.remove('hidden');
    emptyState.classList.add('hidden');

    pkgList.innerHTML = packages.map(p => `
        <div class="nuget-row">
            <span class="nuget-pkg" title="${escapeHtml(p.key)}">${escapeHtml(p.packageId)}</span>
            <span class="nuget-badge version-badge">${escapeHtml(p.version)}</span>
            <span class="nuget-badge framework-badge">${escapeHtml(p.framework)}</span>
            <span class="ttl-badge ${ttlClass(p.expiresAt)}">${formatTtl(p.expiresAt)}</span>
            <button class="btn btn-danger btn-row-delete" onclick="deleteEntry('${escapeHtml(p.key)}')" title="Remove from cache">✕</button>
        </div>
    `).join('');
}

// TTL helpers
function ttlClass(expiresAt) {
    if (!expiresAt) return 'ttl-none';
    const hours = (new Date(expiresAt) - Date.now()) / 36e5;
    if (hours < 6)  return 'ttl-red';
    if (hours < 48) return 'ttl-orange';
    return 'ttl-green';
}

function formatTtl(expiresAt) {
    if (!expiresAt) return 'No expiry';
    const ms = new Date(expiresAt) - Date.now();
    if (ms <= 0) return 'Expired';
    const d = Math.floor(ms / 864e5);
    const h = Math.floor((ms % 864e5) / 36e5);
    const m = Math.floor((ms % 36e5) / 6e4);
    if (d > 0) return `${d}d ${h}h`;
    if (h > 0) return `${h}h ${m}m`;
    return `${m}m`;
}

function showError(message) {
    errorBanner.textContent = message;
    errorBanner.classList.remove('hidden');
    successBanner.classList.add('hidden');
    setTimeout(() => errorBanner.classList.add('hidden'), 5000);
}

function showSuccess(message) {
    successBanner.textContent = message;
    successBanner.classList.remove('hidden');
    errorBanner.classList.add('hidden');
    setTimeout(() => successBanner.classList.add('hidden'), 3000);
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text ?? '';
    return div.innerHTML;
}
