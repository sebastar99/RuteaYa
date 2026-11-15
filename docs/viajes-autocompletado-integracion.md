# Integración mínima de ubicaciones confirmadas en Viajes

## Estado de las dos etapas

- Base persistente cerrada en `6a9047d` (`feat: persistir ubicaciones confirmadas HERE`),
  con 686 pruebas aprobadas y migración generada **sin aplicar**.
- Integración de formularios de esta etapa: sin commit, sin push ni merge.
- No hay nuevas entidades, tablas ni migraciones. Se reutiliza `UbicacionesConfirmadas`.
- No se modificó el CRUD de Envíos ni se habilitó `/Oportunidades/Aceptar`.

## Qué funciona

En Crear y Editar Viaje, origen, destino y cada parada usan `_CampoDireccionHere`:
escribir, ver sugerencias, seleccionar, resolver por lookup y confirmar position/access.
Al confirmar se emite el comprobante persistente desde el snapshot servidor, no desde
coordenadas o un booleano del POST. El componente devuelve ese comprobante al formulario.

Las paradas son filas independientes que pueden agregarse, quitarse, subirse y bajarse.
Se usa el binding `Paradas.Index` con claves estables, no direcciones separadas por comas.
`ViajesApiServicio` ya no hace Split ni convierte una dirección con comas en varias paradas.
La propiedad antigua ParadasTexto queda solo por compatibilidad de modelos: el flujo
nuevo no la consume ni la muestra.

En edición se puede conservar exactamente la dirección que ya existe, incluso si es
legada y su FK es null. Cambiar su texto exige un comprobante. Una ubicación conservada
no se vuelve a geocodificar, no inventa coordenadas y no se marca confirmada por defecto.

## Flujo MVC → API → Lógica

1. `PanelController` prepara el formulario mediante `FormulariosViajeServicio`.
2. El servicio pide a `IViajesApiServicio` los contextos; el endpoint autenticado
   `POST api/viajes/ubicaciones/contexto` usa los claims y `IUbicacionesConfirmadasServicio`
   para verificar propiedad/revisión. La API no necesita una clave ni un cliente HERE.
3. MVC guarda un token de formulario aleatorio en memoria, ligado al usuario y viaje,
   a los campos originales y sus contextos autorizados. Vence a los diez minutos.
4. `DireccionesViajeController` reutiliza `SeleccionDireccionServicio` para autocomplete
   y lookup, con el ámbito persistente exacto del campo.
5. Confirmar primero valida la selección de la caché y después llama a `EmitirAsync`.
   Se persiste solo el hash del comprobante, sus datos verificados y vencimiento.
6. El POST del formulario verifica el contexto servidor, los IDs de parada, el texto
   canónico y el último comprobante emitido. La API recibe una extensión opcional
   `Ubicaciones` de CrearViajeDto/EditarViajeDto; nunca entidades de EF ni FK libres.
7. La API sigue usando los endpoints y métodos existentes de Viajes. Lógica recupera
   los textos de los comprobantes y aplica las validaciones existentes a esos datos
   canónicos, no al texto libre del navegador.
8. Recurso, días, paradas, FK y consumo se guardan juntos. Un fallo revierte también
   el consumo. El backend vuelve a validar usuario, ámbito, revisión y vencimiento.

Los comprobantes de otro usuario, formulario, rol o recurso no sirven. Para paradas
existentes, la clave se deriva del formulario y del ID de parada y se verifica de
nuevo en la API/Lógica. No puede usarse el comprobante de una parada para cambiar otra.

## Cambios de contratos y lógica que hay que coordinar

- `CrearViajeDto.Ubicaciones` (EditarViajeDto hereda de CrearViajeDto).
- `UbicacionesViajeDto`, `CampoViajeUbicacionDto`, `SolicitudContextoViajeDto` y la
  identidad estable de paradas del formulario. No contienen coordenadas autoritativas
  proporcionadas por el navegador.
- Propiedades de lectura `OrigenConfirmado`, `DestinoConfirmado` y `ParadaDto.Confirmada`.
- Método de contexto en `IViajesApiServicio` y extensión de su serialización actual.
- La implementación original de Viajes conserva la validación de días, horarios,
  capacidad, peso, desvío, direcciones distintas, longitud y paradas repetidas. Se
  reutiliza `Validar` con los textos canónicos. El máximo del flujo estructurado es 50.
