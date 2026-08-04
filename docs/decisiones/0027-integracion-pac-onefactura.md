# ADR-0027: Integración con PAC fiscal — abstracción `IPacProvider` con OneFactura como primera implementación

- **Estado**: **Reemplazada por [ADR-0038](./0038-fiscalapi-pac-unico.md)** (2026-05-24)
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: fiscal, pac, integración, cliente-específico, tier-3

> **Nota (2026-05-24):** Esta ADR fue reemplazada porque Millet decidió
> adoptar **FiscalAPI** como PAC único para todo el ciclo fiscal
> (descarga + timbrado + cancelación + consulta), abandonando
> OneFactura. Ver [ADR-0038](./0038-fiscalapi-pac-unico.md) para la
> decisión vigente y la justificación.
>
> El contenido técnico que se preserva (construcción de XML del lado del
> ERP, manejo de CSD en Key Vault, errores normalizados, catálogos SAT)
> sigue siendo válido — solo cambia el vendor concreto y la abstracción
> deja de ser `IPacProvider` para consolidarse en `IFiscalApiClient`.

## Contexto y problema

El ERP debe emitir CFDIs (facturas, complementos de pago, notas de crédito,
cartas porte, etc.) en cumplimiento con el SAT. Esto requiere integración
con un **PAC** (Proveedor Autorizado de Certificación) que sella el TFD
(Timbre Fiscal Digital) y certifica el comprobante ante el SAT.

Millet ya tiene contrato activo con **OneFactura** y credenciales
configuradas. La integración debe cubrir:

- Timbrado de CFDIs 4.0 (factura, complemento de pago, nota de crédito)
- Cancelación de CFDIs (con flujo de aprobación post-CFDI 4.0)
- Consulta de estatus actual en SAT (vigente / cancelado)
- Emisión de complementos específicos (carta porte 3.x, etc.)
- Respuesta a solicitudes de cancelación recibidas (cuando un emisor cancela un CFDI a Millet)

Si el código del módulo Fiscal llama directamente a OneFactura, queda
acoplado: cualquier cambio de PAC en el futuro implica reescribir.
Considerando que (a) los PACs cambian de precios y SLAs, (b) puede haber
outages extendidos que justifiquen un secundario, y (c) el SAT cambia
schemas periódicamente, **la integración debe abstraerse desde día 1**.

## Drivers de la decisión

- Capacidad real de cambiar de PAC sin reescribir el módulo Fiscal
- Resiliencia ante outages (failover futuro a PAC secundario)
- Independencia del proveedor en la construcción del XML CFDI
- Auditoría completa de cada timbrado (qué, cuándo, con qué CSD, contra qué PAC)
- Seguridad de la CSD (Certificado de Sello Digital): si se filtra, alguien puede emitir CFDIs en nombre de Millet
- Manejo predecible de errores transitorios vs permanentes
- Soporte de complementos específicos (carta porte por requerimiento del cliente)

## Opciones consideradas

1. Capa de abstracción `IPacProvider` con OneFactura como primera implementación; XML construido del lado del ERP
2. Acoplamiento directo a OneFactura (sin abstracción)
3. Delegar construcción de XML al PAC (algunos PACs lo permiten)
4. Multi-PAC desde día 1 (primario + secundario simultáneos)

## Decisión

Se adopta la **opción 1**: abstracción `IPacProvider` con `OneFacturaPacProvider`
como primera implementación, **construcción del XML CFDI del lado del ERP**,
CSD almacenada en Azure Key Vault, errores normalizados a tipos neutros.

### Capa de abstracción `IPacProvider`

```csharp
public interface IPacProvider
{
    string ProviderName { get; }                    // "OneFactura"

    // Timbrado: recibe XML CFDI ya sellado por nosotros con la CSD del emisor;
    // retorna XML completo con TFD del PAC + sello SAT
    Task<TimbradoResult> TimbrarAsync(
        TimbradoRequest request, CancellationToken ct);

    // Cancelación: solicita al PAC iniciar el flujo de cancelación con SAT
    Task<CancelacionResult> SolicitarCancelacionAsync(
        CancelacionRequest request, CancellationToken ct);

    // Consulta de estatus actual en el SAT
    Task<EstatusCfdiResult> ConsultarEstatusAsync(
        ConsultaEstatusRequest request, CancellationToken ct);

    // Aceptar o rechazar una solicitud de cancelación recibida
    // (cuando un emisor solicita cancelar un CFDI dirigido a Millet)
    Task<RespuestaCancelacionResult> ResponderCancelacionAsync(
        RespuestaCancelacionRequest request, CancellationToken ct);
}
```

