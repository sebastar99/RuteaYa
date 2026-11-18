# Corrección del flujo HERE y límites de integración

## Causas comprobadas en el código

1. `BusquedaDireccionesHereServicio` capturaba la cancelación del navegador y
   fabricaba otra `OperationCanceledException("Búsqueda de dirección cancelada.")`.
   Ese texto viene de nuestro adaptador, no de una respuesta HTTP de HERE. Una
   búsqueda nueva aborta la anterior mediante `AbortController`; cambiar de página
   o cerrar la pestaña también puede cancelar `RequestAborted` en MVC.
2. MVC devolvía un 503 vacío para respuestas HERE muy distintas, o HTML/404 para
   ciertos tokens vencidos. El cliente descartaba el estado y el cuerpo y mostraba
   siempre el mismo mensaje. Un login redirigido terminaba igualmente como error genérico.
   No había información suficiente para atribuir el fallo observado a permisos,
   cuota, red o vencimiento concreto.
3. El cálculo dependía de un submit convencional con redirect, sin estado de carga
   ni prevención de dobles envíos. El manejo de errores del mapa comenzaba después
   de parsear JSON y crear las capas, dejando excepciones fuera de su frontera.
4. La previsualización no preparaba los campos hasta otro POST de activación.

No se dispone de la respuesta/log histórico del incidente del usuario; no se afirma
que un 401/403 haya sido su causa concreta ni que se haya reproducido su fallo con
la misma dirección y sesión. Se corrigieron los defectos comprobables y se agregó
diagnóstico para distinguir incidentes nuevos.

## Correcciones

- GET de previsualización autoriza y prepara directamente la sesión de direcciones.
  No necesita pulsar "Elegir ubicaciones". Esto no llama a HERE hasta escribir.
- Dropdown debajo del input, mínimo de tres caracteres, debounce de 300 ms,
  clic, flechas arriba/abajo, Enter para seleccionar y Escape. Enter no envía el formulario.
- Continúan siendo explícitas la elección de position/access y su confirmación.
  Lookup nunca confirma ni elige un acceso automáticamente.
- Las cancelaciones esperadas conservan su identidad en los adaptadores y se manejan
  en la frontera MVC como 499 sin pantalla de error. No se genera una segunda OCE.
- El cliente ignora abortos normales y respuestas fuera de orden; no ignora errores reales.
- Cálculo mediante botón explícito y fetch JSON: valida confirmaciones, bloquea
  doble clic, muestra carga, conserva errores y permite reintentar sin recargar.
- Modelo de mapa común a Razor y AJAX con coordenadas `[latitud,longitud]`, sin
  depender del casing Pascal/camelCase. Dibuja secciones independientes, conserva
  métricas, puntos e identificadores, y muestra fallos de geometría/CDN/fondo.
- Se verifica nuevamente usuario, viaje, sesión, huella y campo antes de usar los
  tokens. La sesión y la selección tienen vencimiento absoluto; no se renuevan al confirmar.
- No se modificó el bloqueo de aceptación ni se añadió persistencia de coordenadas.

### Clasificación de errores públicos

| Situación | Resultado |
| --- | --- |
| Nueva búsqueda / navegación / desconexión | AbortError ignorado por cliente; RequestAborted manejado en MVC (499) |
| Timeout del adaptador HERE, sin aborto del navegador | 504, `here_timeout` |
| Cancelación inesperada fuera de RequestAborted/timeout | 500, `cancelacion_inesperada` |
| HERE 401 / 403 | 503 con `here_401` / `here_403`, mensaje de autenticación/permisos |
| HERE 429 | 503 con `here_429`, mensaje de límite de solicitudes |
| HERE 5xx / red / otros HTTP | Mensaje diferenciado, preservando únicamente estado seguro |
| Clave no configurada | 503, `here_no_configurado` |
| Respuesta HERE inválida o ruta no utilizable | 503, `here_respuesta_invalida` |
| Token/sesión no disponible o viaje editado | 409, `seleccion_vencida`, renovar/reseleccionar |
| Ubicaciones incompletas / parámetros inválidos | 400 con mensaje público |
| Usuario no autenticado / sin autorización | 401/403 o login detectado por el cliente |
| Excepción interna | 500 con referencia; no se expone su mensaje remoto |

Los logs conservan servicio/operación, estado HTTP del proveedor, tipo de error y
referencia de solicitud. No se registra la clave, URL autenticada, cuerpo remoto
ni objeto de excepción que pueda incluirlos. Las cancelaciones normales se registran
solo a nivel Debug. No hay catch genérico vacío de producción.

Visual Studio puede detenerse **al lanzarse** una cancelación normal de HttpClient
si está habilitada la opción de interrumpir en excepciones lanzadas. Eso es una
notificación de primera oportunidad, no prueba de un error sin manejar. La app ahora
la maneja en MVC y el navegador sigue funcionando. Para depurar sin esas pausas,
revisar la regla específica de OperationCanceledException/TaskCanceledException en
Exception Settings; no desactivar indiscriminadamente todos los errores CLR.

## Auditoría de formularios: parte detenida antes de cambiar contratos

