# Diseño de frontend — Submódulo Órdenes de Compra (Compras)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 0.4),
> [02-plan-implementacion.md](02-plan-implementacion.md) (Rev. 3),
> [03-pr-breakdown.md](03-pr-breakdown.md) (Rev. 3),
> [04-cuidados-infra.md](04-cuidados-infra.md) (Rev. 2).
>
> **Hereda contexto de:** [`docs/modulos/compras-requisiciones/05-frontend-diseno.md`](../compras-requisiciones/05-frontend-diseno.md)
> (Rev. 5) — el patrón exemplar de UI del back-office vive ahí. Este
> documento **enfatiza lo nuevo de OC** (sub-estados independientes,
> adjuntos, importaciones, consolidación, duplicación, PDF, árbol de
> documentos) y reutiliza el shell sin duplicar diseño.
>
> **Construido contra:** la API real implementada en `Compras` cuando
> los PRs F1–F10 estén mergeados. Las brechas con el backend (§15)
> son tickets a abrir contra ese equipo.
>
> **Estado:** propuesta de diseño UI v1 para revisión con el owner.
> Las decisiones marcadas como `[Asunción FOCxx]` requieren confirmación
> antes de implementar pantallas.
>
> **Fecha:** 2026-05-11.

---

## 0. Cómo leer este documento

Mismas convenciones que el 05 de RQ:

- `[Decidido]` — fijado por ADR existente, decisión del backend ya
  implementada (01-04 del OC), o por el 05 de RQ que ya cerró el patrón.
- `[Asunción FOCxx]` — propuesta del frontend tech lead **para OC
  específicamente**, razonable pero pendiente de confirmación. Listadas
  en §3 con prefijo `FOC` para distinguir de las del backend (`C1`–
  `C12`) y de las del 05 de RQ (`F1`–`F14`).
- `[Diferido]` — fuera de alcance de v1; anotado para no perderlo.
- `[Verificar con equipo]` — algo que no está claro en código ni doc.

Este documento describe **qué construir y por qué en la capa de UI**,
no el código. La implementación seguirá el stack del repo (React 19 +
TanStack ecosystem + shadcn/ui + Tailwind v4) — ver §4.

> **Reuso primero (regla del proyecto):** donde un patrón ya está
> validado en el 05 de RQ, este doc apunta a `§X del 05 de RQ` en
> lugar de duplicar. Solo se expande lo específico de OC.

---

## 1. Posicionamiento

### 1.1 Qué cubre este documento

UI del submódulo Órdenes de Compra (módulo Compras) v1. Cubre las
pantallas que consumen los endpoints HTTP de F1–F10 de OC y los
catálogos compartidos heredados de RQ.

### 1.2 Qué NO cubre

- Pantallas de RQ (vive en `compras-requisiciones/05-frontend-diseno.md`).
- Pantallas de Almacén-Recepción / CxP / Tesorería / Activos Fijos /
  Contabilidad / BI. Conviven en el mismo shell pero su diseño vive
  en sus respectivos documentos (cuando lleguen).
- **Administración de catálogos cross-empresa** (proveedores,
  artículos, incoterms, transportistas, regímenes fiscales) — viven
  en el módulo Datos Maestros. OC solo CONSUME vía selectores
  read-only (decisión §10 cerrada).
- **CRUD de catálogos para administración interna** — diferido
  post-MVP (decisión §10 cerrada).
- Cliente final destinatario en OC (diferido Fase 2 — C5).
- Reapertura de OC autorizadas (descartada — C4 cerrado como cancelar
  + recrear).

### 1.3 Posicionamiento en el shell del ERP

El shell autenticado actual ya tiene Compras activo (cerrado por RQ
en UF0-PR1) con sub-árbol `/compras/requisiciones/...`. Este diseño
**agrega un segundo sub-árbol** `/compras/ordenes/...` y un endpoint
de trazabilidad transversal `/compras/trazabilidad/...`.

Sidebar updated: bajo el grupo "Compras" se mostrarán dos items:
"Requisiciones" y "Órdenes de compra". Ambos gateados por
`compras.<recurso>.leer`.

---

## 2. Personas y flujos

### 2.1 Personas

Derivadas del mapa funcional §2 y de los 10 permisos canónicos del
01-diseño §9 (incluye `compras.ordenes.crear.sin_rq` cerrado en FOC11).

| Persona | Rol | Permisos clave |
|---|---|---|
| **Comprador** | Crea OC desde RQ (1:1) o consolidada (N:1), captura líneas/adjuntos/logística, transmite a autorización, da seguimiento. | `compras.ordenes.crear`, `.adjuntar`, `.leer`, `.reportes.partidas_abiertas` |
| **Jefe de Compras (Rodrigo)** | Autoriza N1. Puede rechazar con motivo. Edita información logística post-aut (transportista, guía, pedimento). | + `.autorizar.nivel1`, `.logistica` |
| **Director** | Autoriza N2 (firma final). Puede rechazar con motivo. Habilita cancelación con recepciones parciales (doble auth). | + `.autorizar.nivel2`, `.cancelar.doble` |
| **Almacén de Insumos** | Solo lectura desde OC. Opera entradas físicas desde el submódulo Recepción (cuando exista). | `.leer` |
| **Cuentas por Pagar (CxP)** | Solo lectura desde OC para validar facturas recibidas. Opera factura desde el submódulo CxP (cuando exista). | `.leer` |
| **Auditor / Lectura** | Solo consulta histórica y bandejas. | `.leer` |

### 2.2 Flujo del Comprador — Creación 1:1 desde RQ

1. Entra a `/compras/requisiciones` (bandeja de RQ). Filtra por
   `estado = Autorizada AND comprometida_en_oc_id IS NULL`.
2. Selecciona una RQ y clickea "**Convertir en OC**".
3. Sheet (slide-from-right) "Nueva OC desde requisición" se abre con
   datos pre-llenados (sucursal, almacén destino, departamento
   solicitante, líneas).
4. Selecciona proveedor (combobox con búsqueda; muestra historial de
   últimas 100 compras al elegir).
5. Captura precio unitario por línea, fecha de entrega esperada,
   condiciones de pago, información logística.
6. **Adjunta cotización ganadora** (drag-and-drop o file picker).
   Marca tipo de documento.
7. Click "Guardar borrador" o "Transmitir a autorización".
8. Si transmite: estado pasa a `EnAutorizacionJefeCompras`. La RQ
   queda marcada como comprometida y desaparece del selector.

### 2.3 Flujo del Comprador — Consolidación N:1

1. Entra a `/compras/ordenes` y clickea "**Nueva OC**".
2. Sheet "Nueva OC" se abre vacía. Selecciona proveedor y sucursal
   destino primero (estos definen el filtro del selector de RQs).
3. Click "**Agregar requisiciones**" → modal con multi-select de RQs
   disponibles **filtradas por sucursal seleccionada** (restricción
   §10.5 cerrada). Mezcla de departamentos permitida.
4. Selecciona N RQs (con preview de líneas). Confirma.
5. Las líneas se agregan preservando trazabilidad línea-a-línea (sin
   sumar cantidades del mismo artículo de RQs distintas — política
   C8).
6. Captura precios, condiciones, logística, adjuntos.
7. Transmite a autorización.

### 2.4 Flujo del Comprador — Sin requisición previa (excepción)

1. Click "Nueva OC" → Sheet → marca toggle "**Sin requisición previa**"
   (visible solo si tiene permiso especial — `[Asunción FOC11]`).
2. Captura motivo obligatorio + adjunta correo de autorización.
3. Resto del flujo normal (proveedor, líneas manuales, transmitir).

### 2.5 Flujo del Jefe de Compras (Autorización N1)

1. Entra a `/compras/ordenes/pendientes-autorizacion` (filtrada por
   `autorizar.nivel1`).
2. Click una OC → detalle completo (cabecera + líneas + cubrimiento
   sub-estados + adjuntos + autorizaciones previas + timeline).
3. Acción "**Aprobar**" → confirm dialog → POST autorización N1.
   - Estado pasa a `EnAutorizacionDireccion`.
   - Notificación a Director (cuando Notificaciones exista).
4. Acción "**Rechazar**" → modal motivo (reusa de RQ — `MotivoRechazoSelector`
   con `aplicaA = OrdenCompra` bitmask).
5. Acción "**Editar logística**" (transportista, guía, instrucciones)
   — disponible en cualquier estado post-borrador.

