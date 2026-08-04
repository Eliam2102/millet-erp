# ADR-0019: Health checks y readiness probes

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: operación, monitoreo, infraestructura, tier-2

## Contexto y problema

Azure App Service y los orquestadores modernos necesitan saber dos cosas
del proceso de la app:

- **¿Está vivo?** Si no responde, el orquestador lo reinicia
- **¿Está listo?** Si no, el orquestador deja de enrutar tráfico hacia esa instancia (pero no la reinicia)

Sin endpoints específicos para esto, el comportamiento por defecto es
problemático: durante el arranque, mientras se aplican migraciones o se
calientan caches, App Service ya enruta tráfico y los usuarios ven errores.
Después en operación, si la BD se cae temporalmente, sin readiness probe
la instancia sigue recibiendo tráfico que va a fallar.

Necesitamos endpoints específicos, rápidos, y diferenciados según el
consumidor (Azure vs humanos vs alertas).

## Drivers de la decisión

- Distinción clara entre liveness y readiness (problemas distintos, respuestas distintas)
- Endpoints rápidos (<50ms) para que Azure no los marque como timeout
- No exponer detalles internos a tráfico no autenticado
- Visibilidad para humanos (debugging, dashboards) sin comprometer seguridad
- Integración natural con Application Insights (ADR-0006) para alertas

## Opciones consideradas

1. Tres endpoints: `/health/live`, `/health/ready`, `/health` (con auth para detalle)
2. Un solo endpoint `/health` con detalle siempre
3. Endpoints separados por dependencia (`/health/db`, `/health/service-bus`)
4. Sin health checks (default de App Service: TCP ping al puerto)

## Decisión

Se adopta la **opción 1**: tres endpoints con responsabilidades distintas,
construidos sobre `Microsoft.Extensions.Diagnostics.HealthChecks` (built-in
en ASP.NET Core, sin paquetes externos).

### Endpoints

**`/health/live` — Liveness probe**

- Consumidor: Azure App Service liveness probe
- Verifica: que el proceso está vivo y respondiendo HTTP
- NO verifica dependencias externas
- Respuesta: `200 OK` con body vacío si está vivo, `503 Service Unavailable` si está cayéndose
- Tiempo objetivo: < 10ms
- Sin autenticación

**`/health/ready` — Readiness probe**

- Consumidor: Azure App Service readiness probe
- Verifica: que la app está lista para recibir tráfico (BD accesible, Service Bus disponible, migraciones aplicadas, etc.)
- Respuesta: `200 OK` con body vacío si está listo, `503 Service Unavailable` con body vacío si no
- Tiempo objetivo: < 50ms
- Sin autenticación

**`/health` — Diagnóstico humano**

- Consumidor: dashboards, equipo de operación, alertas
- Verifica: TODO (incluye dependencias degradables)
- Respuesta: JSON detallado con estado por dependencia, latencias, mensajes
- Tiempo objetivo: sin límite estricto (puede tomar segundos)
- **Requiere autenticación** o IP allowlist (no se expone públicamente)

### Health checks a configurar

| Check                 | Tag           | Crítico  | Descripción                                                       |
|-----------------------|---------------|----------|-------------------------------------------------------------------|
| `postgres`            | `ready`       | Sí       | `SELECT 1` con timeout 2s contra el connection pool               |
| `migrations_applied`  | `ready`, `startup` | Sí  | Verifica que las migraciones esperadas existen en `__EFMigrationsHistory` |
| `service_bus`         | `ready`       | Sí       | Ping al namespace; verifica conectividad                          |
| `key_vault`           | `startup`     | Sí       | (Solo en arranque) Lee un secret de prueba; falla evita arranque  |
| `application_insights`| `ready`       | No       | Connection string válido; degradación, no rechazo                 |
| `signalr_service`     | `ready`       | No       | Conectividad al SignalR Service; degradación                      |

**Tags y filtrado**:

- `live`: ningún check de dependencia (solo el endpoint responde)
- `ready`: todos los checks marcados con `ready` deben pasar (los críticos hacen fallar 503; los no-críticos solo degradan estado)
- `startup`: checks que se evalúan UNA VEZ al arranque; si fallan, la app no termina de iniciar
- `/health`: incluye todos los checks (con tag o sin tag)

**Configuración**:

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: config.GetConnectionString("Postgres"),
        name: "postgres",
        tags: new[] { "ready" },
        timeout: TimeSpan.FromSeconds(2))
    .AddCheck<MigrationsAppliedHealthCheck>(
        name: "migrations_applied",
        tags: new[] { "ready", "startup" })
    .AddAzureServiceBusQueue(
        name: "service_bus",
        tags: new[] { "ready" })
    .AddAzureKeyVault(
        name: "key_vault",
        tags: new[] { "startup" })
    .AddCheck("application_insights", () =>
        string.IsNullOrEmpty(config["ApplicationInsights:ConnectionString"])
            ? HealthCheckResult.Degraded("Connection string ausente")
            : HealthCheckResult.Healthy(),
        tags: new[] { "ready" })
    .AddCheck<SignalRHealthCheck>(
        name: "signalr_service",
        tags: new[] { "ready" });