**Tipos de request/response son neutros** (no exponen detalles específicos
de OneFactura). Cualquier implementación traduce de/hacia el shape específico
del PAC en su capa interna.

### Implementaciones a crear

| Implementación                     | Cuándo                  | Propósito                                      |
|------------------------------------|-------------------------|-----------------------------------------------|
| `FakePacProvider`                  | Fase 1                  | Tests; retorna XML con TFD ficticio determinista |
| `OneFacturaPacProvider`            | Fase 4 (módulo Fiscal)  | Producción real                               |
| `OneFacturaSandboxPacProvider`     | Fase 4                  | Apunta al ambiente de pruebas de OneFactura   |
| `FailoverPacProvider` (composite)  | Cuando se justifique    | Wrapper que prueba primario, fallback al secundario |

### Construcción del XML CFDI del lado del ERP

**Decisión clave**: el ERP construye el `Comprobante` 4.0 al 100%, lo sella
con la CSD del emisor, y solo manda al PAC para que agregue el TFD.

**Razones**:
- **Independencia del PAC**: si cambiamos de proveedor, el XML que producimos sigue siendo válido
- **Control total** sobre catálogos SAT, validaciones, complementos
- **Performance**: validaciones locales no dependen de roundtrip al PAC
- **Auditoría más clara**: cada paso (construcción, sello, timbrado) es trazable y diagnosticable
- **Costo**: muchos PACs cobran extra por "ayuda" en construcción del XML

### Componentes del módulo Fiscal

```
backend/src/Fiscal/
├── Domain/
│   ├── Cfdi.cs                             -- entidad raíz
│   ├── Concepto.cs
│   ├── Impuesto.cs
│   ├── ComplementoCartaPorte.cs
│   └── ...
├── Application/
│   ├── Cfdi/
│   │   ├── ICfdiBuilder.cs                 -- construye XML 4.0 desde dominio
│   │   ├── CfdiBuilder.cs
│   │   ├── ICfdiSigner.cs                  -- firma XML con CSD del emisor
│   │   ├── CfdiSigner.cs
│   │   ├── ICfdiValidator.cs               -- validaciones locales pre-PAC
│   │   └── CfdiValidator.cs
│   ├── Pac/
│   │   ├── IPacProvider.cs
│   │   ├── PacException.cs
│   │   └── (DTOs neutros)
│   ├── Csd/
│   │   ├── ICsdProvider.cs                 -- lee CSD desde Key Vault
│   │   └── CsdProvider.cs
│   └── Pdf/
│       └── CfdiFacturaPdfDocument.cs       -- ADR-0025
└── Infrastructure/
    ├── Pac/
    │   ├── OneFactura/
    │   │   ├── OneFacturaPacProvider.cs
    │   │   ├── OneFacturaErrorMapper.cs
    │   │   ├── OneFacturaSoapClient.cs     -- (o REST según API real)
    │   │   └── ...
    │   └── Fake/
    │       └── FakePacProvider.cs
    └── ...
```

### Servicios de aplicación expuestos

```csharp
public interface IFacturacionService
{
    Task<CfdiTimbradoDto> TimbrarFacturaAsync(
        TimbrarFacturaCommand cmd, CancellationToken ct);

    Task<CfdiTimbradoDto> TimbrarComplementoPagoAsync(
        TimbrarComplementoPagoCommand cmd, CancellationToken ct);

    Task<CfdiTimbradoDto> TimbrarNotaCreditoAsync(
        TimbrarNotaCreditoCommand cmd, CancellationToken ct);

    Task<CfdiTimbradoDto> TimbrarCartaPorteAsync(
        TimbrarCartaPorteCommand cmd, CancellationToken ct);

    Task<CancelacionDto> SolicitarCancelacionAsync(
        SolicitarCancelacionCommand cmd, CancellationToken ct);

    Task<RespuestaCancelacionDto> ResponderCancelacionRecibidaAsync(
        ResponderCancelacionCommand cmd, CancellationToken ct);

    Task<EstatusCfdiDto> ConsultarEstatusEnSatAsync(
        Guid cfdiId, CancellationToken ct);
}
```

