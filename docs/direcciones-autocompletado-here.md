# Autocompletado y confirmación de direcciones HERE

## Probar en MVC

1. Iniciá MVC en `Development`, con la configuración privada existente
   `HereRouting:ApiKey` (User Secrets de MVC o `HereRouting__ApiKey`). La clave
   necesita acceso a HERE Geocoding & Search v7, incluyendo autocomplete y lookup.
2. Iniciá sesión con un usuario que tenga un viaje propio. Desde Oportunidades,
   seleccioná ese viaje y abrí **Previsualizar recorrido del viaje**. También
   se puede acceder a `/Recorridos/{viajeId}`.
3. Los campos aparecen directamente al abrir la previsualización. La sesión se
   prepara en memoria, sin consultar HERE ni modificar el viaje. No hay botón de activación.
4. Editá el campo de origen, parada o destino. A partir de **3 caracteres**, tras
   **300 ms** sin escribir, se solicitan hasta cinco sugerencias. Agregá localidad
   y país para desambiguar: no se inventa un centro de búsqueda ni se limita el país.
5. Elegí una sugerencia mediante su botón (nombre, localidad si HERE la informa y
   dirección). El servidor resuelve su identificador con **lookup**; la sugerencia
   por sí sola no contiene ni confirma coordenadas.
6. Revisá la dirección normalizada, identificador HERE, tipo de resultado y de
   puerta, estimación/interpolación. Elegí explícitamente **position** (referencia)
   o uno de los **access** (navegación) y pulsá **Confirmar ubicación**.
7. Cuando todas estén confirmadas, pulsá **Calcular recorrido con ubicaciones
   confirmadas**. Se reutiliza el routing y mapa existentes. La lista conserva
   dirección normalizada, identificador HERE y punto de navegación elegido;
   los marcadores usan la dirección elegida, no el texto original del viaje.

Se conservan los endpoints anteriores para compatibilidad, pero la pantalla usa
autocompletado directo. Las selecciones de sesiones distintas no se intercambian.

Cambiar el texto o el acceso invalida la confirmación visible. El componente
cancela búsquedas/selecciones anteriores con `AbortController` y descarta respuestas
fuera de orden mediante una versión local. No restaura tokens al recuperar la
página desde la caché del navegador. Al recargar un formulario pendiente hay que
volver a seleccionar y confirmar. Si vence la sesión, iniciá otra previsualización.

Las direcciones elegidas solo afectan esta previsualización, no el CRUD del viaje.
No se crean datos de prueba ni se guardan coordenadas en SQL. Para ver el mapa hace
falta además acceso a HERE Routing y Raster Tile, como en la previsualización previa.

## Endpoints HERE y contratos

La interfaz nueva `IBusquedaDireccionesServicio` está en Lógica; su adaptador
`BusquedaDireccionesHereServicio` está en MVC. Se conserva `IGeocodificacionServicio`
y se reutiliza su parser de ubicaciones mediante un método interno de la misma
assembly, sin modificar la semántica de la geocodificación existente.

- `GET https://autocomplete.search.hereapi.com/v1/autocomplete`
  con `q`, `limit=5` y `lang=es`. Valida 3–200 caracteres útiles, sin caracteres de
  control. Conserva `title`, `address.label`, `address.city` e identificador HERE.
  No transforma sugerencias en coordenadas ni considera la primera confirmada.
- `GET https://lookup.search.hereapi.com/v1/lookup` con `id` y `lang=es`.
  Se verifica que el ID devuelto sea exactamente el solicitado. Se conserva
  `address.label` como dirección normalizada, `position`, todos los `access`,
  tipo y calidad disponibles. Se rechaza una ubicación incompleta o fuera de rango.

La clave usa la configuración existente y solo se añade a las solicitudes salientes
del servidor. Cliente con timeout de 30 s, sin loggers HTTP ni redirecciones.
Las excepciones no incluyen URL, cuerpo, consulta ni excepción interna del proveedor.
Los errores no producen ubicaciones locales, fallback ni confirmaciones automáticas.

## Selección verificada en servidor

`SeleccionDireccionServicio` guarda entradas inmutables y snapshots serializados
en `IMemoryCache`. Emite tokens aleatorios de 256 bits distintos por etapa:

1. **Sugerencia:** token ligado al ID devuelto por autocomplete, usuario y ámbito.
2. **Resuelta:** token ligado a la respuesta real de lookup; `Confirmada=false`,
   acceso y navegación ausentes.
3. **Confirmada:** token ligado al resultado y al índice elegido: `-1` es position,
   `0..n-1` identifica un access. Solo esta etapa permite `ObtenerConfirmada`.

`EstadoSeleccionDireccion` conserva token, resultado de geocodificación, estado de
confirmación, índice y coordenadas de navegación. El `RequiereConfirmacion` del DTO
de geocodificación mantiene su significado original de candidato; el estado del
flujo está en `EstadoSeleccionDireccion.Confirmada`.

El cliente solo envía tokens e índice de acceso. No se reciben coordenadas ni un
booleano de confirmación como fuente de verdad. Las respuestas del consumidor son
copias: modificarlas no cambia el snapshot en caché. Un ID HERE escrito libremente
no reemplaza un token de sugerencia.