### 2.6 Flujo del Director (Autorización N2)

1. Entra a `/compras/ordenes/pendientes-autorizacion` (filtrada por
   `autorizar.nivel2`).
2. Solo ve OCs con `Estado = EnAutorizacionDireccion` (N1 ya firmado).
3. Acción "**Aprobar**" → estado pasa a `Autorizada`.
   - Setea `FechaContabilizacion = now()`.
   - Dispara generación de PDF (síncrono dentro del handler).
   - Notificación al comprador (cuando Notificaciones exista).
4. Acción "**Rechazar**" → vuelve a `Rechazada` con motivo.

### 2.7 Flujo de seguimiento (post-autorización)

1. Comprador entra a `/compras/ordenes/{id}` para ver una OC autorizada.
2. Visualiza **3 sub-estados independientes**:
   - Recepción: SinRecepcion / Parcial / Completa
   - Facturación: SinFactura / Parcial / Completa
   - Pago: SinPago / Parcial / Pagada
3. Click "**Ver árbol de documentos**" → vista cross-módulo con
   trazabilidad RQ → OC → Recepción → Factura → Pago.
4. Cuando los 3 sub-estados llegan a Completa/Completa/Pagada → OC
   pasa automáticamente a `Cerrada`.

### 2.8 Flujo de cancelación

1. Sin recepciones: cancela con motivo (1 firma) → estado `Cancelada`,
   RQs liberadas al pool.
2. Con recepciones parciales: requiere **doble firma** (N1 + N2 + el
   comprador en el mismo POST). UI guía paso a paso. Las RQs se
   liberan solo por la cantidad no recibida.

### 2.9 Flujo de duplicación (C4)

1. Desde una OC `Cancelada` o `Rechazada`, action menu →
   "**Duplicar OC**".
2. Confirm dialog que muestra preview: "Se creará una OC nueva con
   cabecera + líneas; no se copiarán adjuntos, autorizaciones ni
   vínculos a RQs (debes re-seleccionar)".
3. Confirma → backend crea OC nueva en `Borrador` con `oc_origen_id`
   apuntando a la origen. Frontend navega a la nueva OC.
4. Comprador modifica lo necesario y transmite a autorización
   normal.

---

## 3. Asunciones de frontend (OC-específicas)

Las asunciones del backend (C1–C12) están cerradas. Las del frontend
heredadas de RQ (F1–F14) aplican tal cual. Estas son asunciones
**específicas de OC** que se validan antes del primer PR de UI OC.

| # | Asunción | Default propuesto | Si el cliente dice "no" |
|---|---|---|---|
| FOC1 | **Sub-estados visualizados como 3 barras horizontales segmentadas** | Componente `<SubEstadosBar />` con 3 barras (Recepción, Facturación, Pago), cada una con segmentos por línea. Patrón visual + color + número visible (no solo tooltip — heredado de F6 de RQ). En el detalle, las 3 barras viven en una sección "Progreso" sticky arriba de líneas. | Si exige stepper (puntos conectados) o tabla, +3 días. |
| FOC2 | **Adjuntos: drag-and-drop con preview inline** | Componente `<AdjuntosManager />` con zona drop, lista de adjuntos con miniatura (PDFs primera página, imágenes thumbnail). Tipo de documento por selector al subir. **Promover a `components/erp/adjuntos/`** desde el inicio (cross-módulo: CxP, Activos lo van a consumir). | Si exige modal separado para cada upload, simplifica pero degrada UX. |
| FOC3 | **Nueva OC: Sheet único con 3 modos visuales** | Sheet "Nueva OC" detecta el modo según punto de entrada: (a) desde bandeja RQ "Convertir" → modo "1:1 desde RQ" con cabecera pre-llenada bloqueada; (b) desde "Nueva OC" → modo "Consolidación" con botón "Agregar requisiciones"; (c) desde "Nueva OC sin RQ" (con permiso especial) → modo "Sin RQ previa" con toggle + motivo. **Estado del Sheet** preservado vía `useFormIdempotencyKey` + confirmación al cerrar si `isDirty`. | Si exige 3 rutas separadas, fragmenta el componente. |
| FOC4 | **Selector de RQs para consolidación: modal con multi-select + filtros server-side** | Modal `<SelectorRequisicionesConsolidacion>` invocado desde Sheet de Nueva OC. Tabla con checkboxes, filtros (departamento, requisitante, fecha, búsqueda por folio). Server-side pagination. Preview de líneas al expandir fila. Restricción de sucursal aplicada automáticamente (input read-only). | Si exige drag-and-drop, +3 días. |
| FOC5 | **PDF embebido + descarga** | Pestaña "PDF" en detalle P3 con `<iframe>` o viewer (react-pdf) embebido. Botón "Descargar" arriba. URL del blob: `GET /api/v1/compras/ordenes/{id}/pdf`. | Si exige preview-only sin descarga directa, ajustar permisos. |
| FOC6 | **Árbol de documentos: vista dedicada cross-módulo** | Ruta `/compras/trazabilidad/oc/{id}` con vista que renderiza el grafo bidireccional (RQ ← → OC ← → Recepción ← → Factura ← → Pago). Componente `<ArbolDocumentos>` **promovido a `components/erp/trazabilidad/`** desde el inicio (CxP, Recepción, Tesorería lo consumirán). | Si exige inline en P3 sin vista separada, +1 semana. |
| FOC7 | **Duplicar OC: confirm dialog con preview de qué se copia** | Click "Duplicar" → `<ConfirmDuplicarDialog>` muestra: "Se copiarán: cabecera (proveedor, sucursal, líneas). NO se copiarán: adjuntos, autorizaciones, vínculos a RQs". Botón primario "Duplicar y abrir nueva OC". Navega tras confirmar. | Si exige selector de qué copiar, +1 semana (sobreingeniería). |
| FOC8 | **Información logística + importación en tabs dentro del detalle** | En P3, sección "Información" con 3 tabs: "Logística", "Importación" (oculto si `EsImportacion = false`), "Financiera". Cada tab tiene su form con `useFormIdempotencyKey`. Edita inline con borde dashed amber (patrón del exemplar). | Si exige todo plano en una sola tabla, satura captura. |
| FOC9 | **Stepper visual del flujo de autorización N1 → N2** | En P3, sección "Autorización" muestra un stepper visual: ⚪ Borrador → 🟡 En autorización (N1) → 🟡 En autorización (Dirección) → 🟢 Autorizada. Highlight del paso actual. Tooltips con nombre del autorizador en cada paso completado. | Si exige solo texto plano, simplifica pero pierde claridad. |
| FOC10 | **Partidas abiertas: tabla densa con filtros sticky + KPI cards arriba** | Ruta `/compras/ordenes/partidas-abiertas` con: (a) cards arriba mostrando totales agregados (total pendiente recibir, pendiente facturar, días promedio atrasados), (b) tabla con filtros sticky lateral (estado, sub-estados, proveedor, comprador, contenedor, ruta, semana), (c) columna calculada de días atrasados con color (verde / amarillo / rojo según umbral). | Si exige tabla simple sin KPI cards, reduce trabajo de polish (~2 días). |
| FOC11 | **Toggle "Sin RQ previa" gateado por permiso especial** | El toggle en Sheet de Nueva OC para crear sin requisición previa requiere permiso `compras.ordenes.crear.sin_rq`. **Confirmar con backend si crea este permiso adicional**, o si va dentro de `compras.ordenes.crear` con auditoría diferente. | Si todos los compradores pueden, simplifica. Si nadie puede, contradice el mapa funcional §4.3. |
| FOC12 | **Filtros de bandeja preservan estado en URL via search params** | Filtros (estado, sub-estados, proveedor, fechas, contenedor, etc.) viven como search params Zod-validados. Recargar la página o compartir URL preserva el filtro. Patrón ya validado en P1 de RQ. | OK, es el patrón estándar. |
| FOC13 | **`<EstadoBadge>` extendido con 7 estados de OC** | Reusar el componente parametrizado de RQ. Agregar 7 enums de OC (Borrador, EnAutorizacionJefeCompras, EnAutorizacionDireccion, Autorizada, Cerrada, Cancelada, Rechazada) con colores y tooltips de glosario. Verificar contraste WCAG AA. | OK, es extensión simple. |
| FOC14 | **Bandeja general no usa real-time updates en MVP** | Igual que RQ F14. SignalR Capa 2 (soft lock) sí está activo (UF8-PR1 mergeado), pero la bandeja se refresca con `staleTime: 30s` + manual refresh. Real-time invalidación se evalúa post-v1. | OK. |

