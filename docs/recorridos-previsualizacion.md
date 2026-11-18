# Previsualización de recorridos propios

La pantalla también dispone de [autocompletado y confirmación por lookup de HERE](direcciones-autocompletado-here.md),
mediante **Elegir ubicaciones con autocompletado HERE**, sin modificar el viaje publicado.

## Acceso y recorrido de la pantalla

En **Oportunidades**, seleccioná un viaje y usá **Previsualizar recorrido del viaje**.
El enlace también está en el detalle de un candidato. Está disponible aunque el viaje
no tenga envíos candidatos. Acceso directo, con sesión MVC: `/Recorridos/{viajeId}`.

1. **Buscar ubicaciones del viaje** consulta HERE Geocoding desde el servidor para
   origen, paradas por `Orden` y destino. Abrir la pantalla no llama a HERE.
2. Para **cada** ubicación, elegí un candidato y, por separado, su punto para navegar:
   `position` (referencia, no necesariamente acceso vehicular) o un `access` concreto.
   No se preselecciona nada, incluso si hay un único resultado o puntaje 1.
   Cambiar candidato borra la selección anterior de su punto de navegación.
3. **Confirmar ubicaciones y calcular recorrido** consulta HERE Routing v8 con
   los puntos elegidos, sin reordenar las paradas.
4. La pantalla muestra metros/kilómetros y duración reales del proveedor, la geometría
   completa por sección, marcadores de navegación y círculos de `position` de referencia.
   La lista textual conserva la dirección normalizada y el `position`/`access` elegido.

Los candidatos muestran tipo de resultado, tipo de puerta, corrección de número,
posición estimada/interpolada y puntaje de coincidencia (no equivale a precisión en metros).
Sin candidatos para alguna dirección no se puede calcular: corregí el viaje por su
pantalla habitual y volvé a buscar.

## Extensión de geometría

- `IRutasServicio.CalcularAsync` y `ResultadoRutaDto` conservan su contrato y parámetros.
- La interfaz nueva e independiente `IRutasGeometriaServicio` devuelve totales y
  geometría mediante `ResultadoRutaGeometriaDto`.
- `RutasHereServicio` implementa ambas interfaces y comparte validación, consulta y
  suma de resúmenes. Solo el nuevo método pide `return=summary,polyline`.
- Se usa automóvil, `routingMode=fast`, `alternatives=0`, `departureTime=any`, origen,
  destino y un `via` stopover por parada, en el orden original. No se agrega un envío.
- Se decodifica **cada sección** de la primera ruta usando Flexible Polyline v1,
  con precisión del encabezado y consumo de dimensión adicional si existe.
  Se rechazan geometrías ausentes/truncadas/inválidas, coordenadas fuera de rango,
  secciones insuficientes, resúmenes incompletos y avisos críticos. El mapa dibuja
  secciones por separado; no une los marcadores con líneas inventadas.

## Autorización y estado temporal

`RecorridosController` exige autenticación MVC y claim `NameIdentifier`/`sub` válido.
Cada operación, incluidas las teselas, vuelve a consultar el viaje propio mediante
`IViajesLogica.ObtenerPorIdAsync(usuarioId, viajeId)`. Un viaje ajeno/inexistente da 404.
Las búsquedas y confirmaciones son POST con antiforgery.

`PrevisualizacionRecorridoServicio` guarda instantáneas serializadas en `IMemoryCache`:
token aleatorio de 256 bits, usuario, viaje, huella de direcciones/paradas y vencimiento
absoluto de **10 minutos desde que termina la búsqueda**. Confirmar no renueva el plazo.
El navegador solo envía el token y los índices elegidos; las coordenadas usadas para
routing proceden exclusivamente de los candidatos guardados en el servidor.
Tokens de otro usuario/viaje, selecciones incompletas o índices alterados se rechazan.
Si el viaje cambia, hay que buscar nuevamente. Se revalida también después del routing.

La caché es local al proceso: reiniciar MVC o una expulsión de caché puede terminar
antes la sesión. No es persistencia ni una fuente de compatibilidad: no alimenta
`IDatosConfirmadosOportunidadServicio`, no interpreta una salida concreta, no confirma
capacidad y no reserva ni acepta paquetes. No hay escrituras SQL en este flujo.

Límite de esta pantalla: 18 paradas (20 ubicaciones contando extremos), con órdenes
únicos. Las búsquedas se hacen secuencialmente y cada cliente HERE tiene timeout de
30 segundos. No hay reintentos automáticos ni sustitución de errores por rutas mock.

## Mapa, API Key y atribuciones

Se usa **Leaflet 1.9.4**, licencia **BSD-2-Clause**, desde unpkg con versión fija,
SRI y `crossorigin`. No requiere paquete NuGet/npm. Ver [licencia](leaflet-licencia.txt).
Necesita acceso del navegador a `unpkg.com`. El JavaScript propio está en
`RuteaYa/wwwroot/js/recorridos.js`.

