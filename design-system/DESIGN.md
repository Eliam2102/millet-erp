# Millet ERP — Design System (guía para agentes de IA)

> **Para quién es:** cualquier agente (Codex, Claude Code, Cursor, Lovable…) o persona que construya pantallas del ERP de Vidrios Millet.
> **Fuente de verdad visual:** el canvas "Millet ERP — Navegación" (12 pantallas). Si algo de esta guía y el canvas no coinciden, **manda el canvas** y se corrige esta guía.
> **Stack:** React + TypeScript + Tailwind + shadcn/ui. Íconos: `lucide-react`.
> **Archivos:** `tokens.json` (valores), `tokens.css` (variables), `tailwind.config.ts` (clases). Esta guía explica **cómo** usarlos.

---

## 0. Reglas duras (léelas antes de escribir código)

**DEBES**

1. Usar solo colores, tamaños, radios y sombras de `tokens.json` / clases de `tailwind.config.ts`. Nada de hex sueltos ni de la paleta default de Tailwind (`blue-600`, `gray-500`, `slate-*`…).
2. Usar **IBM Plex Sans** para la UI e **IBM Plex Mono** para folios, UUID, RFC y atajos de teclado.
3. Usar **13px** como tamaño base (`text-sm`). Nunca subir el cuerpo de una pantalla de escritorio a 14–16px.
4. Construir cada pantalla dentro del **App Shell** (sección 3): rail oscuro + (panel de módulo) + topbar + contenido.
5. Empezar cada listado desde la **plantilla de Listado** (sección 5.1) y cada registro desde la de **Detalle** (5.2). No inventes layouts nuevos si uno existente aplica.
6. Mostrar montos con `tabular-nums`, alineados a la derecha, en formato `$412,875.40`.
7. Expresar estados con **Badge de estado** (sección 4.6) y su color semántico. Un estado = un color, siempre el mismo en todo el ERP.
8. Escribir toda la UI en **español de México**, con mayúscula solo al inicio (sentence case).
9. Usar elementos reales: `<button>`, `<a href>`, `<input>` con `<label>`. Ponle `aria-label` a todo botón que solo tenga ícono.

**NO DEBES**

- Usar gradientes, glassmorphism, sombras grandes en tarjetas ni bordes laterales de color en tarjetas (el único borde lateral permitido es la selección de la lista maestra, 3px azul).
- Usar emoji, ilustraciones ni logos (el producto no lleva logo en el rail).
- Usar fuentes Inter, Roboto ni Arial.
- Usar rojo/verde como único diferenciador: el estado siempre lleva texto.
- Inventar datos: si no se conoce un valor, usa un placeholder entre corchetes: `[SUCURSAL]`, `[BANCO] ·· [0000]`, `[RESPONSABLE]`.
- Crear un color "parecido" a uno existente. Si de verdad hace falta un color nuevo, se agrega primero a `tokens.json` y a esta guía.

---

## 1. Principios

1. **Denso, pero legible.** Es una herramienta de trabajo diario: mucha información por pantalla, texto de 13px, filas de 48px.
2. **Lo urgente primero.** Las bandejas y los KPIs se ordenan por prioridad (sección 7). El color se reserva para lo que pide acción.
3. **Una sola forma de hacer cada cosa.** Mismo listado, mismos filtros y mismos badges en todos los módulos. Un usuario de CxP debe sentirse en casa en Almacén.
4. **Se navega por teclado.** ⌘K abre "Buscar o ir a", y las acciones frecuentes tienen atajo (`N F`, `G A R`).
5. **Sobrio.** Fondo gris muy claro, tarjetas blancas con borde de 1px y un solo color de marca (azul `#2748B8`).

---

## 2. Tokens (resumen; valores completos en `tokens.json`)

### 2.1 Color

