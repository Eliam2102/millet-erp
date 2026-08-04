# Levantamiento — Módulo Centros de Costo (`Millet.CentrosCosto`)

> **Versión:** 0.2 · **Fecha:** 2026-07-16
> **Fuentes:** `Prop_Cecos_MIndustria_Jul25.xlsx` (Contabilidad, jul-2025),
> `Diseno_Centros_Costo.md` (borrador funcional de Contabilidad, superado
> — ver §7), exploración del repo 2026-07-14 (§4) y diseño cerrado con
> Eduardo y Contabilidad 2026-07-14.

---

## 0. Cómo leer este documento

§1–§2 delimitan el módulo; §3 es el análisis del Excel fuente (los datos
que siembran el catálogo); §4 el AS-IS del sistema (qué existe hoy y qué
huecos llena el módulo); §5 los procesos TO-BE; §6 las entidades; §7 la
historia de la decisión de diseño; §8 los gaps abiertos. El diseño
técnico vive en [`01-diseno.md`](01-diseno.md).

---

## 1. Propósito y alcance

### Qué hace este módulo

Catálogo jerárquico de centros de costo con 3 niveles navegables
(**Dim1 → Dim2 → Dim3** en código; la UI los presenta por contexto —
diseño §4) y 2 grupos de clasificación (**GrupoDim2**, **GrupoDim3** —
etiquetas, NO niveles). La **Dim3** ("Máquina" en documentos) es el único
nivel seleccionable: el usuario elige un solo campo y los niveles
superiores se heredan para reportes, control de acceso y (a futuro)
presupuesto. Incluye la administración del árbol (Contabilidad), la
asignación de alcance usuario→máquinas, y el selector legible que
reemplaza los GUIDs tecleados a mano. **Totalmente separado** de los
catálogos compartidos del ERP (§7.4).

### Qué NO hace (anti-alcance explícito)

- **No maneja presupuesto** en v1 — solo clasifica. La estructura queda
  lista para agregarlo sin cambios de fondo (el legacy PortalSap tenía
  control presupuestal por año/mes/cuenta/centro/fondo; ese es el
  antecedente del alcance futuro, no de esta v1).
- **No toca NINGÚN catálogo compartido** (§7.4): ni
  `compartido.sucursales` (Dim1 es tabla propia — no crea sucursales ni se
  vincula a ellas) ni `compartido.departamentos` (taxonomías
  independientes, §4.3). Cero lecturas y cero escrituras fuera de su
  esquema.
- **No postea a Contabilidad** — cuando el módulo Contabilidad exista,
  consumirá el catálogo vía read-port/eventos (el evento de salida de
  Almacén ya transporta `centro_costo_id`).
- **No migra datos históricos**: los GUIDs libres ya guardados en
  documentos viejos no se validan ni se convierten (§5.4).

### Boundary con otros módulos

| Módulo | Relación |
|---|---|
| Administración | **Ninguna** desde §7.4 — el Conkal del ERP y el CONKAL de CeCo no se relacionan (consecuencia aceptada) |
| Compras | Consumidor: línea de requisición captura la Máquina/Dim3 (Fase E) |
| Almacén | Consumidor: línea de salida + vale urgente capturan la Máquina/Dim3 — un solo campo; la captura de `maquina_destino_id` se retira (Fase E) |
| Identidad | Permisos canónicos `centros_costo.*`; la asignación usuario→máquinas es del CeCo, no de Identidad |
| Contabilidad (futuro) | Consumidor de la clasificación para pólizas |

## 2. Actores

| Actor | Rol en el módulo |
|---|---|
| **Contabilidad** (autoridad funcional) | Administra el árbol completo (CRUD), define asignaciones de alcance; con `centros_costo.equipos.leer-todos` ve todo sin asignación |
| Capturistas de RQ / almacenistas | Seleccionan el Equipo en sus documentos, filtrado por su alcance |
| Jefes de área | Consumen reportes consolidados por la clave computada `{ClaveCeCo}-{ClaveDepto}-{ClaveEquipo}` |

## 3. Fuente de datos — análisis del Excel

**Archivo:** `Prop_Cecos_MIndustria_Jul25.xlsx`, hoja **"Base"**
(headers en fila 4, datos desde fila 5). Columnas: DIM1 = sucursal
(clave numérica + descripción), DIM2 = departamento (+ "Grupo DM2" =
grupo), DIM3 = equipo (+ "Grupo DM3" = subgrupo). Las hojas "Tabla" son
pivotes; "Cat" es el catálogo de valores de dimensión.

