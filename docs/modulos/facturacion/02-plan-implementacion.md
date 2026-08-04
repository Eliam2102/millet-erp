# Plan de implementación — Módulo Facturación (`Millet.Facturacion`)

> **Proyecto:** ERP Millet — Módulo 3 (Facturación CFDI 4.0).
> **Versión:** 1.0 — Plan inicial sobre `01-diseno.md` Rev. 1.0.
> **Fecha:** 2026-05-30
> **Owner:** Eduardo Paredes — `eduardo.paredes@tiglass.net`

---

## 0. Cómo leer

- Las fases agrupan PRs consolidados (S/M/L), no PRs microscópicos
  (memoria [feedback_pr_granularidad]).
- `(S/M/L)` = tamaño relativo de la fase.
- Cada PR es mergeable y deja el módulo verde (`/health/ready` OK).
- `[Verificado]` = confirmado contra el repo (audit §2); `[stub]` = pieza
  temporal con `PLATFORM-TODO` (ADR-0031).

---

## 1. Resumen ejecutivo

**Objetivo:** entregar v1 de `Millet.Facturacion` (MVP amplio: mostrador,
maquila/anticipos, reparto, obras, administrativa, exportación con CCE,
Carta Porte, venta de activos, cancelación, REPP), alineado con
`00-levantamiento.md` y `01-diseno.md`.

**Estrategia:**

1. **Walking skeleton primero:** capturar un pedido manual → emitir una
   factura simple a cliente nominal → verla en bandeja → con el **stub** de
   timbrado. Solo después agregar anticipos, complementos y la ingesta A+W.
2. **Todo el timbrado contra `IFiscalApiClient` stub** hasta el final. La
   **fase 2 de `Integraciones.Fiscal`** (timbrado/cancelación reales con
   complementos) está en rediseño asíncrono y **bloquea PRs nuevos** (memoria
   [project_integraciones_fiscal_rediseno]); el módulo se construye contra el
   stub y el wireup real es la última fase.
3. **Reuso máximo** de lo que ya existe en el repo (§2): `Millet.Catalogos`
   (catálogos SAT), `Compartido.Series` (series + folios → FANT seed),
   `SoftLockExpirationWorker`, SharedKernel (`BaseEntity`, `Money`, Outbox,
   RBAC, ProblemDetails, Idempotency, ETag), y promover las piezas CFDI de
   CxP (`UuidCfdi`, `IXmlCfdiParser`, `ICfdiBlobStorage`) al **repo CFDI
   común** (Decisión 01-B).
4. **Stubs cross-module** para `Integraciones.Fiscal` fase 2, write-back A+W,
   `DatosMaestros` provisioning, `IPeriodoContablePort` (Contabilidad),
   `IActivosFijosReadPort`, Tesorería. Patrón ADR-0031.
5. **Ingesta A+W por cola de solicitudes** (D18) con doble candado
   (`ingesta_control`); contra fixtures hasta tener la tabla-puente real.
6. **`Integraciones.Origenes`** (Planta Pintura + Salidas) es un **módulo
   nuevo** que se construye como track paralelo; Facturación consume sus
   puertos con stubs hasta que exista.
7. **Reportes con motor nativo** (ADR-0036) al final.

**Pendientes que NO bloquean arranque:**

- Timbrado/cancelación reales (`Integraciones.Fiscal` fase 2) — stub hasta F12.
- Write-back real a A+W — stub que loggea hasta que exista el canal.
- `Integraciones.Origenes` (Planta Pintura, Salidas/pedimento) — stubs.
- Contabilidad, Tesorería, Activos Fijos — stubs.
- Frontend — docs separados (`05-frontend-diseno.md` … `07-…`), posterior.

---

## 2. Prerrequisitos — audit del repo (2026-05-30)

Auditado contra `backend/src`.

