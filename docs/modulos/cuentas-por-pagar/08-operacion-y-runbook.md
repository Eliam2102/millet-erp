# 08 — Operación y runbook del módulo Cuentas por Pagar

**Fecha:** 2026-05-23 (cierre F10-PR1).
**Audiencia:** DevOps, responsable CxP (Eduardo / Auxiliar), Dirección de Finanzas.

Este documento es la guía operativa post go-live del módulo CxP del ERP Millet. Cubre despliegue, observabilidad, troubleshooting, FAQs operativas y el inventario de `PLATFORM-TODO` aún abiertos.

---

## 1. Estado del módulo

| Fase | PRs | Estado |
|---|---|---|
| F0 — Foundation | #211 | ✅ Mergeada |
| F1 — CfdiRecibido + ingestión manual | F1-PR1 | ✅ Mergeada |
| F2 — Ingestión automática (SAT + mailbox) | F2-PR1 / F2-PR2 | ✅ Mergeadas |
| F3 — FacturaProveedor con OC + eventos | F3-PR1 / F3-PR2 | ✅ Mergeadas |
| F4 — Workflow de revisión + evidencias | F4-PR1 / F4-PR2 | ✅ Mergeadas |
| F5 — Integración Compras + Almacén | #232 | ✅ Mergeada |
| F6 — NC, anticipos, notas de cargo, ciclo devolución | #231 #233 #241 | ✅ Mergeadas |
| F7 — Comprobaciones + Aduanales + Viáticos + TC | #242 #244 #246 #248 #249 #251 | ✅ Mergeadas |
| F8 — Reportes | #252 #253 | ✅ Mergeadas |
| F9 — Tesorería wireup | #254 | ✅ Mergeada |
| **F10** — Hardening + go-live | F10-PR1 (este PR), F10-PR2 | 🟡 en curso |

Tests: **322 unit tests** pass (Millet.CuentasPorPagar.UnitTests).

---

## 2. Despliegue

### 2.1 Workflows GitHub Actions

- **`Validate Application`** — corre en cada PR contra `main`. 4 jobs: Build backend / Build frontend / Unit tests backend / Lint frontend. Requerido como gate para merge.
- **`Deploy Application to Dev`** — corre en push a `main`. 6 jobs: Build backend / Build frontend / Deploy frontend (SWA) / Apply EF migrations / Deploy backend (App Service) / Smoke tests.

### 2.2 Smoke tests (PR #247)

Tras agregar viáticos + TC el warmup creció a ~110-120s. El workflow ahora tolera 210s:
- `sleep 60` inicial.
- `max=15` reintentos × 10s cada uno.
- `timeout-minutes: 5` sigue holgado.

Si un futuro PR hace que el warmup pase de 210s, ajustar `.github/workflows/deploy-app-dev.yml` antes del merge.

### 2.3 EF Core migrations

Las migraciones se aplican automáticamente en el job `Apply EF Core migrations` del deploy. Una migración falla con SqlState `0A000` si intenta usar una extensión de Postgres no allow-listed en Azure PG. Si surge:

1. Editar la migración para diferir/quitar `CREATE EXTENSION`.
2. Crear un PR de hotfix.
3. Cuando se quiera la extensión real: allow-listarla en bicep (server parameter `azure.extensions`), luego crear una migración separada que ejecute `CREATE EXTENSION` + los GIN indexes que dependen.

### 2.4 Re-ejecución de jobs fallidos

- `gh run rerun <id> --failed` — re-corre solo los jobs failed (más rápido).
- `gh run rerun <id>` — re-corre todo el workflow.
- Causa común de fallos transitorios:
  - **Billing GitHub Actions** (la cuenta TiGlass se quedó sin minutos / payment failure). Síntoma: jobs fallan en 2 segundos sin output. Acción: revisar `https://github.com/organizations/TiGlass/settings/billing`.
  - **Static Web Apps action auth** (`fatal: could not read Username for 'https://github.com'`). Acción: re-run.

