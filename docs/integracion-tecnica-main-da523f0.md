# Prueba de integración técnica de main (sin commit)

## Referencias y aislamiento

- Rama original intacta: `feature/rutas-oportunidades`, `6cb280fc285bf90a965cdc9d2e9ed9f2ac60893f`.
- Origen integrado: `origin/main`, `da523f0acb38dd9c5b157ea1c75f4d1572b4cd39`.
- Rama temporal: `prueba/integracion-rutas-main-da523f0`.
- Worktree: `C:\Users\FRANCO\AppData\Local\Temp\opencode\RuteaYa-integracion-main-da523f0`.
- Merge iniciado con `git merge --no-commit --no-ff origin/main`. Se deja pendiente,
  con conflictos resueltos y correcciones preparadas; no se crea commit ni se hace push.
- Baseline adicional, detached y sin cambios, para reproducir fallos preexistentes:
  `C:\Users\FRANCO\AppData\Local\Temp\opencode\RuteaYa-baseline-main-da523f0`.

## Resoluciones y correcciones mínimas

### Conflictos de Git

- `RuteaYa/Program.cs`: se mantienen todos los registros propios de HERE, mapas,
  caché, desvíos, compatibilidad y evaluación; se incorpora `EnviosLogica` de main.
  Se conservan las mejoras de autenticación/configuración de los compañeros.
- `RuteaYa/Views/Panel/DetalleOportunidad.cshtml`: se mantienen evaluación,
  motivos, métricas y acceso al mapa. No se publica el formulario de aceptación.

### Seguridad y coherencia funcional

- `EnviosLogica.GeocodificarAsync`: eliminado el cálculo de coordenadas por hash y
  el catch que ignoraba errores. Si HERE falla, no hay candidatos o la posición no
  es válida, no se agrega ni se guarda el envío. Se mantiene la política existente
  de seleccionar el primer candidato real: **no equivale a confirmación explícita**.
  Cambiar el flujo de confirmación de Envíos requiere coordinación, fuera de esta prueba.
- `OportunidadesController.Aceptar`: el POST autenticado conserva antiforgery y
  valida parámetros, pero responde **409** sin invocar `EnviosLogica.AceptarAsync`.
  Se eliminó su dependencia `[FromServices]` para que tampoco se resuelva antes del bloqueo.
  No basta con ocultar un botón: el endpoint está bloqueado en servidor.
- `Oportunidades.cshtml`: texto alineado con aceptación deshabilitada; se conserva
  el enlace a la previsualización y la exclusión de publicaciones propias de main.
- `OportunidadesLogica`: el detalle excluye publicaciones del conductor, igual que
  el listado. La asignación existente también rechaza envíos propios, pero el POST
  permanece bloqueado para cualquier envío, propio o ajeno.
- `EnviosController`: un claim ausente/malformado/no positivo devuelve ID inválido,
  nunca 1; se admite `sub` como alternativa a `NameIdentifier`. Crear y cancelar
  dejan de mostrar excepciones internas/SQL, manteniendo mensajes de validación de negocio.
- `RuteaYa.Api/appsettings.json`: retirado el literal `Jwt:Key`. Se conservan el
  resto de los ajustes de main; la clave debe provenir de configuración privada.
  **El literal permanece en el historial compartido.** Retirarlo aquí no revierte
  la exposición: coordinar su sustitución/rotación si fue utilizado. No se modificaron
  User Secrets, credenciales reales ni historial Git.

No se altera el comportamiento de entidades, DbContext, migraciones ni el diseño de Transportes.
Los cambios de esos archivos traídos por main se conservan, incluida la migración
`20261108145615_AgregarConductorAEnvio`, sin ejecutarla.

### Pruebas

- `EnviosControllerTest`: se conservan los escenarios del módulo y se reemplazan
  mocks inválidos de métodos no virtuales por lógica real sobre EF interceptado.
  No se agrega una interfaz ni se hacen virtuales métodos de producción para Moq.
