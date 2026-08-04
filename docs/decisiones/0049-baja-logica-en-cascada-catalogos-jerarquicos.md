# ADR-0049: Baja lógica en cascada para catálogos jerárquicos

- **Estado**: Aceptada
- **Fecha**: 2026-07-15
- **Decisores**: Eduardo Paredes, Victor
- **Etiquetas**: dominio, catálogos, jerarquías, concurrencia

## Contexto y problema

Al desactivar un nodo de un catálogo, el repo tenía hasta hoy una sola
semántica: **bloquear** la baja si el nodo está "en uso" (guardrails
`UBICACION_EN_USO_CON_SALDO`, `UNIDAD_MEDIDA_EN_USO`,
`CATEGORIA_ARTICULO_EN_USO`). Ningún comando desactivaba hijos.

El módulo Centros de Costo introduce un catálogo **jerárquico organizacional**
(Dim1 → Dim2 → Dim3, planta → área → máquina) donde la operación natural es la inversa:
dar de baja una rama completa ("cerró la planta", "desapareció el
departamento"). Bloquear obligaría a desactivar cientos de hojas a mano en
orden inverso; borrar físicamente rompería los documentos históricos que
referencian los nodos. Se necesita una segunda semántica y un criterio claro
de cuándo aplica cada una.

## Decisión

Coexisten **dos semánticas de baja lógica en catálogos**, elegidas por la
naturaleza de la relación padre-hijo:

1. **Guardrail-bloqueo** (la existente): cuando los "hijos" representan
   **uso o existencias** — saldos, documentos, asignaciones. Desactivar se
   rechaza con `BusinessRuleException` (422) y un código `*_EN_USO*`. El
   usuario debe resolver el uso primero.
2. **Cascada lógica** (nueva, este ADR): cuando los hijos son **organización
   pura del mismo catálogo** — la rama es un todo conceptual y dejar hijos
   "vivos" bajo un padre inactivo sería un estado inválido. Desactivar el
   padre desactiva su subárbol vivo.

### Mecánica canónica de la cascada

- **Un solo `SaveChanges`** (una transacción): se cargan los descendientes
  con estatus ≠ Inactivo (todos los niveles, en el mismo handler — sin
  encadenar commands) y se desactivan junto con el padre. O cae el subárbol
  completo o nada.
- **No-op idempotente**: si el padre ya está inactivo, la operación regresa
  sin tocar nada (los hijos ya fueron cascadeados en la primera baja).
- **Conteos en la respuesta** (`XDesactivados` por nivel): auditoría, tests
  y el aviso de la UI.
- **Reactivar NO cascadea**: revive solo el nodo; los descendientes se
  reactivan uno a uno, con intención. La baja masiva es reversible, pero no
  en bloque.
- **Guardrail simétrico `*_PADRE_INACTIVO`** en Crear y Reactivar de hijos:
  sin él, la cascada sería burlable y el árbol tendría ramas vivas bajo
  troncos muertos.
- **Concurrencia (ADR-0012)**: el If-Match del cliente protege al padre; los
  descendientes quedan cubiertos por su concurrency token — un editor
  concurrente de un hijo produce 409 en una de las dos transacciones, y se
  reintenta.
- **Nada se borra**: las FKs quedan `Restrict` y los documentos históricos
  siguen resolviendo por Id; el inactivo solo deja de ofrecerse para uso
  nuevo.

### Cuándo NO aplica cascada

Las **dimensiones de clasificación** (etiquetas con Id referenciadas por
nodos, ej. Grupo/Subgrupo del CeCo) no cascadan al desactivarse: los nodos
que las referencian siguen válidos; la dimensión inactiva solo deja de
ofrecerse para clasificación nueva.

## Consecuencias

**Positivas**: la baja de una rama es una operación (no cientos), atómica y
auditada; el criterio uso-vs-organización es objetivo y extensible (plan de
cuentas de Contabilidad, futuras jerarquías).

**Negativas**: dos semánticas conviven y hay que elegir conscientemente
(este ADR es el criterio); la cascada carga el subárbol en memoria — aceptable
en catálogos (cientos de nodos), no apto para jerarquías de millones de filas
(ahí se evaluaría `ExecuteUpdate` con las mismas garantías).

## Primera implementación

Centros de Costo, CECO-PR2; rutas actualizadas al modelo Dim en CECO-PR4:
`Dim1Commands.DesactivarDim1Handler` (cascada a dos niveles) y
`Dim2Commands.DesactivarDim2Handler`, con tests en
`Api.IntegrationTests/CentrosCosto/BajaCascadaTests.cs`. Referencia de
diseño: `docs/modulos/centros-costo/01-diseno.md` §5 (jerarquía
Dim1 → Dim2 → Dim3 — la traducción de nombres del modelo original vive en
el Rev. de ese doc).
