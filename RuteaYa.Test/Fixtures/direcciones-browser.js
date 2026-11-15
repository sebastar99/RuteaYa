(async () => {
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
    async function wait(check, label) {
        for (let i = 0; i < 150; i++) { if (check()) return; await sleep(50); }
        throw new Error('Tiempo de espera: ' + label);
    }
    const errors = [];
    let payload;
    const originalFetch = window.fetch;
    window.fetch = async (...args) => {
        const response = await originalFetch(...args);
        if (String(args[0]).includes('/ConfirmarDirecciones') && response.ok) payload = await response.clone().json();
        return response;
    };
    window.addEventListener('unhandledrejection', e => errors.push(String(e.reason)));
    try {
        const roots = [...document.querySelectorAll('[data-direccion-here]')];
        assert(roots.length === 2, 'Los inputs deben aparecer sin iniciar otra pantalla.');
        const type = (root, value) => { const input = root.querySelector('input'); input.value = value; input.dispatchEvent(new Event('input', { bubbles: true })); };
        type(roots[0], 'ca'); await sleep(350);
        assert(!roots[0].querySelector('[data-sugerencias] button'), 'No consultar antes de tres caracteres.');
        type(roots[0], 'cancelar0'); await sleep(550); type(roots[0], 'calle0');
        for (let i = 0; i < roots.length; i++) {
            const root = roots[i], input = root.querySelector('input');
            if (i) type(root, 'calle' + i);
            await wait(() => root.querySelector('[data-sugerencias] button'), 'sugerencias');
            input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
            input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
            await wait(() => !root.querySelector('[data-detalle-direccion]').hidden, 'lookup');
            assert(!root.querySelector('[data-confirmacion-direccion]').value, 'Lookup no debe confirmar automáticamente.');
            const access = root.querySelector('select'); access.value = i ? '0' : '-1'; access.dispatchEvent(new Event('change'));
            root.querySelector('[data-confirmar-direccion]').click();
            await wait(() => root.querySelector('[data-confirmacion-direccion]').value, 'confirmación');
        }
        const button = document.querySelector('[data-calcular-direcciones]');
        assert(!button.disabled && button.type === 'button', 'Cálculo explícito sin submit accidental.');
        const sesion = document.querySelector('[name=Sesion]').value;
        button.click(); button.click();
        assert(document.querySelector('[data-estado-calculo]').textContent.includes('Calculando'), 'Debe mostrar carga.');
        await wait(() => document.querySelector('[data-estado-calculo]').textContent.includes('Recorrido calculado'), 'cálculo AJAX');
        assert(document.querySelector('[name=Sesion]').value === sesion, 'No debe recargar ni reemplazar la sesión.');
        assert(document.querySelector('[data-resumen-recorrido]').textContent.includes('3,21'), 'Métricas del resultado en la misma página.');
        assert(document.getElementById('estado-mapa').textContent.includes('Leaflet'), 'Error de CDN debe ser visible, sin romper el cálculo.');
        // Regresión del trazado: ejecutar la misma función JS con la API Leaflet simulada,
        // pero con el JSON real que acabamos de recibir del endpoint de cálculo.
        let polylines = 0;
        const layer = () => ({ addTo() { return this; }, bindPopup() { return this; }, on() { return this; } });
        window.L = {
            map: () => ({ remove() {}, fitBounds() {}, getZoom: () => 10,
                getBounds: () => ({ getNorth: () => 90, getSouth: () => -90, getWest: () => -180, getEast: () => 180 }),
                attributionControl: { addAttribution() {}, removeAttribution() {} }, on() {} }),
            polyline: points => { assert(points.every(p => Array.isArray(p) && p.every(Number.isFinite)), 'Geometría con coordenadas numéricas.'); polylines++; return layer(); },
            marker: layer, circleMarker: layer, tileLayer: layer, featureGroup: () => ({ getBounds: () => ({ extend() {} }) })
        };
        await window.RuteaYaRecorridos.dibujar(payload);
        assert(polylines === payload.secciones.length, 'Dibujar todas las secciones del resultado AJAX.');
        assert(document.getElementById('estado-mapa').textContent.includes('Ruta dibujada'), 'Estado de dibujo correcto.');
        window.fetch = async () => ({ redirected: true, status: 200 });
        let login = false;
        try { await window.RuteaYaHere.request('/prueba'); } catch (error) { login = error.message.includes('iniciar sesión'); }
        assert(login, 'Un login redirigido debe informar sesión vencida, no error genérico.');
        window.fetch = async () => ({ redirected: false, status: 409, ok: false, headers: new Headers({ 'content-type': 'application/json' }),
            json: async () => ({ codigo: 'seleccion_vencida', mensaje: 'Renová la previsualización.', referencia: 'prueba' }) });
        let vencida = false;
        try { await window.RuteaYaHere.request('/prueba'); } catch (error) { vencida = error.message.includes('Renová'); }
        assert(vencida, 'Se debe mostrar el mensaje de sesión/token vencido.');
        window.fetch = async (...args) => String(args[0]).includes('/ConfirmarDirecciones') ? new Response(JSON.stringify({
            codigo: 'here_429', mensaje: 'Límite de solicitudes de HERE (429).', referencia: 'prueba' }),
            { status: 503, headers: { 'content-type': 'application/json' } }) : originalFetch(...args);
        button.click();
        await wait(() => document.querySelector('[data-estado-calculo]').textContent.includes('429'), 'error claro de cálculo');
        assert(!button.disabled && document.querySelector('[name=Sesion]').value === sesion, 'Un error permite reintentar, sin recarga ni bloqueo.');
        window.fetch = originalFetch;
        type(roots[0], 'permiso');
        await wait(() => roots[0].querySelector('[data-estado-direccion]').textContent.includes('403'), 'mensaje de permisos');
        assert(!roots[0].querySelector('[data-confirmacion-direccion]').value, 'Editar invalida la confirmación.');
        assert(button.disabled, 'No calcular con selección invalidada.');
        assert(errors.length === 0, 'Sin promesas rechazadas sin manejar: ' + errors.join(','));
        window.resultadoPruebaDirecciones = 'OK';
    } catch (error) { window.resultadoPruebaDirecciones = 'ERROR: ' + error.message; }
})();
