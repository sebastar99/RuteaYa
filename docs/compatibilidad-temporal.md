# Compatibilidad temporal conservadora

El motor evalúa una fotografía en memoria de una salida concreta y un envío.
Reutiliza los campos existentes de `ViajeCompatibilidadDto` y
`EnvioCompatibilidadDto`; no consulta EF Core, geocodificación ni la hora actual.

## Fechas de entrada

Todas las fechas son `DateTimeOffset`. Las comparaciones usan el instante real,
no la hora de pared: offsets diferentes pueden representar el mismo instante.
No se asume una zona horaria fija.

- `SalidaProgramada` conserva el tipo `DateTimeOffset?`: `null` es salida desconocida.
  Sin una salida concreta no se aprueba el horario. Una salida no tiene estado
  "sin restricción", porque es un instante requerido y no un límite opcional.
- `LlegadaMaxima`, `DisponibleDesde` y `FechaLimiteEntrega` ahora son
  `RestriccionTemporalDto`. Ya no son fechas nullable que mezclan dos significados.

| Estado | Valor | Significado |
| --- | --- | --- |
| `Desconocida` | Sin fecha | El dato no se cargó, no está disponible o no pudo interpretarse |
| `SinRestriccion` | Sin fecha | Sabemos explícitamente que ese límite no aplica |
| `Informada` | `DateTimeOffset` concreto | Hay una restricción que debe verificarse |

El estado inicial es **Desconocida**, incluido el valor cero del enum. Un mapeo que
omita una propiedad no elimina la restricción accidentalmente. La falta de un dato
no equivale a que el usuario haya elegido "sin límite".

`RestriccionTemporalDto` es inmutable: estado y valor se construyen juntos mediante
`Desconocida()`, `SinRestriccion()` e `Informada(fecha)`. Solo Informada tiene fecha;
no hay setters públicos que permitan combinar estados y valores contradictorios.
No hay conversiones implícitas ni campos escalares duplicados para las mismas fechas.
Asignar `null` a una restricción es un contrato inválido y produce error de argumento,
nunca se interpreta como SinRestriccion.

`Informada(default(DateTimeOffset))` se rechaza: una fecha obligatoria desconocida
no se rellena con el 01/01/0001. También se rechaza esa fecha como salida concreta;
se debe usar salida `null` o restricción Desconocida, según corresponda.

Una restricción explícitamente SinRestriccion no impide declarar compatibilidad si se
conoce la salida y todas las demás condiciones aplicables están verificadas.
Una restricción Desconocida mantiene el resultado Indeterminado, salvo un rechazo
definitivo comprobado. Los códigos distinguen `LlegadaMaximaDesconocida`,
`DisponibilidadRetiroDesconocida` y `VencimientoEntregaDesconocido`.
Las ventanas internas invertidas siguen siendo errores de argumento; se permite
la igualdad. Solo se comparan sus límites si están Informados. La coherencia
estructural no implica que el recorrido quepa en esas ventanas.

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
| Restricción Desconocida | Indeterminada, salvo rechazo definitivo de otra regla |
| Restricción SinRestriccion | No agrega un límite ni una fecha |
| Fin posterior a `LlegadaMaxima` | Incompatible |
| Fin anterior o igual al vencimiento | Restricción de entrega satisfecha conservadoramente |
| Fin posterior al vencimiento, pero salida no posterior a él | Indeterminada: la entrega puede ocurrir antes del destino final |
| Disponible desde antes o al salir | Restricción de retiro satisfecha |
| Disponible desde después de finalizar | Incompatible |
| Disponible desde después de salir y antes o exactamente al finalizar | Indeterminada: falta el instante de retiro |
| Salida concreta y todas las restricciones aplicables satisfechas | Compatible |

Las reglas que comparan fechas se aplican a restricciones Informadas. Los datos
desconocidos no impiden comprobar otros rechazos: por ejemplo, llegada máxima
excedida, falta de cupo, estado no disponible o todas las alternativas fuera del
desvío permitido conservan prioridad sobre la indeterminación.

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
- El consumidor debe conservar el estado de conocimiento de cada restricción;
  omitirlo deja el valor seguro Desconocida. Solo la salida usa `null` para desconocido.
- Las capacidades y estados son una fotografía y deberán revalidarse al aceptar.
- HERE Routing actualmente usa `departureTime=any`: la duración no está vinculada
  a la fecha real de salida ni al tráfico en tiempo real. Compatible significa
  que las reglas se cumplen bajo esa estimación y los datos recibidos, no una garantía de llegada.
- No se incorporan espera, carga, descarga ni tiempos exactos por parada. Para
  resolver las restricciones indeterminadas harán falta tiempos por tramos y reglas adicionales.

## Mapeo del futuro servicio de oportunidades

El motor no debe interpretar las entidades ni elegir la zona horaria por su cuenta.
El futuro mapeador deberá seguir estas reglas, sin implementarlas todavía aquí:

1. **Dato no cargado, no capturado o conversión fallida:** Desconocida. Incluye una
   fecha obligatoria de Envíos ausente/por defecto o un `DateTime` cuya zona no se conoce.
2. **Ausencia de límite confirmada por la regla del negocio o por una elección explícita:**
   SinRestriccion. No inferirla de un campo omitido, un error de lectura o un `null` ambiguo.
3. **Fecha y hora conocidas, con su offset resuelto correctamente:** Informada(fecha).
   Conservar el instante; no usar automáticamente la zona horaria del servidor.

Ejemplos de construcción explícita:

```csharp
viaje.LlegadaMaxima = RestriccionTemporalDto.Desconocida(); // Aún no se cargó el límite.
envio.DisponibleDesde = RestriccionTemporalDto.SinRestriccion(); // Confirmado por el negocio.
envio.FechaLimiteEntrega = RestriccionTemporalDto.Informada(vencimientoConOffset);
```

`vencimientoConOffset` debe venir de un dato real interpretado con la política
acordada de fecha/hora/zona. Si esa interpretación no está disponible se usa
Desconocida, sin inventar un instante. Que `Envio.FechaLimite` sea obligatorio
impide mapear su falta como SinRestriccion. Para `DisponibleDesde`, que hoy no
existe en la entidad, el equipo debe acordar si realmente no hay límite o si falta
capturarlo; hasta entonces debe ser Desconocida. Lo mismo aplica si `HoraLlegada`
todavía no tiene una semántica acordada de límite máximo.

Las pruebas del mapeador deberán verificar que cargar parcialmente una entidad o
perder una restricción produce Indeterminado, y que solo una ausencia de límite
explícitamente conocida permite usar SinRestriccion. Las reglas previas y los
rechazos definitivos siguen teniendo prioridad.

## Tests

Las pruebas son unitarias con servicios simulados; no realizan solicitudes reales:

```powershell
dotnet test "RuteaYa.Test/RuteaYa.Test.csproj" --no-restore --filter "FullyQualifiedName~CompatibilidadTemporalTest|FullyQualifiedName~RestriccionTemporalDtoTest"
dotnet test "RuteaYa.slnx" --no-restore
dotnet build "RuteaYa.slnx" --no-restore
```
