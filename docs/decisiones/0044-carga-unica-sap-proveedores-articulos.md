# ADR-0044: Carga única SAP → millet_erp (proveedores + artículos) vía procedimiento SQL documentado

- **Estado**: Aceptada
- **Fecha**: 2026-06-18
- **Decisores**: Eduardo Paredes (owner), Victor, Claude
- **Etiquetas**: datos-maestros, migracion-de-datos, sap, compartido, runbook

## Contexto y problema

Hay que poblar `compartido.proveedores` y `compartido.articulos` con el maestro
real de SAP Business One (`OCRD`/`OITM`) para arrancar el back-office. Es una
**migración de datos de una sola vez** (con re-cargas puntuales de correcciones),
no una integración continua: SAP sigue siendo la fuente mientras dure el
Strangler Fig, pero estas tablas no se sincronizan en vivo en esta fase.

Existió un diseño previo de **herramienta de importación in-app** (feature con
upload de layout, dry-run y upsert por dominio) documentado en
[`docs/modulos/importacion-proveedores/`](../modulos/importacion-proveedores/)
(Fases 1–3, PR #410). Esa propuesta **no fue aceptada** (no se mergeó a `main`):
construir una herramienta desplegada para una carga que ocurre una vez es
sobre-ingeniería. Falta decidir y documentar el procedimiento real que sí se
ejecutó.

## Drivers de la decisión

- Es una **carga única** (+ correcciones), no un flujo recurrente → no amerita
  software desplegado, RBAC, endpoints ni UI.
- **Reproducibilidad y auditoría**: el equipo debe poder repetir/entender la carga
  sin que el conocimiento viva solo en descargas locales y memoria.
- **Seguridad de datos**: el maestro trae PII (nombres/RFC/emails) → los CSV no se
  versionan; credenciales fuera de git.
- **Reversibilidad**: en entornos con rol de mínimo privilegio (sin `DELETE`) hace
  falta una red de seguridad para no dejar datos a medias.
- **Core-only**: no introducir columnas/catálogos nuevos en esta fase (el diseño
  previo de PR #410 proponía `grupo_proveedor`/secuencias/bandeja de excepciones;
  aquí no).

## Opciones consideradas

1. **Herramienta de importación in-app** (el diseño previo, PR #410): feature con
   upload, dry-run y upsert por dominio + catálogos/columnas nuevas.
2. **Procedimiento SQL documentado** (runbook): extracción SAP → CSV → staging →
   vista de transformación → dry-run → INSERT transaccional autoverificable →
   verificación. — *elegida*.
3. **ETL externo / herramienta de terceros**: pipeline en una tool dedicada.

## Decisión

**(2) Carga única vía procedimiento SQL documentado, core-only.** El flujo es:

1. **Extracción** desde SAP (read-only) con `sqlcmd` → CSV UTF-8 bien citado.
2. **Staging** en un schema `staging_carga` (owner = el rol de carga) + `\copy`.
3. **Vista de transformación** que aplica las derivaciones (misma lógica para
   dry-run e INSERT).
4. **Dry-run (STOP)**: conteos + checks críticos; no se inserta hasta revisar.
5. **INSERT transaccional autoverificable**: `BEGIN … INSERT … guard DO $$…$$ …
   COMMIT`; si el guard detecta conteos que no cuadran, lanza excepción y la txn
   aborta (el `COMMIT` no commitea nada). Es la red de seguridad cuando el rol no
   tiene `DELETE`.
6. **Verificación** post-commit + spot-check sin PII.

Procedimiento completo y reproducible en el runbook:
[`docs/operacion/runbook-carga-sap-proveedores-articulos.md`](../operacion/runbook-carga-sap-proveedores-articulos.md)
(scripts validados en [`docs/operacion/carga-sap-scripts/`](../operacion/carga-sap-scripts/)).

### Mapeos clave

**Proveedores** (`OCRD` → `compartido.proveedores`):
- **Filtro**: `CardType='S'` + grupo `OCRG.GroupName LIKE 'PROV.%'` (GroupType `S`;
  excluye `ACREEDORES`) + activos (`validFor='Y' AND frozenFor='N'`).
- `clave_legacy`=`CardCode` (traza/llave de dedup); `razon_social`=`CardName`;
  `nombre_comercial`=`CardFName`∥`CardName`; `rfc`=`LicTradNum`.
- **`tipo_persona` por longitud de RFC** (real 12→moral, 13→física; `XEXX010101000`
  /vacío→moral) — `CmpPrivate` resultó inservible (casi todo `C`).
- **moneda**: `MXP→MXN`, `##` (multimoneda)→`NULL`, resto código→id de `compartido.monedas`.
- **`condiciones_pago_dias`** = `OCTG.ExtraDays + ExtraMonth*30`.
- 🅿️ **clave**: `{prefijo}-{NNNNNN}` por grupo (NAC/EXT), continuando desde el
  máximo existente (en las pruebas se usó `P`+consecutivo).
- **dedup por `CardCode`** (no por RFC: `XEXX` compartido + RFC reales repetidos).

**Artículos** (`OITM` → `compartido.articulos`):
- **Filtro**: activos + `ItemType='I'` (**excluye activos fijos `F`**) + con nombre
  y uom (las **incompletas** van a una lista de revisión, no a la carga).
- `clave`=`ItemCode` (dedup por `ix_articulos_clave`); `nombre`=`ItemName`;
  `unidad_medida_default`=`InvntryUom`; `categoria`=`OITB.ItmsGrpNam`.
- **`naturaleza` por `InvntItem`** (`'N'`→Servicio(1), resto→Estándar(0)).
- Sin precio en esta fase (core+categoria).

### Variante prueba ↔ producción

Solo difieren dos puntos (marcados 🅿️ en el runbook): el **esquema de `clave` de
proveedor** (`P`+consecutivo en prueba ↔ `{prefijo}-{NNNNNN}` en prod) y el
**universo de artículos** (solo papelería/limpieza/insumos ≈1452 en prueba ↔ todos
los materiales ≈12,984 en prod). El resto (filtros, derivaciones, dedup, guards,
mecanismo) es idéntico.

### Estado de validación

| Entorno | Proveedores | Artículos | Esquema clave proveedor |
|---|---|---|---|
| local `millet_dev` | 2925 (total 3033) | 1452 Fase 1 (total 1529) | `P######` (prueba) |
| Azure dev | 2925 (total 2930) | 1452 Fase 1 (total 1462) | `P######` (prueba) |
| Prod | pendiente | pendiente | 🅿️ `{prefijo}-{NNNNNN}` |

No ejercitado aún: el esquema `{prefijo}-{NNNNNN}` y el universo completo (~12,984).
Recomendado un ensayo de la variante de prod en Azure dev antes de prod.

## Consecuencias

**Positivas**
- Cero superficie de software para algo que ocurre una vez; nada que mantener.
- Procedimiento auditable y repetible versionado en el repo.
- Guard intra-txn = reversibilidad incluso sin `DELETE` en el rol de carga.
- Core-only: sin migraciones de esquema ni catálogos nuevos.

**Negativas / trade-offs**
- Re-cargas y dedup son manuales (no hay upsert automático); el runbook lo cubre.
- Requiere ejecutar `sqlcmd`/`psql` a mano en una ventana de bajo tráfico.
- El esquema de clave de prod y el universo completo aún no se ejercitan end-to-end.

## Descartadas

- **Herramienta in-app (opción 1)**: sobre-ingeniería para una carga única;
  la propuesta (PR #410) **no fue aceptada** ni se mergeó a `main`
  (ver *Relación con la propuesta previa*), por lo que no es plan activo.
- **ETL externo (opción 3)**: agrega una dependencia y una superficie operativa
  desproporcionadas frente a un `sqlcmd` + `psql` documentados.

## Relación con la propuesta previa

El diseño de herramienta de importación (PR #410) **no fue aceptado** (no se
mergeó a `main`); sus docs **no forman parte de `main`**. Este ADR registra el
enfoque adoptado en su lugar: **procedimiento SQL documentado**. El PR #410 y su
rama (`docs/importacion-proveedores`) se cierran por separado; no dependen de este
ADR ni de este PR.

## Notas de implementación

- Runbook: [`docs/operacion/runbook-carga-sap-proveedores-articulos.md`](../operacion/runbook-carga-sap-proveedores-articulos.md).
- Scripts validados: [`docs/operacion/carga-sap-scripts/`](../operacion/carga-sap-scripts/)
  (`01_extract_*` / `02_load_*` de proveedores y artículos; variante de prueba).
- **CSV con PII nunca en git** (working dir fuera del repo, borrar al terminar);
  credenciales fuera de git (SAP por `SQLCMDPASSWORD` inline, Postgres por
  `pgpass.conf`). El login read-only de SAP expuesto en el diseño debe **rotarse**
  (runbook §6).
- Pendiente prod: sign-off de las 4 decisiones de dominio (runbook §1a), ejecutar
  la variante 🅿️ y procesar la lista de revisión de artículos incompletos.
