# ADR-0020: Idempotencia HTTP con header `Idempotency-Key`

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: api, integridad, fiscal, tier-2

## Contexto y problema

Los clientes HTTP pueden generar requests duplicados sin querer:

- Doble click en un botón "Timbrar" antes de que la primera request termine
- Conexión inestable: el cliente hace timeout esperando respuesta y reintenta, pero el primer request sí llegó al servidor y se procesó
- Bugs en código del frontend que reintenta agresivamente
- Refresh de la página durante una operación

Sin protección, cada uno de estos escenarios genera operaciones duplicadas.
En un ERP financiero esto es inaceptable: un CFDI timbrado dos veces, un
pago aplicado dos veces, un asiento contable duplicado, son problemas que
implican corrección manual, posiblemente con reportes al SAT.

Necesitamos un mecanismo estándar, predecible, y testeable para garantizar
que operaciones de escritura sean **idempotentes a nivel HTTP**: el mismo
request enviado N veces produce el mismo efecto que enviarlo una sola vez.

## Drivers de la decisión

- Estándar conocido (no inventar formato propio): librerías y herramientas ya entienden el patrón
- Aplicable a operaciones de escritura sin importar el endpoint específico
- Detectable por el frontend automáticamente (sin que cada componente tenga que pensar en duplicados)
- Aislamiento por usuario y empresa (las keys de un usuario no colisionan con las de otro)
- Performance razonable: el overhead por request idempotente debe ser mínimo
- Compatible con `ProblemDetails` (ADR-0010) para errores de violación

## Opciones consideradas

1. Header `Idempotency-Key` con tabla persistente y body hashing (siguiendo IETF draft + Stripe)
2. Idempotencia "natural" basada en business keys (ej. `referencia_cliente` único por cliente para CFDIs)
3. No idempotencia HTTP, confiar en validaciones de negocio (ej. detectar CFDIs duplicados por monto+fecha+cliente)
4. Bloqueo distribuido (Redis) por endpoint+usuario

## Decisión

Se adopta la **opción 1**: header `Idempotency-Key` con tabla persistente
en PostgreSQL, validación de body hash, y middleware genérico que aplica a
endpoints decorados.

### Mecánica

**Cliente envía**:

```
POST /api/fiscal/cfdis/12345/timbrar
Idempotency-Key: 5e8c4d2a-7f3b-4a1c-9d8e-2b6c5a4d3e9f
Content-Type: application/json

{ "fechaEmision": "2026-05-02T20:30:00Z", ... }
```

**El UUID** lo genera el cliente (UUID v4). El backend NO lo genera.

**Backend procesa**:

```
1. ¿El header está presente?
   - No, y el endpoint NO requiere idempotencia → continúa normal
   - No, y el endpoint SÍ requiere [RequireIdempotencyKey] → 400 con código MISSING_IDEMPOTENCY_KEY
   - Sí → continúa al paso 2

2. Validar formato (UUID v4 mediante regex)
   - Inválido → 400 con código INVALID_IDEMPOTENCY_KEY

3. Calcular SHA-256 del body del request

4. INSERT INTO core.idempotency_keys
     (key, empresa_id, usuario_id, http_method, path, request_body_hash, status, created_at)
   VALUES
     (:key, :empresaId, :userId, :method, :path, :hash, 'processing', now())
   ON CONFLICT (empresa_id, usuario_id, key) DO NOTHING
   RETURNING xmax = 0 AS inserted

5. Si inserted = true (era nuevo):
   - Continúa con el handler normal
   - Al terminar: UPDATE status='completed', response_status_code, response_body, completed_at
   - Retorna response normal

6. Si inserted = false (ya existía):
   SELECT * FROM core.idempotency_keys WHERE empresa_id = ... AND usuario_id = ... AND key = ...

   Casos:
   a) status = 'processing': otro request idéntico está ejecutándose
      → 409 con código IDEMPOTENCY_IN_PROGRESS, header Retry-After: 5
   b) status IN ('completed', 'failed') y request_body_hash coincide:
      → retorna response cacheada (SIN re-ejecutar el handler)
   c) status IN ('completed', 'failed') y request_body_hash difiere:
      → 422 con código IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY
```

