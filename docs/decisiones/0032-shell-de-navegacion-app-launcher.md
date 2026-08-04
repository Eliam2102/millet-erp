# ADR-0032: Shell de navegación con App Launcher modal

- **Estado**: Aceptada
- **Fecha**: 2026-05-09
- **Decisores**: Eduardo Paredes (owner)
- **Etiquetas**: frontend, ux, shell, cross-module

## Contexto y problema

El back-office del ERP cubre **10 módulos** (Identidad, Facturación, CxC,
Compras, Almacén, CxP, Activos Fijos, Contabilidad, Reportes/BI, más
módulos transversales). Cada módulo tiene **3-5 pantallas operativas**
(P1 bandeja, P3 detalle, P4 nueva, etc.) más **administración** propia
(catálogos, matrices de aprobación, parámetros).

Un sidebar tradicional con sub-items expandibles no escala:

- 10 módulos × 3-5 sub-items = 30-50 entradas siempre visibles
- Mezcla "operación frecuente" con "configuración rara"
- En v1 solo Compras Requisiciones está implementado, pero el shell
  vive todo el ciclo de vida del ERP — diseñarlo bien una sola vez
  evita reescribirlo en cada UF
- El cliente espera un look "app launcher" tipo Microsoft 365 / Google
  Workspace

Sin esta decisión, cada módulo decide su propia estructura de nav y
terminamos con inconsistencia visual entre Compras, CxP, Contabilidad,
etc.

## Drivers de la decisión

- **Escalabilidad** — el patrón debe seguir siendo legible cuando los
  10 módulos estén implementados
- **Descubribilidad** — el usuario debe poder explorar pantallas de un
  módulo sin haberlas visitado antes
- **Consistencia cross-módulo** — UX idéntica en Compras, Almacén, CxP,
  etc.
- **Permisos** — pantallas que el usuario no puede usar no deben
  aparecer (gateadas por permiso)
- **Velocidad de desarrollo** — el shell se construye una vez; cada
  módulo nuevo solo registra sus secciones y cards

## Opciones consideradas

1. **Sidebar plano + sub-items expandibles** (status quo en v1)
2. **Sidebar plano + modal "App Launcher" al click en módulo** (decisión)
3. **Sidebar plano + dropdown menu al hover/click**
4. **Top-nav con tabs por módulo + sidebar contextual del módulo**
5. **Comando `Cmd+K` + sidebar mínima**

## Decisión

**Opción 2: Sidebar plano + modal de App Launcher por módulo.**

- **Sidebar:** un item por módulo (Compras, CxC, OC, Almacén, ...).
  Click en módulo → abre modal del módulo correspondiente.
- **Modal:** centrado en el área de trabajo, **al lado derecho del
  sidebar** (no sobre el sidebar). Estándar `<Dialog>` de shadcn.
- **Cards (medium-density):** icono lucide + título 1 línea +
  descripción 1-2 líneas + footer opcional (badge/contador). Toda la
  card es clickable; gateada por permiso (no aparece sin él).
- **Grid:** 2 columnas en desktop (≥1024px), 1 columna en mobile.
- **Secciones dentro del modal:** `Operación` / `Configuración` /
  (futuras: una sección por submódulo cuando lleguen OC, Recepciones,
  etc.).
- **Cierre:** ESC + click fuera + auto-close al elegir card + botón
  `[X]` arriba.

## Reglas operativas

Documentadas para los 10 módulos del back-office:

### R1 — Módulos `disabled: true` (no implementados todavía)

El item del sidebar se renderiza pero **no es clickable** (no abre
modal). Esto da awareness al usuario del scope total del ERP sin
generar UX ruidosa con modales vacíos. Cuando el módulo se implementa,
basta cambiar `disabled: false` y registrar sus secciones/cards.

### R2 — Sección "Configuración" sin cards

Si un módulo no tiene cards de configuración todavía (típico en v1 de
cada módulo), la sección **se omite** del modal. No se muestra
"Próximamente" ni placeholder. Se agrega cuando exista al menos una
card real (ej. en Compras, cuando llegue P9 Aprobadores).

### R3 — Permisos