---

## 3. Observabilidad

### 3.1 Logging

Serilog + OpenTelemetry → Azure Monitor (Application Insights). Los logs del módulo CxP llevan estructuradamente:
- `ModuleName=CuentasPorPagar`
- `EventId` (categorías custom: `[EstadoCuentaTcExcelParser]`, `[OcDevolucionRegistradaHandler]`, etc.).
- Trace ID del request HTTP (W3C Trace Context propagado por `Azure.Monitor.OpenTelemetry`).

### 3.2 Queries útiles en App Insights

| Síntoma | Query KQL |
|---|---|
| Falla la conciliación automática | `traces \| where customDimensions.SourceContext contains "ConciliacionAutomaticaService" and severityLevel >= 2` |
| Worker SB no procesa | `traces \| where customDimensions.SourceContext contains "TesoreriaEventListener" or contains "AlmacenEventListener"` |
| Idempotency-Key duplicado | `dependencies \| where customDimensions.endpoint contains "cuentas-por-pagar" \| where resultCode startswith "409"` |
| Migration pendiente | `traces \| where message contains "Applying migration" or message contains "Database is already up to date"` |

### 3.3 Health endpoints

- `GET /health/live` — siempre 200 si el process está vivo.
- `GET /health/ready` — agrega checks de Postgres + migrations applied. Si falla post-deploy: investigar `Apply EF Core migrations` del workflow.

---

## 4. Troubleshooting recetas

### 4.1 "La factura no pasa a `Pagada` aunque Tesorería ya pagó"

1. ¿Llegó el evento `tesoreria.pago-factura-proveedor.aplicado.v1` al topic `tesoreria-events`?
   - Si no: problema upstream en Tesorería.
2. ¿`TesoreriaEventListenerWorker` lo procesó? Ver `cuentas_por_pagar.eventos_procesados` filtrando por `evento_tipo`.
3. Si está procesado pero la factura sigue en `Autorizada`: el handler hizo dedupe falso. Revisar el detalle del registro de evento — quizá el FacturaProveedorId del payload no coincide.

### 4.2 "El estado de cuenta TC no cierra — `EC_DIFERENCIA_NO_CERO`"

El total declarado del banco no cuadra con el conciliado:
- Verificar líneas en estado `Pendiente`. Cada una debe confirmarse o reclasificarse (capturar retroactivamente / disputar).
- Verificar líneas tipo `Interes`, `Anualidad`, `Comision` — el algoritmo las omite del match pero sí cuentan al total. Capturarlas como `MovimientoEspecialTc` para que entren al total conciliado.
- Diferencia cambiaria: si moneda extranjera y banco aplicó TC distinto, registrar via `RegistrarDiferenciaCambiaria`.

### 4.3 "El XLSX del banco no parsea"

`POST /estados-cuenta-tc/{id}/archivo` devuelve `EC_PERFIL_NO_REGISTRADO` o `parseo` errores:
- Verificar `tarjeta.PerfilParser` coincide con un código en `cuentas_por_pagar.perfiles_parser_banco`.
- Si el formato del banco cambió: actualizar la config del perfil (`columna_*`, `fila_inicio_datos`, `formato_fecha`, `regla_signo_refund`). El parser es Strategy por configuración — no requiere recompilar.

### 4.4 "Repp / complemento de pago no actualiza"

El listener `ReppProveedorRecibidoCommand` marca `factura.ReppRecibido = true`. Si no se ve:
- ¿Llegó el evento `tesoreria.repp-proveedor.recibido.v1` al topic?
- Verificar el FacturaProveedorId del payload existe en CxP.

### 4.5 "La conciliación de TC marca todo como `NoConciliado`"

El algoritmo de score §7 del anexo TC necesita:
- Movimientos en estado `Registrado` (no conciliados aún).
- Fechas dentro de la ventana ±3 días (90 días para refunds).
- Merchant comparable (la normalización a UPPER + colapsar espacios ayuda).