**Números verificados (2026-07-14):**

| Concepto | Valor |
|---|---|
| Equipos | **361**, claves únicas globales ✔ (máx 7 caracteres; descripción máx 48) |
| Departamentos | **57 claves** únicas (máx 6 caracteres); ninguno cruza sucursales |
| Grupos (dimensión DM2) | **6**: OPERACIONES, COMERCIAL, ADMINISTRACION Y FINANZAS, CAPITAL HUMANO, MERCADOTECNIA, CM |
| Subgrupos (dimensión DM3) | **44** (GENERAL, HORNO 1–4, LINEA / CORTE n, FLOTADO, FORK LIFT…) |
| Sucursales | **5**: 101 CONKAL, 102 CHICHI SUAREZ, 103 CIRCUITO, 104 CANCUN, 105 PLANTA PINTURA |
| Equipos por sucursal | 101 = 336 · 102 = 10 · 103 = 4 · 104 = 10 · 105 = 1 |

**Hallazgos de calidad de datos:**

- **Typo `20PDPR`**: la misma clave de departamento aparece con dos
  nombres ("SERVICIOS PERIFERICO" y "SERVICIOS PERIFERICOS"). La siembra
  normaliza a **"SERVICIOS PERIFERICOS"** (57 claves reales; el conteo
  "58" de pláticas previas contaba los pares clave-nombre).
- **El grupo es consistente por departamento** (cero departamentos con
  más de un grupo) → la dimensión Grupo vive en el Departamento. El
  subgrupo varía por equipo → vive en el Equipo.
- Ejemplo real completo: `101/CONKAL → 20PDMC/CORTE (OPERACIONES) →
  MCLC101/GANTRY, NS/ 19434 (LINEA / CORTE 1)`.

## 4. Estado actual del sistema (AS-IS, exploración 2026-07-14)

### 4.1 El "centro de costo" hoy: un GUID a mano, sin catálogo

