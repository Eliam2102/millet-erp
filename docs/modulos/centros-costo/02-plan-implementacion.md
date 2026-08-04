# Plan de implementación — Módulo Centros de Costo (`Millet.CentrosCosto`)

> **Versión:** 0.2 · **Fecha:** 2026-07-16
> **Basado en:** [`00-levantamiento.md`](00-levantamiento.md) v0.2 y [`01-diseno.md`](01-diseno.md) v0.4.

---

## 0. Cómo leer

Fases ordenadas por dependencia; cada fase = 1 a 3 PRs. La Fase A (catálogo
backend, modelo anterior) está **mergeada** (#591/#594/#599); el cambio de
modelo Dim (levantamiento §7.4) inserta la **Fase R (refactor)** antes de
retomar la secuencia. El detalle por PR vive en
[`03-pr-breakdown.md`](03-pr-breakdown.md); el frontend en
[`06-frontend-plan-implementacion.md`](06-frontend-plan-implementacion.md).

---

## 1. Resumen ejecutivo

- **8 PRs backend + 5 PRs frontend** (el refactor suma uno; la siembra de
  Compartido murió con la separación).
- El PR #603 (siembra cross-schema del modelo anterior) se **cerró sin
  mergear** — la siembra se rehace single-schema tras el refactor.
- La complejidad restante está en el **refactor de nomenclatura** (mecánico
  pero ancho), la **siembra** (single-schema, ya sin orden entre contextos)
  y el **alcance** (expansión a máquinas + tri-estado calculado — sin molde
  exacto en el repo).
- La **Fase F (reportes)** queda REGISTRADA pero pendiente, con sus 3
  preguntas abiertas (§7) — se aborda al cerrar el módulo.

## 2. Prerrequisitos — audit del repo (2026-07-16)

| Prerrequisito | Estado |
|---|---|
| Fase A mergeada (esquema, CRUD+cascada, jerarquía/listas/búsqueda) | ✅ #591/#594/#599 (con nombres del modelo anterior) |
| Molde table-per-level / árbol lazy / selector server-side | ✅ existen (Almacén/ADR-0047, ArticuloSelector) |
| Molde del alcance nuevo (expansión + tri-estado) | ❌ **no hay** — se diseña en PR-6 (la pieza de más riesgo) |
| Excel fuente verificado (5/6/44/57/361 + typo `20PDPR`) | ✅ 2026-07-15 |
| Tablas del módulo vacías en prod/CI (el refactor renombra sin datos) | ✅ #603 nunca mergeó; **dev local requiere runbook de limpieza previo** |
| Pre-flight de prod en `compartido.sucursales` | 🗑️ obsoleto — la siembra ya no la toca |

## 3. Fases

### Fase A — Catálogo backend ✅ MERGEADA (modelo anterior)
#591 cimiento · #594 CRUD con cascada (ADR-0049) · #599 jerarquía, listas
y búsqueda. Nombres pre-Dim; los corrige la Fase R.

### Fase R — Refactor al modelo Dim (PR-4 · M) — NUEVA
Renombres Dim1/Dim2/Dim3 + GrupoDim2/GrupoDim3 (entidades, tablas,
DbContext, commands, queries, endpoints, tests), colapso del vínculo
(`SucursalCentroCosto` → `Dim1` con Clave+Nombre), **borrado** del
read-port/adapter/batch, migración de renombrado (tablas vacías en
prod/CI), renombre del permiso `equipos.leer-todos` → `dim3.leer-todos`
(GUID intacto) y ajuste de las referencias del ADR-0049. **Entrega:** el
código habla el idioma del diseño. **Riesgo:** bajo-medio (ancho pero
mecánico; la migración opera sobre tablas vacías).

### Fase B — Siembra single-schema (PR-5 · S)
UNA migración en CentrosCosto (la de Compartido murió): 5/6/44/57/361 con
guard final. Sin orden entre contextos, sin pre-flight. **Riesgo:** bajo
(los requisitos anti-#501 vigentes, ver [`04-cuidados-infra.md`](04-cuidados-infra.md) §2).

### Fase C — Admin FE, Módulo 1 (FE-PR1 · M, FE-PR2 · M)
Árbol de configuración (3 niveles, grupos como chips, vocabulario
"Dimensión N") + CRUD con modales. Sin cambios de fondo vs el plan previo.

### Fase D — Asignación, Módulo 2 (PR-6 · M/L, FE-PR3 · M/L) — REDISEÑADA
Backend: tabla `(usuario_id, dim3_id)`, comando de marcado con **expansión
a máquinas** (la regla se calcula y se tira), query del **árbol de 5
niveles con tri-estado calculado** desde las hojas, sin re-evaluación en
vivo. FE: árbol con tri-estado por renglón + barra resumen por dimensión.
**Riesgo:** medio-alto — es la pieza sin molde; el tri-estado por usuario
sobre 361 hojas es barato hoy pero el diseño de la query debe ser sano.

### Fase E — Selector en consumo (PR-7 · M, PR-8 · S, FE-PR4 · M, FE-PR5 · M)
UN campo "Máquina" por línea en salidas/vale (PR-7 + FE-PR4, retirando la
captura de `maquina_destino_id`) y en la línea de RQ (PR-8 + FE-PR5).
CC-G4 abierto: qué documentos muestran los niveles heredados — se analiza
documento por documento aquí.

### Fase F — Reportes — PENDIENTE (registrada, sin PRs)
Se aborda después de cerrar el módulo. **Preguntas abiertas** (no
resolverlas de pasada):
1. **¿De quién es el reporte?** CeCo tiene el árbol pero no los montos;
   Compras/Almacén tienen los montos pero no el árbol.
2. **¿Respeta el alcance del usuario, o Contabilidad ve todo con
   `dim3.leer-todos`?**
3. **¿Qué mide?** ¿Importes, cantidades, documentos? ¿Requisiciones
   (intención) o recepciones (gasto real)? Argumento de Eduardo a tener
   enfrente: *"en la requisición propones, pero no alimentas"*.

## 4. Orden y valor incremental

```
A ✅ → R (PR-4) → B (PR-5) → C (FE-PR1→FE-PR2)
                           ↘ D (PR-6→FE-PR3) → E (PR-7→PR-8→FE-PR4→FE-PR5) → F (pendiente)
```

R va primero que todo lo nuevo (nadie construye sobre nombres muertos); B
da los datos reales; C y D paralelizables tras B; D antes de E (el
selector nace filtrado por alcance).

## 5. Cronograma sugerido (1 dev backend + 1 dev frontend)

| Semana | Backend | Frontend |
|---|---|---|
| 1 | PR-4 (refactor) → PR-5 (siembra) | — |
| 2 | PR-6 (alcance) | FE-PR1 |
| 3 | PR-7 | FE-PR2 |
| 4 | PR-8 | FE-PR3 |
| 5 | buffer | FE-PR4 → FE-PR5 |

## 6. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| El refactor deja residuos del vocabulario viejo (docs, permisos, tests) | Checklist en el PR-4 del breakdown; grep de `SucursalCeCo\|Subgrupo\|Equipo` como gate del PR |
| La migración de renombrado corre sobre un dev con datos del modelo viejo | Runbook de limpieza de millet_dev ANTES del gate (siembra local nunca-mergeada + 2 registros de history) |
| El alcance sin molde se sobre-diseña | El diseño §7 ya fija el contrato (una columna, expansión, tri-estado calculado); el PR-6 no decide semántica, solo implementa |
| Fase F se cuela "de pasada" en E | Las 3 preguntas de §3-F quedan registradas como gate propio; ningún PR de E incluye reportes |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
| 0.2 | 2026-07-16 | Modelo Dim: Fase R insertada, siembra single-schema (PR de Compartido eliminado), Fase D rediseñada (expansión + tri-estado), Fase F registrada con sus 3 preguntas, renumeración PR-4..PR-8. |
