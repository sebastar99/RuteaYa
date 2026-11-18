document.querySelectorAll('[data-form-salida]').forEach(form => {
    const fecha = form.querySelector('[name=Fecha]'), error = form.querySelector('[data-error-fecha]');
    const dias = fecha.dataset.dias.split(',').filter(Boolean).map(Number);
    fecha.addEventListener('change', () => {
        const dia = new Date(fecha.value + 'T12:00:00').getDay() || 7;
        const mensaje = fecha.value && dias.length && !dias.includes(dia) ? 'Elegí una fecha que coincida con los días de tu viaje.' : '';
        fecha.setCustomValidity(mensaje); error.textContent = mensaje;
    });
    const tipo = form.querySelector('[data-tipo-limite]'), campo = form.querySelector('[data-campo-limite]'), limite = campo.querySelector('input');
    tipo.addEventListener('change', () => {
        const sinLimite = tipo.value === 'true'; campo.hidden = sinLimite; limite.disabled = sinLimite; limite.required = !sinLimite;
        if (sinLimite) limite.value = '';
    });
});