| Rol | Token / clase TW | Hex | Uso |
|---|---|---|---|
| Fondo de app | `bg-surface-page` | `#F5F6F8` | `body`, search de topbar |
| Tarjeta | `bg-surface-card` | `#FFFFFF` | Tarjetas, tablas, topbar, panel de módulo |
| Fondo sutil | `bg-surface-subtle` | `#FAFBFC` | Encabezado de tabla, footer de diálogo, inputs de filtro |
| Fondo apagado | `bg-surface-muted` | `#F0F2F6` | Cuadro de ícono, tag de módulo |
| Seleccionado | `bg-surface-selected` | `#EEF2FC` | Ítem activo de menú / lista / paleta |
| Texto principal | `text-ink` | `#161B26` | Títulos, valores |
| Texto secundario | `text-ink-secondary` | `#414A5C` | Celdas secundarias, botón ghost |
| Texto de UI fuerte | `text-ink-strong` | `#2B3243` | Labels de formulario, ítems de menú |
| Texto apagado | `text-ink-muted` | `#667085` | Descripciones, fechas, headers de tabla, placeholder |
| Texto sutil | `text-ink-subtle` | `#8A93A5` | Chevrons, conteos inactivos |
| Borde | `border-line` / `shadow-card-flat` | `#E3E6EC` | Contorno de tarjetas y topbar |
| Divisor interno | `border-line-divider` | `#EEF0F4` | Entre tabs, filtros y encabezado dentro de una tarjeta |
| Línea de fila | `border-line-row` | `#F0F2F5` | Entre filas |
| Borde de control | `border-line-control` | `#D6DAE2` | Inputs, selects, botón secundario |
| Marca / primario | `bg-brand` `text-brand` | `#2748B8` | Botón primario, links, tab activa, foco |
| Primario hover | `brand-hover` | `#1B3590` | Hover; texto sobre fondos azules claros |
| Rail | `bg-rail` | `#131B2C` | Barra de módulos |

**Estados semánticos** (siempre en par fondo + texto; `solid` para puntos, barras e indicadores):

| Estado | bg | fg (texto) | solid | Ejemplos de uso |
|---|---|---|---|---|
| success | `#E5F4EC` | `#1E6B45` | `#1E8E5A` | Autorizada, Pagado, Timbrada, Registrada, Cerrada, En rango, Completa |
| warning | `#FFF4DB` | `#7A5000` | `#C98A00` | Por conciliar, Por autorizar (OC), Por programar, Pendiente, Factura pendiente, Cerca del límite, Atención |
| danger | `#FDECEC` | `#A12A2A` | `#C2362F` | Rechazada, Vencida, Excedido, Error de ingesta, Con diferencia, Crítico |
| info | `#EEF2FC` | `#1B3590` | `#2748B8` | Por autorizar (factura), Programado, Parcial, Autorizada (OC abierta), Layout enviado, Pago parcial |
| neutral | `#EEF0F4` | `#414A5C` | `#414A5C` | Capturada, Cancelada, Cancelación en proceso |

Aviso en línea (callout de advertencia): fondo `#FFF8E8`, texto `#5C3D00`, ícono de triángulo.

### 2.2 Tipografía

| Token | px / peso | Uso |
|---|---|---|
| `text-2xs` | 11 / 500–600 | Labels en MAYÚSCULAS (`tracking-[0.04em]` o `0.05em`), rail, tags, contadores |
| `text-xs` | 12 / 400–500 | Meta, descripciones, badges, celdas secundarias, paginación |
| `text-sm` | **13 / 400–500 (base)** | Cuerpo, celdas, botones, inputs, breadcrumb |
| `text-md` | 14 / 600 | Títulos de sección en Detalle; cuerpo en móvil |
| `text-lg` | 15 / 600 | Título de tarjeta o panel (h2), nombre de módulo |
| `text-xl` | 16 / 600 | Título de diálogo o sheet; input de ⌘K |
| `text-2xl` | 18 / 600 | Conteo en listas (bandeja) |
| `text-3xl` | 22 / 600, `tracking-[-0.01em]` | H1 de pantalla, valor de KPI |
| `text-4xl` | 24 / 600 | Solo el saludo de Inicio |

Pesos permitidos: 400, 500 y 600. No se usa 700.

### 2.3 Espaciado, radios y sombras

- **Espaciado:** base de 4px. Valores usados: 2, 4, 6, 8, 10, 12, 14, 16, 20, 24, 28, 32.
  - Gap entre tarjetas o secciones: 12–24.
  - Padding de tarjeta: 14–16 × 16–20.
  - Padding del área de contenido: `20px 24px` en módulos y `28px 32px` en Inicio.
- **Radios:**
  - `rounded-xs` 4: kbd.
  - `rounded-sm` 6: controles de 22–30px e ítems de menú.
  - `rounded-md` 8: controles de 32–36px.
  - `rounded-lg` 10: tarjetas y tablas.
  - `rounded-xl` 12: paneles de Inicio y tarjetas móviles.
  - `rounded-2xl` 14: diálogos.
  - `rounded-full`: badges, contadores, avatar.
- **Sombras:** las tarjetas usan `shadow-card` o `shadow-card-flat`, un *ring* de 1px y no un `border`. Diálogo: `shadow-dialog`. Sheet: `shadow-sheet`. Nada más.