| Campos existentes | Estado y cambio necesario |
| --- | --- |
| Previsualización: origen, destino y cada parada | Integración directa corregida; datos verificados solo en memoria, sin persistencia |
| Nuevo/editar viaje: Origen y Destino | `Viaje`, `CrearViajeDto`, `EditarViajeDto` y respuesta solo conservan texto. Para persistir la selección faltan ID HERE, position, punto de navegación elegido y procedencia/confirmación; exige contrato, mapeos y esquema |
| Nuevo/editar viaje: `ParadasTexto` | Es un único texto separado por comas. Una dirección normalizada ya contiene comas: no es seguro tratarla como una lista de selecciones. Hace falta un campo estructurado por parada y DTO correspondiente |
| Detalle de viaje: agregar parada | Se recibe `ubicacion` como string; `Parada` solo tiene texto y orden. Requiere contrato y persistencia de selección verificada |
| Nuevo envío: `OrigenDir` (retiro), `DestinoDir` (entrega) | La entidad tiene lat/lng, pero `EnviosLogica.CrearAsync(usuarioId, CrearEnvioDto)` vuelve a geocodificar texto y toma el primer candidato. No acepta la selección verificada: requiere ampliar el contrato de creación y su mapeo antes de integrar el input |
| Editar envío | No existe una acción/formulario de edición de envío en el módulo actual |

No se insertaron componentes meramente cosméticos en esos CRUD: mostrar sugerencias
sin que el backend persista la misma selección incumpliría el requisito.

Antes de continuar esa parte hay que acordar:

1. Un contrato de ubicación verificada con dirección normalizada, ID HERE, position
   y punto de navegación escogido, tipo/procedencia y fecha de confirmación. El backend
   debe obtenerlo de estado servidor, nunca confiar en un booleano/lat/lng libres del navegador.
2. En Viajes/Paradas, campos o una ubicación relacionada/owned que conserven esos datos;
   actualizar entidades, configuración EF, DTOs, mapeos y una migración, **previa autorización**.
3. MVC de Viajes llama a la API propia. Su caché temporal no puede autorizar por sí
   sola a la API: esta debe verificar la selección en un servicio compartido o resolver
   el ID HERE del lado servidor bajo un contexto autorizado. Enviar solo coordenadas
   desde MVC dejaría abierta la entrada directa a la API.
4. En Envíos, cambiar el contrato de creación para consumir las ubicaciones verificadas
   y no reemplazarlas con otra geocodificación. Acordar si las lat/lng existentes
   representan position o navegación y cómo persistir el ID y la confirmación.

No se hicieron esos cambios ni se ejecutaron migraciones. No se modificaron datos SQL.

## Comprobación manual real, separada de los tests

Se ejecutó una sonda temporal fuera del repositorio con User Secrets de MVC y
variables de entorno, sin mostrar la clave, sin iniciar MVC completo y sin SQL:

- Autocomplete de una dirección pública en Buenos Aires: **5 candidatos**.
- Lookup del primer ID devuelto: **position válida y 1 access**.
- Routing de dos puntos de prueba públicos: **1 sección, 5.885 metros**.
- Raster copyright: **41 avisos**.
- Raster tesela de mundo: **PNG de 57.268 bytes**.

Esto verifica la configuración y esas respuestas de endpoints en ese momento.
No es una validación de todos los recorridos, ni demuestra la causa de un incidente
anterior. Los tests automatizados continúan usando exclusivamente HERE simulado.

## Cómo probar desde MVC

1. Reiniciar MVC en Development con su configuración privada actual. Entrar a
   Oportunidades y abrir la previsualización de un viaje propio existente.
2. Escribir en los inputs que ya aparecen. Cambiar rápido el texto mientras busca:
   la anterior debe cancelarse sin pantalla de error. Probar flechas, Enter y clic.
3. Elegir sugerencias y confirmar position/access en todos los campos.
4. Calcular: debe mostrar carga, quedar en la misma página y presentar métricas y
   ruta. Un fallo debe mostrar motivo/referencia y permitir reintentar.
5. Verificar zoom/paneo y fondo HERE. La línea sola no demuestra que Raster funcione.
6. Dejar vencer una sesión o editar el viaje desde su pantalla habitual y comprobar
   que se exija renovar. Si falla HERE, conservar la referencia de la UI y consultar
   el log seguro sin compartir claves, secretos ni URLs autenticadas.

## Regresiones automatizadas

Validación final: solución compilada con **0 errores y 0 advertencias**;
**668 tests aprobados, 0 fallidos y 0 omitidos**, incluida la prueba Edge en este entorno.

Se prueban estados 401/403/429/5xx, timeout, configuración ausente, respuesta inválida,
cancelación normal/inesperada, fallo de routing sin guardar estado parcial, JSON de
mapa, sesión editada/vencida y secretos no filtrados. Edge headless ejecuta el JS
real sobre Razor y MVC en loopback con proveedores simulados: debounce, cancelación
en servidor, teclado, confirmaciones, doble clic, cálculo AJAX sin recarga, reintento,
login redirigido, token vencido y fallo de Leaflet. El trazado se comprueba con Leaflet
simulado y el resultado real del endpoint del host de prueba. No se requiere CDN
ni HERE real en esos tests; se omite solo la prueba Edge si ese navegador no está instalado.