Si todo eso está y el score sigue bajo: revisar `ConciliacionAutomaticaService.CalcularScore` con logging de detalle.

---

## 5. FAQs operativas

**P: ¿Por qué hay 2 endpoints para autorizar comprobaciones?**
R: Caja Chica y Otros usan firma única (`POST /comprobaciones/{id}/autorizar`). Aduanales requiere doble firma (Comercio Exterior + Dirección de Finanzas) vía `POST /autorizar-nivel1` + `POST /autorizar-nivel2` (§7.2). El dominio rechaza la firma incorrecta según el `Tipo`.

**P: ¿Cómo capturo un cargo que no apareció antes en el sistema (captura retroactiva)?**
R: Desde la conciliación, la línea sin match permite `POST /estados-cuenta-tc/{id}/lineas/{lineaId}/capturar-retroactiva` — genera un `MovimientoTarjetaCredito` con flag `CapturaRetroactiva=true` (§5.3 paso 7, D13).

**P: ¿Un cargo en disputa entra al cierre del estado de cuenta?**
R: No. `CerrarEstadoCuentaTcCommand` filtra los movimientos con `EnDisputa=true` del total que se factura al banco. La factura agregada contra el banco es por el total ajustado (§8.5).

**P: ¿Qué pasa si reverso un pago y la factura ya estaba `Pagada`?**
R: Si `monto > 0` y `SaldoPendiente > 0` tras restar, la factura vuelve a `Autorizada` automáticamente (`FacturaProveedor.RevertirPago`). Bitácora queda.

---

## 6. Inventario de `PLATFORM-TODO` abiertos

Tracking sistemático según ADR-0031. Cada item es un código que aparece en algún `PLATFORM-TODO(<id>)` del código + el ticket / dueño cuando aplique.

### 6.1 Pendientes en CxP que dependen de otros módulos

| ID | Bloqueo | Dueño | Síntoma actual | Cierra cuando |
|---|---|---|---|---|
| `<ProveedorReadPort>` | DatosMaestros expone master de proveedores con bank data | DatosMaestros | `NoOpProveedorReadPort` devuelve `null` — el match por OC funciona porque la OC carga snapshot; el reporte de cartera no agrupa por subcategoría | Adapter real al DbContext de DatosMaestros |
| `<EmpleadoReadPort>` | RH/Administración expone master de empleados | RH/Admin | `NoOpEmpleadoReadPort` retorna lista vacía — validaciones en viáticos usan IDs sin nombre | Adapter al master en `Administracion` |
| `<SucursalReadPort>` | DatosMaestros expone sucursales | DatosMaestros | Reportes muestran `SucursalId` como GUID en vez de nombre | Adapter al master |
| `<PuestosEnAdmin>` | RH/Admin expone master de puestos con `Activo` | RH/Admin | `NoOpPuestoReadPort` tiene seed local de 6 puestos — viáticos funciona para esos | Adapter al master + datos reales |
| `<DependenciasRevisorasEnAdmin>` | DatosMaestros expone catálogo `dependencias_revisoras` | DatosMaestros | Seed local en CxP cubre los flujos de revisión; cuando RH cree el master se migra | Adapter al master |
| `<ContabilidadConceptos>` | Módulo Contabilidad expone catálogo de conceptos contables | Contabilidad | Listas vacías; los conceptos se capturan como texto libre | Adapter al catálogo de Contabilidad |
| `<TipoCambioReadPort>` | DatosMaestros expone histórico de TC | DatosMaestros | `NoOpTipoCambioReadPort` retorna fixed 1.0 — TC USD se captura snapshot por movimiento | Adapter al histórico Banxico/manual |
| `<ArticuloReadPort>` | DatosMaestros expone master de artículos | DatosMaestros | El match por artículo no se hace; las líneas de factura llevan descripción libre | Adapter |
| `<ProveedorReadPortConSubcategoria>` | Extensión del `IProveedorReadPort` con categoría/subcategoría | DatosMaestros | Reporte `cartera` no cruza por subcategoría — agrupa por proveedor | Subcategoría en `ProveedorDto` |
| `<ObrasReadPort>` | Módulo Obras (no existe) | Obras (futuro) | Reporte `pasivos-obras` filtra por sucursal sin proyecto | Cuando Obras llegue |
| `<TesoreriaPagoViaticosEvent>` | Tesorería emite evento real de pago de préstamo a empleado | Tesorería | Hoy `MarcarAnticipoPagado` es endpoint manual proxy | Listener al evento real |
| `<PayloadEnriquecido>` | Bank data + evidencias inline en `PasivoAutorizadoParaPago` | DatosMaestros + CxP | Payload mínimo; Tesorería resuelve via queries propias | `IProveedorBancoReadPort` |
| `<NotificacionesGlobal>` / `<NotificacionesCxpOcCancelada>` | Motor de notificaciones global del ERP | Plataforma | `NoOpNotificacionService` loguea — alertas no llegan al humano | Adapter SignalR/email/Teams |