### 2.4 Íconos

- Usa `lucide-react` con `strokeWidth={1.6}` (1.8 en íconos de 14–15px y 2 en chevrons de 12px). Siempre de trazo, nunca rellenos (excepción: el pin cuando está fijado).
- **Tamaños:**
  - 20: rail.
  - 18: topbar e íconos en cuadro de tarjeta de módulo.
  - 16: chevrons de fila y acciones.
  - 15: search.
  - 14: dentro de botones.
  - 12: chevrons de select.
- **Equivalencias usadas:**
  - Inicio `Home`, Compras `ShoppingCart`, Almacén `Package`, CxP `Receipt`.
  - Tesorería `Landmark`, CxC `Inbox`, Facturación `FileText`, Centros de costo `PieChart`.
  - Contabilidad `BookOpen`, Reportes `BarChart3`, Admin `SlidersHorizontal`.
  - Buscar `Search`, Notificaciones `Bell`, Sucursal `MapPin`, Nuevo `Plus`, Exportar `Download`.
  - Acciones de fila `MoreHorizontal`, Fijar `Pin`, Advertencia `TriangleAlert`.

---

## 3. App Shell (todas las pantallas de escritorio)

```
┌──────┬──────────────┬───────────────────────────────────────────────┐
│ Rail │ Panel módulo │ Topbar 56px                                    │
│ 76px │ 240px        ├───────────────────────────────────────────────┤
│ dark │ (opcional)   │ Contenido (padding 20/24)                      │
│      │              │                                               │
└──────┴──────────────┴───────────────────────────────────────────────┘
```

Diseño de referencia de 1440×900; mínimo soportado: 1280px de ancho. La página no hace scroll: el scroll vive dentro de la tabla o lista.

### 3.1 Rail de módulos

- Contenedor: `w-rail bg-rail flex flex-col items-center py-3 gap-1`.
- Ítem (`<a>`): `w-rail-item pt-2 pb-1.5 rounded-md flex flex-col items-center gap-1`, con ícono de 20 y label `text-2xs leading-tight`.
- **Estados:**
  - Activo: `bg-rail-active text-white`.
  - Normal: `text-rail-fg`.
  - Sin acceso o fase posterior: `text-rail-disabled`.
- Orden fijo: Inicio, Compras, Almacén, CxP, Tesorería, CxC, Facturación, C. Costo, Contab., Reportes. Admin va al fondo, separado por un `flex-grow`.
- **Sin logo.** El rail empieza directo con "Inicio".

### 3.2 Panel de módulo (240px, solo dentro de un módulo)

- Contenedor: `bg-surface-card border-r border-line`.
- Encabezado de 56px:
  - Arriba, "MÓDULO" (`text-2xs uppercase tracking-[0.04em] text-ink-muted`).
  - Debajo, el nombre del módulo (`text-lg font-semibold`).
  - A la derecha, el botón "Contraer panel" (28×28).
- Input "Filtrar menú": h-32, `rounded-[7px]`, `bg-surface-subtle border-line`.
- Secciones: **Operación**, **Reportes** y **Configuración** (o **Consulta** en módulos de consulta). Su título va en `text-2xs font-semibold uppercase tracking-[0.05em] text-ink-muted`, con chevron.
- **Ítem de menú:**
  - Base: `h-8 px-2.5 rounded-sm text-sm flex justify-between`.
  - Normal: `text-ink-strong`.
  - Activo: `bg-surface-selected text-brand font-medium`.
- **Contador del ítem:** `min-w-5 h-[18px] px-1.5 rounded-full text-2xs font-medium tabular-nums`, en neutral (`bg-neutral-bg text-ink-secondary`) o alerta (`bg-danger-bg text-danger-fg`).

### 3.3 Topbar (56px)

- Contenedor: `h-topbar bg-surface-card border-b border-line flex items-center gap-4 px-5`.
- Contenido, de izquierda a derecha:
  1. **Breadcrumb**: `Módulo / Sección / Pantalla`. Los niveles previos van en `text-ink-muted` (los que son navegables, como `<a>`) y el actual en `text-ink font-medium`. Separador `/`. En el Detalle, el folio actual va en mono 12px.
  2. Un spacer flexible.
  3. **Search** que abre ⌘K: w-340 (420 en Inicio) × h-34, `rounded-md border-line bg-surface-page text-ink-muted`. Lleva un ícono de 15, el texto "Buscar o ir a…" y `<kbd>⌘K</kbd>` (mono 11, `border-line-control bg-white rounded-xs px-[5px]`).
  4. Otro spacer flexible.
  5. **Selector de sucursal**: botón secundario h-34 con ícono `MapPin`, el texto "Millet" en `text-ink-muted`, el nombre de la sucursal en `font-medium` y un chevron.
  6. **Nuevo**: botón primario h-34 con ícono `Plus`.
  7. **Notificaciones**: icon-button de 34 con un punto `bg-notify` de 7px y halo blanco de 2px.
  8. **Avatar**: 32px, `rounded-full bg-brand-avatar text-brand text-xs font-semibold`, con iniciales.