| Prerrequisito | Estado | Detalle / plan |
|---|---|---|
| Proyecto `Millet.Facturacion` | ❌ no existe | F0-PR1 crea csproj + folders. |
| `FacturacionDbContext` + schema `facturacion` | ❌ no existe | F0-PR2; registrar en `MigrationsHealthCheckOptions.ContextTypes` y `deploy-app-dev.yml` (memoria [feedback_dbcontext_nuevo_checklist]). |
| `Millet.Catalogos` (catálogos SAT: `FormaPago`, `UsoCfdi`, `RegimenFiscal`, `Incoterm`, `Moneda`, `TipoCambio`…) | ✅ existe `[Verificado]` | Facturación consume vía puerto de lectura; no recrea catálogos SAT. |
| `Compartido.Series` (CRUD + `ReservarFolioCommand` + migración `SeriesYSecuenciasFolio`) | ✅ existe `[Verificado]` | **Resuelve las series configurables** (§A8/§5 diseño): seed `FANT` y demás como datos. No se crea tabla `serie` propia. |
| `SoftLockExpirationWorker` + `ISoftLockManager` (`Api/Hubs`) | ✅ existe `[Verificado]` | Reuso directo para el soft-lock del `PedidoFacturable` durante facturación. |
| `SharedKernel` (`BaseEntity` + `Version` ETag, `Money`, `Moneda`) | ✅ existe `[Verificado]` | Hereda. |
| Outbox (`OutboxSaveChangesInterceptor`, `OutboxPublisherWorker<T>`) | ✅ existe (Compras/CxP) | Reuso vía `OutboxPublisherWorker<FacturacionDbContext>`. |
| RBAC granular + `RequirePermissionAttribute` + Entra ID | ✅ existe | Permisos `facturacion.*` a `PermisosCanonicos.Todos` con migración en `IdentidadDbContext` (memoria [feedback_permisos_canonicos_migration]). |
| Problem Details (ADR-0010), Idempotency (ADR-0020), ETag (ADR-0012) | ✅ existe | Estándar. |
| `Integraciones.Fiscal` (proyecto) | ✅ existe; **fase 2 (emisión) pendiente** | F0 define `IFiscalApiClient` stub de emisión; wireup real en F12 cuando cierre el rediseño. |
| `Integraciones.Aw` (proyecto) | ✅ existe (push Glass Agent) | Se le agregan los readers de cola de solicitudes + master (track paralelo); stub/fixtures hasta entonces. |
| `Integraciones.Origenes` (Planta Pintura + Salidas) | ❌ no existe | **Módulo nuevo** — track paralelo. Facturación usa stubs de sus puertos. |
| CFDI building blocks en CxP (`UuidCfdi`, `IXmlCfdiParser`, `ICfdiBlobStorage`, `CfdiRecibido`) | ✅ existe en CxP `[Verificado]` | F2 promueve los reutilizables al **repo CFDI común** en `Integraciones.Fiscal`; CxP migra a referenciarlo (coordinar). |
| `DatosMaestros` master de Cliente/Producto (fiscal + `origen`) | ⏳ parcial | Extender con atributos fiscales + `origen`; `IMasterProvisioningPort` para auto-provisión A+W (stub hasta que DatosMaestros lo soporte). |
| `IPeriodoContablePort` (Contabilidad) | ❌ no existe | Stub "siempre abierto" hasta que exista Contabilidad. |
| `IActivosFijosReadPort` | ❌ no existe | Catálogo mínimo o stub hasta módulo Activos Fijos. |
| OpenAPI + tipos TS | ✅ existe | Facturación entra al pipeline automáticamente. |

---

## 3. Estrategia de dependencias y paralelización

```
Track A (este plan): Millet.Facturacion  ───────────────────────────────►
Track B (paralelo):  Integraciones.Fiscal fase 2 (timbrado real)  ──► wireup en F12
Track C (paralelo):  Integraciones.Origenes (Planta Pintura + Salidas) ──► consumo en F7/F10
Track D (paralelo):  DatosMaestros (fiscal + origen + provisioning)   ──► consumo en F3
```

- **Facturación NO se bloquea** por B/C/D: todo se construye contra **stubs**
  y se cablea al final (o cuando el track esté listo).
- **El contrato lo define Facturación primero** (puertos + records de
  payload + el **esquema de `aw_solicitud_pedido`**, D18) y se pasa a los
  tracks B/C/D para implementación real.
- **Repo CFDI común**: coordinación con CxP en F2 (promoción de
  `UuidCfdi`/parser/blob; CxP migra a referenciar `cfdi_archivo`).
- Sesión semanal con los equipos de `Integraciones.Fiscal` y `Origenes`
  durante F3–F10.

---

## 4. Fases

### Fase 0 — Foundation (S)

Andamiaje del módulo, sin features.

- F0-PR1: csproj `Millet.Facturacion` + folders (`Domain/`, `Application/`,
  `Infrastructure/`). Smoke `GET /api/v1/facturacion/smoke`.
- F0-PR2: `FacturacionDbContext` + schema `facturacion` + migración vacía.
  Registro en `MigrationsHealthCheckOptions.ContextTypes` y `deploy-app-dev.yml`.
