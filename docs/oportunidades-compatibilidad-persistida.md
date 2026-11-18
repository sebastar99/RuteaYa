# Compatibilidad con datos persistidos: alcance real

## Estado

- Envíos cerrado en `4da2d28`, `feat: integrar ubicaciones HERE en envios`.
- Esta conexión de compatibilidad queda sin commit ni push.
- No se añadieron entidades, tablas o migraciones; no se aplicaron migraciones,
  no se modificó SQL Server ni Transportes y la aceptación sigue bloqueada en servidor.

## Datos que existen y datos que no existen

`Viaje` representa una recurrencia: `DiaViaje.DiaSemana`, HoraSalida y HoraLlegada
(`TimeSpan`). No representa una salida fechada, ni conserva su offset/zona. CreadoEn
es fecha de creación, no salida. HoraLlegada es estimada, no prueba de límite máximo.

PesoMax y CantMaxPaquetes son máximos publicados, no capacidad restante. Envío solo
tiene ConductorId, sin vínculo a una salida/viaje concreto. No se pueden descontar
todos los envíos de un conductor de una recurrencia arbitraria, ni considerar un
viaje activo como una salida disponible. La asignación actual está bloqueada.

FechaLimite es DateTime y el formulario permite una fecha, sin zona ni política
persistida de fin de día. SQL datetime2 no conserva Kind. No se convierte a UTC ni
a medianoche/fin de día del servidor por conveniencia. Tampoco existe disponibilidad
de retiro persistida. Esos datos siguen siendo Desconocida/null.

## Fuente persistida y validación

`DatosConfirmadosOportunidadPersistidosServicio` reemplaza el registro conservador
en DI de MVC y usa cuatro SELECT por lote, sin tracking:

1. Viaje por ID y propietario.
2. Paradas por viaje y orden.
3. Envíos solicitados que siguen disponibles y son de otros usuarios.
4. Snapshots por los IDs de las FK, proyectando solo los datos de evaluación, sin hashes.

Se verifica que el snapshot esté consumido/guardado, sea de HERE, tenga coordenadas
válidas y corresponda al rol, texto, tipo/recurso y propietario del registro. Para
el paquete, el propietario del snapshot es el remitente, no el conductor. Se comparan
los campos de publicación recibidos con los actuales; esto no reemplaza una transacción
ni el control de concurrencia necesario al aceptar.

Se usa NavegacionLatitud/Longitud: nunca lat/lng anteriores de Envíos ni la referencia
position por sustitución. El recorrido incluye todos los puntos en orden. Si falta
una parada confirmada, no se calcula una ruta omitiéndola. FK null, comprobante sin
consumir, usuario/rol/texto incorrectos o coordenadas inválidas quedan pendientes.
La ubicación ya guardada sigue siendo válida aunque el token temporal haya vencido.

`DatosConfirmadosOportunidadActualesServicio` se conserva como fuente estricta de
contratos anteriores/pruebas; no es la fuente productiva registrada en MVC.

## Evaluación parcial sin falsificar datos

Se reutilizan `EvaluacionOportunidadServicio`, `ICompatibilidadServicio`, el motor,
`IDesviosServicio` e `IRutasServicio`. La fuente persistida habilita explícitamente
la evaluación parcial; las fuentes anteriores conservan la política estricta.

Rechazos verificables sin HERE:

- Viaje inactivo / disponibilidad expresamente falsa.
- Envío no disponible cuando la regla recibe ese estado (las pantallas lo excluyen).
- Peso superior incluso al máximo publicado.
- Peso/cupos restantes insuficientes cuando una fuente fiable los proporciona.

Los máximos publicados tienen campos separados de la capacidad restante: no se
copian en PesoDisponibleKg/CuposDisponibles. Los desconocidos siguen desconocidos.

Si las coordenadas están completas y no hay rechazo previo, se calculan las
inserciones y desvíos reales del proveedor. Desvío excesivo es rechazo geográfico
probado, incluso si faltan fechas/capacidad. Una inserción que cumple desvío queda
provisional si no hay salida concreta y no se inventa una finalización.

`MotorEjecutado` ahora informa si hubo resultado del motor, incluso parcial.
`DatosPendientes` se conserva junto a las métricas. Un resultado Compatible con
datos pendientes nunca se muestra como Compatible. Un rechazo probado sí prevalece.

Con el modelo persistido actual, en general los resultados dentro de los límites
son **Indeterminado** por falta de salida, disponibilidad, capacidad restante y
restricciones temporales. La decisión plenamente Compatible/temporal funciona con
el contrato completo existente, verificado en pruebas con datos explícitos de una
fuente de prueba; esa información no se inventa en la fuente productiva.

