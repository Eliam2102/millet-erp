# PR Breakdown — Módulo Facturación (`Millet.Facturacion`)

> **Proyecto:** ERP Millet — Módulo 3 (Facturación CFDI 4.0).
> **Versión:** 1.1 — Fecha: 2026-05-30. Owner: Eduardo Paredes.
> Consolida los work-items del [`02-plan-implementacion.md`](02-plan-implementacion.md)
> en PRs **right-sized** (memoria [feedback_pr_granularidad]): se agrupan los
> XS afines y solo se aísla cuando hay **riesgo real** (cross-port, hot path,
> migración cross-module, dependencia externa).

---

## 0. Cómo leer

- Cada fila "PR" es un PR real, mergeable, deja `/health/ready` verde.
- "Bundle" = los work-items del `02-plan` que agrupa.
- Aislamiento justificado solo donde se indica **[aísla: razón]**.
- Tamaño: S (≤1 día), M (1–3 días), L (3–5 días).
- Branch `facturacion/fN-...` (patrón de auto-mode; ver hook
  `validate-auto-merge.ps1`). Nunca directo a `main`.

---

## Fase 0 — Foundation (1 PR · S)

**F0** — Bundle: PR1+PR2+PR3+PR4. csproj + folders + smoke; `FacturacionDbContext`
+ schema + migración (registro en `MigrationsHealthCheckOptions` y
`deploy-app-dev.yml`); permisos `facturacion.*` + migración en
`IdentidadDbContext`; puertos de lectura + stubs (`IFiscalApiClient`,
`ICsdProvider`, `ICfdiRepositorioPort`, `IPeriodoContablePort`,
`ICatalogosSatReadPort` real). *AC:* smoke 200/403; `/health/ready` verde;
permisos asignables; DI resuelve todos los puertos.

> Todo es andamiaje afín → 1 PR (igual que el exemplar CxP).

---

## Fase 1 — Walking skeleton (2 PRs · M)

**F1-PR1 — Dominio + emisión stub.** Bundle: plan PR1+PR3. `Comprobante` (TPT)
+ `FacturaVenta` + líneas + VOs + FSM timbrado; `EmitirFacturaVentaCommand`
contra `IFiscalApiClient` stub; folio vía `Compartido.Series`; candado de
período. *AC:* emitir factura nominal simple → `Timbrado` con UUID (fake).

**F1-PR2 — Captura manual + bandejas.** Bundle: plan PR2+PR4. `PedidoFacturable`
(binario) + captura manual (`Crear`/`Editar`, ETag, líneas inline) + bandejas
(`pedidos`, `facturas`, detalle con cadena CFDI). *AC:* crear/editar pedido
manual (412 al choque); la factura aparece en bandeja.

---

## Fase 2 — Repo CFDI común + PDF/envío (2 PRs · M)

**F2-PR1 — Repo CFDI común.** Plan PR1. `cfdi_archivo` en `Integraciones.Fiscal`
+ `ICfdiRepositorioPort`; promover `UuidCfdi`/`IXmlCfdiParser`/`ICfdiBlobStorage`;
**migración de `CfdiRecibido` en CxP**. **[aísla: migración cross-module con
CxP].** *AC:* Facturación y CxP usan el mismo repo; migración CxP verde.

**F2-PR2 — PDF + envío.** Bundle: plan PR2+PR3. `PdfFacturaRenderer` bilingüe +
térmica; `BitacoraEnvioCorreo` + `EnvioCfdiCorreoWorker` + `INotificacionService`
[stub] + reenviar. *AC:* PDF en ambos formatos; bitácora registra envío.

---

## Fase 3 — Ingesta A+W (2 PRs · M-L)

**F3-PR1 — Cola + worker + matriz + excepciones.** Bundle: plan PR1+PR2+PR4.
`aw_solicitud_pedido` (esquema ERP) + `ingesta_control` + snapshot +
`AwSolicitudesWorker` + matriz operación×estado + soft-lock + bandeja de
excepciones. Readers/write-back A+W [stub]. *AC:* Alta crea pedido+claim;
Modif refresca; Cancel sobre `Facturado` → alerta; `Bloqueado` pospone.