### 3.4 Móvil (390px)

- Header de 56px con breadcrumb (`Todas las sucursales › Inicio`, la pantalla actual en negritas), sin logo. Notificaciones y avatar como botones de 44×44.
- Contenido con padding de 16, h1 de 17/600 y tarjetas `rounded-xl shadow-card-flat`. Filas de mínimo 64px; las filas de "Recientes" pueden ser de 52px.
- Tab bar inferior de 64px con 4 tabs (Inicio, Pendientes, Buscar, Módulos), ícono de 22 y label de 11. Tab activa en `text-brand font-medium`, inactiva en `text-ink-muted`.
- Todos los objetivos táctiles miden mínimo 44px.

---

## 4. Componentes (recetas exactas)

> Las clases son Tailwind con el config de Millet. Para cada caso, extiende el componente de shadcn correspondiente (`Button`, `Input`, `Badge`, `Tabs`, `Dialog`, `Sheet`, `Command`, `Table`) con **estas** variantes; no uses las variantes default de shadcn.

### 4.1 Botones

| Variante | Clases | Uso |
|---|---|---|
| primary | `h-ctl-lg px-3.5 rounded-md bg-brand text-white text-sm font-medium hover:bg-brand-hover` | Una por zona: "Registrar factura", "Nuevo", "Generar layout" |
| secondary | `h-ctl-lg px-3 rounded-md bg-white border border-line-control text-ink text-sm` | Exportar, Ver CFDI, Guardar borrador |
| secondary-danger | secondary + `text-danger-fg` | Rechazar |
| ghost | `h-ctl-lg px-3.5 rounded-md bg-transparent text-ink-secondary` | Cancelar, "Más filtros" |
| icon | `size-[34px] rounded-md bg-transparent text-ink-secondary` + `aria-label` | Notificaciones, ⋯ |
| sm | `h-ctl-sm px-2.5 rounded-sm border border-line text-xs text-ink-secondary` | Anterior/Siguiente, "Cambiar" |
| disabled (primary) | `bg-brand-disabled text-white` + `title` que explique por qué | "Autorizar" cuando falta recepción |

- En formularios y sheets los botones miden h-36 (`h-ctl-xl`).
- Los íconos dentro de un botón son de 14px, con `gap-1.5`.
- Un botón deshabilitado **siempre** explica el motivo (tooltip o texto cercano).

### 4.2 Inputs y formularios

- **Label:** `text-xs font-medium text-ink-strong`, arriba del campo, con `gap-1.5`. Para campos opcionales: `<span class="font-normal text-ink-muted">(opcional)</span>`.
- **Input / select:** `h-ctl-xl px-3 rounded-md border border-line-control bg-white text-sm text-ink`.
- **Textarea:** igual que el input, con `py-2`, `resize-none` y 2 filas.
- **Formulario en sheet:** se organiza en `<fieldset>` numerados ("1. Comprobante fiscal", "2. Vinculación", "3. Revisión previa"). Cada `legend` va en `text-sm font-semibold`. Los campos van en un grid de 2 columnas con `gap-x-4 gap-y-3.5`, y los campos largos con `col-span-2`.
- **Revisión previa (checks):** lista en tarjeta `shadow-card-flat rounded-lg`. Cada fila mide h-44: círculo de 20px (ok: `success-bg/fg`; advertencia: `warning-bg/fg`), el texto y, a la derecha, el detalle en `text-xs text-ink-muted`.

### 4.3 Tarjeta y tarjeta KPI

- **Tarjeta base:** `bg-white rounded-lg shadow-card` (o `shadow-card-flat` si va dentro de un layout denso).
- **KPI:**
  - Contenedor: `rounded-lg shadow-card px-4 py-3.5 flex flex-col gap-1.5`.
  - Label: `text-xs text-ink-muted`, precedido de un cuadrito de 8×8 `rounded-[2px]` del color del dato.
  - Valor: `text-3xl font-semibold tabular-nums`.
  - Subdato: `text-xs text-ink-muted`, en la misma línea base que el valor.
  - Toda la tarjeta es un link a su listado.
  - En un módulo van 4 KPIs: `grid grid-cols-4 gap-3`.
