# Diseño de frontend — Módulo Almacén

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 1), [03-pr-breakdown.md](03-pr-breakdown.md) (Rev. 2), [04-cuidados-infra.md](04-cuidados-infra.md).
>
> **Hereda contexto de:** [`docs/modulos/compras-ordenes-compra/05-frontend-diseno.md`](../compras-ordenes-compra/05-frontend-diseno.md) y [`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md). Este doc **enfatiza lo nuevo de Almacén**.
>
> **Estado:** propuesta de diseño UI v1. `[Asunción FAlmxx]` requieren confirmación.
>
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

- `[Decidido]` — fijado por ADR, decisión backend, o 05 de Compras-OC.
- `[Asunción FAlmxx]` — propuesta del frontend tech lead.
- `[Diferido]` — vNext.

**Reuso primero:** patrones validados en Compras-OC se reutilizan; solo se expande lo específico de Almacén.

---

## 1. Posicionamiento

### 1.1 Qué cubre

UI del módulo Almacén v1. Consume endpoints F0–F9 del backend. Reemplaza el Portal Millet completo.

### 1.2 Qué NO cubre

- Pantallas de otros módulos (Compras, CxP, Tesorería, BI).
- **Master de Artículos** — vive en `DatosMaestros` del módulo Administración. Almacén consume vía selectores read-only.
- **Operación móvil / códigos de barras / scanner** — diferido a vNext.
- **Bins formales** — diferido a vNext.

### 1.3 Posicionamiento en el shell

Sidebar plano (memoria `project_nav_shell_pattern`). Nuevo grupo "Almacén" con cards:

- Recepción (con contador de pendientes)
- Salidas
- Devoluciones
- Inventario físico
- Saldos
- Reportes
- Catálogo (almacenes/sub-almacenes — admin)

---

## 2. Stack frontend

Heredado de Compras-OC y CxP:

- React 19 + TypeScript (strict)
- TanStack Router/Query/Table
- shadcn/ui + Tailwind v4
- `@react-pdf/renderer` (comprobantes de salida, listas de conteo, reportes — ADR-0036)
- `exceljs` (exportación reportes)
- React Hook Form + Zod
- Tipos generados desde OpenAPI

---

## 3. Asunciones a confirmar

| # | Asunción | Default | Si "no" |
|---|---|---|---|
| FAlm1 | **Pantalla principal del Almacenista** es la bandeja de "Recepciones del día" + acceso rápido a "Nueva salida" | Es la primera ruta al entrar a `/almacen`. | Si Carlos prefiere "Salidas del día" como inicio, se reordena. |
| FAlm2 | **Selector global de sub-almacén** en el topbar | Chip persistente que filtra todas las bandejas por sub-almacén seleccionado. Si el usuario solo opera uno, no aparece. | Estándar. |
| FAlm3 | **Vista de captura de salida** tipo "carrito": ir agregando artículos uno por uno con autocomplete | Patrón rápido para 120 salidas/día. Validación de stock en línea. | Si Carlos prefiere lista plana editable, se ajusta. |
| FAlm4 | **Pantalla de conteo "captura sin sesgo"** sin mostrar cantidad teórica | Inputs grandes con foco automático en siguiente línea. **NUNCA** muestra teórico al contador (A6 backend). | Decisión cerrada. |
| FAlm5 | **Comprobante PDF al surtir salida** se genera automáticamente | Se descarga al confirmar la salida; impresión inmediata para firma del solicitante. | Estándar. |
| FAlm6 | **Vista de stock por sub-almacén** con filtros rápidos | Bandeja `/almacen/saldos` con cantidad, costo promedio, valor, ubicación referencia. Filtros por sub-almacén, familia, "con stock", "bajo mínimo". | Estándar. |
| FAlm7 | **Sheet para "Nueva recepción"** con selector de OC | Auxiliar escanea / busca OC, sistema pre-carga líneas con cantidad solicitada. | Estándar. |
| FAlm8 | **Bandeja "Vales pendientes regularizar"** dedicada | Visible para Almacenistas + Coordinadores. Indicador de urgencia por días vencidos. | Estándar. |
| FAlm9 | **Vista del cierre de mes** con checklist visual | Pantalla con steps: (1) movimientos del mes registrados, (2) conteos pendientes, (3) confirmar. | Estándar. |

---

## 4. Rutas

```
/almacen                                   — landing
/almacen/almacenes                         — admin de catálogo (CRUD)
/almacen/sub-almacenes                     — admin

/almacen/recepciones                       — bandeja de recepciones
/almacen/recepciones/$id                   — detalle
/almacen/recepciones/nueva                 — sheet (selector variante A/B)

/almacen/salidas                           — bandeja
/almacen/salidas/$id
/almacen/salidas/nueva                     — sheet (RQ o Vale)

/almacen/devoluciones                      — bandeja unificada
/almacen/devoluciones/internas/$id
/almacen/devoluciones/proveedor/$id        — sub-flujo 8.B

/almacen/inventarios                       — bandeja de conteos
/almacen/inventarios/$id                   — detalle
/almacen/inventarios/$id/captura           — pantalla de captura sin sesgo
/almacen/inventarios/$id/aprobacion        — pantalla de aprobación por línea

/almacen/saldos                            — bandeja de stock
/almacen/saldos/articulo/$id               — histórico de un artículo

/almacen/cierre-mes                        — pantalla del cierre mensual

/almacen/reportes                          — landing
/almacen/reportes/alfak-historial-almacen
/almacen/reportes/mp-cnk
/almacen/reportes/salidas-del-dia          — operativo interno
/almacen/reportes/entradas-del-dia         — operativo interno
```

---

## 5. Patrones de UI aplicables

Heredados (ver `05 de Compras-OC` §X):

- **Master-detail con lista compacta 320px sticky** + panel detalle. Mobile drill-down.
- **Sheets para "Nueva..."** con provider a nivel shell. Confirm al cerrar con `isDirty`.
- **Inline forms para líneas** (border dashed primary / amber).
- **Topbar global** con búsqueda contextual, Quick Create popover, indicador de notificaciones.
- **Sub-topbar del detalle** con acciones contextuales por estado. `data-print="hidden"`.

---

## 6. Pantallas específicas de Almacén

### 6.1 Captura de recepción (sheet)

**Layout:**

```
┌──────────────────────────────────────────────────────────────────┐
│ Header: "Nueva recepción"                                        │
│ Variante: [● A — Con factura  ○ B — Con packing list]            │
├──────────────────────────────────────────────────────────────────┤
│ Selector de OC: [autocomplete con sugerencias por proveedor]     │
│ Líneas de la OC (pre-cargadas con cantidad solicitada):          │
│                                                                  │
│ ┌─ Línea 1: Material XYZ ─────────────────────┐                  │
│ │ Cantidad solicitada: 100                     │                  │
│ │ Cantidad recibida:   [___] unidades          │                  │
│ │ Sub-almacén destino: [select default art]    │                  │
│ │ Lote / fecha:        [___]                   │                  │
│ │ Costo unit (OC):     $120.00                 │                  │
│ │ Comentario:          [___]                   │                  │
│ └───────────────────────────────────────────────┘                  │
│ ┌─ Línea 2: ... ─────────────────────────────┐                   │
│ │ ...                                         │                   │
│ └─────────────────────────────────────────────┘                   │
│                                                                  │
│ Variante A:                                                      │
│   Datos del CFDI: [selector del CfdiRecibido] o ingresa UUID    │
│ Variante B:                                                      │
│   Packing list: [folio + adjunto PDF]                            │
│                                                                  │
│ Adjuntos: [drag-and-drop]                                        │
│                                                                  │
│              [Cancelar] [Guardar borrador] [Validar y registrar] │
└──────────────────────────────────────────────────────────────────┘
```

**Lógica:**

- Al cambiar `cantidad_recibida`, mostrar diff vs solicitada en verde/amarillo/rojo según tolerancia (A5).
- Fuera de tolerancia → modal "Excede tolerancia. Requiere autorización del Supervisor". Comando explícito + adjunto justificación.
- Al "Validar y registrar" → confirma con summary, emite evento al outbox, navega al detalle.

### 6.2 Captura de salida (sheet — patrón "carrito")

**Layout:**

```
┌──────────────────────────────────────────────────────────────────┐
│ Header: "Nueva salida"                                           │
│ Variante: [● A — Con RQ  ○ B — Por vale]                         │
├──────────────────────────────────────────────────────────────────┤
│ Variante A:                                                      │
│   RQ: [selector con autocomplete]                                │
│   Coordinador autorizó: ✓ (validado vs Compras)                  │
│ Variante B:                                                      │
│   Vale firmado por: [select coordinador]                         │
│   Adjunto del vale: [file upload]                                │
│   Plazo regularización: 48h                                      │
│                                                                  │
│ Persona destinataria: [autocomplete empleados]                   │
│ Máquina / equipo: [select opcional]                              │
│                                                                  │
│ Artículos (agregar uno por uno con autocomplete):                │
│ ┌──────────────────────────────────────────────┐                  │
│ │ + Agregar artículo                            │                  │
│ └──────────────────────────────────────────────┘                  │
│                                                                  │
│ Líneas agregadas:                                                │
│ ┌─ Tornillos M6 — Stock: 250 ──────────────────┐                  │
│ │ Cantidad: [___] ✓ stock disponible           │                  │
│ │ Ubicación ref: [___] (opcional)              │                  │
│ │ Sub-almacén:   ACI (default)                 │                  │
│ └───────────────────────────────────────────────┘                  │
│                                                                  │
│ Comentario libre: [textarea]                                     │
│                                                                  │
│              [Cancelar] [Guardar borrador] [Surtir y registrar]  │
└──────────────────────────────────────────────────────────────────┘
```

**Lógica:**

- Validación de stock en línea (`SELECT FOR UPDATE`). Si falta → resalta en rojo + opción de "Crear RQ de compra por desabasto" (link al módulo de Requisiciones).
- Variante B muestra contador "Pendiente regularizar en 48h" visible.
- Al "Surtir y registrar" → genera comprobante PDF inmediatamente (descarga + impresión).

### 6.3 Pantalla de captura de conteo (caso especial — A6)

**Layout (no es sheet, es pantalla dedicada full-width):**

```
┌───────────────────────────────────────────────────────────────────┐
│ Conteo {tipo} — {sub-almacén} — Responsable: {nombre}            │
│ Progreso: 23 / 1,250 artículos                                   │
├───────────────────────────────────────────────────────────────────┤
│ Filtros: [familia ▼] [ubicación ref ▼] [pendientes ▼]            │
├───────────────────────────────────────────────────────────────────┤
│ TORNILLOS M6                                                     │
│   Clave: TNT-001                                                 │
│   Ubicación referencia: A-12-3                                   │
│   Cantidad real: [__________]  [✓ Capturar]                      │
│   ─────────────────────────────────────────                      │
│   ⚠ NO se muestra cantidad teórica                                │
│   (la verás como aprobador al cerrar)                            │
├───────────────────────────────────────────────────────────────────┤
│ SILICÓN 100ML                                                    │
│   Clave: SIL-002                                                 │
│   ...                                                            │
└───────────────────────────────────────────────────────────────────┘
```

**Lógica:**

- Input numérico grande, foco automático.
- Al confirmar línea, salta automáticamente a la siguiente.
- Botón "Marcar para recuento" si el contador detecta una variación visible.
- Endpoint que sirve esta vista **NUNCA** envía `cantidad_teorica` (lógica de backend; ver §4.2 del 04-cuidados de Almacén).

### 6.4 Pantalla de aprobación de conteo (aprobador)

**Layout:**

```
┌───────────────────────────────────────────────────────────────────┐
│ Aprobación de conteo {tipo} — {sub-almacén}                       │
├───────────────────────────────────────────────────────────────────┤
│ Filtros: [Variación > umbral] [Sin justificar] [Por aprobar]     │
│                                                                   │
│ Variación pequeña (< 5% o < $1K): puedes aprobar bulk             │
│ [Aprobar todas las pequeñas]                                      │
├───────────────────────────────────────────────────────────────────┤
│ Artículo  | Teórica | Real | Variación | Valor | Justif | Acción  │
│ TNT-001  | 250     | 248  | -2 (-0.8%)| -$24   | —      | [✓]     │
│ SIL-002  | 50      | 30   | -20 (-40%)| -$2,400| —      | ⚠ Recuento│
│ ...                                                              │
└───────────────────────────────────────────────────────────────────┘
[Enviar a aplicar] (habilitado si todas las líneas tienen acción)
```

**Lógica:**

- Líneas con `requiere_recuento = true` se destacan amarillo si no se ha capturado recuento.
- Bulk approve para variaciones bajo umbral (ahorra clicks con 1,000+ líneas).
- Variación grande (> $10K) → modal "Requiere aprobación del Jefe + notificar Finanzas" antes de aplicar.

### 6.5 Vista de saldos con histórico

`/almacen/saldos/articulo/$id` — vista master-detail:

- Izquierda: stock por sub-almacén (lista compacta).
- Derecha: histórico de movimientos del artículo (últimos 100, paginable). Gráfica simple de stock en el tiempo.

### 6.6 Pantalla de cierre de mes

```
┌───────────────────────────────────────────────────────────────────┐
│ Cierre de mes — Mayo 2026                                         │
├───────────────────────────────────────────────────────────────────┤
│ ✓ 1. Todos los movimientos del mes registrados (5,234)            │
│ ✗ 2. Conteos en proceso (1 pendiente — ALFAK-INSUMOS)             │
│      [Ver conteo →]                                               │
│ ✓ 3. Bloqueos de inventario activos: ninguno                      │
│ ☐ 4. Confirmar cierre                                             │
│                                                                   │
│ Notas: el cierre bloquea todos los movimientos con fecha mayo.   │
│                                                                   │
│ [Cancelar]  [Ejecutar cierre] (deshabilitado)                     │
└───────────────────────────────────────────────────────────────────┘
```

### 6.7 Ubicación de artículos — atajo desde Artículo (con retorno)

La pantalla "Ubicación de artículos" (`/almacen/asignaciones`) recibe atajos
desde otras superficies. El primero es el **detalle de Artículo** (Datos
Maestros): un botón "Ver ubicaciones (N)" en el header — gateado por
`almacen.asignaciones.leer`, con N = asignaciones activas — navega a esta
pantalla con `?articuloId=<id>&desde=articulo&articuloEtiqueta=<clave · nombre>`.
La tabla queda pre-filtrada por ese artículo (el filtro `articuloId` ya vive en
la URL vía `validateSearch`).

Cuando la ruta se abre con `desde=articulo`, aparece arriba un breadcrumb
"← Volver a {articuloEtiqueta}" que regresa al detalle del artículo con **URL
limpia** (sin query params). El estado del retorno vive **solo en la URL**: sin
`localStorage`, sin estado global; se auto-destruye al navegar, cambiar de
sección o cerrar la pestaña.

**Patrón reutilizable para futuros orígenes.** Dos convenciones que cualquier
atajo hacia esta pantalla debe seguir:

- **Sentinel `desde=<origen>`** — distingue "llegué por el atajo" de "filtré
  manualmente". Sin él, filtrar por `articuloId` en la barra **no** pinta
  breadcrumb (evita un retorno fantasma). Hoy solo existe `desde=articulo`.
- **`<origen>Etiqueta=<valor>`** — el origen, que ya tiene el label a la mano,
  lo pasa por la URL para pintar el breadcrumb **sin fetch** en el destino
  (opcional: si falta, el breadcrumb cae a un genérico "← Volver al artículo").

No requirió backend ni ADR: es navegación de TanStack Router. Los dos params se
agregaron opcionales al `AsignacionesSearchSchema` (backward-compatible con
deep-links viejos).

---

## 7. Componentes reutilizables

### 7.1 De Compras-OC y CxP

- `<MasterDetailLayout>`, `<SheetProvider>`, `<InlineForm>`, `<EvidenceUploader>`, `<MoneyInput>`, `<DateRangeInput>`, `<UserChip>`, `<PermissionGuard>`, `<ReporteShell>`.

### 7.2 Nuevos en Almacén

- `<ArticuloAutocomplete>` — autocomplete de artículos con stock visible.
- `<SaldoChip>` — indicador visual de stock disponible (verde/amarillo/rojo).
- `<ToleranciaIndicator>` — heredado de CxP pero adaptado a cantidad (no monto).
- `<ConteoLineaInput>` — input grande con autoavance, sin mostrar teórico.
- `<VariacionBadge>` — chip con variación absoluta + porcentaje.
- `<TipoMovimientoChip>` — chip con icono y color por tipo de movimiento.
- `<EstadoMovimientoChip>` — chip por estado.
- `<JerarquiaAlmacenBreadcrumb>` — breadcrumb Sucursal → Almacén → Sub-almacén.
- `<ComprobanteSalidaPdf>` — componente `@react-pdf/renderer` para el comprobante firmable.
- `<ListaConteoPdf>` — componente PDF para imprimir la lista de conteo.

---

## 8. Estados, errores, loading

Heredado de CxP §8. Específicos de Almacén:

- **Stock insuficiente al surtir** → highlight en rojo + sugerencia "Crear RQ de compra".
- **Bloqueo de inventario anual** → modal "Sub-almacén X bloqueado por conteo anual hasta {fecha}".
- **Periodo cerrado** → 422 con detalle "fecha de movimiento en periodo cerrado".

---

## 9. Permisos en UI

| Rol del usuario | Rutas visibles |
|---|---|
| **Almacenista** | `/almacen/recepciones`, `/almacen/salidas`, `/almacen/devoluciones/internas`, `/almacen/saldos` |
| **SupervisorInsumos** | + autorización de recepciones fuera de tolerancia + aprobación de ajustes pequeños |
| **AlmacenistaMaterialesDirectos** | mismo que Almacenista pero filtrado a sub-almacenes MP |
| **JefeAlmacen** | Todo + `/almacen/cierre-mes` + `/almacen/inventarios` aprobación + admin de catálogo |
| **AuditorExterno** | Solo lectura (`/almacen/saldos`, `/almacen/reportes/*`) |

---

## 10. Reportes (vista frontend)

Estructura común con `<ReporteShell>`:

### 10.1 ALFAK-HISTORIAL-ALMACEN

Reporte de cierre de mes. Por sub-almacén + por artículo. Columnas: saldo inicial, entradas, salidas, ajustes, saldo final, costo promedio, valor.

### 10.2 SAP-REPORTE-EXISTENCIA-MP-CNK

Reporte de inventario diario de MP. Filtrado por sub-almacén MP. Columnas: artículo, cantidad, costo, valor, días sin movimiento.

### 10.3 Salidas / entradas del día (operativos)

Para Almacenista. Consolidado del turno con totales por persona destinataria.

---

## 11. Brechas con el backend

Para confirmar en code review:

- **Endpoint de validación de stock pre-salida** — debe ser sincrónico y rápido (<100ms) para no degradar UX.
- **Endpoint de exportación a Excel/PDF** para reportes — shape JSON estándar (ADR-0036).
- **WebSocket / SignalR** para notificaciones (SLA de vales, conteos completos).
- **Endpoint de bulk approve** para aprobación de conteo (`POST /conteos/{id}/aprobar-bulk` con filtro de variación).

---

## Rev.

- **2026-05-22 — v1 (Draft)** — Diseño de frontend inicial del módulo Almacén. Hereda patrones. Enfatiza pantallas específicas (captura sin sesgo, conteo, salida tipo carrito, cierre de mes con checklist).
- **2026-07-21 — §6.7** — Atajo desde el detalle de Artículo hacia "Ubicación de artículos" con breadcrumb de retorno. Documenta el patrón reutilizable `desde=<origen>` + `<origen>Etiqueta` (estado de retorno solo en URL) para futuros orígenes. Frontend puro, sin ADR.
