# Base persistente de ubicaciones: primera etapa, sin integración de formularios

## Decisión mínima

Una sola tabla `UbicacionesConfirmadas` representa tanto el comprobante pendiente
como la ubicación definitiva. No hay tabla adicional de comprobantes ni biblioteca
`RuteaYa.Integraciones.Here`. La API de Viajes no necesita resolver HERE en esta etapa:
comparte con MVC el servicio de Lógica y valida el comprobante en la base común.

Estados de una fila:

1. Una selección HERE confirmada en servidor emite un token aleatorio. Se guarda
   solo su SHA-256 y el hash de la selección de origen (índices únicos).
2. `ConsumidaEnUtc = null`: comprobante utilizable hasta `VenceEnUtc` y solo en su
   contexto de usuario, formulario, tipo/recurso, revisión y campo.
3. En una transacción se vincula la misma fila al destino, se actualiza el texto
   canónico y se marca consumida. Después queda como snapshot duradero: vencer el
   comprobante no borra ni invalida la ubicación ya guardada.

Una reemisión de la misma selección se rechaza. Una confirmación nueva necesita su
propio contexto y snapshot. Los tokens de previsualización (`recorrido:...`) no sirven
como selecciones persistentes (`persistencia:...`).

## Modelo y migración prevista

Entidad nueva `RuteaYa.Entidades/UbicacionConfirmada.cs`:

- ID GUID, usuario confirmador, formulario, tipo de recurso, ID del recurso si ya
  existe, campo y clave estable de campo, revisión del recurso.
- Dirección normalizada, ID HERE, `position` y navegación separados, índice elegido
  (`-1` para position, `>=0` para access), tipo de resultado/puerta, estimación y corrección.
- Proveedor `HERE-v7`, fechas UTC de confirmación, vencimiento y consumo.
- Hash del comprobante, hash de selección y GUID de concurrencia optimista.

Las coordenadas nuevas usan `decimal(18,8)` con checks de rango. No se almacenan
claves HERE ni tokens en claro. Una selección pendiente no se considera un vínculo
del recurso: solo la FK del padre identifica su ubicación persistida.

Relaciones opcionales, con borrado restringido:

- `Viaje.OrigenUbicacionId`, `Viaje.DestinoUbicacionId`.
- `Parada.UbicacionConfirmadaId`.
- `Envio.RetiroUbicacionId`, `Envio.EntregaUbicacionId`.

Los campos anteriores no cambian ni se eliminan. En Envíos, las coordenadas antiguas
siguen representando position cuando se guarda mediante el nuevo servicio; la
navegación queda explícita en el snapshot. No se amplían todavía los límites actuales
de los textos (150 para Viajes/Paradas y 200 para Envíos): el vínculo rechaza etiquetas
que excedan esos contratos, sin truncarlas.

Migración generada:

`20261115070141_UbicacionesConfirmadasConComprobantes`

Su `Up` solo crea una tabla, añade cinco columnas nullable, índices y FK. No tiene
InsertData, UpdateData, DeleteData ni cambios de tipos en las columnas existentes.
El snapshot y el Designer fueron generados por EF. **No fue aplicada a ninguna base.**
La generación se hizo con servicios de diseño EF en una herramienta temporal fuera
del repositorio y un interceptor que prohíbe abrir conexiones, sin iniciar Program.cs
ni cargar credenciales. `dotnet-ef` no está instalado globalmente en este entorno.

## Servicios y frontera de confianza

- `IUbicacionesConfirmadasServicio`, `UbicacionesConfirmadasServicio` en Lógica:
  crea contexto, emite y guarda/consume. API y MVC registran el mismo servicio.
- `IFuenteSeleccionUbicacionVerificada`: contrato de fuente exclusivamente servidor.
  No debe implementarse mapeando coordenadas o un booleano recibidos de un POST.
- `FuenteSeleccionUbicacionVerificada` en MVC: recupera la confirmación de
  `SeleccionDireccionServicio` por usuario y ámbito exactos. La selección debe tener
  fecha de confirmación y vencimiento del servidor. No vuelve a geocodificar.
- En la API no se registra fuente HERE: puede consumir, pero no emitir a partir de
  datos arbitrarios. Una emisión sin fuente configurada se rechaza.

`ContextoUbicacionDto` se construye con `CrearContextoAsync` después de identificar al
usuario del servidor. Su hash de ámbito incluye formulario, recurso, revisión y campo.
Para Viajes admite origen, destino y paradas; para Envíos, retiro y entrega. Las claves
de parada son GUID estables, no índices visuales. En edición se verifica propietario
y se compara una revisión de los datos actuales. Modificar esa revisión rompe también
el ámbito del token de selección: no autoriza a reciclar una selección anterior.

