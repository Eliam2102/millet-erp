# Cuidados de infraestructura — Módulo Cuentas por Pagar (`Millet.CuentasPorPagar`)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 1.4), [01a-anexo-tc-empresarial.md](01a-anexo-tc-empresarial.md), [02-plan-implementacion.md](02-plan-implementacion.md), [03-pr-breakdown.md](03-pr-breakdown.md).
>
> **Hereda contexto de:** [`docs/modulos/compras-ordenes-compra/04-cuidados-infra.md`](../compras-ordenes-compra/04-cuidados-infra.md). Los cuidados de plataforma compartida (interceptors, auditoría, soft delete, multi-empresa, Money, idempotencia HTTP) son los mismos. Este documento **enfatiza lo nuevo de CxP** y, donde aplique, ajusta umbrales.
>
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

Cada cuidado tiene cuatro líneas:

- **Qué**: la regla en una frase.
- **Por qué importa**: justificación.
- **Cómo lo verificamos**: test, gate de CI, checklist de PR, revisión manual.
- **Qué pasa si lo ignoramos**: modo de falla.

Prioridades: **P0** bloqueante para release · **P1** importante · **P2** nice-to-have trazable.

> **Reuso primero:** muchos cuidados ya están verificados en Compras. Donde aplique, este doc apunta a `§X del 04 de Compras-OC` en lugar de duplicar.

---

## 1. Migraciones EF Core

> Ref: ADR-0005, plan §2 audit, F0-PR2, F1-PR1, F3-PR1, F4-PR1/PR2, F6-PR1/PR4, F7-PR1/PR6/PR8.

### 1.1 [P0] Cada migración debe revisarse como SQL, no como C#

Ver §1.1 del 04 de Compras-OC. CxP introduce **~15 migraciones** entre F1 y F7. Reviewer rechaza el PR si `dotnet ef migrations script` no está adjunto al PR.

### 1.2 [P0] Multi-DbContext en `MigrationsHealthCheckOptions.ContextTypes`

- **Qué**: F0-PR2 introduce `CuentasPorPagarDbContext`. **Debe** registrarse en `MigrationsHealthCheckOptions.ContextTypes` y en `.github/workflows/deploy-app-dev.yml` con `dotnet ef database update --context CuentasPorPagarDbContext`. Memoria explícita: `feedback_dbcontext_nuevo_checklist`.
- **Por qué importa**: sin esto, deploy falla con `/health/ready 503` porque el health check no encuentra migraciones aplicadas para el contexto nuevo. Caso documentado de Compras.
- **Cómo lo verificamos**: checklist del PR + smoke test en staging del `/health/ready`.
- **Qué pasa si lo ignoramos**: deploy a dev se queda con 503; rollback manual.

### 1.3 [P0] `pg_trgm` extension para fuzzy match de merchants (F7-PR9)

- **Qué**: el algoritmo de conciliación de TC usa `pg_trgm` para similaridad de merchants. La migración debe `CREATE EXTENSION IF NOT EXISTS pg_trgm;` antes de crear el índice GIN.
- **Por qué importa**: en Azure Database for PostgreSQL Flexible Server, las extensiones requieren whitelist. `pg_trgm` está en el catálogo predeterminado, pero hay que confirmar en cada ambiente.
- **Cómo lo verificamos**: migración aplica en dev/qa/prod. Si falla, agregar `pg_trgm` a `azure.extensions` en el parámetro del servidor PostgreSQL (Bicep).
- **Qué pasa si lo ignoramos**: F7-PR9 mergea en dev pero falla al desplegar a qa/prod.

### 1.4 [P0] Polimorfismo de `EvidenciaAutorizacion`

- **Qué**: la tabla `evidencias_autorizacion` apunta polimórficamente a 4 tipos de documento (`FacturaProveedor`, `AnticipoProveedor`, `NotaCargo`, `ComprobacionGastos`) vía `(tipo_documento, documento_id)`. El CHECK constraint valida que el documento exista según el tipo.
- **Por qué importa**: sin el CHECK, evidencias huérfanas se acumulan. Sin un índice compuesto eficiente, queries comunes se vuelven lentas.
- **Cómo lo verificamos**: migración F4-PR4 incluye CHECK + índice `(tipo_documento, documento_id)`. Test integración: intentar insertar evidencia con `tipo_documento='FacturaProveedor'` y `documento_id` inexistente → falla con check_violation.
- **Qué pasa si lo ignoramos**: cleanup manual de evidencias huérfanas; performance de queries degrada con volumen.

