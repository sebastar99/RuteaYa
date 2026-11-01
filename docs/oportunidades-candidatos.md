# Listado de candidatos pendientes de evaluación

## Acceso

Con una sesión MVC autenticada:

- `/Oportunidades`: selector de viajes propios.
- `/Oportunidades?viajeId=10`: candidatos de ese viaje, si pertenece al usuario.
- `/Oportunidades?viajeId=10&pagina=2&tamanoPagina=10`: página solicitada.
- `/Panel/Oportunidades`: se conserva el enlace existente; redirige al controlador
  propio y conserva los parámetros de viaje y paginación.

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
  errores seguros, lista vacía y continuidad de la ruta anterior.
- `OportunidadesVistaTest`: host local exclusivo de pruebas con autenticación y
  lógica simuladas; renderiza la vista Razor real. No carga el Program de producción,
  User Secrets ni servicios de datos/HERE. Las claves de Data Protection son efímeras.

```powershell
dotnet test "RuteaYa.slnx" --no-restore
dotnet build "RuteaYa.slnx" --no-restore
```
