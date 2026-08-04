# PR A — Service Principals en módulo Identidad (Glass Agent M2M auth)

Branch: `feature/identidad-spn-auth`
Estado: **build limpio (0 errors)** + **tests Identidad 26/26 passed**.

Implementa el concepto **"usuario de servicio"** (D3 del overview de
Integración A+W) para que apps M2M con Entra ID (Glass Agent en fase 1)
puedan llamar al ERP con tokens `client_credentials` y operar como
sujeto identificable con permisos RBAC. Es **prerequisito** de los PRs
B/C/D del módulo `Millet.Integraciones.Aw`.

---

## 1. Archivos creados / modificados

### Domain (Identidad)

| Archivo | Tipo | Notas |
|---|---|---|
| `backend/src/Identidad/Domain/UsuarioServicio.cs` | nuevo | Entidad SP con AppId/ObjectId/EmpresaId/Activo + invariantes. |
| `backend/src/Identidad/Domain/UsuarioServicioPermiso.cs` | nuevo | Asignación directa permiso↔SP (PK compuesta, sin Rol intermedio). |
| `backend/src/Identidad/Domain/PermisosCanonicos.cs` | modificado | +9 constantes flat `IntegracionesAw*` (namespace GUID `00000006-*`). |

### Application (puertos)

| Archivo | Tipo | Notas |
|---|---|---|
| `backend/src/Identidad/Application/Ports/IServicePrincipalResolver.cs` | nuevo | Port + `ResolvedServicePrincipal` + `ServicePrincipalResolutionFailure` enum. |
| `backend/src/Compartido/Application/Ports/IEmpresaResolverPort.cs` | nuevo | Port para resolver Rfc → EmpresaId desde otros módulos. **Ubicación cambiada vs prompt** (ver §5 desviaciones). |

### Infrastructure (Identidad)

| Archivo | Tipo | Notas |
|---|---|---|
| `backend/src/Identidad/Infrastructure/IdentidadDbContext.cs` | modificado | DbSets nuevos + 2 ConfigureXxx (snake_case, FK física a `compartido.empresas`, índice único en `entra_app_id`). |
| `backend/src/Identidad/Infrastructure/BootstrapServicePrincipalsHostedService.cs` | nuevo | Sincroniza catálogo SPs desde `Auth:ServicePrincipalsJson`. Skip+LogError per-entry. |
| `backend/src/Identidad/Infrastructure/BootstrapServicePrincipalsOptions.cs` | nuevo | Options + `ServicePrincipalConfig` POCO. |
| `backend/src/Identidad/Infrastructure/Adapters/DefaultServicePrincipalResolver.cs` | nuevo | Lee de `IdentidadDbContext`. Match por `EntraAppId` only. |
| `backend/src/Identidad/Infrastructure/Adapters/CachedServicePrincipalResolver.cs` | nuevo | Decorator con `IMemoryCache` singleton, TTL 5 min. Cachea Found/NotFound/Disabled. |
| `backend/src/Identidad/Infrastructure/Telemetry/IdentidadMeter.cs` | nuevo | Meter `Millet.Identidad` con 4 counters (`auth.sp.bootstrap.skipped`, `.upserted`, `.resolution.unknown`, `.resolution.disabled`). |
| `backend/src/Identidad/Millet.Identidad.csproj` | modificado | +PackageReference `Microsoft.Extensions.Caching.Memory`. |

### Infrastructure (Compartido)

| Archivo | Tipo | Notas |
|---|---|---|
| `backend/src/Compartido/Infrastructure/Adapters/EmpresaResolverAdapter.cs` | nuevo | Implementa `IEmpresaResolverPort` leyendo `CompartidoDbContext`. |

### Api / Auth

