# Compatibilidad temporal conservadora

El motor evalúa una fotografía en memoria de una salida concreta y un envío.
Reutiliza los campos existentes de `ViajeCompatibilidadDto` y
`EnvioCompatibilidadDto`; no consulta EF Core, geocodificación ni la hora actual.

## Fechas de entrada

Todas las fechas son `DateTimeOffset`. Las comparaciones usan el instante real,
no la hora de pared: offsets diferentes pueden representar el mismo instante.
No se asume una zona horaria fija.

- `SalidaProgramada = null`: salida desconocida. Sin ella no se aprueba el horario.
- `LlegadaMaxima = null`: no se informó un límite de finalización del viaje.
- `DisponibleDesde = null`: no se informó una restricción de inicio del retiro.
- `FechaLimiteEntrega = null`: no se informó un vencimiento. No se inventa uno.

La ausencia de una restricción opcional no impide declarar compatibilidad si se
conoce la salida y todas las demás condiciones aplicables están verificadas.
Las ventanas internas invertidas siguen siendo errores de argumento; se permite
la igualdad. La coherencia estructural no implica que el recorrido quepa en ellas.

## Reglas

Los estados, el peso y los cupos conservan sus reglas previas. Una salida posterior
al vencimiento del envío es un rechazo definitivo y también evita calcular rutas,
incluso si falta información de capacidad.

Para cada inserción que cumple el desvío máximo se calcula:

```text
fin estimado = salida programada + duración total del recorrido con envío
```

Se usa `DuracionConEnvioSegundos`, no el tiempo adicional ni la duración original.
La suma conserva el offset de salida y se realiza con ticks sin redondear a minutos.

| Condición | Evaluación |
| --- | --- |
| Salida ausente | Indeterminada; no se inventa finalización |
| Fin posterior a `LlegadaMaxima` | Incompatible |
| Fin anterior o igual al vencimiento | Restricción de entrega satisfecha conservadoramente |
| Fin posterior al vencimiento, pero salida no posterior a él | Indeterminada: la entrega puede ocurrir antes del destino final |
| Disponible desde antes o al salir | Restricción de retiro satisfecha |
| Disponible desde después de finalizar | Incompatible |
| Disponible desde después de salir y antes o exactamente al finalizar | Indeterminada: falta el instante de retiro |
| Salida concreta y todas las restricciones aplicables satisfechas | Compatible |

Si una inserción incumple una condición definitiva, es incompatible aunque otras
restricciones sean indeterminadas. No se estiman instantes exactos de retiro ni
de entrega a partir de la duración total.

## Selección y resultado

Se evalúan todas las inserciones válidas, con una sola preparación de ruta base:

1. Elegir la mejor inserción completamente compatible, si existe.
2. Si no existe, elegir la mejor indeterminada y conservarla como provisional.
3. Si todas son incompatibles, devolver rechazo sin inserción seleccionada.

Dentro de cada categoría se mantienen distancia adicional, tiempo adicional,
índice de retiro e índice de entrega como criterios, en ese orden. Una inserción
más corta no desplaza a otra compatible si su horario es incompatible o incierto.

`InsercionSeleccionada` incluye `EstadoTemporal`, `MotivosTemporales` y
`FinalizacionEstimada`. Esta última es el fin del recorrido completo, no la entrega.
Los motivos globales describen la seleccionada o, si todas fallaron temporalmente,
la unión sin duplicados de los motivos de rechazo de las inserciones geográficamente
válidas. Esos rechazos pueden corresponder a alternativas distintas.

`DesvioAprobado` significa que hubo al menos una inserción geográficamente válida;
puede coexistir con estado global incompatible por horario. `HorarioNoEvaluado`
se usa al no evaluar horarios por inserción, por ejemplo por rechazo previo, falta
de capacidad, falta de salida o ausencia de alternativas geográficas válidas.

## Errores y cancelación

Los errores del proveedor y las cancelaciones se propagan aunque ya se haya
encontrado una inserción compatible: no se presenta como definitiva una búsqueda
incompleta. Una duración total negativa o una finalización que desborda el rango
de fechas produce `InvalidOperationException`, no un rechazo de negocio.

## Límites e integración pendiente

- Viajes debe resolver recurrencias y aportar una salida fechada concreta,
  con el offset correcto para esa fecha y sus eventuales cambios estacionales.
  Un `TimeSpan` o día semanal por sí solo no identifica un instante.
- Envíos debe definir el instante real de disponibilidad y vencimiento. Una fecha
  sin hora necesita una política explícita; no se convierte automáticamente a medianoche.
- Los valores nulos de restricciones deben representar datos realmente no informados,
  no información perdida por el mapeo del consumidor.
- Las capacidades y estados son una fotografía y deberán revalidarse al aceptar.
- HERE Routing actualmente usa `departureTime=any`: la duración no está vinculada
  a la fecha real de salida ni al tráfico en tiempo real. Compatible significa
  que las reglas se cumplen bajo esa estimación y los datos recibidos, no una garantía de llegada.
- No se incorporan espera, carga, descarga ni tiempos exactos por parada. Para
  resolver las restricciones indeterminadas harán falta tiempos por tramos y reglas adicionales.

## Tests

Las pruebas son unitarias con servicios simulados; no realizan solicitudes reales:

```powershell
dotnet test "RuteaYa.Test/RuteaYa.Test.csproj" --no-restore --filter "FullyQualifiedName~CompatibilidadTemporalTest"
dotnet test "RuteaYa.slnx" --no-restore
dotnet build "RuteaYa.slnx" --no-restore
```
