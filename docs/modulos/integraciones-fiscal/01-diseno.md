# Diseño — Módulo Integraciones Fiscal (`Millet.Integraciones.Fiscal`)

> **Lee primero:** [`00-levantamiento.md`](00-levantamiento.md). Este
> documento traduce las decisiones del levantamiento a estructuras de
> código, schema PG, contratos HTTP y plan de cutover.
>
> **Versión:** 0.1 — Draft inicial, 2026-05-24.

---

## 1. Arquitectura

```
┌──────────────────────────────────────────────────────────────┐
│                Millet.Integraciones.Fiscal                    │
│                                                                │
│  Domain                                                        │
│   - IFiscalApiClient (puerto público)                          │
│   - ConfiguracionPac (agregado)                                │
│   - RfcReceptor (entidad)                                      │
│                                                                │
│  Application                                                   │
│   - GuardarConfiguracionPacCommand + Handler                   │
│   - TestConexionPacCommand + Handler                           │
│   - ListarRfcsReceptoresQuery                                  │
│   - InvalidarCacheConfiguracionPacEvent (in-process)           │
│                                                                │
│  Infrastructure                                                │
│   - IntegracionesFiscalDbContext (schema integraciones_fiscal) │
│   - FiscalApiHttpClient (impl real con Polly)                  │
│   - NoOpFiscalApiClient (cuando no hay config para la empresa) │
│   - ConfiguracionPacResolver (lee + descifra + cachea por      │
│     empresaId con TTL 60s)                                     │
│   - Workers: DescargaMasivaSatWorker, EstadoSatRefreshWorker   │
│   - SecretCipher (envuelve IDataProtector)                     │
└──────────────────────────────────────────────────────────────┘
        ▲                                            │
        │                                            │ HTTP
        │ IFiscalApiClient (puerto)                  │
        │                                            ▼
┌───────┴──────────┐                          ┌────────────┐
│ Millet.CxP       │                          │ FiscalAPI  │
│ (descarga CFDIs) │                          │ (PAC)      │
└──────────────────┘                          └────────────┘
        ▲
        │ Same puerto
        │
┌───────┴──────────┐
│ Millet.Facturación│ (futuro: timbrado)
└──────────────────┘
```

### Project references

```
Integraciones.Fiscal → SharedKernel, Identidad (para ICurrentEmpresaContext)
CuentasPorPagar      → Integraciones.Fiscal (consume el puerto)
Facturación (futuro) → Integraciones.Fiscal
Api                  → Integraciones.Fiscal (wiring DI + endpoints)
```

> **Sin ciclos.** `Integraciones.Fiscal` no referencia CxP ni Facturación
> — solo expone el puerto.

---

## 2. Schema Postgres

Schema dedicado `integraciones_fiscal`. Toda tabla con `empresa_id` →
multi-tenant per ADR-0030.

### `configuracion_pac`

```sql
CREATE TABLE integraciones_fiscal.configuracion_pac (
  id                            uuid PRIMARY KEY,
  empresa_id                    uuid NOT NULL,
  proveedor                     smallint NOT NULL,    -- enum: 1=FiscalApi (extensible)
  base_url                      text NOT NULL,
  api_key_cifrado               bytea NOT NULL,       -- ciphertext de DataProtection
  api_key_hash                  text NOT NULL,        -- SHA256(plaintext) para detectar cambios sin descifrar
  timeout_descarga_segundos     int  NOT NULL DEFAULT 60,
  timeout_consulta_segundos     int  NOT NULL DEFAULT 10,
  retry_count                   int  NOT NULL DEFAULT 3,
  circuit_breaker_failures      int  NOT NULL DEFAULT 5,
  circuit_breaker_duration_seg  int  NOT NULL DEFAULT 60,
  descarga_intervalo_segundos   int  NOT NULL DEFAULT 21600,  -- 6h
  descarga_backfill_horas       int  NOT NULL DEFAULT 24,
  refresh_intervalo_segundos    int  NOT NULL DEFAULT 21600,
  refresh_batch_size            int  NOT NULL DEFAULT 100,
  activo                        boolean NOT NULL DEFAULT true,
  ultima_rotacion_at            timestamptz,
  ultima_test_conexion_at       timestamptz,
  ultima_test_conexion_exitosa  boolean,
  -- BaseEntity audit
  version                       int  NOT NULL DEFAULT 1,
  created_at                    timestamptz NOT NULL,
  updated_at                    timestamptz NOT NULL,
  created_by                    text,
  updated_by                    text,
  deleted_at                    timestamptz,
  CONSTRAINT uq_configuracion_pac_empresa_proveedor
    UNIQUE (empresa_id, proveedor)
);

CREATE INDEX ix_configuracion_pac_activo
  ON integraciones_fiscal.configuracion_pac (activo)
  WHERE deleted_at IS NULL;
```

