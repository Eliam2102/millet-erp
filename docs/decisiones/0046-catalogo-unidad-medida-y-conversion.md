# ADR-0046: Catálogo de unidad de medida, conversión en dos niveles y validación de decimales por unidad

- **Estado**: Aceptada
- **Fecha**: 2026-06-22
- **Decisores**: Eduardo Paredes (owner), Claude (backend + frontend)
- **Etiquetas**: catálogos, almacén, compras, datos-maestros, inventario, workstream

> Ancla del workstream de **unidad de medida**. Se implementa en **4 etapas**;
> este PR entrega solo la **Etapa 1a** (catálogo + administración). Las etapas
> posteriores referencian este ADR.

## Contexto y problema

Hoy la unidad de medida es un **string libre `varchar(20)`** en
`compartido.articulos.unidad_medida_default` y en los snapshots de las líneas
(`requisicion_lineas`, `orden_compra_lineas`, `lineas_movimiento`,
`lineas_devolucion_proveedor`). Consecuencias observadas:

- **No hay validación de decimales por unidad:** "pieza" acepta `1.5` porque la
  precisión es global (`numeric` con 4-5 decimales) y nada la liga a la unidad.
  Este es el bug que dispara el workstream.
- **Datos sucios heredados de SAP:** en dev (1,531 artículos) conviven 24
  variantes — `PZA` (1192), `PIEZA`, `PZA.`, `pza`, `PPZA`; `LITRO`/`L`;
  `METRO`/`M`; y empaques `PAR`, `CAJA`, `PAQ`, `ROLLO`, `TAMBO`, `CUBETA`,
  `GALON` (~16% sin mapeo claro a una unidad base). En prod se estiman ~12,984.
- **No existe noción de conversión** compra↔inventario: un producto que se
  compra en `TAMBO`/`CUBETA`/`GALON` y se da salida en `L`/`KG` no tiene factor
  en ningún lado (el `IArticuloReadPort` menciona "conversión" pero no se
  implementó).

Se necesita un **catálogo** con dimensión, factor de conversión y decimales
permitidos, sin romper los flujos existentes (las líneas siguen siendo string
en esta etapa).

## Drivers de la decisión

- Eliminar el string libre como fuente de verdad → catálogo administrable.
- Distinguir la conversión **universal** (constantes físicas) de la
  **variable por producto** (empaque), que tienen dueños distintos.
- No romper líneas/movimientos existentes: migración incremental, no big-bang.
- Reusar el patrón de catálogos editables (Incoterms/CondicionesPago) y su
  permiso granular ya provisto (`catalogos.unidades-medida.gestionar`).

## Decisión

### 1. Catálogo `compartido.unidades_medida` (Etapa 1a — este PR)

Entidad `UnidadMedida`: `codigo` (único, inmutable), `nombre`, `dimension`
(enum `Conteo/Peso/Volumen/Longitud/Tiempo`), `factor_a_base` (decimal &gt; 0),
`decimales` (0..6), `es_base` (bool), `estatus` (`EstatusCatalogo`, igual que
los demás catálogos editables). PK surrogate `Guid`
(consistencia con `BaseEntity`: auditoría, concurrencia, soft-delete; y el FK
futuro referencia el `Id`, no el código, permitiendo corregir códigos sin
romper referencias). Seed de 10 unidades (5 dimensiones).

### 2. Conversión en DOS niveles

- **Nivel 1 — catálogo (este ADR):** conversiones **estándar y universales**
  dentro de una dimensión vía `dimension` + `factor_a_base` (G↔KG, ML↔L,
  CM/MM↔M). Son constantes físicas iguales para todos los productos. **No hay
  conversión entre dimensiones** (no se convierte peso a volumen).
- **Nivel 2 — artículo (Etapa 3, futura):** el **empaque variable** (1 cubeta =
  19 L de *este* producto; 1 caja = 12 PZA) depende del producto, **no** es una
  constante universal → vive en el **artículo** (unidad de compra + factor de
  empaque), **NO** en el catálogo. Por eso `CAJA`, `CUBETA`, `TAMBO`, `GALON`,
  `PAR`, `ROLLO` **no** se siembran como unidades del catálogo.

### 3. FK desde artículo + snapshots string en líneas (Etapa 1b, futura)

`articulos.unidad_medida_id` → `unidades_medida.Id`. Las **líneas conservan el
snapshot string** (no se les pone FK): son fotografías históricas de lo
capturado y no deben mutar si el catálogo cambia.

### 4. Estado transitorio nullable + legacy (Etapa 1b, futura)

