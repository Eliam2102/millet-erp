# ADR-0009: Outbox pattern para eventos de integración entre módulos

- **Estado**: Aceptada
- **Fecha**: 2026-05-01
- **Decisores**: Eduardo Paredes
- **Etiquetas**: arquitectura, mensajería, fundación

## Contexto y problema

Los módulos del ERP se comunican entre sí mediante **eventos de integración**
publicados a Azure Service Bus. Por ejemplo:

- Fiscal publica `CfdiTimbradoEvent` → Cobranza lo consume y crea el documento por cobrar
- Cobranza publica `PagoAplicadoEvent` → Contabilidad lo consume y asienta la póliza
- Comercial publica `CotizacionAutorizadaEvent` → Compras lo consume si dispara reposición

El problema clásico: **¿cómo garantizar que el cambio en BD y la publicación
del evento sean consistentes?** Si el módulo escribe en BD y luego intenta
publicar a Service Bus pero falla la publicación, queda un cambio sin evento
(integridad rota). Si publica primero y luego falla la BD, hay un evento
fantasma que dispara consumidores para algo que no ocurrió.

Necesitamos un mecanismo que garantice "todo o nada" entre cambio de estado y
publicación de evento.

## Drivers de la decisión

- Consistencia transaccional entre BD y mensajería
- Resiliencia a fallos transitorios de Service Bus
- Idempotencia en el consumo (un evento puede llegar dos veces)
- Bajo overhead en el caso normal (la mayoría de las publicaciones funcionan a la primera)
- Posibilidad de reproducir eventos para depuración o reproceso

## Opciones consideradas

1. Outbox pattern propio (tabla `outbox` por módulo + worker publisher)
2. MassTransit con su Outbox integrado
3. CAP (.NET library para outbox)
4. Wolverine (framework de mensajería con outbox)
5. Two-Phase Commit / transacciones distribuidas
6. Publicar directamente sin outbox (aceptar la inconsistencia ocasional)

## Decisión

Se propone **implementar Outbox pattern propio**, ligero, integrado con EF
Core.

**Mecánica**:

Cada módulo tiene su tabla `integration_events_outbox` en su esquema:

```
integration_events_outbox
├── id (uuid v7, PK)
├── empresa_id (uuid, FK compartido.empresas, nullable)  -- nullable para eventos de sistema
├── tipo_evento (text, not null)         -- 'CfdiTimbradoEvent', etc.
├── version (int, not null, default 1)   -- versión del schema del evento
├── payload (jsonb, not null)            -- el evento serializado
├── correlation_id (uuid, not null)      -- mismo TraceId que App Insights (ADR-0006)
├── created_at (timestamptz, not null)
├── published_at (timestamptz, nullable)
├── intentos (int, default 0)
└── ultimo_error (text, nullable)        -- mensaje del último intento fallido, si aplica
```

Índices: `(published_at, created_at)` parcial donde `published_at IS NULL`
(para que el worker tome los pendientes rápido), `(empresa_id, created_at)`
para queries operacionales/auditoría.

**Flujo de publicación**:

1. El handler de un comando hace cambios en sus entidades de dominio
2. Antes de `SaveChanges`, agrega los eventos pendientes a la tabla `outbox` del mismo `DbContext`
3. `SaveChanges` ejecuta una sola transacción que escribe entidades + outbox
4. Un **worker publisher** corre cada N segundos, lee eventos no publicados
   en lote, los publica a Service Bus, y marca `published_at`

**Flujo de consumo (idempotencia)**:

Cada módulo consumidor tiene una tabla `integration_events_processed`:

```
integration_events_processed
├── event_id (uuid, PK)            -- el id del evento de outbox del publisher
├── empresa_id (uuid, nullable)
├── processed_at (timestamptz, not null)
└── correlation_id (uuid, not null)
```

Cuando llega un evento, antes de procesarlo, el consumidor verifica que
`event_id` no exista en `processed`. Si ya existe, lo descarta. Esto hace al
consumidor idempotente sin que el handler tenga que pensar en duplicados.

Índice: `(processed_at)` para limpieza periódica.

## Consecuencias

**Positivas**
- Consistencia transaccional garantizada (la transacción de BD incluye el evento)
- Resiliencia: si Service Bus está caído, los eventos esperan en outbox y se publican cuando se recupere
- Reproducibilidad: el outbox guarda histórico (puede limpiarse después de N días si se desea)
- Idempotencia explícita en el consumidor

**Negativas**
- Latencia entre el commit y la publicación (segundos), no milisegundos. Aceptable para eventos de integración.
- El worker publisher es un componente adicional que mantener
- Outbox crece; necesita política de limpieza (eventos publicados se borran después de 30 días, por ejemplo)
- Complejidad inicial mayor que "publicar directo"

## Descartadas

**MassTransit con Outbox integrado**. Excelente librería pero pesada para
empezar: agrega muchas convenciones, abstracciones, y dependencias. Útil
cuando la mensajería es muy compleja; en nuestro caso preferimos algo más
explícito y entendible al inicio. Posible migración futura si justifica.

**CAP**. Buena librería para .NET, pero menos conocida y con menos comunidad.
Riesgo de mantenibilidad a largo plazo.

**Wolverine**. Muy moderno y elegante, pero todavía joven y con menor
adopción. Riesgo similar a CAP.

**Two-Phase Commit**. PostgreSQL y Service Bus no soportan 2PC nativo y, aún
si lo hicieran, 2PC tiene overhead inaceptable en producción.