### Flujo completo de timbrado

```
1. Usuario: clic "Timbrar" en frontend
   POST /api/fiscal/cfdis/{id}/timbrar
   Headers: Idempotency-Key (ADR-0020)

2. Endpoint: [RequirePermission("fiscal.cfdi.timbrar")] valida permisos

3. Handler:
   a. Valida estado del CFDI (debe ser 'borrador'; lock duro de ADR-0012)
   b. Valida datos completos (cliente con RFC válido, conceptos, impuestos)
   c. ICfdiValidator.Validate() ejecuta validaciones locales pre-PAC

4. ICfdiBuilder.Build():
   - Construye XML <cfdi:Comprobante> 4.0 desde la entidad Cfdi
   - Incluye complementos según tipo (carta porte si aplica)
   - Aplica catálogos SAT (claves, regímenes, monedas, formas de pago)
   - Calcula cadena original

5. ICfdiSigner.Sign():
   - Lee CSD del emisor desde Key Vault (ICsdProvider)
   - Firma la cadena original → produce sello digital
   - Inserta sello en el XML

6. IPacProvider.TimbrarAsync():
   - Implementación OneFactura traduce a su API
   - Polly: retry 3 veces con backoff exponencial en errores transitorios
   - Si éxito: retorna XML con TFD + sello SAT
   - Si error: traduce a PacException (Tipo: Transitorio | Validacion | Permanente)

7. Persistencia (en transacción):
   - Sube XML completo a Blob 'cfdis-xml' (immutability 5 años, ADR-0024)
   - Crea fila en core.documentos
   - Actualiza entidad Cfdi: uuid, fecha_timbrado, sello_sat, no_certificado_sat, status='timbrado'
   - Evento CfdiTimbradoEvent en outbox (ADR-0009)
   - Audit log con CSD thumbprint usado (ADR-0008)

8. Respuesta al frontend:
   - 200 OK con CfdiTimbradoDto (uuid, folio, fecha)
   - SignalR notifica a otros usuarios viendo el mismo CFDI

9. Post-timbrado (asíncrono vía evento CfdiTimbradoEvent):
   - Generar PDF (ADR-0025) y guardar en Blob
   - Enviar por email al cliente si se solicitó (ADR-0026)
```

### Manejo de la CSD (Certificado de Sello Digital)

La CSD es lo más sensible del sistema fiscal:
- Es el certificado privado del emisor, emitido por el SAT
- Si se filtra, alguien puede emitir CFDIs en nombre de la empresa
- Tiene vigencia (4 años típicamente); su renovación es operación regulada

**Almacenamiento**:

- Cada empresa sube su `.cer` y `.key` + password vía endpoint admin
- El backend combina `.cer` + `.key` en un `.pfx` y lo guarda como **Certificate** en Azure Key Vault
- El password del `.key` original se descarta (el `.pfx` queda con password manejado por Key Vault)
- Solo el rol con permiso `compartido.csd.gestionar` puede subirlas
- Cero acceso desde frontend una vez subido (no hay endpoint de "descargar mi CSD")

**Lectura en runtime**:

- `ICsdProvider.GetCsdAsync(empresaId)` lee desde Key Vault con managed identity del App Service
- Cache in-memory por empresa con TTL corto (15 min) para evitar lectura repetida
- La CSD nunca se escribe a logs ni a disco

**Auditoría**:
- Cada vez que se sella un XML con la CSD, queda registrado en `audit_log`:
  - `entidad = 'cfdi'`
  - `entidad_id = cfdi.id`
  - `metadatos.csd_thumbprint = '...'`
  - `metadatos.no_certificado = '...'`
- Permite trazabilidad fiscal (qué CSD se usó para qué CFDI) y detección de uso indebido

