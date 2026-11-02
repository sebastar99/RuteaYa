# Listado de candidatos pendientes de evaluación

## Acceso

Con una sesión MVC autenticada:

- `/Oportunidades`: selector de viajes propios.
- `/Oportunidades/Index`: alias explícito del mismo listado, independiente de la acción Login predeterminada global.
- `/Oportunidades?viajeId=10`: candidatos de ese viaje, si pertenece al usuario.
- `/Oportunidades?viajeId=10&pagina=2&tamanoPagina=10`: página solicitada.
- `/Panel/Oportunidades`: se conserva el enlace existente; redirige al controlador
  propio y conserva los parámetros de viaje y paginación.
- `/Oportunidades/Detalle?viajeId=10&envioId=25&pagina=2&tamanoPagina=10`: detalle
  del envío para el viaje propio, con contexto para volver al listado.
- `/Panel/DetalleOportunidad`: ya no devuelve la maqueta. Con ambos IDs redirige
  al detalle real; sin identificadores suficientes vuelve al listado/selector.

El controlador obtiene el conductor desde `NameIdentifier` o `sub` de la identidad
autenticada. No acepta un identificador de conductor desde la URL o el formulario.
Un viaje ajeno y uno inexistente responden ambos con 404, sin consultar candidatos.

## Consulta y alcance

`IOportunidadesLogica` / `OportunidadesLogica` consultan EF Core directamente desde
el módulo MVC, sin modificar el CRUD ni la arquitectura de otros módulos.

La consulta carga una proyección mínima de viajes propios para el selector y la
verificación de propiedad. No incluye días ni paradas. Para el viaje seleccionado:

1. Cuenta envíos con estado `BuscandoConductor`.
2. Obtiene una página ordenada por ID descendente, reutilizando `EnvioDto`.

Se ejecutan como máximo tres consultas de solo lectura: viajes, cantidad y página.
Sin selección se ejecuta solo la primera; sin candidatos no se ejecuta la tercera.
Se usa `AsNoTracking` y no se invoca `SaveChanges`, transacciones ni migraciones.

La página es positiva y su tamaño debe estar entre 1 y 50 (por defecto 10). Una
página mayor que la última se ajusta a la última disponible; si no hay resultados
se usa página 1. Primero se limita la página según el total para evitar desbordes
en el desplazamiento de consultas con números de página muy grandes.

No se usa `EnviosLogica.ListarAsync(conductorId)`, ya que lista publicaciones de un
usuario. No hay una regla existente que excluya envíos propios: esta versión
incluye publicaciones del conductor y de otros usuarios, filtradas solo por estado.
Si el equipo define otra política, deberá agregarse explícitamente y con tests.

Se permiten seleccionar viajes inactivos, identificados como tales: esta pantalla
no evalúa su disponibilidad ni la de una salida fechada. La disponibilidad temporal
del envío tampoco se infiere del estado; se muestra su fecha publicada sin convertir
su zona ni decidir si cumple el vencimiento del viaje.

## Presentación

`OportunidadesController` reutiliza `Views/Panel/Oportunidades.cshtml`, mediante
`OportunidadesViewModel`. Solo se modificó la acción de navegación de oportunidades
en `PanelController`; el CRUD y el layout se mantienen.

Todos los envíos se muestran como **Pendiente de evaluación**. El listado no tiene
distancias, duraciones, compatibilidades calculadas, botones de aceptación ni un
detalle ficticio. `ListadoCandidatosDto.EvaluacionPendiente` es siempre verdadero.
La ausencia de candidatos es un estado normal; errores de almacenamiento conocidos
devuelven una vista con un mensaje seguro y HTTP 503, sin conexiones ni detalles internos.

El total y la página son una lectura, no una reserva. Pueden cambiar si se publica
o asigna un envío entre consultas o después de visualizar la pantalla.

## Detalle del candidato

`ObtenerDetalleAsync(conductorId, viajeId, envioId, cancellationToken)` realiza
como máximo dos consultas `AsNoTracking`: primero verifica viaje propio y luego
busca el envío por ID y estado `BuscandoConductor`. No usa el listado por dueño.
Un viaje ajeno/inexistente no provoca ninguna consulta de envíos. Un envío
inexistente o que dejó de estar disponible responde con el mismo 404, sin revelar su estado.

