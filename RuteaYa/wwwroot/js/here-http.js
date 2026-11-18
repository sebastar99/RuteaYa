/* Frontera HTTP compartida: mensajes públicos estructurados, nunca cuerpos remotos ni credenciales. */
window.RuteaYaHere = {
    async request(url, fields, signal, form) {
        const options = { signal, credentials: 'same-origin', cache: 'no-store',
            headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' } };
        if (fields) {
            options.method = 'POST';
            options.headers.RequestVerificationToken = form?.querySelector('[name="__RequestVerificationToken"]')?.value ?? '';
            options.body = fields instanceof FormData ? fields : new URLSearchParams(fields);
        }
        const response = await fetch(url, options);
        if (response.redirected || response.status === 401) throw new Error('Tu sesión de usuario venció. Volvé a iniciar sesión.');
        if (response.status === 403) throw new Error('No tenés autorización para esta operación.');
        if (response.status === 499) throw new DOMException('Solicitud cancelada.', 'AbortError');
        const json = response.headers.get('content-type')?.includes('application/json');
        if (!response.ok) {
            const data = json ? await response.json() : null;
            const fallback = response.status === 400 ? 'La solicitud no es válida o venció el token del formulario. Recargá la previsualización.' :
                response.status === 404 ? 'El viaje o la sesión de previsualización no están disponibles.' : 'No se pudo completar la operación. Intentá nuevamente.';
            throw new Error((data?.mensaje ?? fallback) + (data?.referencia ? ` Referencia: ${data.referencia}` : ''));
        }
        if (!json) throw new Error('El servidor no devolvió una respuesta válida. Recargá la previsualización.');
        try { return await response.json(); }
        catch (error) {
            if (error.name === 'AbortError') throw error;
            throw new Error('La respuesta del servidor está incompleta o no es válida. Reintentá.');
        }
    },
    message(error) {
        return error instanceof TypeError ? 'No se pudo conectar con el servidor. Revisá tu conexión.' : error.message;
    }
};
