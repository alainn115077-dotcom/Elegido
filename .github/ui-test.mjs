// Prueba en navegadores reales: WebKit (el motor de Safari / iPhone) y Chromium (Android).
// Uso: node .github/ui-test.mjs http://localhost:8080
import { chromium, webkit, devices } from 'playwright';

const B = process.argv[2] || 'http://localhost:8080';
const CLAVE = 'Prueba123';
const iphone = devices['iPhone 15 Pro'] ?? devices['iPhone 13'];
const android = devices['Pixel 7'] ?? devices['Pixel 5'];
let fallos = 0;
const ok = (cond, msg) => { console.log(`${cond ? '✔' : '✘'} ${msg}`); if (!cond) fallos++; };

async function abrir(browser, opciones = {}) {
    const ctx = await browser.newContext(opciones);
    const page = await ctx.newPage();
    page.errores = [];
    page.externos = new Set();
    page.on('pageerror', e => page.errores.push('pageerror: ' + e.message));
    page.on('console', m => { if (m.type() === 'error') page.errores.push('console: ' + m.text()); });
    page.on('request', r => { const u = new URL(r.url()); if (!['localhost', '127.0.0.1'].includes(u.hostname) && u.protocol.startsWith('http')) page.externos.add(u.hostname); });
    return { ctx, page };
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

async function crearUsuario(page, nombre, usuario, rol) {
    await page.goto(B + '/Usuarios/Crear');
    await page.fill('#NombreCompleto', nombre);
    await page.selectOption('#selRol', rol);
    if (await page.locator('#IntegranteId').isVisible()) await page.selectOption('#IntegranteId', { index: 1 });
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
async function revisarAgenda(browser, perfil, usuario, esAdmin, etiqueta) {
    const { ctx, page } = await abrir(browser, perfil);
    await entrar(page, usuario);
    await page.goto(B + '/Agenda');
    await page.waitForSelector('.fc-daygrid-day', { timeout: 15000 });
    const fecha = await page.evaluate(async () => {
        const d = await (await fetch('/Agenda/Eventos?start=2025-01-01T00:00:00&end=2031-01-01T00:00:00&canceladas=false')).json();
        const ev = d.find(x => (x.title || '').includes('Cliente Prueba')) || d[0];
        return ev ? ev.start.substring(0, 10) : null;
    });
    ok(!!fecha, `${etiqueta}: la contratación de prueba aparece en la agenda (${fecha})`);
    if (!fecha) { await ctx.close(); return; }

    for (let i = 0; i < 40 && (await page.locator(`td.fc-daygrid-day[data-date="${fecha}"]`).count()) === 0; i++) {
        await page.click('.fc-next-button');
    }
    const celda = page.locator(`td.fc-daygrid-day[data-date="${fecha}"]`).first();
    await celda.click();
    try { await page.waitForSelector('#modalDia .dia-tarjeta', { timeout: 6000 }); }
    catch { await celda.tap(); await page.waitForSelector('#modalDia .dia-tarjeta', { timeout: 10000 }); }

    const tarjeta = page.locator('#modalDia .dia-tarjeta', { hasText: 'Cliente Prueba' }).first();
    await tarjeta.locator('[data-ver]').click();
    const ficha = tarjeta.locator('.dia-ficha');
    await ficha.waitFor({ state: 'visible', timeout: 5000 });
    const texto = await ficha.innerText();
    console.log(`   ficha de ${etiqueta}: ${texto.replace(/\s+/g, ' ').trim()}`);
    ok(/Cliente Prueba/.test(texto) && /Ubicaci/.test(texto) && /Hora/.test(texto), `${etiqueta}: el ojito muestra nombre, ubicación y hora`);
    if (esAdmin) ok(/Tel[eé]fono/.test(texto) && /899/.test(texto), `${etiqueta}: el administrador sí ve el teléfono`);
    else ok(!/Tel[eé]fono/.test(texto) && !/899/.test(texto) && !/contrataci[oó]n completa/i.test(texto), `${etiqueta}: NO aparece el teléfono del cliente`);

    const dia = await page.evaluate(async f => (await fetch('/Agenda/Dia?fecha=' + f)).json(), fecha);
    const e = dia.eventos.find(x => x.cliente.includes('Cliente Prueba'));
    ok(esAdmin ? !!e.telefono : (!e.telefono && !e.whatsapp && !e.urlDetalle),
        `${etiqueta}: el servidor ${esAdmin ? 'sí envía' : 'NO envía'} el teléfono (${e.telefono ?? 'nada'})`);
    ok(page.errores.length === 0, `${etiqueta}: agenda sin errores de JavaScript ${page.errores.join(' | ')}`);
    await ctx.close();
}

// Recorre la app como en el celular y revisa los ajustes de rendimiento
async function recorrer(browser, perfil, nombre) {
    const { ctx, page } = await abrir(browser, perfil);
    await entrar(page, 'alain');
    const paginas = ['/', '/Agenda', '/Contrataciones', '/Clientes', '/Consultas', '/Calculadora', '/Documentos',
        '/Integrantes', '/Finanzas', '/Solicitudes', '/Solicitudes/Probar', '/Usuarios', '/Bitacora', '/Contrataciones/Crear'];
    for (const p of paginas) {
        const t0 = Date.now();
        const r = await page.goto(B + p, { waitUntil: 'load' });
        ok(r.ok(), `${nombre} ${p} carga en ${Date.now() - t0} ms`);
    }
    ok(page.errores.length === 0, `${nombre}: ninguna página con errores de JavaScript ${page.errores.join(' | ')}`);
    ok(page.externos.size === 0, `${nombre}: no depende de otros servidores (${[...page.externos].join(', ') || 'ninguno'})`);

    await page.goto(B + '/');
    const vp = await page.getAttribute('meta[name=viewport]', 'content');
    ok(/user-scalable=no/.test(vp) && /maximum-scale=1/.test(vp), `${nombre}: sin zoom por doble toque`);
    const estilo = await page.evaluate(() => {
        const t = getComputedStyle(document.querySelector('.topbar'));
        const n = getComputedStyle(document.querySelector('.nav-inferior'));
        const b = getComputedStyle(document.body);
        return { topbar: t.backdropFilter || t.webkitBackdropFilter || 'none', nav: n.backdropFilter || n.webkitBackdropFilter || 'none', fondo: b.backgroundAttachment };
    });
    ok(estilo.topbar === 'none' && estilo.nav === 'none', `${nombre}: barras sin desenfoque pesado (${estilo.topbar} / ${estilo.nav})`);
    ok(estilo.fondo !== 'fixed', `${nombre}: el fondo ya no se repinta al desplazar (${estilo.fondo})`);
    ok(await page.evaluate(async () => { await document.fonts.ready; return document.fonts.check('16px Manrope'); }), `${nombre}: fuente Manrope propia cargada`);

    // Menú: abre y llega hasta la última opción sin quedar tapada por el pie
    await page.click('#btnMenuInferior');
    await page.waitForTimeout(450);
    const ultimo = page.locator('.nav-lateral a').last();
    await ultimo.scrollIntoViewIfNeeded();
    const cajaUltimo = await ultimo.boundingBox();
    const cajaPie = await page.locator('.sidebar-pie').boundingBox();
    ok(cajaUltimo && cajaPie && cajaUltimo.y + cajaUltimo.height <= cajaPie.y + 1,
        `${nombre}: la última opción del menú ("${(await ultimo.innerText()).trim()}") se ve completa`);

    // Página de inicio: encabezados de caché para "atrás" instantáneo
    const r = await page.goto(B + '/');
    const cc = r.headers()['cache-control'] || '';
    ok(!/no-store/.test(cc), `${nombre}: la página permite "atrás" instantáneo (Cache-Control: ${cc})`);
    await ctx.close();
}

const navegadorWebkit = await webkit.launch();
const navegadorChromium = await chromium.launch();
try {
    // Usuarios de prueba (desde escritorio, como administrador)
    {
        const { ctx, page } = await abrir(navegadorChromium);
        await entrar(page, 'alain');
        await crearUsuario(page, 'Omar Prueba', 'omar', 'Colaborador');
        await crearUsuario(page, 'Andy Prueba', 'andy', 'Integrante');
        await ctx.close();
    }

    await revisarAgenda(navegadorWebkit, iphone, 'alain', true, 'iPhone · Administrador');
    await revisarAgenda(navegadorWebkit, iphone, 'omar', false, 'iPhone · Colaborador');
    await revisarAgenda(navegadorWebkit, iphone, 'andy', false, 'iPhone · Integrante');
    await revisarAgenda(navegadorChromium, android, 'omar', false, 'Android · Colaborador');

    await recorrer(navegadorWebkit, iphone, 'iPhone (WebKit)');
    await recorrer(navegadorChromium, android, 'Android (Chromium)');

    const { ctx, page } = await abrir(navegadorChromium, android);
    await page.goto(B + '/Cuenta/Login');
    ok(await page.evaluate(() => HTMLScriptElement.supports?.('speculationrules') === true), 'Android: precarga de páginas disponible (speculation rules)');
    await ctx.close();
} catch (e) {
    ok(false, 'la prueba se detuvo: ' + e.message);
} finally {
    await navegadorWebkit.close();
    await navegadorChromium.close();
}

console.log(fallos === 0 ? '\nTODO BIEN' : `\n${fallos} problema(s)`);
process.exit(fallos === 0 ? 0 : 1);
