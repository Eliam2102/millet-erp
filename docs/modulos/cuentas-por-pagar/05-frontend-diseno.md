# Diseño de frontend — Módulo Cuentas por Pagar

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 1.4), [01a-anexo-tc-empresarial.md](01a-anexo-tc-empresarial.md), [03-pr-breakdown.md](03-pr-breakdown.md) (Rev. 2), [04-cuidados-infra.md](04-cuidados-infra.md).
>
> **Hereda contexto de:** [`docs/modulos/compras-ordenes-compra/05-frontend-diseno.md`](../compras-ordenes-compra/05-frontend-diseno.md) — patrón exemplar de UI del back-office. Y [`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md). Este doc **enfatiza lo nuevo de CxP** y reutiliza el shell sin duplicar diseño.
>
> **Construido contra:** la API real implementada en backend F0–F10. Las brechas se documentan en §13.
>
> **Estado:** propuesta de diseño UI v1. Decisiones `[Asunción FCxPxx]` requieren confirmación.
>
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

- `[Decidido]` — fijado por ADR, decisión del backend ya implementada, o por 05 de Compras-OC que cerró el patrón.
- `[Asunción FCxPxx]` — propuesta del frontend tech lead para CxP. Listadas en §3.
- `[Diferido]` — fuera de alcance v1.

**Reuso primero:** donde un patrón ya está validado en el 05 de Compras-OC, este doc apunta a `§X del 05 de Compras-OC` en lugar de duplicar. Solo se expande lo específico de CxP.

---

## 1. Posicionamiento

### 1.1 Qué cubre

UI del módulo CxP v1: pantallas que consumen los endpoints HTTP de F0–F10 del backend.

### 1.2 Qué NO cubre

- Pantallas de Compras, Requisiciones, Almacén, Tesorería, Activos Fijos, Contabilidad, BI (cada una en su propio módulo).
- **Master de proveedores** — vive en `DatosMaestros` del módulo Administración. CxP solo extiende con vistas para los atributos operativos (tolerancia, `en_revision`, adjuntos).
- **CRUD de catálogos** de Aprobadores/Viáticos/Tarjetas — viven en módulo Admin del área Administración. CxP solo los consume read-only.
- **Portal propio de proveedores** — diferido a vNext (CxP §13.2).

### 1.3 Posicionamiento en el shell

Shell autenticado actual (ya en producción para Compras). Este diseño **agrega un nuevo grupo** "Cuentas por Pagar" en el sidebar con sub-árbol `/cxp/...`.

Memoria `project_nav_shell_pattern` aplica: sidebar plano + modal de cards por sección. Sección "Cuentas por Pagar" con cards:

- CFDIs por capturar (con contador)
- Facturas
- Notas de crédito
- Notas de cargo
- Anticipos
- Comprobaciones de gastos
- Tarjetas de crédito
- Reportes

---

## 2. Stack frontend

Heredado completamente del 05 de Compras-OC:

- React 19 + TypeScript (strict)
- TanStack Router (file-based routing)
- TanStack Query (data fetching)
- TanStack Table (tablas virtualizadas)
- shadcn/ui + Tailwind v4
- `@react-pdf/renderer` (PDFs nativos según ADR-0036)
- `exceljs` (exportación Excel según ADR-0036)
- React Hook Form + Zod (validación)
- TanStack Form (alternativo donde aplique)

**Tipos TypeScript generados automáticamente** desde OpenAPI (ADR-0017). CxP aparece en el pipeline `npm run gen-types`.

---

## 3. Asunciones a confirmar

| # | Asunción | Default | Si "no" |
|---|---|---|---|
| FCxP1 | **Pantalla principal del Auxiliar** es la bandeja de "CFDIs por capturar" | Es la primera ruta al entrar a `/cxp`. Quick action visible: "Capturar siguiente CFDI". | Si el área prefiere "Facturas en revisión" como inicio, se reordena. |
| FCxP2 | **Vista del titular de TC** es separada del Auxiliar | Ruta `/cxp/tc/mis-movimientos` con filtro automático por usuario. | Si los titulares quieren acceso completo a CxP, se elimina la vista filtrada. |
| FCxP3 | **Preview del XML** al capturar factura | Panel izquierdo muestra preview legible del XML (parseado, no raw); panel derecho los campos editables. | Si UX prefiere ocultar XML, se omite. |
| FCxP4 | **Sheet (slide-from-right) para "Nuevo"** en todos los recursos | Patrón establecido en Compras-OC. | Estándar. |
| FCxP5 | **Inline forms para líneas** (sin modal) | Patrón establecido (memoria `feedback_inline_no_modal_para_items`). | Estándar. |
| FCxP6 | **Pantalla de conciliación TC** es full-screen | Vista dual (líneas del banco a la izquierda, movimientos a la derecha) ocupando todo el viewport. | Si UX prefiere split horizontal, se ajusta. |
| FCxP7 | **Captura de comprobación de viáticos por el empleado** es autoservicio puro | Empleado ve solo sus propias comprobaciones; jefe ve las de sus subordinados. | Confirmar con RH. |
| FCxP8 | **Pantalla de autorización informal** central en el módulo | Bandeja dedicada "Autorizaciones pendientes con firma" para que el equipo de Compras le dé seguimiento. | Estándar. |
| FCxP9 | **Filtros server-side default-on para bandejas de gran volumen** | CFDIs y Facturas tienen filtros aplicados por default (estado, periodo último mes). | Estándar. |
| FCxP10 | **PDF preview en línea** del CFDI vinculado | Al ver detalle de factura, panel lateral muestra el PDF del CFDI inline. | Estándar (mejora UX). |

---

## 4. Rutas (TanStack Router)

```
/cxp                                       — landing del módulo
/cxp/cfdis                                 — bandeja CFDIs por procesar (P2 — filtrada server-side)
/cxp/cfdis/$id                             — detalle CFDI (preview XML + PDF + acciones)

/cxp/facturas                              — bandeja facturas (P2)
/cxp/facturas/$id                          — detalle factura (master-detail)
/cxp/facturas/$id/aplicar-nc               — sheet para aplicar NC
/cxp/facturas/$id/aplicar-anticipo         — sheet para aplicar anticipo

/cxp/notas-credito                         — bandeja
/cxp/notas-credito/$id

/cxp/anticipos                             — bandeja
/cxp/anticipos/$id

/cxp/notas-cargo                           — bandeja
/cxp/notas-cargo/$id
/cxp/notas-cargo/$id/autorizar             — sheet de autorización con evidencia

/cxp/comprobaciones                        — bandeja (filtrable por tipo: CajaChica/Viaticos/Aduanales)
/cxp/comprobaciones/$id

/cxp/viaticos                              — sub-área dedicada (autoservicio empleado + revisión CxP)
/cxp/viaticos/mis-solicitudes              — empleado
/cxp/viaticos/por-aprobar                  — jefe
/cxp/viaticos/por-revisar                  — CxP (revisar comprobación al regresar)
/cxp/viaticos/$id

/cxp/tc                                    — landing TC
/cxp/tc/mis-movimientos                    — bandeja titular
/cxp/tc/tarjetas                           — admin de tarjetas
/cxp/tc/tarjetas/$id
/cxp/tc/movimientos                        — bandeja Auxiliar (todas las tarjetas)
/cxp/tc/movimientos/$id
/cxp/tc/estados-cuenta                     — bandeja de estados de cuenta
/cxp/tc/estados-cuenta/$id
/cxp/tc/estados-cuenta/$id/conciliacion    — full-screen de conciliación

/cxp/proveedores                           — vista CxP del master (solo lectura + acciones operativas)
/cxp/proveedores/$id                       — detalle con bitácora de revisión

/cxp/revision                              — bandeja transversal "Facturas en revisión asignadas a mi área"
/cxp/autorizaciones-firma-pendiente        — bandeja "Autorizaciones con firma pendiente"

/cxp/reportes                              — landing
/cxp/reportes/antiguedad-saldos
/cxp/reportes/antiguedad-anticipos
/cxp/reportes/cartera
/cxp/reportes/cfdis-sin-capturar
/cxp/reportes/movimientos-tc
/cxp/reportes/estados-cuenta-tc
```

---

## 5. Patrones de UI aplicables

Heredados de `patrones-compras.md` y `05 de Compras-OC`.

### 5.1 Master-detail con lista compacta sticky

Lista 320px sticky a la izquierda + panel detalle a la derecha. Mobile drill-down. Aplica a: `/cxp/facturas`, `/cxp/notas-credito`, `/cxp/anticipos`, `/cxp/notas-cargo`, `/cxp/comprobaciones`, `/cxp/tc/movimientos`, `/cxp/tc/estados-cuenta`.

### 5.2 Sheets para "Nueva..."

Provider a nivel shell. Confirm al cerrar con `isDirty`. `Force: true` en success. Aplica a:

- "Capturar factura desde CFDI" — sheet con preview XML a la izquierda y campos editables a la derecha (FCxP3).
- "Nueva nota de cargo".
- "Nuevo movimiento de TC sin CFDI".
- "Nueva comprobación de Caja Chica / Aduanales".
- "Nueva solicitud de viáticos" (empleado).

### 5.3 Inline forms para items

Memoria `feedback_inline_no_modal_para_items` — **inline, no modal** (border dashed primary para agregar; amber para editar):

- Líneas de comprobación de gastos (`<LineaComprobacionInlineForm>`).
- Aplicaciones de NC/anticipo a factura.
- Movimientos de TC dentro del estado de cuenta.
- Adjuntos en notas de cargo (drag-and-drop inline).
- Evidencias de autorización (upload inline con preview).

### 5.4 Topbar

- Búsqueda contextual por ruta con debounce 200ms (UUID, folio del proveedor, RFC).
- Quick Create popover con: "Capturar factura", "Nueva nota de cargo", "Nuevo movimiento TC", "Nueva comprobación".
- Indicador de notificaciones (SLA de revisión, autorizaciones pendientes).

### 5.5 Sub-topbar del detalle

Sticky con acciones contextuales por estado: "Enviar a revisión", "Aplicar NC", "Aplicar anticipo", "Cancelar", "Adjuntar evidencia". `data-print="hidden"` para impresión limpia.

### 5.6 Bandejas asignadas

Cada responsable de área tiene una bandeja "Mis facturas en revisión" (filtro server-side por `dependencia_revisora`). Patrón P2 de `patrones-compras.md`.

---

## 6. Pantallas específicas de CxP

### 6.1 Captura de factura desde CFDI

Pantalla flagship del Auxiliar.

**Layout** (sheet wide):

```
┌────────────────────────────────────────────────────────────────────┐
│ Header: "Capturar factura — CFDI {uuid}"                          │
├────────────────────────┬───────────────────────────────────────────┤
│ XML parseado (lectura) │ Campos editables                          │
│ • RFC emisor           │ Proveedor: [autosuggest del XML]          │
│ • Razón social         │ OC asociada: [selector con sugerencia]    │
│ • Folio del proveedor  │ Sucursal: [select]                        │
│ • Fecha CFDI           │ Categoría: [auto-heredada del proveedor]  │
│ • Subtotal             │ Encargado de compras: [auto-heredado OC]  │
│ • IVA trasladado       │ Conceptos contables por línea:            │
│ • Total                │   [LineaInlineForm × N]                   │
│ • Conceptos (lista)    │                                           │
│                        │ Notas / observaciones: [textarea]         │
│ [PDF preview ↓]        │                                           │
│                        │ [☐ Enviar a revisión con motivo: ...]    │
│                        │                                           │
│                        │           [Cancelar] [Guardar borrador]   │
│                        │                       [Capturar]          │
└────────────────────────┴───────────────────────────────────────────┘
```

**Lógica:**

- Al cambiar OC, el sistema valida tolerancia y muestra alerta si excede.
- Conceptos contables se pre-rellenan según subcategoría del proveedor + default por línea.
- Si la captura excede tolerancia → modal "La factura excede la tolerancia del proveedor (X). Se cancelará y devolverá a Compras". Comando explícito.

### 6.2 Pantalla de conciliación TC (full-screen)

Pantalla más compleja del módulo.

**Layout:**

```
┌──────────────────────────────────────────────────────────────────────┐
│ Header: "Conciliación — Estado de cuenta TC {tarjeta} {periodo}"    │
│ Total banco: $X | Conciliado: $Y | Diferencia: $Z (---%)            │
├──────────────────────────────────┬───────────────────────────────────┤
│ Líneas del banco                 │ Movimientos capturados            │
│ (parseadas del Excel/CSV)        │ (en `Registrado`, no conciliados) │
│                                  │                                   │
│ ✓ STARBUCKS — $150 — 12/05       │ • STARBUCKS — $150 — 12/05 ▷    │
│ ✓ AMAZON — $1,200 — 14/05        │ • AMAZON.COM.MX — $1,200 ▷       │
│ ⚠ HOTEL XYZ — $5,000 — 18/05    │ • UBER — $80 — 20/05              │
│   (sugerencia: HOTEL XYZ TJU…)   │                                   │
│ ✗ INTERESES — $300 — 31/05      │                                   │
│   [→ Capturar como GastoFinanc] │                                   │
│ ✗ Refund AMAZON -$200            │                                   │
│   (sugerencia: AMAZON ▷)        │                                   │
│                                  │                                   │
└──────────────────────────────────┴───────────────────────────────────┘
[Cerrar estado de cuenta] (habilitado solo si diferencia = 0)
```

**Lógica:**

- Click en línea del banco + click en movimiento sugerido → confirma match.
- Click en "Capturar como X" → abre sheet pre-llenado.
- Drag-and-drop opcional (nice-to-have).
- Barra de progreso visible.

### 6.3 Pantalla de solicitud de viáticos (autoservicio empleado)

```
┌─────────────────────────────────────────────────────┐
│ Solicitar viáticos                                  │
├─────────────────────────────────────────────────────┤
│ Destino: [autocomplete + clasificación N/I]         │
│ Tipo de destino: [Nacional / Internacional]         │
│ Fecha desde / hasta: [date range]                   │
│ Días estimados: [auto-calculado: 5]                 │
│ Monto solicitado: $[input]                          │
│                                                     │
│ Política aplicable (tu puesto: Vendedor):           │
│ • Tope diario nacional: $2,000                      │
│ • Tope total estimado: $10,000 (5 días × $2,000)    │
│                                                     │
│ ✓ Tu solicitud está dentro de política              │
│   o ⚠ Excede política — requiere doble firma        │
│                                                     │
│ Motivo del viaje: [textarea]                        │
│                                                     │
│ [Cancelar]                              [Solicitar] │
└─────────────────────────────────────────────────────┘
```

### 6.4 Bandejas asignadas

#### "Facturas en revisión asignadas a mi área"

Patrón P2 (server-side filtering por `dependencia_revisora_id = currentUser.area`). Columnas: folio del proveedor, monto, días en revisión (con highlight si SLA excedido), motivo, acciones.

#### "Autorizaciones con firma pendiente"

Patrón P2 filtrado por `firma_fisica_pendiente = true`. Foco operativo del equipo de Compras (no del Auxiliar de CxP).

#### "CFDIs sin capturar > 5 días"

Bandeja de alerta. Patrón P2 filtrado por fecha de recepción > 5 días + estado `PorProcesar`.

### 6.5 Detalle de factura — sub-topbar

Acciones contextuales según `estado`:

| Estado | Acciones |
|---|---|
| `Capturada` | Enviar a revisión, Cancelar |
| `EnRevision` | Liberar revisión (si soy del área), Cancelar |
| `Autorizada` | Aplicar NC, Aplicar anticipo, Cancelar (con motivo) |
| `Pagada` | (solo lectura; ver evidencia de pago de Tesorería) |
| `Cancelada` | (solo lectura; ver motivo) |

---

## 7. Componentes reutilizables

### 7.1 De Compras-OC

- `<MasterDetailLayout>` con lista sticky.
- `<SheetProvider>` y hook `useSheet()`.
- `<InlineForm>` con border dashed/amber.
- `<EvidenceUploader>` (multi-archivo + preview).
- `<MoneyInput>` (con currency selector).
- `<DateRangeInput>`.
- `<UserChip>`.
- `<PermissionGuard>`.

### 7.2 Nuevos en CxP

- `<CfdiXmlViewer>` — renderiza un CFDI parseado en formato legible.
- `<CfdiPdfPreview>` — preview inline del PDF vinculado (vía blob URL).
- `<ToleranciaIndicator>` — chip con indicador visual de tolerancia aplicable.
- `<AutorizacionInformalForm>` — upload de evidencia + comentario obligatorio + flag firma pendiente.
- `<ConciliacionTcDual>` — vista dual del estado de cuenta TC vs movimientos.
- `<PoliticaViaticosCheck>` — visualizador de política aplicable + indicador de cumplimiento.
- `<EstadoPasivoChip>` — chip con color por estado (5 estados).
- `<SubcategoriaSnapshot>` — muestra subcategoría heredada del proveedor con tooltip de origen.

---

## 8. Estados, errores, loading

### 8.1 Loading

- Skeleton en bandejas (placeholders gris animados).
- Spinner en sheets durante mutate.
- Disabled state en botones con `isPending` de TanStack Query.

### 8.2 Errores

- ProblemDetails del backend → `<Alert variant="destructive">` con título + detalle.
- 412 (concurrencia) → modal "Otro usuario modificó este registro. ¿Recargar?".
- 422 (validación) → highlights por campo + mensaje al pie.
- 401/403 → redirect a login / "no autorizado".
- 500 → toast "Ocurrió un error. Por favor intenta de nuevo." + log al backend.

### 8.3 Vacío

- Bandeja vacía: ilustración + call-to-action ("Captura tu primer CFDI").

---

## 9. Permisos en UI

Cada ruta protegida con `<PermissionGuard permission="cuentas_por_pagar.X">`. Sidebar oculta secciones sin permiso.

Mapeo simplificado:

| Rol del usuario | Rutas visibles |
|---|---|
| **EmpleadoConTcAutorizada** | `/cxp/tc/mis-movimientos`, `/cxp/viaticos/mis-solicitudes` |
| **TitularTc** | + `/cxp/tc/tarjetas/$id`, `/cxp/tc/estados-cuenta` (para sus tarjetas) |
| **AuxiliarCxp** | `/cxp/cfdis`, `/cxp/facturas`, `/cxp/notas-credito`, `/cxp/anticipos`, `/cxp/comprobaciones`, `/cxp/tc/movimientos`, `/cxp/proveedores`, `/cxp/reportes/*` |
| **JefeCxp** | + `/cxp/revision`, `/cxp/notas-cargo`, `/cxp/autorizaciones-firma-pendiente`, `/cxp/tc/tarjetas` |
| **ResponsableArea** | `/cxp/revision` filtrada por su área |
| **DireccionFinanzas** | Todo + acciones de autorización |
| **EmpleadoSolicitanteViaticos** | `/cxp/viaticos/mis-solicitudes` |
| **JefeDirecto** | + `/cxp/viaticos/por-aprobar` filtrada por subordinados |

---

## 10. Reportes (vista frontend)

Cada reporte tiene componente React + componente PDF (`@react-pdf/renderer`) + utilidad Excel (`exceljs`) según ADR-0036.

Estructura común con `<ReporteShell>`:

```
┌───────────────────────────────────────────────────────────┐
│ {Título del reporte}                                      │
│ Generado: {fecha} | Filtros aplicados: ...                │
├───────────────────────────────────────────────────────────┤
│ Filtros (sidebar o top)                                   │
├───────────────────────────────────────────────────────────┤
│ Tabla virtualizada (TanStack Table)                       │
│ ...                                                       │
│ ...                                                       │
├───────────────────────────────────────────────────────────┤
│ Totales / agregados                                       │
└───────────────────────────────────────────────────────────┘
[Exportar PDF] [Exportar Excel] [Imprimir]
```

---

## 11. Accesibilidad

Estándar del proyecto (heredado):

- Navegación por teclado en todas las pantallas críticas.
- Focus visible.
- ARIA labels en componentes complejos (sheets, tablas virtualizadas, drag-and-drop).
- Contraste mínimo WCAG AA.
- Mensajes de error asociados con `aria-describedby`.

---

## 12. Internacionalización

Heredado: estándar es español (es-MX). Sin soporte de idiomas adicionales en MVP. Strings centralizados en archivos `*.i18n.json` por si se requiere futuro.

---

## 13. Brechas con el backend

Para confirmar en code review:

- **Endpoint de preview de CFDI parseado** (`GET /cfdis/{id}/parsed`) que devuelve la representación legible del XML. Si no existe, se parsea client-side.
- **Endpoint de exportación a Excel/PDF** para reportes — confirmar shape JSON estándar (ADR-0036).
- **WebSocket / SignalR** para notificaciones en tiempo real (SLA, autorizaciones nuevas). Backend confirma si está disponible o si polling cada 30s es suficiente.
- **Endpoint de conciliación TC** (`POST /tc/estados-cuenta/{id}/conciliar-automatico`) debe devolver score por línea para que la UI muestre sugerencias correctamente.

---

## Rev.

- **2026-05-22 — v1 (Draft)** — Diseño de frontend inicial del módulo CxP. Hereda patrones de Compras-OC. Enfatiza pantallas específicas (captura de CFDI con preview, conciliación TC dual, solicitud de viáticos con política, bandejas asignadas).
