# Conductor y transporte de envíos

Este documento explica qué se agregó para que un envío pueda ser tomado por un
conductor y avanzar hasta quedar entregado, y qué se cambió al crear envíos.

## Idea central

No existe un "rol conductor". Cualquier usuario puede publicar envíos y también
transportar. **Es conductor de un envío el usuario que lo aceptó**, y para
aceptarlo debe usar uno de **sus propios viajes activos**.

## Flujo completo

1. **Usuario A** publica un envío (`Mis envíos > Nuevo envío`). Queda en
   `Buscando conductor` y se geocodifican origen y destino (ver más abajo).
2. **Usuario B** crea un viaje activo (`Mis viajes > Nuevo viaje`).
3. B entra a `Oportunidades`, elige su viaje y abre el detalle de un envío.
4. B pulsa **Tomar este envío**. El envío pasa a `Conductor asignado` y guarda
   `ConductorId = B`.
5. B va a `Mis transportes` y avanza el estado con un botón por paso:
   `Conductor asignado -> Retirado -> En camino -> Entregado`.
6. A ve el estado actualizado en `Mis envíos`, el inicio y el historial.

## Cambios por capa

### Base de datos (`RuteaYa.Entidades`)
- `Envio.ConductorId` (`int?`) y navegación `Envio.Conductor`.
- `RuteaYaDbContext`: relación con `Usuario` (`DeleteBehavior.Restrict`) e índice.
- Migración `AgregarConductorAEnvio` (agrega columna, índice y FK).
  **Hay que aplicarla**:
  ```powershell
  dotnet ef database update --project RuteaYa.Entidades --startup-project RuteaYa.Api
  ```
  Los envíos existentes quedan con `ConductorId = NULL`.

### Lógica (`RuteaYa.Logica`)
`EnviosLogica`:
- `AceptarAsync(conductorId, viajeId, envioId)`: valida que el viaje sea del
  conductor y esté activo, que el envío siga en `Buscando conductor` y que no sea
  del propio conductor. Si dos conductores aceptan a la vez, la columna
  `RowVersion` provoca un `DbUpdateConcurrencyException` y el segundo recibe
  "El envío fue tomado por otro conductor".
- `ActualizarEstadoConductorAsync(conductorId, envioId, nuevoEstado)`: busca el
  envío por `Id` **y** `ConductorId`, solo permite `Retirado`, `EnCamino` o
  `Entregado`, y respeta las transiciones válidas.
- `ListarTransportesAsync(conductorId)`: envíos que el usuario transporta.
- La tabla de transiciones permitidas se extrajo a `TransicionValida` y la usan
  tanto el dueño como el conductor.
- `EnvioDto.ConductorId` nuevo.
- `OportunidadesLogica`: el listado de candidatos ya no incluye los envíos del
  propio conductor (antes se mostraban y no se podían tomar).

### Web MVC (`RuteaYa`)
- `TransportesController` (nuevo, `[Authorize]`): `GET /Transportes` y
  `POST /Transportes/Avanzar/{id}`. El usuario sale siempre de los claims.
- `OportunidadesController`: `POST /Oportunidades/Aceptar` (con antiforgery).
  Recibe `EnviosLogica` por `[FromServices]` para no cambiar el constructor.
- `PanelController.Transportes()` ahora redirige al nuevo controlador.
- Vistas: `Transportes.cshtml` pasó de datos de demostración a datos reales con
  un botón por estado; `DetalleOportunidad.cshtml` agrega el botón
  **Tomar este envío** (solo si el viaje está activo).
- Test de `OportunidadesVistaTest` actualizado: antes verificaba que NO hubiera
  botón de aceptar; ahora verifica que exista.

## Geocodificación al crear un envío
`EnviosLogica.CrearAsync` ahora llama a `IGeocodificacionServicio` (HERE) para
completar `OrigenLat/Lng` y `DestinoLat/Lng`, que antes quedaban en 0.
- Se toma la posición del primer candidato devuelto.
- Si HERE no encuentra la dirección, el envío se rechaza con un mensaje.
- Requiere `HereRouting:ApiKey` configurada:
  ```powershell
  dotnet user-secrets set "HereRouting:ApiKey" "<TU_API_KEY>" --project "RuteaYa/RuteaYa.Web.csproj"
  ```
- Los envíos creados antes de este cambio siguen con coordenadas en 0.

## Cómo probarlo
1. Aplicar la migración (arriba) y configurar la API key de HERE.
2. Registrar dos usuarios (A y B), en dos navegadores o ventanas privadas.
3. Con A: crear un envío.
4. Con B: crear un viaje, ir a Oportunidades, elegir el viaje y tomar el envío.
5. Con B: en Mis transportes pulsar Marcar retirado, Salir en camino y Marcar
   entregado. Con A: comprobar el estado en Mis envíos.

## Limitaciones conocidas
- **No se valida compatibilidad** (ruta, desvío, capacidad, horario) al aceptar.
  El motor de compatibilidad existe pero todavía no se invoca; el conductor
  decide bajo su responsabilidad.
- `ActualizarEstadoAsync` (versión del dueño) sigue permitiendo que el dueño
  marque cualquier transición válida. Conviene limitarla a `Cancelado`.
- El dueño puede cancelar un envío ya asignado; el conductor lo vería como
  `Cancelado` sin aviso.
- No hay forma de que el conductor "suelte" un envío aceptado.
- No hay tests automáticos de `AceptarAsync`/`ActualizarEstadoConductorAsync`: el
  proyecto de pruebas no tiene un proveedor EF en memoria.