### `rfcs_receptores`

```sql
CREATE TABLE integraciones_fiscal.rfcs_receptores (
  id                          uuid PRIMARY KEY,
  empresa_id                  uuid NOT NULL,
  rfc                         varchar(13) NOT NULL,
  descarga_habilitada         boolean NOT NULL DEFAULT true,
  refresh_habilitada          boolean NOT NULL DEFAULT true,
  checkpoint_descarga_at      timestamptz,
  version                     int  NOT NULL DEFAULT 1,
  created_at                  timestamptz NOT NULL,
  updated_at                  timestamptz NOT NULL,
  created_by                  text,
  updated_by                  text,
  deleted_at                  timestamptz,
  CONSTRAINT uq_rfcs_receptores_empresa_rfc
    UNIQUE (empresa_id, rfc),
  CONSTRAINT ck_rfcs_receptores_rfc_longitud
    CHECK (char_length(rfc) BETWEEN 12 AND 13)
);

CREATE INDEX ix_rfcs_receptores_empresa
  ON integraciones_fiscal.rfcs_receptores (empresa_id)
  WHERE deleted_at IS NULL;
```

> El **checkpoint** vive aquí (no en CxP) — es estado de la integración,
> no del CFDI. Hoy en CxP existe la tabla
> `checkpoints_descarga_sat`; en el cutover se migra a este schema.

### Auditoría

Se agrega evento `IntegracionesFiscalConfiguracionActualizadaEvent` al
Outbox para que Identidad/Auditoría lo loggee (cuando exista módulo de
auditoría). Por ahora es solo Outbox + log estructurado Serilog.

---

## 3. Cifrado de secretos — ADR-0037 (nuevo)

### Decisión

ASP.NET DataProtection con DEK que vive en **Azure Key Vault**, ring
de keys en **Azure Blob Storage**.

### Setup en `Program.cs`

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(
        connectionString: builder.Configuration["DataProtection:BlobConnString"],
        containerName: "dataprotection-keys",
        blobName: "millet-erp.xml")
    .ProtectKeysWithAzureKeyVault(
        new Uri(builder.Configuration["DataProtection:KeyIdentifier"]!),
        new DefaultAzureCredential())
    .SetApplicationName("Millet.ERP")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));
```

### Dev local

Sin `KeyIdentifier` → DataProtection cae a `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`
automáticamente. Cero fricción para desarrolladores.

### `SecretCipher` helper

```csharp
public sealed class SecretCipher
{
    private readonly IDataProtector _protector;
    public SecretCipher(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Integraciones.Fiscal.ApiKey.v1");

    public byte[] Encrypt(string plaintext)
        => _protector.Protect(Encoding.UTF8.GetBytes(plaintext));

    public string Decrypt(byte[] ciphertext)
        => Encoding.UTF8.GetString(_protector.Unprotect(ciphertext));

    public static string HashForChangeDetection(string plaintext)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
}
```

> El **purpose string** `Integraciones.Fiscal.ApiKey.v1` es importante:
> cambios accidentales rompen descifrado. Bump a `v2` si cambia el
> formato.

### Detección de cambio sin descifrar

El admin UI muestra `••••` siempre. Al guardar, si el `api_key_hash`
nuevo == al actual → no se re-cifra (idempotente). Si difiere → se
re-cifra y se actualiza `ultima_rotacion_at`.

### Bicep — recursos nuevos

```bicep
// infra/modules/keyvault.bicep — agregar:
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2024-04-01-preview' = {
  parent: keyVault
  name: 'dataprotection-master-key'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: ['wrapKey', 'unwrapKey']
  }
}