```

**Mapeo de endpoints**:

```csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,  // sin checks; solo responde si la app está viva
    AllowCachingResponses = false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
    AllowCachingResponses = false,
    ResultStatusCodes = new Dictionary<HealthStatus, int>
    {
        [HealthStatus.Healthy] = 200,
        [HealthStatus.Degraded] = 200,    // degradado sigue recibiendo tráfico
        [HealthStatus.Unhealthy] = 503
    }
});

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = WriteDetailedHealthResponse  // JSON con detalle
})
.RequireAuthorization("HealthCheckPolicy");  // requiere auth o IP allowlist
```

### Formato de respuesta del `/health` detallado

```json
{
  "status": "Healthy",
  "totalDurationMs": 45,
  "timestamp": "2026-05-02T20:30:15.123Z",
  "checks": [
    {
      "name": "postgres",
      "status": "Healthy",
      "durationMs": 12,
      "tags": ["ready"],
      "data": { "version": "16.2", "connectionsActive": 3 }
    },
    {
      "name": "service_bus",
      "status": "Degraded",
      "durationMs": 250,
      "tags": ["ready"],
      "description": "Latencia alta detectada",
      "data": { "namespace": "millet-erp-prod" }
    },
    {
      "name": "application_insights",
      "status": "Healthy",
      "durationMs": 1,
      "tags": ["ready"]
    }
  ]
}
```

`ResponseWriter` custom: `WriteDetailedHealthResponse` serializa el `HealthReport`
al formato anterior. NO se usa el writer por defecto de
`HealthChecks.UI.Client` porque es verbose y no controlamos su shape.

### Acceso al `/health` detallado

Tres caminos posibles, se elige según ambiente:

- **Desarrollo local**: sin auth (cualquiera puede consultar)
- **QA y Producción**: requiere autenticación con permiso especial `infra.health.leer` (ADR-0007). Asignado a roles `SuperAdmin` y `Infraestructura`
- **Alternativa adicional**: IP allowlist (rangos de la red corporativa de Millet) configurada en App Service para no requerir auth desde dentro de la red

La policy `HealthCheckPolicy` se configura en `Program.cs` para validar
ambos caminos: bypass por IP O permiso explícito.

### Integración con Azure App Service

**Configuración en Bicep**:

```bicep
properties: {
  siteConfig: {
    healthCheckPath: '/health/ready'
    // ...
  }
}
```

- App Service consulta `/health/ready` cada 30s
- Si falla 3 veces consecutivas, **deja de enrutar tráfico** a esa instancia (pero no la reinicia)
- Liveness se configura a nivel de plataforma (Azure App Service no tiene liveness separado del health check; en Linux con contenedores se usa `HEALTHCHECK` del Dockerfile)

**Para Container Apps (alternativa futura)**:

- `livenessProbe`: apunta a `/health/live`
- `readinessProbe`: apunta a `/health/ready`
- `startupProbe`: apunta a `/health/ready` con tolerancia mayor (timeout 60s) durante el arranque

### Integración con Application Insights (ADR-0006)

- Cada `HealthCheckResult` que sea `Unhealthy` o `Degraded` se loguea automáticamente como evento custom `HealthCheckFailed` con detalle del check, dependencia, y mensaje
- Custom metric `health_check_duration_ms` por nombre de check (permite dashboards de tiempo de respuesta de dependencias)
- Custom metric `health_check_status` (0/1/2 para Healthy/Degraded/Unhealthy) por nombre de check
- **Alertas configuradas en App Insights**:
  - Si un check crítico (`postgres`, `service_bus`) está `Unhealthy` por más de 5 min consecutivos: alerta P1 a equipo de operación
  - Si un check no-crítico (`application_insights`) está `Degraded` por más de 30 min: alerta P3 informativa

### Migrations Health Check (custom)

```csharp
public class MigrationsAppliedHealthCheck : IHealthCheck
{
    private readonly IEnumerable<DbContext> _contexts;

