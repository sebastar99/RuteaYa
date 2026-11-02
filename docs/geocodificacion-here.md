# Geocodificación con HERE Geocoding & Search API v7

## Arquitectura y contrato

- `RuteaYa.Logica/IGeocodificacionServicio.cs` expone
  `GeocodificarAsync(string direccion, CancellationToken cancellationToken = default)`.
- Devuelve `IReadOnlyList<ResultadoGeocodificacionDto>` en el orden de relevancia
  recibido de HERE. `items: []` corresponde a lista vacía; no es un error HTTP.
- `RuteaYa/Services/GeocodificacionHereServicio.cs` implementa el adaptador HTTP
  en MVC. No hay modelos JSON ni configuración HTTP en la lógica de negocio.
- `RuteaYa/Program.cs` registra el cliente tipado con timeout de 30 segundos,
  loggers deshabilitados y redirecciones automáticas deshabilitadas.

Se consulta `GET https://geocode.search.hereapi.com/v1/geocode` con `q` codificado
y `addressNamesMode=normalized`. No se fuerza `limit=1`, un país ni un tipo de
resultado: esas restricciones podrían ocultar candidatos o resultados aproximados.
Se usa el límite predeterminado de HERE (20); la lista no es una enumeración
exhaustiva de todas las ubicaciones posibles.

Se recortan espacios exteriores. Se rechazan entradas nulas, vacías, sin letras
ni números o con caracteres de control (incluidos saltos de línea). No se exige
calle y número: consultas parciales también pueden producir candidatos útiles.

## Posición, acceso, precisión y confirmación

El DTO reutiliza `PuntoRutaDto` para latitud/longitud WGS84:

- `Posicion` conserva `position`: referencia para mostrar la ubicación en un mapa.
- `Accesos` conserva todos los `access`, en orden, sin sustituir `Posicion`. Son
  posibles accesos de navegación; no se elige el primero automáticamente.
- `DireccionNormalizada` corresponde a `address.label` y `IdHere` a `id`.
- `TipoResultado` conserva `resultType`, como `houseNumber`, `street`, `locality`,
  etc. Se toleran nuevos tipos sin interpretarlos automáticamente como precisos.
- `TipoNumeroPuerta` conserva `houseNumberType` (`PA`, `interpolated`, etc.).
- `NumeroPuertaCorregido` conserva `houseNumberFallback`: la numeración solicitada
  puede haberse reemplazado por una numeración conocida.
- `PosicionEstimada` conserva `estimatedPointAddress`.
- `PuntajeConsulta` conserva `scoring.queryScore` cuando existe.
- `PuntajesCampos` conserva `scoring.fieldScore`. Un escalar se representa como
  lista de un elemento; listas como `streets` se conservan completas. No se
  fabrican campos ausentes ni se sustituyen puntajes ausentes por 1. Según HERE,
  un campo no incluido en `fieldScore` implica coincidencia 0 para ese campo;
  el DTO conserva su ausencia para distinguir qué datos envió el proveedor.

**Todos los candidatos requieren confirmación en esta primera versión**, incluso
un único `houseNumber/PA` con puntaje 1. Esto es una política conservadora del
contrato, no un error del servicio ni una indicación de que todos sean ambiguos.
`RequiereConfirmacion` es de solo lectura y no representa una confirmación ya
realizada. No se selecciona el primer resultado y no se persiste ninguno.

El puntaje indica coincidencia textual, no precisión en metros. Dos ubicaciones
distintas pueden tener puntaje 1. Un resultado de ciudad, calle o numeración
interpolada no equivale a una dirección exacta. Más adelante el consumidor deberá
presentar candidatos y accesos, permitir confirmar y elegir expresamente las
coordenadas que entregará a `IRutasServicio`. Esa selección todavía no existe.

## Validación y errores

- Se validan coordenadas de `position` y de **cada** `access`, incluso cuando hay
  varios: latitud en [-90, 90], longitud en [-180, 180], ambas obligatorias.
- `access` y `scoring` son opcionales. La falta de `position`, `id`, `resultType`
  o `address.label` impide devolver un candidato utilizable y produce error.
- Si un candidato es inválido falla toda la operación: no se descartan resultados
  silenciosamente, pues eso podría hacer parecer única una consulta ambigua.
- Se validan puntajes numéricos entre 0 y 1; datos malformados producen error.
- Error HTTP/red: `HttpRequestException`, con estado HTTP cuando existe.
- JSON inválido, ausencia de `items` o datos de candidato inválidos:
  `InvalidOperationException`. Se distingue de `items: []` válido.
- API Key ausente: `InvalidOperationException` antes de enviar HTTP.
- Timeout HTTP: `TimeoutException`. Cancelación del llamador:
  `OperationCanceledException` con el token recibido.

Los mensajes no incluyen la consulta, el cuerpo de HERE, la URL con credenciales
ni excepciones internas que pudieran exponerlos. No hay reintentos automáticos.

## Configuración segura

Se reutilizan `HereRoutingOpciones` y **la misma clave `HereRouting:ApiKey`** que
Routing ya utiliza. No hay una segunda credencial ni una sección nueva.
Si ya está configurada, no hace falta repetirla. En desarrollo, el proyecto MVC
carga sus User Secrets con el ambiente `Development`:

```powershell
dotnet user-secrets set "HereRouting:ApiKey" "<TU_API_KEY>" --project "RuteaYa/RuteaYa.Web.csproj"
```

También se admite la variable de entorno `HereRouting__ApiKey` desde el gestor
de secretos del entorno. No guardar su valor en `appsettings.json`, documentación,
capturas ni logs. El servicio se valida al invocarlo y no consulta HERE al arrancar.

## Cómo probar

Los tests utilizan exclusivamente `HttpMessageHandler` simulado y claves ficticias:

```powershell
dotnet test "RuteaYa.Test/RuteaYa.Test.csproj" --no-restore --filter "FullyQualifiedName~GeocodificacionHereServicioTest"
dotnet test "RuteaYa.Test/RuteaYa.Test.csproj" --no-restore
dotnet build "RuteaYa.slnx" --no-restore
```

**No se realizaron solicitudes reales de geocodificación en esta etapa.**
Después de aprobar la implementación, una prueba manual podrá resolver el
servicio registrado en MVC o en un ejecutable temporal que cargue los User Secrets
del ensamblado MVC y repita el registro HTTP seguro. Ejemplo de invocación:

```csharp
var geocodificacion = servicios.GetRequiredService<IGeocodificacionServicio>();
var candidatos = await geocodificacion.GeocodificarAsync(
    "Belgrano 100, Morón, Buenos Aires, Argentina", cancellationToken);
```

Se requiere una clave autorizada para Geocoding & Search v7, cuota disponible y
conectividad HTTPS a `geocode.search.hereapi.com`. Tener acceso a Routing no prueba
por sí solo que esa clave tenga autorización para el producto Geocoding.
Esta etapa no agrega pantallas, endpoints, persistencia ni confirmación de usuario.

## Fuentes oficiales consultadas

- [GET /geocode: parámetros y esquema](https://docs.here.com/geocoding-and-search/reference/get_geocode.md)
- [Geocodificar una dirección: ambigüedad, position y access](https://docs.here.com/geocoding-and-search/docs/code-geocode-address.md)
- [Scoring: interpretación y campos ausentes](https://docs.here.com/geocoding-and-search/docs/code-geocode-scoring.md)
- [Tipos y precisión de direcciones](https://docs.here.com/geocoding-and-search/docs/result-types-address.md)