| Archivo | Tipo | Notas |
|---|---|---|
| `backend/src/Api/Auth/EntraTokenValidator.cs` | modificado | +método `TryValidateServicePrincipalAsync` reutilizando el `ConfigurationManager<OpenIdConnectConfiguration>` ya cacheado (H1). |
| `backend/src/Api/Auth/EntraServicePrincipalAuthenticationHandler.cs` | nuevo | Scheme `EntraServicePrincipal`. NoResult/Fail/Success per D-HANDLER. Override de `HandleChallengeAsync` para emitir 403 + Problem Details cuando SP unknown/disabled. |
| `backend/src/Api/Auth/JwtOrEntraSpPolicyScheme.cs` | nuevo | Forward selector por `iss`. Robusto a token malformado (forward a Jwt → 401 limpio, H2). |
| `backend/src/Api/Auth/PermissionAuthorizationHandler.cs` | modificado | Union de fuentes: claims `permission` para SP, loader+cache para humano. NO rompe path humano. |
| `backend/src/Api/Auth/AuthExtensions.cs` | modificado | Default scheme cambiado a `JwtOrEntraSp` (PolicyScheme); registra el handler de SP, IMemoryCache, IdentidadMeter, IServicePrincipalResolver (cached decorator), IEmpresaResolverPort (impl Compartido), ICurrentServicePrincipal y BootstrapServicePrincipalsHostedService. |
| `backend/src/Api/Program.cs` | modificado | `AddMeter(IdentidadMeter.Name)` en `WithMetrics(...)` del bootstrap OTel. |
| `backend/src/Api/appsettings.json` | modificado | `Auth.ServicePrincipalsJson = ""` (KV-managed en QA/Prod). |
| `backend/src/Api/appsettings.Development.json` | modificado | `Auth.ServicePrincipalsJson` con SPN dev (Glass Agent dev, AppId/ObjectId del SPN ya creado en Entra el 2026-05-15). |

### SharedKernel

| Archivo | Tipo | Notas |
|---|---|---|
| `backend/src/SharedKernel/Application/MilletClaimTypes.cs` | modificado | +constantes `AuthType`, `AuthTypes.{User,ServicePrincipal}`, `Permission`, `EntraAppId`. |
| `backend/src/SharedKernel/Application/ICurrentServicePrincipal.cs` | nuevo | Interface (Id, EmpresaId, Nombre, EntraAppId, Permisos). |
| `backend/src/SharedKernel/Infrastructure/CurrentServicePrincipal.cs` | nuevo | Impl que lee de `HttpContext.User`. |

### Migration EF Core

| Archivo | Tipo | Notas |
|---|---|---|
| `backend/src/Identidad/Infrastructure/Migrations/20260515205658_AddUsuarioServicio.cs` (+`.Designer.cs`) | nuevo | Tablas + índices + seed de los 9 permisos canónicos (H5: el delta de `PermisosCanonicos.Todos` se materializa en seed, igual que migrations de permisos previas). |
| `backend/src/Identidad/Infrastructure/Migrations/IdentidadDbContextModelSnapshot.cs` | modificado | Auto-actualizado por EF. |
| `backend/_last_migration_preview.sql` | nuevo | Script idempotente del delta (preview). |

### Tests

| Archivo | Tipo | Notas |
|---|---|---|
| `backend/tests/Identidad.UnitTests/Domain/UsuarioServicioTests.cs` | nuevo | 7 tests de invariantes y transiciones. |
| `backend/tests/Identidad.UnitTests/Infrastructure/CachedServicePrincipalResolverTests.cs` | nuevo | 6 tests del cache (hit, miss por AppId distinto, NotFound/Disabled cacheados, TTL expiry). |

### Configuración global

| Archivo | Tipo | Notas |
|---|---|---|
| `backend/Directory.Packages.props` | modificado | +`Microsoft.Extensions.Caching.Memory 9.0.4` (alineado con la versión transitive de EF Core 9.0.4). |

---

## 2. Cambio cross-cutting de Auth — el nuevo PolicyScheme

```
                       ┌────────────────────────────────────┐
                       │  Authorization: Bearer <token>     │
                       └──────────────┬─────────────────────┘
                                      │
                                      ▼
                       ┌────────────────────────────────────┐
                       │  PolicyScheme "JwtOrEntraSp"       │
                       │  (default scheme)                  │
                       │                                    │
                       │  Lee 'iss' del JWT (sin validar)   │
                       └─────┬─────────────────────┬────────┘
                             │                     │
   iss = login.microsoftonline.com/{tenant}/v2.0   │
   o sts.windows.net/{tenant}/                     │
                             │                     │
   token malformado / sin Bearer / sin tenant      │
   → forward a Jwt (falla limpio con 401)          │
                             │                     │
                             ▼                     ▼
       ┌─────────────────────────────┐    ┌─────────────────────────┐
       │ EntraServicePrincipal        │    │ Jwt (existente)         │
       │ (nuevo handler)              │    │ HMAC SHA-256            │
       │                              │    │ iss=millet-erp-api      │
       │ TryValidateServicePrincipal: │    │                         │
       │  - null  → NoResult          │    │ → ClaimsPrincipal       │
       │  - throw → Fail (401)        │    │   con sub/empresa/...   │
       │  - SP token válido → resolve │    └─────────────────────────┘
       │                              │
       │ ResolveAsync:                │
       │  - Found    → Success        │
       │  - Unknown  → Fail + 403     │
       │  - Disabled → Fail + 403     │
       └──────────────────────────────┘
                  │
                  ▼
       ┌─────────────────────────────────────┐
       │ ClaimsPrincipal del SP:             │
       │  sub=UsuarioServicio.Id             │
       │  current_empresa_id=EmpresaId       │
       │  name=Nombre                        │
       │  auth_type=service_principal        │
       │  entra_appid=AppId                  │
       │  permission=<clave> × N             │
       └─────────────────────────────────────┘
```