### Tabla `core.idempotency_keys`

```
idempotency_keys
├── key (text, not null)
├── empresa_id (uuid, FK compartido.empresas, not null)
├── usuario_id (uuid, FK identidad.usuarios, not null)
├── http_method (text, not null)              -- POST, PUT, PATCH, DELETE
├── path (text, not null)                     -- /api/fiscal/cfdis/.../timbrar
├── request_body_hash (text, not null)        -- SHA-256 hex del body
├── status (text, not null)                   -- 'processing' | 'completed' | 'failed'
├── response_status_code (int, nullable)      -- el HTTP status que se devolvió
├── response_body (jsonb, nullable)           -- la respuesta cacheada (truncada si > 1 MB)
├── response_body_truncated (bool, default false)
├── correlation_id (uuid, not null)           -- TraceId del request original (ADR-0006)
├── created_at (timestamptz, not null)
└── completed_at (timestamptz, nullable)

PRIMARY KEY (empresa_id, usuario_id, key)
INDEX (status, created_at)                    -- para el job de limpieza
INDEX (created_at)                            -- para particionado futuro si crece
```

**Aislamiento por usuario**: la PK es `(empresa_id, usuario_id, key)`, no
solo `key`. Esto significa que dos usuarios distintos pueden usar el mismo
UUID sin colisión. Razón: evita que un atacante intente "robar" respuestas
generando keys aleatorias y golpeando endpoints.

### Endpoints que exigen idempotencia

**Obligatoria** (con `[RequireIdempotencyKey]`):

- `POST /api/fiscal/cfdis/{id}/timbrar`
- `POST /api/fiscal/cfdis/{id}/cancelar`
- `POST /api/financiero/pagos`
- `POST /api/financiero/pagos/{id}/aplicar`
- `POST /api/comercial/notas-credito`
- `PATCH /api/comercial/notas-credito/{id}/cancelar`
- `POST /api/contabilidad/polizas/{id}/postear`
- `POST /api/compras/ordenes-compra`
- `POST /api/compras/recepciones`
- `POST /api/almacen/salidas`
- Cualquier operación que genere asientos contables o tenga impacto fiscal

**Opcional** (el middleware la procesa si está presente):

- Cualquier otro POST/PUT/PATCH/DELETE
- Recomendado en operaciones que crean entidades importantes (clientes, proveedores) para protección extra

**No aplica**:

- GET y HEAD (idempotentes por definición HTTP)
- Endpoints de salud, login, refresh de token

### Decoración en código

```csharp
[HttpPost("{id}/timbrar")]
[RequireIdempotencyKey]
[ProducesResponseType<CfdiTimbradoDto>(200)]
[ProducesResponseType<ProblemDetails>(400)]  // missing or invalid key
[ProducesResponseType<ProblemDetails>(409)]  // in progress
[ProducesResponseType<ProblemDetails>(422)]  // key reused with different body
public async Task<IActionResult> Timbrar(Guid id, ...)
{
    // ...
}
```

El atributo `[RequireIdempotencyKey]` es marker. La lógica vive en
`IdempotencyMiddleware` que se ejecuta antes de los handlers.

### Validaciones específicas

**Formato del `Idempotency-Key`**:
- Debe ser UUID v4 (regex: `^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$`)
- Si no cumple: 400 con código `INVALID_IDEMPOTENCY_KEY`

**Tamaño del body cacheable**:
- Límite: 1 MB de respuesta serializada
- Si la respuesta es mayor (ej. reporte exportado), se ejecuta normalmente pero NO se cachea la respuesta. Se marca `response_body_truncated = true`
- Si llega un retry de la misma key, se rechaza con 409 + código `IDEMPOTENCY_RESPONSE_TOO_LARGE` (no podemos garantizar mismo resultado sin re-ejecutar)
- En la práctica, las operaciones críticas (timbrar, aplicar pago) tienen respuestas pequeñas (<10KB)

**Status `processing`**:
- Si dos requests con la misma key llegan casi en paralelo, el segundo recibe 409 con `Retry-After: 5`
- La intención es que el cliente no spamee; espera 5s y reintenta. Si el primer request ya terminó, el reintento recibirá la respuesta cacheada

### Política de retención