**Vencimiento**:
- Job diario `CsdExpiryCheckJob` (ADR-0022) revisa CSDs próximas a vencer
- Notifica a admins por email (ADR-0026) con 60, 30, 7 días de anticipación
- Si una CSD vence: bloquear timbrado para esa empresa con error claro hasta que se suba renovada

### Configuración por empresa

Tabla `compartido.empresa_pac_config`:

```
empresa_pac_config
├── empresa_id (uuid, PK, FK compartido.empresas)
├── pac_provider (text, not null)             -- 'onefactura' | 'fake'
├── ambiente (text, not null)                 -- 'sandbox' | 'produccion'
├── credenciales_keyvault_secret_name (text)  -- nombre del secret en KV
├── csd_keyvault_certificate_name (text)      -- nombre del cert en KV
├── activo (bool, not null, default true)
├── created_at, updated_at
```

Las credenciales reales (usuario/password de OneFactura, o token API) **NO viven en BD**:
solo referencias al Key Vault. El backend usa managed identity para
resolverlas en runtime.

### Manejo de errores normalizado

`OneFacturaPacProvider` traduce errores específicos del PAC a excepciones
neutras que el módulo Fiscal entiende:

```csharp
public class PacException : Exception
{
    public string Codigo { get; }                  // 'CFDI_DUPLICADO', 'RFC_INVALIDO', 'CSD_VENCIDA'
    public string CodigoOriginalPac { get; }       // código exacto que dio OneFactura
    public PacExceptionTipo Tipo { get; }
    public string? RawResponse { get; }            // respuesta cruda del PAC para debugging
}

public enum PacExceptionTipo
{
    Transitorio,    // network, timeout, 5xx → reintentar con Polly
    Validacion,     // datos del CFDI inválidos → 422 al usuario, NO reintentar
    Permanente      // CSD vencida, contrato suspendido → 503 + alertar admin, NO reintentar
}
```

**Ejemplos de mapeo OneFactura → neutro**:

| Código OneFactura | Código neutro          | Tipo         |
|-------------------|------------------------|--------------|
| `301`             | `XML_INVALIDO`         | Validacion   |
| `302`             | `RFC_NO_REGISTRADO`    | Validacion   |
| `307`             | `CFDI_DUPLICADO`       | Validacion   |
| `IO_ERROR_*`      | `PAC_NO_DISPONIBLE`    | Transitorio  |
| `AUTH_FAILED`     | `CONTRATO_SUSPENDIDO`  | Permanente   |
| `CSD_VENCIDA`     | `CSD_VENCIDA`          | Permanente   |

### Reintentos con Polly

```csharp
services.AddHttpClient<OneFacturaSoapClient>()
    .AddPolicyHandler(GetRetryPolicy());

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(r => (int)r.StatusCode >= 500)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt =>
                TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            onRetry: (_, timespan, attempt, context) =>
                logger.LogWarning(
                    "OneFactura intento {Attempt} fallido, reintentando en {Delay}",
                    attempt, timespan));
}
```

Después del 3er intento fallido: la `PacException(Tipo=Transitorio)` sube
al handler, retorna 502 al usuario con código `PAC_NO_DISPONIBLE`. El
usuario puede reintentar manualmente; con `Idempotency-Key` (ADR-0020)
no se duplican timbrados.

### Métricas y observabilidad (alineado con ADR-0006)

- `pac.timbrado.duration_ms{pac=onefactura}` — histograma
- `pac.timbrado.success_total{pac, tipo_cfdi}` — contador
- `pac.timbrado.failures_total{pac, codigo, tipo}` — contador
- `pac.cancelacion.duration_ms`, `pac.cancelacion.success_total`
- `pac.consulta_estatus.duration_ms`

**Alertas**:
- Tasa de fallos > 10% sostenida 5 min: P1 (algo grave con OneFactura o nuestro código)
- Tiempo p95 de timbrado > 10s: P2 (degradación)
- Cualquier `PacException(Tipo=Permanente)`: P1 inmediato (contrato/CSD)

### Failover futuro (PAC secundario)

NO se implementa de inicio. La arquitectura lo permite cuando se justifique:

