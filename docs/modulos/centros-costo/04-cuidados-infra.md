# Cuidados de infraestructura — Módulo Centros de Costo (`Millet.CentrosCosto`)

> **Versión:** 0.1 · **Fecha:** 2026-07-14
> Prioridades: **[P0]** rompe el deploy o corrompe datos si se omite; **[P1]** deuda operativa.

---

## 0. Cómo leer

Checklist por área, mismo formato que
[`tesoreria/04-cuidados-infra.md`](../tesoreria/04-cuidados-infra.md).
Los incidentes citados son reales del proyecto; no re-aprenderlos. Este
módulo NO usa Service Bus/outbox en v1, así que el grueso del riesgo
está en migraciones y datos.

---

## 1. Migraciones EF Core

### 1.1 [P0] DbContext nuevo — checklist completo antes de mergear PR-1
`Program.cs` (AddDbContext + `MigrationsHealthCheckOptions`) **y**
`deploy-app-dev.yml` (paso "Apply migrations for all DbContexts") en el
mismo PR. Sin lo segundo el deploy nunca aplica las migraciones del
módulo y `/health/ready` responde 503 (incidente documentado del repo).
⚠️ El PR #580 ya tiene lo primero pero **le falta `deploy-app-dev.yml`**
— es parte obligatoria del rebase.

### 1.2 [P0] Migration en `IdentidadDbContext` al tocar `PermisosCanonicos`
`HasData(PermisosCanonicos.Todos)` afecta al modelo; sin migration el
deploy aborta con `PendingModelChangesWarning`. Aplica al rebase de PR-1
(renumeración `0000000c-*`) y a cualquier permiso posterior.

### 1.3 [P0] Colisión de namespace de permisos — regenerar, no mergear a mano
Lección de este módulo (2026-07-14): A1 tomó `0000000b-*` estando libre
y Tesorería lo ganó al mergear primero — GUID por GUID idénticos. La
resolución correcta es: tomar el snapshot de Identidad de main tal cual,
borrar la migración propia + Designer, renumerar constantes y **regenerar**
con `dotnet ef migrations add`. Nunca resolver el
`IdentidadDbContextModelSnapshot.cs` textualmente.

### 1.4 [P1] Limpieza de BD local tras renumerar
Si una BD dev ya aplicó la migración vieja (`0000000b`), antes del
catch-up hay que borrar las filas de `identidad.permisos` del módulo
**y sus `rol_permisos`** (el bootstrap del SuperAdmin las asigna solo) y
el registro en `__EFMigrationsHistory`. Runbook ejecutado en millet_dev
el 2026-07-14.

### 1.5 [P0] Nomenclatura Dim + convención snake: `HasColumnName` explícito SIEMPRE
Propiedad SISTEMÁTICA (3 casos ya: `clave_ce_co` en A1, `dim1id`/`dim2id` y
`grupo_dim2id`/`grupo_dim3id` en el refactor): la convención snake_case
**no separa dígito→mayúscula** — TODO identificador del módulo que mezcle
letra y dígito (`Dim1Id` → `dim1id`) necesita `HasColumnName` explícito.
Cada columna futura con número en el nombre tropieza igual: ponerlo al
crearla, no al depurar el 42703.

### 1.6 [P1] El drift-check de `has-pending-model-changes` es ciego a la BD
Valida modelo↔**snapshot**, NO modelo↔**BD**: una migración escrita a mano
(renombrados) puede divergir del modelo y ese comando seguir verde — ambos
lados comparten el nombre derivado. El drift-check REAL contra la BD son
los tests de integración del módulo (así se detectó el caso 1.5).

### 1.7 [P0] Enums persistidos con `HasCheckConstraint`
`estatus` (0–2) en las 5 tablas — ya en la migración inicial. Nuevo
valor = constraint + migration + mirror FE (incidente FacturaAnticipo
2026-07-11).

## 2. Migración de siembra (Fase B) — lección #501

### 2.1 [P0] Idempotente y re-ejecutable
`ON CONFLICT DO NOTHING` **sin target** en TODAS las
inserciones. Segunda corrida = no-op verificado por test. El incidente
#501 (migración con datos que falló en prod) es el anti-patrón de
referencia: probada solo en local con datos ya presentes.

### 2.2 ~~Orden entre contextos~~ — OBSOLETO (modelo Dim, levantamiento §7.4)
Con la separación total la siembra es **single-schema**: no existe la
migración de Compartido, no hay orden entre contextos que cuidar, y el
guard de vínculos=5 desapareció. **Lo que queda del mecanismo de ruido:**
el guard FINAL de conteos (5/6/44/57/361) — sigue siendo P0: convierte
cualquier fila tragada por el `ON CONFLICT` sin-target en `RAISE` con
rollback total.