- F0-PR3: Permisos canónicos `facturacion.*` (§10 diseño) + migración en
  `IdentidadDbContext`.
- F0-PR4: Puertos de lectura cross-module con stubs/adapters:
  `IClientesReadPort`, `IProductosReadPort` (DatosMaestros), `ICatalogosSatReadPort`
  (adapter real a `Millet.Catalogos`), `IPeriodoContablePort` [stub abierto],
  `IFiscalApiClient` [stub emisión], `ICsdProvider` [stub CSD pruebas],
  `ICfdiRepositorioPort` [stub local].

**Paralelización:** F0-PR1 → F0-PR2 → (F0-PR3 ‖ F0-PR4).

### Fase 1 — Walking skeleton: Comprobante + Factura de venta manual (M)

Capturar manual y emitir una factura simple contra el stub de timbrado.

- F1-PR1: `Comprobante` (base TPT) + `FacturaVenta` + `factura_venta_linea` +
  value objects (`DatosFiscalesReceptor`, `Importe`, `ConceptoLinea`,
  `DatosTimbrado`). FSM de timbrado (`Borrador → TimbradoEnProceso → Timbrado
  | TimbradoFallido`). *(La FSM se extiende en Fase 13 con
  `TimbradoFallido → Borrador | Descartada` — [Decisión 01-G].)*
- F1-PR2: `PedidoFacturable` (binario, sin parcial) + captura manual:
  `CrearPedidoFacturableManualCommand` + `EditarPedidoFacturableManualCommand`
  (ETag) + endpoints `POST/PUT /pedidos-facturables`. Líneas inline.
- F1-PR3: `EmitirFacturaVentaCommand` contra `IFiscalApiClient` stub (UUID
  fake). Serie/folio vía `Compartido.Series` (`ReservarFolioCommand` — solo
  al crear el documento; el reintento de timbrado reutiliza el folio,
  [Decisión 01-G] G1). Candado de período (`IPeriodoContablePort` stub).
  Validación local previa.
- F1-PR4: Bandejas `BandejaPedidosFacturablesQuery` + `ComprobanteDetalleQuery`
  + endpoints `GET /facturas`, `GET /facturas/{id}`.

**Paralelización:** F1-PR1 → F1-PR2 ‖ (F1-PR3 → F1-PR4).

### Fase 2 — Repo CFDI común + PDF + envío (M)

- F2-PR1: **Repo CFDI común** `cfdi_archivo` en `Integraciones.Fiscal` +
  `ICfdiRepositorioPort`. Promover `UuidCfdi`, `IXmlCfdiParser`,
  `ICfdiBlobStorage` desde CxP. **Coordinar migración de `CfdiRecibido`**
  (CxP referencia el archivo común). `[Decisión 01-B]`
- F2-PR2: `PdfFacturaRenderer` — plantillas React/`@react-pdf` bilingüe
  (ES/EN) + simplificada térmica (ADR-0036). `RelacionCfdi` + cadena.
- F2-PR3: `BitacoraEnvioCorreo` + `EnvioCfdiCorreoWorker` (reintentos);
  `INotificacionService` [stub]. `ReenviarCfdiCorreoCommand`.

**Paralelización:** F2-PR1 → (F2-PR2 ‖ F2-PR3).

### Fase 3 — Ingesta A+W (cola de solicitudes) + provisión de master (M/L)

El corazón de la integración entrante (D18, §12.1 levantamiento).

- F3-PR1: Esquema **`aw_solicitud_pedido`** (definido por el ERP) +
  `ingesta_control` + `pedido_facturable_snapshot`. `IAwSolicitudesReader`
  [stub fixtures] + `IAwWriteBackPort` [stub que loggea].
- F3-PR2: `AwSolicitudesWorker` + `ProcesarSolicitudAwCommand` — matriz
  operación × estado (Alta/Modificación/Cancelación), orden por `version`,
  hash de contenido, claim write-back. Soft-lock vía `ISoftLockManager`.
- F3-PR3: Auto-provisión de master: `IMasterProvisioningPort`
  (`EnsureClienteDesdeAw`/`EnsureArticuloDesdeAw`) [stub → DatosMaestros] +
  `IAwClientesReader`/`IAwArticulosReader` [stub]. Excepción si Planta Pintura.
- F3-PR4: `bandeja_excepcion_importacion` + `BandejaExcepcionesQuery` +
  `ResolverExcepcionImportacionCommand`.

**Paralelización:** F3-PR1 → F3-PR2 → (F3-PR3 ‖ F3-PR4).