Al migrar el string a FK, los artículos cuyo valor legacy **no mapea** a una
unidad base (los ~16% de empaque/variantes sucias) quedan con
`unidad_medida_id = NULL` y se **preserva el string** en una columna
`unidad_medida_legacy`. El FK arranca **nullable** durante la transición; se
endurece a `NOT NULL` cuando la reconciliación (y el empaque variable de Etapa
3) cubran el universo.

### 5. Validación de decimales por unidad (Etapa 2)

La captura de cantidad valida los `decimales` de la unidad del artículo:
"pieza" (0 decimales) rechaza `1.5` y acepta `2`; "kg" (3) acepta `1.250` y
rechaza `1.2505`.

**Implementación (decisión):**

- **Regla pura, por redondeo-igualdad:** una cantidad cabe en `n` decimales sii
  `decimal.Round(cantidad, n) == cantidad` — exacto y robusto ante ceros a la
  derecha (`1.50` cabe en 1 decimal) sin contar la representación string. Vive
  en `SharedKernel.Application.UnidadesMedida.DecimalesUnidad`.
- **Puerto compartido** `IUnidadMedidaReadPort` (`SharedKernel.Application`):
  resuelve en batch `articuloId → decimales de su unidad` (un round-trip); el
  adapter (`Compartido.Infrastructure`) hace el JOIN
  `articulos ⨝ unidades_medida`.
- **Guard de aplicación compartido** `IDecimalesUnidadGuard`, agnóstico de
  módulo, invocado en el handler (regla de negocio con I/O → [ADR-0018](0018-validacion-fluentvalidation.md);
  no es validación estructural de FluentValidation, que conserva solo `> 0`).
  Lanza `BusinessRuleException("CANTIDAD_DECIMALES_EXCEDE_UNIDAD")` (HTTP 422)
  incluyendo en el mensaje cuántos decimales permite la unidad.
- **FK NULL → no valida** (skip): los artículos legacy sin unidad de catálogo
  (~8% tras 1b) caen al comportamiento previo hasta reconciliarse / Etapa 3.
- **Sin feature flag:** la validación queda activa al mergear.

Se entregó en tres PRs: **2a** (núcleo + Compras: RQ y OC), **2b** (Almacén
reusa el mismo guard, 10 handlers), **2c** (frontend advisory).

**Estado de la validación de decimales:** el **backend es AUTORITATIVO y completo**
(Compras + Almacén; valida y rechaza en los 7 puntos de captura). El **frontend es
ADVISORY** (feedback temprano, no es quien rechaza): cubre **RQ, OC, salida vs RQ,
vale, recepción, devolución a proveedor y MatRev** — step por unidad + validación
por unidad (Zod en RQ/OC, guard de submit en Almacén), con **fallback global**
cuando la unidad no resuelve (FK null / string sin match — nunca bloquea). El
`unidadMedidaId` en los forms es **solo-frontend: NO se envía al backend** (el
contrato de los comandos no cambia); las capturas heredadas matchean el string de
unidad contra el código del catálogo.

**Follow-ups conocidos** (advisory frontend pendiente; el backend ya valida
autoritativo, así que no hay hueco funcional):

- **Conteo** (`CapturaConteoPage`) y **devolución interna 8.A**
  (`AplicarDevolucionInternaSheet`): su punto de captura no tiene la unidad (el
  DTO trae solo `articuloId` / un GUID de línea de salida tecleado), así que el
  frontend no puede resolver decimales sin **enriquecer el DTO** (cambio de
  backend). Diferidos a un follow-up.
- **Toast con `traceId` rotulado "Código:"**: bug de UX **general y
  pre-existente** (todos los errores de negocio, todos los módulos) — ajeno a la
  Etapa 2; corregir el toast genérico en un follow-up aparte.

### 6. Guardrail de reconfiguración (Etapa 1a, con dato pendiente)

`dimension` y `factor_a_base` **no** deben cambiarse si la unidad ya está en
uso (reinterpretaría cantidades históricas). El dominio
(`UnidadMedida.ReconfigurarConversion(..., estaEnUso)`) ya lanza
`UNIDAD_MEDIDA_EN_USO`. En 1a **nadie referencia** la unidad (el FK es 1b), así
que el handler pasa `estaEnUso = false` con
`PLATFORM-TODO(<UnidadMedidaEnUso>)`: en 1b se reemplaza por una consulta de
referencias. `nombre` y `decimales` son siempre editables; el `estatus` se
cambia con desactivar/reactivar.

### Etapas del workstream

