(async () => {
    const assert = (v, m) => { if (!v) throw new Error(m); };
    try {
        assert(!document.querySelector('[data-direccion-here]'), 'No volver a pedir las direcciones guardadas.');
        assert(document.getElementById('datos-recorrido'), 'Debe calcularse al abrir.');
        for (let i = 0; i < 100 && !/Leaflet|Ruta dibujada/.test(document.getElementById('estado-mapa').textContent); i++) await new Promise(r => setTimeout(r, 30));
        const data = JSON.parse(document.getElementById('datos-recorrido').textContent);
        if (window.pruebaLeafletReal) {
            assert(typeof L !== 'undefined', 'Debe cargar Leaflet real, con su integridad verificada.');
            assert(document.querySelector('.leaflet-overlay-pane svg path'), 'Debe existir el trazado SVG real.');
            assert(document.querySelector('.leaflet-overlay-pane svg path').getAttribute('d').length > 0, 'Geometría visible.');
            assert(document.getElementById('mapa-recorrido').getBoundingClientRect().height > 200, 'Mapa con altura visible.');
        } else {
        assert(document.getElementById('estado-mapa').textContent.includes('Leaflet'), 'El fallo de CDN debe ser visible.');
        let lineas = 0;
        const layer = () => ({ addTo() { return this; }, bindPopup() { return this; }, on() { return this; } });
        window.L = {
            map: () => ({ remove() {}, fitBounds() {}, invalidateSize() {}, getZoom: () => 10,
                getBounds: () => ({ getNorth: () => 90, getSouth: () => -90, getWest: () => -180, getEast: () => 180 }),
                attributionControl: { addAttribution() {}, removeAttribution() {} }, on() {} }),
            polyline: points => { assert(points.every(p => p.every(Number.isFinite)), 'Coordenadas numéricas.'); lineas++; return layer(); },
            marker: layer, tileLayer: layer, featureGroup: () => ({ getBounds: () => ({ extend() {} }) })
        };
        await window.RuteaYaRecorridos.dibujar(data);
        assert(lineas === data.secciones.length, 'Dibujo de todas las secciones.');
        assert(document.getElementById('estado-mapa').textContent.includes('Ruta dibujada'), 'Dibujo confirmado.');
        }
        assert(document.querySelector('[data-resumen-recorrido]').textContent.includes('3,21'), 'Distancia visible.');
        if (!sessionStorage.getItem('recorrido-recalculado')) {
            sessionStorage.setItem('recorrido-recalculado', 'si'); document.querySelector('[data-recalcular-recorrido]').click(); return;
        }
        window.resultadoPruebaDirecciones = 'OK';
    } catch (e) { window.resultadoPruebaDirecciones = 'ERROR: ' + e.message; }
})();