### Fase 4 — Anticipos (M/L)

El núcleo fiscal (§6 levantamiento).

- F4-PR1: `FacturaAnticipo` (serie `FANT` vía `Compartido.Series`) +
  `Anticipo` (saldo amortizable = cobrado − amortizado). `EmitirFacturaAnticipoCommand`.
- F4-PR2: `VincularAnticipoCommand` (M2, relación 07) + validación de saldo.
- F4-PR3: **NC de amortización atómica** (M3): autogenera + timbra
  `NotaCredito` en la misma transacción que la factura final; relaciona
  factura de anticipo + factura final. `anticipo_vinculacion`.
- F4-PR4: Reportes Control de Anticipos (Resumen + Detallada por cliente).

**Paralelización:** F4-PR1 → F4-PR2 → F4-PR3 → F4-PR4.

### Fase 5 — NC por bonificación + Cancelación SAT (M)

- F5-PR1: `NotaCredito` motivo bonificación (relación 01) +
  `EmitirNotaCreditoBonificacionCommand`. NC inmediata en mostrador (§7.1).
- F5-PR2: `SolicitudCancelacion` (FSM `Solicitada→EnProceso→{Aceptada|
  Rechazada|Vencida}`) + `SolicitarCancelacionCommand` (motivos SAT, UUID
  sustituto). Validación de cadena (anticipos amortizados, §10 levantamiento).
- F5-PR3: `CancelacionSatPollerWorker` (estatus async; suscribe
  `CancelacionSatResueltaEvent` de Integraciones.Fiscal [stub]).

**Paralelización:** F5-PR1 ‖ (F5-PR2 → F5-PR3).

### Fase 6 — Complementos de pago (REPP) (M)

- F6-PR1: `ReciboPago` (CFDI P, Pago 2.0) + `recibo_pago_factura`
  (multi-factura, parcialidades). `EmitirReppCommand`.
- F6-PR2: **Ganancia/pérdida cambiaria** automática (diferencia TC factura vs
  cobro) → `ConceptoContable` `GANANCIA_/PERDIDA_CAMBIARIA`.
- F6-PR3: Automatización: suscribe `PagoClienteConfirmadoEvent`
  (Tesorería/Ingresos) [stub] → genera REPP. Flujo reparto (forma 99 +
  liquidación de ruta).

**Paralelización:** F6-PR1 → (F6-PR2 ‖ F6-PR3).

### Fase 7 — Exportación + CCE + pedimento condicional (M/L)

- F7-PR1: `complemento_cce` + `complemento_cce_linea`. IVA 0%, INCOTERM, TC
  DOF, receptor extranjero (`XEXX`).
- F7-PR2: **Compuerta de pedimento condicional** (§3.bis.5):
  `requiere_pedimento` por línea/cabecera (del pedido), estado
  `PendientePedimento`, `AplicarPedimentoCommand`.
- F7-PR3: `ISalidasPedimentosReader` [stub] + `PedimentoSalidasWorker`
  (empareja Hoja de Salida ↔ pedimento → completa borrador o sustituye).

**Paralelización:** F7-PR1 ‖ (F7-PR2 → F7-PR3).

### Fase 8 — Carta Porte 3.1 (M)

- F8-PR1: `CartaPorte` (entidad propia, tipos T/I) + `carta_porte_mercancia`
  + `Vehiculo`/`Operador` (catálogos ERP). `EmitirCartaPorteCommand`.
- F8-PR2: Multi-tramo: `CrearSiguienteTramoCommand` (prellena, referencia
  tramo previo). Reglas de cuándo aplica (§7.6).

**Paralelización:** F8-PR1 → F8-PR2.

### Fase 9 — Venta de activos fijos (S/M)

- F9-PR1: Comportamiento fiscal venta de activo: validación de alta
  (`IActivosFijosReadPort` [stub]) + `AutorizarVentaActivoCommand` (rol
  Contador General).
- F9-PR2: Asientos de baja (conceptos `ACTIVO_FIJO`, `DEPRECIACION_ACUMULADA`,
  `UTILIDAD_/PERDIDA_VENTA_ACTIVO`) → evento a Contabilidad [stub].

**Paralelización:** F9-PR1 → F9-PR2.

### Fase 10 — Eventos de integración + write-back real (M)

- F10-PR1: Publicación vía Outbox: `FacturaVentaTimbradaEvent`,
  `FacturaAnticipoTimbradaEvent`, `NotaCreditoTimbradaEvent`,
  `ReciboPagoTimbradoEvent`, `ComprobanteCanceladoEvent` →
  Contabilidad/CxC [stubs consumidores].