- `ViajesLogica.Ubicaciones.cs` encapsula el nuevo caso dentro de la misma clase parcial;
  no crea un CRUD paralelo ni una segunda implementación de sus reglas.
- `IUbicacionesConfirmadasServicio` incorpora lectura validada de la dirección y
  guardado de agregado Viaje, incluyendo el caso sin comprobantes nuevos en edición.
- La estrategia de desplazar temporalmente órdenes se ejecuta dentro de la transacción
  del guardado, antes del consumo. Se mantienen los IDs/FK de las paradas conservadas;
  no se eliminan y recrean todas. No se cambia la estructura de la base.

Los endpoints anteriores siguen aceptando su contrato legado sin atribuir confirmación
a textos libres. En edición, un cliente anterior no puede cambiar textos de una ruta
confirmada y conservar coordenadas anteriores: debe usar el contrato nuevo. Si la
ruta no cambia, la edición anterior conserva las paradas y sus FK.

Crear/Editar Viaje usan el flujo nuevo. La acción histórica de agregar una parada
textual desde DetalleViaje se conserva como legado y no se presenta como confirmación
HERE; su adaptación visual no forma parte de estos dos formularios.

## Límites explícitos del TP

- Se conservan límites de 150 caracteres en Viajes/Paradas. Si el label HERE los
  excede, se rechaza con validación, sin truncar ni generar otra ubicación.
- Los campos dinámicos tienen hasta 50 espacios por sesión de formulario; si se
  agotan tras quitar/agregar muchas filas, hay que reabrir el formulario.
- La selección y el formulario son temporales. Tras reiniciar MVC o vencer la sesión
  hay que reabrirlo; no se agrega infraestructura distribuida ni guardado de borradores.
- La revisión protege los datos de ubicación; no se añadió una nueva política general
  de concurrencia para horarios/capacidad. Las reglas actuales del módulo se conservan.
- Cambiar direcciones no habilita aceptación ni presupone capacidad/salida concretas.

## Pruebas de integración

Validación final: **697 tests aprobados, 0 fallidos y 0 omitidos**, con compilación
de toda la solución sin errores ni advertencias. Son las 686 regresiones anteriores
más 11 casos de integración de esta etapa, incluida la prueba Edge.

`ViajesDireccionesIntegracionTest` levanta MVC y API reales en loopback, con su lógica
real y SQLite en memoria compartido. Solo la identidad y HERE se simulan; no se ejecuta
Program.cs de producción, no se conecta a SQL Server ni se aplican migraciones.

Se verifica creación, direcciones normalizadas, navegación frente a position, paradas
con comas y orden, reuso, usuario ajeno, campo intercambiado, vencimiento, ID de parada
intercambiado, datos canónicos frente a texto libre y validaciones de negocio. También
se verifica edición de datos legados, conservación de IDs y cambio parcial de una
ubicación confirmada. El test Edge ejecuta el componente real, agrega una parada
dinámica y guarda a través de MVC → API → Lógica.

## Para probar con una base de desarrollo migrada (pendiente)

1. Coordinar con el responsable de Viajes estos contratos, validaciones y edición por
   diferencias. No incorporar solo los formularios sin la API y Lógica compatibles.
2. Revisar el historial real de migraciones y respaldar la base de desarrollo; aplicar
   la migración de la base persistente **solo con autorización**, fuera de esta tarea.
3. API y MVC deben apuntar a la **misma base**: MVC emite los comprobantes y la API los
   consume. La clave HERE queda en los secretos de MVC; el JWT y la URL de la API deben
   estar configurados como en el flujo del compañero.
4. Arrancar API y MVC de esta misma versión. Entrar a Mis viajes → Nuevo viaje.
   Confirmar origen/destino, agregar dos paradas con direcciones que contengan comas,
   reordenarlas y guardar. Verificar su orden y confirmación al editar.
5. Editar un viaje antiguo sin cambiar las direcciones: debe conservar texto e IDs
   y mostrar las ubicaciones pendientes. Después confirmar solo una ubicación nueva.
6. Probar sesión vencida, error HERE y un guardado inválido: deben mostrarse mensajes,
   no duplicarse viajes ni perderse comprobantes por un fallo de validación.

No se ejecutaron estos pasos contra SQL Server en esta etapa. El esquema actual sin
migrar no admite el nuevo modelo EF: no arrancar esta versión contra esa base hasta
coordinar la migración. No se hicieron solicitudes reales a HERE en esta etapa.
