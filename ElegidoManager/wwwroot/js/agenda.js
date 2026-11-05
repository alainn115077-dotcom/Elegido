// Agenda: calendario FullCalendar alimentado desde SQL Server
(function () {
    'use strict';
    const rutas = window.EM_RUTAS;
    const el = document.getElementById('calendario');
    const chkCanceladas = document.getElementById('verCanceladas');
    const esMovil = window.matchMedia('(max-width: 575.98px)').matches;

    const pad = n => String(n).padStart(2, '0');
    const local = d => d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':00';
    const soloFecha = d => d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
    const esc = s => { const div = document.createElement('div'); div.textContent = s ?? ''; return div.innerHTML; };

    const calendario = new FullCalendar.Calendar(el, {
        locale: 'es',
        initialView: 'dayGridMonth',
        firstDay: 1,
        height: 'auto',
        nowIndicator: true,
        dayMaxEvents: 3,
        navLinks: !esMovil,
        headerToolbar: esMovil
            ? { left: 'prev,next', center: 'title', right: 'today' }
            : { left: 'prev,next today', center: 'title', right: 'dayGridMonth,timeGridWeek,timeGridDay,listMonth' },
        footerToolbar: esMovil ? { center: 'dayGridMonth,timeGridWeek,timeGridDay,listMonth' } : false,
        buttonText: { today: 'Hoy', month: 'Mes', week: 'Semana', day: 'Día', list: 'Lista' },
        eventTimeFormat: { hour: 'numeric', minute: '2-digit', meridiem: 'short', hour12: true },
        slotLabelFormat: { hour: 'numeric', minute: '2-digit', meridiem: 'short', hour12: true },
        slotMinTime: '08:00:00',
        scrollTime: '16:00:00',

        // Los eventos se piden al servidor para el rango visible
        events: function (info, ok, error) {
            const params = new URLSearchParams({ start: local(info.start), end: local(info.end), canceladas: chkCanceladas.checked });
            fetch(rutas.eventos + '?' + params.toString())
                .then(r => { if (!r.ok) throw new Error(); return r.json(); })
                .then(ok)
                .catch(() => { error(); });
        },

        // En celular, en la vista de mes cada evento es solo una marca verde; se toca el día para ver detalles
        eventContent: function (arg) {
            if (window.innerWidth < 768 && arg.view.type === 'dayGridMonth') {
                const cancelada = (arg.event.extendedProps.estado || '').toLowerCase().startsWith('cancel');
                const empalme = !!arg.event.extendedProps.empalme;
                return { html: '<span class="marca-evento' + (cancelada ? ' cancelada' : empalme ? ' empalme' : '') + '"></span>' };
            }
        },

        eventDidMount: function (arg) {
            const p = arg.event.extendedProps;
            const celda = arg.el.closest('.fc-daygrid-day');
            if (celda && !(p.estado || '').toLowerCase().startsWith('cancel')) celda.classList.add(p.empalme ? 'dia-con-empalme' : 'dia-con-evento');
            arg.el.title = (p.empalme ? '⚠ Se empalma con otra presentación\n' : '') + `${p.cliente}\n${p.horario}\n${p.lugar}` + (p.saldo ? `\nEstado: ${p.estado} · Saldo: ${p.saldo}` : '');
        },

        dateClick: function (info) {
            abrirDia(info.date);
        },

        // Al tocar un evento se abre su día (en lugar de salir de la agenda)
        eventClick: function (info) {
            info.jsEvent.preventDefault();
            abrirDia(info.event.start);
        }
    });
    calendario.render();
    chkCanceladas.addEventListener('change', () => calendario.refetchEvents());

    // ---------- Ventana del día ----------
    const modal = bootstrap.Modal.getOrCreateInstance(document.getElementById('modalDia'));

    async function abrirDia(fecha) {
        const titulo = document.getElementById('diaTitulo');
        const resumen = document.getElementById('diaResumen');
        const cont = document.getElementById('diaContenido');
        const pie = document.getElementById('diaPie');
        titulo.textContent = fecha.toLocaleDateString('es-MX', { weekday: 'long', day: 'numeric', month: 'long' });
        resumen.textContent = '';
        cont.innerHTML = '<div class="escribiendo"><span></span><span></span><span></span></div>';
        pie.innerHTML = '';
        modal.show();
        try {
            const r = await fetch(rutas.dia + '?fecha=' + soloFecha(fecha));
            const d = await r.json();
            titulo.textContent = d.titulo;
            resumen.textContent = d.eventos.length === 0 ? 'Día libre' : d.eventos.length + (d.eventos.length === 1 ? ' presentación' : ' presentaciones');
            if (d.eventos.length === 0) {
                cont.innerHTML = '<div class="dia-libre"><i class="bi bi-calendar-check"></i><strong>Día disponible</strong><span>No hay presentaciones registradas.</span></div>';
            } else {
                cont.innerHTML = '<div class="dia-eventos">' + d.eventos.map((e, i) => `
                    <article class="dia-tarjeta">
                        <div class="dia-hora ${e.inicio ? '' : 'pendiente'}">
                            ${e.inicio ? `<strong>${esc(e.inicio)}</strong><span>a ${esc(e.fin)}</span>` : '<strong>Horario</strong><span>pendiente</span>'}
                        </div>
                        <div class="dia-cuerpo">
                            <strong class="dia-cliente">${esc(e.cliente)}</strong>
                            <span class="dia-lugar"><i class="bi bi-geo-alt"></i> ${esc(e.ubicacion)}</span>
                        </div>
                        <div class="dia-acciones">
                            <button type="button" class="btn btn-icon" data-ver="${i}" title="Ver"><i class="bi bi-eye"></i></button>
                            ${e.urlEditar ? `<a href="${e.urlEditar}" class="btn btn-icon" title="Editar"><i class="bi bi-pencil"></i></a>` : ''}
                        </div>
                        <div class="dia-ficha d-none" id="ficha-${i}">
                            <div><span>Nombre</span><strong>${esc(e.cliente)}</strong></div>
                            ${e.telefono ? `<div><span>Teléfono</span><strong><a href="tel:${esc(e.telefono)}">${esc(e.telefono)}</a> <a href="${e.whatsapp}" target="_blank" rel="noopener" class="text-wa ms-1" title="WhatsApp"><i class="bi bi-whatsapp"></i></a></strong></div>` : ''}
                            <div><span>Ubicación</span><strong>${esc(e.ubicacion)} ${e.mapa ? `<a href="${e.mapa}" target="_blank" rel="noopener" class="enlace-oro ms-1">Mapa</a>` : ''}</strong></div>
                            <div><span>Hora</span><strong>${esc(e.horario)}</strong></div>
                            ${e.urlDetalle ? `<a href="${e.urlDetalle}" class="enlace-oro small mt-1">Ver contratación completa <i class="bi bi-arrow-right"></i></a>` : ''}
                        </div>
                    </article>`).join('') + '</div>';
                cont.querySelectorAll('[data-ver]').forEach(b => b.addEventListener('click', () => {
                    const ficha = document.getElementById('ficha-' + b.dataset.ver);
                    const abierta = !ficha.classList.toggle('d-none');
                    b.classList.toggle('activo', abierta);
                }));
            }
            if (!d.pasado && d.urlNueva) {
                pie.innerHTML = `<a class="btn btn-oro" href="${d.urlNueva}"><i class="bi bi-plus-lg"></i> ${d.eventos.length === 0 ? 'Registrar contratación' : 'Registrar otra en este día'}</a>`;
            }
        } catch {
            cont.innerHTML = '<p class="text-rojo mb-0">No se pudo consultar el día.</p>';
        }
    }
})();
