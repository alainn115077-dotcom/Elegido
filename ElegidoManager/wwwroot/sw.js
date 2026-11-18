// ELEGIDO MANAGER · service worker
// • Las páginas (agenda, finanzas…) SIEMPRE se piden al servidor para que la información esté al día,
//   y con "navigation preload" la petición sale en paralelo mientras arranca el service worker.
// • Estilos, scripts, fuentes e imágenes con versión (?v=…) se sirven directo desde el celular, sin red.
// • Si no hay internet se muestra un aviso en lugar de la página de error del navegador.
const VERSION = 'em-v4';
const OFFLINE = '/offline.html';
const PRECARGA = [OFFLINE, '/img/logo.svg', '/img/icono-192.png', '/lib/manrope/manrope-latin-wght-normal.woff2'];
const ESTATICO = /\.(css|js|svg|png|jpg|jpeg|webp|ico|woff2?)$/i;

self.addEventListener('install', e => {
    e.waitUntil(caches.open(VERSION).then(c => c.addAll(PRECARGA)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', e => {
    e.waitUntil((async () => {
        const claves = await caches.keys();
        await Promise.all(claves.filter(k => k !== VERSION).map(k => caches.delete(k)));
        if (self.registration.navigationPreload) await self.registration.navigationPreload.enable();
        await self.clients.claim();
    })());
});

// Guarda un archivo con versión y borra las versiones viejas del mismo archivo
async function guardar(cache, req, resp) {
    const url = new URL(req.url);
    if (url.search) {
        for (const k of await cache.keys()) {
            const u = new URL(k.url);
            if (u.origin === url.origin && u.pathname === url.pathname && u.search !== url.search) await cache.delete(k);
        }
    }
    await cache.put(req, resp);
}

self.addEventListener('fetch', e => {
    const req = e.request;
    if (req.method !== 'GET') return;
    const url = new URL(req.url);

    // Calendario desde CDN (solo si no está en la app): se guarda para la próxima vez
    if (url.hostname === 'cdn.jsdelivr.net') {
        e.respondWith(caches.open(VERSION).then(async cache => {
            const guardado = await cache.match(req);
            if (guardado) return guardado;
            const r = await fetch(req);
            if (r.ok || r.type === 'opaque') cache.put(req, r.clone());
            return r;
        }));
        return;
    }
    if (url.origin !== self.location.origin) return;

    // Páginas: del servidor (aprovechando la petición adelantada); sin conexión, aviso
    if (req.mode === 'navigate') {
        e.respondWith((async () => {
            try {
                const adelantada = await e.preloadResponse;
                if (adelantada) return adelantada;
                return await fetch(req);
            } catch {
                return (await caches.match(OFFLINE)) || Response.error();
            }
        })());
        return;
    }

    if (!ESTATICO.test(url.pathname) || url.pathname === '/sw.js') return;

    e.respondWith(caches.open(VERSION).then(async cache => {
        const guardado = await cache.match(req);
        // Con versión (?v=…): el contenido nunca cambia, no hace falta preguntar a la red
        if (guardado && url.search) return guardado;
        const red = fetch(req).then(r => {
            if (r.ok) guardar(cache, req, r.clone()).catch(() => { });
            return r;
        }).catch(() => guardado);
        return guardado || red;
    }));
});
