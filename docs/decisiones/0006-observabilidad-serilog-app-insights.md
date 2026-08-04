# ADR-0006: Observabilidad con Serilog + Application Insights + correlation IDs

- **Estado**: Aceptada
- **Fecha**: 2026-05-01
- **Decisores**: Eduardo Paredes
- **Etiquetas**: observabilidad, operación, fundación

## Contexto y problema

En un monolito modular con eventos asíncronos (Service Bus + Outbox pattern),
una sola operación de negocio puede atravesar varios módulos. Por ejemplo:
"timbrar un CFDI" toca el módulo Fiscal (genera XML, llama al PAC), publica un
evento que consume Cobranza (genera el documento por cobrar), publica otro
evento que consume Contabilidad (asienta la póliza). Si algo falla en el medio,
sin trazabilidad cruzada es imposible saber qué pasó.

Necesitamos observabilidad estructurada desde el día uno: logs útiles, métricas
relevantes, y trazas correlacionadas a través de operaciones que cruzan módulos
y procesos.

## Drivers de la decisión

- Correlación de logs a través de un mismo flujo de negocio, aunque pase por múltiples módulos y workers asíncronos
- Logs estructurados (no texto plano) para poder consultarlos eficientemente
- Captura de métricas (latencia, throughput, errores) sin código instrumentado a mano en cada handler
- Bajo costo operacional (no operar Elasticsearch propio si hay alternativa gestionada)
- Cumplimiento: NO logear datos sensibles (RFCs completos sin enmascarar, montos confidenciales, JWTs, contraseñas)

## Opciones consideradas

1. Serilog + Azure Application Insights + W3C Trace Context
2. Serilog + Seq self-hosted
3. Serilog + ELK (Elasticsearch + Logstash + Kibana) self-hosted
4. OpenTelemetry directo + Datadog/New Relic
5. Logging por defecto de .NET sin librería extra

## Decisión

Se adopta **Serilog como librería de logging**, con sink a **Azure Application
Insights**, y **propagación de correlation IDs vía W3C Trace Context** (header
`traceparent`).

Cada request HTTP entrante recibe (o reusa, si viene del cliente) un
`TraceId`. Ese `TraceId` se propaga:

- A todos los logs emitidos durante el request (vía `LogContext`)
- A los mensajes publicados a Service Bus (en propiedades del mensaje)
- A los handlers de mensajes consumidos (lo extraen y lo restauran al `LogContext`)
- A llamadas HTTP salientes (propagado automáticamente por `HttpClient` con la integración estándar)

Convenciones de logging:

- Niveles: `Verbose` y `Debug` solo en desarrollo; `Information` en adelante en QA/Prod
- Formato estructurado: usar templates con propiedades nombradas, nunca interpolación
  - Sí: `_logger.LogInformation("CFDI timbrado {CfdiId} para {ClienteRfc}", cfdiId, rfc)`
  - No: `_logger.LogInformation($"CFDI timbrado {cfdiId} para {rfc}")`
- Datos sensibles: enriquecedor `MaskSensitivePropertiesEnricher` con política **balanceada** para contexto mexicano:
  - **Enmascarar siempre**: `password`, `token`, `bearer`, `authorization`, `apiKey`, `clientSecret`, `curp`, `clabe`, `numeroCuenta`, `numeroTarjeta`, `cvv`, `nombrePersonaFisica`, `apellidoPaterno`, `apellidoMaterno`, `emailPersonal`, `telefonoPersonal`
  - **NO enmascarar** (decisión explícita): `rfc` (de personas morales y físicas), `domicilioFiscal`, `razonSocial`, `nombreComercial`. Tratamos el RFC como dato quasi-público dado que aparece en CFDIs y reportes oficiales; si en el futuro hay que diferenciar moral vs. física, se distinguirá por convención de campo
  - El enmascaramiento deja los primeros 2 y últimos 2 caracteres visibles (ej. `CU**********AB`) para permitir debugging sin exponer el dato completo
- Cada módulo agrega un enriquecedor que pone `Module = "Fiscal"` (o el nombre del módulo) en todos sus logs

Métricas:

- Application Insights captura automáticamente latencia y throughput de requests HTTP, llamadas a BD vía EF Core, llamadas a HttpClient, mensajes de Service Bus
- Custom metrics solo cuando agregan valor de negocio (ej. CFDIs timbrados por hora, tasa de éxito del PAC)

Sampling:

- Se mantiene el **sampling adaptativo por defecto** de Application Insights (la SDK ajusta dinámicamente la tasa para mantener costo bajo)
- **Mitigación obligatoria**: configurar `ExcludedTypes = "Exception"` en la regla de sampling para preservar **el 100% de las excepciones**, sin importar el volumen. Sin esta exclusión explícita, las excepciones caen en el muestreo igual que el resto de la telemetría
- Si en operación se detecta que se pierden eventos críticos por sampling, se reevaluará la política (posible pasar a fixed-rate sampling de 100% en eventos de negocio críticos)

Región y residencia de datos:

- El recurso de Application Insights vive en **Azure Mexico Central** para mantener residencia nacional de los datos de telemetría
- Implica un costo ligeramente mayor que regiones más maduras (East US, etc.) pero alinea con la política de mantener datos del ERP en territorio nacional
- Decisión consistente con la región esperada del resto de los recursos (App Service, PostgreSQL, Service Bus, Storage)

