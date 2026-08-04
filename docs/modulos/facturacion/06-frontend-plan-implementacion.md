# Plan de implementación de frontend — Módulo Facturación

> **Proyecto:** ERP Millet — Módulo 3 (Facturación CFDI 4.0).
> **Versión:** 1.0 — Fecha: 2026-05-30. Owner: Eduardo Paredes.
> Sobre [`05-frontend-diseno.md`](05-frontend-diseno.md). Corre una fase
> detrás del backend (`02-plan-implementacion.md`).

---

## 0. Cómo leer

Fases de frontend alineadas a las del backend. Cada fase FE se construye
contra los endpoints del backend ya mergeados (o contra tipos OpenAPI +
mocks si el endpoint aún no existe).

---

## 1. Resumen ejecutivo

**Objetivo:** UI v1 de Facturación con foco en velocidad de caja y
trazabilidad. **Estrategia:**

1. Foundation FE (shell, rutas, tipos OpenAPI) primero.
2. Walking skeleton: bandeja de pedidos → captura manual → emisión (contra el
   stub de timbrado del backend).
3. Reuso de componentes compartidos (Sheet, master-detail, `LineaInlineForm`,
   `<ReporteShell>`).
4. Las pantallas de complementos (CCE, Carta Porte) y reportes al final.

---

## 2. Prerrequisitos

- Backend F0–F1 mergeado (smoke, permisos, captura manual, emisión stub).
- Kit de UI compartido (Sheet, master-detail, topbar, `LineaInlineForm`).
- Pipeline OpenAPI → tipos TS activo para `Millet.Facturacion`.
- `<ReporteShell>` + utilitarios de export (ADR-0036).

---

## 3. Fases

### Fase 0 FE — Foundation (S)

Shell del módulo, rutas base (`/facturacion/...`), navegación (modal de cards
del shell, memoria [project_nav_shell_pattern]), tipos OpenAPI, guards por
permiso. Quick Create popover.

### Fase 1 FE — Pedidos + captura manual + emisión skeleton (M)

Bandeja `/pedidos` (filtros server-side), Sheet "Nuevo pedido manual" (líneas
inline + ETag), pantalla de emisión con cobro multi-forma, bandeja `/facturas`
+ detalle básico. Contra el stub de timbrado.

### Fase 2 FE — Detalle del comprobante + PDF/envío (M)

`/facturas/$id` con cadena de relaciones CFDI, historial de comprobantes del
pedido, bitácora de envío, descargas XML/PDF (bilingüe/térmica), reenviar
correo. Print CSS (`data-print`).

### Fase 3 FE — Ingesta A+W: bandejas (M)

Bandeja de pedidos facturables con badges de origen/estado, bandeja de
excepciones de ingesta con resolución inline.

### Fase 4 FE — Anticipos (M)

Pantalla de emisión de anticipo, vinculación en la emisión de factura final,
**Control de Anticipos** (Resumen + Detallada por cliente) con `<ReporteShell>`.

### Fase 5 FE — NC bonificación + cancelación (M)

Acción NC bonificación (Caja general), flujo de cancelación (motivo SAT, UUID
sustituto), visualización del estado de la solicitud y re-facturación.

### Fase 6 FE — REPP + reparto (M)

Emisión de complemento de pago (multi-factura), pantalla de liquidación de
ruta (reparto), aplicación de cobros por cliente.

### Fase 7 FE — Exportación/CCE + pedimento (M)

Campos de CCE en la emisión de exportación, indicador/captura
`requiere_pedimento`, vista de facturas en `PendientePedimento`.

### Fase 8 FE — Carta Porte (M)

Bandeja + Sheet de Carta Porte (mercancías inline), acción "Crear siguiente
tramo".

### Fase 9 FE — Activos fijos + reportes (M)

Flujo de autorización del Contador General; reportes Liquidación de caja y
Estados de facturas de anticipo.

### Fase 10 FE — Hardening (S)

A11y (operación de caja por teclado), estados de error/loading, pulido de
print, revisión de permisos en UI.

---

## 4. Cronograma (1 dev frontend)

Una fase FE por detrás del backend; ~la misma cadencia. Bloques: F0–F2 (~2–3
sem), F3–F5 (~3 sem), F6–F9 (~3–4 sem), F10 (~1 sem).

---

## 5. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| Timbrado real (Fiscal fase 2) no listo → UI muestra stub | UI agnóstica al resultado; muestra UUID y estados de la FSM |
| Pantalla de cobro lenta en caja | Optimizar para teclado; pocos pasos; sin recargas innecesarias |
| Inconsistencia con patrones de Compras | Reusar el kit compartido; revisar contra `patrones-compras.md` |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-05-30 | Plan de frontend inicial. |