No existe entidad ni tabla de centros de costo. `CentroCostoId` es un
`Guid?` suelto **a nivel línea**, sin FK ni validación de existencia
(los docs de Compras lo declaran "informativo, sin validación
bloqueante"), capturado en **tres** inputs de texto libre con
`placeholder="GUID"`:

| Flujo | FE | BE |
|---|---|---|
| Salida de Almacén con RQ | `NuevaSalidaSheet.tsx` (~L804) | `RegistrarSalidaConRequisicionCommand` → `LineaMovimiento.CentroCostoId` |
| Vale urgente (captura libre) | `NuevaSalidaSheet.tsx` (~L1029) | `RegistrarSalidaPorValeCommand` |
| Línea de requisición | `LineaInlineForm.tsx` (~L440) | `AgregarLinea/ActualizarLineaCommand` → `LineaRequisicion.CentroCostoId` |

Los schemas zod solo validan **forma** de UUID. El evento
`SalidaRequisicionRegistradaIntegrationEvent` ya transporta el campo
(comentado: "Contabilidad asienta póliza al centro de costo").

### 4.2 `MaquinaDestinoId`: el huérfano que ES el Equipo

La salida de Almacén tiene `MovimientoInventario.MaquinaDestinoId`
(`Guid?` sin FK, sin tabla destino, sin catálogo), capturado como
`<Input placeholder="GUID de la máquina">`. El diseño FE original pedía
un select que nunca tuvo catálogo detrás. **Conceptualmente es el mismo
"equipo" del CeCo** — la Fase E lo re-apunta al mismo selector. También
existe `ProyectoId` igual de huérfano (fuera de alcance de este módulo).

### 4.3 Lo que ya existe y NO se reutiliza (con razón documentada)

- **`compartido.departamentos`** (Administración): 7 departamentos
  **funcionales** (ALMACEN, CAL, COMPRAS, ING, MTTO, SIS, SIS-REAB) con
  N:M a sucursales, usados por aprobadores de Compras e Identidad
  (`Usuario.DepartamentoId`). Se comparó contra los 57 del Excel: **solo
  1 de 58 pares coincide de nombre, por casualidad**. Son taxonomías
  distintas (funcional vs centros de costo) → el CeCo crea su
  Departamento propio y no toca el catálogo de Administración.
- **`compartido.sucursales`**: existe, pero al momento de la exploración
  solo tenía el **seed de prueba** (MID/MTY/QRO — en prod la tabla nace
  vacía). El diseño original la vinculaba por Guid lógico; desde la
  decisión de separación total (§7.4) **CeCo NO la toca**: Dim1 es tabla
  propia con la clave 101–105 y el nombre del Excel, sin relación alguna
  con el catálogo general.

### 4.4 Moldes existentes que el módulo copia

Table-per-level de Almacén N2–N4 (`Ubicacion`), consulta jerárquica lazy
ADR-0047 (`JerarquiaQueries` + `ArbolSaldos`), selector server-side
(`ArticuloSelector`). El alcance del modelo Dim (expansión a máquinas +
tri-estado calculado, diseño §7) **no tiene molde exacto en el repo** — de
Cajas (`UsuarioAlcance`) solo sobrevive la idea de fila-por-concesión.
Detalle por PR en [`03-pr-breakdown.md`](03-pr-breakdown.md).

## 5. Procesos (TO-BE)

### 5.1 Administración del árbol [Contabilidad]
Pantalla única (Módulo 1): árbol jerárquico lazy + CRUD completo con
modales. Alta hereda el padre del nodo donde se pulsó "+" (nunca se
selecciona a mano); editar permite clave/nombre/dimensión; **reubicar =
baja + alta** (el padre es inmutable); baja lógica en cascada (equipos
de un departamento dado de baja quedan inactivos, sin borrar).

### 5.2 Asignación de alcance [Contabilidad]
Módulo 2 (entrada propia de menú): árbol de **5 niveles** (Dim1 →
GrupoDim2 → Dim2 → GrupoDim3 → Dim3, grupos acotados al padre) con
tri-estado calculado. Marcar cualquier nivel es un atajo de captura que
**expande y congela máquinas** (`usuario → dim3_id`, una columna) — sin
re-evaluación en vivo. Detalle y consecuencias en diseño §7.
`centros_costo.dim3.leer-todos` (Contabilidad) ve todo.

### 5.3 Selección en documentos [capturistas]
**UN solo campo: "Máquina"** — combobox con búsqueda server-side que
muestra `MCLC101 - Gantry (Corte · Conkal)` — nunca el UUID — filtrado por
el alcance del usuario, por línea en RQ/salida/vale. Centro de costo y
máquina destino son lo mismo (la captura del viejo `maquina_destino_id` se
retira). Qué documentos MUESTRAN los niveles heredados: punto abierto por
documento en Fase E.

### 5.4 Documentos históricos
Los GUIDs libres ya guardados no migran ni validan retroactivamente; en
detalle/impresión se muestran como "No catalogado". Los documentos en
curso que referencian un equipo dado de baja siguen resolviendo por Id.

## 6. Entidades del dominio

| Entidad (código) | Nivel/rol | Campos propios | UI |
|---|---|---|---|
| `Dim1` | N1 | `Clave` (UNIQUE global, "101"), `Nombre` ("CONKAL") | "Dimensión 1" |
| `GrupoDim2` | clasificación de Dim2 | `Nombre` (UNIQUE) | "Grupo dimensión 2" |
| `GrupoDim3` | clasificación de Dim3 | `Nombre` (UNIQUE) | "Grupo dimensión 3" |
| `Dim2` | N2 | `Dim1Id` (FK física), `Clave` (UNIQUE global), `Nombre`, `GrupoDim2Id` | "Dimensión 2" |
| `Dim3` | N3 — hoja | `Dim2Id` (FK física), `Clave` (UNIQUE global), `Nombre`, `GrupoDim3Id` | "Dimensión 3" / **"Máquina"** en documentos |
| Asignación (Fase D) | alcance | `(usuario_id, dim3_id)` — una columna, congelado en máquinas | tri-estado calculado, nunca almacenado |

Todas con `Estatus` estilo `EstatusCatalogo` (baja lógica) y `BaseEntity`
(Version/ETag, auditoría). Cero relaciones fuera del esquema. Clave
consolidada de reportes `{Dim1}-{Dim2}-{Dim3}` = **computada, no
almacenada**.

## 7. Historia de la decisión de diseño

1. **Borrador de Contabilidad** (`Diseno_Centros_Costo.md`, jul-2025):
   proponía **4 niveles** (Sucursal → **Grupo** → Departamento → Equipo)
   y alcance por rol. **Superado** por el diseño cerrado 2026-07-14:
   el Excel demostró que Grupo es un atributo consistente del
   Departamento (no un nivel navegable) y Subgrupo del Equipo; el
   alcance quedó por **usuario→nodo** (molde Cajas), no por rol.
2. **¿Módulo propio o anexo?** (2026-07-14, con Eduardo): se analizó el
   patrón "anexo" del repo (Cajas en Facturación, TC en CxP, OC en
   Compras — comparten proyecto/esquema/DbContext/namespace del
   anfitrión). Decisión: **módulo propio** con las 5 cosas propias
   (proyecto, esquema `centros_costo`, DbContext, namespace `0000000c-*`,
   set documental completo), porque tiene dueño funcional propio
   (Contabilidad), dos entradas de UI propias, alcance/asignaciones
   propios y consumidores múltiples (Compras, Almacén, Contabilidad
   futura). La observación de Eduardo que motivó este set: *un módulo
   trae su set documental completo* — precedente Tesorería.
3. **Namespace de permisos**: A1 usó `0000000b-*` cuando estaba libre;
   Tesorería (#579) lo ganó al mergear primero. CeCo usa **`0000000c-*`**
   (renumeración pendiente en el rebase del PR #580).
4. **Separación total — modelo Dim** (2026-07-16, decidido con el superior
   de Victor tras la observación de Eduardo). Con la siembra ya armada
   (PR #603), Eduardo alertó que estábamos sembrando datos en catálogos
   compartidos que otros módulos usan para otros fines. Su comentario decía
   "deptos" — **un malentendido**: los departamentos de CeCo son tabla
   propia desde #591 y nadie más los usa. Pero el instinto aterrizó en algo
   real: la migración insertaba 5 filas en `compartido.sucursales`, que SÍ
   es catálogo compartido (Almacén cuelga almacenes de él; Cajas usa su
   `zona_horaria` para el día de operación). **Decisión: CeCo totalmente
   separado.** Dim1 pasa a tabla propia del módulo (como ya lo eran los
   otros niveles); mueren el vínculo `SucursalCentroCosto`, la ClaveCeCo
   (colapsa como `Clave` de Dim1), el `ISucursalReadPort` con su adapter y
   batch, la migración de Compartido, el orden entre contextos y el guard
   de count=5. La nomenclatura de código pasa a **Dim1/Dim2/Dim3 +
   GrupoDim2/GrupoDim3** — el lenguaje del Excel fuente — eliminando la
   colisión de nombres que causó la alarma; la UI traduce por contexto
   (diseño §4). El PR #603 se cerró sin ajustarse; la siembra se rehace
   single-schema. **Consecuencia aceptada — decisión consciente, no
   descubrimiento posterior**: el CONKAL de CeCo y la sucursal Conkal del
   ERP quedan SIN relación (no hay join posible); un reporte "gastos por
   sucursal" sale del árbol de CeCo y no se cruza con Almacén ni
   Facturación. Aceptable porque CeCo solo clasifica.

## 8. Gaps / decisiones abiertas

| ID | Tema | Estado |
|---|---|---|
| ~~CC-G1~~ | ~~Claves de las 5 sucursales en el catálogo general~~ | **Obsoleto por §7.4**: CeCo ya no da de alta sucursales; 101–105 son la clave de Dim1, directas del Excel |
| ~~CC-G2~~ | ~~Alcance a nivel "sucursal completa"~~ | **Resuelto por diseño**: marcar la Dim1 en el árbol de asignación expande todas sus máquinas (atajo de captura, diseño §7) |
| ~~CC-G3~~ | ~~Grupo en alcance: ¿global o acotado?~~ | **Resuelto**: acotado al padre — un grupo nunca se asigna global (diseño §7) |
| CC-G4 | Qué documentos MUESTRAN los niveles heredados (detalle, impresión, contexto) | Abierto — analizar documento por documento en Fase E (la CAPTURA sí está cerrada: un solo campo "Máquina") |
| CC-G5 | Usuario sin asignación: ¿lista vacía o ver-todo? | v1: lista vacía + mensaje (administración explícita) |

**Decisiones ya cerradas** (no re-abrir): separación total de catálogos
compartidos (§7.4); 3 niveles Dim + 2 grupos propios; nomenclatura de
código Dim pura con UI contextual; claves únicas globales; padre inmutable;
baja lógica en cascada; clave consolidada computada; alcance congelado en
máquinas sin re-evaluación en vivo, con tri-estado calculado; GUIDs
**deterministas** en la siembra; carga = migración single-schema (no script
ni endpoint); nunca-GUID en UI.

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
| 0.2 | 2026-07-16 | §7.4 separación total (modelo Dim) con la alerta de Eduardo y la consecuencia aceptada; §4.3/§5/§6/§8 actualizados al modelo nuevo; gaps CC-G1/G2/G3 cerrados u obsoletos, CC-G4 redefinido. |