Relación con auditoría (ADR-0008):

- Esta ADR cubre **logs operacionales**: qué pasó técnicamente, latencias, errores, debugging
- ADR-0008 cubre **auditoría de negocio**: quién cambió qué dato, cuándo, con qué valor anterior y posterior
- Son complementarias y NO se sustituyen. El audit log vive en la BD del ERP; los logs operacionales viven en App Insights. Un cambio de un CFDI genera ambos: un registro en `audit_log` (para cumplimiento) y eventos en App Insights (para debugging y métricas)

Correlación a través de SignalR:

- W3C Trace Context se propaga **automáticamente** en HTTP entrante, `HttpClient` saliente y mensajes de Service Bus (con la integración estándar de OpenTelemetry)
- En **SignalR** la propagación NO es automática: cuando el servidor envía una notificación a un cliente vía hub, el `traceparent` no viaja por defecto en el payload del mensaje WebSocket
- Para tener correlación end-to-end (request HTTP → cambio de estado → evento publicado → notificación SignalR → cliente recibe), se debe propagar el `traceId` **manualmente** como parte del payload del hub (ej. campo `correlationId` en el DTO enviado al cliente)
- Documentar este patrón en `CLAUDE.md` y aplicarlo de forma consistente en todos los hubs

## Consecuencias

**Positivas**
- Una sola operación de negocio se ve completa en App Insights, aunque pase por workers asíncronos
- Logs consultables con KQL (Kusto Query Language) en Azure Monitor
- Cero infraestructura adicional que mantener (App Insights es gestionado)
- Integración nativa con .NET y con SignalR

**Negativas**
- Costo de App Insights por GB ingestado (~2.30 USD/GB después de 5 GB gratis al mes); en Mexico Central el precio es marginalmente mayor que East US. Estimación inicial para tráfico moderado de oficina: 50-60 USD/mes. Si crece el volumen sin sampling adicional, puede llegar a 150-200 USD/mes
- Vendor lock-in con Azure Monitor (mitigable si en el futuro se usa OpenTelemetry como abstracción)
- Latencia de unos minutos en aparición de logs en consola de Azure (no es para debugging en vivo, sino para análisis)
- Sampling adaptativo puede descartar telemetría no-excepción durante picos de tráfico (riesgo asumido conscientemente; mitigable extendiendo `ExcludedTypes` a más tipos críticos si surgen)

## Descartadas

**Seq**. Excelente herramienta de logs, pero requiere operarla. Sin ventaja
clara cuando App Insights está disponible y ya está en el stack de Azure.

**ELK self-hosted**. Capaz pero costo operacional altísimo (Elasticsearch
necesita cuidado, almacenamiento, parches, escalabilidad). Solo justificable
si hay volumen muy alto de logs, que no es el caso.

**Datadog / New Relic**. Capacidades excelentes, pero costo desproporcionado
(~$15/host/mes en adelante) para un proyecto que recién arranca.

**Logging por defecto de .NET**. No soporta sinks estructurados ni
enriquecedores nativamente. Es el punto de partida pero no la solución.

## Notas de implementación

- Crear recurso de Application Insights en **Azure Mexico Central** vía módulo Bicep (`infra/modules/app-insights.bicep`)
- Configurar retención por defecto (90 días); evaluar extensión a 730 días solo si surge necesidad explícita
- Instalar paquetes: `Serilog.AspNetCore`, `Serilog.Enrichers.Environment`, `Serilog.Enrichers.Process`, `Serilog.Enrichers.Thread`, y `Azure.Monitor.OpenTelemetry.AspNetCore` (Azure Monitor OpenTelemetry Distro). Serilog produce los logs estructurados; el Distro los exporta a Application Insights junto con traces y métricas, y agrega instrumentación automática de `HttpClient`, EF Core y Service Bus sin configuración manual. El sink directo `Serilog.Sinks.ApplicationInsights` queda obsoleto en favor de esta integración
- Configurar `Program.cs` con el pipeline de Serilog antes de construir el host, y registrar el OTel Distro vía `AddOpenTelemetry().UseAzureMonitor()`
- Crear `MaskSensitivePropertiesEnricher` con la lista balanceada definida arriba; tests unitarios que verifiquen que CURP/CLABE/etc. siempre se enmascaran y que RFC/domicilio NO se enmascaran
- Configurar regla de sampling con `ExcludedTypes = "Exception"` en `applicationinsights.json` o vía código en `TelemetryProcessorChainBuilder`
- La propagación del `traceparent` (W3C Trace Context) entre HTTP, EF Core y Service Bus viene incluida en el OTel Distro — cero configuración manual
- En cada hub de SignalR: convención de incluir `correlationId` en los DTOs salientes, restaurarlo en cliente para correlación con logs frontend (si se hace logging en frontend)
- Documentar convenciones en `CLAUDE.md`: cómo loguear, qué NO loguear, cómo usar `LogContext` en handlers de Service Bus, cómo propagar `traceId` en hubs de SignalR
- Considerar agregar un health check endpoint (`/health`) que App Insights monitoree como availability test (decisión separada en backlog)
