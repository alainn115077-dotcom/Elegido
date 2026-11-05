// Coordenadas: "26.0508, -98.2979" o "26°03'03.0"N 98°17'52.0"W"
function esCoordenada(t) {
    t = (t || '').trim();
    return /^-?\d{1,3}(\.\d+)?\s*[, ]\s*-?\d{1,3}(\.\d+)?$/.test(t) || /\d+\s*°.*[NSns].*\d+\s*°.*[EWOeow]/.test(t);
}

// Formulario de contratación: cliente nuevo/registrado, día+mes (sin año), hora de inicio + duración,
// saldo y disponibilidad en vivo (consulta real a SQL Server)
(function () {
    'use strict';
    const $ = id => document.getElementById(id);
    const fmt = new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' });
    const pad = n => String(n).padStart(2, '0');

    // ---------- Cliente registrado / nuevo ----------
    const hidNuevo = $('NuevoCliente');
    if (hidNuevo) document.querySelectorAll('[data-modo-cliente]').forEach(btn => {
        btn.addEventListener('click', () => {
            const nuevo = btn.dataset.modoCliente === 'nuevo';
            hidNuevo.value = nuevo ? 'true' : 'false';
            $('bloqueNuevo').classList.toggle('d-none', !nuevo);
            $('bloqueExistente').classList.toggle('d-none', nuevo);
            document.querySelectorAll('[data-modo-cliente]').forEach(b => b.classList.toggle('activo', b === btn));
        });
    });

    // ---------- Reloj de 12 horas (hora, minutos, a. m. / p. m.) → campo oculto HH:mm ----------
    const selHora = $('selHora'), selMinuto = $('selMinuto');
    let esPm = !!document.querySelector('[data-ampm="pm"].activo');
    function sincronizarHora() {
        const oculto = $('HoraInicio');
        const h = parseInt(selHora.value);
        if (!h) { oculto.value = ''; }
        else {
            let h24 = h % 12 + (esPm ? 12 : 0);
            oculto.value = pad(h24) + ':' + pad(parseInt(selMinuto.value) || 0);
        }
        oculto.dispatchEvent(new Event('input', { bubbles: true }));
    }
    document.querySelectorAll('[data-ampm]').forEach(b => b.addEventListener('click', () => {
        esPm = b.dataset.ampm === 'pm';
        document.querySelectorAll('[data-ampm]').forEach(x => x.classList.toggle('activo', x === b));
        sincronizarHora();
    }));
    [selHora, selMinuto].forEach(el => el.addEventListener('change', sincronizarHora));

    // ---------- Fecha (día + mes → próxima vez que ocurra) ----------
    const selDia = $('Dia'), selMes = $('Mes'), hi = $('HoraInicio'), dur = $('DuracionHoras');
    const dOrig = parseInt($('DiaOriginal').value), mOrig = parseInt($('MesOriginal').value), aOrig = parseInt($('AnioOriginal').value);

    function resolverFecha() {
        const d = parseInt(selDia.value), m = parseInt(selMes.value);
        if (!d || !m) return null;
        if (aOrig && d === dOrig && m === mOrig) return new Date(aOrig, m - 1, d);
        const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
        for (let a = hoy.getFullYear(); a <= hoy.getFullYear() + 8; a++) {
            const f = new Date(a, m - 1, d);
            if (f.getMonth() !== m - 1) continue; // ese día no existe en ese mes/año
            if (f >= hoy) return f;
        }
        return null;
    }

    // ---------- Horario pendiente ----------
    const hidHorario = $('HorarioPendiente');
    const btnHorario = $('btnHorarioPendiente');
    const horarioPendiente = () => hidHorario.value === 'true';
    function pintarHorarioPendiente() {
        const p = horarioPendiente();
        btnHorario.classList.toggle('activo', p);
        document.querySelectorAll('.bloque-horario').forEach(b => b.classList.toggle('apagado', p));
    }
    btnHorario.addEventListener('click', () => {
        hidHorario.value = horarioPendiente() ? 'false' : 'true';
        pintarHorarioPendiente();
        actualizar();
    });
    pintarHorarioPendiente();

    function calcular() {
        if (horarioPendiente()) return null;
        const fecha = resolverFecha();
        const horas = parseFloat(dur.value);
        if (!fecha || !hi.value || !(horas > 0)) return null;
        const [h, mi] = hi.value.split(':').map(Number);
        const inicio = new Date(fecha); inicio.setHours(h, mi, 0, 0);
        const fin = new Date(inicio.getTime() + horas * 3600000);
        return { fecha, inicio, fin };
    }

    const horaTxt = d => d.toLocaleTimeString('es-MX', { hour: 'numeric', minute: '2-digit' });
    const cap = s => s.charAt(0).toUpperCase() + s.slice(1);

    function pintarHorario() {
        const txt = $('textoHorario');
        const box = $('resumenHorario');
        const fecha = resolverFecha();
        if (selDia.value && selMes.value && !fecha) {
            txt.textContent = 'Esa fecha no existe. Revisa el día y el mes.';
            box.classList.add('error'); return null;
        }
        box.classList.remove('error');
        if (fecha && horarioPendiente()) {
            txt.innerHTML = '<strong>' + cap(fecha.toLocaleDateString('es-MX', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })) + '</strong> · horario pendiente';
            return null;
        }
        const r = calcular();
        if (!r) {
            txt.textContent = fecha
                ? cap(fecha.toLocaleDateString('es-MX', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })) + ' · falta la hora de inicio y la duración.'
                : 'Elige el día, el mes, la hora de inicio y la duración.';
            return null;
        }
        const diaSig = r.fin.getDate() !== r.inicio.getDate() ? ' (del día siguiente)' : '';
        txt.innerHTML = '<strong>' + cap(r.inicio.toLocaleDateString('es-MX', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })) +
            '</strong> · de ' + horaTxt(r.inicio) + ' a <strong>' + horaTxt(r.fin) + '</strong>' + diaSig;
        return r;
    }

    document.querySelectorAll('[data-horas]').forEach(b => b.addEventListener('click', () => {
        dur.value = b.dataset.horas;
        actualizar();
    }));

    // ---------- Saldo ----------
    const precio = $('PrecioAcordado'), anticipo = $('Anticipo');
    function saldo() {
        const p = parseFloat(precio.value) || 0;
        const otros = parseFloat(($('OtrosPagos') || {}).value) || 0;
        const a = (parseFloat(anticipo ? anticipo.value : 0) || 0) + otros;
        $('resPrecio').textContent = fmt.format(p);
        $('resAnticipo').textContent = fmt.format(a);
        const s = p - a;
        const el = $('resSaldo');
        el.textContent = fmt.format(s);
        el.classList.toggle('text-rojo', s < 0);
        el.classList.toggle('text-verde', s === 0 && p > 0);
    }

    // ---------- Disponibilidad en vivo ----------
    const panel = $('disponibilidad');
    let temporizador;
    function verificar(r) {
        clearTimeout(temporizador);
        const fecha = resolverFecha();
        if (!fecha) { panel.classList.add('d-none'); return; }
        temporizador = setTimeout(async () => {
            const params = new URLSearchParams({ fecha: fecha.getFullYear() + '-' + pad(fecha.getMonth() + 1) + '-' + pad(fecha.getDate()) });
            if (r) {
                params.set('horaInicio', pad(r.inicio.getHours()) + ':' + pad(r.inicio.getMinutes()));
                params.set('horaFin', pad(r.fin.getHours()) + ':' + pad(r.fin.getMinutes()));
            }
            if (panel.dataset.excluir) params.set('excluir', panel.dataset.excluir);
            try {
                const resp = await fetch(panel.dataset.url + '?' + params.toString(), { headers: { 'Accept': 'application/json' } });
                if (!resp.ok) throw new Error();
                pintar(await resp.json(), !!r);
            } catch {
                panel.className = 'disponibilidad';
                panel.textContent = 'No se pudo verificar la disponibilidad.';
            }
        }, 300);
    }

    const esc = s => { const div = document.createElement('div'); div.textContent = s ?? ''; return div.innerHTML; };

    function pintar(d, conHorario) {
        panel.classList.remove('d-none', 'libre', 'ocupado', 'conflicto');
        let html;
        if (d.conflictos.length > 0) {
            panel.classList.add('conflicto');
            html = '<strong><i class="bi bi-x-octagon"></i> Se empalma</strong> (se requieren ' + d.margen + ' min de margen) con:<ul>' +
                d.conflictos.map(c => '<li>' + esc(c.cliente) + ' · ' + esc(c.horario) + '</li>').join('') + '</ul>' +
                '<small>Si quieres, guárdala de todos modos: se registra y en la agenda sale en amarillo.</small>';
        } else if (!d.diaLibre) {
            panel.classList.add('ocupado');
            html = '<strong><i class="bi bi-check-circle"></i> Ese día ya hay presentaciones</strong>' +
                (conHorario ? ', pero este horario no se empalma.' : '. Revisa el horario:') + '<ul>' +
                d.eventos.map(e => '<li>' + esc(e.horario) + ' · ' + esc(e.cliente) + (e.lugar ? ' (' + esc(e.lugar) + ')' : '') + ' · ' + esc(e.estado) + '</li>').join('') + '</ul>';
        } else {
            panel.classList.add('libre');
            html = '<strong><i class="bi bi-check-circle"></i> ¡Fecha disponible!</strong> No hay presentaciones registradas el ' + esc(d.fecha) + '.';
        }
        panel.innerHTML = html;
    }

    function actualizar() { verificar(pintarHorario()); }

    [selDia, selMes].forEach(el => el.addEventListener('change', actualizar));
    [hi, dur].forEach(el => el.addEventListener('input', actualizar));
    precio.addEventListener('input', saldo);
    if (anticipo) anticipo.addEventListener('input', saldo);

    actualizar(); saldo();
})();

