# DocumentReprocessor

Programa de consola en **C# / .NET 10** que toma archivos `.txt` de una carpeta configurable, los envía al endpoint de NumRot (SendDIAN ENR PDF) —en paralelo de forma configurable— y registra el resultado en logs JSON y un Excel por corrida. Los archivos enviados correctamente se mueven a una carpeta de procesados.

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Acceso de red al endpoint de producción
- Un token Bearer válido

## Configuración

Edita el archivo `appsettings.json` (se copia junto al ejecutable al compilar).

Ese archivo **no se versiona** (está en `.gitignore` porque contiene el token). En el repo está `appsettings.Example.json`: cópialo como `appsettings.json` y completa token y rutas:

```json
{
  "Api": {
    "EndpointUrl": "https://nra_api_prod.numrotapi.net/api/SendDIAN/Enr/Pdf",
    "BearerToken": "TU_TOKEN_AQUI",
    "ContentType": "text/plain",
    "WrapBodyAsJsonString": false
  },
  "Paths": {
    "InputFolder": "C:\\Documentos\\Pendientes",
    "ProcessedFolder": "C:\\Documentos\\Procesados",
    "LogFolder": "C:\\Documentos\\Logs",
    "ReportFolder": "C:\\Documentos\\Logs\\Reportes"
  },
  "Processing": {
    "MaxDegreeOfParallelism": 20,
    "MaxRetryAttempts": 3,
    "RetryBaseDelayMilliseconds": 3000,
    "HttpTimeoutSeconds": 45
  }
}
```

| Clave | Descripción |
| --- | --- |
| `Api:EndpointUrl` | URL del endpoint POST |
| `Api:BearerToken` | Token de autenticación. **Actualízalo manualmente cuando expire** |
| `Api:ContentType` | Media type del body. Por defecto `text/plain` (como Postman en modo Text) |
| `Api:WrapBodyAsJsonString` | Si es `true`, envuelve el contenido del txt como string JSON |
| `Paths:InputFolder` | Carpeta con los `.txt` pendientes de envío |
| `Paths:ProcessedFolder` | Carpeta destino de los archivos enviados correctamente |
| `Paths:LogFolder` | Carpeta donde se escriben los logs JSON |
| `Paths:ReportFolder` | Carpeta del Excel por corrida. Si se omite, usa `LogFolder` |
| `Processing:MaxDegreeOfParallelism` | Máximo de documentos enviados **a la vez** (no son lotes). `1` = secuencial. Default `20` |
| `Processing:MaxRetryAttempts` | Reintentos extra ante errores transitorios (429, 503, timeouts) |
| `Processing:RetryBaseDelayMilliseconds` | Espera base entre reintentos (crece con cada intento) |
| `Processing:HttpTimeoutSeconds` | Timeout HTTP por petición |

## Cómo ejecutar

Desde la carpeta del proyecto:

```bash
dotnet restore
dotnet run
```

O publicar un ejecutable:

```bash
dotnet publish -c Release -o .\publish
.\publish\DocumentReprocessor.exe
```

Antes de ejecutar:

1. Coloca los archivos `.txt` en `InputFolder`.
2. Configura un `BearerToken` válido en `appsettings.json`.
3. Asegúrate de que las carpetas de entrada, procesados y logs existan o puedan crearse (la de entrada debe existir).

## Flujo de trabajo

