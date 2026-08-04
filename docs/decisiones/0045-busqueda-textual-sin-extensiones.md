# ADR-0045: Búsqueda textual insensible a acentos sin extensiones de Postgres (`translate` nativo + primer `HasDbFunction`)

- **Estado**: Aceptada
- **Fecha**: 2026-06-22
- **Decisores**: Eduardo Paredes (owner), Claude (backend + frontend)
- **Etiquetas**: compras, catálogos, búsqueda, persistencia, infraestructura

> Relacionada con [ADR-0041](./0041-autorizacion-por-operacion-y-lectura-de-catalogos.md)
> (la lectura de este catálogo usa `compartido.catalogos.leer`, permiso que el
> requisitante ya posee), [ADR-0042](./0042-enriquecer-dtos-con-nombres-cross-modulo.md)
> (resolución id→etiqueta en el selector) y [ADR-0044](./0044-carga-unica-sap-proveedores-articulos.md)
> (volumen del catálogo de artículos).

## Contexto y problema

La línea de requisición solo permitía buscar artículo por **clave** (substring,
`a.Clave.Contains`). Operativamente eso obliga a memorizar claves; se necesita
buscar también por **nombre**, insensible a acentos y mayúsculas y matcheando en
cualquier posición ("papeleria" debe encontrar "Papelería de oficina genérica").

El selector ya pega a `GET /api/v1/catalogos/articulos` (protegido por
`compartido.catalogos.leer`, que el requisitante tiene — ADR-0041). El otro
endpoint con búsqueda por nombre, `GET /api/v1/datos-maestros/articulos`, está
protegido por `datos_maestros.articulos.gestionar` (permiso de administración):
reapuntar el selector ahí daría **403** al requisitante. Conclusión: la búsqueda
por nombre debe vivir en el endpoint de catálogos.

El problema de fondo es **cómo** hacer el match insensible a acentos. El camino
idiomático en Postgres es la extensión `unaccent` (o `pg_trgm` para índice). Pero
en **Azure Database for PostgreSQL Flexible Server** las extensiones requieren
estar allow-listed en el server parameter `azure.extensions` vía **bicep** — un
cambio de infraestructura. El repo ya esquivó esa dependencia una vez: la
migración `20260523154200_EstadoCuentaTcYParser` removió un `CREATE EXTENSION
pg_trgm` y lo dejó como `PLATFORM-TODO(<PgTrgmAllowList>)`, resolviendo el match
en C#. Queremos esta feature sin arrastrar infra.

## Drivers de la decisión

- Cero dependencia de infraestructura (no `azure.extensions`, no bicep, no
  `unaccent`, no `pg_trgm`) — encaja en un solo PR de aplicación.
- Folding de acentos **y** mayúsculas, match en cualquier posición.
- Reusar el endpoint y el permiso que el requisitante ya tiene (ADR-0041).
- Filtrado server-side con la paginación existente (no traer el catálogo al
  cliente — ~1.5k artículos hoy, ~13k en prod, ADR-0044).

## Opciones consideradas

1. **`translate` nativo (built-in) mapeado vía `HasDbFunction` + folding en la
   query** — *elegida*.
2. Extensión `unaccent` (+ allow-list `azure.extensions` en bicep + migración).
3. Extensión `pg_trgm` con índice GIN (+ misma allow-list de infra).
4. Columna normalizada persistida (`nombre_normalizado`) poblada por la app.
5. `ILIKE` a secas (case-insensitive pero **sin** folding de acentos).

## Decisión

**(1) `translate` nativo.** Se usa el built-in `pg_catalog.translate(text, from,
to)` para "aplanar" acentos: `translate(lower(x), 'áéíóúüñ', 'aeiouun')`. El
filtro aplica el folding sobre **ambos lados** (columna y aguja) con el **mismo
mapa**, previo `lower()`:

```
translate(lower(a.nombre), 'áéíóúüñ', 'aeiouun')  LIKE
    %translate(lower(@nombre), 'áéíóúüñ', 'aeiouun')%
```

`translate` es built-in de `pg_catalog` — **no** requiere extensión ni allow-list.

**Primer `HasDbFunction` del repo.** Para invocar `translate` desde LINQ se
registra un método estático type-safe (`PostgresFunctions.Translate`) mapeado en
`CompartidoDbContext.OnModelCreating` con `HasDbFunction(...).HasName("translate")
.HasSchema("pg_catalog")`. El schema se declara explícito porque el default del
modelo es `compartido` (sin él, EF generaría `compartido.translate`, que no
existe). Queda asentado el patrón para que otros módulos mapeen built-ins igual.

**`clave` y `nombre` excluyentes, `clave` con precedencia.** El backend aplica el
que venga con valor, nunca ambos; si llegan los dos, gana `clave` (comportamiento
histórico intacto). El frontend (`ArticuloSelector`, dos cajas) garantiza que
solo una viaje, deshabilitando y limpiando la otra al escribir.

**Folding `ñ→n` intencional.** El mapa colapsa `ñ` a `n`: "munoz" encuentra
"Muñoz". Es una decisión de tolerancia de búsqueda, no de correctitud
ortográfica; aceptada explícitamente.

## Consecuencias

**Positivas**

- Sin dependencia de infra: la feature entra en un PR de aplicación, sin tocar
  bicep ni el server parameter `azure.extensions`.
- El requisitante busca por nombre con el permiso que ya tiene (`compartido
  .catalogos.leer`); no hay 403 ni endpoint nuevo.
