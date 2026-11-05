// ELEGIDO MANAGER · service worker
// Carga más rápido los estilos, scripts e imágenes y muestra un aviso si no hay internet.
// Las páginas (agenda, finanzas…) SIEMPRE se piden al servidor para que la información esté al día.
const VERSION = 'em-v2';
const OFFLINE = '/offline.html';
const PRECARGA = [OFFLINE, '/img/logo.svg', '/img/icono-192.png'];

self.addEventListener('install', e => {
    e.waitUntil(caches.open(VERSION).then(c => c.addAll(PRECARGA)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', e => {
    e.waitUntil(caches.keys()
        .then(keys => Promise.all(keys.filter(k => k !== VERSION).map(k => caches.delete(k))))
        .then(() => self.clients.claim()));
});

self.addEventListener('fetch', e => {
    const req = e.request;
    if (req.method !== 'GET') return;
    const url = new URL(req.url);
    if (url.origin !== self.location.origin) return;

    // Páginas: siempre del servidor; si no hay conexión, aviso
    if (req.mode === 'navigate') {
        e.respondWith(fetch(req).catch(() => caches.match(OFFLINE)));
        return;
    }

    // Archivos estáticos: rápido desde caché y se actualiza en segundo plano
    if (/\.(css|js|svg|png|jpg|jpeg|webp|ico|woff2?)$/i.test(url.pathname)) {
        e.respondWith(caches.open(VERSION).then(async cache => {
            const guardado = await cache.match(req);
            const red = fetch(req).then(r => { if (r.ok) cache.put(req, r.clone()); return r; }).catch(() => guardado);
            return guardado || red;
        }));
    }
});