    public async Task<HealthCheckResult> CheckHealthAsync(...)
    {
        foreach (var ctx in _contexts)
        {
            var pending = await ctx.Database.GetPendingMigrationsAsync();
            if (pending.Any())
                return HealthCheckResult.Unhealthy(
                    $"Migraciones pendientes en {ctx.GetType().Name}: {string.Join(", ", pending)}");
        }
        return HealthCheckResult.Healthy();
    }
}
```

Útil porque:
- Si la app arranca antes de que el job de migración termine, `/health/ready` responde 503 hasta que las migraciones están aplicadas
- Azure no enruta tráfico a una instancia con BD desincronizada

### Lo que esta ADR explícitamente NO cubre

- **Synthetic monitoring** desde fuera (Application Insights Availability Tests, Azure Front Door): se configura cuando QA esté arriba; ADR de infra
- **Health checks específicos del PAC fiscal**: cuando exista el módulo Fiscal, registra su check propio (`AddCheck<PacHealthCheck>`)
- **UI dashboard** (HealthChecks.UI): si surge necesidad de un dashboard visual, se agrega; por ahora App Insights cumple
- **Health checks de jobs en background** (outbox publisher, cleanup): se incluyen como checks custom cuando se implementen los jobs en Fase 2

## Consecuencias

**Positivas**
- Azure App Service deja de enrutar tráfico a instancias con problemas
- Arranque seguro: la app no recibe tráfico hasta que migraciones aplican
- Diagnóstico humano detallado disponible vía `/health` con auth
- Integración con App Insights permite alertas proactivas
- Endpoints rápidos y minimalistas para los probes (sin overhead de logging detallado)
- Modelo extensible: cada módulo puede registrar sus checks adicionales

**Negativas**
- Cuatro tipos de evaluación (live, ready, startup, detallado): hay que entender la diferencia. Mitigado por documentación
- Custom `MigrationsAppliedHealthCheck` requiere mantener referencia a todos los `DbContext`. Aceptable
- Configuración de policy de auth para `/health` agrega complejidad. Mitigado: la policy es una; se reusa
- Health checks consumen recursos (queries cada 30s a Postgres, ping a Service Bus). Mínimo, pero existe

## Descartadas

**Un solo endpoint `/health`**. Mezcla responsabilidades: el de Azure
necesita ser rápido y minimalista; el de humanos necesita detalle. Hacerlos
iguales obliga a comprometer en ambos ejes.

**Endpoints separados por dependencia** (`/health/db`, `/health/service-bus`).
Multiplica configuración sin ganancia: el detalle ya está disponible en `/health`.
Y Azure solo consulta uno; los demás serían no usados.

**Sin health checks**. App Service haría TCP ping al puerto, lo cual solo
detecta si el puerto está abierto, no si la app realmente puede servir
requests. Inaceptable: una app con BD caída sigue aceptando conexiones TCP
y respondiendo errores.

## Notas de implementación

**Backend**

- Health checks built-in son parte de `Microsoft.Extensions.Diagnostics.HealthChecks` (sin paquete extra)
- Para checks específicos: `AspNetCore.HealthChecks.NpgSql`, `AspNetCore.HealthChecks.AzureServiceBus`, `AspNetCore.HealthChecks.AzureKeyVault` (paquetes Xabaril, MIT)
- Configurar en `Program.cs` con `services.AddHealthChecks().AddX()`
- Mapear los tres endpoints con sus predicates y options
- `MigrationsAppliedHealthCheck` custom en `Shared/Infrastructure/HealthChecks/`
- `WriteDetailedHealthResponse` custom serializer en `Shared/Infrastructure/HealthChecks/`
- Policy de autorización `HealthCheckPolicy` que valida bypass por IP O permiso `infra.health.leer`

**Infraestructura (Bicep)**

- App Service: `siteConfig.healthCheckPath = '/health/ready'`
- IP restrictions opcional en `siteConfig.ipSecurityRestrictions` para `/health` permitiendo solo rangos corporativos
- Application Insights: alertas configuradas para `HealthCheckFailed`

**Tests**

- Test de integración por endpoint:
  - `/health/live` retorna 200 sin auth
  - `/health/ready` retorna 200 cuando todas las dependencias responden
  - `/health/ready` retorna 503 cuando Postgres está caído (simulado con Testcontainers stop)
  - `/health` requiere auth en QA/Prod simulado
- Test del `MigrationsAppliedHealthCheck`: con migraciones pendientes retorna `Unhealthy`

**Documentación en `CLAUDE.md`**

- Cuándo agregar un health check nuevo (al integrar una dependencia externa)
- Cómo elegir el tag (`live` / `ready` / `startup`)
- Cómo decidir si un check es crítico (afecta enrutamiento) o no (solo degrada)
- Cómo testear que el check funciona en QA antes de producción

**ADRs hijo posibles**

- Estrategia de availability tests / synthetic monitoring desde fuera
- Health UI dashboard si surge necesidad
- SLOs específicos por dependencia (latencia objetivo, disponibilidad esperada)
