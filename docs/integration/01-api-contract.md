# 01 — API Contract: Glass Agent ↔ Millet ERP

> **Doc espejo.** Mantener copia idéntica en ambos repos:
> - `Project_Millet_ERP/docs/integration/01-api-contract.md`
> - `glass-agent/docs/integration/01-api-contract.md`
>
> **Versión:** 2.1.0 (campo `sucursal` requerido — cutover hard)
> **Última actualización:** 2026-05-21
> **Source of truth:** repo `Project_Millet_ERP` (código C# wins sobre este doc).
> **Depende de:** `00-system-overview.md` (lectura previa obligatoria).
>
> **Breaking changes desde v1.0.0**: JSON keys cambiaron de `snake_case`
> (propuesto) a `camelCase` (implementación real ASP.NET Core default).
> Schema del body se aplanó (sin `customer{}` ni `metadata{}` anidados).
> El `status` enum dejó de ser string y pasó a integer. El path de detalle
> usa `{id}` (Guid) en lugar de `{quote_reference}`. Ver CHANGELOG §11.

---

## 1. Propósito

Este documento define el contrato REST entre el Glass Agent (consumidor) y el módulo `Millet.Integraciones.Aw` del ERP (proveedor). Cubre:

- Convenciones globales del API (versionado, idempotency, ETag, errores, fechas).
- Mecánica de autenticación con service principal Entra ID.
- Schemas completos de cada endpoint con ejemplos request/response.
- Notificaciones realtime vía SignalR.
- Códigos de error específicos del dominio.
- Política de versionado y deprecación.

### 1.1 Modelo del flow asíncrono (cambio de polling → callback)

La v1.0 asumía que el ERP **polleaba SQL Server on-prem** vía Hybrid
Connection para correlacionar EDIs procesados por A+W. Esa arquitectura
fue reemplazada en PR #198–#203 por un modelo **callback per-EDI**:

- El **drop service on-prem** ahora bloquea el HTTP del ERP hasta que A+W
  termina de procesar el EDI (waited ~30-60s en condiciones normales).
- Retorna `outcome=success` con `aw_doc_id` extraído del log de A+W
  (códigos `(4602)` para detect del file + `(4615)` para el doc id),
  `outcome=failed` si A+W rechazó, o `outcome=stuck` si timeout (120s).
- El ERP transita directamente `Submitted → Correlated|FailedCorrelation|FailedDrop`.
  El estado intermedio `AwaitingCorrelation` fue retirado.

Implicación para Glass Agent: **el endpoint POST de cotización no cambió
su contrato externo** (sigue siendo 202 Accepted con estado inicial = Submitted).
Lo que cambió es la latencia interna del backend (~30-60s típico vs polling
de hasta 2.5min) y los estados terminales disponibles.

### 1.2 Versionado de este documento

Cualquier cambio breaking al contrato requiere:
1. Actualización de este documento (con bump de versión y entrada en CHANGELOG)
2. Sincronización a ambos repos
3. Versión nueva del path (`/api/v2/...`) si rompe consumidores existentes

---

## 2. Convenciones globales

### 2.1 Versionado

Versionado por path. Versión actual: `v1`.

```
https://app-millet-{env}-mxc-01.azurewebsites.net/api/v1/integraciones/aw/...
```

Reglas (alineadas a ADR-0021 del ERP):

- **Cambios additivos NO rompen v1**: campos nuevos en response, endpoints nuevos, valores nuevos en enums. Glass Agent debe tolerar campos extras desconocidos (forward-compatible parsing).
- **Cambios breaking exigen v2**: eliminación de campos, cambio de tipo, cambio de semántica. Las versiones coexisten al menos 90 días tras anuncio.
- **Versión menor en headers**: opcional, `X-Api-Version: 1.3` para tracking interno. No afecta routing.

### 2.2 Fechas y zonas horarias

Todas las fechas/timestamps en **ISO 8601 UTC** con sufijo `Z`:

```json
"submitted_at": "2026-05-15T18:32:14Z"
"aw_correlated_at": "2026-05-15T18:34:22.150Z"
```

El ERP siempre devuelve UTC. Conversión a hora local (México Central) es responsabilidad del cliente (Glass Agent al mostrar al vendedor).

### 2.3 Identificadores

| Campo | Formato | Origen | Ejemplo |
|---|---|---|---|
| `quote_reference` | `Q-{YYYY}-{NNNNN}` | Glass Agent genera | `Q-2026-00451` |
| `aw_doc_id` | Integer | A+W asigna al procesar | `12345` |
| `idempotency_key` | UUID v4 | Glass Agent por request | `7c9e6679-7425-40de-944b-e07fc1f90ae7` |
| `quote_id` (interno ERP) | UUID v7 | ERP al persistir | `01935a8b-5a4e-7c9e-9c4a-6b8e2f1d3a5c` |
| `request_id` | UUID v4 | ERP genera por request | header `X-Request-Id` en response |

`quote_reference` es la **llave de correlación primaria** entre los tres sistemas (Glass Agent ↔ ERP ↔ A+W). Se inyecta en el record `FH` del EDI; A+W persiste su valor en `pool_auftrag.<campo_correlacion>` (TBD, ver overview §10).

### 2.4 Idempotency

Endpoints de escritura **requieren** header `Idempotency-Key` (UUID v4). Sigue ADR-0020 del ERP.

```
Idempotency-Key: 7c9e6679-7425-40de-944b-e07fc1f90ae7
```

Comportamiento:

- Misma key + mismo body en ventana de 24h → respuesta cacheada (mismo status code y body).
- Misma key + body distinto → `409 Conflict` con detalle `idempotency_key_reused_with_different_body`.
- Key en proceso (request anterior aún corriendo) → `409 Conflict` con detalle `idempotency_key_in_flight`.

El Glass Agent debe **persistir la key** asociada al pedido local (en `glass_edi_outbox.idempotency_key`) antes de hacer el primer request, y reusarla en reintentos del mismo lote. Si el pedido se modifica y se reenvía, debe generar key nueva.

### 2.5 ETag y concurrencia optimista

Endpoints `GET` retornan `ETag` (alineado a ADR-0012):

```
HTTP/1.1 200 OK
ETag: "W/\"a8f3e6b1d2c7\""
Cache-Control: no-cache
```

El cliente puede usar `If-None-Match` para evitar transferir bodies repetidos:

```
GET /api/v1/integraciones/aw/cotizaciones/Q-2026-00451
If-None-Match: "W/\"a8f3e6b1d2c7\""

HTTP/1.1 304 Not Modified
ETag: "W/\"a8f3e6b1d2c7\""
```

Endpoints `PATCH` (no aplica en MVP de Glass Agent — solo aplicará a endpoints administrativos futuros) exigirán `If-Match` para concurrencia.

### 2.6 Error format (RFC 7807 Problem Details)

Todos los errores siguen RFC 7807 (alineado a ADR-0010):

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/problem+json
```

```json
{
  "type": "https://docs.millet-erp.com/errors/aw/edi_malformed",
  "title": "El contenido EDI no respeta el formato esperado de A+W",
  "status": 422,
  "detail": "El record FH debe tener exactamente 512 caracteres. Se recibió 480.",
  "instance": "/api/v1/integraciones/aw/cotizaciones",
  "request_id": "01935a8b-5a4e-7c9e-9c4a-6b8e2f1d3a5c",
  "trace_id": "00-7c9e66797425-40de944be07fc1f9-01",
  "errors": {
    "edi_content": [
      "Record FH inválido: longitud incorrecta (480, esperado 512)"
    ]
  }
}
```

Campos:

- `type`: URI estable del tipo de error (no cambia entre versiones)
- `title`: descripción legible (puede cambiar entre versiones; usar `type` para lógica)
- `status`: código HTTP duplicado
- `detail`: contexto específico de esta instancia
- `instance`: path original
- `request_id`: para correlación en Application Insights
- `trace_id`: W3C trace context si está disponible
- `errors`: opcional, validación por campo

Lista de `type` URIs en §6.

### 2.7 Headers comunes

**Requeridos en todo request:**

```
Authorization: Bearer <entra_access_token>
Content-Type: application/json   (en escrituras)
Accept: application/json
```

**Opcionales:**

```
X-Request-Id: <uuid_v4>          # Si lo manda Glass Agent, el ERP lo usa; si no, lo genera
X-Correlation-Id: <free_text>    # Para correlar múltiples llamadas (ej: session_id del agent)
```

**Headers de response:**

```
X-Request-Id: <uuid_v4>
ETag: <weak_etag>                 # En GET
RateLimit-Limit: 60
RateLimit-Remaining: 58
RateLimit-Reset: 30
```

---

## 3. Autenticación

### 3.1 Modelo

`Auth.Mode = EntraId` (único modo en QA/Prod del ERP, ADR-0003).

Glass Agent autentica vía **service principal Entra ID + client credentials flow** (OAuth 2.0). No hay API keys estáticas en este path.

```
[Glass Agent PHP]
      │
      │ 1. Solicita token a Entra (con client_id + secret)
      ▼
[Entra ID]
      │
      │ 2. Devuelve access_token (JWT firmado, válido ~1h)
      ▼
[Glass Agent PHP]
      │
      │ 3. POST al ERP con Authorization: Bearer <token>
      ▼
[Millet.Api]
      │
      │ 4. Valida JWT (firma + iss + aud + exp + appid)
      │ 5. Mapea appid a "usuario de servicio" + permisos
      ▼
[Procesa request, retorna response]
```

### 3.2 Configuración Entra ID (App Registration)

Se crea **una App Registration por cada sistema M2M** que consume el ERP. Para Glass Agent:

- **Nombre**: `Glass Agent - {env}` (p.ej. `Glass Agent - Production`)
- **Application (client) ID**: se almacena en config.php del Glass Agent como `GLASS_ERP_CLIENT_ID`
- **Client secret**: generado en Entra, se almacena en config.php como `GLASS_ERP_CLIENT_SECRET` (gitignored, rotar cada 6 meses)
- **API permissions**: ninguna (no llama a Graph)
- **Expose an API**: no aplica
- **Audience**: `api://<erp_api_client_id>` (configurado del lado del ERP)

El **scope** que pide Glass Agent al token endpoint es:

```
api://<erp_api_client_id>/.default
```

Esto solicita "todos los permisos que la app tiene configurados" — los permisos específicos se gestionan del lado del ERP en su mapeo de service principals (ver §3.5).

### 3.3 Obtención de token desde Glass Agent (PHP)

```php
// glass-agent/include/functions_erp.php

function glass_erp_get_access_token(): string {
    // Cache lookup primero
    $cached = glass_erp_token_cache_get();
    if ($cached !== null) {
        return $cached;
    }
    
    $tenantId = GLASS_ERP_TENANT_ID;
    $url = "https://login.microsoftonline.com/{$tenantId}/oauth2/v2.0/token";
    
    $body = http_build_query([
        'grant_type'    => 'client_credentials',
        'client_id'     => GLASS_ERP_CLIENT_ID,
        'client_secret' => GLASS_ERP_CLIENT_SECRET,
        'scope'         => GLASS_ERP_SCOPE,  // api://<erp_api_client_id>/.default
    ]);
    
    $ch = curl_init($url);
    curl_setopt_array($ch, [
        CURLOPT_POST            => true,
        CURLOPT_POSTFIELDS      => $body,
        CURLOPT_RETURNTRANSFER  => true,
        CURLOPT_HTTPHEADER      => ['Content-Type: application/x-www-form-urlencoded'],
        CURLOPT_TIMEOUT         => 10,
    ]);
    
    $response = curl_exec($ch);
    $httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
    curl_close($ch);
    
    if ($httpCode !== 200) {
        throw new RuntimeException("Entra token endpoint returned {$httpCode}: {$response}");
    }
    
    $data = json_decode($response, true);
    $token = $data['access_token'];
    $expiresIn = (int) $data['expires_in']; // típicamente 3599 segundos
    
    // Cache con margen de seguridad (90% del TTL)
    glass_erp_token_cache_set($token, intval($expiresIn * 0.9));
    
    return $token;
}
```

Cache de tokens: tabla `glass_erp_token_cache` (no detallada aquí; mig 017 futura). Una sola fila con valor + expiry. Concurrencia: lock optimista o reuse el cache de Redis si existe.

### 3.4 Validación de token en el ERP

Extender `EntraTokenValidator` existente (`backend/src/Api/Auth/EntraTokenValidator.cs`) para aceptar tokens de service principal:

- Tokens humanos llevan claim `oid` (object id del usuario) y `upn`/`preferred_username`.
- Tokens de service principal llevan claim `oid` + `appid` (Application ID, sin `upn`).
- Ambos llevan `iss` (issuer = tenant), `aud` (audience = ERP API), `exp`, `iat`, `nbf`.

Pseudocódigo del flujo extendido:

```csharp
public async Task<ValidatedToken> ValidateAsync(string rawToken)
{
    var jwtHandler = new JsonWebTokenHandler();
    var validationResult = await jwtHandler.ValidateTokenAsync(rawToken, _validationParameters);
    
    if (!validationResult.IsValid)
        throw new AuthenticationFailedException(validationResult.Exception);
    
    var token = validationResult.SecurityToken as JsonWebToken;
    var oid = token.GetClaim("oid")?.Value;
    var appId = token.GetClaim("appid")?.Value;
    var upn = token.GetClaim("upn")?.Value ?? token.GetClaim("preferred_username")?.Value;
    
    if (appId is not null && upn is null)
    {
        // Es service principal: resolver via UsuarioServicio
        var spnUser = await _spnResolver.ResolveAsync(appId, oid);
        return ValidatedToken.ForServicePrincipal(spnUser);
    }
    
    // Es usuario humano: flujo existente
    var user = await _loginOrchestrator.ResolveOrCreateUserAsync(oid, upn);
    return ValidatedToken.ForUser(user);
}
```

### 3.5 Mapeo appid → UsuarioServicio en `Identidad`

Cambio nuevo al módulo `Identidad`. Agregar:

**Tabla** `identidad.usuario_servicio`:

```sql
CREATE TABLE identidad.usuario_servicio (
    id                  UUID PRIMARY KEY,
    nombre              VARCHAR(120) NOT NULL,        -- "Glass Agent - Production"
    entra_appid         UUID NOT NULL UNIQUE,         -- claim 'appid' del token
    entra_object_id     UUID NOT NULL,                -- claim 'oid' del token
    empresa_id          UUID NOT NULL REFERENCES compartido.empresa(id),
    activo              BOOLEAN NOT NULL DEFAULT true,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deactivated_at      TIMESTAMPTZ NULL,
    notes               TEXT NULL
);

CREATE TABLE identidad.usuario_servicio_permiso (
    usuario_servicio_id UUID NOT NULL REFERENCES identidad.usuario_servicio(id),
    permiso_clave       VARCHAR(120) NOT NULL,        -- ej "integraciones.aw.cotizaciones.crear"
    PRIMARY KEY (usuario_servicio_id, permiso_clave)
);
```

**Resolver** (`IServicePrincipalResolver`): recibe `appid` + `oid` del token, retorna `UsuarioServicio` con sus permisos. Cache en memoria 5 min (mismo patrón que `InMemoryPermissionCache`).

**Bootstrap inicial**: similar a `BootstrapSuperAdminHostedService`, un `BootstrapServicePrincipalsHostedService` que al arranque del ERP en QA/Prod crea/actualiza los SPs configurados via app settings. Configurable así:

```json
"Auth": {
  "ServicePrincipals": [
    {
      "Name": "Glass Agent - Production",
      "AppId": "11111111-2222-3333-4444-555555555555",
      "ObjectId": "66666666-7777-8888-9999-aaaaaaaaaaaa",
      "EmpresaRfc": "MID010101AAA",
      "Permisos": [
        "integraciones.aw.cotizaciones.crear",
        "integraciones.aw.cotizaciones.consultar"
      ]
    }
  ]
}
```

Valores reales vienen de Key Vault refs.

### 3.6 Permisos canónicos nuevos

Se agregan a `PermisosCanonicos` (`backend/src/Identidad/Domain/PermisosCanonicos.cs`):

```csharp
public static class PermisosCanonicos
{
    // ... existentes ...
    
    public static class IntegracionesAw
    {
        // Cotizaciones
        public const string CotizacionesCrear = "integraciones.aw.cotizaciones.crear";
        public const string CotizacionesConsultar = "integraciones.aw.cotizaciones.consultar";
        public const string CotizacionesReintentar = "integraciones.aw.cotizaciones.reintentar";
        
        // Otros dominios (placeholder, no implementados en fase 1)
        public const string PedidosConsultar = "integraciones.aw.pedidos.consultar";
        public const string ClientesConsultar = "integraciones.aw.clientes.consultar";
        public const string ArticulosConsultar = "integraciones.aw.articulos.consultar";
        public const string InventarioConsultar = "integraciones.aw.inventario.consultar";
        
        // Administración del módulo
        public const string AdministrarServicios = "integraciones.aw.administracion.servicios";
        public const string AdministrarConfiguracion = "integraciones.aw.administracion.configuracion";
    }
}
```

Glass Agent recibe inicialmente: `CotizacionesCrear`, `CotizacionesConsultar`. Nada más. Si en algún punto se necesita consultar pedidos o reintentar, se le otorga explícitamente.

### 3.7 Errores de auth

| Situación | HTTP | Type URI |
|---|---|---|
| Sin header `Authorization` | 401 | `.../auth/missing_token` |
| Token mal formado | 401 | `.../auth/malformed_token` |
| Firma inválida | 401 | `.../auth/invalid_signature` |
| Token expirado | 401 | `.../auth/expired_token` |
| Audience incorrecta | 401 | `.../auth/wrong_audience` |
| appid no registrado como SP | 403 | `.../auth/unknown_service_principal` |
| SP existe pero sin permiso requerido | 403 | `.../auth/insufficient_permissions` |
| SP desactivado | 403 | `.../auth/service_principal_disabled` |

---

## 4. Endpoints

### 4.1 POST /api/v1/integraciones/aw/cotizaciones

Encolar una cotización (EDI) para entrega a A+W. Es el endpoint primario del Glass Agent.

**Permiso requerido:** `integraciones.aw.cotizaciones.crear`

**Headers:**

```
Authorization: Bearer <token>
Content-Type: application/json
Idempotency-Key: <uuid_v4>          (REQUERIDO)
X-Request-Id: <uuid_v4>             (opcional)
X-Correlation-Id: <session_id>      (opcional, para traza con SupportBoard)
```

**Request body (camelCase, schema plano):**

```json
{
  "quoteReference": "Q-2026-00451",
  "sucursal": "CIR",
  "ediContent": "FH02.2.02700000000000000010001…\nK1…\nP1A…\n#END#\n",
  "customerTaxId": "XAXX010101000",
  "customerName": "Cliente Demo SA de CV",
  "source": "glass_agent",
  "itemsCount": 3,
  "payloadOriginalJson": "{\"sb_conversation_id\":12345,\"operator_user_id\":67,\"glass_agent_version\":\"2.4.0\"}"
}
```

**Schema (validador FluentValidation real en `RegistrarCotizacionEdiValidator.cs`):**

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `quoteReference` | string | ✅ | Regex `^Q-\d{4}-\d{5}$` (ej. `Q-2026-00451`) |
| `sucursal` | string | ✅ | **Código corto** uppercase de 3 chars; debe estar en el catálogo (ver §4.1.1). Case-insensitive (Agent puede mandar `"cir"` o `"CIR"`). |
| `ediContent` | string | ✅ | No vacío, **≥ 100 chars**, **contiene `#END#`**. Body máx 5 MB (Kestrel). |
| `customerTaxId` | string | ✅ | Regex `^([A-Z0-9]{12,13}\|EXT)$` (RFC mexicano en mayúsculas o literal `"EXT"`) |
| `customerName` | string | ✅ | No vacío, máx 254 chars |
| `source` | string | ✅ | Enum: `"glass_agent"` o `"interno"` |
| `itemsCount` | integer | ✅ | ≥ 1 |
| `payloadOriginalJson` | string | ✅ | No vacío. **JSON serializado como string** — snapshot del payload original del Agent. El ERP lo guarda raw como JSONB en `payload_original` para auditoría. Sugerido incluir `sb_conversation_id`, `operator_user_id`, `glass_agent_version`, etc. |

#### 4.1.1 Catálogo de sucursales

Códigos válidos hoy (configurados en `appsettings.json` → `IntegracionesAw:Sucursales`):

| Código | Display name |
|---|---|
| `CIR` | CIRCUITO |
| `CHI` | CHICHI SUAREZ |
| `CAN` | CANCUN |
| `CON` | CONKAL |

El código se **embebe en el filename** que el ERP escribe en el drop service on-prem: `cot_<SUCURSAL>_<QUOTE_REFERENCE>.edi` (ej. `cot_CIR_Q-2026-00451.edi`). Cada customizing de A+W usa pattern matching (`cot_CIR_*`, `cot_CHI_*`, etc.) para levantar **solo** los archivos que le tocan — cada customizing puede crear pedidos para una sola sucursal por diseño A+W.

**Si necesitas una sucursal adicional**, se agrega al config del ERP + se setea pattern en el customizing A+W correspondiente. NO requiere cambio de código del Agent.

**Validaciones server-side adicionales (más allá del validador):**

1. JWT válido + permiso `integraciones.aw.cotizaciones.crear` (sino 401/403).
2. **Idempotency middleware**: `Idempotency-Key` header presente y UUID v4 (sino 400). Misma key + mismo body en 24h → respuesta cacheada (mismo status/body, sin ejecutar handler).
3. **Unicidad por empresa**: `quoteReference` único en `integraciones_aw.entidad_externa` para la empresa del SPN. Duplicado → `409 aw_quote_reference_duplicada`.
4. La **empresa** la resuelve el ERP del JWT del SPN (1:1 por diseño D3). Glass Agent **NO manda** `empresaId` en el body.

**Success response (202 Accepted):**

```http
HTTP/1.1 202 Accepted
Content-Type: application/json
X-Request-Id: 01935a8b-5a4e-7c9e-9c4a-6b8e2f1d3a5c
Location: /api/v1/integraciones/aw/cotizaciones/01935a8b-5a4e-7c9e-9c4a-6b8e2f1d3a5c
```

```json
{
  "id": "019e397b-493b-78bd-9e6a-95b37f729ca2",
  "quoteReference": "Q-2026-00451",
  "sucursal": "CIR",
  "estado": 0,
  "submittedAt": "2026-05-18T05:07:21.5318837+00:00"
}
```

**`estado` es integer (no string)**, mapeado al enum `EstadoEntidad` del backend:

| Valor | Nombre | Significado |
|---|---|---|
| `0` | `Submitted` | Recibido por el ERP, en cola Outbox |
| `2` | `Correlated` | A+W procesó OK, doc id asignado |
| `3` | `FailedDrop` | Error entregando al on-prem (network, timeout, terminal HTTP) — reintentable |
| `4` | `FailedCorrelation` | A+W rechazó el EDI (artículo inválido, posiciones incompletas, etc.) |
| `5` | `ManuallyResolved` | Operador Tiglass cerró administrativamente con nota |

> El valor `1` que era `AwaitingCorrelation` (modelo polling) fue retirado en PR #201. El callback per-EDI transita directamente `Submitted → Correlated|FailedCorrelation|FailedDrop`.

Tras el POST, el estado **siempre arranca en 0** (`Submitted`). La transición a un estado terminal toma típicamente ~30-60s (drop service espera a A+W). Glass Agent puede consultar el estado actualizado vía `GET /{id}` (§4.2).

**Error responses (RFC 7807 Problem Details):**

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json
```
```json
{
  "type": "https://millet-erp/errors/aw_cotizacion_invalida",
  "title": "El payload de cotización tiene errores de validación",
  "status": 400,
  "errors": {
    "QuoteReference": ["Formato inválido. Esperado Q-YYYY-NNNNN"],
    "EdiContent": ["EdiContent muy pequeño (<100 chars), probablemente truncado o inválido."]
  }
}
```

> Las keys de `errors` vienen en PascalCase (nombre de la propiedad C#) per default de ASP.NET Core ValidationProblemDetails. El payload de input sigue siendo camelCase — solo el error map usa PascalCase.

```http
HTTP/1.1 409 Conflict
```
```json
{
  "type": "https://millet-erp/errors/idempotency/key_reused_with_different_body",
  "title": "La idempotency key ya se usó con un body distinto",
  "status": 409,
  "detail": "La key 7c9e6679-7425-40de-944b-e07fc1f90ae7 corresponde a Q-2026-00451 con hash distinto"
}
```

```http
HTTP/1.1 409 Conflict
```
```json
{
  "type": "https://millet-erp/errors/aw_quote_reference_duplicada",
  "title": "Cotización duplicada",
  "status": 409,
  "detail": "Ya existe una cotización con quoteReference=Q-2026-00451 en esta empresa."
}
```

```http
HTTP/1.1 422 Unprocessable Entity
```
```json
{
  "type": "https://millet-erp/errors/aw_edi_malformed",
  "title": "El contenido EDI no respeta el formato esperado de A+W",
  "status": 422,
  "detail": "El record FH debe tener exactamente 512 caracteres",
  "errors": {
    "edi_content": ["Record FH inválido: longitud 480, esperado 512"]
  }
}
```

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json
```
```json
{
  "type": "https://millet-erp/errors/aw_cotizacion_invalida",
  "title": "El payload de cotización tiene errores de validación",
  "status": 400,
  "errors": {
    "Sucursal": ["AW_SUCURSAL_INVALIDA: Sucursal fuera de catálogo. Válidas: CAN, CHI, CIR, CON."]
  }
}
```

```http
HTTP/1.1 400 Bad Request
```
```json
{
  "type": "https://millet-erp/errors/aw_cotizacion_invalida",
  "title": "El payload de cotización tiene errores de validación",
  "status": 400,
  "errors": {
    "Sucursal": ["AW_SUCURSAL_REQUERIDA: Sucursal es requerida."]
  }
}
```

### 4.2 GET /api/v1/integraciones/aw/cotizaciones/{id}

Consultar estado actual de una cotización por su `id` (Guid devuelto en el POST).

**Permiso requerido:** `integraciones.aw.cotizaciones.consultar`

**Path params:** `id` (Guid v7, el campo `id` que retornó el 202 del POST).

> v1.0 del doc usaba `{quote_reference}` como path param. La implementación real usa `{id}` (Guid). Glass Agent debe persistir el `id` retornado por el POST y usarlo para consultas. Si solo tiene el `quoteReference`, usar §4.5 listar con filtro `quoteReference=...`.

**Headers:**

```
Authorization: Bearer <token>
```

(El endpoint NO emite ETag hoy — `If-None-Match` no aplica.)

**Response 200:**

```http
HTTP/1.1 200 OK
Content-Type: application/json
```

```json
{
  "id": "019e397b-493b-78bd-9e6a-95b37f729ca2",
  "quoteReference": "Q-2026-00451",
  "estado": 2,
  "submittedAt": "2026-05-18T05:07:21Z",
  "deliveredToAwAt": "2026-05-18T05:07:25Z",
  "correlatedAt": "2026-05-18T05:07:54Z",
  "awDocId": 10432220,
  "awDocIdSecondary": null,
  "awErrorCodes": null,
  "awErrorMessage": null,
  "awDiagnosticLog": null,
  "lastError": null,
  "retryCount": 0,
  "resolutionNote": null,
  "customerTaxId": "XAXX010101000",
  "customerName": "Cliente Demo SA de CV",
  "source": "glass_agent",
  "itemsCount": 3
}
```

Campos:

- `estado` integer (ver tabla §4.1).
- `awDocId` el ID del documento creado por A+W (null hasta que `estado=2 Correlated`).
- `awErrorCodes`, `awErrorMessage`, `awDiagnosticLog` poblados cuando `estado=4 FailedCorrelation` (A+W rechazó); para `FailedDrop` ver `lastError`.
- `ediContent` **NO se devuelve** en este response (puede ser MB). Si se necesita auditarlo, hay un endpoint admin separado (post-MVP).

**Response 404:**

```http
HTTP/1.1 404 Not Found
```
```json
{
  "type": "https://millet-erp/errors/aw_cotizacion_no_encontrada",
  "title": "La cotización solicitada no existe",
  "status": 404,
  "instance": "/api/v1/integraciones/aw/cotizaciones/019e397b-493b-78bd-9e6a-95b37f729ca2"
}
```

(También 404 si la cotización existe pero pertenece a otra empresa — el global query filter por empresa del JWT la oculta.)

### 4.3 GET /api/v1/integraciones/aw/cotizaciones/{id}/historial

Histórico de eventos de una cotización (recepción, drop, correlación, reintentos).

**Permiso requerido:** `integraciones.aw.cotizaciones.consultar`

**Path params:** `id` (Guid, el de §4.2).

**Response 200:**

```json
{
  "id": "019e397b-493b-78bd-9e6a-95b37f729ca2",
  "quoteReference": "Q-2026-00451",
  "events": [
    {
      "occurredAt": "2026-05-18T05:07:21Z",
      "type": "cotizacion_recibida",
      "actor": "Glass Agent SPN dev"
    },
    {
      "occurredAt": "2026-05-18T05:07:25Z",
      "type": "drop_iniciado",
      "actor": "AwDropWorker"
    },
    {
      "occurredAt": "2026-05-18T05:07:54Z",
      "type": "pedido_correlacionado",
      "actor": "AwDropWorker",
      "awDocId": 10432220,
      "waitedMs": 28215
    }
  ]
}
```

`type` enum (eventos del callback flow per-EDI):

- `cotizacion_recibida` — POST aceptado, persistido en `Submitted`.
- `drop_iniciado` — Worker tomó del SB y llamó al drop service.
- `pedido_correlacionado` — A+W procesó exitosamente, doc id asignado.
- `cotizacion_rechazada` — A+W rechazó el EDI (códigos en el evento).
- `drop_fallado_terminal` — drop HTTP terminal (4xx/5xx) o `stuck` por timeout A+W; tras agotar SB redeliveries.
- `reintento_manual` — operador disparó `POST /reintentar` desde la UI.
- `resuelto_manual` — operador cerró administrativamente con nota.

> Los eventos `drop_intentado` y `correlacion_expirada` del modelo polling viejo ya NO se emiten (PR #201 retiró el `AwCorrelationWorker`).

### 4.4 POST /api/v1/integraciones/aw/cotizaciones/{id}/reintentar

Forzar reintento del drop de una cotización en estado fallido.

**Permiso requerido:** `integraciones.aw.cotizaciones.reintentar`

> Endpoint orientado a **operadores Tiglass desde la UI interna**. Glass Agent normalmente NO lo llama (su flow es: POST + esperar; si quiere reintentar, hace POST nuevo con quoteReference nuevo o mismo idempotency-key).

**Headers:**

```
Authorization: Bearer <token>
Idempotency-Key: <uuid_v4>
Content-Type: application/json
```

**Request body:** (vacío hoy; el operador no necesita justificar)

```json
{}
```

**Response 202:**

```json
{
  "id": "019e397b-493b-78bd-9e6a-95b37f729ca2",
  "quoteReference": "Q-2026-00451",
  "estado": 0,
  "retryCount": 0,
  "requestedAt": "2026-05-18T20:14:00Z"
}
```

`retryCount` se resetea a 0 al `Reintentar()` (el aggregate limpia contadores y vuelve a `Submitted`).

**Errores:**

- `409 aw_invalid_state_transition`: el estado actual NO es `FailedDrop` ni `ManuallyResolved`. Estados activos (`Submitted`, ya en flight) y terminales OK (`Correlated`) o `FailedCorrelation` no se pueden reintentar directo. Si está en `FailedCorrelation`, primero usar `POST /marcar-resuelto` con nota y luego reintentar.
- `404 aw_cotizacion_no_encontrada`: el id no existe (o pertenece a otra empresa).

### 4.4-bis POST /api/v1/integraciones/aw/cotizaciones/{id}/marcar-resuelto

Cerrar administrativamente una cotización con nota.

**Permiso requerido:** `integraciones.aw.cotizaciones.reintentar` (mismo que reintentar — operador con permiso de write).

**Request body:**

```json
{
  "nota": "Cliente canceló pedido por teléfono. No reintentar."
}
```

`nota` es requerido (no vacío). Va a parar a `resolutionNote` con prefijo `[operador={userId}]`.

**Response 200:**

```json
{
  "id": "019e397b-493b-78bd-9e6a-95b37f729ca2",
  "estado": 5,
  "resolvedAt": "2026-05-18T20:14:00Z",
  "resolutionNote": "[operador=...] Cliente canceló pedido..."
}
```

**Errores:**

- `409`: estado actual es `Correlated` (terminal exitoso) o ya `ManuallyResolved` — no aplica resolver de nuevo.
- `422`: nota vacía.

### 4.5 GET /api/v1/integraciones/aw/cotizaciones

Listado paginado (offset/limit). Útil para dashboards operativos y para que Glass Agent busque por `quoteReference` sin tener que guardar el `id`.

**Permiso requerido:** `integraciones.aw.cotizaciones.consultar`

**Query params (todos opcionales):**

```
?estado=0                    (int del enum EstadoEntidad — un solo valor por ahora)
&desde=2026-05-18T00:00:00Z  (rango temporal, ISO 8601 con offset)
&hasta=2026-05-19T00:00:00Z
&quoteReference=Q-2026-00451 (filtro exacto)
&quoteReferenceSearch=2026-0 (prefijo case-insensitive vía ILIKE en Postgres)
&awDocId=10432220            (filtro por ID asignado por A+W)
&offset=0                    (default 0)
&limit=50                    (default 50, máx 200)
```

**Response 200:**

```json
{
  "items": [
    {
      "id": "019e397b-...",
      "quoteReference": "Q-2026-00451",
      "estado": 2,
      "submittedAt": "2026-05-18T05:07:21Z",
      "correlatedAt": "2026-05-18T05:07:54Z",
      "awDocId": 10432220,
      "customerName": "Cliente Demo SA de CV",
      "lastError": null
    }
  ],
  "total": 142,
  "offset": 0,
  "limit": 50
}
```

> El response no usa "pagination": {...} ni "links": {...}. Es `total/offset/limit` plano. Paginación: client computa `hasNext = offset + items.length < total`.

---

## 5. Notificaciones realtime (SignalR)

El ERP publica eventos por SignalR cuando hay un cambio de estado relevante (correlación, rechazo, drop fallido). Esto permite al frontend interno de Tiglass reaccionar sin polling.

### 5.1 Hub

**Decidido en PR #194**: hub dedicado **`IntegracionesAwHub`** (no compartido con Compras). Razones:

- Aisla auth y autorización (clientes de Compras no reciben eventos de A+W).
- Permite escalar canales independientemente.
- Cumple con segregación lógica de módulos (ADR-0030).

**Ruta:** `/hubs/integraciones-aw`.

**Auth:** JWT en query param `access_token` (patrón compartido con `ComprasHub` por compatibilidad con WebSockets que no permiten headers custom desde el browser).

**Permiso del hub:** `integraciones.aw.cotizaciones.consultar` (declarado en el `[Authorize]` del hub).

**Groups:** los clientes se suscriben automáticamente al grupo `aw-empresa:{empresaId}` de la empresa de su JWT. Esto evita que un usuario de empresa A reciba eventos de empresa B.

### 5.2 Eventos publicados

Implementación en `IntegracionesAwHubNotifier` (Api/Hubs/). Glass Agent NO necesita estos eventos (su flow es POST + polling opcional vía §4.2). Documentados para el frontend interno de Tiglass.

| Método del hub | Payload (camelCase) | Cuándo |
|---|---|---|
| `CotizacionEstadoActualizado` | `{ aggregateId, quoteReference, estado, ocurridoEn }` | Cualquier cambio de estado persistido. |
| `CorrelacionExitosa` | `{ aggregateId, quoteReference, awDocId, correlatedAt }` | Drop service reportó `outcome=Success` y aggregate transitó a `Correlated`. |
| `DropFallido` | `{ aggregateId, quoteReference, errorMessage, errorKind, retryCount }` | Drop HTTP transient (Abandon + redelivery SB) o `outcome=Stuck`. |
| `DropDeadLetter` | `{ aggregateId, quoteReference, errorMessage, errorKind }` | Drop HTTP terminal — mensaje a DLQ. |

> El método `CorrelacionExpirada` del modelo polling viejo **ya NO se emite** (PR #201 retiró el worker que lo disparaba).

### 5.3 Subscripciones del lado Glass Agent

Glass Agent **NO se suscribe** al hub. Su flow es:

- POST cotización → recibe 202 con `id` y `estado=0`.
- Si quiere actualizar UI del vendedor (estado de la cotización), hace **polling** a `GET /api/v1/integraciones/aw/cotizaciones/{id}` cada 5–10s mientras `estado=0`. Total ~5-15 polls (60-90s típico).
- Suficiente para volumen actual (≤ 100 cotizaciones/día). SignalR del lado PHP es overkill.

Si el volumen crece a >1000/día, evaluar Server-Sent Events o webhooks salientes del ERP — no requiere PHP-side SignalR.

---

## 6. Códigos de error (Problem Details types)

Lista canónica de `type` URIs. La raíz real en el ERP es `https://millet-erp/errors/` (no `docs.millet-erp.com` — ese host no existe; los `type` son IDs lógicos, no URLs navegables).

| Type | HTTP | Cuándo |
|---|---|---|
| `auth/missing_token` | 401 | Sin header Authorization |
| `auth/malformed_token` | 401 | Token no es JWT válido |
| `auth/invalid_signature` | 401 | Firma del JWT no coincide |
| `auth/expired_token` | 401 | exp del JWT en el pasado |
| `auth/wrong_audience` | 401 | aud del JWT ≠ esperado |
| `auth/unknown_service_principal` | 403 | appid no registrado como SP |
| `auth/insufficient_permissions` | 403 | SP carece de permiso requerido |
| `auth/service_principal_disabled` | 403 | SP fue desactivado |
| `aw_cotizacion_invalida` | 400 | Validación de body falló (ver `errors{}`) |
| `aw_edi_malformed` | 422 | EDI no respeta formato A+W |
| `aw_quote_reference_duplicada` | 409 | Ya existe una cotización con ese `quoteReference` en la empresa |
| `aw_cotizacion_no_encontrada` | 404 | El `id` no existe (o es de otra empresa, oculto por query filter) |
| `aw_invalid_state_transition` | 409 | Estado actual no permite la operación (ej. `Reintentar` desde `Correlated`) |
| `idempotency/key_in_flight` | 409 | Misma key con request en curso |
| `idempotency/key_reused_with_different_body` | 409 | Misma key, body distinto |
| `idempotency/malformed_key` | 400 | Header `Idempotency-Key` no es UUID v4 |
| `general/rate_limit_exceeded` | 429 | Demasiados requests del mismo SP |
| `general/maintenance` | 503 | Modo mantenimiento activado |
| `INTERNAL_ERROR` | 500 | Bug interno del ERP. Reintentar con backoff; usar `traceId` para soporte. |

> Retirados en v2.0.0:
> - `aw/correlation_field_no_configurado` — el polling worker ya no existe.
> - `aw/hybrid_connection_down` — el HC failure se reporta como `aw_processing_timeout` vía el path normal.
> - `aw/reintento_no_aplica` — consolidado en `aw_invalid_state_transition`.

---

## 7. Rate limiting

Política inicial (revisable al observar tráfico real):

| Endpoint | Límite por SP | Ventana |
|---|---|---|
| `POST /cotizaciones` | 60 | 1 min |
| `GET /cotizaciones/{ref}` | 600 | 1 min |
| `GET /cotizaciones` (listado) | 60 | 1 min |
| `POST /cotizaciones/{ref}/reintentar` | 10 | 1 min |

Excedido → `429 Too Many Requests` con headers `Retry-After` y `RateLimit-*`.

Glass Agent debería implementar backoff exponencial respetando `Retry-After`.

---

## 8. Auditoría y trazabilidad

Cada request del ERP genera entradas en:

- **Application Insights**: trace completo con `request_id`, `correlation_id`, `service_principal_name`, `operation_name`, `result_code`, duración.
- **Tabla `integraciones_aw.envio`**: registro de cada intento de drop con timestamp, status, error si lo hubo.
- **Outbox `integraciones_aw.outbox`**: eventos publicados a Service Bus.
- **Log Serilog estructurado**: con masking de campos sensibles (tokens, secretos).

Glass Agent del lado del PHP guarda en `glass_chat_log` (existente) el evento de envío con su `idempotency_key` y `quote_reference`, lo que permite end-to-end tracing: SupportBoard conversation → Glass Agent session → glass_orders → ERP quote → A+W doc_id.

---

## 9. Pendientes / TODOs

| TODO | Bloqueante para | Estado |
|---|---|---|
| ~~Confirmar campo en `pool_auftrag` para correlación~~ | ~~Polling worker~~ | ✅ **Resuelto en PR #199-#203**: el polling worker fue retirado; el doc id viene del log de A+W vía drop service callback (código `(4615)`). |
| ~~Decidir hub SignalR (compartido vs nuevo)~~ | ~~Notificaciones~~ | ✅ **Resuelto en PR #194**: hub dedicado `IntegracionesAwHub` (§5.1). |
| Política de retención de `edi_content` en BD | Diseño de migración futura | ⏳ Hoy se guarda siempre en `entidad_externa.edi_content` (text). Pendiente decisión sobre blob storage tras N días — no urgente con volumen actual. |
| Definir endpoint admin para auditoría EDI | Operación post-MVP | ⏳ Hoy solo accesible vía SQL directo a la BD del módulo. |
| Definir endpoint admin para purga de datos viejos | Operación post-MVP | ⏳ Cleanup manual / cron por ahora. |
| URL real para `type` URIs | Cosmético | No bloqueante. Los `type` son IDs lógicos (`aw_cotizacion_invalida`), no URLs navegables. |
| Reintegrar `polling_worker` para integraciones futuras | Otros consumidores (CFDI/Almacén/CxC) | ⏳ El `HybridConnectionAwSqlReader` y el puerto `IAwSqlReader` se quedan vivos en el código (D1 del CLAUDE.md) — listos para futuros workers que necesiten leer A+W vía SQL. |

---

## 10. Cross-references

- **`00-system-overview.md`** §3 (decisiones), §4 (componentes), §7 (flujos críticos), §8 (alineación a ADRs)
- **`02-edi-correlation.md`** — detalle del flujo asíncrono y eventos Service Bus
- **`runbooks/aw-customizing.md`** v2.0 — runbook para el customizing A+W que produce el `last_batch.log`
- **ADR-0007** RBAC con `PermisosCanonicos`
- **ADR-0009** Outbox pattern
- **ADR-0010** Problem Details
- **ADR-0012** ETag/If-Match
- **ADR-0020** Idempotency
- **ADR-0021** Versionado por path
- **ADR-0030** Multi-DbContext
- **PR #198** — contrato del callback `DropResult` + métodos del aggregate
- **PR #199** — drop service per-EDI con lanes
- **PR #201** — cableado backend + retiro del `AwCorrelationWorker`
- **PR #203** — parser content-based + single `last_batch.log`
- **PR #204** — runbook customizing v2.0

---

## 11. CHANGELOG

### v2.1.0 — 2026-05-21

**Hard cutover** — campo `sucursal` requerido en POST `/api/v1/integraciones/aw/cotizaciones`:

- Nuevo campo `sucursal` (string, required, uppercase 3 chars). Catálogo: `CIR` (CIRCUITO), `CHI` (CHICHI SUAREZ), `CAN` (CANCUN), `CON` (CONKAL). Case-insensitive (el ERP normaliza a uppercase). Fuera de catálogo → `400 AW_SUCURSAL_INVALIDA`. Ausente → `400 AW_SUCURSAL_REQUERIDA`.
- El response 202 ahora incluye `"sucursal"` como confirmación.
- El detail GET, listado y historial exponen `sucursal` en sus respuestas.
- **El ERP escribe el filename como `cot_<SUCURSAL>_<QUOTE_REFERENCE>.edi`** en el drop service on-prem. Cada customizing de A+W usa pattern matching (`cot_CIR_*`, etc.) para procesar solo su sucursal — un customizing solo puede crear pedidos para una sola sucursal por diseño A+W. Sin cambios en el drop service (filename routing).
- Notificaciones Soketi al Agent ahora incluyen `"sucursal"` en el payload (snake_case) para que la UI del operador pueda filtrar/agrupar por sucursal.

Migración para Agent (Glass Agent v2.6.0+):
1. Agregar `sucursal` (código corto) al body del POST de cotización.
2. Determinar el código en runtime según el sitio/instalación del Agent (config local o lookup).
3. Deploy coordinado con el ERP (hard cutover: cualquier POST sin `sucursal` recibe 400).

### v2.0.0 — 2026-05-18

**Breaking** (alineación a la implementación real post PR #198–#203):

- **JSON keys** ahora son `camelCase` (default de ASP.NET Core), no `snake_case` como proponía v1.0.
- **Schema del body** se aplanó: sin `customer{}` ni `metadata{}` anidados. Campos directos: `quoteReference`, `ediContent`, `customerTaxId`, `customerName`, `source`, `itemsCount`, `payloadOriginalJson`.
- **`estado`** del response es **integer** (no string), mapeado al enum `EstadoEntidad` del backend. Tabla en §4.1.
- **`AwaitingCorrelation`** (valor `1` del enum) **retirado** del state machine — el callback per-EDI transita `Submitted → Correlated|FailedCorrelation|FailedDrop` directo.
- **`GET /{quote_reference}`** reemplazado por **`GET /{id}`** (Guid). Para buscar por `quoteReference`, usar `GET /` con query param `quoteReference=...`.
- **Listado**: paginación es `total/offset/limit` plano (no `pagination.{...}` anidado ni `links.next/previous`).
- **Eventos del historial**: nombres del callback flow (`pedido_correlacionado`, `cotizacion_rechazada`, `drop_fallado_terminal`). Retirados los del polling viejo (`drop_intentado`, `correlacion_expirada`).
- **Error types**: retirados `aw/correlation_field_no_configurado` y `aw/hybrid_connection_down` (polling worker eliminado). Nuevo `aw_invalid_state_transition` (409) consolida fallos por transición inválida.

**Resuelto** (TODOs de v1.0):

- Hub SignalR decidido: `IntegracionesAwHub` (PR #194).
- Polling SQL on-prem reemplazado por callback per-EDI (PR #198–#203).
- Campo de correlación en `pool_auftrag`: ya NO necesario, el doc id viene del log de A+W via código `(4615)` parseado por el drop service.

**Sin cambios funcionales**:

- Auth flow (service principal Entra ID + `client_credentials`).
- Permisos canónicos del módulo.
- Rate limits (sin tráfico real aún para tunearlos).
- Idempotency semantics (ADR-0020).
- Problem Details RFC 7807 (ADR-0010).

### v1.0.0 — 2026-05-15

- Versión inicial (proposal antes de implementación).
- Definía polling SQL desde Azure como modelo de correlación.
- Schema propuesto en `snake_case` con `customer{}` y `metadata{}` anidados.
- Hub SignalR a decidir.