**F3-PR2 — Provisión de master.** Plan PR3. `IMasterProvisioningPort`
(`EnsureClienteDesdeAw`/`EnsureArticuloDesdeAw`) + readers de master A+W [stub→
DatosMaestros]. **[aísla: cross-port a DatosMaestros].** *AC:* faltante A+W →
auto-provisión (stub); faltante Planta Pintura → excepción.

---

## Fase 4 — Anticipos (2 PRs · M-L)

**F4-PR1 — Anticipo + vinculación + control.** Bundle: plan PR1+PR2+PR4.
`FacturaAnticipo` (serie `FANT`) + `Anticipo` (saldo) + `VincularAnticipoCommand`
(relación 07) + reporte Control de Anticipos. *AC:* no vincula más que el saldo
cobrado; reporte muestra saldos.

**F4-PR2 — NC de amortización atómica.** Plan PR3. Autogenera + timbra
`NotaCredito` en la misma transacción que la factura final; relaciona anticipo
+ factura final. **[aísla: hot path transaccional — atomicidad crítica].**
*AC:* rollback total si falla un paso; saldo se reduce correctamente.

---

## Fase 5 — NC bonificación + cancelación (2 PRs · M)

**F5-PR1 — NC por bonificación.** Plan PR1. `NotaCredito` motivo bonificación
(relación 01). *AC:* no aparece en el XML de la venta; bloqueada si origen
cancelado.

**F5-PR2 — Cancelación SAT + re-facturación.** Bundle: plan PR2+PR3.
`SolicitudCancelacion` (FSM) + `SolicitarCancelacionCommand` + poller +
validación de cadena; al cancelar el CFDI vigente el pedido vuelve a
`Importado`. *AC:* no cancela anticipo amortizado sin cancelar sus NCs; pedido
re-facturable tras cancelar.

---

## Fase 6 — REPP (1 PR · M)

**F6** — Bundle: plan PR1+PR2+PR3. `ReciboPago` (Pago 2.0, multi-factura,
parcialidades) + diferencia cambiaria automática + automatización
(`PagoClienteConfirmadoEvent` [stub]) + flujo reparto. *AC:* un REPP cubre
varias facturas; ganancia/pérdida cambiaria correcta.

---

## Fase 7 — Exportación/CCE + pedimento (2 PRs · M-L)

**F7-PR1 — CCE.** Plan PR1. `complemento_cce` + líneas (IVA 0%, INCOTERM, TC
DOF, `XEXX`). *AC:* factura de exportación con CCE válido (stub).

**F7-PR2 — Pedimento condicional + Salidas.** Bundle: plan PR2+PR3.
`requiere_pedimento` (línea/cabecera) + estado `PendientePedimento` +
`AplicarPedimentoCommand` + `ISalidasPedimentosReader` [stub] +
`PedimentoSalidasWorker`. *AC:* sin `requiere_pedimento` timbra directo; con él
y sin pedimento se retiene solo esa factura.

---

## Fase 8 — Carta Porte (1 PR · M)

**F8** — Bundle: plan PR1+PR2. `CartaPorte` (T/I) + mercancías + catálogos
`Vehiculo`/`Operador` + multi-tramo (`CrearSiguienteTramoCommand`). *AC:* CP
T e I; segundo tramo referencia al primero.

---

## Fase 9 — Venta de activos fijos (1 PR · S-M)

**F9** — Bundle: plan PR1+PR2. Validación de alta (`IActivosFijosReadPort`
[stub]) + `AutorizarVentaActivoCommand` (Contador General) + asientos de baja.
*AC:* no timbra sin autorización; genera conceptos de baja.

---

## Fase 10 — Eventos + write-back (2 PRs · M)

**F10-PR1 — Eventos + Contabilidad stub.** Bundle: plan PR1+PR2. Publicación
vía Outbox de los 5 eventos + `IContabilidadAsientoPort` [stub] (mapeo `TBD-*`).
*AC:* eventos en Outbox y publicados.