```csharp
public class FailoverPacProvider : IPacProvider
{
    private readonly IPacProvider _primary;
    private readonly IPacProvider _secondary;
    private readonly ILogger<FailoverPacProvider> _logger;

    public async Task<TimbradoResult> TimbrarAsync(
        TimbradoRequest request, CancellationToken ct)
    {
        try
        {
            return await _primary.TimbrarAsync(request, ct);
        }
        catch (PacException ex) when (ex.Tipo == PacExceptionTipo.Transitorio)
        {
            _logger.LogWarning(
                "Primary PAC failed, failing over to secondary: {Error}",
                ex.Message);
            return await _secondary.TimbrarAsync(request, ct);
        }
    }
}
```

Cuando se contrate un PAC secundario (después de algún incidente o por
requerimiento de continuidad), se cambia el registro de DI y listo.

### Carta porte 3.x

Carta porte es un complemento que acompaña a CFDIs cuando hay transporte
de bienes. `ICfdiBuilder` debe soportarlo:

**Schema** del complemento (resumen):
- Mercancías (lista con descripción, peso, fracción arancelaria, claves SAT)
- Ubicaciones (origen, destino, distancia)
- Autotransporte (placas, configuración vehicular, seguros, permisos SCT)
- Conductor (RFC, licencia, datos)

**Catálogos SAT específicos** que el ERP debe mantener:
- Configuraciones vehiculares
- Tipos de embalaje
- Materiales peligrosos
- Subtipos de remolque
- Tipos de permiso SCT

**Validaciones específicas**:
- Si distancia > X km: requiere ciertos campos adicionales
- Mercancía peligrosa: campos de embalaje obligatorios
- Peso bruto coherente con conceptos del CFDI

**PDF específico**: representación impresa con secciones de carta porte
(ADR-0025).

### Lo que NO incluye este ADR

- **Descarga de CFDIs recibidos** (parte de Validex): ADR aparte cuando se
  clarifique qué es Validex
- **Renovación automática de CSD**: las CSD vencen cada 4 años; cuando
  se acerque el primer vencimiento, ADR específico
- **PAC secundario en producción**: cuando se decida contratar uno
- **Otros complementos** (notarios, divisas, vales de despensa, donatarias,
  IEDU, etc.): se agregan según se requieran, mismo patrón
- **Construcción de XML para versiones anteriores a 4.0**: solo CFDI 4.0
  (es lo vigente; transición histórica fue en 2022)
- **Generación del PDF del CFDI**: ya cubierto en ADR-0025
- **Envío del CFDI al cliente por email**: ya cubierto en ADR-0026

## Consecuencias

**Positivas**
- Cambio de PAC posible en días, no meses (módulo Fiscal no se reescribe)
- XML construido por el ERP: control total, independencia, mejor auditoría
- CSD segura en Key Vault con auditoría de uso
- Errores normalizados: el módulo Fiscal no conoce códigos específicos de OneFactura
- Resiliencia ante outages transitorios con Polly
- Failover preparado arquitectónicamente para cuando se justifique
- Carta porte y otros complementos siguen el mismo patrón
- Métricas detalladas para detectar degradación del PAC tempranamente

**Negativas**
- Construir XML completo en el ERP es trabajo significativo: validaciones, catálogos SAT, schemas. Aceptable: el control vale el esfuerzo
- Catálogos SAT (productos/servicios, unidades, regímenes, claves de carta porte, etc.) deben mantenerse sincronizados con el SAT. Mitigado: job nocturno que descarga catálogos del SAT periódicamente
- Cada CFDI nuevo requiere construcción + sello + timbrado, latencia ~1-3 segundos típica. Aceptable
- Pruebas E2E con OneFactura sandbox son lentas. Mitigado: tests usan `FakePacProvider` mayoritariamente; sandbox solo para tests de integración con OneFactura específicamente

## Descartadas

**Acoplamiento directo a OneFactura**. Tentador por simplicidad inicial,
pero el costo futuro del cambio (cuando inevitablemente surja) supera
ampliamente el costo de la abstracción ahora.

