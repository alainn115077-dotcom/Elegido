// ELEGIDO MANAGER · comportamiento general de la interfaz
(function () {
    'use strict';

    // Menú lateral en móvil
    const btnMenu = document.getElementById('btnMenu');
    const velo = document.getElementById('sidebarVelo');
    if (btnMenu) btnMenu.addEventListener('click', () => document.body.classList.toggle('menu-abierto'));
    if (velo) velo.addEventListener('click', () => document.body.classList.remove('menu-abierto'));
    const btnMenuInf = document.getElementById('btnMenuInferior');
    if (btnMenuInf) btnMenuInf.addEventListener('click', () => document.body.classList.toggle('menu-abierto'));

    // Tablas: en celular cada fila se vuelve tarjeta; cada celda toma el título de su columna
    document.querySelectorAll('table.tabla-em').forEach(tabla => {
        const titulos = Array.from(tabla.querySelectorAll('thead th')).map(th => th.textContent.trim());
        tabla.querySelectorAll('tbody tr').forEach(tr => {
            let i = 0;
            Array.from(tr.children).forEach(td => {
                const t = titulos[i] || '';
                if (!td.hasAttribute('data-label')) td.setAttribute('data-label', t);
                if (!t) td.classList.add('celda-sin-titulo');
                i += td.colSpan || 1;
            });
        });
        tabla.classList.add('tabla-apilable');
    });

    // Avisos de éxito / error
    document.querySelectorAll('.toast').forEach(t => bootstrap.Toast.getOrCreateInstance(t).show());

    // Filas clicables en tablas
    document.addEventListener('click', e => {
        const fila = e.target.closest('tr.fila-click');
        if (!fila || e.target.closest('a, button, form, input, select')) return;
        const url = fila.dataset.href;
        if (url) window.location.href = url;
    });

    // Confirmación antes de acciones destructivas
    document.addEventListener('submit', e => {
        const form = e.target;
        const msg = form.getAttribute('data-confirmar');
        if (msg && !window.confirm(msg)) e.preventDefault();
    });

    // Mensajes preparados de WhatsApp (enlaces wa.me, sin API)
    const modalEl = document.getElementById('modalWhatsApp');
    if (modalEl) {
        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        const txt = document.getElementById('waMensaje');
        const abrir = document.getElementById('waAbrir');
        let telefono = '';

        const actualizar = () => {
            abrir.href = 'https://wa.me/' + telefono + '?text=' + encodeURIComponent(txt.value);
        };
        txt.addEventListener('input', actualizar);
        abrir.addEventListener('click', () => setTimeout(() => modal.hide(), 300));

        document.addEventListener('click', e => {
            const btn = e.target.closest('[data-wa-mensaje]');
            if (!btn) return;
            e.preventDefault();
            telefono = btn.dataset.waTelefono || '';
            document.getElementById('waTitulo').textContent = btn.dataset.waTitulo || 'Mensaje de WhatsApp';
            document.getElementById('waDestino').textContent = btn.dataset.waDestino || telefono;
            txt.value = btn.dataset.waMensaje || '';
            actualizar();
            modal.show();
        });
    }
})();

// Teléfono con formato automático: 899 494 7922
document.addEventListener('input', e => {
    const el = e.target;
    if (!el.matches || !el.matches('[data-formato-telefono]')) return;
    let d = el.value.replace(/\D/g, '');
    if (d.length > 10 && d.startsWith('52')) d = d.slice(2);
    if (d.length === 11 && d.startsWith('1')) d = d.slice(1);
    d = d.slice(0, 10);
    el.value = d.length > 6 ? d.slice(0, 3) + ' ' + d.slice(3, 6) + ' ' + d.slice(6)
        : d.length > 3 ? d.slice(0, 3) + ' ' + d.slice(3) : d;
});
