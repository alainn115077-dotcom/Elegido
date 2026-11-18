// Prueba en navegadores reales: WebKit (el motor de Safari / iPhone) y Chromium (Android).
// Uso: node ui-test.mjs http://localhost:8080   (las capturas quedan en ./capturas)
import { chromium, webkit, devices } from 'playwright';
import { mkdirSync } from 'node:fs';

const B = process.argv[2] || 'http://localhost:8080';
const CLAVE = 'Prueba123';
const iphone = devices['iPhone 15 Pro'] ?? devices['iPhone 13'];
const android = devices['Pixel 7'] ?? devices['Pixel 5'];
mkdirSync('capturas', { recursive: true });

let fallos = 0;
const ok = (cond, msg) => { console.log(`${cond ? '✔' : '✘'} ${msg}`); if (!cond) fallos++; };
const nombreArchivo = s => s.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^\w]+/g, '-').replace(/^-|-$/g, '').toLowerCase();

async function abrir(browser, opciones = {}) {
    const ctx = await browser.newContext(opciones);
    const page = await ctx.newPage();
    page.errores = [];
    page.externos = new Set();
    page.on('pageerror', e => page.errores.push('pageerror: ' + e.message));
    page.on('console', m => { if (m.type() === 'error') page.errores.push('console: ' + m.text()); });
    page.on('request', r => {
        const u = new URL(r.url());
        if (u.protocol.startsWith('http') && !['localhost', '127.0.0.1'].includes(u.hostname)) page.externos.add(u.hostname);
    });
    return { ctx, page };
}

// Cada parte corre por separado: si una falla, las demás siguen y queda una captura de pantalla
async function parte(nombre, browser, perfil, fn) {
    console.log(`\n— ${nombre}`);
    const { ctx, page } = await abrir(browser, perfil);
    try { await fn(page); }
    catch (e) {
        ok(false, `${nombre}: se detuvo → ${String(e.message).split('\n')[0]}`);
        await page.screenshot({ path: `capturas/error-${nombreArchivo(nombre)}.png`, fullPage: true }).catch(() => { });
    } finally { await ctx.close(); }
}

async function entrar(page, usuario) {
    await page.goto(B + '/Cuenta/Login');
    await page.fill('#Usuario', usuario);
    await page.fill('#Contrasena', CLAVE);
    await Promise.all([
        page.waitForURL(u => !new URL(u).pathname.startsWith('/Cuenta/Login'), { timeout: 15000 }),
        page.click('button[type=submit]')
    ]);
}

async function crearUsuario(page, nombre, usuario, rol, integrante) {
    await page.goto(B + '/Usuarios/Crear');
    await page.fill('#NombreCompleto', nombre);
    await page.selectOption('#selRol', rol);
    if (await page.locator('#IntegranteId').isVisible())
        await page.selectOption('#IntegranteId', { label: integrante }).catch(() => page.selectOption('#IntegranteId', { index: 1 }));
    await page.fill('#Usuario', usuario);
    await page.fill('#Contrasena', CLAVE);
    await page.fill('#ConfirmarContrasena', CLAVE);
    await Promise.all([
        page.waitForURL(u => !new URL(u).pathname.endsWith('/Crear'), { timeout: 15000 }),
        page.getByRole('button', { name: /Crear usuario/ }).click()
    ]);
    ok(true, `usuario "${usuario}" (${rol}) creado`);
}

