(async () => {
    const assert = (v, m) => { if (!v) throw new Error(m); };
    const sleep = ms => new Promise(r => setTimeout(r, Math.max(ms, 250))); // Esperar también las transiciones CSS del borde.
    async function wait(fn, m) { for (let i = 0; i < 160; i++) { if (fn()) return; await new Promise(r => setTimeout(r, 40)); } throw new Error(m); }
    const set = (form, name, value) => { const el = form.elements.namedItem(name); el.value = value; el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true })); return el; };
    const mensaje = (form, name) => [...form.querySelectorAll('[data-valmsg-for]')].find(s => s.dataset.valmsgFor === name);
    const rojo = el => el.classList.contains('input-validation-error') && getComputedStyle(el).borderColor === 'rgb(180, 35, 24)';
    const enviar = form => form.querySelector('[type=submit]').click();
    function errores(form, names) { names.forEach(name => { const el = form.elements.namedItem(name); assert(rojo(el), 'Borde rojo: ' + name + ' (' + el.className + ', ' + getComputedStyle(el).borderColor + ')'); assert(mensaje(form, name)?.textContent.trim(), 'Mensaje específico: ' + name); assert(!mensaje(form, name).textContent.includes('The '), 'Español: ' + name); }); }
    async function elegir(form, id, texto) {
        const root = document.getElementById(id).closest('[data-direccion-here]'); set(form, root.querySelector('[data-texto-direccion]').name, texto);
        await wait(() => root.querySelector('[data-sugerencias] button'), 'Sugerencias ' + id); root.querySelector('[data-sugerencias] button').click();
        await wait(() => root.querySelector('[data-confirmacion-direccion]').value, 'Selección ' + id);
    }
    try {
        sessionStorage.setItem('validacion-paso', location.pathname);
        window.addEventListener('beforeunload', () => {
            if (sessionStorage.getItem('validacion-paso') === 'viaje: peso inválido') sessionStorage.setItem('validacion-paso', 'viaje: navegación inesperada al enviar peso inválido');
        });
        await wait(() => window.RuteaYaValidacion && [...document.forms].some(f => f.dataset.validacionInicializada), 'Carga de validación compartida');
        if (location.pathname === '/Acceso/Registro') {
            const f = document.querySelector('.acceso-form'); enviar(f); await sleep(300);
            errores(f, ['Nombre', 'Apellido', 'Email', 'Password', 'ConfirmarPassword']);
            set(f, 'Nombre', 'Ana'); set(f, 'Apellido', 'Prueba'); set(f, 'Email', 'correo-no-valido'); set(f, 'Password', 'abc'); set(f, 'ConfirmarPassword', 'otro'); enviar(f); await sleep(50);
            errores(f, ['Email', 'Password', 'ConfirmarPassword']); assert(!rojo(f.elements.Nombre), 'Nombre corregido');
            set(f, 'Email', 'ana@test.invalid'); set(f, 'Password', 'Prueba123!'); set(f, 'ConfirmarPassword', 'Prueba123!'); await sleep(80);
            assert(!f.querySelector('input.input-validation-error'), 'Se limpian todos los errores corregidos');
            location.href = '/Acceso/Login'; return;
        }
        if (location.pathname === '/Acceso/Login') {
            const f = document.querySelector('.acceso-form'); enviar(f); await sleep(50); errores(f, ['Email', 'Password']);
            set(f, 'Email', 'ana@test.invalid'); set(f, 'Password', 'Prueba123!'); await sleep(80);
            assert(!f.querySelector('input.input-validation-error'), 'Login corregido'); location.href = '/Panel/NuevoViaje'; return;
        }
        if (location.pathname === '/Panel/NuevoViaje') {
            sessionStorage.setItem('validacion-paso', 'viaje: vacíos');
            const f = document.querySelector('[data-viaje-direcciones]');
            ['HoraSalida', 'HoraLlegada', 'CantMaxPaquetes', 'PesoMax', 'DesvioMaxKm'].forEach(n => set(f, n, ''));
            enviar(f); await sleep(50); errores(f, ['Origen', 'Destino', 'HoraSalida', 'HoraLlegada', 'CantMaxPaquetes', 'PesoMax', 'DesvioMaxKm']);
            assert(mensaje(f, 'DiasSemana').textContent.trim(), 'Grupo de días requerido');
            sessionStorage.setItem('validacion-paso', 'viaje: selección HERE');
            await elegir(f, 'viaje-origen', 'Origen validado'); await elegir(f, 'viaje-destino', 'Destino validado');
            f.querySelector('[data-agregar-parada]').click(); await wait(() => f.querySelector('[data-parada-viaje]'), 'Parada dinámica');
            sessionStorage.setItem('validacion-paso', 'viaje: parada');
            enviar(f); await sleep(50); const parada = f.querySelector('[data-parada-viaje] [data-texto-direccion]'); assert(rojo(parada), 'Parada dinámica requerida');
            await elegir(f, parada.id, 'Parada validada');
            ['HoraSalida', 'HoraLlegada', 'CantMaxPaquetes', 'PesoMax', 'DesvioMaxKm'].forEach((n,i) => set(f,n,['08:00','09:00','2','0','3'][i]));
            f.querySelector('[name=DiasSemana]').checked = true; f.querySelector('[name=DiasSemana]').dispatchEvent(new Event('change', {bubbles:true}));
            const token = f.querySelector('[name=OrigenComprobante]').value;
            sessionStorage.setItem('validacion-paso', 'viaje: peso inválido');
            assert(!jQuery(f).valid(), 'El peso 0 debe invalidar el formulario; reglas=' + JSON.stringify(jQuery(f.elements.PesoMax).rules()) + '; valor=' + f.elements.PesoMax.value);
            enviar(f); await sleep(50); errores(f,['PesoMax']); assert(f.querySelector('[name=OrigenComprobante]').value === token, 'Comprobante conservado en rechazo cliente');
            // POST directo: el servidor debe devolver los mismos valores y comprobantes vigentes.
            sessionStorage.setItem('validacion-paso', 'viaje: POST servidor');
            const respuesta = await fetch(f.action, {method:'POST', body:new FormData(f), signal:AbortSignal.timeout(12000)}); const html = await respuesta.text();
            sessionStorage.setItem('validacion-paso', 'viaje: respuesta servidor');
            assert(respuesta.status === 200, 'Rechazo servidor vuelve al formulario');
            const doc = new DOMParser().parseFromString(html,'text/html');
            assert(doc.querySelector('[name=PesoMax]').classList.contains('input-validation-error'), 'Borde de servidor');
            assert(doc.querySelector('[name=OrigenComprobante]').value === token, 'Comprobante conservado en rechazo servidor');
            assert(doc.querySelector('[name=Origen]').value.includes('Origen validado'), 'Texto válido conservado');
            set(f,'PesoMax','10'); await sleep(80); assert(!rojo(f.elements.PesoMax), 'Peso corregido');
            location.href='/Panel/EditarViaje/1'; return;
        }
        if (location.pathname === '/Panel/EditarViaje/1') {
            const f=document.querySelector('[data-viaje-direcciones]'); set(f,'CantMaxPaquetes',''); enviar(f); await sleep(50); errores(f,['CantMaxPaquetes']);
            set(f,'CantMaxPaquetes','2'); await sleep(50); assert(!rojo(f.elements.CantMaxPaquetes),'Edición corregida'); location.href='/Envios/Crear'; return;
        }
        if (location.pathname === '/Envios/Crear') {
            const f=document.querySelector('[data-envio-direcciones]'); ['PesoKg','Monto','FechaLimite','Tamano'].forEach(n=>set(f,n,'')); enviar(f); await sleep(50);
            errores(f,['OrigenDir','DestinoDir','Descripcion','PesoKg','Monto','FechaLimite','Tamano']);
            await elegir(f,'envio-retiro','Retiro válido'); await elegir(f,'envio-entrega','Entrega válida');
            const token=f.elements.RetiroComprobante.value; enviar(f); await sleep(50); assert(f.elements.RetiroComprobante.value===token,'Envío conserva dirección');
            location.href='/Oportunidades?viajeId=1'; return;
        }
        if (location.pathname === '/Oportunidades') {
            const f=document.querySelector('[data-form-salida]'); set(f,'LlegadaSinRestriccion','false'); enviar(f); await sleep(80);
            errores(f,['Fecha','LlegadaMaximaLocal']);
            set(f,'LlegadaSinRestriccion','true');
            let fecha=new Date(); fecha.setDate(fecha.getDate()+15); while(fecha.getDay()!==2) fecha.setDate(fecha.getDate()+1);
            const iso = fecha.getFullYear()+'-'+String(fecha.getMonth()+1).padStart(2,'0')+'-'+String(fecha.getDate()).padStart(2,'0');
            set(f,'Fecha',iso); enviar(f); await sleep(50); errores(f,['Fecha']);
            fecha.setDate(fecha.getDate()-1); set(f,'Fecha',fecha.getFullYear()+'-'+String(fecha.getMonth()+1).padStart(2,'0')+'-'+String(fecha.getDate()).padStart(2,'0')); await sleep(80);
            assert(!rojo(f.elements.Fecha),'Día habilitado corrige error'); location.href='/Panel/DetalleViaje/1'; return;
        }
        if (location.pathname === '/Panel/DetalleViaje/1') {
            const agregar=[...document.forms].find(f=>f.action.includes('AgregarParada')); set(agregar,'orden','0'); enviar(agregar); await sleep(50); errores(agregar,['ubicacion','orden']);
            const reordenar=[...document.forms].find(f=>f.action.includes('ReordenarParadas')); set(reordenar,'ordenes[0]','1'); set(reordenar,'ordenes[1]','1'); enviar(reordenar); await sleep(50); errores(reordenar,['ordenes[0]','ordenes[1]']);
            set(reordenar,'ordenes[1]','2'); await sleep(80); assert(!reordenar.querySelector('input.input-validation-error'),'Orden corregido');
            window.resultadoPruebaDirecciones='OK'; return;
        }
        throw new Error('Pantalla inesperada: '+location.pathname);
    } catch(e) { window.resultadoPruebaDirecciones='ERROR: '+e.message; }
})();
