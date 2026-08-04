# Diseño — Módulo Centros de Costo (`Millet.CentrosCosto`)

> **Versión:** 0.5 · **Fecha:** 2026-07-16
> **Basado en:** [`00-levantamiento.md`](00-levantamiento.md) v0.2.
> Estado: diseño cerrado (modelo Dim, decisión 2026-07-16 — ver
> levantamiento §7.4). Refactor aplicado en #620 y siembra en #621: el
> código ya habla este modelo.

---

## 0. Cómo leer este documento

§1–§2 fijan objetivo y decisiones; §3–§5 el modelo (estructura, datos,
reglas); §6–§7 permisos y asignación; §8 conexiones; §9 la carga inicial;
§10 UI. La fuente funcional y la historia de las decisiones viven en el
[`00-levantamiento.md`](00-levantamiento.md); el plan por PRs en
[`02-plan-implementacion.md`](02-plan-implementacion.md) y
[`03-pr-breakdown.md`](03-pr-breakdown.md).

---

## 1. Objetivo

Catálogo jerárquico de centros de costo que reemplaza el input de GUID
tecleado a mano en requisiciones y salidas de almacén. Al capturar un
documento, el usuario selecciona **un solo campo** — la **Máquina** (Dim3,
el centro de costo final) — y los niveles superiores se heredan
automáticamente para reportes, control de acceso y (a futuro) presupuesto.

**Regla UX transversal: nunca un GUID de cara al usuario.** Selectores,
árboles y documentos muestran clave + nombre legibles con contexto, ej.
`MCLC101 - Gantry (Corte · Conkal)`.

**Regla de separación (decisión 2026-07-16): CeCo es TOTALMENTE separado.**
No lee ni escribe ningún catálogo compartido: sin Guid lógico cross-schema,
sin read-ports, sin filas sembradas fuera de su esquema. Consecuencia
aceptada y consciente: el CONKAL de CeCo y la sucursal Conkal del ERP no
tienen relación — no hay join posible (levantamiento §7.4).

## 2. Decisiones de diseño y convenciones aplicadas

