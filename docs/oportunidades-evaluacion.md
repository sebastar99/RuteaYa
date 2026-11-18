# Integración del motor en el detalle de oportunidades

## Flujo y responsabilidades

```text
OportunidadesController.Detalle (sesión y claims)
  → OportunidadesLogica.ObtenerDetalleAsync
      → viaje propio y envío BuscandoConductor, con AsNoTracking
      → IEvaluacionOportunidadServicio
          → IDatosConfirmadosOportunidadServicio
          → comprobación de requisitos y mapeo
          → ICompatibilidadServicio (solo con datos completos)
              → IDesviosServicio / IEvaluacionDesvios
                  → IRutasServicio
```

El listado no evalúa candidatos. El detalle obtiene dos proyecciones y evalúa una
sola vez después de autorizar ambas lecturas. La vista y el controlador no vuelven
a ejecutar el motor. Un viaje ajeno/inexistente o envío no disponible impide llegar
al adaptador de evaluación. No se agregan escrituras, entidades, tablas ni CRUD.

`IDatosConfirmadosOportunidadServicio` es una frontera de servidor, no un modelo
que se acepte desde una URL o formulario. La fuente debe garantizar que sus IDs,
confirmaciones, capacidad y fechas corresponden a la misma fotografía vigente.
El recorrido confirmado debe contener origen, todas las paradas del conductor en
su orden y destino. La confirmación no se deriva de una dirección, del resultado
primero de geocodificación ni del hecho de que un decimal esté dentro de rango.

## Fuente actual y datos faltantes

`DatosConfirmadosOportunidadActualesServicio` aprovecha lo que realmente acreditan
los contratos actuales y deja el resto desconocido:

| Dato | Tratamiento |
| --- | --- |
| ID, actividad del viaje y desvío máximo | Se leen de la proyección de Viaje |
| ID, peso y estado del envío | Se leen de EnvioDto |
| Recorrido/retiro/entrega confirmados | Desconocidos: el modelo no registra confirmaciones |
| Coordenadas numéricas almacenadas en Envio | No se convierten automáticamente en confirmadas |
| Salida fechada y disponibilidad de esa salida | Desconocidas; no se deducen de TimeSpan, días o Activo |
| Peso y cupos restantes | Desconocidos; no se rellenan con máximos del viaje |
| Límite de llegada e inicio de retiro | Desconocidos; requieren semántica y datos explícitos |
| Vencimiento del envío | Solo se toma directamente si la fecha ya expresa UTC; Unspecified/Local quedan sin interpretar |

SQL `datetime2` normalmente entrega `DateTimeKind.Unspecified`; no se aplica la
zona del servidor. El vencimiento es obligatorio en la entidad actual, por lo que
esta integración no permite descartarlo con `SinRestriccion`. Una fuente futura
deberá interpretarlo con una política de fecha/hora/zona acordada con Envíos.
El motor general sigue admitiendo restricciones SinRestriccion para otros usos.

Con los datos actuales, el resultado normal es **Pendiente de evaluación**, con
códigos `DatoPendienteOportunidad` y mensajes claros de cada requisito faltante.
No existe una implementación de almacenamiento de confirmaciones ni se inventa
una en esta etapa. La fuente se podrá sustituir por una implementación respaldada
por los módulos del equipo cuando esos datos existan; no se modifica su formulario.

## Preparación y seguridad de la evaluación

- Se verifica correspondencia de IDs de la fuente y del resultado del motor.
- Se exigen coordenadas confirmadas válidas, incluida toda la secuencia base.
  `(0,0)` confirmado es válido; un dato no confirmado no se sustituye por `(0,0)`.
- Se requiere salida concreta, disponibilidad conocida y capacidad real conocida.
  Falso y cero confirmados son valores conocidos: el motor puede rechazarlos sin rutas.
- Se exigen restricciones temporales conocidas, ya sean Informadas o SinRestriccion
  cuando corresponda. Vencimiento del envío debe ser Informado y la publicación
  debe contener una fecha. Si ya era UTC no se acepta un instante distinto.
- Pesos, capacidad, desvío y ventanas temporales inválidas quedan señalados para
  revisión, sin fabricar correcciones ni consultar el proveedor.
- Se copian las coordenadas al construir los DTOs existentes de compatibilidad;
  estado, peso y desvío máximo provienen de la publicación, no de valores HTTP.
- Con faltantes no se llama al motor, HERE ni geocodificación. Si hubo evaluación
  completa se conserva el resultado real: compatible, incompatible o indeterminado.
- Errores técnicos y cancelación no se convierten en compatibilidad ni en un rechazo
  de negocio. El controlador maneja fallos conocidos con un mensaje seguro y HTTP 503.

`EvaluacionOportunidadDto` distingue preparación pendiente de `ResultadoMotor`.
Su estado es Indeterminado por defecto y no puede exponerse como Compatible si
contiene faltantes. `DetalleCandidatoDto.EvaluacionPendiente` también contempla
el resultado indeterminado del motor por sus límites temporales conservadores.

## Presentación y costo

La vista muestra los requisitos pendientes o los motivos del motor. Solo con
resultado utilizable e inserción seleccionada aparecen distancia adicional,
duración total, tiempo adicional y segmentos. Las diferencias negativas se
conservan. La finalización del recorrido no se presenta como hora de entrega.
No se habilita aceptación.

La fuente actual no agrega consultas EF ni HTTP. Cuando se complete, se realiza
una evaluación por GET y se conserva la preparación de base una vez por evaluación:
con cuatro puntos son una base y seis alternativas, siete cálculos. No se crea
caché global ni se reutilizan resultados de otro candidato o recarga; una recarga
debe leer datos vigentes. Antes de evaluar masivamente habrá que definir vigencia
y versiones con los módulos responsables.

## DI y pruebas

MVC agrega registros scoped para la fuente conservadora, el adaptador de evaluación,
`ICompatibilidadServicio` y `IDesviosServicio`. Conserva los clientes tipados HERE
y todos los registros anteriores. Resolverlos no hace una solicitud HTTP.

Los tests simulan la fuente de confirmaciones y el motor. Incluyen además una
integración completa en memoria con `CompatibilidadServicio`, `DesviosServicio`
y `RutasMockServicio`, con recorridos configurados expresamente. SQL se intercepta
sin conexión en las pruebas de consulta; las pruebas Razor usan solo loopback y
dependencias simuladas. No hay datos de prueba en SQL Server ni solicitudes HERE reales.

Si MVC/API están abiertos y bloquean sus DLL, se puede validar sin cerrarlos:

```powershell
dotnet test "RuteaYa.slnx" --artifacts-path "$env:TEMP\opencode\RuteaYa-validacion-evaluacion-detalle"
dotnet build "RuteaYa.slnx" --no-restore --artifacts-path "$env:TEMP\opencode\RuteaYa-validacion-evaluacion-detalle"
```

Para ver los cambios en las instancias originales será necesario detenerlas,
compilar en su salida normal y volver a iniciarlas. Esta etapa no las cierra.
