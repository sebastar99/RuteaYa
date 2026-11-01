# Rutas con HERE Routing API v8

## Arquitectura y uso

`RuteaYa/Services/RutasHereServicio.cs` implementa `IRutasServicio` de
`RuteaYa.Logica`. El adaptador HTTP vive en MVC; la lógica de desvíos no depende
de HERE, de opciones de ASP.NET ni de modelos JSON externos.

MVC registra el cliente tipado mediante `AddHttpClient<IRutasServicio,
RutasHereServicio>`. Su timeout es de 30 segundos. No se crean endpoints de la API
propia y no se hacen llamadas a HERE durante el arranque.

El contrato recibe coordenadas WGS84 ya resueltas. El primer punto es `origin`,
el último `destination` y cada intermedio se envía como un `via` independiente,
en el orden recibido. Los `via` son paradas (stopover), sin duración adicional
configurada. No se realiza geocodificación ni reordenamiento.

Parámetros de esta versión:

- `transportMode=car` y `routingMode=fast`.
- `departureTime=any`: cálculo sin una hora de salida específica, sin tráfico en
  tiempo real. HERE puede considerar incidentes de larga duración. Es una
  estimación, no una promesa de tiempo de viaje.
- `return=summary` y `alternatives=0`.
- Se suman `summary.length` (metros) y `summary.duration` (segundos) de **todas**
  las secciones de la primera ruta. No se suman rutas alternativas ni se usa
  `baseDuration` en lugar de `duration`.

## API Key local, fuera del repositorio

La clave se obtiene de `HereRouting:ApiKey`. No se agrega a `appsettings.json`.
El proyecto MVC tiene un `UserSecretsId` para desarrollo. Ejecutar localmente,
reemplazando el marcador con la clave personal, sin compartir su valor:

```powershell
dotnet user-secrets set "HereRouting:ApiKey" "<TU_API_KEY>" --project "RuteaYa/RuteaYa.Web.csproj"
```

En desarrollo debe estar activo el ambiente `Development` para cargar User Secrets.
Como alternativa, el proveedor de variables de entorno admite `HereRouting__ApiKey`.
En un despliegue, suministrar esa variable desde el gestor de secretos del entorno.
User Secrets es un mecanismo de desarrollo fuera del repositorio, no un almacén cifrado.

La clave se valida al calcular: MVC puede arrancar sin ella, pero una consulta
de rutas falla con un mensaje claro. La autenticación con API Key usa la query
según el contrato oficial. Se deshabilitan los loggers del cliente HERE y las
redirecciones automáticas; las excepciones del adaptador no incluyen la URL,
el cuerpo remoto ni excepciones internas que puedan revelar la clave.

## Errores y cancelación

- Puntos insuficientes, nulos o fuera de rango: `ArgumentException` o su derivada.
- Clave ausente: `InvalidOperationException`, antes de enviar HTTP.
- HTTP no exitoso o error de red: `HttpRequestException`; se conserva el estado
  HTTP cuando existe. No se reintenta ni se reemplaza el error por datos mock.
- JSON inválido, ruta ausente, sección incompleta, totales negativos o desbordados:
  `InvalidOperationException`.
- Avisos `critical` o `noRouteFound`: se rechaza la respuesta. Los avisos
  informativos no invalidan los totales.
- Cancelación del llamador: `OperationCanceledException` con su token.
- Vencimiento del timeout HTTP: `TimeoutException`.

## Pruebas y solicitud real

`RuteaYa.Test/RutasHereServicioTest.cs` utiliza un `HttpMessageHandler` simulado
y claves ficticias. No requiere conectividad ni cuenta HERE.

Para una prueba real hace falta una API Key habilitada para HERE Routing API v8,
cuota disponible, acceso HTTPS a `router.hereapi.com` y al menos dos coordenadas
en una zona enrutable. Luego, desde un consumidor MVC o una ejecución local de
desarrollo que resuelva el servicio registrado, invocar:

```csharp
var rutas = servicios.GetRequiredService<IRutasServicio>();
var resultado = await rutas.CalcularAsync(new[]
{
    new PuntoRutaDto { Latitud = -34.6037m, Longitud = -58.3816m },
    new PuntoRutaDto { Latitud = -34.6158m, Longitud = -58.4333m }
}, cancellationToken);
```

`servicios` representa un `IServiceProvider` de MVC; los tipos de negocio están
en los namespaces `RuteaYa.Logica` y `RuteaYa.Logica.DTOs`. Esta etapa no añade una
pantalla ni un endpoint de prueba. No se ejecutaron consultas reales con credenciales.

## Documentación oficial consultada

- [GET /v8/routes: parámetros, autenticación y respuesta](https://docs.here.com/routing/reference/routing-api-v8-calculateroutes.md)
- [Paradas intermedias y orden de via](https://docs.here.com/routing/docs/routing-v8-intermediate-waypoints.md)
- [Resumen por sección, unidades y totales](https://docs.here.com/routing/docs/routing-v8-route-summary.md)
- [Avisos y severidad critical](https://docs.here.com/routing/docs/routing-v8-notice.md)
