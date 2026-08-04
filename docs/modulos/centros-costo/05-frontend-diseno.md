# Diseño de frontend — Módulo Centros de Costo

> **Versión:** 0.4.1 · **Fecha:** 2026-07-17
> **Basado en:** [`01-diseno.md`](01-diseno.md) v0.5 y
> [`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md).

---

## 0. Cómo leer

**La UI es contextual y el API habla Dim** (diseño §4): el backend nunca
hornea etiquetas; cada pantalla traduce. Regla transversal: nunca un GUID
de cara al usuario.

| Contexto | Dim1 | GrupoDim2 | Dim2 | GrupoDim3 | Dim3 |
|---|---|---|---|---|---|
| **Configuración** (Módulo 1) | "Dimensión 1" | "Grupo dimensión 2" | "Dimensión 2" | "Grupo dimensión 3" | "Dimensión 3" |
| **Asignación** (Módulo 2) | ídem configuración | ídem | ídem | ídem | ídem |
| **Documentos** (RQ/OC/salida) | (heredada) | — | (heredada) | — | **"Máquina"** |

En configuración/asignación NO se usan las palabras
Sucursal/Departamento/Máquina.

---

## 1. Posicionamiento

**Dos entradas** en el shell, ambas con NavCard (nav + landing) desde el
día 1:

- **Módulo 1 — "Centros de Costo"** (configuración): árbol de 3 niveles +
  CRUD con modales. Hipervínculo al Módulo 2.
- **Módulo 2 — "Asignación de Centros de Costo"**: árbol de 5 niveles con
  tri-estado; entrada PROPIA de menú.

Permisos: Módulo 1 con `catalogo.leer`/`catalogo.administrar`; Módulo 2 con
`asignaciones.administrar`; `dim3.leer-todos` = badge "alcance total".

## 2. Rutas (TanStack Router)

| Ruta | Patrón | Contenido |
|---|---|---|
| `/centros-costo` | landing | NavCard del Módulo 1 |
| `/centros-costo/configuracion` | específica (§4.1) | Árbol de 3 niveles + CRUD con modales |
| `/centros-costo/asignaciones` | específica (§4.2) | Módulo 2: árbol de 5 niveles con tri-estado por usuario |

## 3. Los DOS árboles (misma data, dos formas)

- **Árbol de configuración (3 niveles)**: Dim1 → Dim2 → Dim3; los grupos
  son un **chip** del renglón (GrupoDim2 en Dim2, GrupoDim3 en Dim3).
  Réplica de `ArbolSaldos` (lazy, conteos de vivos, toggle de inactivos).
- **Árbol de asignación (5 niveles)**: Dim1 → GrupoDim2 → Dim2 → GrupoDim3
  → Dim3, con los grupos **acotados al padre** ("OPERACIONES bajo CONKAL" =
  solo las Dim2 de Conkal con ese grupo). Cada renglón lleva **tri-estado**
  (ninguno / parcial / todo) calculado desde las hojas; barra resumen por
  dimensión arriba.

## 4. Pantallas específicas

### 4.1 Configuración (Módulo 1) — pantalla única

Como el diseño previo (modales con padre heredado, advertencia de cascada
con conteos, toggle de inactivos, búsqueda con expansión de rama, CRUD de
grupos en menú secundario) con el vocabulario "Dimensión N" / "Grupo
dimensión N". Desviación deliberada del patrón Sheet: el CRUD va en
**modales** sobre la misma pantalla del árbol — el contexto (posición en el
árbol) es la mitad del formulario.

### 4.2 Asignación (Módulo 2) — árbol con tri-estado

- Selector de usuario arriba (molde `UsuarioSelector`); el árbol pinta el
  alcance de ESE usuario.
- **Marcar cualquier nivel = atajo de captura**: el sistema expande a las
  máquinas vivas bajo el nodo y guarda esas hojas (`usuario → dim3_id`).
  La UI muestra el resultado expandido, no la regla — no hay "reglas
  guardadas" que administrar.
- **Tri-estado por renglón** (checkbox de tres estados: vacío / raya /
  palomita) + **barra resumen por dimensión** ("Dim1: 2/5 completas ·
  Dim3: 118/361 asignadas").
- **Sin re-evaluación en vivo**: una máquina creada después NO entra sola;
  el renglón del grupo pasa de "todo" a "parcial" y alguien re-marca.
  **Consecuencia visible (aceptada)**: quitar una máquina a propósito y
  faltar por desactualización pintan IGUAL ("parcial") — el tri-estado no
  distingue intención.
- Usuarios con `dim3.leer-todos`: badge "alcance total", árbol
  deshabilitado con explicación.
- **Convención del clic (copiada de `MatrizPermisos.tsx`, el molde de
  tri-estado del ERP):** el checkbox del padre es COMPUTADO, no un estado
  clicable — `Todo→true`, `Parcial→'indeterminate'`, `Ninguno→false`.
  Radix normaliza `indeterminate→true` al clic, así que **clic en un padre
  parcial → COMPLETA** (marca todas las hojas vivas bajo él). El ciclo por
  clic es **vacío↔todos**; "parcial" solo aparece como reflejo de hijos
  mixtos, jamás como resultado de un clic. En CeCo cada clic es un POST
  inmediato (`MarcarAlcance`, `Asignar` = true si el destino es completar,
  false si limpiar) — no hay acumulación cliente ni "Guardar". Antes solo
  vivía en el código de MatrizPermisos.
- **`esAlcanceTotal` (badge "alcance total"):** el GET `/arbol` devuelve
  `esAlcanceTotal` = si el usuario SELECCIONADO tiene
  `centros_costo.dim3.leer-todos`. Se resuelve en el ENDPOINT (Api) vía
  `IPermissionLoader` del usuario seleccionado — NO en el módulo
  CentrosCosto, que sigue sin dependencia de Identidad (#620). Con `true`:
  badge + árbol deshabilitado (asignar no aporta, ese usuario ve todo).
- **Reconciliación tras marcar:** la fuente de verdad del tri-estado es el
  backend (lo recomputa desde las hojas). La UI **no re-deriva**: click →
  renglón en pending → POST → invalida+refetch del árbol → repinta con el
  tri-estado del response. Cero cálculo de tri-estado en el cliente
  (refetch-only; el full-tree es una query barata, ~475 nodos).
- **Superficies (post-Fase E):** el árbol de asignación vive en dos lugares,
  ambos sobre el mismo componente compartido
  `AsignacionUsuarioPanel` (`features/centros-costo/components/`) — árbol +
  resumen + banner de alcance total + hooks, recibe `usuarioId: string` y
  `readOnly?: boolean`:
  1. La pantalla dedicada `/centros-costo/asignaciones` (esta §4.2): aporta el
     `UsuarioSelector` arriba y el prompt "selecciona un usuario"; delega el
     resto al panel.
  2. El **tab "Centros de Costo"** del detalle de usuario
     (`/admin/usuarios/$id`, `modules/identidad/components/UsuarioDetalle.tsx`):
     monta el panel con el usuario del detalle, sin selector. Reduce la
     fricción del modelo "config por demanda" de Fase E — el admin asigna sin
     salir del contexto del usuario. **Gateado por
     `centros_costo.asignaciones.administrar`** (el mismo permiso del GET del
     árbol); sin él el tab no se renderea. El `ArbolAsignacion` no cambia: su
     prop `disabled` ya cubre el read-only y el panel no embebe selector.

### 4.3 Selector "Máquina" en documentos (Fase E)

Diseño de fondo: **ADR-0050** + [`08-consumo-compras.md`](08-consumo-compras.md).
El FE no decide el alcance — pinta lo que el backend le sirve.

**UN solo campo por línea** (`Dim3Picker`, combobox server-side molde
`ArticuloSelector`): item `MCLC101 — Gantry`, línea secundaria de área
(`Corte · Conkal`) **solo como ayuda de desambiguación al elegir entre ~361**,
no como jerarquía del documento. **Tres modos, decididos por documento, no por
el FE:**

- **Filtrado (RQ)** — el requisitante elige solo **sus** máquinas
  (`GET /dim3/buscar`, filtrado por alcance). Prellenado si su alcance resuelve
  a 1. Obligatorio.
- **Abierto (vale + línea manual de OC)** — el proxy (almacenista / comprador)
  elige de **todas las activas**. El selector abierto vive detrás de un endpoint
  gateado por su permiso (`almacen.salidas.por-vale` / `compras.ordenes.crear-sin-rq`):
  si el FE de un requisitante lo llama, **403**. No hay eje del ERP (departamento)
  que acote las 361 — el catálogo de máquinas es independiente (#620) —, por eso
  el abierto es lo único coherente. Obligatorio.
- **Heredado, solo lectura (OC-desde-RQ, entrada, salida-con-RQ)** — **no hay
  selector**: se muestra el valor que viajó con el documento. El nombre se
  resuelve por el read-port batch **sin filtro de alcance** (un aprobador sin esa
  máquina en su alcance igual ve el nombre, no "No catalogado") e **incluye
  inactivas** (ADR-0049).

La captura del viejo "Máquina destino" (cabecera de salida) **muere** — máquina
destino y CC-Máquina son lo mismo, y el CC-Máquina es **por línea**. Cold value
irresoluble (GUID de un dato roto): "No catalogado" con tooltip.

**CC-G4 CERRADO**: los documentos muestran **`clave — nombre`, sin jerarquía**.
La jerarquía (planta → área → máquina) es material de los **reportes de la
Fase F**, no del documento operativo. La captura por proxy es **dato de menor
confianza** (elige a mano sin eje que lo guíe): un reporte de Fase F que no
cuadre ahí no es bug — se revisa/reasigna en el consumo (ADR-0050).

## 5. Componentes

- `ArbolCentrosCosto` (config, réplica de `ArbolSaldos`) y
  `ArbolAsignacion` (5 niveles + tri-estado — **sin molde exacto**: árbol
  lazy + checkbox tri-estado; el más cercano es `ArbolSaldos` para el
  esqueleto).
- `MaquinaSelector` (documentos, molde `ArticuloSelector`).
- `UsuarioSelector`, `EstadoBadge` — existentes.

## 6. Estados, errores, permisos

- Problem Details → toasts (patrón global); 409 de clave duplicada con la
  clave visible; If-Match: 428/409 → recargar con aviso.
- Árbol vacío pre-siembra con mensaje de arranque.
- Acciones ocultas sin permiso (patrón del shell); excepción documentada:
  árbol de asignación deshabilitado-con-explicación para `leer-todos`.

---

## 7. Lenguaje visual (ajuste de alineación al ERP, 2026-07-16)

Tras la revisión de usuarios de FE-PR1/FE-PR2, se alinea el módulo al
lenguaje visual establecido del ERP (exemplar: Compras/Requisiciones).
**Regla dura: cero cambios a `components/ui/*` ni a otros módulos** — todo
sale de primitivos y patrones ya existentes. Verificado contra el código
en la fecha del ajuste; **un molde sin `archivo:línea` es una intención,
no una decisión**, así que la tabla de abajo lleva ruta.

### 7.1 Tabla de mapeo molde-por-elemento

| Elemento en CeCo | Qué se hace | Molde del ERP (`archivo:línea`) |
|---|---|---|
| Tabla del árbol (encabezados uppercase/gris) | `<table>` raw (NO hay primitivo `table.tsx`) envuelto en `overflow-x-auto rounded-md border`; `<thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">`; `<th className="px-3 py-2 text-left">`; filas zebra `bg-muted/20` impares; clave en `font-mono` | `features/compras/pages/BandejaRequisiciones.tsx:201-231` (replicado en `features/almacen/pages/RecepcionesPage.tsx:322`) |
| Columnas | Clave·Nombre \| Grupo \| Contenido (conteos de vivos) \| Acciones | — (diseño propio) |
| Dim1 con fondo sutil | El nivel raíz se distingue por peso/fondo, no solo por sangría | tono `bg-muted/40` (token del tema) |
| Badges (píldora) | **Ya son píldora** vía el primitivo (`rounded-full`); no cambia la forma | `components/ui/badge.tsx:7` |
| Badge "Inactivo" | Gris tenue con píldora | `bg-slate-100 text-slate-700` (paleta de `EstadoBadge`) |
| Selector de GRUPO en los modales (dim2 y dim3) | **Combobox ⇅** (`CatalogoEagerCombobox` vía `GrupoDimCombobox`), NO Select: grupo es un **catálogo** buscable, no una lista corta (grupos_dim3 son 44 → un Select desborda la pantalla sin buscador). **Ambos** niveles usan el mismo control aunque dim2 tenga 6 — ver la regla abajo. Carga TODOS los grupos marcando los inactivos (edición muestra el grupo real, no un GUID). | `components/erp/selectors/CatalogoEagerCombobox.tsx`; uso EN MODAL: `features/compras/components/DesignarAprobadorDialog.tsx:143-266` |
| Filtro de ESTADO del árbol | **Select nativo ˅** (`ChevronDown`) en vez del checkbox de inactivos suelto — ese SÍ es lista corta fija (activos / incluir-inactivos, no crece) → `incluirInactivos`. El **filtro de grupo NO se implementó** — ver §7.6 | `components/ui/select.tsx:29`; uso ej. `features/compras/pages/NuevaRequisicion.tsx:394-417` |
| Botón primario | `+ Nueva dimensión` = `Button` default (`bg-primary` = **negro neutro**, no navy) | `components/ui/button.tsx:11`; uso `BandejaRequisiciones.tsx:95-99` |
| Botón secundario | `Asignaciones` = `Button variant="outline"` | `components/ui/button.tsx:13` |
| Inputs | `Input` primitivo: `h-9 rounded-md border border-input` (radius deriva del token `--radius`) | `components/ui/input.tsx:12-14` |
| Selector de catálogo buscable | Combobox con ícono flechitas ⇅ (`ChevronsUpDown`) — para el `MaquinaSelector` de Fase E | `components/erp/selectors/CatalogoEagerCombobox.tsx:125` / `ArticuloSelector.tsx:189` |
| Select de lista corta fija | Chevron abajo ˅ (`ChevronDown`) — para filtros de grupo/estado | `components/ui/select.tsx:29` |
| Modales CRUD | Grid `md:grid-cols-2`; labels chicos con asterisco rojo (`text-rose-600`); footer `Cancelar` ghost a la izquierda + primario a la derecha; padre heredado en el TÍTULO, ruta completa + "no se cambia" en el subtítulo | `features/compras/pages/NuevaRequisicion.tsx:298` (grid), `:564-591` (`FormRow` label+asterisco), `:509-541` (footer) |

**Regla combobox ⇅ vs Select ˅ — el criterio es QUÉ CONTIENE, no dónde
vive:**

- **Un CATÁLOGO va a combobox ⇅ aunque hoy tenga 6 elementos** (grupos,
  máquinas, sucursales…): puede crecer y merece buscador. Un Select sin
  buscador sobre un catálogo desborda la pantalla (44 grupos_dim3 taparon
  el modal — bug real, fix 2026-07-17). Además: dos controles distintos
  para el MISMO concepto en modales hermanos (Select para dim2 con 6,
  combobox para dim3 con 44) es una inconsistencia que el usuario nota, y
  el umbral "N elementos" no tiene buena respuesta. Por eso **ambos
  selectores de grupo usan combobox**.
- **Una LISTA CORTA FIJA va a Select ˅** (activo/inactivo/todos,
  prioridad, clasificación): conjunto acotado que NO crece. El filtro de
  estado del árbol es esto.
- **Popover-en-Dialog está probado**: `CatalogoEagerCombobox` (Popover +
  Command) montado dentro de un Radix `Dialog` funciona — el focus-trap
  NO pelea. Precedente vivo:
  `features/compras/components/DesignarAprobadorDialog.tsx:143-266`
  (`DepartamentoSelector`/`UsuarioSelector` dentro del `DialogContent`).
  No volver a dudarlo.

### 7.2 Tokens vs colores hardcodeados (no inventar hex)

Es **Tailwind v4**: el tema vive en `frontend/src/index.css` vía `@theme`
(no hay `tailwind.config`). **Neutros y radius son tokens** (`--primary`
= negro neutro `oklch(0.205 0 0)`; `--muted`, `--border`, `--input`,
`--radius: 0.5rem`) — usarlos. El **navy solo existe en los tokens del
sidebar** (`--sidebar`), NO en el primario. Los **colores de estado NO son
tokens**: el ERP los hardcodea con paleta Tailwind vía `EstadoBadge`
(`components/erp/display/EstadoBadge.tsx:78-141`): verde=`emerald`,
ámbar=`amber`, gris=`slate`, rojo=`rose`, en píldora
`rounded-full … ring-1 ring-inset`. Reutilizar esas clases, no inventar
paleta.

### 7.3 El color semántico del tri-estado — DÓNDE va (FE-PR3.1)

En el árbol de **configuración** (FE-PR2) los badges son chips de grupo
(outline) + "Inactivo" (slate) — no llevan color de estado.

**El color semántico va en la columna ASIGNADAS del árbol de ASIGNACIÓN.**
(La versión previa de este §7.3 decía "el color de los badges" sin decir
cuáles — ambigüedad real: en FE-PR3 el tri-estado lo transmite un
**checkbox**, no un badge. FE-PR3 dejó la columna ASIGNADAS como conteo
gris pelado. FE-PR3.1 la corrige.) Reglas:

| Estado del nodo | Render en la columna ASIGNADAS |
|---|---|
| **Todas** | Píldora **emerald** — "Todas · {asignadas}" |
| **Parcial** | Píldora **amber** — "Parcial · {asignadas} de {vivas}" |
| **Ninguna** | Texto **gris tenue SIN píldora** — "Ninguna · 0 de {vivas}" |

- **"Ninguna" va como texto, no píldora** (refinamiento, no contradicción
  del slate=Ninguno): con datos reales la mayoría de renglones queda en
  Ninguna (un usuario típico tiene pocas de 361 máquinas), y decenas de
  píldoras grises ahogarían las pocas emerald/amber que sí importan. El
  color señala SEÑAL, no ocupa espacio — el ojo debe saltar a lo asignado.
- **El resumen de arriba NO lleva badge** (conteo pelado): es un AGREGADO
  de todo el árbol ("Dimensión 3: 14/361"), no el tri-estado de un nodo.
  No hay un `TriEstado` único que pintar, y si lo hubiera sería ámbar
  permanente — un color que nunca cambia no informa.
- **Las hojas (Dim3) NO llevan badge** en esa columna: son binarias
  (asignada sí/no, `dim3Vivas=0` haría "X de 0" absurdo); su checkbox ya
  lo dice.
- **Píldora inline con las clases de la paleta** (emerald/amber
  `rounded-full … ring-1 ring-inset`), NO el componente `EstadoBadge`
  (acoplado a estados de RQ/OC) ni tocando `components/ui/*` — mismo
  patrón que la píldora "Inactivo" del §7.1.

Los grupos usan su propio tono neutro/outline para no competir con el
tri-estado.

### 7.4 Maestro-detalle: NO adoptado (no reabrir sin leer el porqué)

El maestro-detalle (lista izquierda ~320px + detalle derecho con acciones
arriba) es el estándar del ERP — 13 layouts, molde
`features/compras/pages/RequisicionesLayout.tsx:107-156` (aside `md:w-80`
+ section `flex-1`) con sub-topbar `sticky top-14`
(`DetalleRequisicion.tsx:152-213`). **Se evaluó y se descartó para CeCo**,
por razones de fondo, no de costo:

1. **Un nodo de CeCo no tiene cuerpo.** El panel derecho mostraría
   clave/nombre/grupo/estatus — todo lo que YA está en el renglón — más 2
   botones. Consistencia formal con contenido vacío es peor que romper el
   patrón por una razón.
2. **El maestro-detalle asume una LISTA plana**; nuestro maestro ES el
   árbol jerárquico. No es el mismo objeto.
3. **320px no le alcanzan al árbol**: clave + nombre + chip de grupo +
   conteos truncarían salvaje. El árbol necesita ancho completo — y el de
   asignación (5 niveles + tri-estado, FE-PR3) aún más.

**Decisión: tabla-en-árbol a ancho completo**, con el menú de acciones por
renglón (no sub-topbar de detalle). Si en Fase E surge un panel-detalle
con contenido que lo justifique (p. ej. documentos que consumen ese CeCo),
se agrega ahí — con cuerpo real, no como cáscara por consistencia.

### 7.5 Fase E — el selector de Dim3 SUSTITUYE un campo existente (no agrega)

Verificado en código: el campo **`centroCostoId`** que el
`MaquinaSelector` reemplazará ya existe **como `<Input type="text">` de
texto libre** (placeholder "opcional"), bajo el toggle "+ Detalles" junto
a Proyecto y Cuenta contable:

- **RQ**: `features/compras/components/LineaInlineForm.tsx:443-447`
  (schema `features/compras/schemas/linea.ts:57`).
- **OC**: `features/compras/ordenes/components/LineaInlineFormOc.tsx`
  (mismo patrón).
- **Almacén (salida)**: `features/almacen/components/NuevaSalidaSheet.tsx`
  (campo `centroCosto` en su schema `salida.ts`).

**Inconsistencia que Fase E resuelve**: el schema ya valida forma de GUID
(`linea.ts:20` → `idLikeOpcional = z.string().regex(UUID_SHAPE_RE)
.nullish()`) pero la UI captura texto libre — el selector cierra la brecha
produciendo el GUID. La Fase E **cambia el control, no agrega columna ni
migra datos** (los GUIDs históricos → display "No catalogado", §4.3).

### 7.6 Punto abierto — filtro por GRUPO en el árbol (diferido, NO es "se nos olvidó")

El ajuste visual entregó el filtro de **estado** (Select). El filtro por
**grupo** ("Todos los grupos ˅") **se difirió a propósito**: no es un
restyle, es un cambio de UX que no cabe en un PR de ajuste visual.

- **Por qué no se pudo hacer como restyle:** el árbol de configuración es
  **lazy** — el endpoint de jerarquía (`GET /centros-costo/jerarquia`)
  devuelve los hijos-de-un-nodo y **NO toma parámetro de grupo**. Filtrar
  solo los nodos ya expandidos MENTIRÍA: el usuario elige "OPERACIONES" y
  obtiene lo que ya abrió, no lo del catálogo. Peor que no tenerlo.
- **Vía probable cuando se retome:** los endpoints **planos** SÍ filtran
  por grupo (`ListarDim2Query.grupoDim2Id`, `ListarDim3Query.grupoDim3Id`),
  así que un filtro por grupo real es un **modo lista-plana** (otra
  pantalla/vista, no el árbol filtrado) — o un parámetro nuevo en el
  endpoint de jerarquía. Es diseño, no estilo.
- **Estado:** abierto. No reabrir como bug de "faltó el filtro".

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
| 0.2 | 2026-07-16 | Modelo Dim: nomenclatura contextual (tabla de traducción), los DOS árboles (config 3 niveles con chips vs asignación 5 niveles acotados), tri-estado calculado con su consecuencia visible, campo único "Máquina" en documentos (muere la captura de máquina destino), CC-G4 redefinido. |
| 0.3 | 2026-07-16 | §7 lenguaje visual (ajuste post-revisión de usuarios): tabla de mapeo molde-por-elemento con `archivo:línea` (tabla raw uppercase de `BandejaRequisiciones`, `EstadoBadge`, combobox ⇅ vs select ˅, modal `NuevaRequisicion`); tokens vs colores de estado hardcodeados (Tailwind v4 `@theme`, primario negro-neutro no navy); color semántico de badges (emerald/amber/slate) diferido a FE-PR3; **maestro-detalle NO adoptado** con razonamiento (nodo sin cuerpo, árbol≠lista, 320px insuficientes); hallazgo Fase E: `centroCostoId` es hoy `<Input>` texto libre que el selector SUSTITUYE (RQ/OC/salida), con la inconsistencia schema-espera-GUID/UI-texto-libre. |
| 0.3.1 | 2026-07-16 | Al implementar el ajuste: §7.6 filtro por grupo DIFERIDO (el árbol es lazy y `/jerarquia` no toma parámetro de grupo — filtrar lo cargado mentiría; vía real = modo lista-plana con los endpoints planos `grupoDim2Id`/`grupoDim3Id`). §7.1 corregido: solo el filtro de estado se implementó. |
| 0.3.2 | 2026-07-17 | Fix de UX: el selector de grupo de los modales era un Select nativo y con 44 grupos_dim3 el dropdown desbordaba la pantalla tapando el modal. §7.1 corregido (la fila imprecisa "Select para filtros de grupo/estado" separada en dos: **selector de grupo → combobox ⇅**, **filtro de estado → Select ˅**) + **regla explícita "el criterio es qué contiene, no dónde vive"** (un catálogo va a combobox aunque tenga 6) + precedente verificado de Popover-en-Dialog (`DesignarAprobadorDialog.tsx:143-266`). |
| 0.4 | 2026-07-17 | FE-PR3: §4.2 — **convención del clic** en el tri-estado (copiada de `MatrizPermisos.tsx`: clic en parcial → completa; ciclo vacío↔todos; el parcial nunca sale de un clic); **`esAlcanceTotal`** resuelto en el ENDPOINT vía `IPermissionLoader` del usuario seleccionado (el módulo CentrosCosto sigue sin dependencia de Identidad, #620 intacto); **reconciliación refetch-only** (la UI no re-deriva tri-estado; el backend es la fuente de verdad). |
| 0.4.1 | 2026-07-17 | FE-PR3.1: §7.3 reescrito para resolver su ambigüedad (decía "el color de los badges" sin decir cuáles; FE-PR3 dejó la columna ASIGNADAS como conteo gris). Ahora dice DÓNDE va el color semántico: la **columna ASIGNADAS del árbol de asignación**, píldora emerald (Todas) / amber (Parcial); **Ninguna en texto gris SIN píldora** (con datos reales la mayoría es Ninguna — el color señala señal, no ocupa espacio); resumen y hojas sin badge, con su porqué; píldora inline con las clases de la paleta (no el componente `EstadoBadge`). |
| 0.5 | 2026-07-18 | Fase E cerrada (ADR-0050 + doc `08-consumo-compras.md`): §4.3 reescrito con las **3 semánticas del selector** (filtrado RQ / abierto-por-proxy vale+línea manual OC gateado por permiso en el backend / heredado solo-lectura con read-port sin filtro). **CC-G4 CERRADO** (documentos = `clave — nombre`, sin jerarquía; jerarquía → Fase F). Muere la máquina destino de cabecera. Captura por proxy = dato de menor confianza (consecuencia aceptada). El área secundaria del picker es ayuda de desambiguación, no jerarquía del documento. |
| 0.6 | 2026-07-21 | Post-Fase E: §4.2 — el árbol de asignación se extrae al componente compartido **`AsignacionUsuarioPanel`** y gana una **segunda superficie**: el tab "Centros de Costo" del detalle de usuario (`/admin/usuarios/$id`), gateado por `centros_costo.asignaciones.administrar`. Reduce la fricción del "config por demanda" (asignar sin salir del contexto del usuario). Sin ADR nuevo (no hay decisión arquitectónica: refactor + patrón de gate ya existente). `ArbolAsignacion` intacto. |