Los tokens vencen a los diez minutos de la sugerencia, sin renovar el plazo al
resolver o confirmar. También pueden perderse por reinicio/expulsión de caché.
En previsualización rige además el vencimiento de la sesión del recorrido, que
puede ser anterior al de la sugerencia.

El consumidor debe construir el **ámbito del lado servidor** y autorizar el recurso.
En esta primera integración es `recorrido:{sesion}:{indice}`. Cada petición a las
acciones de direcciones de `RecorridosController` vuelve a autorizar el viaje y
verifica sesión, huella de direcciones/paradas y campo. Los POST usan antiforgery.
Las respuestas llevan `no-store`. El cálculo recupera todas las confirmaciones por
su ámbito exacto, respeta el orden de las paradas y revalida el viaje tras routing.

## Reutilización por formularios del equipo

No se modificaron los formularios ni el CRUD de Viajes o Envíos. Para integrar
posteriormente origen, destino, paradas, retiro o entrega, se necesita:

1. Renderizar `Views/Shared/_CampoDireccionHere.cshtml` con un
   `CampoDireccionViewModel` por campo: `IdHtml` único, etiqueta, texto inicial,
   nombre del hidden de token y tres URLs autorizadas para sugerir, resolver y confirmar.
2. Incluir una vez `~/css/direcciones-here.css`, `~/js/here-http.js` y luego `~/js/direcciones-here.js`. No usa
   npm, paquetes NuGet nuevos, jQuery ni SDK HERE en el navegador.
3. Ubicarlo dentro de un formulario con antiforgery. El script toma el token del
   formulario y lo envía en el header `RequestVerificationToken` para los POST.
4. Preparar un contexto/ámbito servidor ligado al usuario y a ese formulario/recurso
   y campo. Para altas, usar un contexto temporal de formulario validado por servidor;
   para edición, incluir la revisión del recurso e invalidarla si cambia. No aceptar
   un ámbito arbitrario enviado por el navegador como autorización.
5. Las acciones del consumidor autorizan ese contexto y llaman a
   `SeleccionDireccionServicio.SugerirAsync`, `ResolverAsync` y `Confirmar`. No deben
   aceptar IDs HERE directos ni coordenadas del navegador para saltarse el flujo.
6. El DTO del formulario transporta los tokens de confirmación. Antes de guardar,
   el servidor llama a `ObtenerConfirmada(usuarioId, ambitoEsperado, token)` por campo
   y usa exclusivamente esa dirección/ID/position/access verificados. La adaptación
   del CRUD y su persistencia debe acordarse con los responsables de esos módulos.

El script emite `direccion:estado` (con `detail.confirmada`) al cambiar la confirmación.
Sirve para actualizar la interfaz; nunca autoriza el guardado. El habilitado del botón
de cálculo de recorridos es un adaptador de página separado del componente genérico.
Los endpoints actuales `/Recorridos/{viajeId}/Direcciones/...` están autorizados para
previsualización; los futuros formularios necesitan sus propias acciones/contextos.

Estas confirmaciones temporales **no alimentan** `IDatosConfirmadosOportunidadServicio`
ni habilitan `/Oportunidades/Aceptar`. Tampoco prueban que una calle/localidad o un
punto interpolado sea una dirección de entrega exacta: se muestra el tipo informado
por HERE para que el usuario lo revise.

## Verificación

- `BusquedaDireccionesHereServicioTest`: endpoints oficiales, encoding, nombre y
  localidad, position/access, ID coincidente, validaciones, JSON inválido, cero
  resultados, HTTP no exitoso, errores sin credenciales, timeout y cancelación.
- `SeleccionDireccionServicioTest`: etapas, confirmación explícita, cruces de
  usuario/campo/tipo de token, vencimiento absoluto y snapshots independientes.
- `RecorridosVistaTest`: Razor con tres componentes, auth/claims, antiforgery,
  errores, lookup y confirmación, datos agregados libremente ignorados, tokens
  incompletos/cruzados, ruta en orden, XSS escapado y viaje editado/ajeno.
- Las respuestas HERE de los tests son simuladas, sin SQL real ni migraciones. Los hosts MVC
  solo hacen HTTP en loopback y no ejecutan Program.cs de la aplicación.

Además de Razor, hay una regresión en Edge headless que ejecuta el JavaScript real
contra MVC en loopback con HERE simulado: debounce, aborto de búsqueda, teclado,
confirmación, cálculo AJAX, fallo de CDN y errores claros sin recargas. El trazado
se verifica también con una API Leaflet simulada y el JSON del endpoint de cálculo.
La sonda manual de endpoints HERE reales es independiente de los tests: ver
[diagnóstico y límites de persistencia](here-diagnostico-y-limites-persistencia.md).

## Referencias oficiales consultadas

- [Autocomplete v7](https://docs.here.com/geocoding-and-search/reference/get_autocomplete.md)
- [Lookup v7](https://docs.here.com/geocoding-and-search/reference/get_lookup.md)