El formulario debe reutilizar su `FormularioId` para todos sus campos y conservar
identidad estable por parada. Una alta todavía no tiene ID de recurso: el comprobante
está ligado al formulario y a una entidad nueva del usuario; la FK final identifica
el recurso creado. Los contextos de diferentes formularios/recursos no se mezclan.

## Guardado y consumo transaccional

`GuardarAsync(usuarioId, asignaciones)` recibe comandos internos que apuntan a entidades
ya preparadas/agregadas en el mismo DbContext. **No es un DTO para binding HTTP**.
Las paradas deben tener su viaje padre preparado. El futuro consumidor conserva las
reglas de negocio de activo, fechas, capacidad, etc.; este servicio no las reemplaza.

El servicio:

1. Rechaza tokens duplicados, destinos/campos repetidos, recursos mezclados y snapshots
   modificados antes de comenzar.
2. Abre una transacción Serializable. Vuelve a validar usuario, recurso/revisión,
   formulario, rol de campo, vencimiento y ausencia de consumo.
3. Verifica que las entidades de destino correspondan al recurso y usuario; en edición
   de parada comprueba también su pertenencia real al viaje.
4. Asigna FK y texto normalizado desde el snapshot, sin usar coordenadas del cliente.
5. Cambia fecha de consumo y GUID de versión; guarda recurso y comprobante con el
   mismo SaveChanges y commit. No permite escrituras ajenas a esa unidad de trabajo.
6. Ante un error hace rollback y restaura valores/estado previos en el tracker. Para
   reintentar tras conflictos/errores SQL, usar una petición y contexto EF nuevos.

El GUID es un token de concurrencia de EF: si dos contextos leyeron una versión
disponible, solo uno puede consumirla. Un segundo guardado con versión vieja falla
y revierte también el recurso que intentaba crear. No se reemplaza por "éxito".

El DbContext impide modificar la ubicación del snapshot y consumirlo aisladamente:
el cambio de consumo necesita versión nueva, transacción activa y guardado del vínculo
correspondiente. Solo cambian los metadatos de consumo, no la ubicación verificada.

## Compatibilidad y uso de la aplicación

Después de aplicar la migración autorizada, las filas antiguas conservan texto y
coordenadas y sus nuevas FK quedan null. Eso significa pendiente, no confirmado,
incluso si sus coordenadas son distintas de cero. No se generaron coordenadas,
backfills ni confirmaciones automáticas de registros históricos.

**Compatibilidad de datos no equivale a compatibilidad con un esquema sin migrar.**
EF ahora puede seleccionar las nuevas columnas al cargar Viajes/Paradas/Envíos.
No iniciar esta versión contra una base sin esas columnas: primero debe revisarse y
autorizarse la aplicación de la migración en la etapa correspondiente. Esta tarea
no consultó ni modificó SQL Server y no habilitó los nuevos guardados desde formularios.

## Pruebas y coordinación pendiente

Las pruebas relacionales usan SQLite en memoria con `EnsureCreated` (no Migrate ni
migraciones). Ejecutan restricciones, transacciones, rollback y conflicto optimista
entre dos contextos. No requieren SQL Server ni HERE real. Los ajustes SQLite para
decimal y RowVersion están solo en el contexto de prueba; no alteran SQL Server.
También se compara el modelo SQL Server con el snapshot y se genera su script SQL
en memoria, sin ejecutarlo ni abrir conexión.

Validación de esta etapa: **686 tests aprobados, 0 fallidos y 0 omitidos**;
compilación completa con **0 errores y 0 advertencias**. Incluye 18 pruebas nuevas
de esta base persistente, además de las 668 regresiones anteriores.

Antes de incorporar al proyecto compartido, coordinar:

- Nueva entidad, cinco FK opcionales, configuración EF, checks, índices y migración.
- Contratos nuevos de Lógica (`ContextoUbicacionDto`, comprobante, selección verificada
  y asignación interna), sin cambiar aún CrearViajeDto/CrearEnvioDto ni sus endpoints.
- La unidad de trabajo compartida y el tratamiento de conflictos para cuando los
  CRUD comiencen a llamar a GuardarAsync. No llamar al guardado anterior después de
  consumir de forma separada.
- Sustitución futura de ParadasTexto y paso de tokens por MVC/API, sin aceptar FK,
  direcciones o coordenadas del navegador como confirmación.
- Política para cambios de dirección en clientes antiguos: no deben conservar una
  FK confirmada mientras cambian el texto. La nueva integración visual sigue pendiente.
- Revisar límites de longitud antes de ampliar contratos, y la futura limpieza de
  comprobantes vencidos no vinculados; no se implementó limpieza ni borrado de datos.

No se tocaron formularios, controladores CRUD ni sus métodos de negocio. No se
habilitó aceptación, no se ejecutaron migraciones y no se hicieron solicitudes HERE
reales en esta etapa. Todos los cambios permanecen sin commit, push ni merge.