**F10-PR2 — Write-back real + Planta Pintura.** Plan PR3. `IAwWriteBackPort`
real (claim + estatus 115/70) + `IPlantaPinturaPedidosReader` (cuando
`Integraciones.Origenes` exista). **[aísla: dependencia externa de los tracks
A+W/Origenes].** *AC:* claim/estatus reflejados; orden Planta Pintura ingestada.

---

## Fase 11 — Reportes (1 PR · M)

**F11** — Bundle: plan PR1+PR2+PR3. Liquidación de caja + Estados de facturas
de anticipo + `CfdisPorObraQuery`/`IFacturacionCfdiReadPort`. *AC:* contrato
JSON ADR-0036; export client-side. (Reportes afines → 1 PR.)

---

## Fase 12 — Wireup Fiscal fase 2 + hardening (3 PRs · M) — ejecutado

**F12-PR1 — Contrato estructurado + emisor (#499).** Puerto
`ICfdiTimbradoPort` movido a `Integraciones.Fiscal` con el contrato
PAC-neutral `CfdiEmision` (+ complementos Pago/CCE/Carta Porte);
`CfdiEmisionBuilder` + `TimbradoEjecutor` centralizan la construcción desde
los agregados; `EmisorSnapshot` resuelve razón social + LugarExpedicion del
master sin quemar folio. *AC:* todos los handlers de emisión pasan por el
builder; fecha en zona fiscal; split Serie/Folio.

**F12-PR2 — Adapter real + worker + flag temporal (#500).**
`FiscalApiTimbradoAdapter` sobre el SDK oficial (timbrar/cancelar/estatus,
semántica Timbrado/Fallido/EnProceso) detrás del flag
`Facturacion:Timbrado:UsarPacReal` (dev = true, sandbox);
`TimbradoPendienteWorker` resuelve pendientes ambiguos (`PAC_TIMEOUT`, nunca
re-timbra automático). CCE/Carta Porte rechazados con
`COMPLEMENTO_NO_SOPORTADO` hasta PR3. *AC:* timbre real en sandbox FiscalAPI.

**F12-PR3 — CCE 2.0 + Carta Porte 3.1 completos + limpieza (este PR).**
Mapeo de `Invoice.Complement` (ComercioExterior + CartaPorte) en el adapter;
dominio gana los datos que el SAT exige (domicilio de ubicaciones CP 3.1,
peso bruto vehicular, domicilio del receptor CCE, clave de pedimento,
certificado de origen) con validación pre-vuelo en el builder
(`CARTA_PORTE_DATOS_SAT_INCOMPLETOS` / `CCE_DOMICILIO_RECEPTOR_INCOMPLETO`,
sin quemar folio ni timbre); se eliminan `StubFiscalApiClient`,
`StubCsdProvider`, `ICsdProvider` y el flag (registro incondicional del
adapter real; el flag salió de Bicep). *AC:* `rg "PLATFORM-TODO"` solo deja
los que dependen de módulos aún inexistentes.

---

## Fase 13 — Reintento de timbrado sobre el mismo comprobante (2 PRs · M) — [Decisión 01-G]

Diseño completo en `01-diseno.md` §2.bis (sub-decisiones G1–G7). Modelo:
**1 pedido → 1 documento → N intentos**; el folio se asigna una vez y el
reintento nunca consume consecutivo nuevo. G1/G2/G6/G7 (reintento genérico
para los cinco tipos, compuerta `confirmarNoDuplicado`, banner FE) llegaron
en **#530** fuera de fase; la Fase 13 completa G3/G4/G5.

**F13-PR1 — Backend: Descartada + bitácora + G5 + migración de datos.**
- **G3 — FSM** (`Domain/Comprobantes/Comprobante.cs`): `Descartar()`
  (`TimbradoFallido → Descartada`, terminal). Nuevo valor
  `EstadoTimbrado.Descartada = 8` ⇒ espejo del enum en el frontend en el
  mismo PR (memoria incidente #526/#528; `comprobante.estado` no tiene
  check constraint). Endpoint `POST /comprobantes/{id}/descartar`
  (`DescartarComprobanteCommand`, permiso nuevo
  `facturacion.comprobantes.descartar` + migration en `IdentidadDbContext`):
  libera el pedido si esta factura lo tenía tomado (`RevertirAFacturable` +
  write-back `SinFacturar` si origen A+W) y cancela el `Anticipo` si el
  comprobante es factura de anticipo.
- **G4 — Bitácora** `bitacora_intento_timbrado` (entidad + configuration +
  migración; patrón `BitacoraEnvioCorreo`). `TimbradoEjecutor` escribe una
  fila por llamada al PAC (emisión y reintento comparten el punto único).
  Consulta `GET /comprobantes/{id}/intentos-timbrado`.
- **G5 — emisión** (`EmitirFacturaVentaHandler.cs`): el pedido se marca
  `Facturado` siempre que la factura persiste — se elimina la excepción
  `factura.Estado != TimbradoFallido`. `ReintentarTimbradoHandler` dispara
  el write-back A+W también cuando el pedido ya estaba tomado por esta
  factura (antes solo lo tomaba si seguía `Importado`).
- **Migración de datos históricos** (misma migration de la bitácora):
  fallidas cuyo pedido fue tomado por otro comprobante o cancelado →
  `Descartada`; pedido `Importado` con varias fallidas → se conserva la más
  reciente; pedido `Importado` con una fallida viva → backfill G5 (queda
  tomado por ella).
- *AC:* un rechazo del PAC nunca libera el pedido; descartar libera el
  pedido y quema el folio de forma auditada; la bitácora acumula los
  intentos; no queda camino que emita factura nueva por fallo de timbrado.

**F13-PR2 — Frontend: acciones + historial.**
- `features/facturacion/pages/DetalleFactura.tsx`: acción **Descartar**
  (confirm destructivo, junto al banner de reintento de #530); panel
  "Historial de intentos" (`GET /comprobantes/{id}/intentos-timbrado`).
- *AC:* el operador resuelve un rechazo del PAC sin salir del detalle de la
  factura; las fallidas/descartadas se distinguen visualmente de los CFDI.

Alcance diferido — **absorbido por
[`13-anticipos-ciclo-completo.md`](13-anticipos-ciclo-completo.md)**: la
factura de anticipo gana página propia (bandeja + detalle, descargas XML/PDF,
Cancelar/Reintentar/Descartar vía los componentes compartidos de #530/#535)
y trazabilidad transversal. La NC sigue sin página propia (se opera desde los
documentos que la generan; descargas XML/PDF sí — doc 13 §5).

---

## Resumen de granularidad

| Fase | PRs | Aislamientos justificados |
|---|---|---|
| F0 | 1 | — |
| F1 | 2 | — |
| F2 | 2 | repo CFDI común (cross-module CxP) |
| F3 | 2 | provisión master (cross-port) |
| F4 | 2 | NC amortización (hot path) |
| F5 | 2 | — |
| F6 | 1 | — |
| F7 | 2 | — |
| F8 | 1 | — |
| F9 | 1 | — |
| F10 | 2 | write-back real (dependencia externa) |
| F11 | 1 | — |
| F12 | 2 | timbrado real (dependencia externa) |
| F13 | 2 | enum persistido + migración de datos (backend) / UI (frontend) |
| **Total** | **23 PRs** | ~1.6 PRs/fase (en línea con CxP) |

Los work-items `FN-PRx` del `02-plan` se **agrupan** en estos 21 PRs; solo se
aísla cuando hay riesgo real (4 casos arriba).

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-05-30 | Desglose inicial (~41 PRs — demasiado granular). |
| 1.1 | 2026-05-30 | **Consolidado a 21 PRs** (memoria [feedback_pr_granularidad]): se agrupan XS afines; se aísla solo por riesgo real (repo CxP, provisión master, NC amortización atómica, write-back/timbrado real). |
| 1.2 | 2026-07-12 | **Fase 13** — reintento de timbrado sobre el mismo comprobante ([Decisión 01-G]): FSM `TimbradoFallido → Borrador \| Descartada`, bitácora de intentos, folio único por documento. 23 PRs. |
