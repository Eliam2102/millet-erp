# Diseño de frontend — Módulo Cuentas por Cobrar

> **Versión:** v0.1 (borrador)
> **Fecha:** 2026-07-13
> **Depende de:** [`01-diseno.md`](01-diseno.md) · [`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md)

---

## 0. Cómo leer

Aplica los patrones cross-módulo del exemplar de Compras (P1 bandeja, P2 bandeja
filtrada, P3 master-detail, P4 sheet "Nueva…") y el patrón de reportes de
ADR-0036. Aquí solo se documenta lo específico de CxC; la estructura de ventanas
no se reinventa.

## 1. Posicionamiento

- **Ruta base:** `/cxc` (los segmentos de ruta usan siglas, como `/cxp`; el
  código/permisos usan `cuentas_por_cobrar`).
- **Nav shell:** card "Cuentas por Cobrar" en la sección Finanzas del modal de
  cards, visible si el usuario tiene algún permiso `cuentas_por_cobrar.*`.
- **Usuarios:** Gerente de Crédito y Cobranza (Prida), Coordinador nacional,
  Encargado internacional, Ingresos (confirmación interina).

## 2. Rutas (TanStack Router)

| Ruta | Patrón | Contenido |
|---|---|---|
| `/cxc` | landing | Cards de acceso + indicadores rápidos (vencido, alertas abiertas) |
| `/cxc/lineas-credito` | P1 | Bandeja de líneas (cliente, moneda, límite, disponible, estado) |
| `/cxc/lineas-credito/$id` | P3 | Detalle master-detail: línea + crédito disponible (con banner `datoIncompleto`) + historial de decisiones del cliente |
| `/cxc/liberaciones` | P2 | Bandeja de decisiones; acción "Decidir liberación" |
| `/cxc/cobranza` | P2 | Seguimientos por cliente (filtro cliente obligatorio) + registrar gestión |
| `/cxc/cartera` | reporte | Antigüedad de saldos con `<ReporteShell>` (buckets configurables, export PDF/Excel) |
| `/cxc/estado-cuenta` | reporte | Estado de cuenta por cliente con `<ReporteShell>` |
| `/cxc/anticipos` | P2 | Lista filtrable de saldos de anticipo (reemplazo del "mapa" A+W; datos del read port) |
| `/cxc/aplicaciones` | P1 + P3 | Bandeja de propuestas + detalle con matching depósito↔facturas |
| `/cxc/alertas` | P2 | Bandeja de alertas (tipo, cliente, disparada, atender) |

## 3. Patrones aplicados

- **Sheet (P4)** para: "Nueva línea de crédito", "Registrar gestión de
  cobranza", "Nueva propuesta de aplicación". Provider a nivel shell
  (`useNuevaLineaCredito().abrir()` etc.), confirm al cerrar con `isDirty`.
- **Inline forms (nunca modal)** para las filas factura↔importe dentro de la
  propuesta de aplicación (estilo `LineaInlineForm` de RQ: dashed primary al
  agregar, amber al editar).
- **Decidir liberación** es un flujo de acción, no un form largo: panel con
  pedido, cliente, crédito disponible (snapshot), regla que aplica y resultado
  propuesto; el botón cambia según el caso (Liberar / Retener / Liberar con
  override → selector de autorización vigente).
- **Sub-topbar sticky** en detalles con `data-print="hidden"`; asides master
  también, para impresión limpia de estados de cuenta.
- **Topbar search** contextual por ruta (cliente en cartera/cobranza, folio en
  liberaciones) con debounce 200 ms; Quick Create con las 3 acciones del sheet.

## 4. Pantallas específicas

### 4.1 Matching de aplicación de pago
Dos columnas: depósito (monto, moneda, referencia, remittance) a la izquierda;
facturas abiertas del cliente a la derecha con checkbox + importe editable
(parcialidades). Footer con `Σ aplicado`, `diferencia` y, si `|diferencia| <
tolerancia`, toggle "ajuste no fiscal (comisión bancaria)" con leyenda de que
**no** genera CFDI. Sin remittance → el botón Proponer queda deshabilitado con
tooltip de la regla.

### 4.2 Crédito disponible con dato incompleto
Mientras G1 siga abierto, el número se muestra con badge ámbar "sin material
liberado A+W" (tooltip explica el término faltante). No se oculta el dato: se
enseña qué le falta.

### 4.3 Override consumible
Calco de la UX de apertura de caja: el gerente crea la autorización (cliente/
pedido, motivo, vigencia) desde `/cxc/liberaciones`; el operador la ve listada
como "vigente" al decidir y la consume. Nunca captura de contraseña ajena.

## 5. Componentes reutilizables

- **Se hereda:** `<ReporteShell>` + export utils, badges de estado, tablas
  paginadas server-side, `EmptyState`, patrones de permisos (`usePermisos`),
  formato moneda (multi-moneda sin conversión: cada monto con su divisa).
- **Nuevos de CxC:** `<CreditoDisponibleCard>` (número + badge dato incompleto),
  `<BucketAntiguedadPill>`, `<MatchingAplicacionTable>`, `<TimelineCobranza>`
  (historial de gestiones con canal/resultado).

## 6. Estados, errores, permisos

- Loading skeleton por bandeja; errores RFC 7807 → toast con `detail`;
  vacíos con CTA (p. ej. "Sin gestiones para este cliente — Registrar la
  primera").
- Permisos en UI espejo del backend: `lineas-credito.gestionar` habilita
  crear/editar/bloquear; `liberacion.decidir` la acción de liberar;
  `liberacion.override` la creación de autorizaciones; `aplicacion-pago.confirmar`
  los botones de Ingresos; `cartera.leer` gobierna las rutas de reportes.
- Enums espejeados del backend (mirror obligatorio al agregar valores —
  incidente 2026-07-11).

## Rev.

| Versión | Fecha | Cambio |
|---|---|---|
| v0.1 | 2026-07-13 | Borrador inicial |