**Publicar directo sin outbox**. La inconsistencia ocasional en un sistema
financiero es inaceptable. Esto es lo que provoca el caso clásico de
"facturación dice cobrado y cobranza no se enteró".

## Notas de implementación

**Estructura de tablas**
- Crear tabla `integration_events_outbox` en cada esquema de módulo emisor
- Crear tabla `integration_events_processed` en cada esquema de módulo consumidor
- Un módulo puede ser ambos (emite y consume); tiene ambas tablas

**Convenciones de eventos**
- Cada evento es una clase record en `Backend/src/{Modulo}/Application/IntegrationEvents/`
- Naming: `{Sustantivo}{Verbo}Event` en pasado, ej. `CfdiTimbradoEvent`, `PagoAplicadoEvent`
- Cada evento incluye `IntegrationEventBase` con: `EventId`, `EmpresaId`, `OcurridoEn` (DateTimeOffset UTC), `CorrelationId`
- Versionado: cada evento declara `int Version`; los consumidores manejan múltiples versiones explícitamente cuando un evento evoluciona

**Emisión**
- Las entidades de dominio implementan `IDomainEventEmitter` con método `IReadOnlyList<DomainEvent> DomainEvents { get; }`
- El `BaseDbContext` tiene un override de `SaveChangesAsync` que:
  1. Recolecta `DomainEvents` de las entidades modificadas
  2. Los traduce a `IntegrationEvents` mediante un mapper por módulo (no todos los domain events salen al outbox; algunos son internos al módulo)
  3. Inserta los integration events en la tabla `outbox` del mismo `DbContext`
  4. Llama al `SaveChangesAsync` real (entidades + outbox van en una sola transacción)
- El `empresa_id` del evento se asigna automáticamente desde el contexto del request (ADR-0011)

**Worker publisher**
- Hosted service `OutboxPublisherWorker` por módulo emisor (o uno central que maneja todos los módulos; decisión menor)
- Polling cada 5 segundos: SELECT batch de 100 eventos donde `published_at IS NULL` ordenados por `created_at`
- Por cada evento: serializar a Service Bus, marcar `published_at = now()`
- Retry con backoff exponencial: si falla, incrementa `intentos`, guarda `ultimo_error`, reintenta en el siguiente ciclo (con espera proporcional a `intentos`)
- Si `intentos > 10`: marca como "tóxico", se ignora en publicaciones automáticas, se notifica vía Application Insights y queda visible en una cola de "eventos atascados" que admin puede revisar
- Cada publicación incluye el `correlation_id` como propiedad del mensaje de Service Bus para que el consumidor pueda restaurarlo en su `LogContext` (ADR-0006)

**Consumo**
- Middleware/decorator en cada handler de Service Bus que:
  1. Extrae `event_id` del mensaje
  2. Verifica en `integration_events_processed`; si existe, descarta y ACK
  3. Si no existe, ejecuta el handler
  4. Si el handler tiene éxito: inserta en `processed` y ACK
  5. Si el handler falla: NACK y deja que Service Bus reintente
- Toda la operación (handler + insert en processed) corre en una transacción de BD si el handler modifica estado

**Política de limpieza**
- Hosted service `OutboxCleanupJob` corre cada noche
- Borra de `outbox` los eventos con `published_at IS NOT NULL AND published_at < NOW() - 30 días`
- Borra de `processed` los registros con `processed_at < NOW() - 30 días`
- Los registros tóxicos (intentos > 10, no publicados) NO se borran automáticamente; quedan para revisión manual

**Métricas y monitoreo (en App Insights, ADR-0006)**
- Custom metric: `outbox.publishing_lag_seconds` = `published_at - created_at` por evento; alerta si p95 > 60s
- Custom metric: `outbox.pending_count` = filas con `published_at IS NULL`; alerta si > 100
- Custom metric: `outbox.toxic_count` = filas con `intentos > 10`; alerta si > 0 (un solo evento tóxico amerita revisión)
- Custom metric: `processed.duplicates_dropped_count` = eventos descartados por idempotencia; informativo, no alerta

**Despliegue**
- En la fase inicial, todos los hosted services (`OutboxPublisherWorker`, `OutboxCleanupJob`, etc.) corren dentro del mismo proceso de la API web (App Service único)
- Cuando se escala horizontalmente a N instancias, cada instancia corre los workers en paralelo. Para los workers de polling (Outbox publisher), esto es seguro gracias al `SELECT ... FOR UPDATE SKIP LOCKED` en el batch
- Para jobs nocturnos singleton (cleanup, archival, DOF fetcher), se adquiere un **advisory lock** en PostgreSQL al iniciar la ejecución (`pg_try_advisory_lock`): solo la instancia que obtiene el lock ejecuta el job, las demás duermen
- Si en el futuro algún worker se vuelve pesado (ej. archival procesando GB), se extrae a Container Apps Jobs o Azure Functions con Timer Trigger; el código `IHostedService` permite la extracción mecánica

**Cambios en otras ADRs**
- ADR-0008: la tabla outbox NO está marcada como `IAuditable` (se marca con `INotAudited` para evitar ruido); el publish/consume queda visible en App Insights por correlation_id
- ADR-0011: la columna `empresa_id` del outbox permite filtrar eventos por empresa cuando se requiere
- ADR-0006: el worker publisher propaga `correlation_id` como propiedad de Service Bus para correlación end-to-end

**ADRs hijo posibles**
- Convenciones de versionado de eventos (cuando un evento cambia su schema)
- Estrategia de "saga" o coreografía cuando un flujo de negocio requiere múltiples eventos coordinados
- Migración futura a MassTransit/Wolverine si la mensajería se vuelve compleja
