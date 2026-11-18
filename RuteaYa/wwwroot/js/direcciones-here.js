/* Reutilizable: URLs autorizadas por el consumidor, tokens opacos, ningún acceso directo a HERE. */
function inicializarDireccionHere(root) {
    if (root.dataset.inicializada === 'true') return;
    root.dataset.inicializada = 'true';
    const input = root.querySelector('[data-texto-direccion]');
    const list = root.querySelector('[data-sugerencias]');
    const status = root.querySelector('[data-estado-direccion]');
    const hidden = root.querySelector('[data-confirmacion-direccion]');
    let timer, controller, version = 0, resolved = '', active = -1;
    const changed = () => root.dispatchEvent(new CustomEvent('direccion:estado', { bubbles: true, detail: { confirmada: !!hidden.value } }));
    function invalidate() { hidden.value = ''; root.dataset.conservarActual = 'false'; changed(); }
    function cancel() { clearTimeout(timer); controller?.abort(); version++; }
    async function request(url, fields, signal) {
        return window.RuteaYaHere.request(url, fields, signal, root.closest('form'));
    }
    function begin() { cancel(); controller = new AbortController(); return { current: version, signal: controller.signal }; }
    async function choose(token) {
        const { current, signal } = begin();
        invalidate(); resolved = ''; list.replaceChildren(); active = -1;
        input.setAttribute('aria-expanded', 'false');
        input.removeAttribute('aria-activedescendant');
        status.textContent = 'Seleccionando dirección…';
        try {
            const result = await request(root.dataset.resolverUrl, { token }, signal);
            if (current !== version) return;
            resolved = result.token;
            const confirmed = await request(root.dataset.confirmarUrl, { token: resolved }, signal);
            if (current !== version) return;
            if (!confirmed.confirmada || !confirmed.token) throw new Error('No se pudo seleccionar la dirección. Volvé a intentarlo.');
            input.value = confirmed.ubicacion.direccionNormalizada;
            hidden.value = confirmed.token;
            status.textContent = '✓ Dirección seleccionada';
            changed(); input.focus();
        } catch (error) {
            if (current === version && error.name !== 'AbortError') status.textContent = window.RuteaYaHere.message(error);
        }
    }
    input.addEventListener('input', () => {
        cancel(); invalidate(); resolved = ''; list.replaceChildren(); active = -1;
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
                status.textContent = items.length ? 'Elegí una dirección de la lista.' : 'No se encontraron direcciones. Agregá calle, número y localidad.';
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
    // No restaurar confirmaciones de un formulario recuperado de la caché del navegador.
    window.addEventListener('pageshow', event => { if (event.persisted) { cancel(); invalidate(); resolved = ''; } });
}
document.querySelectorAll('[data-direccion-here]').forEach(inicializarDireccionHere);
window.RuteaYaDirecciones = { inicializar: inicializarDireccionHere };
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