- F10-PR2: `IContabilidadAsientoPort` [stub] (asientos por `ConceptoContable`,
  mapeo `TBD-*`).
- F10-PR3: **Write-back real a A+W** (claim + estatus 115/70) cuando el canal
  exista; ingesta Planta Pintura (`IPlantaPinturaPedidosReader`) cuando
  `Integraciones.Origenes` exista. Hasta entonces, stubs activos.

**Paralelización:** F10-PR1 → F10-PR2; F10-PR3 cuando los tracks C/A+W estén.

### Fase 11 — Reportes (M)

Motor nativo (ADR-0036), `<ReporteShell>`.

- F11-PR1: Liquidación de caja (facturado vs cobrado por forma de pago, NCs).
- F11-PR2: Estados de facturas de anticipo (export PDF/Excel).
- F11-PR3: `CfdisPorObraQuery` + `IFacturacionCfdiReadPort` (para Obras/CxC).

**Paralelización:** F11-PR1 ‖ F11-PR2 ‖ F11-PR3.

### Fase 12 — Wireup `Integraciones.Fiscal` fase 2 + hardening (M)

- F12-PR1: Reemplazar el **stub `IFiscalApiClient`** por la implementación
  real (timbrado/cancelación con complementos CCE/Carta Porte/Pago 2.0)
  cuando cierre el rediseño asíncrono. `ICsdProvider` real (CSD en Key Vault).
- F12-PR2: Sandbox FiscalAPI en dev/QA; producción solo en prod (variable de
  ambiente). Política de fallas (encolar + reintentos).
- F12-PR3: Limpieza de stubs restantes, hardening, revisión de
  `PLATFORM-TODO`, pruebas E2E del ciclo completo.

**Paralelización:** depende del cierre del track B.

---

## 5. Cronograma sugerido (2 devs backend + 1 frontend)

| Bloque | Fases | Estimado |
|---|---|---|
| Cimientos + skeleton | F0–F2 | ~2–3 semanas |
| Ingesta A+W + anticipos | F3–F4 | ~3–4 semanas |
| NC/cancelación + REPP | F5–F6 | ~2–3 semanas |
| Complementos (CCE/pedimento, Carta Porte) + activos | F7–F9 | ~3–4 semanas |
| Eventos/write-back + reportes | F10–F11 | ~2–3 semanas |
| Wireup Fiscal fase 2 + hardening | F12 | depende del track B |

Frontend (docs 05–07) corre en paralelo desde F1, una fase detrás del backend.

---

## 6. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| `Integraciones.Fiscal` fase 2 no cierra a tiempo → no hay timbrado real | Todo contra stub; el módulo queda funcional salvo el timbre real; F12 aislada al final |
| `Integraciones.Origenes` no existe → sin Planta Pintura/Salidas | Stubs de sus puertos; F7-PR3 y F10-PR3 cableables tarde sin reescribir |
| Master fiscal de Cliente/Producto incompleto al go-live | Auto-provisión A+W + bandeja de excepciones; no se factura sin clave SAT/RFC |
| Promoción del repo CFDI común rompe CxP | Migración coordinada en F2; si CxP no listo, repo local temporal (A7) |
| Atomicidad factura final + NC amortización con PAC asíncrono | FSM `TimbradoEnProceso`; `TimbradoPendienteWorker` resuelve ambos juntos |
| Contrato de la vista de datos del pedido A+W aún `[Gap]` | El esquema de la cola lo define el ERP; la vista de datos se acuerda en track A+W antes de F3-PR3 |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.1 | 2026-07-12 | Extensión post-plan: Anticipos ciclo completo + trazabilidad transversal — [`13-anticipos-ciclo-completo.md`](13-anticipos-ciclo-completo.md) (ANT-PR0..PR3, fuera de las fases F0–F13). |
| 1.0 | 2026-05-30 | Plan inicial. 13 fases (F0–F12), walking-skeleton-first, todo el timbrado contra stub hasta F12. Audit del repo confirma reuso de `Millet.Catalogos`, `Compartido.Series` (resuelve series/FANT), `SoftLockExpirationWorker`, y piezas CFDI de CxP para el repo común. Incorpora todas las decisiones del levantamiento/diseño: orígenes (A+W/Planta Pintura/manual), Salidas=pedimento, cola de solicitudes bidireccional (D18), no-parcial (D19), factura-manda (D20), pedimento condicional, master desde A+W. |