// infra/modules/storage.bicep — agregar container:
resource dpKeysContainer '...containers' = {
  parent: blobService
  name: 'dataprotection-keys'
  properties: { publicAccess: 'None' }
}
```

App settings que entran al App Service:
- `DataProtection__BlobConnString` → KV ref a storage connection string
- `DataProtection__KeyIdentifier` → URI del key versioned

---

## 4. Puerto público `IFiscalApiClient`

### Resolución por empresa

El cliente HTTP **no es singleton** con config estática como hoy. Es
**scoped** y resuelve la config por la empresa actual:

```csharp
public sealed class FiscalApiHttpClient : IFiscalApiClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguracionPacResolver _resolver;
    private readonly ICurrentEmpresaContext _empresa;

    public async Task<DescargaResult> DescargarAsync(string rfcReceptor, ...)
    {
        var config = await _resolver.ResolverAsync(_empresa.EmpresaId, cancellationToken);
        if (config is null) return DescargaResult.NoConfigurado;

        var http = _httpFactory.CreateClient(FiscalApiHttpClient.HttpClientName);
        http.BaseAddress = new Uri(config.BaseUrl);
        http.DefaultRequestHeaders.Authorization = new("X-Api-Key", config.ApiKey);
        // ... resto del request
    }
}
```

### Cache del resolver

`ConfiguracionPacResolver` cachea por `empresaId` con TTL 60s en
`IMemoryCache`. El evento
`IntegracionesFiscalConfiguracionActualizadaEvent` invalida la entry
correspondiente cuando el admin guarda.

### En workers (sin HTTP context)

Los workers iteran todas las `configuracion_pac` activas, levantan un
scope DI por empresa, y bypassean `ICurrentEmpresaContext` para
inyectarle la `EmpresaId` de la config. Mismo patrón que
`ComprasEventListenerWorker` hoy.

---

## 5. Endpoints admin

Base path: `/api/v1/integraciones/fiscal`. Permiso requerido:
`integraciones.fiscal.administrar` (excepto los GETs que requieren
`integraciones.fiscal.leer`).

### `GET /configuracion/{empresaId}`

```json
{
  "id": "...",
  "empresaId": "...",
  "proveedor": "FiscalApi",
  "baseUrl": "https://api.fiscalapi.com",
  "apiKey": "••••",  // mask siempre
  "apiKeyConfigured": true,
  "timeoutDescargaSegundos": 60,
  "timeoutConsultaSegundos": 10,
  "retryCount": 3,
  "circuitBreakerFailures": 5,
  "circuitBreakerDuracionSeg": 60,
  "descargaIntervaloSegundos": 21600,
  "descargaBackfillHoras": 24,
  "refreshIntervaloSegundos": 21600,
  "refreshBatchSize": 100,
  "activo": true,
  "ultimaRotacionAt": "2026-05-24T12:00:00Z",
  "ultimaTestConexionAt": "2026-05-24T12:01:33Z",
  "ultimaTestConexionExitosa": true
}
```

### `PUT /configuracion/{empresaId}`

- Body acepta `apiKey: null` → no cambia la actual. `apiKey: "..."` → rota.
- ETag obligatorio (ADR-0012).
- Idempotency-Key obligatorio.

### `POST /configuracion/{empresaId}/test`

- Body: `{ "apiKey": "..." }` (transitorio, no se persiste).
- Backend invoca `GET /health` del PAC con esa key.
- Response: `{ "exitosa": true/false, "mensaje": "..." }`.
- Rate limit: 10/min por usuario (revisar contrato FiscalAPI antes de
  exponer).

### `GET/POST/DELETE /rfcs-receptores`

CRUD estándar. Validación de RFC mexicano (12 o 13 chars, regex SAT).

---

## 6. Cutover desde CxP — sin romper producción

### Estrategia: shim + feature flag

**Fase A (PR de creación):** se crea el módulo nuevo, las tablas, el
puerto, el resolver, los endpoints admin. **NO se mueve nada de CxP
todavía.** CxP sigue funcionando idéntico.

**Fase B (PR de shim):** se agrega al módulo nuevo un adapter shim que
implementa `IFiscalApiClient` leyendo `IOptionsMonitor<FiscalApiOptions>`
de CxP. Feature flag `Integraciones.Fiscal.Enabled` controla cuál se
usa. Default `false` → comportamiento idéntico al actual.

**Fase C (PR de cutover):** se mueven los workers + el cliente real al
módulo nuevo, leyendo de `configuracion_pac`. Se migra la config actual
de `appsettings.json` a una fila de seed. Feature flag pasa a `true` en
dev primero, luego qa, luego prod.

**Fase D (PR de cleanup):** se borran las clases obsoletas de CxP
(`FiscalApiHttpClient`, `FiscalApiOptions`, workers viejos,
`WorkerSchedulingOptions.RfcsReceptores`).

### Migration plan PG

- `M001`: crea schema `integraciones_fiscal` + tablas.
- `M002`: data-migration que copia
  `cuentas_por_pagar.checkpoints_descarga_sat` → `rfcs_receptores`
  (durante Fase C).
- `M003`: opcional — `DROP TABLE cuentas_por_pagar.checkpoints_descarga_sat`
  (durante Fase D, ventana de hotfix de 1 release por si rollback).

---

## 7. Frontend admin UI

### Página `/admin/integraciones/fiscal`

Master-detail estándar (patrón ERP):

- **Lista** de empresas con su estado: empresa, proveedor, activo
  (badge), última conexión exitosa (timestamp).
- **Detalle** (panel derecho): formulario en 4 secciones colapsables:
  1. **Conexión** — BaseUrl, ApiKey (`<input type="password">` +
     placeholder `••••` si ya configurada), botón "Rotar".
  2. **Schedule** — intervalos de descarga + refresh + backfill.
  3. **Retry/Circuit Breaker** — defaults expuestos como sliders/inputs.
  4. **RFCs receptores** — tabla inline (agregar / desactivar).
- **Botón "Test conexión"** al lado del campo ApiKey. Solo habilitado
  si hay key sin guardar (rotación) o si el usuario marca "verificar
  actual". Muestra resultado en `<Toast>`.
- **Botón "Guardar"** abajo del panel. Deshabilitado hasta que test
  exitoso o usuario marca "guardar sin probar" (warning).

### Hook API

```ts
// frontend/src/features/integraciones-fiscal/api/useConfiguracionFiscal.ts
export function useConfiguracionFiscal(empresaId: string) { ... }
export function useActualizarConfiguracionFiscal() { ... }
export function useTestConexionFiscal() { ... }
export function useRfcsReceptores(empresaId: string) { ... }
```

### Permisos en `permission-codes.ts`

```ts
IntegracionesFiscalLeer: 'integraciones.fiscal.leer',
IntegracionesFiscalAdministrar: 'integraciones.fiscal.administrar',
```

---

## 8. Plan de PRs estimado

| # | Branch | Scope | Tamaño |
|---|---|---|---|
| 1 | `docs/integraciones-fiscal-mailbox` | Este documento + 00-levantamiento (ambos módulos) | XS — solo docs |
| 2 | `feature/integraciones-fiscal-foundation` | Proyecto nuevo + DbContext + tablas + migrations + permisos canónicos | M |
| 3 | `feature/integraciones-fiscal-data-protection` | ADR-0037 + wiring DataProtection + Bicep KV/Storage | S |
| 4 | `feature/integraciones-fiscal-endpoints` | Endpoints admin (GET/PUT/test) + handlers + tests | M |
| 5 | `feature/integraciones-fiscal-cliente` | `FiscalApiHttpClient` real + resolver + cache | M |
| 6 | `feature/integraciones-fiscal-workers` | Mover workers + iteración por empresa | M |
| 7 | `feature/integraciones-fiscal-cutover` | Feature flag + shim + cutover en dev | S |
| 8 | `feature/integraciones-fiscal-frontend` | Admin UI + hooks + sheet de RFCs | M |
| 9 | `chore/integraciones-fiscal-cleanup` | Borrar código viejo de CxP tras estabilización | S |

Total estimado: ~6-7 semanas calendario si se trabaja en serie. PRs 2-5
son paralelizables parcialmente.

---

## 9. ADRs nuevos

- **ADR-0037** — DataProtection con DEK en Key Vault para credenciales
  operativas. Justifica la elección sobre pgcrypto y KV-per-secret.

---

## 10. Pendientes que requieren confirmación

1. ¿Se cobra al PAC por `GET /health` (test connection)? Validar contrato
   antes de exponer el botón.
2. ¿`circuit_breaker_*` y `retry_*` se exponen al admin o quedan ocultos
   con defaults sensatos? (recomendación: exponer en sección colapsable
   "Avanzado").
3. ¿Notificaciones cuando una `configuracion_pac` falla N veces
   consecutivas? Requiere módulo Notificaciones (PLATFORM-TODO).
4. ¿"Auditoría" de cambios en config es necesaria en fase 1, o se
   posterga al módulo de Auditoría general?
5. Migration de los `RfcsReceptores` actuales (vacíos en dev) — basta
   con seed `[]` por empresa.
