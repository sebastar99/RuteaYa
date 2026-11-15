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
    let timer, controller, version = 0, resolved = '', active = -1;
    const changed = () => root.dispatchEvent(new CustomEvent('direccion:estado', { bubbles: true, detail: { confirmada: !!hidden.value } }));
    function invalidate() { hidden.value = ''; changed(); }
    function cancel() { clearTimeout(timer); controller?.abort(); version++; }
    async function request(url, fields, signal) {
        return window.RuteaYaHere.request(url, fields, signal, root.closest('form'));
    }
    function begin() { cancel(); controller = new AbortController(); return { current: version, signal: controller.signal }; }
    async function choose(token) {
        const { current, signal } = begin();
        invalidate(); resolved = ''; detail.hidden = true; list.replaceChildren(); active = -1;
        input.setAttribute('aria-expanded', 'false');
        input.removeAttribute('aria-activedescendant');
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
            if (current === version && error.name !== 'AbortError') status.textContent = window.RuteaYaHere.message(error);
        }
    }
    input.addEventListener('input', () => {
        cancel(); invalidate(); resolved = ''; detail.hidden = true; confirm.disabled = true; list.replaceChildren(); active = -1;
        input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant');
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
                active = -1;
                items.forEach(item => {
                    const li = document.createElement('li'), button = document.createElement('button');
                    li.setAttribute('role', 'option'); li.id = `${input.id}-opcion-${list.children.length}`;
                    button.type = 'button';
                    button.textContent = `${item.sugerencia.nombre} — ${item.sugerencia.localidad || 'Localidad no informada'} (${item.sugerencia.direccion})`;
                    button.addEventListener('click', () => choose(item.token));
                    li.append(button); list.append(li);
                });
                input.setAttribute('aria-expanded', items.length ? 'true' : 'false');
                status.textContent = items.length ? 'Elegí una sugerencia. Ninguna está confirmada.' : 'No se encontraron sugerencias. Agregá calle, número y localidad.';
            } catch (error) {
                if (current === version && error.name !== 'AbortError') status.textContent = window.RuteaYaHere.message(error);
            }
        }, 300);
    });
    input.addEventListener('keydown', e => {
        const items = [...list.children];
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            if (!items.length) return;
            active = active < 0 ? (e.key === 'ArrowDown' ? 0 : items.length - 1) :
                (active + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
            items.forEach((item, i) => item.setAttribute('aria-selected', String(i === active)));
            input.setAttribute('aria-activedescendant', items[active].id);
        }
        if (e.key === 'Enter') { e.preventDefault(); if (active >= 0) items[active]?.querySelector('button').click(); }
        if (e.key === 'Escape') { cancel(); list.replaceChildren(); active = -1; input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant'); }
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
            if (current === version && error.name !== 'AbortError') { status.textContent = window.RuteaYaHere.message(error); confirm.disabled = false; }
        }
    });
    // No restaurar confirmaciones de un formulario recuperado de la caché del navegador.
    window.addEventListener('pageshow', () => { cancel(); invalidate(); resolved = ''; detail.hidden = true; });
});
document.querySelectorAll('[data-recorrido-autocompletado]').forEach(form => {
    const button = form.querySelector('[data-calcular-direcciones]');
    const status = form.querySelector('[data-estado-calculo]');
    let calculating = false;
    const ready = () => [...form.querySelectorAll('[data-confirmacion-direccion]')].every(i => i.value);
    const update = () => {
        button.disabled = calculating || !ready();
        if (!calculating && !ready()) {
            document.getElementById('resultado-recorrido').hidden = true;
            status.textContent = 'Elegí y confirmá todas las ubicaciones antes de calcular.';
        }
    };
    form.addEventListener('direccion:estado', update);
    form.addEventListener('submit', e => { e.preventDefault(); }); // Enter en un input nunca dispara un cálculo accidental.
    button.addEventListener('click', async () => {
        if (calculating) return;
        if (!ready()) { status.textContent = 'Faltan ubicaciones por confirmar.'; return; }
        const body = new FormData(form);
        calculating = true; update(); form.setAttribute('aria-busy', 'true');
        const controls = [...form.querySelectorAll('input:not([type=hidden]), select, button')].filter(c => !c.disabled);
        controls.forEach(c => c.disabled = true);
        status.textContent = 'Calculando recorrido con HERE…';
        try {
            const data = await window.RuteaYaHere.request(form.action, body, undefined, form);
            status.textContent = 'Recorrido calculado. Cargando el mapa…';
            await window.RuteaYaRecorridos.dibujar(data);
            status.textContent = 'Recorrido calculado. Revisá el resultado y el estado del mapa.';
        } catch (error) {
            if (error.name === 'AbortError') status.textContent = 'Se canceló el cálculo. Podés reintentarlo.';
            else status.textContent = window.RuteaYaHere.message(error);
        } finally {
            controls.forEach(c => c.disabled = false);
            calculating = false; form.removeAttribute('aria-busy'); update();
        }
    });
    update();
});