| Etapa | Alcance | Estado |
|---|---|---|
| **1a** | Catálogo `unidades_medida` + CRUD + seed + guardrail (sin dato de uso) | ✅ Hecho |
| **1b** | FK `articulos.unidad_medida_id` (nullable + legacy) + reconciliación de datos SAP (runbook) + wireo del guardrail "en uso" + selector de unidad en artículo | ✅ Hecho |
| **2** | Validación de decimales por unidad — **backend autoritativo completo** (Compras + Almacén) + **frontend advisory** (RQ, OC, salida vs RQ, vale, recepción, dev. a proveedor, MatRev) | Backend ✅ · Frontend advisory parcial (follow-ups: conteo, dev-interna 8.A, toast) |
| **3** | Empaque variable en artículo (unidad de compra + factor) | Pendiente |
| **4** | Conversión compra↔inventario en movimientos de almacén | Pendiente |

## Consecuencias

**Positivas**

- El catálogo queda como fuente de verdad administrable; el string libre se
  retira por etapas sin romper líneas/movimientos.
- La separación de dos niveles evita meter empaques específicos de producto en
  un catálogo cross-empresa (donde su factor no tendría sentido único).
- Reusa el patrón y permiso de catálogos editables; cero infra nueva.

**Negativas / trade-offs**

- El guardrail "en uso" no es ejecutable end-to-end hasta 1b (cubierto hoy por
  test de dominio); riesgo acotado porque nadie referencia la unidad aún.
- La reconciliación de ~16% de valores sin mapeo claro (empaques) queda
  diferida a 1b/3 y requiere decisión de negocio por artículo.
- Como los otros catálogos granulares, el CRUD lo tiene **super-admin** (el rol
  `admin-catalogos` del bootstrap matchea `compartido.catalogos*`, no
  `catalogos.*`); no se cambian grants en este PR.

## Descartadas

- **Enum de C# para la unidad:** cerrado a cambios sin deploy; no admite que el
  cliente dé de alta unidades ni lleva factor/decimales/dimensión.
- **Meter el empaque (CAJA/CUBETA/GALON) como unidades del catálogo:** su factor
  depende del producto (1 caja = 12 PZA *de este artículo*), no es universal →
  rompería la semántica de `factor_a_base`. Va al artículo (Etapa 3).
- **FK NOT NULL desde el día 1:** imposible mientras ~16% de artículos no mapean
  a una unidad base; de ahí el transitorio nullable + legacy.
- **Validar decimales ya en 1a:** requiere el FK artículo→unidad para conocer
  los decimales aplicables en cada línea; se hace en Etapa 2 sobre 1b.

## Notas de implementación

- **Backend:** `Catalogos/Domain/UnidadMedida.cs` + `DimensionUnidad.cs`;
  `ConfigureUnidadMedida` + seed en `CompartidoDbContext`; migración
  `UnidadMedidaCatalogo`; `Compartido/Application/Catalogos/UnidadesMedida/`
  (Crear/Actualizar/Desactivar); GET en `CatalogosOcEndpoints`
  (`compartido.catalogos.leer`), mutaciones en `CatalogosEditablesEndpoints`
  (`catalogos.unidades-medida.gestionar`).
- **Frontend:** `modules/catalogos` (api/hooks `unidades-medida.ts`, schema,
  `UnidadesMedidaPage`, `SheetNuevaUnidadMedida`) + ruta
  `/admin/catalogos/unidades-medida` + card en `admin.ts`.
- **Tests:** `Catalogos.UnitTests` (dominio + guardrail) +
  `Api.IntegrationTests/Catalogos` (seed + CRUD + dup 409).
- **Etapa 1b (hecho):** FK `articulos.unidad_medida_id` (nullable + RESTRICT),
  el guardrail `UnidadMedidaEnUso` ya consulta el FK (se retiró el
  `PLATFORM-TODO`), y la reconciliación re-ejecutable vive en
  `docs/operacion/reconciliacion-unidades.md`. El alta/edición de artículo usa
  el selector del catálogo. Los empaques variables (CAJA/CUBETA/GALON…) y
  `PPZA`/`PSP` quedan con FK NULL + legacy para la Etapa 3.
- **Etapa 2a (núcleo + Compras):** `SharedKernel.Application.UnidadesMedida`
  (`IUnidadMedidaReadPort`, `DecimalesUnidad`, `IDecimalesUnidadGuard` +
  `DecimalesUnidadGuard`); adapter
  `Compartido.Infrastructure.PublicAdapters.UnidadMedidaReadAdapter` + registro
  en DI del `Api`. El guard se invoca en los **6 handlers** de captura de
  Compras (RQ: agregar/actualizar línea, registrar recepción; OC: línea manual,
  actualizar línea, desde-RQ). Tests: `SharedKernel.UnitTests` (validador +
  guard) + `Compras.IntegrationTests` (RQ E2E: pieza→422, entero→201, kg 3
  decimales, FK NULL→permite). Almacén (2b) y frontend (2c) pendientes.
