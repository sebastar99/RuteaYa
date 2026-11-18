(async () => {
    const assert = (value, text) => { if (!value) throw new Error(text); };
    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
    async function wait(check, text) { for (let i = 0; i < 160; i++) { if (check()) return; await sleep(50); } throw new Error(text); }
    try {
        if ((location.pathname === '/Envios' || location.pathname === '/Envios/Index') && sessionStorage.getItem('envio-enviado') === 'si') {
            assert(document.body.textContent.includes('Retiro UI, Morón, Argentina'), 'La dirección publicada debe venir de HERE.');
            assert(document.body.textContent.includes('Desde navegador'), 'Se debe conservar la descripción.');
            window.resultadoPruebaDirecciones = 'OK'; return;
        }
        const form = document.querySelector('[data-envio-direcciones]');
        assert(form, 'Crear Envío debe tener el formulario integrado.');
        async function elegir(id, texto) {
            const root = document.getElementById(id).closest('[data-direccion-here]'), input = root.querySelector('input[type=search]');
            input.value = texto; input.dispatchEvent(new Event('input', { bubbles: true }));
            await wait(() => root.querySelector('[data-sugerencias] button'), 'Sin sugerencias');
            root.querySelector('[data-sugerencias] button').click();
            await wait(() => root.querySelector('[data-confirmacion-direccion]').value, 'No confirmó la ubicación');
        }
        await elegir('envio-retiro', 'Retiro UI'); await elegir('envio-entrega', 'Entrega UI');
        const fecha = new Date(); fecha.setDate(fecha.getDate() + 1);
        for (const [nombre, valor] of Object.entries({ Descripcion: 'Desde navegador', PesoKg: '2.5', Monto: '1500.75', Tamano: '2',
            FechaLimite: `${fecha.getFullYear()}-${String(fecha.getMonth() + 1).padStart(2, '0')}-${String(fecha.getDate()).padStart(2, '0')}` }))
            form.querySelector(`[name=${nombre}]`).value = valor;
        assert(form.checkValidity(), 'Los otros campos deben conservar validaciones HTML.');
        sessionStorage.setItem('envio-enviado', 'si'); form.querySelector('button[type=submit]').click();
        await sleep(2500);
        window.resultadoPruebaDirecciones = 'ERROR: No publicó: ' + form.querySelector('[data-estado-viaje]').textContent;
    } catch (error) { window.resultadoPruebaDirecciones = 'ERROR: ' + error.message; }
})();
