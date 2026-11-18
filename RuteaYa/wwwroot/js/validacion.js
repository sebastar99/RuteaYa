/* Validación común: usa las reglas MVC y mantiene el servidor como autoridad. */
(() => {
    if (!window.jQuery?.validator || window.RuteaYaValidacion) return;
    const $ = window.jQuery;
    $.extend($.validator.messages, {
        required: 'Completá este campo.', email: 'Ingresá un correo electrónico válido.',
        number: 'Ingresá un número válido.', digits: 'Ingresá un número entero.',
        date: 'Ingresá una fecha válida.', dateISO: 'Ingresá una fecha válida.',
        min: $.validator.format('Ingresá un valor mayor o igual a {0}.'),
        max: $.validator.format('Ingresá un valor menor o igual a {0}.'),
        range: $.validator.format('Ingresá un valor entre {0} y {1}.'),
        minlength: $.validator.format('Ingresá al menos {0} caracteres.'),
        maxlength: $.validator.format('No ingreses más de {0} caracteres.'),
        equalTo: 'Los valores no coinciden.', step: 'Ingresá un valor con el incremento permitido.'
    });
    // jquery.validate solo implementa step para number/range/text. Los controles
    // temporales ya exponen esa misma comprobación mediante la API nativa.
    const stepNumerico = $.validator.methods.step;
    $.validator.methods.step = function (value, el, param) {
        return ['time', 'date', 'datetime-local'].includes(el.type)
            ? this.optional(el) || !el.validity.stepMismatch : stepNumerico.call(this, value, el, param);
    };
    $.validator.addMethod('direccionhere', (_, el) => {
        const root = el.closest('[data-direccion-here]');
        return !el.value.trim() || !!root.querySelector('[data-confirmacion-direccion]').value ||
            root.dataset.conservarActual === 'true' && el.value === root.dataset.textoOriginal;
    }, 'Elegí esta dirección de la lista de sugerencias.');
    $.validator.addMethod('valideznativa', (_, el) => !el.validity || !el.validity.customError,
        (_, el) => el.validationMessage || 'Revisá este campo.');
    $.validator.addMethod('ordensinrepetir', (value, el) => !value || [...el.form.querySelectorAll('input[name^="ordenes["]')].filter(e => e.value === value).length <= 1,
        'Ingresá un orden que no esté repetido.');
    $.validator.addMethod('horadiferente', (value, el) => !value || !el.form.elements.HoraSalida?.value || value !== el.form.elements.HoraSalida.value,
        'La hora de llegada no puede ser igual a la de salida.');
    $.validator.addMethod('fechafinal', (value, el) => !value || !el.form.elements.desde?.value || value >= el.form.elements.desde.value,
        'La fecha final no puede ser anterior a la fecha inicial.');
    function mensaje(el) {
        const label = [...el.form.querySelectorAll('label')].find(l => l.htmlFor === el.id && el.id);
        const nombre = (label?.textContent || el.closest('.panel-field, .acceso-field')?.querySelector('label')?.textContent || '').replace(/\s+/g, ' ').replace(/\*/g, '').trim();
        return nombre ? `Completá el campo «${nombre}».` : 'Completá este campo.';
    }
    function inicializar(root = document) {
        const forms = root.matches?.('form') ? [root] : [...new Set([...root.querySelectorAll('form')].concat(root.closest?.('form') || []))];
        forms.forEach(form => {
            // Con JS se muestran TODOS los errores; sin JS quedan MVC y la validación nativa.
            form.noValidate = true;
            const validator = $(form).validate();
            validator.settings.errorClass = 'input-validation-error';
            validator.settings.errorElement = 'span';
            validator.settings.highlight = el => {
                const grupo = el.type === 'checkbox' || el.type === 'radio' ? [...form.elements].filter(e => e.name === el.name) : [el];
                grupo.forEach(e => { e.classList.add('input-validation-error'); e.setAttribute('aria-invalid', 'true'); });
            };
            validator.settings.unhighlight = el => {
                [...form.elements].filter(e => e.name === el.name).forEach(e => { e.classList.remove('input-validation-error'); e.removeAttribute('aria-invalid'); });
            };
            validator.settings.errorPlacement = (error, element) => {
                const el = element[0];
                let span = [...form.querySelectorAll('[data-valmsg-for]')].find(s => s.dataset.valmsgFor === el.name);
                if (!span) {
                    span = document.createElement('span'); span.dataset.valmsgFor = el.name; span.dataset.valmsgReplace = 'true';
                    (el.closest('[data-direccion-here]') || el).after(span);
                }
                span.className = 'field-validation-error'; span.setAttribute('role', 'alert');
                span.id ||= `validacion-${el.id || [...form.elements].indexOf(el)}`;
                el.setAttribute('aria-describedby', [...new Set((el.getAttribute('aria-describedby') || '').split(' ').filter(Boolean).concat(span.id))].join(' '));
                span.replaceChildren(error[0]); error.data('unobtrusiveContainer', $(span));
            };
            form.querySelectorAll('input:not([type=hidden]):not([type=submit]):not([type=button]), select, textarea').forEach(el => {
                if (!el.name) return;
                if (!el.id) el.id = `campo-${[...document.forms].indexOf(form)}-${[...form.elements].indexOf(el)}`;
                const rules = { valideznativa: true };
                if (el.type !== 'password') rules.normalizer = value => typeof value === 'string' ? value.trim() : value;
                if (el.required || el.hasAttribute('data-val-required')) {
                    const texto = el.getAttribute('data-val-required');
                    rules.messages = { required: texto && !/^The .* field is required\./.test(texto) ? texto : mensaje(el) };
                }
                if (el.hasAttribute('data-val-number')) rules.messages = { ...rules.messages, number: 'Ingresá un número válido.' };
                if (el.hasAttribute('data-val-date')) rules.messages = { ...rules.messages, date: 'Ingresá una fecha válida.' };
                if (el.matches('[data-texto-direccion]')) rules.direccionhere = true;
                if (el.name === 'DiasSemana') { rules.required = true; rules.messages = { required: 'Seleccioná al menos un día de circulación.' }; }
                if (el.name.startsWith('ordenes[')) rules.ordensinrepetir = true;
                if (el.name === 'HoraLlegada') rules.horadiferente = true;
                if (el.name === 'hasta') rules.fechafinal = true;
                $(el).rules('add', rules);
            });
            if (form.dataset.validacionInicializada) return;
            form.dataset.validacionInicializada = 'true';
            form.addEventListener('submit', e => {
                form.dataset.validacionIntentada = 'true';
                try {
                    if (!$(form).valid()) { e.preventDefault(); e.stopImmediatePropagation(); validator.focusInvalid(); }
                } catch (_) {
                    e.preventDefault(); e.stopImmediatePropagation();
                    let aviso = form.querySelector('[data-error-validacion]');
                    if (!aviso) { aviso = document.createElement('p'); aviso.dataset.errorValidacion = ''; aviso.className = 'validation-summary-errors'; aviso.setAttribute('role', 'alert'); form.prepend(aviso); }
                    aviso.textContent = 'No se pudo revisar el formulario. Recargá la página antes de reintentar.';
                }
            }, true);
            const revisar = e => {
                const el = e.target.matches('[data-texto-direccion]') ? e.target : e.target.closest('[data-direccion-here]')?.querySelector('[data-texto-direccion]') || e.target;
                queueMicrotask(() => {
                    const revisarCampo = campo => {
                        if (!campo.name || campo.disabled || !validator.element(campo)) return;
                        const span = [...form.querySelectorAll('[data-valmsg-for]')].find(s => s.dataset.valmsgFor === campo.name);
                        if (span) {
                            const anterior = span.textContent.trim();
                            span.replaceChildren(); span.classList.remove('field-validation-error'); span.classList.add('field-validation-valid');
                            form.querySelectorAll('[data-valmsg-summary] li').forEach(li => { if (anterior && li.textContent.trim() === anterior) li.remove(); });
                            form.querySelectorAll('[data-valmsg-summary]').forEach(s => { if (!s.querySelector('li')) { s.classList.remove('validation-summary-errors'); s.classList.add('validation-summary-valid'); } });
                        }
                    };
                    if (el.name && (form.dataset.validacionIntentada || el.classList.contains('input-validation-error'))) revisarCampo(el);
                    [...form.elements].filter(c => c !== el && c.name && c.classList.contains('input-validation-error')).forEach(revisarCampo);
                });
            };
            form.addEventListener('input', revisar); form.addEventListener('change', revisar); form.addEventListener('direccion:estado', revisar);
        });
    }
    window.RuteaYaValidacion = { inicializar };
    $(function () { inicializar(); });
})();
