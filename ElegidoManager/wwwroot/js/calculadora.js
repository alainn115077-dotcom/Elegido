// Calculadora: suma, resta, multiplicación, división y porcentaje, con botones rápidos de $3,500, $7,000 y −$500.
(function () {
    'use strict';
    const exprEl = document.getElementById('calcExpresion');
    const resEl = document.getElementById('calcResultado');
    const histEl = document.getElementById('calcHistorial');
    const sinHist = document.getElementById('calcSinHistorial');
    const fmt = new Intl.NumberFormat('es-MX', { maximumFractionDigits: 2 });
    const OPS = ['+', '−', '×', '÷'];
    let expr = '';
    let recienCalculado = false;
    let ultimoResultado = 0;

    // ---------- Evaluación segura (sin eval) ----------
    function tokens(s) {
        const t = [];
        let num = '';
        for (let i = 0; i < s.length; i++) {
            const c = s[i];
            if (/[0-9.]/.test(c)) { num += c; continue; }
            if (c === '%') { if (num) { t.push({ pct: parseFloat(num) }); num = ''; } continue; }
            if (OPS.includes(c)) {
                if (num) { t.push(parseFloat(num)); num = ''; }
                // signo negativo al inicio o después de otro operador
                if (c === '−' && (t.length === 0 || OPS.includes(t[t.length - 1]))) { num = '-'; continue; }
                t.push(c);
            }
        }
        if (num && num !== '-') t.push(parseFloat(num));
        return t;
    }

    function evaluar(s) {
        const t = tokens(s);
        while (t.length && OPS.includes(t[t.length - 1])) t.pop();
        if (t.length === 0) return 0;
        // primero × y ÷
        const valor = x => (typeof x === 'object' ? x.pct / 100 : x);
        const sumas = [valor(t[0])];
        for (let i = 1; i < t.length; i += 2) {
            const op = t[i];
            const n = (op === '+' || op === '−') ? t[i + 1] : valor(t[i + 1]);
            if (op === '×') sumas[sumas.length - 1] *= n;
            else if (op === '÷') sumas[sumas.length - 1] = n === 0 ? NaN : sumas[sumas.length - 1] / n;
            else sumas.push(op, n);
        }
        let r = sumas[0];
        for (let i = 1; i < sumas.length; i += 2) {
            // "300 + 50%" suma el 50% de lo que se lleva (450), como en una calculadora normal
            const n = typeof sumas[i + 1] === 'object' ? r * sumas[i + 1].pct / 100 : sumas[i + 1];
            r = sumas[i] === '+' ? r + n : r - n;
        }
        return Math.round(r * 100) / 100;
    }

    const bonito = s => s.replace(/(\d+(\.\d+)?)/g, m => {
        const [ent, dec] = m.split('.');
        return Number(ent).toLocaleString('es-MX') + (dec !== undefined ? '.' + dec : '');
    });

    // Arriba (chico) se va escribiendo la cuenta; el número grande solo cambia al dar "="
    function pintar(cuentaTerminada) {
        exprEl.innerHTML = expr ? bonito(expr) + (cuentaTerminada ? ' =' : '') : '&nbsp;';
        resEl.textContent = isNaN(ultimoResultado) ? 'Error' : '$' + fmt.format(ultimoResultado);
        resEl.classList.toggle('negativo', ultimoResultado < 0);
    }

    function teclear(k) {
        if (recienCalculado) {
            // Después de "=": un número empieza una cuenta nueva; un operador sigue con el resultado
            if (/[0-9.%]/.test(k)) { expr = ''; ultimoResultado = 0; }
            else expr = String(ultimoResultado).replace('-', '−');
        }
        const ultimo = expr.slice(-1);
        recienCalculado = false;
        if (OPS.includes(k)) {
            if (expr === '' && k !== '−') return;
            if (OPS.includes(ultimo)) expr = expr.slice(0, -1);
        }
        if (k === '.') {
            const actual = expr.split(/[+−×÷]/).pop();
            if (actual.includes('.')) return;
            if (actual === '') expr += '0';
        }
        expr += k;
        pintar();
    }

    // Botones rápidos: +3500, +7000, -500
    function insertar(valor) {
        if (recienCalculado) expr = String(ultimoResultado).replace('-', '−');
        recienCalculado = false;
        const signo = valor[0] === '-' ? '−' : '+';
        const numero = valor.slice(1);
        if (expr === '') expr = signo === '−' ? '−' + numero : numero;
        else if (OPS.includes(expr.slice(-1))) expr = expr.slice(0, -1) + signo + numero;
        else expr += signo + numero;
        pintar();
    }

    function igual() {
        if (!expr) return;
        const r = evaluar(expr);
        if (isNaN(r)) { resEl.textContent = 'Error'; return; }
        agregarHistorial(bonito(expr), r);
        ultimoResultado = r;
        recienCalculado = true;
        pintar(true);
    }

    // ---------- Historial (solo en este navegador) ----------
    let historial = [];
    try { historial = JSON.parse(localStorage.getItem('em-calc-historial') || '[]'); } catch { historial = []; }
    function guardar() { try { localStorage.setItem('em-calc-historial', JSON.stringify(historial.slice(0, 5))); } catch { } }
    function pintarHistorial() {
        histEl.innerHTML = historial.map((h, i) =>
            `<li data-i="${i}" class="calc-hist-item"><span>${h.e} =</span><strong>$${fmt.format(h.r)}</strong></li>`).join('');
        sinHist.classList.toggle('d-none', historial.length > 0);
    }
    function agregarHistorial(e, r) {
        historial.unshift({ e, r });
        historial = historial.slice(0, 5);
        guardar(); pintarHistorial();
    }
    histEl.addEventListener('click', ev => {
        const li = ev.target.closest('[data-i]');
        if (!li) return;
        ultimoResultado = historial[li.dataset.i].r;
        expr = String(ultimoResultado).replace('-', '−');
        recienCalculado = true; pintar(true);
    });
    document.getElementById('calcLimpiarHistorial').addEventListener('click', () => { historial = []; guardar(); pintarHistorial(); });

    // ---------- Eventos ----------
    document.getElementById('calculadora').addEventListener('click', ev => {
        const b = ev.target.closest('button');
        if (!b) return;
        if (b.dataset.tecla) teclear(b.dataset.tecla);
        else if (b.dataset.insertar) insertar(b.dataset.insertar);
        else if (b.dataset.accion === 'limpiar') { expr = ''; ultimoResultado = 0; recienCalculado = false; pintar(); }
        else if (b.dataset.accion === 'borrar') { if (!recienCalculado) expr = expr.slice(0, -1); pintar(); }
        else if (b.dataset.accion === 'igual') igual();
    });

    document.addEventListener('keydown', ev => {
        if (ev.target.closest('input, textarea')) return;
        const mapa = { '*': '×', '/': '÷', '-': '−', '+': '+', 'x': '×' };
        if (/^[0-9.%]$/.test(ev.key)) teclear(ev.key);
        else if (mapa[ev.key]) teclear(mapa[ev.key]);
        else if (ev.key === 'Enter' || ev.key === '=') { ev.preventDefault(); igual(); }
        else if (ev.key === 'Backspace') { if (!recienCalculado) expr = expr.slice(0, -1); pintar(); }
        else if (ev.key === 'Escape' || ev.key.toLowerCase() === 'c') { expr = ''; ultimoResultado = 0; recienCalculado = false; pintar(); }
        else return;
        ev.preventDefault();
    });

    pintar();
    pintarHistorial();
})();