---

## 4. Stack frontend (auditado contra el repo, 2026-05-11)

Sin cambios respecto al §4 del 05 de RQ. Toda la infraestructura
introducida por RQ (UF0-PR1 + UF0-PR2: cliente HTTP enriquecido,
permission codes, primitives shadcn, UX kit, ConflictResolutionDialog,
useCollaboration con hub real activo desde UF8-PR1) **se reusa tal
cual** para OC.

### 4.1 Lo que falta del stack para OC (extensiones puntuales)

| Pieza | Estado | Cuándo se introduce |
|---|---|---|
| **`features/compras/ordenes/`** (carpeta del submódulo) | ❌ vacía | UF-OC0 |
| **DTOs mirror de OC** (`features/compras/ordenes/api/types.ts`) | ❌ no existe | UF-OC1 |
| **`<SubEstadosBar />`** | ❌ no existe | UF-OC1 (component nuevo) |
| **`<AdjuntosManager />`** | ❌ no existe | UF-OC2 (promover a `components/erp/adjuntos/`) |
| **`<SelectorRequisicionesConsolidacion>`** | ❌ no existe | UF-OC4 (vive en `features/compras/ordenes/`) |
| **`<InformacionLogisticaForm>` + `<InformacionImportacionForm>`** | ❌ no existen | UF-OC2 |
| **`<ConfirmDuplicarDialog>`** | ❌ no existe | UF-OC6 |
| **`<ArbolDocumentos>`** | ❌ no existe | UF-OC7 (promover a `components/erp/trazabilidad/`) |
| **PDF viewer** (`react-pdf` o equivalente) | ❌ no instalado | UF-OC6. **Decisión pendiente**: `react-pdf` (Mozilla pdf.js wrapper) vs `<embed>` nativo del browser. Recomendación inicial: nativo `<embed>` (cero dependencias, browser lo maneja). |
| **`useUploadFile` hook** (multipart + progress) | ❌ no existe | UF-OC2 (usa `apiRequest` extendido) |

### 4.2 Extensiones a piezas existentes

| Pieza existente | Extensión |
|---|---|
| `permission-codes.ts` | Agregar las 10 constantes `compras.ordenes.*` (incluye `crear.sin_rq` por FOC11) |
| `nav.ts` (sidebar) | Agregar item "Órdenes de compra" bajo Compras |
| `<EstadoBadge>` | Agregar 7 estados de OC con colores + tooltips |
| `<DomainTermTooltip>` + `glosario.ts` | Agregar términos OC (sub-estados, consolidación, duplicar, contenedor, ruta, semana, partida abierta) |
| `<MotivoRechazoSelector>` (de RQ) | Sin cambio en componente; backend extiende seed con bitmask `aplicaA = OrdenCompra` |

---

## 5. Inventario de pantallas

Convención de rutas: `/compras/ordenes/...` y `/compras/trazabilidad/...`.
Todas viven bajo `routes/_app/compras/...` (auth guard heredado).

| ID | Nombre | Ruta | Propósito | Permisos | Estados que muestra |
|---|---|---|---|---|---|
| **P1** | Bandeja general de OCs | `/compras/ordenes` | Listado paginado con filtros (estado, sub-estados, proveedor, fechas, contenedor, ruta, semana, importe). Default: ordenado por fecha desc. Presets: "Mis borradores", "Autorizadas pendientes recepción", "Recibidas pendientes factura", etc. | `compras.ordenes.leer` | Todos |
| **P2** | Bandeja pendientes de autorización | `/compras/ordenes/pendientes-autorizacion` | Bandeja del autorizador (N1 o N2 según permiso). Filtros: proveedor, comprador titular, importe. | `compras.ordenes.leer` (+ visibilidad gateada por `.autorizar.nivel1` o `.nivel2`) | `EnAutorizacionJefeCompras`, `EnAutorizacionDireccion` |
| **P3** | Detalle de OC | `/compras/ordenes/$id` | Pantalla principal con master-detail layout: cabecera + 3 sub-estados + líneas + adjuntos + información logística/importación/financiera + autorizaciones (timeline) + acciones contextuales por estado (§6). | `compras.ordenes.leer` | Todos |
| **P4** | Nueva OC (Sheet) | `/compras/ordenes/nueva` o overlay sobre P1 | Sheet de creación con 3 modos: (a) 1:1 desde RQ, (b) consolidación N:1, (c) sin RQ previa. Wizard de 1 paso (cabecera) → guarda como Borrador → redirige a P3 para líneas y adjuntos. | `compras.ordenes.crear` | — |
| **P5** | Selector RQs para consolidación | overlay (dialog) en P4 | Multi-select de RQs disponibles + filtros + preview de líneas. Restricción sucursal aplicada. | `compras.ordenes.crear` | — |
| **P6** | Editor de líneas (embebido en P3) | sección en P3 | Tabla densa con add/edit/delete inline en `Borrador`/`Rechazada`; read-only en estados posteriores. Borde dashed primary (agregar) / amber (editar). Para líneas con FK a RQ, badge "Desde RQ-MID2026-000045". | `compras.ordenes.crear` | `Borrador`, `Rechazada` (editable); resto read-only |
| **P7** | Modal de motivo | overlay sobre P3 | Selector motivo + texto opcional (reusa de RQ con `aplicaA = OrdenCompra`). Para rechazo, cancelación, eliminación. | el permiso de la acción | la OC donde se invoca |
| **P8** | Confirm Duplicar OC | overlay sobre P3 | Confirm dialog con preview de qué se copia. Acción primaria "Duplicar y abrir nueva". | `compras.ordenes.crear` | la OC origen (`Cancelada` o `Rechazada`) |
| **P9** | Partidas abiertas | `/compras/ordenes/partidas-abiertas` | KPI cards arriba + tabla densa con filtros sticky + columna días atrasados con color. Vista crítica del negocio (mapa funcional §8.2). | `compras.ordenes.reportes.partidas_abiertas` | `Autorizada` con sub-estados activos |
| **P10** | Árbol de documentos | `/compras/trazabilidad/oc/$id` (o `/rq/$id`, `/recepcion/$id`, etc.) | Vista grafo bidireccional cross-módulo: RQ ← OC ← Recepción ← Factura ← Pago. Cada nodo con folio + fecha + monto + enlace a su detalle. **Promover a `components/erp/trazabilidad/`**. | `compras.ordenes.leer` | — (no muestra OCs, muestra grafo) |
| **P11** | Reporte últimas 100 compras del material | `/compras/articulos/$id/historial-compras` | Histórico de compras de un artículo. Filtros: proveedor, tipo de documento, fecha, cantidad mínima. | `compras.ordenes.leer` | — |

**Total: 11 pantallas**, donde P5/P6/P7/P8 son overlay sobre P3/P4.
Top-level **6 rutas**: P1, P2, P3, P4 (Sheet o ruta dedicada), P9,
P10, P11.

> **Diferidas explícitamente:**
>
> - Selector visual de items en bandeja para acciones masivas
>   (heredado de F13 de RQ — diferido v1.1).
> - Pantalla de admin de regímenes fiscales (vive en Datos Maestros).
> - Pantalla de "Mis duplicadas" como vista propia (KPI cubierto por
>   filtro preset en P1).
> - Cliente final destinatario en P3 (diferido a Fase 2 — C5).

---

## 6. Mapeo estado ↔ acciones disponibles

Esta tabla es la **fuente única de la lógica condicional de acciones**
en P3. Cualquier botón / ítem de menú deriva su `disabled` o `hidden`
de aquí.

### 6.1 Tabla maestra

> Convención: ✅ = aparece habilitado · ⚪ = aparece deshabilitado
> con tooltip · ❌ = no aparece. "Permiso" además del `.leer`; sin
> el permiso = ❌ siempre.