## Listado, detalle y rendimiento

- Se evalúa la página de candidatos seleccionada, no todos los paquetes del sistema.
- El evaluador carga datos por lote y evalúa una vez cada par. No llama a ObtenerAsync
  por candidato cuando usa la fuente persistida.
- Listado: selector + count + página + cuatro consultas de fuente = siete SELECT,
  independientemente de la cantidad de candidatos. Detalle: autorización/publicación
  + cuatro consultas de fuente. No hay lazy-loading ni tracking de entidades.
- `RutasConsultaServicio` reutiliza métricas de recorridos idénticos por coordenadas
  ordenadas solo durante la solicitud MVC. Comparte la base entre candidatos y también
  alternativas idénticas; no conserva fallos/cancelaciones como rutas válidas.
- Se conserva la preparación de desvíos: una ruta base preparada y todas las parejas
  válidas de inserción, sin recalcularla dentro de cada alternativa.
- No se agregan cachés globales ni un optimizador de múltiples paquetes. El número de
  alternativas mantiene la complejidad del algoritmo existente según las paradas;
  no se afirma que evaluar 50 candidatos con muchas paradas tenga costo constante.
- Abrir detalle es otra solicitud: revalida datos actuales y recalcula. No se reutiliza
  un resultado de negocio del listado que podría haber quedado obsoleto.
- HTTP/timeout del proveedor no se convierten en Incompatible ni en métricas ficticias;
  el controlador muestra un error público seguro. El aborto normal se maneja con 499.

Las dos pantallas muestran estado, motivos, datos pendientes y métricas cuando existen.
En detalle pueden coexistir métricas geográficas y datos temporales/capacidad pendientes.
El botón/POST de aceptación no se habilitó.

## Mínimo necesario antes de aceptación segura (no implementado)

1. Una **salida concreta de un viaje recurrente**: ID, FK Viaje, fecha/hora con offset
   conocido y disponibilidad/estado. Agregar solo una fecha al viaje recurrente no
   alcanza si ese viaje tiene múltiples salidas. Esta necesidad justificaría una
   entidad/tabla SalidaViaje en la próxima etapa; no se generó aquí.
2. Vínculo de asignación del envío a esa salida. Para el TP puede ser una FK nullable
   en Envío si cada paquete se asigna a una única salida; no hace falta un optimizador
   global ni una tabla extra de reservas si las reglas pueden derivarse de ese vínculo.
3. Capacidad total de esa salida y regla explícita para reservas/cupos/peso ocupados,
   de modo que se calcule capacidad restante real y concurrentemente revalidable.
4. Política de restricciones: vencimiento de entrega como instante con offset,
   disponibilidad de retiro y límite de llegada conocido o SinRestriccion explícita.
   El DateTime sin zona y los campos omitidos no autorizan a suponer ausencia de límites.
5. Al aceptar: revalidar propietario, estado, ubicaciones, capacidad, salida y motor;
   asignar y reservar atómicamente con control de concurrencia/idempotencia. La UI,
   una evaluación anterior o un token de previsualización no bastan para autorizarlo.

Estos cambios requieren acordar entidades/contratos y una migración aditiva antes
de implementarlos. No se añadieron ahora por el límite de alcance solicitado.

## Verificación

Compilación de la solución: 0 advertencias y 0 errores. Suite completa: 732 pruebas
aprobadas, 0 fallidas y 0 omitidas, con artefactos aislados fuera del repositorio.

Pruebas con SQLite en memoria, identidad y HERE simulados. Cubren viaje sin/con paradas,
posición de navegación frente a referencia y columnas legadas, indeterminación real,
desvío excesivo, peso máximo, viaje inactivo, datos insuficientes/corruptos, propiedad,
envíos propios/no disponibles y snapshots históricos. Escenarios contractuales explícitos
cubren Compatible, peso/cupo insuficiente, disponibilidad falsa y llegada excedida.
Se cuentan SELECT y llamadas de rutas, y se prueba presentación de métricas/motivos en Razor.
No se ejecuta ninguna migración ni se accede a SQL Server o HERE real.

Para prueba manual con datos reales hará falta la base de desarrollo autorizada y
migrada de las etapas anteriores, ambas aplicaciones en la misma versión/base y la
clave privada HERE en MVC. Crear/confirmar todas las ubicaciones y entrar a Oportunidades,
seleccionar el viaje y revisar la página y el detalle. Un viaje legado debe mostrar
coordenadas pendientes, no utilizar latitudes viejas como confirmaciones.