### 1.5 [P1] Secuencias atómicas para folios

- **Qué**: F6-PR4 crea `cuentas_por_pagar.folio_secuencias_nota_cargo` (`(año, secuencial)`). Generación atómica vía `nextval`. Reset anual el 1 de enero (si aplica) o eterno según política. Confirmar con el área.
- **Por qué importa**: dos notas de cargo concurrentes con mismo folio bloquean la operación.
- **Cómo lo verificamos**: test concurrente: 100 INSERT paralelos → 100 folios distintos. F6-PR4 incluye este test.
- **Qué pasa si lo ignoramos**: colisión de folios.

### 1.6 [P1] Particionamiento de `cfdis_recibidos` (post-MVP)

- **Qué**: con la descarga masiva del SAT, la tabla `cfdis_recibidos` crece ~500 filas/mes. En 5 años son 30K registros — manejable. **No** particionar en MVP. Monitorear; si supera 1M, particionar por trimestre (A1).
- **Por qué importa**: particionamiento prematuro es complejidad innecesaria; particionamiento tardío requiere migración con downtime.
- **Cómo lo verificamos**: alerta de Azure Monitor cuando `cfdis_recibidos` > 500K filas.
- **Qué pasa si lo ignoramos**: queries de bandeja se degradan más allá del punto.

---

## 2. Outbox + Service Bus (ADR-0009)

### 2.1 [P0] Outbox del CxP independiente del de Compras

- **Qué**: tabla `cuentas_por_pagar.outbox_messages` con su propio `OutboxPublisherWorker<CuentasPorPagarDbContext>`. **NO** comparte tabla con Compras o Almacén (cada módulo aislado).
- **Por qué importa**: aislamiento de transacciones. Si Compras tiene un problema, el outbox de CxP sigue publicando.
- **Cómo lo verificamos**: F0-PR2 crea tabla en schema propio. Worker registrado por DbContext.
- **Qué pasa si lo ignoramos**: cross-module coupling; difícil debug.

### 2.2 [P0] Idempotencia en consumidores cross-módulo

- **Qué**: cada listener (de Tesorería, Compras, Almacén) verifica `(evento_id, evento_tipo)` antes de procesar. Tabla local `eventos_procesados` (estándar A12 del 01-diseno de Almacén; análogo en CxP).
- **Por qué importa**: at-least-once delivery garantizado por Service Bus.
- **Cómo lo verificamos**: test integración: mismo evento dos veces → un solo efecto.
- **Qué pasa si lo ignoramos**: duplicación de pasivos, doble pago, conciliaciones incorrectas.

### 2.3 [P1] TTL del outbox + alertas

- **Qué**: mensajes `outbox_messages.published_at IS NULL` por más de 5 min → alerta. Más de 100 mensajes pendientes → alerta high.
- **Por qué importa**: si Service Bus está caído o el worker no corre, los eventos se acumulan.
- **Cómo lo verificamos**: dashboard de Application Insights + alerta automática.
- **Qué pasa si lo ignoramos**: backlog masivo cuando vuelve el servicio; procesamiento lento; eventos potencialmente expirados.

### 2.4 [P1] Naming canónico de eventos

- **Qué**: convención `{Agregado}{Verbo}Event` con sufijo `Event`. Documentada en CLAUDE.md "Triada Compras ↔ Almacén ↔ CxP". Code review rechaza nombres que no la sigan.
- **Por qué importa**: consistencia cross-módulo; integración fácil con Service Bus topics.
- **Cómo lo verificamos**: revisión de PR + grep automatizado en CI (`rg -P "Event\b" backend/src/CuentasPorPagar/Domain/.*Events/" | grep -v "Event\.cs$" → 0 hits"`).
- **Qué pasa si lo ignoramos**: drift; refactor doloroso post-MVP.

---

## 3. Integración FiscalAPI (ADR-0027)

### 3.1 [P0] Credenciales en Azure Key Vault

- **Qué**: API key + endpoints de FiscalAPI viven en Key Vault. Acceso via Managed Identity. `appsettings.json` solo tiene referencia.
- **Por qué importa**: PCI-DSS-ish. Cualquier credencial en código se rota inmediatamente.
- **Cómo lo verificamos**: revisión de PR + scanner secrets en CI (Gitleaks o similar).
- **Qué pasa si lo ignoramos**: filtración → costo legal + rotación obligatoria.

