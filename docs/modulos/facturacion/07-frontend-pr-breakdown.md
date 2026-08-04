# PR Breakdown de frontend — Módulo Facturación

> **Proyecto:** ERP Millet — Módulo 3 (Facturación CFDI 4.0).
> **Versión:** 1.0 — Fecha: 2026-05-30. Owner: Eduardo Paredes.
> Desglose de PRs del [`06-frontend-plan-implementacion.md`](06-frontend-plan-implementacion.md).

---

## 0. Cómo leer

PRs de frontend, mergeables, consistentes con los patrones de Compras.
Branch `facturacion-fe/fN-...` (patrón de auto-mode). "AC" = criterio de aceptación.

---

## Fase 0 FE — Foundation (1 PR · S)

**FE-F0-PR1.** Shell `/facturacion`, rutas base, tipos OpenAPI, guards por
permiso, Quick Create popover, entrada en el modal de cards del shell.
*AC:* navegación a rutas vacías con guards; tipos TS generados.

## Fase 1 FE — Pedidos + captura manual + emisión (1 PR · M)

**FE-F1-PR1.** Bandeja `/pedidos`, Sheet "Nuevo pedido manual" (líneas inline +
ETag), pantalla de emisión con cobro multi-forma, bandeja `/facturas` + detalle
básico. *AC:* crear pedido manual y emitir (stub) end-to-end.

## Fase 2 FE — Detalle + PDF/envío (1 PR · M)

**FE-F2-PR1.** `/facturas/$id` con `CadenaCfdi`, historial de comprobantes,
bitácora de envío, descargas XML/PDF (bilingüe/térmica), reenviar correo,
print CSS. *AC:* detalle muestra cadena y permite descargar/imprimir limpio.

## Fase 3 FE — Bandejas de ingesta (1 PR · M)

**FE-F3-PR1.** Bandeja de pedidos facturables (badges origen/estado) + bandeja
de excepciones con resolución inline. *AC:* excepción se resuelve desde la UI.

## Fase 4 FE — Anticipos (2 PRs · M)

**FE-F4-PR1.** Emisión de anticipo + vinculación en factura final.
**FE-F4-PR2.** Control de Anticipos (Resumen + Detallada) con `<ReporteShell>`.
*AC:* vincular anticipo respeta saldo; reporte exporta PDF/Excel.

## Fase 5 FE — NC bonificación + cancelación (1 PR · M)

**FE-F5-PR1.** Acción NC bonificación, flujo de cancelación (motivo/sustituto),
estado de solicitud, re-facturación tras cancelar. *AC:* cancelar deja el
pedido re-facturable visible en bandeja.

## Fase 6 FE — REPP + reparto (1 PR · M)

**FE-F6-PR1.** Emisión REPP multi-factura + liquidación de ruta. *AC:* un REPP
cubre varias facturas; conciliación de ruta por cliente.

## Fase 7 FE — Exportación/CCE + pedimento (1 PR · M)

**FE-F7-PR1.** Campos CCE en emisión de exportación, captura/indicador
`requiere_pedimento`, vista `PendientePedimento`. *AC:* factura de exportación
con CCE; retención visible solo de las que requieren pedimento.

## Fase 8 FE — Carta Porte (1 PR · M)

**FE-F8-PR1.** Bandeja + Sheet Carta Porte (mercancías inline) + "Crear
siguiente tramo". *AC:* segundo tramo referencia al primero.

## Fase 9 FE — Activos + reportes (1 PR · M)

**FE-F9-PR1.** Autorización Contador General + reportes Liquidación de caja y
Estados de facturas de anticipo. *AC:* sin autorización no se permite emitir
venta de activo.

## Fase 10 FE — Hardening (1 PR · S)

**FE-F10-PR1.** A11y (caja por teclado), estados error/loading, pulido print,
permisos en UI. *AC:* operación de caja sin mouse; checklist a11y.

---

## Extensión — Anticipos ciclo completo (doc 13)

Bandeja + detalle de facturas de anticipo (P1/P3), descargas XML/PDF,
acciones de ciclo de vida y trazabilidad transversal en todos los detalles
del módulo. PRs `facturacion-fe/anticipos-pr2-bandeja-detalle` y
`facturacion-fe/anticipos-pr3-trazabilidad` — diseño y AC en
[`13-anticipos-ciclo-completo.md`](13-anticipos-ciclo-completo.md) §6/§8.

---

## Resumen de granularidad

| Fase | PRs |
|---|---|
| F0–F3 | 4 |
| F4 | 2 |
| F5–F10 | 6 |
| **Total** | **~12 PRs** |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-05-30 | Desglose de PRs de frontend inicial. |
| 1.1 | 2026-07-12 | Extensión Anticipos ciclo completo (doc 13): FE PR2/PR3. |