// ---------- Vista previa del mapa (busca la dirección; si no trae ciudad, en Reynosa) ----------
(function () {
    'use strict';
    const input = document.getElementById('Direccion');
    const caja = document.getElementById('mapaPrevia');
    if (!input || !caja) return;
    const frame = document.getElementById('mapaFrame');
    const texto = document.getElementById('mapaTexto');
    const abrir = document.getElementById('mapaAbrir');
    const base = caja.dataset.ciudad;
    const ciudades = ['reynosa', 'rio bravo', 'matamoros', 'valle hermoso', 'diaz ordaz', 'camargo', 'miguel aleman', 'nuevo laredo',
        'monterrey', 'san fernando', 'mier', 'guerrero', 'mcallen', 'hidalgo', 'pharr', 'edinburg', 'mission', 'brownsville', 'tamaulipas', 'nuevo leon', 'texas'];
    const normal = s => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    const consulta = d => esCoordenada(d) || ciudades.some(c => normal(d).includes(c)) ? d : d + ', ' + base;
    let t, ultima = '';

    function actualizar() {
        const d = input.value.trim();
        if (d.length < 4) { caja.classList.add('d-none'); return; }
        const q = consulta(d);
        if (q === ultima) return;
        ultima = q;
        texto.textContent = q;
        abrir.href = 'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(q);
        frame.src = 'https://maps.google.com/maps?q=' + encodeURIComponent(q) + '&z=15&output=embed';
        caja.classList.remove('d-none');
    }
    input.addEventListener('input', () => { clearTimeout(t); t = setTimeout(actualizar, 700); });
    actualizar();
})();

