/* Reutilizable: URLs autorizadas por el consumidor, tokens opacos, ningún acceso directo a HERE. */
document.querySelectorAll('[data-direccion-here]').forEach(root => {
    const input = root.querySelector('[data-texto-direccion]');
    const list = root.querySelector('[data-sugerencias]');
    const status = root.querySelector('[data-estado-direccion]');
    const detail = root.querySelector('[data-detalle-direccion]');
    const summary = root.querySelector('[data-resumen-direccion]');
    const access = root.querySelector('[data-acceso-direccion]');
    const confirm = root.querySelector('[data-confirmar-direccion]');
    const hidden = root.querySelector('[data-confirmacion-direccion]');
    let timer, controller, version = 0, resolved = '';
    const changed = () => root.dispatchEvent(new CustomEvent('direccion:estado', { bubbles: true, detail: { confirmada: !!hidden.value } }));
    function invalidate() { hidden.value = ''; changed(); }
    function cancel() { clearTimeout(timer); controller?.abort(); version++; }
    async function request(url, fields, signal) {
        const options = { signal, credentials: 'same-origin', cache: 'no-store' };
        if (fields) {
            options.method = 'POST';
            options.headers = { 'RequestVerificationToken': root.closest('form')?.querySelector('[name="__RequestVerificationToken"]')?.value ?? '' };
            options.body = new URLSearchParams(fields);
        }
        const response = await fetch(url, options);
        if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('consulta');
        return response.json();
    }
    function begin() { cancel(); controller = new AbortController(); return { current: version, signal: controller.signal }; }
    async function choose(token) {
        const { current, signal } = begin();
        invalidate(); resolved = ''; detail.hidden = true; list.replaceChildren();
        status.textContent = 'Resolviendo la sugerencia con HERE…';
        try {
            const result = await request(root.dataset.resolverUrl, { token }, signal);
            if (current !== version) return;
            resolved = result.token;
            const u = result.ubicacion;
            input.value = u.direccionNormalizada;
            summary.textContent = `${u.direccionNormalizada} · HERE ${u.idHere} · Tipo ${u.tipoResultado} · Puerta ${u.tipoNumeroPuerta ?? 'sin información'} · ${u.posicionEstimada || u.tipoNumeroPuerta === 'interpolated' ? 'posición estimada/interpolada' : 'posición informada'}.`;
            access.replaceChildren(new Option('Seleccioná position o access', ''));
            access.add(new Option(`Position (referencia): ${u.posicion.latitud}, ${u.posicion.longitud}`, '-1'));
            u.accesos.forEach((p, i) => access.add(new Option(`Access ${i + 1} (navegación): ${p.latitud}, ${p.longitud}`, String(i))));
            access.disabled = false; confirm.disabled = true; detail.hidden = false;
            status.textContent = 'Ubicación resuelta, todavía sin confirmar. Elegí position o un access.';
            access.focus();
        } catch (error) {
            if (current === version && error.name !== 'AbortError') status.textContent = 'No se pudo resolver la sugerencia. Buscá nuevamente; no se confirmó ninguna ubicación.';
        }
    }
    input.addEventListener('input', () => {
        cancel(); invalidate(); resolved = ''; detail.hidden = true; confirm.disabled = true; list.replaceChildren();
        const text = input.value.trim();
        status.textContent = text.length < 3 ? 'Escribí al menos 3 caracteres.' : 'Buscando sugerencias…';
        if (text.length < 3 || text.length > 200) return;
        const current = version;
        timer = setTimeout(async () => {
            controller = new AbortController();
            try {
                const url = new URL(root.dataset.sugerenciasUrl, window.location.href);
                url.searchParams.set('texto', text);
                const items = await request(url, null, controller.signal);
                if (current !== version) return;
                list.replaceChildren();
                items.forEach(item => {
                    const li = document.createElement('li'), button = document.createElement('button');
                    button.type = 'button';
                    button.textContent = `${item.sugerencia.nombre} — ${item.sugerencia.localidad || 'Localidad no informada'} (${item.sugerencia.direccion})`;
                    button.addEventListener('click', () => choose(item.token));
                    li.append(button); list.append(li);
                });
                status.textContent = items.length ? 'Elegí una sugerencia. Ninguna está confirmada.' : 'No se encontraron sugerencias. Agregá calle, número y localidad.';
            } catch (error) {
                if (current === version && error.name !== 'AbortError') status.textContent = 'No se pudieron buscar sugerencias. Revisá el servicio o renová la previsualización.';
            }
        }, 300);
    });
    input.addEventListener('keydown', e => {
        if (e.key === 'ArrowDown') { const first = list.querySelector('button'); if (first) { e.preventDefault(); first.focus(); } }
        if (e.key === 'Escape') { cancel(); list.replaceChildren(); }
    });
    access.addEventListener('change', () => { cancel(); invalidate(); confirm.disabled = !resolved || access.value === ''; status.textContent = 'Punto pendiente de confirmación.'; });
    confirm.addEventListener('click', async () => {
        if (!resolved || access.value === '') return;
        const { current, signal } = begin();
        invalidate(); confirm.disabled = true;
        try {
            const result = await request(root.dataset.confirmarUrl, { token: resolved, acceso: access.value }, signal);
            if (current !== version) return;
            if (!result.confirmada) throw new Error('confirmacion');
            hidden.value = result.token;
            status.textContent = `Ubicación confirmada: ${result.ubicacion.direccionNormalizada} · ${result.acceso === -1 ? 'position' : 'access ' + (result.acceso + 1)}.`;
            changed();
        } catch (error) {
            if (current === version && error.name !== 'AbortError') { status.textContent = 'No se pudo confirmar la ubicación. Volvé a seleccionar.'; confirm.disabled = false; }
        }
    });
    // No restaurar confirmaciones de un formulario recuperado de la caché del navegador.
    window.addEventListener('pageshow', () => { cancel(); invalidate(); resolved = ''; detail.hidden = true; });
});
document.querySelectorAll('[data-recorrido-autocompletado]').forEach(form => {
    const button = form.querySelector('[data-calcular-direcciones]');
    const ready = () => [...form.querySelectorAll('[data-confirmacion-direccion]')].every(i => i.value);
    const update = () => { button.disabled = !ready(); };
    form.addEventListener('direccion:estado', update);
    form.addEventListener('submit', e => { if (!ready()) e.preventDefault(); });
    update();
});