| Acción | Borrador | EnAutorizJefeCompras | EnAutorizDireccion | Autorizada | Cerrada | Cancelada | Rechazada | Permiso |
|---|---|---|---|---|---|---|---|---|
| **Editar cabecera** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | `crear` |
| **Agregar línea (manual)** | ✅ si `SinRQ` | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ si `SinRQ` | `crear` |
| **Agregar línea (desde RQ)** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | `crear` |
| **Editar línea (estructural)** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | `crear` |
| **Eliminar línea** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | `crear` |
| **Editar texto_adicional línea** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ | `crear` |
| **Adjuntar documento** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ | `adjuntar` |
| **Remover adjunto** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | `crear` |
| **Editar info logística** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ | `crear` o `logistica` |
| **Editar info importación** | ✅ | ❌ | ❌ | ⚠️ solo `NumeroPedimento` | ❌ | ❌ | ✅ | `crear` o `logistica` |
| **Transmitir a autorización** | ✅ (validaciones §7.1 OK) | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | `crear` |
| **Aprobar N1** | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | `autorizar.nivel1` |
| **Aprobar N2** | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | `autorizar.nivel2` |
| **Rechazar** | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | `autorizar.nivel1` o `.nivel2` |
| **Cancelar (1 firma)** | ✅ | ✅ | ✅ | ✅ si sin recepciones | ❌ | ❌ | ✅ | `cancelar` |
| **Cancelar (doble firma)** | ❌ | ❌ | ❌ | ✅ si con recepciones | ❌ | ❌ | ❌ | `cancelar.doble` + `.nivel1` + `.nivel2` |
| **Duplicar OC** | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | `crear` |
| **Descargar PDF** | ❌ | ❌ | ❌ | ✅ | ✅ | ⚠️ histórico | ❌ | `leer` |
| **Ver árbol documentos** | ⚪ "Sin descendientes" | ⚪ | ⚪ | ✅ | ✅ | ✅ | ⚪ | `leer` |
| **Ver detalle (read-only)** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | `leer` |

> **"Aprobar N2" nunca aparece antes de N1**: el invariante del
> agregado lo exige. La UI deshabilita con tooltip "Falta autorización
> Jefe de Compras" en lugar de ocultarlo.
>
> **Duplicar OC solo en terminales**: §13.1 del 04 lo exige. UI
> deshabilita con tooltip "Solo se puede duplicar desde estado
> Cancelada o Rechazada" en estados no terminales.
>
> **Editar info logística post-autorización** es limitado a campos
> específicos (NumeroGuia, Transportista, Pedimento) — la UI muestra
> el form completo pero solo esos campos son editables; el resto
> read-only con tooltip "Solo editable en Borrador o Rechazada".

### 6.2 Implementación sugerida

Mismo patrón que §6.2 del 05 de RQ — función por acción retorna
`AccionDisponible { visible, habilitada, motivoDeshabilitada }`. Tests
parametrizados recorren cada celda de la tabla.

---

## 7. Estrategia de datos

Hereda toda la estrategia del §7 del 05 de RQ: cliente HTTP enriquecido
(`apiRequest`, `ApiError`, ProblemDetails parsing, ETag, Idempotency-Key,
retry 409, applyServerErrors). OC solo agrega:

### 7.1 DTOs mirror específicos de OC

En `features/compras/ordenes/api/types.ts`:

- `OrdenCompraListItemResponse` (para bandejas)
- `OrdenCompraDetalleResponse` (para P3)
- `LineaOrdenCompraResponse`
- `AdjuntoOcResponse`
- `AutorizacionOcResponse`
- `InformacionLogisticaResponse`
- `InformacionImportacionResponse`
- `TotalesOCResponse`
- `RequisicionDisponibleResponse` (selector consolidación)
- `PartidaAbiertaResponse` (reporte)
- `NodoArbolDocumentosResponse` (trazabilidad)
- `UltimaCompraMaterialResponse`
- Enums: `EstadoOrdenCompra` (7), `SubEstadoRecepcion`, `SubEstadoFacturacion`,
  `SubEstadoPago`, `NivelAutorizacion` (heredado), `ResultadoAutorizacion`,
  `DescuentoTipo`, `TipoDocumentoOc`.

### 7.2 Hooks de read (TanStack Query)

```typescript
useOrdenesCompra(filtros)             // bandeja general
usePendientesAutorizacionOc(nivel)    // bandeja autorizador
useOrdenCompra(id)                    // detalle (extrae ETag)
useRequisicionesDisponibles(sucursalId) // selector consolidación
usePartidasAbiertas(filtros)
useArbolDocumentos(tipo, id)
useUltimas100ComprasMaterial(articuloId, filtros)
usePdfOrdenCompra(id)                 // URL del blob, no carga el PDF
```

### 7.3 Hooks de mutate

```typescript
useCrearOrdenCompraDesdeRequisicion()
useCrearOrdenCompraVacia()
useAgregarLineaDesdeRequisicion()
useAgregarLineaManual()
useActualizarLinea()
useEliminarLinea()
useActualizarCabecera()
useActualizarInformacionLogistica()
useActualizarInformacionImportacion()
useAdjuntarDocumento()                // multipart, con progress
useRemoverAdjunto()
useEnviarAAutorizacion()
useAutorizarOrdenCompra()
useRechazarOrdenCompra()
useCancelarOrdenCompra()
useDuplicarOrdenCompra()
```

Cada uno con:
- `Idempotency-Key` automático via `useFormIdempotencyKey`.
- `If-Match` header desde el ETag capturado en `useOrdenCompra`.
- Invalidación de queries relevantes en `onSuccess`.
- Retry automático en 409 con back-off (heredado de cliente HTTP).

### 7.4 Multipart upload (adjuntos)

Nuevo helper `useUploadFile`:

```typescript
function useUploadFile<TResponse>(endpoint: string) {
  // Wrapper sobre `apiRequest` que:
  // - Construye FormData con archivo + metadata (tipo_documento_id)
  // - Reporta progress via XMLHttpRequest o fetch + stream
  // - Maneja errores ProblemDetails como apiRequest
  // - Retorna { upload, progress, isLoading, error }
}
```

Usado en `AdjuntosManager` para subir archivos al endpoint `POST .../adjuntos`.

---

## 8. Concurrencia y soft lock en UX

OC hereda toda la estrategia del §8 del 05 de RQ.

### 8.1 Soft lock activo desde release v1

El `CollaborationHub` SignalR está activo (UF8-PR1 mergeado por RQ).
OC declara `OrdenCompra` en la lista de entidades con awareness
colaborativo del módulo (cambio trivial en
`Compras.Infrastructure.Collaboration.EntidadesConSoftLock.cs`).

En el detalle P3:
- `<CollaborationIndicator>` arriba (heredado) muestra avatares
  apilados de los compradores con el documento abierto.
- Si dos compradores tienen el mismo P3 abierto, banner "Pedro García
  también tiene esta OC abierta" con tono informativo (no bloqueante).
- Si Pedro está modificando (focus en un input), badge "Pedro está
  editando".

### 8.2 Conflictos 409 con `<ConflictResolutionDialog>`

Heredado tal cual de RQ (F9 cerrado). Aplica a todas las mutaciones
de OC. El dialog:
1. Captura form state local antes de refrescar.
2. Refresca + muestra diff filtrado (solo campos que solapan).
3. Ofrece "Reaplicar mis cambios" como primaria.

Sin merge automático (v1.1 si emerge necesidad).

---

## 9. Componentes nuevos / extendidos para OC

### 9.1 Componentes nuevos de OC