// Abre la agenda, toca el día de la contratación de prueba y luego el ojito
async function revisarAgenda(page, usuario, esAdmin, etiqueta) {
    await entrar(page, usuario);
    await page.goto(B + '/Agenda');
    await page.waitForSelector('.fc-daygrid-day', { timeout: 15000 });
    const fecha = await page.evaluate(async () => {
        const d = await (await fetch('/Agenda/Eventos?start=2025-01-01T00:00:00&end=2031-01-01T00:00:00&canceladas=false')).json();
        const ev = d.find(x => (x.title || '').includes('Cliente Prueba'));
        return ev ? ev.start.substring(0, 10) : null;
    });
    ok(!!fecha, `${etiqueta}: la contratación de prueba aparece en la agenda (${fecha})`);
    if (!fecha) return;

    for (let i = 0; i < 40 && (await page.locator(`td.fc-daygrid-day[data-date="${fecha}"]`).count()) === 0; i++)
        await page.click('.fc-next-button');
    const celda = page.locator(`td.fc-daygrid-day[data-date="${fecha}"]`).first();
    await celda.click();
    try { await page.waitForSelector('#modalDia .dia-tarjeta', { timeout: 6000 }); }
    catch { await celda.tap(); await page.waitForSelector('#modalDia .dia-tarjeta', { timeout: 10000 }); }

    const tarjeta = page.locator('#modalDia .dia-tarjeta', { hasText: 'Cliente Prueba' }).first();
    await tarjeta.locator('[data-ver]').click();
    const ficha = tarjeta.locator('.dia-ficha');
    await ficha.waitFor({ state: 'visible', timeout: 5000 });
    await ficha.scrollIntoViewIfNeeded();
    await page.waitForTimeout(400);
    await page.screenshot({ path: `capturas/agenda-${nombreArchivo(etiqueta)}.png` });
    const texto = (await ficha.innerText()).replace(/\s+/g, ' ').trim();
    console.log(`   lo que muestra el ojito: ${texto}`);

    ok(/cliente prueba/i.test(texto) && /ubicaci/i.test(texto) && /hora/i.test(texto), `${etiqueta}: el ojito muestra nombre, ubicación y hora`);
    if (esAdmin) ok(/tel[eé]fono/i.test(texto) && /899/.test(texto), `${etiqueta}: el administrador sí ve el teléfono`);
    else ok(!/tel[eé]fono/i.test(texto) && !/899/.test(texto) && !/contrataci[oó]n completa/i.test(texto), `${etiqueta}: NO aparece el teléfono del cliente`);

    const dia = await page.evaluate(async f => (await fetch('/Agenda/Dia?fecha=' + f)).json(), fecha);
    const e = dia.eventos.find(x => x.cliente.includes('Cliente Prueba'));
    ok(esAdmin ? !!e.telefono : (!e.telefono && !e.whatsapp && !e.urlDetalle),
        `${etiqueta}: el servidor ${esAdmin ? 'sí envía' : 'NO envía'} el teléfono (${e.telefono ?? 'nada'})`);
    ok(page.errores.length === 0, `${etiqueta}: agenda sin errores de JavaScript ${page.errores.join(' | ')}`);
}

