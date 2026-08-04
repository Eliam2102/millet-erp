# ADR-0026: Notificaciones por email con Azure Communication Services Email

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: email, notificaciones, infraestructura, tier-3

## Contexto y problema

El ERP necesita enviar correos por varias razones operativas:

- **Transaccionales**: enviar CFDI a cliente, complemento de pago, nota de crédito
- **Notificaciones del sistema**: reporte generado disponible, documento rehidratado del Archive, job batch completado
- **Alertas administrativas**: errores críticos, eventos de seguridad
- **Recordatorios**: vencimientos de cartera, renovaciones (cuando aplique)

Sin servicio definido, los devs tienden a soluciones que parecen simples
pero son frágiles operativamente: SMTP propio (pesadilla de
deliverability), enviar desde el email del usuario (problemas de SPF/DKIM),
o agregar un proveedor externo cualquiera sin pensar en integración con
el resto del stack.

Necesitamos definir proveedor, arquitectura de envío (con garantías de
entrega y trazabilidad), plantillas, y manejo de bounces/errores.

## Drivers de la decisión

- Deliverability: que los emails efectivamente lleguen (no terminen en spam)
- Integración con el stack Azure existente (Bicep, Key Vault, Application Insights)
- Garantía de entrega: si el envío falla, debe reintentarse; nunca perder un email transaccional
- Trazabilidad: saber quién recibió qué y cuándo
- Adjuntos eficientes: PDFs y XMLs de varios MB sin saturar la BD
- Costo razonable
- Privacidad: no activar tracking innecesario por default

## Opciones consideradas

1. Azure Communication Services (ACS) Email
2. SendGrid
3. Mailgun
4. AWS SES
5. SMTP propio en App Service Mail Add-on
6. Microsoft 365 / Exchange Online via Graph API

## Decisión

Se adopta **Azure Communication Services Email** como proveedor único, con
arquitectura de outbox para garantizar entrega, plantillas como código en
C#, y adjuntos referenciando `core.documentos` (ADR-0024).

### Servicio: ACS Email

**Razones**:
- Mismo cloud que el resto del stack: Bicep configura el dominio, KeyVault guarda connection string, Application Insights captura métricas/logs
- Mexico Central tiene ACS Email disponible (alineado con ADR-0006)
- Billing unificado con el resto de Azure
- DKIM automatic provisioning al verificar dominio
- Event Grid integration para tracking de delivery/bounce/engagement

**No se eligió SendGrid** a pesar de ser maduro porque:
- Vendor adicional que mantener (cuenta separada, billing aparte, IPs aparte)
- ACS Email cumple las mismas funciones con integración nativa
- Soporte de Microsoft tiene SLA contractual con Millet vía el resto de Azure

### Categorías de email

Esta ADR cubre:
- **Transaccionales operativos**: envío de CFDI/pago/nota a clientes
- **Notificaciones del sistema**: reporte listo, documento rehidratado, job batch completado
- **Alertas administrativas**: errores críticos a admins, eventos de seguridad
- **Recordatorios automáticos**: vencimientos, renovaciones

NO cubre (fuera del alcance):
- Marketing automation
- Newsletters masivos
- Campañas promocionales
- Inbound email parsing

### Servicio `IEmailSender`

```csharp
public interface IEmailSender
{
    Task<EnviarEmailResult> SendAsync(
        EnviarEmailRequest request,
        CancellationToken ct);
}

public record EnviarEmailRequest(
    string Plantilla,                          // 'cfdi-enviado-cliente', etc.
    Dictionary<string, object> Datos,          // contexto para la plantilla
    EmailDestinatario Destinatario,
    EmailDestinatario? ReplyTo = null,
    Guid? EmpresaId = null,                    // null para emails de sistema
    List<EmailAdjunto>? Adjuntos = null,
    string? IdempotencyKey = null,
    EmailPrioridad Prioridad = EmailPrioridad.Normal);

public record EmailDestinatario(string Email, string? Nombre = null);

public record EmailAdjunto(
    Guid DocumentoId,                          // referencia a core.documentos
    string? NombreSugerido = null);

public enum EmailPrioridad { Baja, Normal, Alta }

public record EnviarEmailResult(
    Guid EmailId,                              // ID de la fila en email_outbox
    EmailStatus Status);                       // 'queued' siempre al inicio
```