| Componente | Ubicación | Propósito |
|---|---|---|
| `<SubEstadosBar>` | `features/compras/ordenes/components/` | 3 barras horizontales para Recepción / Facturación / Pago con segmentos por línea (FOC1). Promover a `components/erp/oc-flow/` si CxP lo consume. |
| `<AdjuntosManager>` | **`components/erp/adjuntos/`** (cross-módulo desde el inicio) | Drag-and-drop + lista + preview + selector tipo. CxP, Activos, Recepción lo consumirán. |
| `<SelectorRequisicionesConsolidacion>` | `features/compras/ordenes/components/` | Modal multi-select con filtros (FOC4). Específico de OC. |
| `<InformacionLogisticaForm>` | `features/compras/ordenes/components/` | Form de logística (dirección, transportista, guía, instrucciones). |
| `<InformacionImportacionForm>` | `features/compras/ordenes/components/` | Form de importación (incoterm, país origen, contenedor, ruta, semana, pedimento). Condicional `EsImportacion`. |
| `<TotalesFinancierosForm>` | `features/compras/ordenes/components/` | Form de descuento global, gastos adicionales, redondeo. Cálculo de totales en tiempo real desde el agregado. |
| `<ConfirmDuplicarDialog>` | `features/compras/ordenes/components/` | Confirm con preview de qué se copia (FOC7). |
| `<ArbolDocumentos>` | **`components/erp/trazabilidad/`** (cross-módulo desde el inicio) | Vista grafo bidireccional. CxP, Tesorería, Recepción lo consumirán. |
| `<StepperAutorizacionOc>` | `features/compras/ordenes/components/` | Stepper visual N1 → N2 (FOC9). |
| `<KpiCardsPartidasAbiertas>` | `features/compras/ordenes/components/` | Cards de totales agregados arriba de la tabla (FOC10). |
| `<DiasAtrasadosBadge>` | `features/compras/ordenes/components/` | Badge con color (verde/amarillo/rojo según umbral) para días atrasados. |
| ~~`<ProveedorComboBox>`~~ | **YA EXISTE** en `components/erp/selectors/ProveedorSelector.tsx` (cross-módulo desde RQ). | OC lo reusa **tal cual** — no crea componente nuevo. |
| `<HistorialComprasProveedor>` | `features/compras/ordenes/components/` | Componente complementario al `<ProveedorSelector>` existente: cuando el comprador elige proveedor en Sheet P4, se renderiza una sección que llama `useUltimas100ComprasMaterial({ proveedorId })` y muestra las últimas compras. Promover a `components/erp/` solo si CxP lo necesita. |

### 9.2 Componentes extendidos del shell

| Componente existente | Extensión para OC |
|---|---|
| `<EstadoBadge>` | + 7 enums de OC con colores y tooltips |
| `<MoneyDisplay>` | Sin cambios |
| `<DateTimeDisplay>` | Sin cambios |
| `<NaturalezaBadge>` (de RQ) | Reusar solo si en P3 se muestran las líneas con su naturaleza heredada de la RQ origen — `[Asunción FOC15]` |
| `<MotivoRechazoSelector>` (de RQ) | Sin cambio en componente; backend extiende seed con bitmask |
| `<EmpresaSelector>` (topbar) | Sin cambio |
| `<UsuarioSelector>` | Reutilizado en confirm de doble autorización |
| `<Breadcrumbs>` | Sin cambio |
| `<DomainTermTooltip>` + glosario | Glosario extendido con términos OC |

### 9.3 Reglas de promoción cross-módulo

Heredadas de §14.7 del 01-diseño:

- 3 módulos consumidores → `components/erp/` o `SharedKernel`.
- 2 módulos → `components/erp/<domain>/` con prop de parametrización.
- 1 consumidor → `features/<modulo>/`.

OC promueve **desde el inicio** (no preventivamente, pero porque CxP/
Recepción claramente los consumirán):

- `<AdjuntosManager>` → `components/erp/adjuntos/`
- `<ArbolDocumentos>` → `components/erp/trazabilidad/`
- ~~`<ProveedorComboBox>` → `components/erp/selectors/`~~ (no aplica: `<ProveedorSelector>` ya existe ahí desde RQ)

---

## 10. Pantallas detalladas

### 10.1 P1 — Bandeja general de OCs

**Ruta**: `/compras/ordenes`