// Recorre la app como en el celular y revisa los ajustes de rendimiento
async function recorrer(page, nombre) {
    await entrar(page, 'alain');
    const paginas = ['/', '/Agenda', '/Contrataciones', '/Contrataciones/Crear', '/Clientes', '/Consultas', '/Calculadora',
        '/Documentos', '/Integrantes', '/Finanzas', '/Finanzas/PorEvento', '/Finanzas/Gastos', '/Solicitudes', '/Solicitudes/Probar',
        '/Usuarios', '/Bitacora', '/Cuenta/CambiarContrasena'];
    for (const p of paginas) {
        const t0 = Date.now();
        const r = await page.goto(B + p, { waitUntil: 'load' });
        const ms = Date.now() - t0;
        const ancho = await page.evaluate(() => {
            const w = document.documentElement.clientWidth;
            const fuera = [...document.querySelectorAll('body *')]
                .filter(el => { const c = el.getBoundingClientRect(); return c.width > 0 && c.right > w + 1 && getComputedStyle(el).position !== 'fixed'; })
                .slice(0, 4).map(el => el.tagName.toLowerCase() + (typeof el.className === 'string' && el.className.trim() ? '.' + el.className.trim().split(/\s+/).join('.') : ''));
            return { sobra: document.documentElement.scrollWidth - w, fuera };
        });
        ok(r.ok(), `${nombre} ${p} carga en ${ms} ms`);
        ok(ancho.sobra <= 1, `${nombre} ${p} cabe a lo ancho${ancho.sobra > 1 ? ` (se sale ${ancho.sobra}px: ${ancho.fuera.join(', ')})` : ''}`);
        if (ancho.sobra > 1) await page.screenshot({ path: `capturas/ancho-${nombreArchivo(nombre + p)}.png`, fullPage: true });
    }
    ok(page.errores.length === 0, `${nombre}: ninguna página con errores de JavaScript ${page.errores.join(' | ')}`);
    ok(page.externos.size === 0, `${nombre}: no depende de otros servidores (${[...page.externos].join(', ') || 'ninguno'})`);

    await page.goto(B + '/');
    const vp = await page.getAttribute('meta[name=viewport]', 'content');
    ok(/user-scalable=no/.test(vp) && /maximum-scale=1/.test(vp), `${nombre}: sin zoom por doble toque`);
    const estilo = await page.evaluate(async () => {
        const css = el => { const s = getComputedStyle(document.querySelector(el)); return s.backdropFilter || s.webkitBackdropFilter || 'none'; };
        await document.fonts.ready;
        return {
            topbar: css('.topbar'), nav: css('.nav-inferior'),
            fondo: getComputedStyle(document.body).backgroundAttachment,
            fuente: [...document.fonts].filter(f => f.family.replace(/["']/g, '') === 'Manrope').map(f => f.status)
        };
    });
    ok(estilo.topbar === 'none' && estilo.nav === 'none', `${nombre}: barras sin desenfoque pesado (${estilo.topbar} / ${estilo.nav})`);
    ok(estilo.fondo !== 'fixed', `${nombre}: el fondo ya no se repinta al desplazar (${estilo.fondo})`);
    ok(estilo.fuente.includes('loaded'), `${nombre}: fuente Manrope propia cargada (${estilo.fuente.join(', ') || 'no declarada'})`);

    // Menú: abre y la última opción se ve completa
    await page.click('#btnMenuInferior');
    await page.waitForTimeout(450);
    const ultimo = page.locator('.nav-lateral a').last();
    await ultimo.scrollIntoViewIfNeeded();
    const cajaUltimo = await ultimo.boundingBox();
    const cajaPie = await page.locator('.sidebar-pie').boundingBox();
    ok(cajaUltimo && cajaPie && cajaUltimo.y + cajaUltimo.height <= cajaPie.y + 1,
        `${nombre}: la última opción del menú ("${(await ultimo.innerText()).trim()}") se ve completa`);
    await page.screenshot({ path: `capturas/menu-${nombreArchivo(nombre)}.png` });

    // "Atrás" instantáneo: la página no debe llevar no-store
    const r = await page.goto(B + '/');
    const cc = r.headers()['cache-control'] || '';
    ok(!/no-store/.test(cc), `${nombre}: la página permite "atrás" instantáneo (Cache-Control: ${cc})`);
    await page.screenshot({ path: `capturas/inicio-${nombreArchivo(nombre)}.png` });
}

const navegadorWebkit = await webkit.launch();
const navegadorChromium = await chromium.launch();
try {
    await parte('Crear usuarios de prueba', navegadorChromium, {}, async page => {
        await entrar(page, 'alain');
        await crearUsuario(page, 'Omar Prueba', 'omar', 'Colaborador', 'Omar');
        await crearUsuario(page, 'Andy Prueba', 'andy', 'Integrante', 'Andy');
    });
    await parte('iPhone · Administrador', navegadorWebkit, iphone, p => revisarAgenda(p, 'alain', true, 'iPhone · Administrador'));
    await parte('iPhone · Colaborador', navegadorWebkit, iphone, p => revisarAgenda(p, 'omar', false, 'iPhone · Colaborador'));
    await parte('iPhone · Integrante', navegadorWebkit, iphone, p => revisarAgenda(p, 'andy', false, 'iPhone · Integrante'));
    await parte('Android · Integrante', navegadorChromium, android, p => revisarAgenda(p, 'andy', false, 'Android · Integrante'));
    await parte('Recorrido iPhone (WebKit)', navegadorWebkit, iphone, p => recorrer(p, 'iPhone'));
    await parte('Recorrido Android (Chromium)', navegadorChromium, android, async p => {
        await recorrer(p, 'Android');
        ok(await p.evaluate(() => HTMLScriptElement.supports?.('speculationrules') === true), 'Android: precarga de páginas disponible (speculation rules)');
    });
} finally {
    await navegadorWebkit.close();
    await navegadorChromium.close();
}

console.log(fallos === 0 ? '\nTODO BIEN' : `\n${fallos} problema(s)`);
process.exit(fallos === 0 ? 0 : 1);