1. Lee todos los `.txt` de la carpeta de entrada (solo el primer nivel).
2. Por cada archivo, hace un `POST` con el **contenido completo del archivo** como body, `Content-Type: text/plain` y autenticación `Bearer`. Hasta `MaxDegreeOfParallelism` documentos se envían al mismo tiempo.
3. Extrae el número de documento (`FAD05`), el NIT (`FAJ21`), la fecha (`FAD09`) y la hora (`FAD10`) para trazabilidad.
4. Escribe un log JSON con fecha/hora de proceso, NIT, `FAD05`, fecha/hora del documento, código HTTP, mensaje completo de la API (`StatusMessage` / `StatusDescription`), cuerpo de respuesta y resultado.
5. Si la respuesta HTTP es exitosa (2xx), mueve el archivo a `ProcessedFolder` con fecha/hora en el nombre (por ejemplo `factura_20260924_093015.txt`) para no sobrescribir versiones anteriores.
6. Si falla, el archivo **permanece** en la carpeta de entrada; el resto del lote **sigue** procesándose. Ante 429/503/timeouts se reintenta según configuración.
7. Al final de la corrida genera un Excel (`RunReport_yyyyMMdd_HHmmss.xlsx`) con todos los documentos de esa ejecución (ordenado por nombre de archivo).

## Envío paralelo

`MaxDegreeOfParallelism` limita cuántas peticiones pueden estar **en vuelo** a la vez (no divide en lotes fijos).

Con capacidad del API de ~1000 req/min, `20` es un valor inicial razonable. Ajusta según el tiempo real de cada respuesta:

`MaxDegreeOfParallelism ≈ (1000 / 60) × segundos_por_request` (dejando margen).

Usa `1` si quieres volver al modo secuencial.

## Reporte Excel por corrida

Al terminar cada ejecución se crea un archivo `.xlsx` en `ReportFolder` (o en `LogFolder` si no se configura) con columnas:

| Columna | Contenido |
| --- | --- |
| `NumeroDocumento` | Número de documento (`FAD05`) |
| `NitEmpresa` | NIT de la empresa (`FAJ21`) |
| `FechaDocumento` | Fecha del documento (`FAD09`) |
| `HoraDocumento` | Hora del documento (`FAD10`) |
| `CodigoHttp` | Código HTTP de la respuesta |
| `CodigoApi` | Código de negocio del JSON de la API |
| `MensajeRespuesta` | Mensaje completo de respuesta |
| `Exitoso` | Si el envío HTTP fue exitoso |
| `Archivo` | Nombre del archivo `.txt` |
| `FechaHoraProceso` | Fecha/hora del procesamiento |

## Formato del log

Cada documento genera un archivo JSON en `LogFolder` nombrado con fecha/hora, NIT y `FAD05`, por ejemplo `20260323_143015_800215758_A332242.json`:

```json
{
  "timestamp": "2026-03-23T14:30:15.1234567-05:00",
  "fileName": "factura001.txt",
  "documentNumber": "A332242",
  "companyNit": "800215758",
  "documentDate": "2025-12-01",
  "documentTime": "09:51:35-05:00",
  "sourcePath": "C:\\Documentos\\Pendientes\\factura001.txt",
  "processedPath": "C:\\Documentos\\Procesados\\factura001.txt",
  "statusCode": 200,
  "apiStatusCode": "200",
  "statusMessage": "Documento con errores en campos mandatorios.",
  "statusDescription": "Validación contiene errores en campos mandatorios.",
  "responseMessage": "StatusCode=200 | Documento con errores en campos mandatorios. | Validación contiene errores en campos mandatorios.",
  "trackId": "...",
  "uuid": "...",
  "success": true,
  "responseBody": "...",
  "errorMessage": null
}
```

## Notas sobre el token

El token es dinámico y lo administra el usuario. Cuando expire, actualiza `Api:BearerToken` en `appsettings.json` y vuelve a ejecutar el programa. No es necesario recompilar si usas el `appsettings.json` de la carpeta de salida/publicación.

## Estructura del proyecto

```
DocumentReprocessor/
├── Program.cs
├── appsettings.Example.json
├── DocumentReprocessor.csproj
├── Configuration/
│   └── AppSettings.cs
├── Models/
│   └── DocumentLogEntry.cs
└── Services/
    ├── DianApiClient.cs
    ├── DocumentProcessor.cs
    ├── EnrDocumentParser.cs
    ├── ExcelRunReportWriter.cs
    └── JsonFileLogger.cs
```