### 3.2 [P0] Circuit breaker + retry exponencial

- **Qué**: F2-PR1 usa Polly. Retry exponencial (3 intentos, 2^n segundos). Circuit breaker (5 fallas consecutivas → abre 60 segundos).
- **Por qué importa**: FiscalAPI puede tener disponibilidad <99.9%. Sin circuit breaker, los workers entran en thrashing.
- **Cómo lo verificamos**: F2-PR1 incluye test con mock que retorna 500.
- **Qué pasa si lo ignoramos**: workers timeout; backlog del outbox; saturación de CPU.

### 3.3 [P1] Rate limiting respetado

- **Qué**: FiscalAPI tiene rate limits (típicamente 100 req/min por API key). Workers respetan con throttling.
- **Por qué importa**: exceder rate limit → 429 + ban temporal.
- **Cómo lo verificamos**: configuración en `appsettings.json` + log de 429s en App Insights.
- **Qué pasa si lo ignoramos**: descarga SAT se detiene; backlog acumulado.

---

## 4. Mailbox y Microsoft Graph (F2-PR3)

### 4.1 [P0] Cuenta de servicio del mailbox vía Azure AD

- **Qué**: el mailbox `cfdi@millet.com.mx` se accede via Microsoft Graph con un App Registration en Entra ID. Credenciales en Key Vault.
- **Por qué importa**: cuentas con MFA no se pueden automatizar con IMAP/SMTP user-password.
- **Cómo lo verificamos**: F2-PR3 documenta el App Registration en `08-operacion-y-runbook.md`.
- **Qué pasa si lo ignoramos**: worker no puede leer mailbox; integración rota.

### 4.2 [P1] Retención del mailbox

- **Qué**: mensajes procesados se marcan como leídos o se mueven a `Processed` folder. Política de retención 6 meses (configurable).
- **Por qué importa**: mailbox crece; auditoría requiere preservar originales por X tiempo.
- **Cómo lo verificamos**: política aplicada en Exchange Online + procedimiento documentado.
- **Qué pasa si lo ignoramos**: mailbox lleno; nuevos correos rechazados.

### 4.3 [P1] Manejo de attachments malformados

- **Qué**: parser de XML CFDI puede fallar con archivos corruptos. F2-PR3 captura el error, archiva el correo en `Failed/`, alerta al Auxiliar.
- **Por qué importa**: un correo malo no debe detener el worker.
- **Cómo lo verificamos**: test integración con XML corrupto.
- **Qué pasa si lo ignoramos**: worker crashea; backlog se acumula.

---

## 5. Concurrencia y locking

### 5.1 [P0] Optimistic concurrency en todas las entidades (ADR-0012 Capa 1)

Ver §5 del 04 de Compras-OC. Aplica idéntico a CxP. Toda PATCH/PUT requiere `If-Match` con `Version`. Conflict → 412.

### 5.2 [P0] Soft lock vía SignalR para captura simultánea (ADR-0012 Capa 2)

- **Qué**: declarar `FacturaProveedor`, `NotaCargo`, `AnticipoProveedor`, `ComprobacionGastos` en la lista de entidades con awareness colaborativo. Patrón establecido en Compras.
- **Por qué importa**: dos Auxiliares editando la misma comprobación de viáticos chocan en última-escritura-gana sin awareness.
- **Cómo lo verificamos**: test E2E con dos navegadores; soft lock notifica.
- **Qué pasa si lo ignoramos**: trabajo perdido por write conflicts silenciosos.

### 5.3 [P0] Saldo de anticipo con concurrencia

- **Qué**: aplicar anticipo a factura reduce `anticipo.monto_amortizado`. Lock optimista vía `Version`; si dos aplicaciones simultáneas → la segunda recibe 409 y reintenta con el nuevo `Version`.
- **Por qué importa**: aplicar 100 sobre saldo 50 dejaría saldo -50. Invariante `saldo_amortizable >= 0` se debe respetar.
- **Cómo lo verificamos**: test concurrente F6-PR3.
- **Qué pasa si lo ignoramos**: anticipos amortizados de más; saldos incorrectos.

---

## 6. Seguridad y datos sensibles

### 6.1 [P0] RFC y datos bancarios del proveedor