- **KPI con prioridad (Inicio):** se agregan
  - Tag "Crítico" (danger) o "Atención" (warning), de 20px de alto, `rounded-full text-2xs font-semibold`.
  - Botón pin de 28px: relleno en `text-brand` si está fijado; vacío en `text-ink-subtle` si no.
  - Una línea de motivo en `text-xs`, en el color del nivel (`font-medium`).
  - En crítico, el ring de la tarjeta cambia a `danger-ring`.
  - Ver la regla en la sección 7.

### 4.4 Tabla de listado

Estructura (todo dentro de una tarjeta `bg-white rounded-t-lg shadow-card-flat flex flex-col overflow-hidden` que crece hasta el fondo):

1. **Tabs de vista** (h-44, `border-b border-line-divider px-4 gap-5`):
   - Tab: `text-sm`. Activa: `text-ink font-medium shadow-tab-active`. Inactiva: `text-ink-muted`.
   - Cada tab lleva su conteo en `text-2xs tabular-nums` (`text-brand` si está activa, `text-ink-subtle` si no).
   - Al final, el botón ghost "+ Guardar vista".
2. **Barra de filtros** (`px-4 py-2.5 border-b border-line-divider gap-2`):
   - Búsqueda: 260×30, `rounded-[7px] border-line`, con placeholder que diga qué se puede buscar ("Folio, UUID, proveedor u OC").
   - Chip para agregar filtro: `h-ctl-md px-2.5 rounded-[7px] border border-dashed border-line-dashed text-xs text-ink-secondary`, con texto "+ Proveedor".
   - Chip de filtro activo: `bg-brand-soft border-brand-soft-border text-brand-hover`, con texto "Fecha: septiembre 2026 ×".
   - Botón ghost "Más filtros".
   - Spacer flexible.
   - Total en `text-xs text-ink-muted` ("214 facturas").
   - Botón de columnas de 30px.
3. **Header de tabla:** h-36, `bg-surface-subtle border-b border-line-divider px-3`, en `text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted`.
4. **Filas:**
   - Cómoda: `flex: 1 0 44px` (se estiran para llenar el alto y no dejan hueco), referencia de 48px. Compacta: h-36.
   - `px-3 border-b border-line-row text-sm`.
   - Fila resaltada (la que se abrió): `bg-surface-row-focus`.
5. **Footer:** h-44, `border-t border-line-divider px-4 text-xs text-ink-muted`, con "1–10 de 214" a la izquierda y los botones sm "Anterior"/"Siguiente" a la derecha.

**Reglas de columnas:**

- Usa CSS grid con anchos fijos. La columna principal (proveedor o cliente) es la única flexible: `minmax(0, 1.6fr)`.
- Orden típico: `[checkbox 36] Folio · Entidad (+ subtexto) · Referencias · Fecha(s) · Importe · Indicador · Estado · [⋯ 36]`.
- Folio: mono 12/500. Si abre el detalle, es un `<a>` en `text-brand`.
- Entidad: nombre en `text-sm` con ellipsis. Debajo, el subtexto (RFC en mono 11, departamento o zona) en `text-2xs`/`text-xs text-ink-muted`.
- Fechas en formato corto ("21 sep"), en `text-ink-secondary`. Si está vencida o es urgente: `text-danger-fg font-medium`.
- Importe: alineado a la derecha, `pr-4 tabular-nums font-medium`.
- Estado: Badge (4.6).
- Acciones: icon-button ⋯ de 28px con `aria-label="Acciones de {folio}"`.
- Cada listado muestra **10 filas** por página.

### 4.5 Indicadores dentro de tabla

- **Conciliación (CxP):** columna "Conciliación" de 150px, con un punto de 7px y una frase. No uses siglas ni puntos sin texto. Las frases posibles son:
  - `Completa` (success, peso normal).
  - `Falta recepción` (warning).
  - `Sin orden de compra` (warning).
  - `Precio difiere de la OC` (danger).

  El tooltip detalla las tres partes: "Orden de compra: ok · Recepción: pendiente · Factura vs OC: ok".
