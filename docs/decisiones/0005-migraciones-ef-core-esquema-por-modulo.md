# ADR-0005: Migraciones de BD con EF Core, un esquema por módulo

- **Estado**: Aceptada
- **Fecha**: 2026-05-01
- **Decisores**: Eduardo Paredes
- **Etiquetas**: backend, base-de-datos, fundación

## Contexto y problema

El sistema es un monolito modular con módulos independientes (Identidad,
Comercial, Fiscal, Financiero, Compras, Almacén, Activos Fijos, Contabilidad,
Reportería, Integración A+W). Todos comparten una sola base de datos
PostgreSQL.

Necesitamos una estrategia de migraciones de esquema que:

- Mantenga aislamiento entre módulos (un módulo no debe tocar tablas de otro)
- Permita evolucionar cada módulo a su propio ritmo
- Sea automatizable en el pipeline de despliegue
- Permita rollback razonable

## Drivers de la decisión

- Aislamiento entre módulos como propiedad arquitectónica de primer nivel
- Migraciones versionadas en Git, reproducibles en cualquier ambiente
- Compatibilidad con .NET 9 y PostgreSQL 16
- Productividad para el equipo (no escribir SQL a mano si EF Core puede generarlo)

## Opciones consideradas

1. EF Core con `DbContext` por módulo, un esquema PostgreSQL por módulo
2. EF Core con un único `DbContext` para toda la aplicación
3. DbUp con scripts SQL crudos versionados
4. FluentMigrator
5. Roundhouse / Flyway

## Decisión

Se adopta **EF Core con un `DbContext` por módulo**, donde cada `DbContext`
apunta a un **esquema PostgreSQL distinto**. Los nombres de esquema usan
kebab-case en plural del módulo: `identidad`, `comercial`, `fiscal`,
`financiero`, `compras`, `almacen`, `activos`, `contabilidad`, `aw`.

Cada módulo tiene su propia carpeta de migraciones (`backend/src/{Modulo}/Infrastructure/Migrations/`).
Las migraciones se aplican en el arranque de la aplicación en desarrollo, y
mediante un job dedicado en pipelines de QA y producción (nunca en arranque
en producción).

Esquemas adicionales no vinculados a un módulo de negocio:

- **`compartido`**: catálogos nacionales y datos cross-empresa (catálogo del SAT, tipos de cambio del DOF, códigos postales, tabla `empresas`). Definido en ADR-0011.
- **`core`**: tablas transversales del propio sistema (audit log central, ver ADR-0008).

**Multi-empresa** (ADR-0011): cada tabla de módulo de negocio incluye una columna `empresa_id` con FK a `compartido.empresas` y se aplica un global query filter automático. El aislamiento entre empresas es **lógico** vía esa columna, no físico vía esquemas separados.

## Consecuencias

**Positivas**
- Aislamiento físico entre módulos a nivel de esquema: imposible que un módulo "vea" tablas de otro accidentalmente
- Cada módulo evoluciona su esquema independientemente
- Convenciones automáticas de naming (snake_case) configuradas en una clase base de `DbContext`
- EF Core genera las migraciones desde el modelo C#: cambios automáticos al ejecutar `dotnet ef migrations add`
- Posibilidad futura de extraer un módulo a su propia BD si se justifica (cada esquema puede convertirse en una BD)

**Negativas**
- Los joins entre módulos NO son posibles vía SQL: las consultas cruzan-módulo deben hacerse vía interfaces de dominio o en el módulo de Reportería (que tiene permiso de lectura en todos los esquemas)
- Múltiples `DbContext` requieren registración cuidadosa en DI y manejo explícito de transacciones distribuidas (que no haremos: cada operación transaccional vive en un solo módulo)
- Las migraciones deben aplicarse en orden si un módulo agrega FK cross-schema (que evitaremos por diseño)

## Descartadas

**Un único `DbContext` global**. Rompe el aislamiento modular: cualquier módulo
podría agregar referencias a entidades de otro. La integridad referencial
cruzada se vuelve trivialmente fácil y eso es exactamente lo que queremos
evitar.

**DbUp / FluentMigrator / scripts SQL**. Funcionarían, pero perdemos la
generación automática de EF Core desde el modelo C#. Productividad del equipo
baja sustancialmente.

**Roundhouse / Flyway**. Excelentes herramientas, pero agregan una dependencia
fuera del ecosistema .NET sin beneficio en este contexto.

## Notas de implementación

- Cada módulo expone una clase `{Modulo}DbContext : DbContext` con `[Schema("nombre")]` o configuración equivalente
- Convenciones globales en `BaseDbContext` (clase abstracta heredada):
  - `snake_case` para tablas y columnas (vía interceptor)
  - `id` como PK uniforme con `Guid` v7 (ordenable temporalmente)
  - `version` (int) como concurrency token, configurada con `IsConcurrencyToken()` (ver ADR-0012)
  - `created_at`, `updated_at` automáticos
  - `created_by`, `updated_by` desde el contexto de autenticación
  - `empresa_id` automático para entidades que implementan `IPerteneceAEmpresa` (ver ADR-0011)
- Migraciones se aplican vía:
  - Dev: `Database.Migrate()` en arranque
  - QA/Prod: job dedicado en GitHub Actions, antes del despliegue de la app
- ADR hijo posible: estrategia de seed data por ambiente
- ADR hijo posible: estrategia de migración de datos desde SAP (es un mini-proyecto en sí)
