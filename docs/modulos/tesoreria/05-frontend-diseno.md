# Diseño de frontend — Módulo Tesorería / Bancos

> **Versión:** 0.1 · **Fecha:** 2026-07-14
> **Basado en:** [`01-diseno.md`](01-diseno.md) v0.1 y
> [`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md)
> (exemplar de patrones de UI del back-office).

---

## 0. Cómo leer

Las diferencias entre módulos viven en el query/schema/columnas, no en la
estructura de ventanas. Este doc solo lista rutas, patrón aplicado por
pantalla y las (pocas) pantallas específicas. Todo lo demás se copia del
patrón más cercano (P1 bandeja general, P2 bandeja filtrada server-side,
P3 detalle, P4 nueva con Sheet).

---

## 1. Posicionamiento

App "Tesorería" en el shell (modal de cards del launcher, patrón
UF3-PR1). Secciones: Pagos, Corridas, Ingresos, Movimientos,
Conciliación, REPP, Reportes. Permisos `tesoreria.*` gobiernan
visibilidad de secciones y acciones (patrón existente de guards por
permiso).

## 2. Rutas (TanStack Router)

| Ruta | Patrón | Contenido |
|---|---|---|
| `/tesoreria/pagos` | P2 | Bandeja de pasivos pendientes (filtros server-side: vencimiento, proveedor, moneda, monto) + acción "Registrar pago" (Sheet) |
| `/tesoreria/pagos-cuenta` | P2 | Pagos a cuenta abiertos con antigüedad + "Nuevo pago a cuenta" (Sheet) + acción "Ligar" |
| `/tesoreria/corridas` | P1 + P3 | Bandeja de corridas → `/corridas/$id` master-detail con líneas inline |
| `/tesoreria/depositos` | P2 | Depósitos por confirmar (propuestas CxC + expectativas de Caja) |
| `/tesoreria/movimientos` | P1 + P3 | Libro de movimientos por cuenta → detalle con aplicaciones y contramovimientos |
| `/tesoreria/conciliacion` | P1 + específica | Conciliaciones por cuenta/periodo → pantalla de matching (§4.1) |
| `/tesoreria/repp` | P2 | Pagos PPD sin REPP recibido + "Registrar REPP" (Sheet) |
| `/tesoreria/reportes/flujo-efectivo` · `/reportes/auxiliar-bancos` | `<ReporteShell>` | ADR-0036 |

## 3. Patrones aplicados

- **Sheet (slide-from-right)** para: Registrar pago, Nuevo pago a cuenta,
  Registrar ingreso manual, Registrar REPP. Provider a nivel shell
  (`useNuevoPago().abrir()`), confirm al cerrar con `isDirty`,
  `Force: true` en success.
- **Inline forms** (nunca modal) para líneas de corrida: border dashed
  primary (agregar) / amber (editar), estilo `LineaInlineForm` de RQ.
- **Sub-topbar sticky** en detalles con `data-print="hidden"`; aside
  master igual, para impresión limpia del oficio/acta.
- **Quick Create** del topbar: "Pago a proveedor", "Pago a cuenta",
  "Registrar REPP".
- **Idempotency-Key:** `crypto.randomUUID()` por submit, sin sufijos.
- **Mirrors FE de enums** con los mismos valores del check constraint
  (sentido, estados de aplicación/conciliación/corrida/depósito, etc.).

## 4. Pantallas específicas

### 4.1 Matching de conciliación

Dos columnas: extracto (izquierda) ↔ movimientos internos (derecha),
réplica del patrón de matching de aplicación de pagos de CxC
(`cuentas-por-cobrar/05-frontend-diseno.md` §4.1). Tres acciones por
línea: confirmar match sugerido (lote con checkbox), match manual
(seleccionar movimiento), alta asistida (prellenar movimiento desde la
línea — comisiones/intereses). Header con avance (n/m líneas resueltas,
diferencia de saldo en vivo). Botón "Cerrar conciliación" deshabilitado
hasta saldo cuadrado (RN-7), con tooltip del faltante.

### 4.2 Registrar pago (Sheet)

Desde la bandeja con multi-selección de pasivos (mismo proveedor y
moneda): resumen de pasivos, cuenta de egreso (filtrada por moneda —
RN-3), fecha valor, referencia bancaria. Datos bancarios del proveedor
visibles enmascarados; botón "ver completo" gated por
`tesoreria.movimientos.ver-cuenta-completa`.

### 4.3 Liga tardía de pago a cuenta

Al abrir un pago a cuenta con sugerencia (pasivo del mismo proveedor
recibido): banner de sugerencia + comparación monto movimiento vs saldo
pasivo. Si difieren, la confirmación exige rol de Jefe (doble
confirmación, §3.4 del levantamiento).

### 4.4 Confirmar depósito

Detalle de propuesta CxC (facturas propuestas, remittance) + selector
del movimiento de ingreso (o alta rápida). Confirmar publica el evento;
el estado fiscal (REPP timbrado) llega después y se refleja con badge —
dejar claro en UI que confirmar ≠ timbrado (§3.3, nota de contrato).

## 5. Componentes reutilizables

- `ProveedorSelector`, `ClienteSelector` — existentes (saneamiento CxP).
- `<ReporteShell>` + exportadores PDF/Excel — ADR-0036.
- `MonedaBadge`, `EstadoBadge` — existentes; agregar variantes de estados
  de Tesorería.
- `CuentaBancariaSelector` — nuevo, con máscara y filtro por moneda.
- `PasivoPicker` multi-selección — nuevo (referencia: pickers de CxP
  #572).

## 6. Estados, errores, permisos

- Problem Details → toasts con detalle expandible (patrón global).
- ETag/If-Match en corrida/conciliación/depósito: conflicto 412 → recargar
  con aviso.
- Bandejas vacías con explicación del flujo (p. ej. "los pasivos aparecen
  cuando CxP los autoriza") — el módulo depende de eventos y la bandeja
  vacía es un estado normal en el arranque.
- Acciones ocultas (no deshabilitadas) sin permiso, patrón del shell.

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