- **Avance (Compras, Centros de costo):**
  - Barras de 6px de alto con `rounded-[3px]`, fondo `#E3E6EC` y relleno sólido. Es el único lugar donde se usa `linear-gradient`, y solo como corte duro de relleno.
  - En Compras van 3 barras de 26px (Recepción · Facturación · Pago); el relleno es `info` y pasa a `success` al llegar a 100%.
  - En Centros de costo va 1 barra de 96px más el % en `text-xs`. Color según el avance: `info` por debajo de 90%, `warning` de 90 a 100% y `danger` arriba de 100%.

### 4.6 Badge de estado y tag de módulo

- **Badge de estado:** `inline-flex items-center h-ctl-xs px-2 rounded-full text-xs font-medium whitespace-nowrap`, con `bg-{estado}-bg text-{estado}-fg`. El texto es el nombre exacto del estado (tabla 2.1).
- **Tag de módulo** (bandeja de Inicio): `h-ctl-xs px-2 rounded-sm text-2xs font-medium bg-surface-muted text-ink-strong`. No lleva color semántico.
- **Contador:** ver el panel de módulo (3.2).

### 4.7 Diálogo "Buscar o ir a" (⌘K)

- **Contenedor:**
  - Scrim: `bg-[var(--mt-scrim-dialog)]`.
  - Diálogo: 640×520, `rounded-2xl shadow-dialog`, a 120px del borde superior y centrado.
- **Input:** h-56, `text-xl`, con ícono `Search` y `<kbd>Esc</kbd>`.
- **Grupos:** cada uno con su título en `text-2xs font-semibold uppercase tracking-[0.05em] text-ink-muted`.
  - **Ítem:** h-48 (44 en la vista vacía) y `rounded-md`, con un cuadro de ícono de 28 (`bg-surface-muted rounded-[7px]`), título en `font-medium` y ruta en `text-xs text-ink-muted`. Atajo o fecha a la derecha, en mono 12.
  - **Ítem activo:** `bg-surface-selected`.
- **Footer:** h-40, `bg-surface-subtle`, con las ayudas `↑↓ navegar`, `↵ abrir`, `⌘↵ abrir en pestaña nueva` y `Escribe > para acciones`.
- **Con texto escrito:** grupos "Ir a", "Acciones" y "Registros".
- **Sin texto:**
  - "Sugeridos para ti": 3 ítems con conteo y el motivo de la sugerencia.
  - "Accesos directos": 4 tarjetas de 64px en `grid-cols-4`, con ícono, atajo y label, más un link "Editar".
  - "Recientes": 2 folios.

### 4.8 Sheet lateral (crear o registrar)

- **Contenedor:** 560px a la derecha, a todo el alto, con `shadow-sheet` y scrim `--mt-scrim-sheet`.
- **Header** (h-64): título `text-xl font-semibold` y, debajo, una línea de contexto en `text-xs text-ink-muted`. Botón cerrar de 32px.
- **Footer** (h-68, `border-t`): `Cancelar` (ghost) a la izquierda; a la derecha `Guardar borrador` (secondary) y la acción primaria.

### 4.9 Lista maestra + detalle

- **Lista maestra** (320px, `bg-white border-r border-line`):
  - Arriba, un selector de vista ("Por conciliar · 18") y un filtro.
  - **Ítem:** padding `11px 14px`, con folio en mono y el importe a la derecha; debajo, la entidad y la fecha.
  - **Ítem seleccionado:** `bg-surface-selected shadow-list-selected`.

### 4.10 Avisos en línea y actividad

- **Callout de advertencia:** `role="note"`, `bg-warning-note-bg text-warning-note-fg rounded-md px-3 py-2.5`, con ícono `TriangleAlert` de 16. El texto dice **qué falta y cómo se destraba**.
- **Pasos** (conciliación de tres vías en el Detalle): 3 columnas. Cada paso tiene un círculo de 24px (ok: `success-bg/fg` con check; pendiente: `warning-bg/fg` con "!"), un título en `font-medium` y el detalle en `text-xs text-ink-muted`.
- **Actividad:** filas con un punto de 8px `#B5BCC9`, texto `text-ink-strong` y tiempo relativo a la derecha. Ordenadas de la más reciente a la más antigua.

---

## 5. Plantillas de pantalla

Usa siempre una de estas cinco. Las referencias son artboards del canvas.

### 5.1 Listado de módulo (CxP Facturas, Almacén Recepciones, Compras OCs, Tesorería Pagos, CxC Cartera, Facturación Comprobantes, Centros de costo)