| Estado de la key | Retención        |
|------------------|------------------|
| `processing`     | 1 hora           |
| `completed`      | 24 horas         |
| `failed`         | 24 horas         |

**Razón de los plazos**:
- 24h en `completed`/`failed`: cubre el caso del usuario que cierra el navegador y vuelve al día siguiente con un retry. Después es muy improbable
- 1h en `processing`: si un request tarda más de 1h, algo grave pasa; asumimos que el cliente ya se rindió

**Job de limpieza**:
- `IdempotencyKeysCleanupJob` corre cada hora
- Sigue el patrón de advisory lock (ADR-0009) para correr solo en una instancia
- Borra keys según política de retención

### Frontend

**Generación de keys**:
- El cliente HTTP genera la key automáticamente para operaciones de escritura
- Implementación: `crypto.randomUUID()` (estándar en navegadores modernos, retorna v4)
- Cada operación lógica del usuario tiene UNA key:
  - Si el usuario llena un formulario y hace clic "Guardar": una key
  - Si el clic produce un error retryable y el usuario hace clic de nuevo en el MISMO formulario abierto: la MISMA key (reusa)
  - Si el usuario abre un formulario nuevo: nueva key

**Implementación en `api-client.ts`**:

```typescript
// Operaciones de escritura agregan key automáticamente
async function postWithIdempotency<T>(
  url: string,
  body: unknown,
  formId: string  // identificador único del formulario abierto
): Promise<T> {
  const key = getOrCreateKey(formId);
  return api.request<T>("POST", url, body, {
    headers: { "Idempotency-Key": key }
  });
}

// formId puede ser un useId() de React; persiste durante la vida del componente
```

**Manejo de respuestas idempotency-related**:

- `200/201` con respuesta cacheada: indistinguible de respuesta normal — el frontend la procesa igual
- `409 IDEMPOTENCY_IN_PROGRESS`: el cliente espera el `Retry-After` y reintenta automáticamente (transparente para el usuario)
- `422 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY`: indica bug en el cliente (no debería pasar). Se loguea en consola, se muestra error genérico al usuario
- `400 MISSING_IDEMPOTENCY_KEY`: bug en el cliente HTTP (algún componente no usa el wrapper). No debería llegar al usuario; se loguea con stack trace

### Observabilidad

**Métricas custom (App Insights, ADR-0006)**:

- `idempotency.cache_hits` por endpoint: cuántos requests fueron servidos desde cache
- `idempotency.in_progress_conflicts` por endpoint: cuántos requests recibieron 409
- `idempotency.body_mismatch_conflicts` (alerta si > 0 sostenido): sugiere bug en cliente que reusa keys con diferentes payloads
- `idempotency.processing_time_p99`: tiempo desde `created_at` hasta `completed_at`

**Logs estructurados**:
- Cada request idempotente loguea `idempotency_key` como propiedad
- Permite correlacionar el request original con sus retries en App Insights

### Cambios en otras ADRs

- **ADR-0010**: agregar a la tabla de mapeo de excepciones:
  - `IdempotencyKeyMissingException` → 400, código `MISSING_IDEMPOTENCY_KEY`
  - `IdempotencyKeyInvalidException` → 400, código `INVALID_IDEMPOTENCY_KEY`
  - `IdempotencyInProgressException` → 409, código `IDEMPOTENCY_IN_PROGRESS`
  - `IdempotencyBodyMismatchException` → 422, código `IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY`

- **ADR-0009**: la tabla `core.idempotency_keys` se agrega al esquema `core` (junto con `audit_log` y `locks_duros`); el job de limpieza usa el mismo patrón de advisory lock

## Consecuencias

**Positivas**
- Doble click y retries automáticos del cliente NO generan duplicados en operaciones críticas
- Estándar conocido: alineado con Stripe API, IETF draft, easy a entender para devs nuevos
- Body hashing detecta bugs del cliente (reuso de key con payload distinto)
- Aislamiento por usuario impide ataques cross-user
- Aplicable a cualquier endpoint con un atributo: bajo costo marginal por endpoint
- Frontend lo maneja transparentemente; los componentes no piensan en duplicados