- **Qué**: RFC y datos bancarios no se loggean. Serilog masking activo (ya en Compras). Atributo `[PII]` en VOs sensibles.
- **Por qué importa**: cumplimiento + LFPDPPP.
- **Cómo lo verificamos**: revisión de logs en staging después de capturar 5 facturas; ningún log muestra RFC completo.
- **Qué pasa si lo ignoramos**: filtración → infracción + multa.

### 6.2 [P0] Adjuntos de evidencias en Azure Blob privado

- **Qué**: blob storage con acceso vía SAS tokens cortos (1h). No public read.
- **Por qué importa**: evidencias de autorización pueden contener fotos personales, audios privados, capturas con datos confidenciales.
- **Cómo lo verificamos**: configuración del Storage Account + revisión Bicep.
- **Qué pasa si lo ignoramos**: filtración pública de evidencias.

### 6.3 [P1] Estado de cuenta del banco (TC) — sin PAN completo

- **Qué**: el archivo Excel/CSV del banco puede contener números de tarjeta completos. El parser **descarta** la columna de PAN si existe; solo conserva últimos 4 dígitos. PCI-DSS-ish.
- **Por qué importa**: aunque CxP no procesa pagos, almacenar PAN entera = scope PCI.
- **Cómo lo verificamos**: F7-PR9 incluye test con archivo que tenga PAN; parser lo redacta.
- **Qué pasa si lo ignoramos**: scope PCI involuntario; auditoría exige certificación cara.

---

## 7. Performance

### 7.1 [P1] Bandejas paginadas siempre

- **Qué**: ningún endpoint de bandeja devuelve N filas sin paginación. Default `page_size=50`, max `500`.
- **Por qué importa**: con 500 facturas/mes, en 5 años son 30K — manejable solo con paginación.
- **Cómo lo verificamos**: validador en F3-PR3 que rechaza `page_size > 500` con 422.
- **Qué pasa si lo ignoramos**: queries lentas; timeouts; mala UX.

### 7.2 [P1] Índices del §5.1

- **Qué**: la migración base de cada tabla incluye los índices del §5.1 del 01-diseno. Análisis de plan en staging con datos sintéticos.
- **Por qué importa**: queries de bandeja sin índice degradan rápido.
- **Cómo lo verificamos**: F3-PR1 + F4-PR2 + F7-PR8 incluyen tests de `EXPLAIN ANALYZE` con 10K filas.
- **Qué pasa si lo ignoramos**: bandejas inutilizables a 6 meses del go-live.

### 7.3 [P2] Vista materializada para reportes agresivos (post-MVP)

- **Qué**: si reportes (F8) degradan a 6 meses, evaluar `MATERIALIZED VIEW` con refresh diario.
- **Por qué importa**: reportes de cartera con 12 meses de histórico pueden volverse lentos.
- **Cómo lo verificamos**: monitoreo post-MVP.
- **Qué pasa si lo ignoramos**: usuarios esperan 30s+ por reportes.

---

## 8. Cross-module integration

### 8.1 [P0] No SELECT directo a tablas de otro módulo

- **Qué**: CxP solo lee de Compras/Administración/Almacén vía `I*ReadPort`. Cero `SELECT ... FROM compras.ordenes_compra` en código de CxP.
- **Por qué importa**: bounded contexts limpios; refactor de un módulo no rompe el otro.
- **Cómo lo verificamos**: code review + grep automatizado (`rg "FROM\s+(compras|administracion|almacen)\." backend/src/CuentasPorPagar` → 0 hits).
- **Qué pasa si lo ignoramos**: refactor de Compras rompe CxP; deuda técnica.

### 8.2 [P0] Stubs `NoOp*` con `PLATFORM-TODO` (ADR-0031)

- **Qué**: cada `NoOp*Port` lleva `PLATFORM-TODO(<TicketId>)` con identificador. Auditoría regular para cerrar stubs reemplazables.
- **Por qué importa**: trazabilidad de deuda de plataforma.
- **Cómo lo verificamos**: F10-PR3 audita `rg "PLATFORM-TODO" backend/src/CuentasPorPagar` → lista en runbook.
- **Qué pasa si lo ignoramos**: stubs olvidados en prod.

### 8.3 [P1] Versioning de contratos de eventos