```
Shell (rail + panel de módulo + topbar)
└ main  p-[20px_24px_0] flex-col gap-4
  ├ Header de página: H1 (22/600) + descripción 13 muted (1 línea: qué es y qué se hace aquí)
  │                   └ derecha: [Exportar] secondary + [Acción primaria]
  ├ KPIs: 4 tarjetas KPI (grid-cols-4 gap-3), cada una filtra el listado
  └ Tarjeta de tabla (crece hasta el fondo, rounded-t-lg): Tabs → Filtros → Header → 10 filas → Footer
```

- Las tabs de vista corresponden a los estados del flujo, en el orden del proceso, más "Todas".
- Los KPIs muestran lo accionable (por autorizar, rechazadas, vencen esta semana) y no totales decorativos.

### 5.2 Detalle de registro (Factura FE-3391)

```
Shell (rail + topbar con breadcrumb que termina en el folio)
└ [Lista maestra 320] | main p-[24px_28px] flex-col gap-5
                         ├ Encabezado: Folio (mono 22/600) + Badge · entidad + RFC  | Acciones
                         ├ <dl> de datos clave: grid-cols-4 en tarjeta (dt 12 muted, dd 13/500)
                         ├ Sección de validación (pasos + callout)
                         ├ Sección de líneas (tabla con totales: Subtotal · IVA · Total)
                         └ Actividad (crece hasta el fondo)
```

La acción primaria va deshabilitada mientras haya bloqueos, con un `title` que diga el motivo.

### 5.3 Inicio (bandeja cross-módulo)

```
main p-[28px_32px] grid cols [1.55fr 1fr] rows [auto auto 1fr] gap-6
├ Fecha (12 muted) + "Buenos días, [NOMBRE]" (24/600)        | Rol: [ROL]
├ Indicadores (col-span-2): encabezado con regla + "Personalizar" · 4 KPI con prioridad
├ "Requiere tu acción" (tarjeta rounded-xl): filas [tag módulo 104 | título+detalle | conteo 18/600 | antigüedad | chevron]
└ Columna: "Recientes" (folio mono azul · descripción · cuándo)  +  "Módulos" (grid 2 col de tarjetas con ícono en cuadro)
```

En la bandeja, una antigüedad urgente va en `text-danger-fg font-medium`.

### 5.4 Overlay sobre pantalla (⌘K, Registrar)

El diálogo o sheet se pinta encima de la pantalla del módulo (que queda debajo del scrim). No se navega a otra página.

### 5.5 Móvil

Ver 3.4. Solo es para consultar y aprobar; la captura pesada es de escritorio.

---

## 6. Contenido y formatos

- **Idioma:** español (México). Sentence case en todo: "Registrar factura", no "Registrar Factura".
- **Verbos en botones:** infinitivo y específico: Registrar factura, Generar layout, Aplicar pago, Autorizar, Rechazar. Evita "Aceptar", "OK" y "Enviar" genéricos.
- **Moneda:**
  - En tablas y detalle: `$412,875.40`. Agrega "MXN" solo en el detalle.
  - Resumida en KPIs: `$642.9 k`, `$1.24 M`, con un decimal y espacio antes de k/M.
- **Fechas:**
  - En listas: `21 sep` (mes en 3 letras y minúscula).
  - Completa: `21 sep 2026`.
  - Relativas: `hace 12 min`, `hace 4 h`, `hace 2 d`, `ayer`, `hoy`.
  - En formularios: `29/09/2026`.
- **Porcentajes:** `82 %` (con espacio), `+0.4 %`.
- **Folios** en mono, tal como vienen del sistema: `OC-2026-0412`, `REC-2026-0931`, `FE-3391`, `CB-0931`.
- **Abreviaturas aceptadas:**
  - Módulos: CxP, CxC, OC, CFDI, REPP, UUID, RFC, A+W.
  - En el rail: "C. Costo", "Contab.".
- **Descripciones de pantalla:** una oración que diga qué hay y qué se hace ("Captura, conciliación contra OC y recepción, y autorización para pago.").
- **Mensajes de bloqueo:** di qué falta y qué lo destraba ("La línea 3 no tiene recepción en Almacén. La factura podrá autorizarse cuando se registre la recepción o se corrija la OC.").
- **Placeholders** para datos desconocidos: `[MAYÚSCULAS ENTRE CORCHETES]`. Nunca uses lorem ipsum.

---

## 7. Reglas de comportamiento

### 7.1 Prioridad (KPIs de Inicio y bandeja "Requiere tu acción")

- **Puntaje = impacto × urgencia × bloqueo.**
  - Impacto: el monto contra el umbral del indicador.
  - Urgencia: días para vencer o días de atraso.
  - Bloqueo: si detiene a otra área.