**Delegar construcción de XML al PAC**. Algunos PACs ofrecen "tú me das
los datos, yo construyo el XML y lo timbro". Pros: menos código.
Contras importantes:
- Encadena el formato de los datos al PAC: cambiar implica reescribir
- Catálogos SAT viven del lado del PAC; si necesitamos validación local, no podemos
- Suelen cobrar más
- Pierde auditoría granular de la construcción
Descartado.

**Multi-PAC desde día 1**. Tiene sentido para empresas con volumen
crítico (miles de CFDIs/hora) donde un outage es inaceptable. Para Millet
en su fase inicial: sobre-engineering. La arquitectura lo permite cuando
se justifique.

## Notas de implementación

**Backend**

- Crear interfaces y DTOs en `Fiscal/Application/Pac/`
- Crear `FakePacProvider` desde Fase 1 (necesario para tests)
- `OneFacturaPacProvider` se implementa cuando se aborde Fase 4 (módulo Fiscal real)
- Agregar paquetes a `Directory.Packages.props`:
  - `Polly` y `Microsoft.Extensions.Http.Polly`
  - SDK de OneFactura si existe; si no, `System.ServiceModel.Http` para SOAP o `HttpClient` para REST
  - `System.Security.Cryptography.X509Certificates` (built-in)
- Configurar `services.AddHttpClient<OneFacturaSoapClient>()` con políticas de Polly
- Implementar `ICsdProvider` con cache in-memory de TTL 15min
- Endpoint admin para subir CSD: `POST /api/admin/empresas/{id}/csd` (multipart con `.cer`, `.key`, password)

**Bicep**

- Key Vault ya existe (Fase 1); agregar:
  - Access policies para certificates (gestión de CSD)
  - Managed Identity del App Service con permiso `Get` sobre certificates y secrets correspondientes
- Variables de configuración por ambiente:
  - `Pac:DefaultProvider = "onefactura"` (o `"fake"` en dev)
  - `Pac:OneFactura:Endpoint = "..."`

**Catálogos SAT**

- Tabla `compartido.catalogo_sat_*` con seed inicial (descargado del SAT)
- Job mensual `CatalogosSatSyncJob` (ADR-0022) que descarga catálogos
  actualizados y los actualiza
- Cuando un catálogo cambia (raro), notificar a admins

**Frontend**

- Pantalla `Configuración fiscal` por empresa (admin):
  - Subir CSD
  - Configurar PAC (dropdown con providers disponibles)
  - Ambiente sandbox vs producción
  - Probar conexión (botón que hace timbrado de prueba contra `FakePacProvider` o sandbox real)
- En timbrado: feedback claro de errores (códigos neutros con mensajes en español)
- Mostrar fecha de vencimiento de CSD en dashboard

**Tests**

- `FakePacProvider` retorna XML válido con TFD ficticio determinista
- Unit tests de `CfdiBuilder` con casos representativos (factura simple, con descuentos, con varios impuestos, carta porte)
- Tests de mapeo `OneFacturaErrorMapper`: cada código original → neutro esperado
- Snapshot tests del XML generado (Verify, ADR-0016): detectar regresiones en construcción
- Integration tests con sandbox de OneFactura (en pipeline nocturno, no en cada PR)

**Documentación en `CLAUDE.md`**

- Cómo agregar un nuevo tipo de complemento al `CfdiBuilder`
- Cómo subir/renovar CSD
- Cómo cambiar de PAC (registro de DI)
- Cómo manejar errores del PAC en handlers
- Lista de códigos de error neutros y su significado

**Permisos** (alineados con ADR-0007)

- `fiscal.cfdi.timbrar`
- `fiscal.cfdi.cancelar.solicitar`
- `fiscal.cfdi.cancelar.aprobar`
- `fiscal.cfdi.consultar_estatus`
- `compartido.csd.gestionar` (subir/cambiar CSD)
- `fiscal.pac.configurar` (cambiar PAC, ambiente)

**ADRs hijo posibles**

- Estrategia de renovación de CSD (cuando se acerque el primer vencimiento)
- Contratación e implementación de PAC secundario (cuando se justifique)
- Soporte para complementos adicionales según los requiera el negocio
- Política de retención de XMLs históricos más allá de los 5 años SAT