**Layout**: aplicar el patrón cross-módulo del exemplar (ver
[`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md)):

- Sub-topbar sticky con: título, search global, presets pills, botón
  primario "Nueva OC".
- Filtros laterales (drawer colapsable) con: estado, sub-estados
  (Recepción/Facturación/Pago), proveedor (combobox), comprador,
  rango de fechas, importe min/max, contenedor, ruta, semana de
  embarque.
- Tabla densa con columnas: Folio, Fecha, Proveedor, Comprador,
  Líneas (count), Total (Money), Estado (badge), Sub-estados
  (3 badges compactos), Acciones.
- Paginación offset-based (50/200 por página).
- Click en fila → navega a P3 (`/compras/ordenes/{id}`).

**Presets** (chips clickeables que setean filtros):

- "Mis borradores" → `estado = Borrador AND comprador_titular = me`
- "Pendientes recepción" → `estado = Autorizada AND sub_estado_recepcion ≠ Completa`
- "Pendientes factura" → `estado = Autorizada AND sub_estado_facturacion ≠ Completa`
- "Pendientes pago" → `estado = Autorizada AND sub_estado_pago ≠ Pagada`
- "Cerradas (mes actual)" → `estado = Cerrada AND fecha_cierre_in_mes_actual`
- "Canceladas / Rechazadas" → `estado IN (Cancelada, Rechazada)` (auditoría)

**Search params Zod-validados** (`bandeja-oc-search-schema.ts`):

```typescript
const searchSchema = z.object({
  estado: z.array(z.enum([...])).optional(),
  subEstadoRecepcion: z.array(z.enum([...])).optional(),
  // ... resto
  proveedorId: z.string().uuid().optional(),
  fechaDesde: z.string().optional(),  // ISO date
  fechaHasta: z.string().optional(),
  contenedor: z.string().optional(),
  q: z.string().optional(),            // folio o referencia proveedor
  offset: z.number().int().nonnegative().default(0),
  limit: z.number().int().min(10).max(200).default(50),
});
```

**Estados de UI**:

- Loading: `<TableSkeleton />` (heredado).
- Empty: `<EmptyState />` con CTA "Nueva OC" (gateado por `.crear`).
- Error: `<ErrorState />` con retry.

### 10.2 P2 — Bandeja pendientes de autorización

**Ruta**: `/compras/ordenes/pendientes-autorizacion`

**Filtro automático** por permiso:
- Usuario con `autorizar.nivel1` ve `estado = EnAutorizacionJefeCompras`.
- Usuario con `autorizar.nivel2` ve `estado = EnAutorizacionDireccion`.
- Usuario con ambos ve los dos (tab switcher).

**Layout**: igual que P1 pero con columnas optimizadas para autorizador:
Folio, Proveedor, Comprador, Líneas, Total, Días esperando (calculado
desde `fecha_envio_autorizacion`), Acción "Ver detalle" (botón único
en cada fila).

**Sin acciones inline de aprobar/rechazar (FOC16 cerrado)**: el
autorizador **siempre debe abrir P3** antes de firmar o rechazar. La
fila solo navega al detalle. Esto previene firmas-sin-revisar y
asegura que el autorizador vea líneas, adjuntos y autorizaciones
previas antes de decidir. Patrón conservador típico de ERPs maduros.

### 10.3 P3 — Detalle de OC (corazón del submódulo)

**Ruta**: `/compras/ordenes/$id`

**Layout master-detail responsive**:

```
┌────────────────┬─────────────────────────────────────────────────┐
│  Aside list    │  Detalle principal                              │
│  320px sticky  │  ┌─────────────────────────────────────────┐    │
│                │  │ Sub-topbar: Folio + EstadoBadge +       │    │
│  RQs upstream  │  │ acciones contextuales                    │    │
│  + OCs hijas   │  ├─────────────────────────────────────────┤    │
│  (si duplicada)│  │ <SubEstadosBar> (3 barras)              │    │
│                │  ├─────────────────────────────────────────┤    │
│  CollaborationIndicator │ Cabecera (proveedor, sucursal, total)   │    │
│                │  ├─────────────────────────────────────────┤    │
│                │  │ <StepperAutorizacionOc>                 │    │
│                │  ├─────────────────────────────────────────┤    │
│                │  │ Tabs: Líneas | Información | Adjuntos | │    │
│                │  │       Autorización | Historial          │    │
│                │  │  ↓                                       │    │
│                │  │  Tab activa (full width)                 │    │
│                │  └─────────────────────────────────────────┘    │
└────────────────┴─────────────────────────────────────────────────┘
```

**Aside list** (heredado del patrón exemplar):
- Si la OC viene de N RQs → lista compacta de RQs origen con badges
  de estado.
- Si la OC fue duplicada → enlace a la OC origen + lista de OCs
  hermanas (otras duplicaciones de la misma origen).

**Sub-topbar de detalle** con:
- Folio en grande + ReferenciaProveedor en chico.
- `<EstadoBadge>` + Días en el estado.
- Botones primarios contextuales (Transmitir / Aprobar / Rechazar /
  Duplicar / etc.) según §6.1.
- Menú overflow con acciones secundarias (Editar logística, Cancelar,
  Descargar PDF, Ver árbol docs).
- `data-print="hidden"` para no aparecer en impresión.

**Tabs internos**:

- **Líneas**: tabla densa con columnas Articulo, Cant, UM, Precio,
  Desc, IVA, Subtotal, Sub-estados de línea (cantidad recibida vs
  total), Departamento, RQ origen (link si aplica), Acciones inline.
  Borde dashed primary al agregar línea, amber al editar (patrón
  exemplar).
- **Información**: tabs anidados Logística / Importación (si aplica) /
  Financiera. Cada uno con inline editing siguiendo el patrón.
- **Adjuntos**: `<AdjuntosManager>` con lista + drag-and-drop.
- **Autorización**: `<StepperAutorizacionOc>` (visual) + tabla de
  autorizaciones registradas (autor, fecha, resultado, motivo si
  rechazo).
- **Historial**: timeline de eventos (creación, transmisión,
  autorizaciones, recepciones, facturas, pagos, cancelación,
  duplicación). Heredado patrón de `<TimelineAutorizaciones>` de RQ,
  extendido a más tipos de evento.

**Acciones contextuales** vienen de §6.1 — la UI deriva de la tabla
maestra, no hardcoded.

### 10.4 P4 — Nueva OC (Sheet)

**Trigger**:
- Desde bandeja RQ (botón "Convertir en OC") → modo 1:1.
- Desde bandeja OC (botón "Nueva OC") → modo consolidación o vacía.
- Desde shell (Quick Create popover) → modo selector ("¿Desde RQ o
  sin RQ?").

**Layout Sheet slide-from-right**:

```
┌───────────────────────────────────────────────────────────┐
│ Nueva OC                                       [X] cerrar │
│ ─────────────────────────────────────────────────────────│
│ Modo: [● 1:1 desde RQ]  [○ Consolidación]  [○ Sin RQ]    │
│                                                           │
│ Proveedor: [combobox con búsqueda]                       │
│ Sucursal destino: [select]                               │
│ Almacén destino default: [select]                        │
│ Moneda: MXN [▼]    Tipo cambio: [si != MXN]              │
│ Condiciones de pago: [select]                            │
│ Bandera: [☐ Es importación]                              │
│                                                           │
│ ─── Si modo consolidación ───                            │
│ [Agregar requisiciones]                                   │
│ Lista de RQs seleccionadas:                              │
│   • RQ-MID2026-000045 (3 líneas)                         │
│   • RQ-MID2026-000089 (2 líneas)                         │
│                                                           │
│ ─── Si modo "Sin RQ previa" ───                          │
│ Motivo: [textarea]                                       │
│ Adjuntar correo autorización: [drop zone]                │
│                                                           │
│ ─── Footer sticky ───                                    │
│ [Cancelar]                          [Guardar borrador]   │
└───────────────────────────────────────────────────────────┘
```

**Comportamiento**:
- `useFormIdempotencyKey()` para el POST.
- `useUnsavedChangesGuard(isDirty)` para confirm al cerrar.
- Submit exitoso → redirige a P3 con la OC en `Borrador` para que el
  comprador agregue precios, adjunte cotización, y transmita.

### 10.5 P5 — Selector de RQs para consolidación

**Trigger**: botón "Agregar requisiciones" dentro de P4 (modo
consolidación).

**Layout dialog modal**:

```
┌──────────────────────────────────────────────────────────┐
│ Seleccionar requisiciones para consolidar       [X]      │
│ ────────────────────────────────────────────────────────│
│ Filtrar: [☐ Mi departamento] [Buscar folio]              │
│ Sucursal: ▒MID (heredado de OC, no editable)▒            │
│                                                           │
│ ┌─────────────────────────────────────────────────────┐  │
│ │ ☐ Folio          Fecha     Requisitante  Líneas  ▶ │  │
│ │ ☑ RQ-MID2026-... 12/05/26  Juan García   3        │  │
│ │ ☑ RQ-MID2026-... 13/05/26  María López   5        │  │
│ │ ☐ RQ-TOL2026-... 14/05/26  Pedro Ruiz    2  (otra │  │
│ │                                            sucursal│  │
│ │                                            — no   │  │
│ │                                            elegible│  │
│ ├─────────────────────────────────────────────────────┤  │
│ │ [< 1 2 3 ... >]                                     │  │
│ └─────────────────────────────────────────────────────┘  │
│                                                           │
│ 2 RQs seleccionadas (8 líneas total)                     │
│                                                           │
│ [Cancelar]                              [Agregar (2)]    │
└──────────────────────────────────────────────────────────┘
```

**Comportamiento**:
- Query `useRequisicionesDisponibles(sucursalId)` con filtro
  server-side.
- Restricción de sucursal: solo aparecen RQs de la misma sucursal
  que la OC. RQs de otras sucursales NO aparecen (filtro backend).
- Expandir fila → preview de líneas de esa RQ.
- Submit → cierra modal, agrega RQs al payload del Sheet de P4.

### 10.6 P6 — Editor de líneas (sección embebida en P3)

Heredado del patrón exemplar (master-detail con inline editing).
Especificidad de OC:

- **Líneas desde RQ**: badge azul "Desde RQ-..." con enlace.
  Cantidad y artículo no editables (vienen de la RQ); precio sí.
- **Líneas manuales** (solo si `SinRQ` = true): todo editable.
- **Múltiples líneas del mismo artículo desde RQs distintas**: se
  muestran como entradas separadas (política C8). Tooltip explicativo.
- Inline editing con borde dashed primary (agregar) / amber (editar).
- Cálculo de subtotal e IVA en tiempo real al cambiar precio o
  cantidad.
- Sub-estado de línea (cantidad recibida / cantidad total) visible
  cuando OC está autorizada.

### 10.7 P7 — Modal de motivo

Heredado tal cual de RQ. Backend ya extiende seed con `aplicaA =
OrdenCompra` bitmask. Solo el filtro de motivos disponibles cambia
según la acción y el bitmask.

### 10.8 P8 — Confirm Duplicar OC

Modal de confirmación específico (FOC7):

```
┌──────────────────────────────────────────────────────────┐
│ Duplicar OC                                       [X]    │
│ ────────────────────────────────────────────────────────│
│ Estás duplicando OC-MID2026-000124 (Cancelada el         │
│ 11/05/26).                                                │
│                                                           │
│ Se copiará a la nueva OC:                                │
│  ✓ Cabecera (proveedor, sucursal, almacén, moneda,       │
│    condiciones, observaciones)                            │
│  ✓ Líneas (cantidades y precios, sin vínculo a RQs)      │
│  ✓ Información logística                                  │
│                                                           │
│ NO se copiará:                                            │
│  ✗ Adjuntos (cotización debe ser nueva)                  │
│  ✗ Autorizaciones (nuevo ciclo de firma)                 │
│  ✗ Vínculos a requisiciones                              │
│                                                           │
│ La nueva OC quedará en estado Borrador, ligada a esta    │
│ via "OC origen".                                          │
│                                                           │
│ [Cancelar]                    [Duplicar y abrir nueva]   │
└──────────────────────────────────────────────────────────┘
```

### 10.9 P9 — Partidas abiertas (reporte crítico)

**Ruta**: `/compras/ordenes/partidas-abiertas`

**Layout** (FOC10):

```
┌─────────────────────────────────────────────────────────┐
│ Partidas abiertas                                       │
│ ───────────────────────────────────────────────────────│
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐   │
│  │ $1.2M    │ │ $850K    │ │ $400K    │ │ 23 OCs   │   │
│  │ Pte recib│ │ Pte fact │ │ Pte pago │ │ atrasadas│   │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘   │
│                                                          │
│ Filtros: [estado ▾] [sub-recepción ▾] [proveedor ▾]    │
│          [comprador ▾] [contenedor ▾] [ruta ▾] [semana ▾]│
│                                                          │
│ ┌──────────────────────────────────────────────────────┐│
│ │ Folio  │Prov │ Total │Rec │Fac │Pago│Días │Acciones ││
│ ├──────────────────────────────────────────────────────┤│
│ │ OC-... │ ... │ $50k  │ 50%│ 0% │ 0% │ 🔴12│ Ver     ││
│ │ OC-... │ ... │ $30k  │100%│ 50%│ 0% │ 🟡5 │ Ver     ││
│ │ OC-... │ ... │ $20k  │ 0% │ 0% │ 0% │ 🟢2 │ Ver     ││
│ └──────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────┘
```

**Días atrasados** con `<DiasAtrasadosBadge>`:
- 🟢 verde: 0-7 días después de fecha entrega esperada.
- 🟡 amarillo: 8-14 días.
- 🔴 rojo: > 14 días.

**Performance**: P95 < 500ms con 5k OCs activas. Si degrada, vista
materializada o índice adicional (decisión en F7-PR1 del backend).

### 10.10 P10 — Árbol de documentos (cross-módulo)

**Ruta**: `/compras/trazabilidad/oc/$id` (o por nodo origen).

**Layout** (FOC6):

```
┌─────────────────────────────────────────────────────────┐
│ Trazabilidad: OC-MID2026-000124                        │
│ ───────────────────────────────────────────────────────│
│                                                          │
│  Upstream                                                │
│  ┌──────────────┐    ┌──────────────┐                   │
│  │ RQ-MID2026-..│    │ RQ-MID2026-..│                   │
│  │ Autorizada   │    │ Autorizada   │                   │
│  │ Juan García  │    │ María López  │                   │
│  └──────┬───────┘    └──────┬───────┘                   │
│         └───────┬───────────┘                            │
│                 ▼                                        │
│         ┌──────────────────────┐                         │
│         │ OC-MID2026-000124    │  ← actual               │
│         │ Autorizada           │                         │
│         │ Rodrigo Chay         │                         │
│         │ $50,000 MXN          │                         │
│         └────────┬─────────────┘                         │
│                  │                                       │
│  Downstream      ▼                                       │
│         ┌──────────────────────┐                         │
│         │ Recepción RC-2024-.. │                         │
│         │ Completa             │                         │
│         │ 10/05/26             │                         │
│         └────────┬─────────────┘                         │
│                  ▼                                       │
│         ┌──────────────────────┐                         │
│         │ Factura FP-7843      │                         │
│         │ Parcial (60%)        │                         │
│         │ $30,000 / $50,000    │                         │
│         └────────┬─────────────┘                         │
│                  ▼                                       │
│         ┌──────────────────────┐                         │
│         │ Pago PG-2024-..      │                         │
│         │ Pagada               │                         │
│         └──────────────────────┘                         │
└─────────────────────────────────────────────────────────┘
```

**Cada nodo es clickeable** → navega al detalle del documento
correspondiente (RQ → P3 de RQ; OC → P3 de OC; resto cuando los
submódulos existan).

**Implementación**: `<ArbolDocumentos>` en `components/erp/trazabilidad/`.
Recibe `(tipoDocumento, id)` y consulta el endpoint backend que devuelve
el árbol bidireccional.

### 10.11 P11 — Reporte últimas 100 compras del material

**Ruta**: `/compras/articulos/$id/historial-compras`

**Entry points**: desde detalle de un artículo, desde editor de línea
en P3 (botón "ver historial"), desde menú reportes.

**Layout**: tabla con columnas Proveedor, Documento (tipo + folio),
Fecha, Cantidad, Precio unitario, Descuento, Precio neto. Filtros:
proveedor (uno, varios, todos), fecha, cantidad mínima, tipo de
documento (OC, factura, recepción, devolución, nota de crédito).

**Default**: últimas 100 compras, ordenadas por fecha desc.

---

## 11. Patrones cross-pantalla

Heredados del §13 del 05 de RQ. Mismos componentes, mismas
convenciones. Recapitulados acá lo OC-específico:

### 11.1 Loading / Empty / Error

`<TableSkeleton>`, `<EmptyState>`, `<ErrorState>` heredados. Mensajes
específicos de OC en empty (e.g., "No tienes OCs en borrador. Crea una
nueva desde el botón Nueva OC.").

### 11.2 Domain terms con tooltips

Glosario extendido con términos OC: sub-estado, partida abierta,
consolidación, duplicar OC, contenedor, ruta, semana, pedimento,
cotización excepcionada, oc origen.

### 11.3 Print-friendly

`data-print="hidden"` en sub-topbar y aside list. P3 imprime
limpiamente (cabecera + líneas + totales + autorización + adjuntos
list). Los tabs colapsan para impresión: todas las secciones visibles
en flujo lineal.

### 11.4 Conflict resolution (heredado)

`<ConflictResolutionDialog>` en todas las mutaciones de OC.

### 11.5 Unsaved changes guard

`useUnsavedChangesGuard(isDirty)` en P4 Sheet y forms inline de P3.

---

## 12. Accesibilidad

Heredado de F3 del 05 de RQ (WCAG AA). Recapitular OC-específico:

- `<SubEstadosBar>`: contraste de colores + patrón visual + número
  visible (no solo color para distinguir sub-estados).
- `<DiasAtrasadosBadge>`: contraste + ícono adicional para color
  blind (🟢 ✓ / 🟡 ⚠ / 🔴 ⛔).
- `<AdjuntosManager>` drag-and-drop: fallback con botón "Subir archivo"
  para teclado-only.
- `<ArbolDocumentos>` con navegación por teclado (Tab entre nodos,
  Enter para abrir).

---

## 13. Testing strategy

Heredado del §16 del 05 de RQ (Vitest + MSW + Playwright).
Específico de OC:

- Tests unit por hook (mock MSW): `useOrdenesCompra`, `useOrdenCompra`,
  `useCrearOrdenCompraDesdeRequisicion`, etc.
- Tests integration por pantalla con MSW: P1 con filtros y paginación,
  P3 con tabs y acciones contextuales, P4 con los 3 modos.
- Tests de accesibilidad con `axe-core` automático en CI.
- Tests E2E con Playwright para los 4 flujos críticos:
  1. Crear OC desde RQ → autorizar N1 → autorizar N2 → PDF generado.
  2. Crear OC consolidada N:1 → transmitir → rechazo N1 → editar →
     re-transmitir → aprobar.
  3. Cancelar OC con recepciones parciales (doble firma).
  4. Cancelar + Duplicar OC → modificar líneas → autorizar.
- Tests parametrizados por celda de §6.1 (tabla maestra de acciones)
  para validar visibilidad/habilitación.

---

## 14. Brechas con backend (tickets a abrir)

Heredado patrón del §14 del 05 de RQ. Tickets a abrir contra el equipo
de backend antes/durante la implementación frontend:

### 14.1 [INCLUIDO en F7-PR3 backend] Endpoint de historial de eventos de OC

Para P3 tab "Historial", el frontend necesita
`GET /api/v1/compras/ordenes/{id}/historico` que devuelva todos los
eventos del agregado en orden cronológico (creación, transmisión,
autorizaciones, recepciones, facturas, pagos, cancelación, duplicación).

**Estado (Rev. 2 — 2026-05-11)**: ya **agregado al plan backend
(F7-PR3, query `ObtenerHistoricoOrdenCompraQuery`)** como parte de la
Rev. 4 del 02-plan. El modelo de auditoría ADR-0008 captura todo; la
query lee `core.audit_log` filtrado por entidad y agrupa eventos.
Como el backend está en desarrollo activo, no requiere ticket externo
— se entrega junto con F7-PR3.

### 14.2 [INCLUIDO en F7-PR3 backend] Endpoint para listar OCs hermanas duplicadas

Para el aside list de P3 que muestra "OCs hermanas" cuando la OC
fue duplicada, el frontend necesita:
`GET /api/v1/compras/ordenes/{id}/duplicadas`
→ lista de OCs con el mismo `oc_origen_id`.

**Estado**: ya **agregado al plan backend (F7-PR3, query
`ListarOcsHermanasDuplicadasQuery`)**. Filtro trivial con índice
`ix_oc_origen` ya existente.

### 14.3 [P1 resuelto] Permission gating de "Sin RQ previa" en captura

**Decisión cerrada** (FOC11): permiso especial separado
`compras.ordenes.crear.sin_rq` agregado al 01-diseño §9 (10 permisos
canónicos en total). El toggle en Sheet de Nueva OC solo aparece para
usuarios con este permiso (Dirección + Compradores designados).

### 14.4 [INCLUIDO en F7-PR3 backend] Endpoint de KPIs agregados de partidas abiertas

Para las KPI cards de P9 (FOC10 cerrado), el frontend necesita totales
agregados: monto total pendiente recibir, pendiente facturar, pendiente
pago, count de atrasadas.

**Estado**: ya **agregado al plan backend (F7-PR3, query
`ObtenerKpisPartidasAbiertasQuery`)**. Endpoint dedicado
`GET /api/v1/compras/ordenes/partidas-abiertas/kpis` → JSON con los 4
números agregados. Reactivo a los mismos filtros que F7-PR1.

### 14.5 [P2 → upgrade a P1, incluir en MVP] Search global contextual del topbar

**Decisión cerrada** (Rev. 2 — Round 3): incluir en MVP. El topbar
global ya existe; solo necesita reconocer ruta `/compras/ordenes/...` y
pasar `entityType=orden-compra` al backend. Backend ya tiene índices
(§10.3 del 01-diseño). ~1 día absorbido en UF-OC1.

---

## 15. Reutilización de código (resumen)

Tabla heredada del §14 del 01-diseño. Recapitular en perspectiva
frontend:

### 15.1 Frontend heredado tal cual

Shell (auth, sidebar, topbar, EmpresaSelector), cliente HTTP
enriquecido, permission codes (extender), UX kit (EmptyState,
ErrorState, TableSkeleton, Breadcrumbs, DomainTermTooltip,
useUnsavedChangesGuard, CollaborationIndicator, useCollaboration,
ConflictResolutionDialog), display components (MoneyDisplay,
DateTimeDisplay, EstadoBadge extender, NaturalezaBadge si aplica),
Sheet pattern, master-detail layout, inline forms con border dashed
(patrón del exemplar).

### 15.2 Frontend extender

`<EstadoBadge>` (+7 estados OC), `nav.ts` (+ item "Órdenes de
compra"), `permission-codes.ts` (+10 constantes), `glosario.ts` (+
términos OC), `bandeja-search-schema.ts` (variante para OC), MSW
handlers (extender con endpoints de OC).

### 15.3 Frontend nuevo

12 componentes nuevos (§9.1). De ellos, **2 se promueven a
`components/erp/`** desde el inicio: `<AdjuntosManager>` y
`<ArbolDocumentos>`. El resto vive en
`features/compras/ordenes/components/` hasta que aparezca un segundo
consumidor.

> **Hallazgo 2026-05-11**: `<ProveedorSelector>` ya existe cross-módulo
> en `components/erp/selectors/ProveedorSelector.tsx` (heredado de RQ
> + 8 selectores más). OC los reusa todos tal cual. Solo agrega
> `<HistorialComprasProveedor>` (componente complementario, no
> selector).

---

## 16. Glosario

Términos OC-específicos visibles via `<DomainTermTooltip>`:

| Término | Definición |
|---|---|
| **Sub-estado** | Dimensión independiente de progreso de una OC autorizada (recepción, facturación, pago). |
| **Partida abierta** | OC autorizada con algún sub-estado en Parcial o SinX. Vista crítica del negocio. |
| **Consolidación** | Práctica de agrupar varias RQs en una sola OC con el mismo proveedor y sucursal. |
| **Duplicar OC** | Acción que pre-llena una OC nueva desde una OC cancelada o rechazada (C4). |
| **Cotización excepcionada** | Cuando la OC se transmite sin cotización adjunta porque existe autorización por correo (bandera `CotizacionExcepcionada`). |
| **OC origen** | OC cancelada/rechazada de la que se duplicó la OC actual. Visible en cabecera. |
| **Contenedor / Ruta / Semana** | Campos estructurados de información de importación (reemplazan texto libre del SAP legacy). |
| **Pedimento** | Documento aduanero requerido en importaciones, capturable post-autorización al recibir. |
| **Incoterm** | Término de comercio internacional (FOB, CIF, etc.) aplicable a importaciones. |
| **Compromiso de RQ** | Estado de una RQ vinculada a una OC activa, removida del pool de disponibles. |

---

## 17. Cambios respecto a versiones previas

### Rev. 3 — verificación de selectores cross-módulo existentes (2026-05-11)

Auditado el repo `frontend/src/components/erp/selectors/`:

- **9 selectores cross-módulo ya existen** heredados de RQ:
  `ProveedorSelector`, `ArticuloSelector`, `AlmacenSelector`,
  `DepartamentoSelector`, `SucursalSelector`, `UsuarioSelector`,
  `CatalogoEagerCombobox` (+ 2 tests). OC los reusa **todos tal cual**.
- **4 selectores NO existen** y los crea OC alineado con F9-PR1
  backend (catálogos seed): `IncotermSelector`, `TransportistaSelector`,
  `CondicionesPagoSelector`, `RegimenFiscalSelector`.
- **`<ProveedorComboBox>` descartado** como componente nuevo: el
  `<ProveedorSelector>` existente cubre la captura. El "historial
  de últimas 100 compras al elegir" se separa en
  `<HistorialComprasProveedor>` como componente complementario que
  vive en `features/compras/ordenes/components/`.
- §9.1 del 05 actualizada: 12 componentes nuevos (era 12 incluyendo
  `<ProveedorComboBox>`); ahora son 11 nuevos + 9 reusados.
- §15.3 ajustada: 2 componentes promovidos cross-módulo desde el
  inicio (era 3).

### Rev. 2 — cierre de decisiones frontend (2026-05-11)

Tras 3 rondas de decisiones con owner, todas las asunciones FOC1–FOC16
cerradas + las 5 brechas backend (§14) confirmadas:

**Cambios derivados de FOC11 (permiso especial sin_rq):**
- §9 del 01-diseño extendido con 10º permiso `compras.ordenes.crear.sin_rq`.
- §2, §4.2, §15.2, Rev. 1 del 05 actualizadas: 9 → **10 permisos canónicos**.
- 02-plan §4 F0 lista actualizada con el 10º item.
- §14.3 marcada como resuelta.

**Cambios derivados de FOC16 (siempre abrir detalle):**
- §10.2 P2 reescrita: sin acciones inline; única acción por fila es
  "Ver detalle". Patrón conservador anti-firmas-sin-revisar.

**Cambios en §14 brechas:**
- §14.1, §14.2, §14.4 **ya incluidas en el plan backend** (F7-PR3 del
  02-plan Rev. 4 / 03-PR Rev. 4) ahora que el backend está en
  desarrollo activo. No se difieren a tickets posteriores.
- §14.3 resuelto por FOC11 (permiso `compras.ordenes.crear.sin_rq`
  agregado a F0-PR1).
- §14.5 upgrade P2 → P1, incluir en MVP (search contextual del topbar).

**Confirmados sin cambio (defaults aceptados):**
- FOC1 sub-estados como 3 barras horizontales segmentadas.
- FOC2+FOC6 promover `<AdjuntosManager>` y `<ArbolDocumentos>` a
  `components/erp/` desde el inicio.
- FOC3 Sheet único con 3 modos.
- FOC4 selector RQs modal multi-select.
- FOC5 PDF `<embed>` nativo.
- FOC7 ConfirmDuplicarDialog con preview.
- FOC8 tabs Logística/Importación/Financiera.
- FOC9 stepper visual N1→N2.
- FOC10 KPI cards arriba en partidas abiertas.
- FOC12 filtros en URL search params Zod.
- FOC13 EstadoBadge extender 7 estados.
- FOC14 sin real-time en bandeja.
- FOC15 NaturalezaBadge heredado en líneas con RQ origen.

### Rev. 1 — versión inicial (2026-05-11)

Primer corte del diseño de frontend para OC. Construido contra:
- 01-diseño Rev. 0.4 (C4 = cancelar + recrear; agregado
  `DuplicarOrdenCompraCommand`; 7 estados; 10 permisos canónicos).
- 02-plan Rev. 3, 03-PR Rev. 3, 04-cuidados Rev. 2.
- Patrón exemplar del 05 de RQ (Rev. 5) y `frontend/docs/patrones-compras.md`.

**Filosofía**: reuso máximo del shell común introducido por RQ.
Solo lo OC-específico se diseña desde cero (sub-estados, adjuntos,
importaciones, consolidación, duplicación, PDF, árbol de documentos).
Componentes con potencial cross-módulo se promueven a
`components/erp/` desde el inicio: `<AdjuntosManager>` y
`<ArbolDocumentos>` (los demás selectores cross-módulo ya existen
heredados de RQ).

11 pantallas (vs 9 de RQ; OC agrega P5 selector consolidación, P8
duplicar, P9 partidas abiertas, P10 árbol docs, P11 historial; consolida
algunos overlay vs rutas top).

16 asunciones de frontend específicas (FOC1–FOC16) además de las
heredadas F1–F14 del 05 de RQ.
