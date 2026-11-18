(async () => {
    const assert = (value, text) => { if (!value) throw new Error(text); };
    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
    async function wait(check, text) { for (let i = 0; i < 160; i++) { if (check()) return; await sleep(50); } throw new Error(text); }
    try {
        if (location.pathname === '/Panel/Viajes' && sessionStorage.getItem('viaje-enviado') === 'si') {
            assert(document.body.textContent.includes('Origen UI, Morón, Argentina'), 'Lista de viajes debe reflejar dirección canónica guardada.');
            const enlace = [...document.querySelectorAll('a[href]')].find(a => /\/DetalleViaje\/\d+/.test(a.getAttribute('href')));
            assert(enlace, 'Debe existir el viaje guardado.'); location.href = '/Recorridos/' + enlace.href.match(/\/DetalleViaje\/(\d+)/)[1]; return;
        }
        assert(location.pathname === '/Panel/NuevoViaje', 'Pantalla inesperada: ' + location.pathname);
        const form = document.querySelector('[data-viaje-direcciones]');
        assert(form && !form.querySelector('[name=ParadasTexto]'), 'Formulario debe tener campos estructurados.');
        async function elegir(root, texto) {
            const input = root.querySelector('input[type=search]'); input.value = texto; input.dispatchEvent(new Event('input', { bubbles: true }));
            await wait(() => root.querySelector('[data-sugerencias] button'), 'No hay sugerencias');
            input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
            input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
            await wait(() => root.querySelector('[data-confirmacion-direccion]').value, 'Confirmación persistente falló: ' + root.textContent);
        }
        await elegir(form.querySelector('#viaje-origen').closest('[data-direccion-here]'), 'Origen UI');
        const origen = form.querySelector('#viaje-origen'), rootOrigen = origen.closest('[data-direccion-here]');
        origen.value = 'Otra dirección'; origen.dispatchEvent(new Event('input', { bubbles: true }));
        assert(!rootOrigen.querySelector('[data-confirmacion-direccion]').value, 'Editar debe invalidar la dirección anterior.');
        assert(!rootOrigen.querySelector('select') && !rootOrigen.querySelector('[data-confirmar-direccion]'), 'Sin pasos técnicos.');
        await elegir(rootOrigen, 'Origen UI');
        await elegir(form.querySelector('#viaje-destino').closest('[data-direccion-here]'), 'Destino UI');
        form.querySelector('[data-agregar-parada]').click();
        await wait(() => form.querySelector('[data-parada-viaje]'), 'No se agregó parada');
        await elegir(form.querySelector('[data-parada-viaje] [data-direccion-here]'), 'Parada UI');
        for (const [name, value] of Object.entries({ HoraSalida: '08:00', HoraLlegada: '09:00', CantMaxPaquetes: '2', PesoMax: '10', DesvioMaxKm: '3' }))
            form.querySelector(`[name=${name}]`).value = value;
        form.querySelector('[name=DiasSemana]').checked = true;
        assert(form.checkValidity(), 'Validación HTML inesperada.');
        sessionStorage.setItem('viaje-enviado', 'si');
        form.querySelector('button[type=submit]').click();
        await sleep(2500);
        window.resultadoPruebaDirecciones = 'ERROR: No se navegó tras guardar: ' + form.querySelector('[data-estado-viaje]').textContent;
    } catch (error) { window.resultadoPruebaDirecciones = 'ERROR: ' + error.message; }
})();
