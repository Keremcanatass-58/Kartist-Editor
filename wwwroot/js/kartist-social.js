/* ==========================================================================
   KARTIST SOCIAL v3.0 — Core JavaScript
   ========================================================================== */

// --- CSRF Token ---
function CSRF() {
    const el = document.querySelector('input[name="__RequestVerificationToken"]');
    return el ? el.value : '';
}

// --- HTML Escape (XSS guard for template-literal interpolation) ---
function escapeHtml(s) {
    if (s == null) return '';
    return String(s)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

// --- Media URL Normalizer ---
window.normalizeMediaUrl = function(url) {
    if (!url) return url;
    url = url.trim().replace(/\\/g, '/');
    if (url.startsWith('~/')) return '/' + url.slice(2);
    if (url.startsWith('http://') || url.startsWith('https://')) return url;
    if (url.startsWith('/')) return url;
    if (url.startsWith('uploads/')) return '/' + url;
    if (/^[^\/]+\.(jpg|jpeg|png|gif|webp|svg)$/i.test(url)) return '/uploads/social/' + url;
    return '/' + url;
}

// --- Secure Fetch Helper ---
async function secureFetch(url, method = 'GET', body = null) {
    try {
        const opts = {
            method,
            credentials: 'same-origin'
        };
        if (body) {
            opts.body = body;
            // FormData doesn't need Content-Type header
        }
        const res = await fetch(url, opts);
        if (!res.ok) {
            console.warn(`API Error: ${res.status} ${url}`);
            return { success: false, message: `HTTP ${res.status}` };
        }
        return await res.json();
    } catch (e) {
        console.error('secureFetch error:', e);
        return { success: false, message: 'Bağlantı hatası' };
    }
}

// --- Neon Toast ---
function showNeonToast(message, icon = 'fa-check-circle') {
    // Remove existing toasts
    document.querySelectorAll('.neon-toast').forEach(t => t.remove());
    
    const toast = document.createElement('div');
    toast.className = 'neon-toast';
    toast.innerHTML = `<i class="fa-solid ${escapeHtml(icon)}"></i> ${escapeHtml(message)}`;
    document.body.appendChild(toast);
    
    requestAnimationFrame(() => {
        requestAnimationFrame(() => toast.classList.add('show'));
    });
    
    setTimeout(() => {
        toast.classList.remove('show');
        setTimeout(() => toast.remove(), 400);
    }, 2500);
}

// --- Time Ago ---
function timeAgo(dateStr) {
    if (!dateStr) return '';
    const now = new Date();
    const date = new Date(dateStr);
    const diff = Math.floor((now - date) / 1000);
    
    if (diff < 60) return 'az önce';
    if (diff < 3600) return Math.floor(diff / 60) + 'dk';
    if (diff < 86400) return Math.floor(diff / 3600) + 'sa';
    if (diff < 604800) return Math.floor(diff / 86400) + 'g';
    return date.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short' });
}

// --- Avatar HTML Generator ---
function avatarHTML(url, name, size = 44) {
    const px = Number(size) || 44;
    if (url) return `<img src="${escapeHtml(normalizeMediaUrl(url))}" class="avatar-img" style="width:${px}px;height:${px}px;" loading="lazy">`;
    const letter = escapeHtml(((name || '?')[0] || '?').toUpperCase());
    return `<div class="avatar-initial" style="width:${px}px;height:${px}px;font-size:${Math.round(px/2.5)}px;">${letter}</div>`;
}

// --- Notification System ---
let notifPanelOpen = false;

function toggleNotifPanel() {
    notifPanelOpen = !notifPanelOpen;
    document.getElementById('notifPanel').classList.toggle('open', notifPanelOpen);
    document.getElementById('notifOverlay').classList.toggle('open', notifPanelOpen);
    
    if (notifPanelOpen) {
        loadNotifications();
        markNotificationsRead();
    }
}

async function loadNotifications() {
    const data = await secureFetch('/Social/GetBildirimler');
    if (!data || !data.success) return;
    
    const list = document.getElementById('notifList');
    if (!data.bildirimler || data.bildirimler.length === 0) {
        list.innerHTML = `<div style="text-align:center; padding:60px 20px; color:var(--text-muted)">
            <i class="fa-regular fa-bell" style="font-size:2.5rem; display:block; margin-bottom:12px; opacity:0.3"></i>
            Henüz bildirim yok
        </div>`;
        return;
    }
    
    list.innerHTML = data.bildirimler.map(b => {
        const avatar = b.GonderenResim
            ? `<img src="${escapeHtml(normalizeMediaUrl(b.GonderenResim))}" class="notif-avatar">`
            : `<div class="avatar-initial notif-avatar" style="width:40px;height:40px;font-size:16px;">${escapeHtml(((b.GonderenAd || '?')[0] || '?'))}</div>`;
        const unread = (b.OkunduMu == 0) ? 'unread' : '';
        return `<div class="notif-item ${unread}" onclick="window.location.href='/Social/Feed'">
            ${avatar}
            <div>
                <div class="notif-text">${escapeHtml(b.Mesaj)}</div>
                <div class="notif-time">${escapeHtml(timeAgo(b.Tarih))}</div>
            </div>
        </div>`;
    }).join('');
}

async function loadNotifBadge() {
    try {
        const data = await secureFetch('/Social/GetBildirimler');
        if (data && data.success && data.okunmamis > 0) {
            const badge = document.getElementById('notifBadge');
            badge.textContent = data.okunmamis > 99 ? '99+' : data.okunmamis;
            badge.style.display = 'flex';
        } else {
            document.getElementById('notifBadge').style.display = 'none';
        }
    } catch(e) {}
}

async function markNotificationsRead() {
    const fd = new FormData();
    fd.append('__RequestVerificationToken', CSRF());
    await secureFetch('/Social/BildirimleriOku', 'POST', fd);
    document.getElementById('notifBadge').style.display = 'none';
}

// --- Global Search ---
let searchTimer = null;
function globalSearchHandler(q) {
    clearTimeout(searchTimer);
    const container = document.getElementById('globalSearchResults');
    
    if (q.length < 2) {
        container.style.display = 'none';
        return;
    }
    
    searchTimer = setTimeout(async () => {
        const data = await secureFetch(`/Social/Ara?q=${encodeURIComponent(q)}`);
        if (!data || !data.success) return;
        
        if (data.sonuclar.length === 0) {
            container.innerHTML = '<div style="padding:20px; text-align:center; color:var(--text-muted); font-size:0.85rem;">Sonuç bulunamadı</div>';
            container.style.display = 'block';
            return;
        }
        
        container.innerHTML = data.sonuclar.map(u => {
            const id = Number(u.Id) || 0;
            return `<a href="/Social/Profil/${id}" style="display:flex; align-items:center; gap:12px; padding:12px 16px; text-decoration:none; color:var(--text-primary); transition:0.2s;" onmouseover="this.style.background='var(--bg-hover)'" onmouseout="this.style.background=''">
                ${avatarHTML(u.ProfilResmi, u.AdSoyad, 38)}
                <div>
                    <div style="font-weight:700; font-size:0.9rem;">${escapeHtml(u.AdSoyad)}</div>
                    <div style="font-size:0.78rem; color:var(--text-muted);">${escapeHtml(u.Biyografi || '')}</div>
                </div>
            </a>`;
        }).join('');
        container.style.display = 'block';
    }, 250);
}

// Close search on click outside
document.addEventListener('click', (e) => {
    const search = document.getElementById('globalSearch');
    const results = document.getElementById('globalSearchResults');
    if (search && results && !search.contains(e.target) && !results.contains(e.target)) {
        results.style.display = 'none';
    }
});

// --- Sidebar Suggestions Loader ---
async function loadSuggestions() {
    try {
        const data = await secureFetch('/Social/GetKesf');
        if (!data || !data.success || !data.oneriler) return;
        
        const list = document.getElementById('suggestList');
        if (!list) return;
        
        list.innerHTML = data.oneriler.slice(0, 3).map(u => {
            const id = Number(u.Id) || 0;
            const takipci = Number(u.TakipciSayisi) || 0;
            return `
            <div class="suggest-item">
                ${avatarHTML(u.ProfilResmi, u.AdSoyad, 40)}
                <div class="suggest-info">
                    <div class="suggest-name">${escapeHtml(u.AdSoyad)}</div>
                    <div class="suggest-detail">${takipci} takipçi</div>
                </div>
                <button class="suggest-follow" onclick="quickFollow(this, ${id})">Takip Et</button>
            </div>
        `;
        }).join('');
    } catch(e) {}
}

async function quickFollow(btn, userId) {
    const fd = new FormData();
    fd.append('hedefId', userId);
    fd.append('__RequestVerificationToken', CSRF());
    const data = await secureFetch('/Social/TakipEt', 'POST', fd);
    if (data && data.success) {
        if (data.following) {
            btn.textContent = 'Takipte';
            btn.style.background = 'transparent';
            btn.style.color = 'var(--text-primary)';
            btn.style.border = '1px solid var(--border)';
        } else {
            btn.textContent = 'Takip Et';
            btn.style.background = '';
            btn.style.color = '';
            btn.style.border = '';
        }
    }
}

// --- SignalR Connection ---
let signalRConnection = null;

function initSignalR() {
    try {
        signalRConnection = new signalR.HubConnectionBuilder()
            .withUrl('/notifHub')
            .withAutomaticReconnect()
            .build();

        signalRConnection.on('ReceiveNotification', (msg, link, tip) => {
            loadNotifBadge();
            showNeonToast(msg, tip === 'begeni' ? 'fa-heart' : tip === 'yorum' ? 'fa-comment' : 'fa-bell');
        });

        signalRConnection.start().catch(err => console.log('SignalR connection failed:', err));
    } catch(e) {
        console.log('SignalR not available');
    }
}

// --- Double-tap to like ---
function enableDoubleTapLike(element, gonderiId) {
    let lastTap = 0;
    element.addEventListener('click', (e) => {
        const now = Date.now();
        if (now - lastTap < 300) {
            // Double tap detected
            triggerLike(gonderiId);
            showDoubleTapHeart(element);
        }
        lastTap = now;
    });
}

function showDoubleTapHeart(container) {
    const heart = container.querySelector('.double-tap-heart');
    if (heart) {
        heart.classList.remove('pop');
        void heart.offsetWidth; // reflow
        heart.classList.add('pop');
    }
}

// --- Like Particle Burst ---
function spawnLikeParticles(btn) {
    const colors = ['#ff4757', '#ff6b81', '#ff4757', '#c6ff00', '#ffd700'];
    for (let i = 0; i < 6; i++) {
        const p = document.createElement('div');
        p.className = 'like-particle';
        const angle = (Math.PI * 2 / 6) * i;
        const dist = 15 + Math.random() * 15;
        const dx = Math.cos(angle) * dist;
        const dy = Math.sin(angle) * dist;
        p.style.background = colors[i % colors.length];
        p.style.left = '50%';
        p.style.top = '50%';
        p.style.setProperty('--dx', dx + 'px');
        p.style.setProperty('--dy', dy + 'px');
        p.style.animation = `none`;
        
        const container = btn.querySelector('.like-particles') || btn;
        container.appendChild(p);
        
        requestAnimationFrame(() => {
            p.style.transition = '0.5s ease-out';
            p.style.transform = `translate(${dx}px, ${dy}px) scale(0)`;
            p.style.opacity = '0';
        });
        
        setTimeout(() => p.remove(), 600);
    }
}

// --- Init ---
document.addEventListener('DOMContentLoaded', () => {
    loadNotifBadge();
    loadSuggestions();
    initSignalR();
    
    // Poll for notifications
    setInterval(loadNotifBadge, 30000);
});