**Negativas**
- Tabla `core.idempotency_keys` crece con cada request idempotente: con 100 ops/min y 24h de retención, ~150K filas live. Aceptable; el job de limpieza la mantiene controlada
- Cada request idempotente hace un INSERT extra: ~1-2ms de overhead. Despreciable en operaciones que toman 100ms+
- Body hashing requiere leer todo el body antes de procesarlo: para bodies grandes, slight overhead. Mitigable con cap de 10MB
- Disciplina obligatoria: cada endpoint nuevo crítico debe ser decorado. Mitigado por test/linter que verifica que endpoints listados en una "lista de obligatorios" tengan `[RequireIdempotencyKey]`

## Descartadas

**Idempotencia "natural" por business keys**. Funciona para algunos casos
("no puedo crear un cliente con RFC duplicado") pero no cubre todos. Una
"timbrado de CFDI" no tiene business key natural antes de timbrar (el folio
SAT lo asigna el PAC). Forzar a inventar business keys para todo es frágil
y específico por endpoint.

**Sin idempotencia, validaciones de negocio**. Tentador pero insuficiente.
Detectar duplicados con queries tipo "¿hay un CFDI con el mismo cliente,
fecha y monto en los últimos 5 minutos?" tiene falsos positivos
(legitimamente puede haber dos CFDIs así) y falsos negativos (un retry
después de 6 minutos pasa). Y obliga a cada handler a pensar en eso.

**Bloqueo distribuido (Redis) por endpoint+usuario**. Resuelve el problema
del doble click pero no de los retries con timeout. Una vez liberado el
lock, un retry retrasado vuelve a procesar. Y agrega Redis como
dependencia para algo que PostgreSQL puede resolver con `INSERT ON
CONFLICT`.

## Notas de implementación

**Backend**

- Crear tabla `core.idempotency_keys` en la primera migración del esquema `core`
- Implementar `IdempotencyMiddleware` que:
  - Detecta header en POST/PUT/PATCH/DELETE
  - Si endpoint tiene `[RequireIdempotencyKey]` y el header está ausente, retorna 400
  - Si header presente: ejecuta el flujo descrito (INSERT ON CONFLICT, body hashing, cache de response)
- Implementar atributo `[RequireIdempotencyKey]` (marker, no lógica)
- Excepciones `IdempotencyKeyMissingException`, `IdempotencyKeyInvalidException`, `IdempotencyInProgressException`, `IdempotencyBodyMismatchException` mapeadas en el middleware de Problem Details
- `IdempotencyKeysCleanupJob` como hosted service con advisory lock
- El response cacheado se serializa a JSON para guardar en jsonb; si la respuesta es streaming/binary, no se cachea (marca `response_body_truncated = true`)

**Frontend**

- En `api-client.ts`, función `postWithIdempotency`, `putWithIdempotency`, etc.
- Hook `useFormIdempotencyKey()` que retorna una key estable para la vida del formulario
- Manejo automático de 409 IDEMPOTENCY_IN_PROGRESS con respeto a `Retry-After`
- Tipos de error específicos para `MISSING_IDEMPOTENCY_KEY` y `BODY_MISMATCH` (que indican bugs)

**Tests**

- Test de integración: dos POSTs con misma key → segundo retorna response cacheada sin ejecutar handler de nuevo
- Test: dos POSTs concurrentes con misma key → uno completa, otro recibe 409
- Test: misma key con body distinto → 422
- Test: endpoint con `[RequireIdempotencyKey]` sin header → 400
- Test: cleanup job borra keys según política de retención
- Test de carga: cuántas idempotency keys/seg puede manejar el sistema sin degradar (target: > 1000/seg)

**Documentación en `CLAUDE.md`**

- Cuándo decorar un endpoint con `[RequireIdempotencyKey]`
- Cómo el frontend genera las keys (no manualmente; el cliente HTTP lo hace)
- Qué hacer cuando aparece un error 422 BODY_MISMATCH (probable bug del cliente)
- Cómo testear endpoints idempotentes

**ADRs hijo posibles**

- Política específica si el volumen crece y la tabla se vuelve cuello de botella (sharding por empresa, particionado por fecha)
- Estrategia de idempotencia para webhooks entrantes (cuando aplique)
- Idempotencia entre el frontend y SignalR (si surge necesidad)
