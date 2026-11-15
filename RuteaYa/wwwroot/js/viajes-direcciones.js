document.querySelectorAll('[data-viaje-direcciones]').forEach(form => {
    const status = form.querySelector('[data-estado-viaje]'), list = form.querySelector('[data-paradas-viaje]');
    let saving = false;
    const ready = () => {
        const roots = [...form.querySelectorAll('[data-direccion-here]')];
        return roots.length >= 2 && roots.every(root => root.querySelector('[data-confirmacion-direccion]').value ||
            root.dataset.conservarActual === 'true' && root.querySelector('[data-texto-direccion]').value === root.dataset.textoOriginal);
    };
    form.addEventListener('submit', event => {
        if (saving || !ready()) { event.preventDefault(); status.textContent = 'Elegí y confirmá todas las direcciones nuevas o modificadas.'; return; }
        saving = true; status.textContent = 'Guardando viaje y ubicaciones verificadas…';
        form.querySelectorAll('button[type=submit]').forEach(b => b.disabled = true);
    });
    form.addEventListener('click', async event => {
        const remove = event.target.closest('[data-quitar-parada]'), move = event.target.closest('[data-mover-parada]');
        if (remove) { remove.closest('[data-parada-viaje]').remove(); return; }
        if (move) {
            const row = move.closest('[data-parada-viaje]');
            if (move.dataset.moverParada === '-1' && row.previousElementSibling) list.insertBefore(row, row.previousElementSibling);
            if (move.dataset.moverParada === '1' && row.nextElementSibling) list.insertBefore(row.nextElementSibling, row);
            return;
        }
        const add = event.target.closest('[data-agregar-parada]');
        if (!add || add.disabled) return;
        add.disabled = true; status.textContent = 'Preparando una nueva parada…';
        try {
            const response = await fetch(add.dataset.url, { method: 'POST', credentials: 'same-origin', headers: {
                'RequestVerificationToken': form.querySelector('[name=__RequestVerificationToken]').value,
                'X-Requested-With': 'XMLHttpRequest' } });
            if (response.redirected || response.status === 401) throw new Error('Tu sesión venció. Volvé a iniciar sesión.');
            if (!response.ok) {
                const data = response.headers.get('content-type')?.includes('application/json') ? await response.json() : null;
                throw new Error(data?.mensaje ?? 'No se pudo agregar la parada. Reabrí el formulario.');
            }
            const template = document.createElement('template'); template.innerHTML = await response.text();
            const row = template.content.firstElementChild;
            if (!row?.matches('[data-parada-viaje]')) throw new Error('La respuesta del formulario no es válida.');
            list.append(row); row.querySelectorAll('[data-direccion-here]').forEach(window.RuteaYaDirecciones.inicializar);
            row.querySelector('input[type=search]').focus(); status.textContent = 'Escribí y confirmá la dirección de la parada.';
        } catch (error) { status.textContent = window.RuteaYaHere.message(error); }
        finally { add.disabled = false; }
    });
});