El fondo es **HERE Raster Tile API v3**, recurso `base`, proyección `mc`, estilo
`explore.day`, PNG, zoom 0–18. Dos acciones de MVC funcionan como proxy autenticado:

- `/Recorridos/{viajeId}/Atribuciones?sesion=...`: obtiene los avisos de los grupos
  asociados a `resources.base.styles.explore.day`. Se devuelven solo campos públicos
  conocidos de esos avisos, no la respuesta cruda del proveedor.
- `/Recorridos/{viajeId}/Tesela/{z}/{x}/{y}?sesion=...`: valida índices de tesela y
  devuelve únicamente imágenes PNG; no admite una URL/host arbitrario.

No se solicitan teselas antes de obtener las atribuciones. El mapa mantiene visible
`© año HERE`, etiquetas de los proveedores que intersectan el área/zoom (incluido
el cruce del antimeridiano) y el enlace legal de HERE. Los textos completos aparecen
en el desplegable de avisos. Tanto avisos como etiquetas de marcadores se tratan
como texto, no HTML del proveedor. Si falla copyright, se informa y no se carga el
fondo; la geometría calculada sigue visible. Los fallos de teselas también se informan.

La clave sigue siendo **solo servidor**, `HereRouting:ApiKey`, configurada en User
Secrets de MVC (`RuteaYa.Web-HereRouting`) o en `HereRouting__ApiKey`. Ver
[configuración existente](rutas-here.md#api-key-local-fuera-del-repositorio).
Debe tener acceso a **Geocoding**, **Routing v8** y, para el fondo, **Raster Tile v3**.
Habilitar Raster Tile puede requerir configuración adicional de la cuenta HERE.
No se agregó ninguna credencial al repositorio.

El navegador ve URLs de MVC y un token temporal, nunca la API Key ni URLs autenticadas
de HERE. Los clientes HERE deshabilitan logging HTTP y redirecciones; los errores no
reenvían cuerpo, URL ni excepción interna del proveedor. Las respuestas de la pantalla
y del proxy llevan `no-store`. No se guarda caché persistente de teselas.
La revalidación de propiedad por tesela implica lecturas SQL adicionales al navegar.

## Verificación automatizada y prueba manual

- `RecorridosHereTest`: ejemplo oficial de Flexible Polyline, dimensión adicional,
  geometrías inválidas, secciones, orden de `via`, sumas, copyright por estilo,
  teselas fuera de rango, PNG y errores sin credenciales.
- `PrevisualizacionRecorridoServicioTest`: autorización, confirmación explícita,
  selección de access/position, índices manipulados, token cruzado, vencimiento,
  viaje editado, cero candidatos y errores sin estado parcial.
- `RecorridosVistaTest`: host MVC real en loopback con autenticación simulada,
  formularios antiforgery, Razor, rutas, acceso al proxy y escape de texto/JSON.

Todas las respuestas HERE son simuladas; los hosts de prueba no ejecutan `Program.cs`,
no cargan User Secrets ni consultan bases de datos reales. La prueba de Razor no
ejecuta Leaflet en un navegador. En este entorno no hay Node.js para `node --check`.

Para probar manualmente (pendiente; no se hicieron solicitudes HERE reales):

1. Configurá la clave de MVC con los tres servicios habilitados y arrancá MVC en
   `Development` por HTTPS. Iniciá sesión con un usuario que tenga un viaje propio
   y direcciones reales en una zona enrutable.
2. Abrí la previsualización desde Oportunidades. Verificá que no haya puntos elegidos,
   seleccioná cada candidato y `position` o `access`, y confirmá.
3. Comprobá geometría por calles, paradas en orden, cifras del proveedor, zoom/paneo,
   marcadores y avisos de copyright. En Network, las imágenes deben ir a MVC y no
   debe aparecer la API Key en HTML, JavaScript ni solicitudes del navegador.
4. Editá una dirección del viaje o esperá diez minutos y verificá que se exija buscar
   nuevamente. Una sesión de otro usuario no debe poder abrir ese viaje ni su proxy.

## Documentación oficial

- [Geometría de Routing v8](https://docs.here.com/routing/docs/routing-v8-route-geometry.md)
- [Especificación Flexible Polyline v1](https://github.com/heremaps/flexible-polyline/blob/master/README.md)
- [Leaflet quick start](https://leafletjs.com/examples/quick-start/)
- [HERE Raster: teselas](https://docs.here.com/map-rendering/reference/gettile.md)
- [HERE Raster: copyright y modelo de áreas/zoom](https://docs.here.com/map-rendering/reference/getcopyright.md)
- [Aviso obligatorio de copyright](https://docs.here.com/map-rendering/docs/required-copyright-notice.md)
- [Condiciones y avisos de proveedores](https://legal.here.com/en-gb/terms/general-content-supplier-terms-and-notices)