- **Qué**: cada record de evento tiene `version: int`. Cambios breaking → bump de versión + topic separado en Service Bus.
- **Por qué importa**: cuando Tesorería entre en runtime, cambios en el payload de `PasivoAutorizadoParaPagoEvent` no deben romper a CxP.
- **Cómo lo verificamos**: F9-PR1 establece convención; revisión de PR.
- **Qué pasa si lo ignoramos**: refactor en CxP rompe Tesorería.

---

## 9. Observabilidad

### 9.1 [P0] Logs estructurados con `correlation_id`

Ver §6 del 04 de Compras-OC. CxP propaga `correlation_id` desde HTTP hasta eventos del Outbox.

### 9.2 [P1] Métricas custom para bandejas críticas

- **Qué**: contador de "CFDIs sin capturar > 5 días" en Application Insights. Alertas si > 50.
- **Por qué importa**: visibilidad operativa proactiva.
- **Cómo lo verificamos**: dashboard de App Insights documentado en runbook.
- **Qué pasa si lo ignoramos**: el área se entera tarde.

### 9.3 [P1] Tracing distribuido con OpenTelemetry

- **Qué**: spans entre captura HTTP → handler MediatR → eventos Outbox → Service Bus → consumidor.
- **Por qué importa**: debug de flujos cross-módulo en producción.
- **Cómo lo verificamos**: configuración en `Program.cs` + traces visibles en App Insights.
- **Qué pasa si lo ignoramos**: 10x más tiempo para diagnosticar incidentes.

---

## 10. Seeds y datos iniciales

### 10.1 [P0] Catálogos pendientes al go-live

- **Qué**: antes del go-live, llenar:
  - `motivos_revision` (seed automático en F4-PR1).
  - `tarjetas_credito` (manual por Auxiliar/Admin — el área).
  - `aprobadores_limites` (manual por RH).
  - `politicas_viaticos` (manual por RH + Dirección).
  - `perfiles_parser_banco` (seed `AMEX_MX` en F7-PR9; otros bancos manual).
- **Por qué importa**: el módulo no opera sin estos catálogos.
- **Cómo lo verificamos**: checklist en `09-go-live-checklist.md`.
- **Qué pasa si lo ignoramos**: go-live posponed; usuarios bloqueados.

### 10.2 [P0] Migración de Proveedores depurada

- **Qué**: no migrar proveedores sin movimientos en los últimos 2 años (§5.1 levantamiento). Script previo con reporte de "proveedores a migrar" + "proveedores a archivar".
- **Por qué importa**: master sucio = bandejas sucias = caos operativo.
- **Cómo lo verificamos**: F1 de migración pre-go-live; firma de Auxiliar de CxP.
- **Qué pasa si lo ignoramos**: master ilegible.

---

## 11. Despliegue

### 11.1 [P0] `deploy-app-dev.yml` actualizado en F0-PR2

Ver §1.2. Memoria explícita.

### 11.2 [P0] Workers `IHostedService` se inician en `Millet.Api`

- **Qué**: F2-PR2/PR3/PR4 + F4-PR5 + F6-PR2 registran workers en `Program.cs`. Confirmación de que se inician al deploy.
- **Por qué importa**: workers que no arrancan = funcionalidad oculta.
- **Cómo lo verificamos**: smoke test post-deploy: log `Worker X started` aparece en App Insights.
- **Qué pasa si lo ignoramos**: descarga SAT no corre; usuarios reportan días después.

### 11.3 [P1] Rollback de migraciones

- **Qué**: cada migración tiene su `Down()` testeado en local. Para migraciones grandes (F7-PR8), staging primero.
- **Por qué importa**: si una migración falla en producción, hay que poder volver.
- **Cómo lo verificamos**: test en local de `dotnet ef migrations remove` + `update`.
- **Qué pasa si lo ignoramos**: producción atascada en estado intermedio.

---

## 12. Pendientes para `08-operacion-y-runbook.md`

A documentar en F10-PR4:

- Procedimiento de re-procesar un CFDI rechazado por parser.
- Procedimiento de re-conciliar un estado de cuenta de TC con error.
- Procedimiento de marcar manualmente una factura como `Cancelada` por instrucción del área.
- Procedimiento de re-emitir evento del outbox manualmente (para casos de Service Bus down).
- FAQs operativas comunes.

---

## Rev.

- **2026-05-22 — v1 (Draft)** — Cuidados de infra del módulo CxP. Hereda contexto del 04 de Compras-OC para los cuidados compartidos; expande lo específico de CxP (FiscalAPI, mailbox, TC, evidencias polimórficas, secuencias de folios).
