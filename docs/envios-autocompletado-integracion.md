# Crear Envío con ubicaciones HERE confirmadas

## Estado y alcance

- Integración de Viajes guardada en `2a3e32d`, `feat: integrar ubicaciones HERE en viajes y paradas`.
- Esta etapa de Envíos permanece sin commit, push ni merge.
- Se conserva MVC → Lógica → EF. Crear Envío no llama a la API de Viajes.
- No hay entidades, tablas o migraciones nuevas. No se aplicaron migraciones ni se tocó SQL Server.
- No se implementa edición de envíos ni se modifica Transportes o el bloqueo de aceptación.

## Flujo

1. GET `/Envios/Crear` identifica al usuario y prepara dos contextos de alta con
   `TipoRecurso=Envio`, campos `RetiroEnvio`/`EntregaEnvio` y un mismo formulario.
2. Los inputs de retiro y entrega reutilizan `_CampoDireccionHere`, los scripts
   HERE, debounce, lookup y confirmación explícita de position/access.
3. Confirmar emite el comprobante persistente desde la selección servidor. MVC no
   considera coordenadas ni booleanos del navegador una selección verificada.
4. POST `/Envios/Crear` reconstruye `CrearEnvioDto.Ubicaciones` a partir del formulario
   servidor y comprueba usuario, rol, formulario, texto normalizado y último token.
   Cualquier `Ubicaciones` ligado libremente por el model binder se descarta.
5. `EnviosLogica.CrearAsync` exige ambos comprobantes, incluso cuando se llama sin
   el controlador. Verifica que correspondan a alta de Envío, rol y formulario correctos,
   y recupera los textos canónicos mediante `IUbicacionesConfirmadasServicio`.
6. Aplica las validaciones existentes al dato canónico. En particular, retiro y entrega
   iguales se rechazan aunque el cliente envíe textos distintos.
7. El servicio compartido vincula las dos ubicaciones, asigna texto/coordenadas y
   consume ambos comprobantes junto a la inserción del envío, en una transacción.
   Si falla SQL, se revierte también el consumo y se puede corregir/reintentar.

El constructor anterior de EnviosLogica conserva su parámetro de geocodificación para
compatibilidad de consumidores, pero el método de creación no lo utiliza. Se eliminó
el helper que seleccionaba el primer candidato. No hay una ruta de creación por texto
que vuelva a geocodificar o que prescinda de comprobantes.

## Reutilización y archivos

`FormulariosViajeServicio` se generalizó como `FormulariosDireccionesServicio`: comparte
caché, comprobación de usuario/formulario/campo, resolución, emisión y renderizado.
Viajes conserva su API; Envíos prepara su contexto directamente en Lógica. El recurso
del formulario determina el ámbito y se verifica al preparar cada guardado; no se
puede usar un formulario de Viajes para crear un Envío ni agregar paradas a un Envío.

Las rutas anteriores de direcciones de Viajes se conservan. El mismo controlador
sirve `/DireccionesEnvio/{formulario}/...` con los mismos controles de autenticación,
antiforgery, cancelación y mensajes HERE seguros. No se duplica el adaptador HERE.

Archivos principales nuevos/modificados:

- `CrearEnvioDto` y `UbicacionesEnvioDto` (contrato de los dos comprobantes).
- `EnviosLogica.CrearAsync` y eliminación de su geocodificación por texto.
- `CrearEnvioViewModel`, `DecimalHtmlBinder` y `EnviosController`.
- `NuevoEnvio.cshtml` y adaptación mínima del script de coordinación de formularios.
- Renombrado/generalización del servicio de formulario y actualización de su DI.

El resto del formulario conserva fecha límite, descripción, peso, monto, tamaño,
indicaciones adicionales, validaciones y mensajes. No se agrega persistencia para
indicaciones adicionales: ese control ya no se guardaba en el CRUD anterior.

Se corrigió un problema de cultura del binding: los inputs HTML number envían punto
decimal. En Crear Envío, peso y monto se ligan con cultura invariante, conservando sus
rangos. `2.5 kg` ya no se interpreta como `25 kg` en un servidor de cultura es-AR.
No se modifica globalmente la cultura de MVC ni los formularios de los compañeros.

## Coordenadas y registros anteriores

- `Envio.OrigenLat/Lng` y `DestinoLat/Lng` conservan su significado anterior: **position**.
- El punto de navegación elegido está en `RetiroUbicacion`/`EntregaUbicacion` y puede
  diferir. Para routing se debe leer ese punto, sin sustituirlo por las columnas antiguas.
- Las direcciones con comas se conservan completas, sin Split ni truncado.
- Los envíos anteriores siguen siendo legibles. Sus FK null siguen significando
  pendiente/no verificado; no se modifica su texto ni se infiere confirmación de números.
- Se mantienen los límites existentes de los textos de Envíos; un label demasiado
  largo se rechaza sin consumir el comprobante ni recortarlo.

## Pruebas

Resultado final: **711 tests aprobados, 0 fallidos y 0 omitidos**, con compilación
completa sin errores ni advertencias. Incluye 14 casos nuevos de integración de Envíos,
además de las 697 regresiones previas; las pruebas de Viajes siguen pasando.

Las pruebas HTTP y de navegador usan MVC/Lógica/EF reales con SQLite en memoria.
Solo HERE y la identidad de prueba son simulados; el geocodificador anterior es un
mock estricto sin llamadas. Se verifica también que Crear Envío no invoque el gateway
de la API de Viajes. No se ejecuta Program.cs de producción ni hay SQL Server real.

Cobertura nueva: publicación con dos ubicaciones, comas, position frente a navegación,
reuso, duplicación/intercambio de comprobantes, usuario ajeno, vencimiento, endpoint
anterior sin selección, texto/lat/lng/flags/usuario manipulados, reglas de negocio,
conservación de datos antiguos y error de inserción real provocado por un trigger
solo en SQLite efímero. Ese error revierte ambos consumos; el reintento crea un único envío.

Edge headless completa los dos inputs reales, confirma y publica conservando descripción,
fecha, tamaño, peso decimal y monto decimal. Se prueba el formulario, no solo el servicio.

## Qué falta para compatibilidad y prueba manual

Ya hay datos geográficos persistidos para los viajes/envíos creados o confirmados por
estos formularios. El siguiente adaptador de `IDatosConfirmadosOportunidadServicio`
podrá cargar el recorrido en orden y retiro/entrega desde sus FK, comprobando que todos
los snapshots estén guardados/consumidos y correspondan al recurso y propietario.
Para un envío, ese propietario es el remitente, no necesariamente el conductor.

No se conectó el motor automáticamente en esta etapa. El proveedor conservador actual
sigue sin inventar información: faltan salida concreta con zona/fecha, disponibilidad,
capacidad restante y semántica temporal del vencimiento. Las ubicaciones por sí solas
no habilitan aceptación ni permiten declarar compatibilidad completa.

Para probar MVC contra desarrollo habrá que autorizar y aplicar primero la migración
de la base persistente, usar la misma base desde MVC/API y configurar la clave privada
de HERE en MVC. Después abrir Mis envíos → Nuevo envío, seleccionar/confirmar retiro
y entrega y publicar. Verificar el estado en listado/detalle y el guardado normalizado.
No arrancar este modelo EF contra un esquema sin las columnas nuevas. No se aplicó
la migración ni se hicieron solicitudes HERE reales en esta etapa.