### Flujo de envío con outbox

`SendAsync` **NO envía directamente** a ACS. Inserta en outbox para
garantizar entrega:

```
1. Handler de negocio invoca _emailSender.SendAsync(request)
2. SendAsync valida (plantilla existe, destinatario válido, etc.)
3. SendAsync inserta fila en core.email_outbox:
   - status='pending', attempts=0
   - serializa request a la fila
   - empresa_id, correlation_id (TraceId)
4. SaveChanges (transacción común con la operación de negocio que disparó
   el envío; si la operación se hace rollback, el email también)
5. EmailSenderWorker (hosted service, ADR-0022) hace polling cada 10s:
   - SELECT FROM email_outbox
     WHERE status='pending' AND attempts < 5
     ORDER BY prioridad DESC, created_at ASC
     LIMIT 50 FOR UPDATE SKIP LOCKED
6. Por cada email:
   a. Resuelve plantilla y renderiza HTML + texto
   b. Resuelve adjuntos (descarga del Blob via IDocumentStorage)
   c. Llama a EmailClient.SendAsync de ACS
   d. Si éxito: status='sent', sent_at=now(), guarda message_id de ACS
   e. Si error transitorio (red, throttling): attempts++, last_error=msg,
      próximo intento con backoff (5min, 15min, 1h, 4h, 24h)
   f. Si error permanente (email malformado, dominio inexistente):
      status='failed', failed_at=now()
7. Si attempts alcanza 5 sin éxito: status='failed', requiere intervención
   manual desde UI admin
```

### Tabla `core.email_outbox`

```
email_outbox
├── id (uuid v7, PK)
├── empresa_id (uuid, FK compartido.empresas, nullable)  -- null para emails de sistema
├── plantilla (text, not null)
├── datos (jsonb, not null)                  -- contexto para la plantilla
├── destinatario_email (text, not null)
├── destinatario_nombre (text, nullable)
├── reply_to_email (text, nullable)
├── reply_to_nombre (text, nullable)
├── adjuntos_documento_ids (uuid[], nullable)-- FK lógicas a core.documentos
├── prioridad (text, not null, default 'normal')  -- 'baja' | 'normal' | 'alta'
├── idempotency_key (text, nullable)
├── status (text, not null, default 'pending')
├── attempts (int, not null, default 0)
├── last_error (text, nullable)
├── acs_message_id (text, nullable)          -- ID que retorna ACS al enviar
├── correlation_id (uuid, not null)
├── created_at (timestamptz, not null)
├── sent_at (timestamptz, nullable)
├── failed_at (timestamptz, nullable)
├── delivered_at (timestamptz, nullable)     -- de Event Grid
├── bounced_at (timestamptz, nullable)
└── bounce_reason (text, nullable)

INDEX (status, prioridad, created_at) WHERE status='pending'
INDEX (idempotency_key) WHERE idempotency_key IS NOT NULL
INDEX (acs_message_id) WHERE acs_message_id IS NOT NULL
```

### Idempotencia

Si un caller proporciona `IdempotencyKey`, antes de insertar se verifica
unicidad: si ya existe fila con esa key, se retorna el resultado existente
sin enviar duplicado.

Útil para casos donde el handler de negocio se reintenta (ej. el outbox de
eventos de integración - ADR-0009 - reintenta el handler que dispara el
envío de CFDI por email).

### Plantillas como código

Plantillas viven en `Shared/Application/Email/Templates/`. Cada una es
una clase que implementa `IEmailTemplate`:

