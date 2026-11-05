// Consultas rápidas: interfaz tipo chat SIN inteligencia artificial.
// Cada botón envía una clave predefinida; el servidor ejecuta la consulta SQL y responde con datos reales.
(function () {
    'use strict';
    const ruta = window.EM_RUTAS.responder;
    const mensajes = document.getElementById('mensajes');
    const entradaFecha = document.getElementById('entradaFecha');
    const entradaMes = document.getElementById('entradaMes');
    const inputFecha = document.getElementById('inputFecha');
    const inicial = mensajes.innerHTML;
    let pendiente = null; // pregunta que espera fecha o mes
    let ocupado = false;

    const esc = s => { const d = document.createElement('div'); d.textContent = s ?? ''; return d.innerHTML; };
    const bajar = () => { mensajes.scrollTop = mensajes.scrollHeight; };

    function burbujaUsuario(texto) {
        mensajes.insertAdjacentHTML('beforeend', `<div class="msg usuario"><div class="burbuja">${esc(texto)}</div></div>`);
        bajar();
    }

    function escribiendo() {
        const div = document.createElement('div');
        div.className = 'msg sistema';
        div.innerHTML = '<div class="burbuja"><div class="escribiendo"><span></span><span></span><span></span></div></div>';
        mensajes.appendChild(div);
        bajar();
        return div;
    }

    function cerrarEntradas() {
        entradaFecha.classList.add('d-none');
        entradaMes.classList.add('d-none');
        pendiente = null;
    }

    // ---------- Construcción de la respuesta ----------
    const iconoTono = { exito: 'bi-check-circle-fill', alerta: 'bi-exclamation-circle-fill', info: 'bi-info-circle-fill', vacio: 'bi-dash-circle' };

    function renderRespuesta(r) {
        let h = `<p class="msg-titulo"><i class="bi ${iconoTono[r.tono] || 'bi-info-circle-fill'}"></i>${esc(r.titulo)}</p>`;
        (r.parrafos || []).forEach(p => { h += `<p>${esc(p)}</p>`; });

        if (r.datos && r.datos.length) {
            h += '<div class="msg-datos">' + r.datos.map(d =>
                `<div class="dato"><span>${esc(d.etiqueta)}</span><strong class="${esc(d.clase || '')}">${esc(d.valor)}</strong></div>`).join('') + '</div>';
        }

        if (r.dias && r.dias.length) {
            h += '<div class="mini-cal">' + ['L', 'M', 'M', 'J', 'V', 'S', 'D'].map(x => `<span class="mc-cab">${x}</span>`).join('');
            for (let i = 0; i < r.desfaseCalendario; i++) h += '<span class="mc-dia vacio"></span>';
            r.dias.forEach(d => {
                const cls = ['mc-dia', d.libre ? 'libre' : 'ocupado', d.finDeSemana ? 'finde' : '', d.pasado ? 'pasado' : ''].join(' ');
                h += d.url
                    ? `<a class="${cls}" href="${d.url}" title="Registrar contratación el ${d.fecha}">${d.numero}</a>`
                    : `<span class="${cls}">${d.numero}</span>`;
            });
            h += '</div>';
        }

        if (r.items && r.items.length) {
            h += '<div class="msg-items">' + r.items.map(it => {
                const fecha = it.fechaDia ? `<span class="item-fecha"><b>${esc(it.fechaDia)}</b><small>${esc(it.fechaMes)}</small></span>` : '';
                const cuerpo = `<span class="item-cuerpo"><strong>${esc(it.titulo)}</strong>` +
                    (it.subtitulo ? `<small>${esc(it.subtitulo)}</small>` : '') +
                    (it.detalle ? `<small>${esc(it.detalle)}</small>` : '') + '</span>';
                const lado = '<span class="item-lado">' +
                    (it.etiqueta ? `<span class="estado ${esc(it.etiquetaClase || '')}"><span class="punto"></span>${esc(it.etiqueta)}</span>` : '') +
                    (it.monto ? `<span class="monto">${esc(it.monto)}</span>` : '') + '</span>';
                const wa = it.whatsApp ? `<a class="item-wa" href="${it.whatsApp}" target="_blank" rel="noopener" title="Enviar recordatorio por WhatsApp"><i class="bi bi-whatsapp"></i></a>` : '';
                return it.url
                    ? `<div class="item-c">${fecha}<a href="${it.url}" class="item-cuerpo-link d-flex align-items-center gap-3 flex-fill text-reset" style="min-width:0">${cuerpo}${lado}</a>${wa}</div>`
                    : `<div class="item-c">${fecha}${cuerpo}${lado}${wa}</div>`;
            }).join('') + '</div>';
        }

        if (r.acciones && r.acciones.length) {
            h += '<div class="msg-acciones">' + r.acciones.map(a =>
                `<a href="${a.url}" class="btn btn-sm ${a.principal ? 'btn-oro' : 'btn-ghost'}" ${a.url.startsWith('http') ? 'target="_blank" rel="noopener"' : ''}><i class="bi ${esc(a.icono)}"></i> ${esc(a.texto)}</a>`).join('') + '</div>';
        }
        return h;
    }

    // ---------- Envío de la pregunta ----------
    async function preguntar(clave, textoUsuario, extra) {
        if (ocupado) return;
        ocupado = true;
        cerrarEntradas();
        if (textoUsuario) burbujaUsuario(textoUsuario);
        const espera = escribiendo();

        const params = new URLSearchParams({ clave });
        if (extra && extra.fecha) params.set('fecha', extra.fecha);
        if (extra && extra.mes) params.set('mes', extra.mes);

        try {
            const inicio = Date.now();
            const resp = await fetch(ruta + '?' + params.toString(), { headers: { 'Accept': 'application/json' } });
            const r = await resp.json();
            // Pequeña pausa para que la animación se perciba natural
            const resta = 450 - (Date.now() - inicio);
            if (resta > 0) await new Promise(res => setTimeout(res, resta));

            if (!textoUsuario && r.pregunta) {
                espera.insertAdjacentHTML('beforebegin', `<div class="msg usuario"><div class="burbuja">${esc(r.pregunta)}</div></div>`);
            }
            espera.classList.add('tono-' + (r.tono || 'info'));
            espera.querySelector('.burbuja').innerHTML = renderRespuesta(r);
        } catch {
            espera.classList.add('tono-alerta');
            espera.querySelector('.burbuja').innerHTML = '<p class="msg-titulo"><i class="bi bi-wifi-off"></i>No pude consultar la base de datos</p><p>Revisa la conexión e intenta de nuevo.</p>';
        } finally {
            ocupado = false;
            bajar();
        }
    }

    // ---------- Selección de pregunta ----------
    function seleccionar(btn) {
        const clave = btn.dataset.clave;
        const requiere = btn.dataset.requiere;
        if (requiere === 'fecha') {
            pendiente = clave;
            entradaMes.classList.add('d-none');
            entradaFecha.classList.remove('d-none');
            inputFecha.focus();
            if (inputFecha.showPicker) { try { inputFecha.showPicker(); } catch { /* algunos navegadores no lo permiten */ } }
        } else if (requiere === 'mes') {
            pendiente = clave;
            entradaFecha.classList.add('d-none');
            entradaMes.classList.remove('d-none');
        } else {
            preguntar(clave, btn.dataset.texto);
        }
        if (window.matchMedia('(max-width: 1199.98px)').matches) document.querySelector('.chat').scrollIntoView({ behavior: 'smooth' });
    }

    document.querySelectorAll('[data-clave]').forEach(b => b.addEventListener('click', () => seleccionar(b)));

    document.getElementById('btnEnviarFecha').addEventListener('click', () => {
        if (!pendiente || !inputFecha.value) return;
        // El servidor arma el texto de la pregunta con la fecha elegida
        preguntar(pendiente, null, { fecha: inputFecha.value });
    });
    inputFecha.addEventListener('keydown', e => { if (e.key === 'Enter') document.getElementById('btnEnviarFecha').click(); });

    entradaMes.querySelectorAll('[data-mes]').forEach(b => b.addEventListener('click', () => {
        if (!pendiente) return;
        preguntar(pendiente, null, { mes: b.dataset.mes });
    }));

    document.querySelectorAll('[data-cerrar-entrada]').forEach(b => b.addEventListener('click', cerrarEntradas));
    document.getElementById('btnLimpiar').addEventListener('click', () => { mensajes.innerHTML = inicial; cerrarEntradas(); });
})();