// ---------- Sugerencias rápidas de precio y anticipo ----------
document.querySelectorAll('[data-llenar]').forEach(b => b.addEventListener('click', () => {
    const el = document.getElementById(b.dataset.llenar);
    if (!el) return;
    el.value = b.dataset.valor;
    el.dispatchEvent(new Event('input', { bubbles: true }));
}));

// ---------- Dirección con formato: mayúsculas y # antes del número ----------
(function () {
    'use strict';
    const input = document.getElementById('Direccion');
    if (!input) return;
    const particulas = ['de', 'del', 'y', 'e', 'en', 'a'];
    const sinGato = ['km', 'km.', 'cp', 'c.p.', 'cp.', 'no', 'no.', 'num', 'num.', 'núm', 'núm.', '#', 'n°', 'lote', 'mz', 'mz.', 'manzana', 'piso', 'int', 'int.', 'interior'];
    const minusculas = ['km', 'kms'];
    function formatear(texto) {
        if (esCoordenada(texto)) return texto.trim();
        const palabras = texto.trim().replace(/\s+/g, ' ').split(' ');
        return palabras.map((p, i) => {
            const anterior = i > 0 ? palabras[i - 1].toLowerCase().replace(/,$/, '') : '';
            if (/^\d{1,5}(-?[A-Za-z])?,?$/.test(p) && i > 0 && !sinGato.includes(anterior) && !anterior.startsWith('#'))
                return '#' + p.toUpperCase();
            if (!p || !/\p{L}/u.test(p[0])) return p;
            const baja = p.toLowerCase();
            const limpia = baja.replace(/[,.]+$/, '');
            if (minusculas.includes(limpia)) return baja;
            if (i > 0 && particulas.includes(limpia)) return baja;
            // Siglas sin vocales (SM, CTM, PRI...) van en mayúsculas
            if (limpia.length <= 4 && /^\p{L}+$/u.test(limpia) && !/[aeiouáéíóúü]/.test(limpia)) return p.toUpperCase();
            return baja.charAt(0).toUpperCase() + baja.slice(1);
        }).join(' ');
    }
    input.addEventListener('blur', () => {
        const nuevo = formatear(input.value);
        if (nuevo !== input.value) { input.value = nuevo; input.dispatchEvent(new Event('input', { bubbles: true })); }
    });
})();