```csharp
public interface IEmailTemplate
{
    string Name { get; }                      // 'cfdi-enviado-cliente'
    string Subject(EmailDatos datos);
    string RenderHtml(EmailDatos datos);
    string RenderText(EmailDatos datos);      // fallback texto plano
    Type DatosType { get; }                   // para validación type-safe
}
```

**Ejemplo**:

```csharp
public class CfdiEnviadoClienteTemplate : EmailTemplateBase<CfdiEnviadoClienteDatos>
{
    public override string Name => "cfdi-enviado-cliente";

    public override string Subject(CfdiEnviadoClienteDatos datos) =>
        $"Factura {datos.Folio} de {datos.NombreEmpresa}";

    public override string RenderHtml(CfdiEnviadoClienteDatos datos) =>
        EmailHtmlBuilder.Create()
            .EmpresaHeader(datos.Empresa)
            .Saludo($"Estimado(a) {datos.NombreCliente}")
            .Parrafo("Adjunto encontrarás tu factura electrónica.")
            .TablaDatos(new[]
            {
                ("Folio fiscal", datos.UuidFiscal),
                ("Folio interno", datos.Folio),
                ("Fecha de emisión", datos.FechaFormateada),
                ("Total", datos.TotalFormateado)
            })
            .Parrafo("Si tienes alguna duda sobre este comprobante, " +
                     "responde a este correo y te atenderemos.")
            .Despedida($"Equipo de {datos.Empresa.Nombre}")
            .Build();

    public override string RenderText(CfdiEnviadoClienteDatos datos) =>
        $"Estimado(a) {datos.NombreCliente},\n\n" +
        $"Adjunto encontrarás tu factura electrónica.\n\n" +
        $"Folio fiscal: {datos.UuidFiscal}\n" +
        $"Folio interno: {datos.Folio}\n" +
        $"Fecha: {datos.FechaFormateada}\n" +
        $"Total: {datos.TotalFormateado}\n\n" +
        $"Equipo de {datos.Empresa.Nombre}";
}

public record CfdiEnviadoClienteDatos(
    EmpresaInfo Empresa,
    string NombreCliente,
    string UuidFiscal,
    string Folio,
    string FechaFormateada,
    string TotalFormateado);
```

**`EmailHtmlBuilder`** es un helper que produce HTML responsive con
estilos inline (compatibilidad con Outlook, Gmail, Apple Mail, etc.).
Genera estructura de tabla anidada (patrón estándar en email HTML para
máxima compatibilidad). Vivienda: `Shared/Application/Email/Builders/`.

### Type safety en plantillas

`EmailTemplateBase<TDatos>` valida en runtime que el `Datos` que llega en
`EnviarEmailRequest` sea del tipo esperado:

```csharp
public abstract class EmailTemplateBase<TDatos> : IEmailTemplate
    where TDatos : class
{
    public Type DatosType => typeof(TDatos);

    public string Subject(EmailDatos datos) => Subject(datos.As<TDatos>());
    public string RenderHtml(EmailDatos datos) => RenderHtml(datos.As<TDatos>());
    // ...

    public abstract string Subject(TDatos datos);
    public abstract string RenderHtml(TDatos datos);
}
```

Si un caller pasa datos del tipo equivocado, error claro en el render
(no después en producción).

### No usar Liquid / Handlebars / motor de templates externo