Cada card declara un `permission` (any-of de uno) o `permissionsAny`
(any-of de varios — útil para pantallas multi-permiso como Pendientes
de autorización). La card **no aparece** si el usuario no califica.
Si todas las cards de una sección están filtradas por permisos, la
sección se omite. Si todas las cards del modal están filtradas, el
modal renderiza un mensaje neutro ("Sin pantallas disponibles para
tu rol").

El gate del frontend es UX-only — el backend sigue siendo la barrera
real de seguridad.

### R4 — Click en sidebar = abrir modal siempre

No "última pantalla visitada". Predecible y simple. Si en el futuro
el cliente pide atajo a la pantalla principal del módulo, agregamos
chevron pequeño en el item del sidebar — no cambiamos el
comportamiento del item principal.

### R5 — Sin contadores reales en v1

Los cards pueden tener footer con badge/contador (ej. "12 pendientes
N1"), pero requieren endpoint backend `GET /<modulo>/summary` que no
existe. Iniciar **sin contadores**; abrir ticket backend cuando el
cliente los pida. Mantener el slot en el componente Card para
agregarlos sin refactor.

### R6 — Sin Cmd+K ni atajos por módulo en v1

Atajos de teclado (`Cmd+K` global de búsqueda, `Cmd+1..9` por módulo)
**diferidos a UF7-PR3 polish**. No entran al primer corte del
launcher.

### R7 — Sin card "Resumen/Dashboard" del módulo en v1

Cuando exista landing con KPIs por módulo, se agrega una card
adicional arriba de la sección "Operación" llamada "Dashboard". Por
ahora no.

### R8 — Una sección a la vez con tabs verticales (2026-07-17)

Con CxP el modal llegó a 14 cards (9 Operación + 3 Reportes + 2
Configuración) y se desbordaba de la pantalla; Tesorería viene igual
de cargada. El modal ya **no apila todas las secciones**: renderiza
**una sección a la vez** y las demás se eligen con **tabs verticales
tipo "separador de libro"** pegados al borde derecho del modal
(texto en `writing-mode: vertical-rl`; el tab activo sobresale un poco
más, como separador jalado). Reglas:

- El grid se mantiene a **2 columnas** y el ancho en `max-w-2xl`.
- Con tabs, el panel de cards es de **altura fija** (no `max-h`): el
  modal está centrado con `translate-y -50%`, así que si la altura
  dependiera de la sección activa, cambiar de tab movería el modal y
  los tabs quedarían en otro lugar bajo el cursor. El scroll interno
  del panel cubre la sección que no cabe.
- En pantallas `<md` los tabs caen a **pills horizontales** arriba del
  grid (el borde derecho del modal queda fuera del viewport).
- Módulos con **una sola sección visible** no muestran tabs — se
  renderiza la sección con su heading, como antes.
- El tab activo por default es la **primera sección visible**
  (normalmente Operación) y se resetea al cambiar de módulo.
- R3 aplica antes que los tabs: una sección filtrada completa por
  permisos tampoco genera tab.

## Consecuencias

**Positivas**

- **Consistencia cross-módulo automática**. Todos los módulos heredan
  el mismo shell — un módulo nuevo solo registra `{ moduloId, label,
  icon, secciones: [...] }` y todo funciona.
- **Sidebar limpio** incluso con 10 módulos × varias pantallas — solo
  10 items.
- **Descubribilidad mejorada**: el usuario abre el modal y ve qué
  pantallas existen sin tener que adivinar.
- **Configuración separada visualmente** de operación dentro de cada
  módulo — el usuario rara vez entra a config, no la quiere mezclada
  con su trabajo diario.

**Negativas**

- **Un click extra** para llegar a una pantalla específica vs. el
  sidebar expandible (operación → modal → card). Mitigado en UF7-PR3
  con atajos de teclado.
- **El usuario debe recordar** qué hay en cada módulo (no está visible
  todo el tiempo). Mitigado: nombres claros + descripciones cortas en
  cards.
- **Estructura de `nav.ts` se vuelve más compleja**: array plano →
  estructura jerárquica `{ moduloId, secciones: [{label, cards: [...]}] }`.
  Migración de un solo PR (UF3-PR2).

## Descartadas

- **(1) Sidebar expandible plano (status quo)**: no escala a 10 módulos.
  Status quo en v1 solo porque hasta UF3-PR1 había 1-2 entradas. Se
  reemplaza acá.
- **(3) Dropdown menu**: hover-dropdown es problemático en mobile y
  con touch; click-dropdown se siente menos "espacioso" que el modal
  para mostrar descripciones de pantallas.
- **(4) Top-nav + sidebar contextual**: rompe el patrón mental de
  "sidebar = navegación primaria"; agrega área del header que en
  desktop es valiosa. Adicional, no se gana nada con el sidebar
  contextual cuando ya tenemos breadcrumbs.
- **(5) Cmd+K only**: pierde descubribilidad para usuarios que no
  conocen el universo de pantallas. Cmd+K se agrega *adicional* al
  launcher (UF7-PR3), no como reemplazo.

## Notas de implementación

### Estructura de `nav.ts`

```ts
export type NavCard = {
  label: string;
  description: string;
  to: string;
  icon: LucideIcon;
  permission?: string;
  permissionsAny?: readonly string[];
};

export type NavSeccion = {
  label: 'Operación' | 'Configuración' | string;
  cards: readonly NavCard[];
};

export type NavModulo = {
  moduloId: string;
  label: string;
  icon: LucideIcon;
  disabled?: boolean;
  /** Si está vacío, el modal renderiza mensaje neutro. */
  secciones: readonly NavSeccion[];
};

export const navModulos: readonly NavModulo[] = [...]
```

### Componentes

- `components/layout/Sidebar.tsx` — items planos, click abre modal.
- `components/layout/AppLauncherModal.tsx` — modal de cards. Es
  shell-level (no per-módulo), recibe `modulo: NavModulo` y renderiza.
- `components/layout/AppLauncherCard.tsx` — card individual. Reusa
  `<Card>` de shadcn como base; agrega icon, descripción, footer slot.

### Migración

Implementación inicial del patrón en **UF3-PR2** (rama
`compras/uf3-app-launcher`). Aplica:

1. Refactor de `nav.ts` plano → jerárquico.
2. `Sidebar.tsx` muestra solo módulos; click abre modal.
3. Compras estrena el patrón con sus cards (P1, P2; P9 cuando llegue).
4. Otros 9 módulos quedan `disabled: true` con item en sidebar pero
   sin modal.

### Reglas para módulos futuros

Cuando un módulo se va a implementar (CxC, Almacén, CxP, etc.):

1. Cambiar `disabled: false` en su entrada de `navModulos`.
2. Definir `secciones`: arrancar con solo `Operación` y sus pantallas
   principales.
3. Cuando lleguen pantallas de configuración, agregar sección
   `Configuración`.
4. Si el módulo tiene submódulos (ej. Compras → Requisiciones, OC,
   Recepciones), cada submódulo es una sección dentro del modal de
   Compras (no un módulo aparte en el sidebar). Decisión revisable
   cuando lleguen OC y Recepciones.
5. Cards declaran su permiso. Sin permiso = no aparecen.