`DetalleCandidatoDto` reutiliza `ViajeOportunidadesDto` y `EnvioDto`. Las proyecciones
EF del listado y detalle son compartidas. No se consultan relaciones de Usuario,
nombres, correos o teléfonos; el ID de publicador del DTO no se muestra en HTML.

La acción GET `OportunidadesController.Detalle` toma la identidad de la sesión y
reutiliza `Views/Panel/DetalleOportunidad.cshtml`, con `DetalleOportunidadViewModel`.
Los enlaces incluyen viaje y envío y conservan la página y tamaño de origen. Se
revalida disponibilidad al abrir el detalle: un candidato puede cambiar después
de haber sido mostrado en el listado. El regreso al listado vuelve a ejecutar su
paginación normal y puede ajustarla si cambió la cantidad de publicaciones.

Se muestran direcciones, descripción, peso, tamaño, fecha límite publicada y monto
del modelo. El estado continúa siendo **Pendiente de evaluación**, sin distancias,
desvíos, horarios de entrega inventados ni botón de aceptación. La fecha publicada
se muestra sin interpretar su zona horaria. Los errores de almacenamiento conocidos
devuelven HTTP 503 y un mensaje seguro, sin exponer detalles del proveedor.

## Datos pendientes para evaluar compatibilidad

No se llama a HERE, geocodificación, rutas, desvíos ni al motor de compatibilidad.
Tener una dirección escrita o coordenadas numéricamente válidas no acredita su
confirmación. Para evaluar de verdad se necesitan coordenadas confirmadas, una
salida concreta, capacidad restante real y restricciones temporales mapeadas con
sus estados explícitos. El listado no transforma datos faltantes en `SinRestriccion`
ni máximos del viaje en capacidad libre.

Para usar datos reales, la base ya debe contener las tablas del proyecto y el
usuario debe tener viajes propios. Esta etapa no crea tablas, aplica migraciones,
inserta datos de demostración ni modifica SQL Server.

## Tests

- `OportunidadesLogicaTest`: usa EF Core SQL Server con apertura de conexión
  suprimida y lectores simulados. Verifica SQL generado, propiedad, estado,
  paginación, cancelación y ausencia de tracking/guardados sin consultar SQL Server.
- `OportunidadesControllerTest`: identidad desde claims, manipulación del ID,
  errores seguros, lista vacía, detalle y continuidad de las rutas anteriores.
- `OportunidadesVistaTest`: host local exclusivo de pruebas con autenticación y
  lógica simuladas; renderiza la vista Razor real. No carga el Program de producción,
  User Secrets ni servicios de datos/HERE. Las claves de Data Protection son efímeras.
  Usa el patrón de enrutamiento convencional de producción, además de las rutas
  explícitas del controlador. Sigue los enlaces listado-detalle-regreso para
  verificar el contexto y la codificación de contenido publicado.

## Prueba manual con datos reales existentes

1. Usar la conexión local ya configurada para el MVC. Las tablas/columnas deben
   existir previamente; no ejecutar migraciones automáticamente ni crear datos de demostración.
2. Ejecutar API y MVC como habitualmente (la API sigue siendo necesaria para el login).
   Si ya están compilados, se pueden ejecutar en dos terminales:

   ```powershell
   dotnet run --project "RuteaYa.Api/RuteaYa.Api.csproj" --no-build --no-restore
   dotnet run --project "RuteaYa/RuteaYa.Web.csproj" --no-build --no-restore
   ```

3. Iniciar sesión con un usuario existente que tenga viajes propios y abrir
   `/Oportunidades` en el puerto del MVC.
4. Seleccionar un viaje; abrir **Ver detalle** en un envío real publicado.
   Si no hay envíos BuscandoConductor, la pantalla vacía es el resultado correcto.
5. Verificar datos, etiqueta pendiente y enlace **Volver al listado**, incluida la
   paginación. No hay aceptación ni cambios de estado en estas pantallas.
6. Cambiar manualmente el viaje por uno ajeno o probar un envío inexistente/no
   disponible debe producir 404 sin mostrar datos privados. No modificar registros
   para preparar esos casos: usar IDs que ya existan o los tests automatizados.

Las pruebas automatizadas usan datos simulados. Una conexión comprobada no
equivale a una prueba manual autenticada con publicaciones reales; esa verificación
se realiza siguiendo estos pasos, sin exponer credenciales en comandos ni informes.

```powershell
dotnet test "RuteaYa.slnx" --no-restore
dotnet build "RuteaYa.slnx" --no-restore
```