### 6.2 Pendientes en CxP que dependen de infra

| ID | Bloqueo | Dueño | Cierra cuando |
|---|---|---|---|
| `<CfdiBlobStorage>` | Azure Blob Storage para CFDIs (hoy filesystem local del App Service) | DevOps | Adapter Azure Blob con SAS firmadas |
| `<EvidenciaBlobStorage>` | Azure Blob Storage para evidencias de autorización | DevOps | Adapter Azure Blob |
| `<PgTrgmAllowList>` | Allow-listar `pg_trgm` en Azure PG | DevOps | `azure.extensions` actualizado vía bicep + nueva migración con `CREATE EXTENSION` + GIN indexes |
| `<MailboxIngestion>` | Graph mailbox real (hoy NoOp salvo F2-PR2 wireado) | DevOps + IT | App registration con permission `Mail.Read` |
| `<FiscalApi>` | Cliente FiscalAPI real ya implementado pero apagado por config | Operación | Llenar `CuentasPorPagar:FiscalApi` en Key Vault |

### 6.3 Pendientes intra-CxP (refactors menores)

| ID | Qué hace falta | Cuándo |
|---|---|---|
| `<EvidenciasParaTodosLosTiposDoc>` | Extender CHECK constraint para `tipo_documento IN (1,2,3,4)` ahora que existen Anticipo / NotaCargo / ComprobacionGastos | Cuando el operador pida adjuntar evidencias a esos tipos |

---

## 7. Catálogos seed instalados

| Catálogo | Filas seed | Origen |
|---|---|---|
| `motivos_revision` | 14 | F4-PR1 (§5.3 levantamiento) + 1 nueva en F9 (`TESORERIA_SOLICITA_CANCELAR`) |
| `perfiles_parser_banco` | 1 (`AMEX_MX`) | F7-PR5 — agregar BANAMEX/BBVA cuando el área lo confirme |
| `aprobadores_limites` | 0 | RH llena al go-live (§13.1 punto 2 levantamiento) |
| `politicas_viaticos` | 0 | RH + Dirección llenan al go-live (§13.1 punto 3) |
| `tarjetas_credito` | 0 | Auxiliar/Admin crea las 2-5 TC corporativas via `POST /tarjetas` |

---

## 8. Métricas de éxito post go-live

- **Adopción**: # facturas capturadas/mes vs SAP histórico. Meta: 100% en 90 días.
- **Latencia**: `GET /facturas` p95 < 500ms con 500 facturas/mes activas.
- **Conciliación TC**: % líneas Matched auto ≥ 70% (las demás caen en sugerencia o captura retroactiva).
- **Tasa de rechazo por tolerancia**: < 5% (si sube, ajustar tolerancia del proveedor).
- **Tiempo cierre estado de cuenta TC**: < 2 días hábiles tras corte del banco.

---
