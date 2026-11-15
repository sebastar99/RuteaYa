/* Leaflet 1.9.4: https://leafletjs.com, BSD-2-Clause. HERE calls stay on the server. */
document.querySelectorAll('[data-ubicacion]').forEach(fieldset => {
    fieldset.querySelectorAll('[data-candidato]').forEach(radio => radio.addEventListener('change', () => {
        fieldset.querySelectorAll('[data-accesos]').forEach(select => {
            select.disabled = select.dataset.accesos !== radio.value;
            select.value = ''; // Cambiar candidato exige volver a confirmar el punto.
        });
    }));
});

window.RuteaYaRecorridos = (() => {
    let map, controller, version = 0;
    async function dibujar(data) {
    const container = document.getElementById('mapa-recorrido');
    const status = document.getElementById('estado-mapa');
    if (!container || !Array.isArray(data?.secciones) || !data.secciones.length ||
        data.secciones.some(s => !Array.isArray(s) || s.length < 2) || !Array.isArray(data.puntos) || data.puntos.length < 2)
        throw new Error('El servidor devolvió un recorrido incompleto. No se puede dibujar la ruta.');
    const current = ++version;
    controller?.abort(); controller = new AbortController();
    document.getElementById('resultado-recorrido').hidden = false;
    const summary = document.querySelector('[data-resumen-recorrido]');
    const metrics = document.createElement('p'), list = document.createElement('ol');
    metrics.textContent = `${(data.distanciaMetros / 1000).toLocaleString('es-AR')} km · ${(data.duracionSegundos / 60).toLocaleString('es-AR', { maximumFractionDigits: 1 })} minutos estimados. Automóvil, sin tráfico en tiempo real.`;
    data.etiquetas.forEach((label, i) => { const li = document.createElement('li'); li.textContent = `${label} · HERE ${data.identificadores[i] || 'sin identificador'} · navegación ${data.puntos[i].join(', ')}`; list.append(li); });
    summary.replaceChildren(metrics, list);
    if (map) { map.remove(); map = null; }
    if (typeof L === 'undefined') { status.textContent = 'La ruta se calculó, pero no se pudo cargar Leaflet para dibujarla. Revisá la conexión a los recursos del mapa.'; return; }
    try {
    const point = p => {
        if (!Array.isArray(p) || p.length !== 2 || !p.every(Number.isFinite)) throw new Error('Coordenadas de mapa inválidas.');
        return p;
    };
    map = L.map(container, { minZoom: 0, maxZoom: 18 });
    const layers = data.secciones.map(section => L.polyline(section.map(point), { color: '#1765ba', weight: 5 }).addTo(map));
    data.puntos.forEach((p, i) => {
        const label = document.createElement('span');
        label.textContent = `${i + 1}. ${data.etiquetas[i]}`;
        L.marker(point(p)).bindPopup(label).addTo(map);
    });
    data.posiciones.forEach((p, i) => {
        const label = document.createElement('span');
        label.textContent = `Position de referencia: ${data.etiquetas[i]}`;
        L.circleMarker(point(p), { radius: 6, color: '#555', fillOpacity: 0.4 }).bindPopup(label).addTo(map);
    });
    const bounds = L.featureGroup(layers).getBounds();
    data.puntos.concat(data.posiciones).forEach(p => bounds.extend(point(p)));
    map.fitBounds(bounds, { padding: [30, 30], maxZoom: 16 });
    } catch (error) {
        status.textContent = `La ruta se calculó pero no se pudo dibujar: ${error.message}`;
        return;
    }
    status.textContent = 'Geometría calculada. Cargando fondo de mapa…';
    try {
        const notices = await window.RuteaYaHere.request(data.atribuciones || container.dataset.atribuciones, null, controller.signal);
        if (current !== version) return;
        // Comparar intervalos normalizados permite navegar a ambos lados del antimeridiano.
        const spans = (west, east) => {
            if (Math.abs(east - west) >= 360) return [[-180, 180]];
            const norm = x => ((x + 180) % 360 + 360) % 360 - 180;
            west = norm(west); east = norm(east);
            return west <= east ? [[west, east]] : [[west, 180], [-180, east]];
        };
        let previous = '';
        function attribution() {
            const b = map.getBounds(), z = map.getZoom();
            const visible = notices.filter(n => z >= n.minLevel && z <= n.maxLevel && n.boundingBoxes.some(box =>
                box.south <= b.getNorth() && box.north >= b.getSouth() &&
                spans(box.west, box.east).some(a => spans(b.getWest(), b.getEast()).some(c => a[0] <= c[1] && c[0] <= a[1]))));
            const node = document.createElement('span');
            node.textContent = `© ${new Date().getFullYear()} HERE${visible.length ? ', ' + [...new Set(visible.map(n => n.label))].join(', ') : ''}. `;
            const link = document.createElement('a');
            link.href = 'https://legal.here.com/en-gb/terms/general-content-supplier-terms-and-notices';
            link.textContent = 'Avisos legales'; link.target = '_blank'; link.rel = 'noopener noreferrer';
            node.append(link);
            if (previous) map.attributionControl.removeAttribution(previous);
            previous = node.outerHTML;
            map.attributionControl.addAttribution(previous);
            document.getElementById('avisos-mapa').replaceChildren(...visible.map(n => {
                const p = document.createElement('p'); p.textContent = `${n.label}: ${n.copyrightText}`; return p;
            }));
        }
        attribution();
        map.on('moveend zoomend', attribution);
        // URL generada por MVC; se sustituyen únicamente los segmentos numéricos de teselas.
        const template = (data.teselas || container.dataset.teselas).replace('/Tesela/0/0/0', '/Tesela/{z}/{x}/{y}');
        L.tileLayer(template, { maxZoom: 18, noWrap: true }).on('tileerror', () => {
            if (current !== version) return;
            status.textContent = 'No se pudieron cargar algunas teselas. Revisá HERE Raster Tile API v3 o renová la sesión.';
        }).addTo(map);
        status.textContent = 'Ruta dibujada. Fondo cartográfico de HERE.';
    } catch (error) {
        if (current === version && error.name !== 'AbortError')
            status.textContent = `La geometría está dibujada, pero el fondo no está disponible. ${window.RuteaYaHere.message(error)}`;
    }
    }
    return { dibujar };
})();
const datosRecorrido = document.getElementById('datos-recorrido');
if (datosRecorrido) {
    try {
        window.RuteaYaRecorridos.dibujar(JSON.parse(datosRecorrido.textContent)).catch(error => {
            document.getElementById('estado-mapa').textContent = window.RuteaYaHere.message(error);
        });
    } catch (error) { document.getElementById('estado-mapa').textContent = `No se pudo leer el recorrido: ${error.message}`; }
}
