# Diseño de frontend — Módulo Facturación

> **Proyecto:** ERP Millet — Módulo 3 (Facturación CFDI 4.0).
> **Versión:** 1.0 — Fecha: 2026-05-30. Owner: Eduardo Paredes.
> Replica los patrones de UI del exemplar Compras
> ([`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md))
> y la memoria [project_estructura_ventanas_erp].

---

## 0. Cómo leer

UI del módulo: rutas, pantallas, componentes. El detalle de negocio vive en
[`01-diseno.md`](01-diseno.md). Aquí solo la capa de presentación.

---

## 1. Posicionamiento

Facturación es un módulo de **operación de caja + back-office fiscal**. El
usuario principal es el **Cajero** (emite, cobra, imprime), con roles
secundarios Caja general (NCs bonificación), CxC (instruye anticipos),
Contador General (autoriza activos). La UI prioriza:

- **Velocidad en caja**: cargar pedido → cobrar → emitir en pocos pasos.
- **Bandejas** de pedidos facturables y de excepciones de ingesta.
- **Trazabilidad**: cadena de relaciones CFDI, historial de comprobantes por
  pedido, bitácora de envío.

---

## 2. Stack frontend

- React + TypeScript, TanStack Router + Query (igual que Compras/CxP).
- Tipos generados desde OpenAPI del backend.
- Reportes con motor nativo (ADR-0036): `<ReporteShell>` + `@react-pdf/renderer`
  (PDF) + `exceljs` (Excel).
- Sheet (slide-from-right), inline forms, master-detail — del kit compartido.

---

## 3. Asunciones a confirmar

- A1: la impresión térmica (versión simplificada) usa CSS print +
  `data-print` (no un driver especial).
- A2: el cobro multi-forma se captura en una sola pantalla antes de emitir.
- A3: la captura manual de pedido reusa el componente de líneas de
  Requisiciones (memoria [feedback_reutilizacion_codigo]).

---

## 4. Rutas (TanStack Router)

```
/facturacion
  /pedidos                      (P2 — bandeja de pedidos facturables, filtrada server-side)
  /pedidos/excepciones          (bandeja de excepciones de ingesta)
  /pedidos/$id                  (detalle/edición de pedido; manual = editable)
  /facturas                     (P1 — bandeja de comprobantes emitidos)
  /facturas/$id                 (P3 — master-detail del comprobante + cadena CFDI)
  /anticipos                    (Control de Anticipos — resumen)
  /anticipos/$clienteId         (estado de cuenta del cliente)
  /carta-porte                  (bandeja de Carta Portes)
  /reportes/liquidacion-caja
  /reportes/estados-anticipos
```

Quick Create (popover del topbar): "Nueva factura", "Nuevo anticipo",
"Nuevo pedido manual", "Nueva Carta Porte".

---

## 5. Patrones de UI aplicables

- **Master-detail** (lista 320px sticky + panel detalle). Mobile drill-down.
- **Sheet** para "Nueva ..." (factura/anticipo/NC/Carta Porte/pedido manual);
  confirm al cerrar con `isDirty`; `Force: true` en success.
- **Inline forms** (sin modal) para líneas de factura, mercancías de Carta
  Porte y formas de pago — estilo `LineaInlineForm` de RQ (memoria
  [feedback_inline_no_modal_para_items]).
- **Sub-topbar** del detalle sticky con `data-print="hidden"`; aside master
  `data-print="hidden"` para impresión limpia del CFDI.
- **Topbar global**: search contextual por ruta (debounce 200ms), Quick Create,
  ayuda contextual.

---

## 6. Pantallas específicas de Facturación

### 6.1 Bandeja de pedidos facturables (`/pedidos`)

Tabla filtrada server-side (canal, sucursal, estado, origen A+W/Manual). Acción
**"Facturar"** abre el flujo de emisión (toma soft-lock). Botón **"Nuevo
pedido"** (captura manual). Badge de origen y de `requiere_pedimento`.

### 6.2 Nuevo/editar pedido manual (Sheet)

Encabezado (cliente, canal, comportamiento fiscal, moneda, Obra) + **líneas
inline** (producto/servicio, cantidad, precio, descuento, retención, checkbox
`requiere_pedimento`). ETag/If-Match al guardar. Reusa el componente de líneas
de Requisiciones.

### 6.3 Emisión de factura

Carga el pedido → muestra **comentarios del origen en panel aparte** (nunca al
XML) → cálculo `total − bonificación` → **cobro multi-forma** en una pantalla
(efectivo/transferencia/tarjeta: solo 4 últimos dígitos) → emitir. Si hay
bonificación, ofrece NC inmediata; si PPD/multipago, REPP. Estados de la FSM
visibles (`Borrador`/`TimbradoEnProceso`/`Timbrado`/`PendientePedimento`).

### 6.4 Detalle del comprobante (`/facturas/$id`)

Datos fiscales, **cadena de relaciones CFDI** (anticipos 07, NC 01, sustitución
04), **historial de comprobantes del pedido** (cancelados + vigente), bitácora
de envío, descargas XML/PDF (bilingüe / térmica), acción Cancelar (motivo SAT,
UUID sustituto) y Reenviar correo.

### 6.5 Control de Anticipos

Resumen (filtros cliente/fechas/obra/estado, sumatorias) + Detallada por
cliente (anticipos, facturas vinculadas, NCs, saldo). Export PDF/Excel.

### 6.6 Carta Porte

Bandeja + Sheet de captura (tramo, vehículo, operador, mercancías inline) +
acción **"Crear siguiente tramo"** (prellena y referencia el tramo previo).

### 6.7 Bandeja de excepciones de ingesta

Lista por motivo (`cliente_no_existe`, `producto_sin_clave_sat`,
`almacen_no_asignado`…). Resolución inline / re-procesar.

---

## 7. Componentes reutilizables

- `LineaInlineForm` (de RQ) → líneas de factura y mercancías Carta Porte.
- `FormasPagoInline` (nuevo) → cobro multi-forma con 4 últimos dígitos.
- `CadenaCfdi` (nuevo) → visualización de la cadena de relaciones.
- `<ReporteShell>` (compartido) → Control de Anticipos, Liquidación de caja.
- Sheet/master-detail/topbar del kit compartido.

---

## 8. Estados, errores, loading

- Loading skeletons en bandejas; optimistic en edición de líneas.
- Errores Problem Details (ADR-0010) mapeados a toasts/inline.
- 412 (ETag) → "el pedido cambió, recarga". Soft-lock activo → banner "otro
  usuario está facturando este pedido".
- Estados de timbrado asíncrono (`TimbradoEnProceso`) con spinner + polling.

---

## 9. Permisos en UI

Ocultar/deshabilitar acciones por permiso `facturacion.*`: emitir, anticipos
(emitir/vincular), NC bonificación, Carta Porte, REPP, cancelaciones,
`activos.autorizar` (solo Contador General), reportes, caja.liquidar.

---

## 10. Reportes (vista frontend)

`<ReporteShell>` con contrato JSON del backend (`{titulo, generadoEn,
filtrosAplicados, columnas, filas, totales}`). Export PDF/Excel client-side.
Liquidación de caja, Estados de facturas de anticipo, Control de Anticipos.

---

## 11. Accesibilidad

Navegación por teclado en caja (cobro rápido), focus management en Sheets,
labels ARIA, contraste AA. La pantalla de cobro debe operarse sin mouse.

---

## 12. Internacionalización

UI en español. **PDF de factura bilingüe ES/EN** (no la UI). Versión
simplificada térmica solo español.

---

## 13. Brechas con el backend

- El timbrado real depende de `Integraciones.Fiscal` fase 2 — la UI muestra el
  resultado del stub hasta entonces (UUID fake visible en dev).
- Bandeja de excepciones y cola de solicitudes A+W dependen de los workers de
  ingesta (F3).
- Carta Porte/CCE dependen de los complementos del PAC (F7/F8/F12).

> **Tracker vivo de deuda y diferidos:**
> [`10-frontend-deuda-y-diferidos.md`](10-frontend-deuda-y-diferidos.md)
> consolida, por categoría, las brechas de backend que bloquean FE
> (`GET /pedidos-facturables/{id}`, maestro de clientes, producto fiscal,
> emitir-desde-pedido), los diferidos de FE (selectores de catálogo SAT) y
> los diferidos por fase (cobro multi-forma, cadena CFDI, descargas). Se
> actualiza en cada PR de `facturacion-fe/*`.

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-05-30 | Diseño de frontend inicial. |