// ---------- Lugar pendiente ----------
(function () {
    'use strict';
    const hid = document.getElementById('LugarPendiente');
    const btn = document.getElementById('btnLugarPendiente');
    const input = document.getElementById('Direccion');
    const mapa = document.getElementById('mapaPrevia');
    if (!hid || !btn || !input) return;
    function pintar() {
        const p = hid.value === 'true';
        btn.classList.toggle('activo', p);
        input.disabled = p;
        input.placeholder = p ? 'Lugar pendiente: lo agregas cuando el cliente te lo pase' : 'Ej. Palapa El BOP  ·  Calle Arteaga 1545';
        if (p && mapa) mapa.classList.add('d-none');
    }
    btn.addEventListener('click', () => {
        hid.value = hid.value === 'true' ? 'false' : 'true';
        pintar();
        if (hid.value === 'false') { input.focus(); input.dispatchEvent(new Event('input', { bubbles: true })); }
    });
    pintar();
})();

// ---------- Precio y anticipo automáticos según la duración ($3,500 por hora, anticipo $500) ----------
(function () {
    'use strict';
    const form = document.getElementById('formContratacion');
    const dur = document.getElementById('DuracionHoras');
    const precio = document.getElementById('PrecioAcordado');
    const anticipo = document.getElementById('Anticipo');
    if (!form || !dur || !precio) return;
    const porHoraNormal = parseFloat(form.dataset.precioHora) || 0;
    const porHoraTuba = parseFloat(form.dataset.precioTuba) || porHoraNormal;
    const sugerido = parseFloat(form.dataset.anticipo) || 0;
    const edicion = form.dataset.edicion === 'true';
    const btnTuba = document.getElementById('btnTuba');
    let conTuba = false;
    const porHoraActual = () => conTuba ? porHoraTuba : porHoraNormal;
    const pesos = n => '$' + Math.round(n).toLocaleString('es-MX');

    // Botón Tuba: cambia la tarifa por hora ($3,500 → $4,300) y los botones de precio
    function pintarTuba() {
        const h = porHoraActual();
        btnTuba.classList.toggle('activo', conTuba);
        btnTuba.setAttribute('aria-pressed', conTuba);
        const c1 = document.getElementById('chipPrecio1'), c2 = document.getElementById('chipPrecio2');
        c1.dataset.valor = h; c1.textContent = pesos(h);
        c2.dataset.valor = h * 2; c2.textContent = pesos(h * 2) + ' · 2 h';
        document.getElementById('sugPrecio1').value = h;
        document.getElementById('sugPrecio2').value = h * 2;
    }
    if (btnTuba) btnTuba.addEventListener('click', () => {
        conTuba = !conTuba;
        pintarTuba();
        precioManual = false; // al cambiar la tarifa se recalcula el precio
        aplicar();
    });

    // Si el usuario escribe el precio a mano, ya no se lo cambiamos
    let precioManual = edicion && precio.value !== '';
    precio.addEventListener('input', e => { if (e.isTrusted) precioManual = true; });

    function aplicar() {
        const horas = parseFloat(dur.value);
        const porHora = porHoraActual();
        if (!(horas > 0) || !porHora) return;
        if (!precioManual) {
            precio.value = Math.round(horas * porHora);
            precio.dispatchEvent(new Event('input', { bubbles: true }));
        }
        if (anticipo && anticipo.value === '' && sugerido > 0 && !edicion) {
            anticipo.value = sugerido;
            anticipo.dispatchEvent(new Event('input', { bubbles: true }));
        }
    }
    dur.addEventListener('input', aplicar);
    document.querySelectorAll('[data-horas]').forEach(b => b.addEventListener('click', () => setTimeout(aplicar, 0)));
    // Los botones rápidos de precio cuentan como elección manual
    document.querySelectorAll('[data-llenar="PrecioAcordado"]').forEach(b => b.addEventListener('click', () => { precioManual = true; }));
})();