- **Niveles:**
  - **Crítico:** pasa el umbral crítico. Tag y ring en danger; va primero.
  - **Atención:** tag warning.
  - **Normal:** sin tag.
- Inicio muestra 4 KPIs. El usuario fija hasta 2 (pin relleno) y el sistema llena el resto por puntaje. Los fijados van al final y no se mueven.
- Cada KPI con prioridad muestra una línea de **motivo** ("9 facturas con más de 60 días", "2 vencen mañana").
- Qué KPIs son elegibles depende del rol del usuario.

### 7.2 Estados por entidad (texto exacto del badge)

| Entidad | Estados en orden de flujo |
|---|---|
| Factura de proveedor (CxP) | Capturada → Por conciliar → Por autorizar → Autorizada · Rechazada |
| Orden de compra | Por autorizar → Autorizada → Cerrada · Cancelada |
| Recepción | Parcial → Factura pendiente → Registrada · Con diferencia |
| Pago (Tesorería) | Por programar → Programado → Layout enviado → Pagado · Rechazado por banco |
| Factura de cliente (CxC) | Vigente → Por vencer → Vencida · Pago parcial |
| Comprobante (Facturación) | Timbrada · REPP pendiente · Error de ingesta · Cancelación en proceso |
| Centro de costo | En rango · Cerca del límite (≥90 %) · Excedido (>100 %) |

### 7.3 Navegación

- Rail = módulo. Panel de módulo = pantallas del módulo. Breadcrumb = ubicación actual.
- Las tarjetas de KPI y las filas de la bandeja llevan al listado ya filtrado.
- El folio lleva al detalle. ⌘K lleva a cualquier pantalla, acción o registro.
- **Atajos:** `⌘K` buscar; `G` + letras para ir a (`G A R` = Almacén › Recepciones); `N` + letra para nuevo (`N F` = Registrar factura).

---

## 8. Accesibilidad

- Contraste mínimo 4.5:1 para texto. `#667085` sobre blanco cumple; no uses textos más claros que `text-ink-subtle` para información.
- El estado nunca se comunica solo con color: siempre lleva texto o tooltip.
- `aria-current="page"` va en el ítem activo del breadcrumb y del menú. `aria-selected` en las tabs. `role="dialog"` con `aria-label` en ⌘K y el sheet.
- Foco visible: `outline 2px` en `--mt-primary` con offset de 2px. No quites el outline.
- En móvil, los objetivos táctiles miden mínimo 44px.

---

## 9. Checklist antes de entregar una pantalla

- [ ] Está dentro del App Shell, con el ítem correcto activo en el rail y en el panel de módulo.
- [ ] El breadcrumb refleja la ubicación.
- [ ] Usa una plantilla de la sección 5 y no tiene layouts inventados.
- [ ] No hay hex, tamaños ni radios fuera de los tokens (`grep -E "#[0-9A-Fa-f]{6}"` en el componente debe dar 0).
- [ ] La tipografía es Plex Sans 13 base; folios, UUID y RFC van en Plex Mono.
- [ ] Montos con `tabular-nums`, alineados a la derecha y en formato `$0,000.00`.
- [ ] Los estados usan los badges y textos exactos de 7.2.
- [ ] La tabla muestra 10 filas, se estira al alto disponible y no deja hueco vacío.
- [ ] Solo hay una acción primaria por zona, y los botones deshabilitados explican el motivo.
- [ ] Los botones de ícono tienen `aria-label`, los inputs tienen `<label>` y no hay `onClick` en `div`.
- [ ] Los textos están en español (es-MX), en sentence case, y no hay datos inventados.

---

## 10. Cómo conectarlo a tu agente

Agrega esto a `AGENTS.md` (Codex) y/o `CLAUDE.md` (Claude Code) en la raíz del repo:

```md
## UI / Design system
- Antes de crear o modificar cualquier pantalla o componente, lee `design-system/DESIGN.md` completo.
- Usa solo los tokens de `design-system/tokens.json` vía las clases de `tailwind.config.ts`; nunca hex sueltos ni la paleta default de Tailwind.
- Toda pantalla nueva parte de una plantilla de la sección 5 y pasa el checklist de la sección 9.
- Si necesitas algo que no existe en el design system, detente y propón el cambio a DESIGN.md en vez de improvisarlo.
```

Estructura sugerida en el repo:

```
design-system/
  DESIGN.md          ← esta guía
  tokens.json        ← valores
  tokens.css         ← importar en src/index.css
  tailwind.config.ts ← copiar/mergear a la raíz
src/components/ui/   ← componentes shadcn con las variantes de la sección 4
```