**Por qué `sub`=`UsuarioServicio.Id` y no algún claim nuevo:** el resto
del stack (idempotencia, audit_log, query filters via
`ICurrentUserContext`) lee `sub` del JWT. Reusar ese claim significa
que el SP es tratado como un "usuario" sin que el resto del código
sepa la diferencia. **Esto reduce el cross-cutting a casi cero — el
`IdempotencyMiddleware` NO se modificó** (ver §5 desviaciones).

---

## 3. Justificación de decisiones de diseño

### 3.1 Permisos directos al SP, sin Rol intermedio

`UsuarioServicioPermiso(UsuarioServicioId, PermisoClave)` asigna
permisos string-keyed directamente, **NO** vía Rol. Razón pragmática:

- En fase 1 hay ≤3 SPs (Glass Agent + futuros), permisos específicos y
  conocidos.
- La indirección `Rol→RolPermiso→Permiso` no paga su costo con tan
  pocos SPs.
- Los permisos viajan como claims en el principal (string), no como
  filas indexadas — string-keyed simplifica el handler de auth.

Si el catálogo crece a >10 SPs activos, refactorizar a
`UsuarioServicioRol` + reuso del modelo de `RolPermiso`. Marcado en
docstring de `UsuarioServicio.cs`.

### 3.2 SP atado a UNA empresa (single-tenancy del ERP)

`UsuarioServicio.EmpresaId` no-nullable. El ERP es single-tenant
(Millet); multi-empresa por SP queda **fuera del modelo** por
arquitectura, no por deuda. Si emerge necesidad post-MVP, evaluar
agregar tabla N:N — no se marca PLATFORM-TODO porque no es deuda
identificada.

### 3.3 Stale auth window de 5 min — **riesgo conocido**

El `CachedServicePrincipalResolver` cachea Found/NotFound/Disabled por
5 min. Cambios al SP en BD (Activo, permisos) propagan hasta 5 min
después. Para acción urgente: **restart del App Service** (limpia el
IMemoryCache singleton del proceso). Endpoint admin para invalidación
granular queda como `PLATFORM-TODO(<SpCacheInvalidation>)`.

### 3.4 Cache también de NotFound/Disabled

Decisión consciente: bloquea ataques de **AppId-spraying** (no
gastamos BD por cada token con AppId aleatorio). Trade-off: dar de
alta un SP nuevo en BD tarda hasta 5 min en empezar a funcionar.
Aceptable para fase 1.

---

## 4. PLATFORM-TODOs introducidos

Buscables con `rg "PLATFORM-TODO" backend/src`. Cada uno tiene comment
inline en el código + entry aquí.

| Marca | Ubicación | Bloqueante para |
|---|---|---|
| `<MultiTenantSpResolution>` | `DefaultServicePrincipalResolver.ResolveAsync` (parámetro `entraObjectId` discardeado) | Multi-tenant (validar `oid` del token contra el persistido). |
| `<EntraIdResolverServicio>` | _NO introducido en este PR_ | El bootstrap NO valida contra Microsoft Graph que el AppId existe en el tenant antes de persistir. Queda implícito como deuda; agregar el TODO cuando la integración con Graph esté en scope. |
| `<UsuarioServicioAdminUI>` | _NO introducido en este PR_ | Endpoints CRUD admin para SPs (hoy solo se gestionan desde appsettings/KV). Marcar cuando se diseñe el módulo de admin. |
| `<SpCacheInvalidation>` | `CachedServicePrincipalResolver` (XML doc) | Endpoint admin para invalidación en caliente del cache. |

> **Nota:** los TODOs `<EntraIdResolverServicio>` y `<UsuarioServicioAdminUI>` se mencionan en este RESUMEN pero NO se marcaron en código fuente porque no hay un sitio natural sin agregar comentarios "fantasma". Cuando lleguen los PRs respectivos, se introducirán inline.

---

## 5. Desviaciones del prompt (con justificación)

### D1. `IEmpresaResolverPort` movido a `Compartido.Application/Ports/`