### 2.2-bis [P0] Migración de RENOMBRADO del refactor (CECO-PR4)
`RenameTable`×5 + drop del vínculo sobre tablas **vacías en prod/CI**
(#603 nunca mergeó). El único ambiente con datos del modelo viejo es el
dev local que probó #603: **runbook de limpieza ANTES del gate** (borrar
la siembra local nunca-mergeada, sus 2 registros de
`__EFMigrationsHistory` y las 5 sucursales CKL–PIN de compartido — espejo
del runbook de los permisos `0000000b`).

### 2.3 [P0] GUIDs deterministas
Nodos sembrados con GUIDs fijos (estilo seed `00000005-…` de sucursales
de test): mismo Id en local/CI/prod, reproducible y depurable. Nada de
`Guid.NewGuid()` en la siembra.

### 2.4 [P0] Probada con datos reales en BD limpia
Test de integración que corre las migraciones desde cero y afirma
conteos (5/6/44/57/361) + jerarquía completa (cero huérfanos, cero
claves duplicadas). "Compila" no es prueba.

### 2.5 [P1] Fuente documentada
Encabezado de la migración: archivo Excel fuente, fecha de corte, hoja,
transformaciones (typo `20PDPR`). El script generador one-off no se
despliega ni se mergea.

## 3. Datos y consumidores (Fase E)

### 3.1 [P0] Sin migración de datos históricos
Las columnas `centro_costo_id` y `maquina_destino_id` existentes se
reusan tal cual (pasan a contener `Equipo.Id`). Los GUIDs viejos NO se
migran, NO se validan retroactivamente y NO se borran — display
"No catalogado". Cualquier intento de "limpiar" histórico es un cambio
de alcance que requiere decisión explícita.

### 3.2 [P0] Validación por read-port, no FK física
La tentación de agregar FK de `almacen.lineas_movimiento` /
`compras.requisicion_lineas` hacia `centros_costo.equipos` viola la
convención de Guids lógicos cross-schema Y rompería con los GUIDs
históricos (3.1). La validación vive en los commands vía
`ICentroCostoReadPort`.

### 3.3 [P1] Coordinación con módulos activos
PR-6/PR-7 tocan Almacén y Compras, que tienen equipo trabajando a
diario. STOP #2 del [`_kickoff.md`](_kickoff.md): confirmar con Eduardo
que no hay trabajo en vuelo sobre `NuevaSalidaSheet`/`LineaInlineForm`
antes de arrancar.

## 4. Seguridad y permisos

- **[P0]** El evaluador de alcance corre en el handler (datos), no solo
  en el endpoint (403): un usuario con `catalogo.leer` pero sin
  asignaciones ve lista vacía, no el catálogo completo (CC-G5).
- **[P1]** `equipos.leer-todos` es el único bypass; no introducir
  "ver-todo" implícitos en queries de reportes.

## 5. Concurrencia

- Optimista vía `Version`/ETag (ADR-0012) en todos los niveles;
  `VersionEsperada` en comandos de edición/baja.
- La cascada de baja corre en una transacción; el índice UNIQUE global
  de clave respalda las validaciones de unicidad ante dobles submit
  (mismo criterio que el índice parcial de Cajas).

## 6. Performance

Volúmenes triviales (361 equipos, ~440 nodos totales). Único punto de
atención: la resolución batch de nombres para el árbol y los reportes
(una query por nivel, nunca N+1 al read-port de sucursales).

## 7. Observabilidad

- Correlation IDs estándar (ADR-0006); sin listeners ni DLQ en v1.
- Métrica mínima: conteo de documentos con centro de costo "No
  catalogado" (proxy de adopción del selector vs GUIDs históricos).

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
| 0.2 | 2026-07-16 | Modelo Dim: §2.1 sin-target, §2.2 obsoleto (single-schema) con el guard final como P0 vigente, §2.2-bis cuidados del refactor (renombrado sobre tablas vacías + runbook dev). |
| 0.3 | 2026-07-16 | Lecciones del gate de CECO-PR4: §1.5 nomenclatura Dim exige `HasColumnName` explícito (la convención snake no separa dígito→mayúscula — 3 casos ya) y §1.6 `has-pending-model-changes` es ciego a la BD (el drift-check real son los tests de integración). |