| Decisión | Referencia |
|---|---|
| Módulo propio (proyecto + esquema + DbContext + namespace + set doc) | Levantamiento §7.2; precedente Tesorería |
| **Separación total de catálogos compartidos** — Dim1 tabla propia | Levantamiento §7.4 (alerta de Eduardo, 2026-07-16) |
| **Nomenclatura de código Dim1/Dim2/Dim3 + GrupoDim2/GrupoDim3** (el lenguaje del Excel fuente); la UI traduce por contexto (§10) | Elimina la colisión con `compartido.departamentos`/`sucursales` |
| Esquema `centros_costo`, DbContext propio con migraciones propias | ADR-0030 |
| Nombres resueltos server-side, nunca GUID al usuario | ADR-0042 (ahora todo local al esquema — sin puertos) |
| Concurrencia optimista Version/ETag con If-Match explícito | ADR-0012 (semántica Cajas, implementada en #594) |
| Idempotency-Key en mutaciones · Versionado `/api/v1/centros-costo/*` · Problem Details | ADR-0020 · ADR-0021 · ADR-0010 |
| Carga inicial por migración de siembra idempotente **single-schema** | Lección #501; [`04-cuidados-infra.md`](04-cuidados-infra.md) §2 |
| Permisos canónicos namespace `0000000c-*` | ADR-0007 (renombre `equipos.leer-todos` → `dim3.leer-todos` en el refactor) |
| Baja lógica en cascada Dim1→Dim2→Dim3 | ADR-0049 (los nombres del ADR son los del modelo previo; este doc es la traducción) |
| Sin outbox en v1 (no hay consumidor de eventos del catálogo) | ADR-0009 aplica cuando Contabilidad exista |

## 3. Estructura

**Tres niveles navegables** (jerarquía física, FK por nivel) y **dos grupos
de clasificación** — todos tablas PROPIAS del esquema `centros_costo`:

```
Dim1  (UI-config: "Dimensión 1";   la planta/sucursal del negocio)
 └─ Dim2  (UI-config: "Dimensión 2";  clasificada por GrupoDim2)
     └─ Dim3  (UI-config: "Dimensión 3"; clasificada por GrupoDim3)
           ↑ único nivel seleccionable en documentos — UI-documentos: "Máquina"
```

- **GrupoDim2** (ej. OPERACIONES, COMERCIAL — 6 en la fuente) clasifica a
  Dim2; **GrupoDim3** (ej. GENERAL, HORNO 1 — 44) clasifica a Dim3. Son
  catálogos **globales** con Id (la fuente los usa consistentes entre
  plantas); en el árbol de **configuración** son un chip del registro; en el
  árbol de **asignación** (§7) actúan como niveles ACOTADOS AL PADRE —
  misma data, dos árboles.
- Los 3 niveles son idénticos en esqueleto: `Clave` (única global) +
  `Nombre` + `Estatus` + padre/grupo donde aplica (Dim1 no tiene padre ni
  grupo).

## 4. Esquema PostgreSQL (`centros_costo`)

| Tabla | Rol | Campos propios | Unicidad |
|---|---|---|---|
| `dim1` | N1 | `clave` (ej. "101"), `nombre` (ej. "CONKAL") | UNIQUE(`clave`) global |
| `grupos_dim2` | clasificación de Dim2 | `nombre` | UNIQUE(`nombre`) |
| `grupos_dim3` | clasificación de Dim3 | `nombre` | UNIQUE(`nombre`) |
| `dim2` | N2 | `dim1_id` (FK física), `clave`, `nombre`, `grupo_dim2_id` (FK) | UNIQUE(`clave`) global |
| `dim3` | N3 — hoja | `dim2_id` (FK física), `clave`, `nombre`, `grupo_dim3_id` (FK) | UNIQUE(`clave`) global |

Decisiones de modelado:

- **La ClaveCeCo colapsó**: la clave de reportes (101–105) ES la `clave` de
  Dim1 — ya no contamina nada porque la tabla es nuestra. La entidad
  vínculo (`SucursalCentroCosto`) desaparece; no hay `sucursal_id` ni
  read-port ni batch.
- **Table-per-level** con FK física `Restrict` al padre dentro del esquema
  (molde Almacén N2–N4), sin navegaciones. TODAS las FKs del módulo son
  internas al esquema.
- **Clave consolidada de reportes** `{Dim1}-{Dim2}-{Dim3}` (ej.
  `101-20PDMC-MCLC101`): computada, no almacenada.
- `HasCheckConstraint` en `estatus` (0–2) en las 5 tablas; `BaseEntity` +
  `IAuditable` en todas (Version/ETag, auditoría, soft-delete).

**Mapeo código ↔ UI** (la UI es contextual — el API habla Dim y no hornea
etiquetas):

| Código / tabla / API | UI configuración | UI documentos |
|---|---|---|
| `Dim1` / `dim1` / `nodoTipo=dim1` | "Dimensión 1" | (heredada — ver §8) |
| `GrupoDim2` / `grupos_dim2` | "Grupo dimensión 2" | — |
| `Dim2` / `dim2` / `nodoTipo=dim2` | "Dimensión 2" | (heredada — ver §8) |
| `GrupoDim3` / `grupos_dim3` | "Grupo dimensión 3" | — |
| `Dim3` / `dim3` | "Dimensión 3" | **"Máquina"** (el campo de captura) |

En configuración NO se usan las palabras Sucursal/Departamento/Máquina.

## 5. Reglas del catálogo

- **El padre es inmutable.** Editar permite clave, nombre y grupo; reubicar
  un nodo = baja lógica + alta nueva.
- **Baja lógica con cascada** (ADR-0049, traducido a este modelo):
  desactivar Dim1 desactiva sus Dim2 y Dim3 vivos en la misma transacción;
  desactivar Dim2 desactiva sus Dim3. Guardrail simétrico
  `CECO_PADRE_INACTIVO` en crear/reactivar hijos. Reactivar NO reactiva
  hijos. Nada se borra físicamente; los documentos históricos siguen
  resolviendo por Id.
- Los grupos (GrupoDim2/GrupoDim3) NO cascadan: desactivarlos solo los
  retira de la clasificación nueva.
- **Solo clasificar** (sin presupuesto en v1).

## 6. Permisos (ADR-0007, namespace `0000000c-*`)

| Código (post-refactor) | GUID | Uso |
|---|---|---|
| `centros_costo.catalogo.leer` | `0000000c-0001-…-0001` | Consultar árboles, niveles y grupos |
| `centros_costo.catalogo.administrar` | `0000000c-0001-…-0002` | CRUD de niveles y grupos |
| `centros_costo.asignaciones.administrar` | `0000000c-0002-…-0001` | Administrar el alcance usuario→máquinas (§7) |
| `centros_costo.dim3.leer-todos` | `0000000c-0003-…-0001` | Bypass de alcance: ver todas las máquinas sin asignación (Contabilidad) |

Revisión de los 4 (decisión 2026-07-16): solo `equipos.leer-todos` carga
vocabulario del modelo viejo → se renombra a `dim3.leer-todos` en el PR de
refactor (UpdateData del `codigo`; el GUID queda intacto → las asignaciones
de rol sobreviven). En la misma migración se corrigen las DESCRIPCIONES que
mencionan "departamento/subgrupo/equipo". El espejo FE llega en Fase C.

## 7. Asignación de alcance (Fase D) — modelo 2026-07-16

**El alcance se CONGELA en máquinas.** La tabla de asignación tiene UNA
columna de alcance: `(usuario_id, dim3_id)` — nada más. No se guardan
`dim1_id`/`grupo_dim2_id` ni pares nivel+nodo; no hay CHECK de
combinaciones; no existe el nodo polimórfico del diseño anterior.

- **El árbol de asignación tiene 5 niveles**: Dim1 → GrupoDim2 → Dim2 →
  GrupoDim3 → Dim3. Distinto del árbol de configuración (donde el grupo es
  un chip del registro): misma data, dos árboles. **Los grupos van acotados
  al padre**: "OPERACIONES bajo CONKAL" son solo las Dim2 de Conkal con ese
  grupo — un grupo nunca se asigna global.
- **Marcar un nodo superior es un ATAJO DE CAPTURA**: el sistema expande la
  regla a las hojas (Dim3) vivas bajo ese nodo y guarda ESAS filas. La
  regla se usa para calcular y se tira.
- **NO se re-evalúa en vivo** (decisión tomada): si Contabilidad crea una
  máquina nueva bajo un grupo "completo", el usuario NO la obtiene hasta
  que alguien vuelva a marcar.
- **Tri-estado calculado desde abajo, nunca almacenado**: un nodo se pinta
  *todo* si sus hojas vivas están todas asignadas, *parcial* si algunas,
  *ninguno* si ninguna — calculado hoja→arriba en la query, por usuario. Va
  en la barra resumen por dimensión y en cada renglón del árbol.
- **Consecuencia aceptada** (escrita a propósito): quitar una máquina a
  propósito se ve igual que faltar por desactualización — ambas pintan
  "parcial". El tri-estado no distingue intención.
- Contabilidad (con `dim3.leer-todos`) ve/gestiona todo sin asignación.
  Usuario sin asignaciones = lista vacía en el selector.
- **`usuario_id` es Guid lógico, sin FK a Identidad** (molde
  `facturacion.usuario_alcance`; las FKs del módulo son internas al
  esquema, §4) y el command NO valida que el usuario exista.
  **Consecuencia aceptada** (decisión 2026-07-16): si un usuario se borra
  en Identidad, sus filas de `asignaciones` quedan huérfanas y nada las
  limpia — mismo hueco que tiene `facturacion.usuario_alcance` en Cajas.
  Es inocuo (nadie las evalúa sin el usuario) pero existe; si algún día
  duele, la limpieza es un evento de Identidad, no una FK.

## 8. Conexiones con otros módulos (Fase E)

- En documentos (requisición, OC, salida) se captura **UN solo campo:
  "Máquina" (Dim3), por línea**. Centro de costo y máquina destino son lo
  mismo: la columna existente `centro_costo_id` pasa a contener `Dim3.Id`
  (validado por read-port público del módulo: existe + activa + en el
  alcance) y **la captura de `maquina_destino_id` se retira** (el campo
  cabecera queda deprecado, sin migración de datos).
- **Punto abierto (analizar documento por documento en Fase E)**: qué
  documentos MUESTRAN los niveles heredados (detalle, impresión, contexto)
  — no se cierra aquí.
- GUIDs históricos: no migran ni validan retroactivamente; display "No
  catalogado".
- Consumidor futuro: Contabilidad (outbox cuando exista). Reportes: Fase F,
  pendiente ([`02-plan-implementacion.md`](02-plan-implementacion.md) §7).

## 9. Carga inicial (Fase B) — migración de siembra SINGLE-SCHEMA

UNA migración en CentrosCosto (la de Compartido murió con el vínculo):
grupos_dim2 (6) → grupos_dim3 (44) → dim1 (5, con clave 101–105 y nombre
del Excel) → dim2 (57, typo `20PDPR` normalizado) → dim3 (361). Requisitos
anti-#501 vigentes ([`04-cuidados-infra.md`](04-cuidados-infra.md) §2):
idempotente (`ON CONFLICT DO NOTHING` sin target) con segunda corrida
PROBADA, GUIDs congelados `0000000c-{bloque}-…-{seq}`, guard final de
conteos (5/6/44/57/361) como mecanismo de ruido, probada en BD limpia,
fuente documentada (generador one-off en scratchpad). Ya **no** hay orden
entre contextos ni guard de vínculos ni pre-flight de prod: la siembra no
toca nada fuera de su esquema.

## 10. UI (Fases C y D)

- **Módulo 1 — Configuración**: pantalla única con árbol de 3 niveles
  (grupos como chips) + CRUD con modales; vocabulario "Dimensión 1/2/3" y
  "Grupo dimensión 2/3". NavCard + landing.
- **Módulo 2 — Asignación**: árbol de 5 niveles con tri-estado por renglón
  y barra resumen por dimensión; marcar cualquier nivel expande a máquinas.
- **Documentos**: un solo selector "Máquina" (combobox server-side, item
  `MCLC101 - Gantry (Corte · Conkal)`).

Detalle en [`05-frontend-diseno.md`](05-frontend-diseno.md).

## 11. Dependencias de plataforma pendientes

Ninguna. Con la separación total, el módulo no tiene stubs, puertos
externos ni Service Bus en v1.

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión (en la rama del PR #580, namespace `0000000b-*`). |
| 0.2 | 2026-07-14 | Set documental completo: namespace corregido a `0000000c-*`, molde Tesorería. |
| 0.3 | 2026-07-15 | CECO-PR2: referencia al ADR-0049 (cascada) y guardrail `CECO_PADRE_INACTIVO`. |
| 0.4 | 2026-07-16 | **Modelo Dim (separación total)**: Dim1 tabla propia, ClaveCeCo colapsada, muere vínculo/read-port/batch, nomenclatura Dim1/Dim2/Dim3 + GrupoDim2/GrupoDim3, mapeo código↔UI contextual, asignación congelada en máquinas con árbol de 5 niveles y tri-estado, campo único "Máquina" en documentos, siembra single-schema, permiso `dim3.leer-todos`. Ver levantamiento §7.4. |
| 0.5 | 2026-07-16 | CECO-PR6: §7 — `usuario_id` Guid lógico sin validación de existencia; consecuencia aceptada de filas huérfanas al borrar el usuario en Identidad (mismo hueco que Cajas). |