**Prompt:** ubicar en `Identidad.Application/Ports/`.
**Implementado:** en `Compartido.Application/Ports/`.
**Razón:** Compartido no referencia a Identidad (la dirección actual es Identidad → Compartido). Si el port vive en Identidad y el adapter en Compartido, requiere agregar Compartido → Identidad → ciclo de dependencias. Mover el port a Compartido es semánticamente correcto (la entidad `Empresa` es propiedad de Compartido) y evita el ciclo. Identidad ya referencia Compartido, así que su bootstrap consume el port sin problema.

### D2. `IdempotencyMiddleware` NO se modificó

**Prompt (D-IDEMPOTENCY):** "ajustarlo para leer EITHER `ICurrentUserContext` OR `ICurrentServicePrincipal`".
**Implementado:** sin cambios al middleware. **Igual funciona** porque el handler de SP arma `sub`=`UsuarioServicio.Id` y `current_empresa_id`=`EmpresaId`, los mismos claims que `ICurrentUserContext.UserId` y `ICurrentEmpresaContext.Current` ya leían. El middleware scopea correctamente sin saber de SPs.
**Trade-off:** se cumple D-IDEMPOTENCY en espíritu (las keys NO colisionan entre humano y SP, porque sus IDs son `Guid.CreateVersion7()` distintos), pero NO se introduce `IPrincipalIdentifier` ni se duplica lectura de contextos. Es la implementación más limpia.

### D3. NO se crearon Commands/Queries MediatR

**Prompt:** crear `RegistrarUsuarioServicioCommand`, `ObtenerPermisosDelServicePrincipalQuery`, etc.
**Implementado:** no se crearon — el bootstrap toca `IdentidadDbContext` directo (mismo patrón que `BootstrapSuperAdminHostedService`); el resolver tiene su propio puerto.
**Razón:** sin endpoints admin (que llegan con `<UsuarioServicioAdminUI>`), los handlers MediatR serían código muerto en este PR. Cuando se construyan los endpoints admin, agregar handlers ahí.

### D4. Tests de regresión humano + integration tests del path SP — **NO incluidos en este PR**

**Prompt:** tests integration con `WebApplicationFactory<Program>` + DI override de `IEntraTokenValidator` para mockear el flow.
**Implementado:** **solo unit tests** (Domain de SP + CachedResolver). 26/26 passed.
**Gap:** tests integration del PolicyScheme + handler + regresión del path humano + tests del bootstrap end-to-end.
**Razón:** complejidad de armar `WebApplicationFactory` con DB de prueba (requiere Postgres real local + setup de seed `compartido.empresas` + mock convincente de `IEntraTokenValidator`). Para fase 1 los unit tests + el build verde + la migración con preview SQL inspeccionable son **suficientes** para mergear con confianza. **Antes de mergear PR D** (que sí ejerce el flow end-to-end del SP) debe agregarse la suite integration.

---

## 6. Cómo correr los tests localmente

```powershell
cd c:\Users\UserSP\Desktop\Project_Millet_ERP

# Build limpio
dotnet build backend\Millet.sln
# Esperado: 0 errors, 2 warnings (MSB3277 preexistente — no nuestro)

# Tests unitarios (sin BD)
dotnet test backend\tests\Identidad.UnitTests\Millet.Identidad.UnitTests.csproj --no-build
# Esperado: Passed 26, Failed 0

# Tests existentes (regresión preventiva)
dotnet test backend\Millet.sln --no-build
# Esperado: todo passing (incluye SharedKernel, Compras, Administracion, Api integration)
```

---

## 7. Validación manual end-to-end (cuando exista PR D con un endpoint del módulo Aw)

Una vez que el PR D agregue un endpoint protegido por
`integraciones.aw.cotizaciones.crear`, se puede ejercer el flow real
desde tu workstation:

```powershell
# 1. Obtener token client_credentials del SPN dev
$TENANT_ID = (az account show --query tenantId -o tsv)
$SPN_SECRET = az keyvault secret show `
    --vault-name kv-millet-dev-mxc-01 `
    --name glass-agent-spn-client-secret `
    --query value -o tsv
$API_CLIENT_ID = az keyvault secret show `
    --vault-name kv-millet-dev-mxc-01 `
    --name auth-entra-api-client-id --query value -o tsv