- `EnviosLogicaTest`: orden/tipos de lectores compatibles con el SQL generado,
  `AnyAsync` responde bool, se incluye RowVersion y el guardado es simulado en memoria.
- `Helpers/EnviosContextoPrueba.cs`: fixture exclusivo de tests; suprime apertura SQL,
  solo permite lectores SELECT simulados y sobreescribe SaveChanges sin ejecutar SQL.
  El helper conservador existente `ConsultasEfSimuladas` no se modifica.
- `OportunidadesVistaTest`: expectativa de UI sin aceptación y prueba HTTP real en
  loopback: anónimo 401, sin antiforgery 400, con antiforgery válido 409; no se registra
  EnviosLogica ni EF/HERE en ese host.
- `OportunidadesLogicaTest`: listado y detalle verifican exclusión de envíos propios.
- Regresiones nuevas: claim inválido no opera como usuario 1, errores HERE/cero
  candidatos no generan coordenadas ni guardan datos, errores internos no se muestran.

## Resultados y clasificación

| Ejecución | Total | Aprobados | Fallidos | Omitidos |
| --- | ---: | ---: | ---: | ---: |
| main sin cambios (`da523f0`) | 510 | 487 | 23 | 0 |
| Integración inicial, conflictos resueltos | 601 | 577 | 24 | 0 |
| Integración con correcciones y regresiones | 610 | 610 | 0 | 0 |

Los 23 fallos preexistentes se reproducen en main sin nuestras correcciones:

- 15 casos de `EnviosControllerTest`: constructor de mock incompatible y métodos
  no virtuales que Moq no puede configurar.
- 8 casos de `EnviosLogicaTest`: lectores con orden/tipos incorrectos, `AnyAsync`
  representado como una entidad y escenarios de guardado usando un helper que lo prohíbe.
  Los errores de materialización ocultaban este último problema de los fixtures.

El fallo adicional de integración fue el test de la vista que exigía el botón de
aceptación. Se ajustó a la política de bloqueo y se agregó una comprobación HTTP
del POST directo, manteniendo todas las pruebas de evaluación y mapas.

Se restauraron dependencias y compiló toda la solución sin errores ni advertencias.
La revisión inicial contra la rama original detectó cinco espacios finales ya
presentes en main (`Envio.cs` y `DetalleEnvio.cshtml`). Se eliminaron únicamente
esos espacios antes de guardar el merge para dejar `git diff --cached --check` limpio.
Los artefactos y TRX están fuera del repositorio:

- `C:\Users\FRANCO\AppData\Local\Temp\opencode\RuteaYa-validacion-integracion-da523f0`
  (`integracion-inicial.trx`, `integracion-corregida.trx`).
- `C:\Users\FRANCO\AppData\Local\Temp\opencode\baseline-main.trx`.

No se ejecutó Program.cs de la aplicación, no se consultó/modificó una base real,
no se aplicaron migraciones y no se hicieron solicitudes HERE reales. Las respuestas
HERE se simulan; solo los hosts de prueba MVC usan HTTP real en loopback.

## Pendientes de coordinación

1. Rotación de la clave JWT versionada si se utilizó y suministro mediante secretos.
2. Flujo de confirmación/procedencia de coordenadas de Envíos: el primer candidato
   no es una ubicación confirmada. Las coordenadas artificiales ya existentes, si
   las hay en alguna base, necesitan identificación y revisión; no se tocaron datos.
3. Compatibilidad, capacidad disponible, asociación persistente al viaje/salida
   concreta y concurrencia antes de habilitar aceptación. El método legado de
   asignación se conserva, pero no tiene un endpoint de aceptación habilitado.
4. Aplicación futura de la migración de main y prueba SQL, previa autorización.
5. Prueba visual con HERE real y servicios Routing/Geocoding/Raster habilitados.

Esta prueba deja una integración técnica validada para revisión e incorporación
posterior, **con aceptación bloqueada**. No habilita esa funcionalidad ni representa
validación contra una base real o una cuenta HERE real.