Razones:
- Plantillas son pocas (~30 en todo el ERP) y estables
- Type safety: si una plantilla espera un campo y el dev no lo pasa, error en compile-time (vía records C#)
- Refactor seguro: renombrar un campo del modelo se propaga a la plantilla
- Cero parsing en runtime
- Mensajes en español hardcoded sin layer de traducción innecesaria

Si en el futuro las plantillas se vuelven editables por usuarios finales
(comunicaciones personalizadas por cliente), se evaluará Liquid u otro
motor — ADR aparte.

### Adjuntos por referencia

Los adjuntos NO son bytes en `email_outbox.datos` (un PDF de 2 MB en jsonb
es ineficiente). Son **referencias a `core.documentos`** (ADR-0024):

```csharp
new EmailAdjunto(
    DocumentoId: cfdiPdfDocumento.Id,
    NombreSugerido: $"factura-{folio}.pdf");
```

Cuando el worker procesa el email:
1. Lee `email_outbox` con `adjuntos_documento_ids`
2. Por cada documento_id: descarga del Blob con
   `IDocumentStorage.DownloadStreamAsync`
3. Construye el mensaje ACS con los attachments inline
4. Envía

**Beneficio**: la fila del outbox queda chica (~1 KB), eficiente para
queries y particionado. El blob ya está en Storage (probablemente Hot
o Cool, ADR-0024).

### Configuración del remitente

**Remitente único por empresa**: `noreply@{dominio-empresa-millet}.com.mx`.

**No usar el email del usuario** que dispara la acción (ej.
`juan@millet.com.mx`) porque:
- Genera problemas de SPF/DKIM (parece spoofing al receptor)
- Si el usuario sale de la empresa, los correos enviados quedan huérfanos en respuestas
- ACS no permite enviar desde dominios no verificados

**Reply-To** sí puede ser el email del usuario que disparó la acción, para
que respuestas lleguen a la persona correcta. Configurable por plantilla.

**Tabla de configuración**: `compartido.empresa_email_config`

```
empresa_email_config
├── empresa_id (uuid, PK, FK compartido.empresas)
├── dominio_remitente (text)              -- 'millet.com.mx'
├── email_remitente (text)                -- 'noreply@millet.com.mx'
├── nombre_remitente (text)               -- 'Grupo Millet - ERP'
├── acs_domain_resource_id (text)         -- ID del dominio configurado en ACS
├── activo (bool, default true)
└── ...
```

### SPF, DKIM, DMARC

Configuración a nivel DNS del dominio:

- **SPF**: `v=spf1 include:spf.protection.azure.com -all`
- **DKIM**: ACS provee public keys al verificar dominio; dos registros TXT (`selector1._domainkey` y `selector2._domainkey`)
- **DMARC**: `v=DMARC1; p=quarantine; rua=mailto:dmarc@millet.com.mx`

**Documentar en `CLAUDE.md`** el setup completo para ambientes nuevos
(QA, otros tenants si surgen).

### Tracking de delivery

ACS publica eventos vía Event Grid:
- `EmailDeliveryReportReceived` — entregado, rechazado, bounced
- `EmailEngagementTrackingReportReceived` — abierto, link clickeado (solo si tracking activado)

**Webhook subscriber** en el ERP: endpoint `POST /api/internal/email-events`
que ACS llama (autenticado con shared key).

Por cada evento, actualiza la fila en `core.email_outbox`:
- `delivered_at`, `bounced_at`, `bounce_reason`

**Bounces hard**: si un email rebota como permanente (dominio
inexistente, mailbox no existe), el sistema:
- Marca el email del destinatario como `email_invalido` en el cliente/proveedor correspondiente
- NO intenta enviar nuevamente a ese email hasta que un usuario lo verifique manualmente
- Notifica al usuario que envió la solicitud original

### Engagement tracking (opens, clicks)

**Desactivado por default** en todas las plantillas. Razones:
- Privacidad: el tracking implica pixel invisible y URL rewriting que filtran información del receptor
- Algunos clientes corporativos lo bloquean activamente
- Compliance LFPDPPP: trackear sin consentimiento explícito es área gris

Activable solo por plantilla específica si surge necesidad operativa
(ej. enviar un reporte y querer saber si lo leyeron). Decisión por
plantilla, no global.

### Plantillas iniciales (a implementar conforme se requieran)

| Plantilla                           | Trigger                                          |
|-------------------------------------|--------------------------------------------------|
| `cfdi-enviado-cliente`              | Usuario timbra CFDI y solicita envío             |
| `complemento-pago-enviado-cliente`  | Usuario aplica pago y solicita envío             |
| `nota-credito-enviada-cliente`      | Cancelación o ajuste                             |
| `reporte-generado`                  | User job de reporte completa (ADR-0022)          |
| `documento-rehidratado`             | Blob de Archive disponible (ADR-0024)            |
| `usuario-creado-bienvenida`         | Admin crea usuario nuevo                         |
| `error-critico-admin`               | Sistema detecta error crítico                    |
| `cierre-mensual-completado`         | Job de cierre completa                           |
| `vencimiento-cartera-recordatorio`  | Cuenta por cobrar próxima a vencer (cuando exista módulo) |

### Endpoints administrativos

- `GET /api/admin/emails` (permiso `admin.emails.leer`): cola de emails pendientes/sent/failed
- `GET /api/admin/emails/{id}`: detalle (incluye render previo de plantilla)
- `POST /api/admin/emails/{id}/reintentar`: forzar reintento de un failed
- `POST /api/admin/emails/{id}/cancelar`: cancelar un pending

Pantalla de admin en frontend que consume estos endpoints para visibilidad
operativa.

### Política de retención

Tabla `core.email_outbox` crece con cada email enviado. Política:

| Estado     | Retención      |
|------------|----------------|
| `sent` con `delivered_at`     | 90 días     |
| `sent` sin `delivered_at`     | 90 días     |
| `failed`                       | 1 año (para investigación)   |
| `bounced`                      | 1 año                         |

Job nocturno (alineado con ADR-0022) borra registros que excedan política.
Antes de borrar emails con adjuntos: verificar que los `documentos`
referenciados no sean exclusivos de ese email; si son compartidos con
otras filas (cosa rara pero posible), no se borran.

### Observabilidad

**Métricas custom (App Insights, ADR-0006)**:
- `email.sent_total` por plantilla
- `email.delivery_lag_seconds` (sent_at → delivered_at)
- `email.failed_total` por plantilla y razón
- `email.bounced_total` por dominio destinatario (alertar si crece anormalmente)
- `email.queue_depth_pending` (alerta si > 500 sostenido)

**Logs estructurados**:
- Cada envío logea con `email_id`, `correlation_id`, `plantilla`, `destinatario_dominio` (no el email completo por privacidad)

**Alertas**:
- Si `email.queue_depth_pending` > 500 por más de 15 min: P2
- Si tasa de bounce > 5% en una hora: P2 (problema de deliverability)
- Si EmailSenderWorker no procesa en 10 min: P1

### Lo que NO incluye

- **Marketing automation**: no es objetivo
- **Newsletters masivos** (>1000 destinatarios): si surge, ADR aparte (cuotas, throttling, segmentación)
- **Templates editables por usuarios finales**: si surge, evaluar Liquid/Handlebars
- **Inbound email parsing**: no es objetivo
- **A/B testing de templates**: no es objetivo
- **Personalización avanzada**: solo nombre del destinatario y datos básicos
- **Templates con dark mode**: opcional, evaluar cuando se justifique
- **SMS notifications**: si surge necesidad, ADR aparte (ACS también soporta SMS)
- **WhatsApp Business**: si surge necesidad, vendor especializado

## Consecuencias

**Positivas**
- Garantía de entrega vía outbox: si el envío falla transitoriamente, se reintenta automáticamente
- Trazabilidad completa: cada email tiene fila con histórico de attempts y delivery
- Adjuntos eficientes: referencia a Blob, no bytes en BD
- Type safety en plantillas: errores de campos en compile-time
- Integración nativa Azure: monitoring, billing, secrets unificados
- Tracking de bounces protege la reputación del dominio (no insistir con emails inválidos)
- Privacidad por default: no engagement tracking sin decisión explícita

**Negativas**
- Plantillas en código requieren PR para cambios. Aceptable: cambian rara vez y consistencia visual es importante
- Latencia entre `SendAsync` y delivery real: típicamente segundos a minutos. Aceptable para emails transaccionales (no son real-time como SignalR)
- ACS Email es relativamente nuevo (GA 2023): comunidad menor que SendGrid. Mitigado por soporte oficial Microsoft
- Configuración inicial DNS (SPF/DKIM/DMARC) por dominio nuevo es trabajo manual

## Descartadas

**SendGrid**. Maduro y popular pero:
- Vendor adicional (cuenta separada, billing aparte, integración paralela)
- Sin ventaja sobre ACS Email para nuestros casos de uso
- ACS está mejor integrado con el resto del stack Azure

**Mailgun**. Sin presencia notable en MX, vendor adicional sin razón.

**AWS SES**. Más barato pero introduce AWS al stack. No tiene sentido si
todo lo demás vive en Azure.

**SMTP propio en VM o App Service**. Pesadilla operacional:
- IP reputation cero al inicio; emails caen en spam
- Mantener SPF/DKIM/DMARC manualmente
- Throttling de outbound 25 por algunos ISPs
- Cero monitoreo built-in

**Microsoft 365 / Exchange Online via Graph API**. Posible pero:
- Limitado por cuotas de M365 (10,000 emails/día/buzón típicamente)
- Diseñado para email humano, no transaccional
- Cada CFDI enviado contaría contra el buzón corporativo
- ACS Email es la respuesta de Microsoft a este caso de uso específico

## Notas de implementación

**Backend**

- Agregar `Azure.Communication.Email` a `Directory.Packages.props`
- Crear `IEmailSender` y `EmailSender` (default impl) en `Shared/Application/Email/`
- Crear `IEmailTemplate` y `EmailTemplateBase<TDatos>` en `Shared/Application/Email/Templates/`
- Crear `EmailHtmlBuilder` en `Shared/Application/Email/Builders/`
- Tabla `core.email_outbox` y `compartido.empresa_email_config` en migraciones (cuando se requiera, probablemente Fase 2 o 3)
- Hosted service `EmailSenderWorker` (alineado con ADR-0022)
- Endpoint `POST /api/internal/email-events` que recibe webhooks de Event Grid

**Bicep**

- Recurso `Microsoft.Communication/emailServices`
- Subdomain verificado por empresa (asignado al `acs_domain_resource_id`)
- Connection string en Key Vault
- Event Grid subscription al endpoint del backend

**Frontend**

- Pantalla admin "Cola de emails" en `features/admin/emails/`
  - Lista de emails con filtros (status, plantilla, fecha, destinatario)
  - Detalle con preview del HTML render
  - Botones de reintento manual

**Plantillas embebidas (recursos)**

- HTML base layouts en `Shared/Application/Email/Layouts/` como Embedded Resources
- `EmailHtmlBuilder` los compone con el contenido específico

**Tests**

- Unit tests de cada plantilla con datos de prueba (verifica que renderiza, que el HTML es válido, que el subject no está vacío)
- Snapshot testing con Verify para HTML rendered (detecta cambios visuales accidentales)
- Tests de integración con ACS sandbox o mock
- Test de bounce handling: simular un evento de Event Grid y verificar que actualiza correctamente

**Documentación en `CLAUDE.md`**

- Cómo crear una plantilla nueva (heredar de `EmailTemplateBase<T>`, definir `Datos` como record)
- Cómo invocar `IEmailSender` desde un handler
- Cómo asociar adjuntos via `core.documentos`
- Convenciones de naming
- Cómo configurar dominio nuevo (SPF/DKIM/DMARC)
- Cómo testear plantillas localmente

**ADRs hijo posibles**

- SMS notifications via ACS si surge requerimiento
- WhatsApp Business si surge requerimiento
- Templates editables por usuarios (Liquid u otro engine)
- Política de internacionalización (i18n) si se envía a clientes extranjeros
- Estrategia para envíos masivos si surge caso (newsletter, promociones)
- Política de antispam corporativo: cómo manejar el caso de empresas
  cliente cuyo IT bloquea correos del dominio Millet