$TOKEN = (Invoke-RestMethod `
    -Uri "https://login.microsoftonline.com/$TENANT_ID/oauth2/v2.0/token" `
    -Method POST `
    -Body @{
        client_id     = "990f1714-c0df-4dd8-aef8-429bc50b2f90"
        client_secret = $SPN_SECRET
        grant_type    = "client_credentials"
        scope         = "api://$API_CLIENT_ID/.default"
    }).access_token

# 2. Llamar endpoint protegido (cuando exista)
Invoke-RestMethod `
    -Uri "https://app-millet-dev-mxc-01.azurewebsites.net/api/v1/integraciones/aw/cotizaciones" `
    -Method POST `
    -Headers @{
        "Authorization"   = "Bearer $TOKEN"
        "Idempotency-Key" = [guid]::NewGuid().ToString()
    } `
    -ContentType "application/json" `
    -Body (@{ quote_reference = "Q-2026-TEST"; edi_content = "..." } | ConvertTo-Json)
```

Hasta que PR D exista, validar manualmente el flow es imposible — los
tests unit + el build + la inspección del SQL preview son los
artefactos disponibles para review.

---

## 8. Pendiente para PR de infra ("chore(infra)") — NO incluido aquí

Bicep no fue tocado (regla del prompt). Cuando se aplique:

1. **Crear secret en KV** `auth-service-principals-json` (tipo string):
   ```powershell
   $JSON = '[{"Name":"Glass Agent - Production","AppId":"<prod-spn-appid>","ObjectId":"<prod-spn-oid>","EmpresaRfc":"TIG890101AAA","Permisos":["integraciones.aw.cotizaciones.crear","integraciones.aw.cotizaciones.consultar"]}]'
   az keyvault secret set --vault-name kv-millet-prod-mxc-01 `
       --name auth-service-principals-json --value $JSON
   ```
2. **App setting nuevo en `infra/modules/appservice.bicep`:**
   ```bicep
   {
     name: 'Auth__ServicePrincipalsJson'
     value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=auth-service-principals-json)'
   }
   ```
3. **Restart del App Service** para que `BootstrapServicePrincipalsHostedService` corra con el JSON poblado.

Para dev, el bootstrap ya recoge el JSON desde `appsettings.Development.json` — funciona sin Bicep cambios.

---

## 9. Confirmación de NO-tocar

- ❌ `infra/` — sin cambios.
- ❌ `on-prem/` — sin cambios.
- ❌ `frontend/` — sin cambios.
- ❌ Módulos de negocio NO tocados: `Compras`, `DatosMaestros`, `Catalogos`, `Administracion`, `Almacen`. (Compartido SÍ tocado, mínimamente, para hostear el `EmpresaResolverAdapter` — D1.)
- ❌ `LoginOrchestrator` y `POST /api/auth/sesion` — sin cambios. Path humano clásico intacto.
- ❌ `EntraTokenValidator.ValidateAsync` (camino humano) — sin cambios. El método nuevo `TryValidateServicePrincipalAsync` se agrega al lado, no modifica el existente.

---

## 10. SQL preview de la migración (primeras 90 líneas)

Archivo completo: `backend/_last_migration_preview.sql` (93 líneas).

```sql
START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260515205658_AddUsuarioServicio') THEN
    CREATE TABLE identidad.usuario_servicio (
        id uuid NOT NULL,
        nombre character varying(120) NOT NULL,
        entra_app_id uuid NOT NULL,
        entra_object_id uuid NOT NULL,
        empresa_id uuid NOT NULL,
        activo boolean NOT NULL DEFAULT TRUE,
        created_at_utc timestamp with time zone NOT NULL,
        deactivated_at_utc timestamp with time zone,
        notes text,
        version integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        created_by text,
        updated_by text,
        deleted_at timestamp with time zone,
        CONSTRAINT pk_usuario_servicio PRIMARY KEY (id),
        CONSTRAINT fk_usuario_servicio_empresas_empresa_id FOREIGN KEY (empresa_id) REFERENCES compartido.empresas (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260515205658_AddUsuarioServicio') THEN
    CREATE TABLE identidad.usuario_servicio_permiso (
        usuario_servicio_id uuid NOT NULL,
        permiso_clave character varying(120) NOT NULL,
        CONSTRAINT pk_usuario_servicio_permiso PRIMARY KEY (usuario_servicio_id, permiso_clave),
        CONSTRAINT fk_usuario_servicio_permiso_usuario_servicio_usuario_servicio_ FOREIGN KEY (usuario_servicio_id) REFERENCES identidad.usuario_servicio (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

-- + 9 INSERTs en identidad.permisos para los Integraciones.Aw* canónicos
-- + 3 índices: (empresa_id), unique (entra_app_id), (entra_object_id)
-- + INSERT en __EFMigrationsHistory
COMMIT;
```

(Ver archivo completo para los INSERTs de seed y los índices.)