- Filtrado y paginación server-side; el cliente nunca baja el catálogo completo.
- Patrón `HasDbFunction` documentado y reutilizable para futuros built-ins.

**Negativas / trade-offs**

- **LIKE sin índice → sequential scan.** El `translate(...) LIKE '%...%'` no usa
  índice. **Aceptado** al volumen actual (~1.5k artículos; ~13k en prod) con
  `limit` capado a 200. El índice (`pg_trgm` GIN sobre la expresión normalizada)
  queda **diferido** y atado al mismo allow-list de infra que ya rastrea
  `PLATFORM-TODO(<PgTrgmAllowList>)` — cuando se habilite `azure.extensions`, se
  agrega el índice y este filtro lo aprovecha sin cambios de código.
- El mapa de acentos es **manual y acotado** al set español en minúsculas
  (`áéíóúüñ`). Caracteres fuera del mapa no se foldean. Suficiente para el
  catálogo; ampliable en un solo lugar (`CatalogosEndpoints.AcentosOrigen/Destino`).
- `ñ→n` puede sorprender a quien espere distinción estricta; documentado arriba.

## Descartadas

- **`unaccent` (Opción 2) / `pg_trgm` (Opción 3):** son la vía idiomática, pero
  exigen allow-list en `azure.extensions` (bicep) — cambio de infra fuera del
  scope, exactamente lo que el repo ya difirió con `pg_trgm`
  (`PLATFORM-TODO(<PgTrgmAllowList>)`). Se retoman cuando se aborde ese allow-list.
- **Columna normalizada persistida (Opción 4):** requiere migración de schema +
  repoblar en cada alta/edición + mantener sincronía. Sobre-ingeniería para el
  volumen actual; `translate` en query da el mismo resultado sin columna nueva.
- **`ILIKE` a secas (Opción 5):** resuelve mayúsculas pero **no** acentos
  ("papeleria" no encontraría "Papelería"). No cumple el requisito.

## Notas de implementación

- **Función + mapeo:** `backend/src/SharedKernel/Infrastructure/Persistence/PostgresFunctions.cs`
  (método `Translate`); registro en
  `backend/src/Compartido/Infrastructure/Persistence/CompartidoDbContext.cs`
  (`OnModelCreating`). **No** requiere migración (no hay columna nueva ni
  extensión; `translate` es built-in).
- **Filtro:** `backend/src/Api/Endpoints/Catalogos/CatalogosEndpoints.cs`
  (`GET /api/v1/catalogos/articulos`, parámetro `nombre`; constantes
  `AcentosOrigen`/`AcentosDestino` en un solo lugar). Permiso y paginación sin
  cambios.
- **Frontend:** `ListarArticulosFiltros.nombre` + `buildArticulosPath` en
  `frontend/src/features/catalogos/api/hooks.ts`; segunda caja excluyente en
  `frontend/src/components/erp/selectors/ArticuloSelector.tsx` (prop
  `permitirBusquedaPorNombre`, default `true`; activa en los 6 consumidores). Las
  cajas viven en el Popover (portal, ancho fijo), independientes del contenedor.
- **Diferido:** índice `pg_trgm` sobre la expresión normalizada — atado a
  `PLATFORM-TODO(<PgTrgmAllowList>)` en
  `backend/src/CuentasPorPagar/Infrastructure/Persistence/Migrations/20260523154200_EstadoCuentaTcYParser.cs`.

---

## Addendum (2026-06-25): patrón extendido a `ProveedorSelector` + códigos case-insensitive

El mismo patrón de **dos cajas excluyentes clave/nombre** y el **folding `translate`
case + acento-insensitive** se extiende a la **búsqueda de proveedor** (defect reportado:
el `ProveedorSelector` solo buscaba por clave). **La decisión, drivers y opciones de este
ADR no cambian** — sólo se amplía la superficie:

- **Búsqueda por nombre de proveedor.** `GET /api/v1/catalogos/proveedores` gana el
  parámetro `nombre` (excluyente con `clave`, `clave` precede), que matchea sobre
  **razón social OR nombre comercial** con el mismo folding
  (`translate(lower(...), 'áéíóúüñ', 'aeiouun')`), con null-guard en `NombreComercial`.
- **Códigos vs texto libre.** `clave` y `rfc` (códigos, sin acentos) usan `lower()` en
  ambos lados (case-insensitive sin folding de acentos); el folding `translate` completo
  se reserva para texto libre (nombre / razón social / descripción). Esto corrige además
  el comportamiento **case-sensitive histórico** de la búsqueda por clave, que este ADR
  había dejado "intacto".
- **Alcance del case-insensitive.** Aplica a los endpoints de selector
  (`CatalogosEndpoints`) y a las queries admin de Datos Maestros
  (`ListarProveedoresQuery` / `ListarArticulosQuery`).
- **Mapa de acentos centralizado.** `AcentosOrigen/Destino` pasan de `private const` en
  `CatalogosEndpoints` a `PostgresFunctions.AcentosOrigen/Destino` (SharedKernel) como
  **fuente única** compartida por endpoints de catálogos y queries admin.
- **Frontend.** `ProveedorSelector` adopta las dos cajas (prop
  `permitirBusquedaPorNombre`, default `true`), espejo de `ArticuloSelector`;
  `ListarProveedoresFiltros.nombre` + `buildProveedoresPath` (clave else nombre).

Sin ADR nuevo: es la misma decisión técnica (built-in `translate`, sin extensiones,
server-side, excluyente con precedencia de clave) aplicada a un segundo selector.
