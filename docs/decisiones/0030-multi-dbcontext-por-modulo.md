# ADR-0030: Múltiples DbContexts por familia de esquemas

- **Estado**: Propuesta
- **Fecha**: 2026-05-04
- **Decisores**: Eduardo Paredes
- **Etiquetas**: persistencia, ef-core, modularidad, fundación

## Contexto y problema

[ADR-0005](0005-migraciones-ef-core-esquema-por-modulo.md) estableció
**esquema-por-módulo** sobre una sola base PostgreSQL: cada módulo es dueño
de su esquema (`identidad`, `compartido`, `core`, `facturacion`, etc.) y
nadie más escribe en él. Esa decisión es sobre el **layout físico** de la
base.

La pregunta que ese ADR no resolvió es la **estructura de DbContexts en EF
Core**: ¿uno solo que abarque todos los esquemas, o varios? La
implementación actual de Phase 1 ya tomó una postura por la fuerza del
desarrollo: hay tres `DbContext` distintos (`CompartidoDbContext`,
`CoreDbContext`, `IdentidadDbContext`) registrados en `Program.cs`, cada
uno con sus interceptors y su propio set de migraciones.

Conviene ratificar o ajustar esa decisión antes de que crezcan los
módulos de negocio (PR-7+) — agregar un módulo nuevo va a forzar la
pregunta "¿extiendo `CoreDbContext` o creo `FacturacionDbContext`?" y la
respuesta debe estar documentada.

## Drivers de la decisión

- **Aislamiento de migraciones**: que un cambio en el módulo de Facturación
  no requiera regenerar migraciones de Identidad o Compartido.
- **Tiempos de scaffolding**: agregar una entidad nueva no debería implicar
  recompilar/migrar todos los módulos.
- **Trade-off transaccional**: EF Core no soporta `SaveChanges` cross-context
  con una sola transacción atómica; ya estamos lidiando con esto en el
  bootstrap (`BootstrapSuperAdminHostedService` escribe en compartido
  primero, luego en identidad, sin transacción global, idempotente).
- **Aliñar con la arquitectura modular**: monolito modular con fronteras
  claras (CLAUDE.md) preferiría que cada módulo tenga su DbContext propio
  para reforzar la frontera, no debilitarla.
- **Costo de boilerplate**: cada DbContext nuevo agrega ceremonia (registro
  en DI, comando `dotnet ef migrations add`, interceptors).

## Opciones consideradas

1. **Un único `MilletDbContext`** que abarca todos los esquemas. Una sola
   tabla `__EFMigrationsHistory`, todos los `DbSet<>` en un mismo lugar.
2. **Un DbContext por módulo de negocio** (lo actual extendido a futuro):
   `IdentidadDbContext`, `FacturacionDbContext`, `CobranzaDbContext`, etc.,
   más uno transversal `CompartidoDbContext` y un `CoreDbContext` para
   tablas core compartidas (auditoría, outbox, etc.).
3. **DbContext por "familia"**: tres contextos fijos (`CompartidoDbContext`
   transversal, `CoreDbContext` para infraestructura, y un único
   `NegocioDbContext` que englobe todos los módulos de negocio).

## Decisión

(Pendiente de ratificación por el owner.)

La inclinación basada en la implementación actual y los drivers es **opción
2**: un DbContext por módulo, alineado uno-a-uno con el esquema dueño. Esto:

- Refuerza la frontera del módulo (no se puede "accidentalmente" hacer un
  join con tablas de otro módulo desde código nuevo — habría que agregar
  el otro DbContext explícitamente al handler).
- Mantiene migraciones aisladas (`dotnet ef migrations add Foo --context
  FacturacionDbContext` no toca a Identidad).
- Acepta el costo de no-transaccionalidad cross-module: la comunicación
  cross-module ya es por **eventos asíncronos** vía outbox (ADR-0009), no
  por transacciones SQL distribuidas. Los pocos casos que hoy requieren
  escritura cross-context (bootstrap) son idempotentes por diseño.

`CompartidoDbContext` y `CoreDbContext` se mantienen como casos especiales:
- `CompartidoDbContext`: tablas que son "verdad de catálogo" para varios
  módulos (Empresas, RegimenFiscal, etc.). Puede ser propiedad del módulo
  Identidad o vivir como context independiente — la implementación actual
  lo mantiene independiente.
- `CoreDbContext`: tablas de infraestructura cross-cutting (audit_log,
  outbox, idempotency_keys). Independiente de cualquier módulo de negocio.

## Consecuencias

**Positivas**
- Cada módulo gestiona sus migraciones sin afectar a los demás.
- El compilador y el DI ayudan a detectar acoplamiento indeseado entre
  módulos.
- Tests de integración pueden levantar un subset de DbContexts si el
  módulo bajo prueba no toca todo.
- Alineación clara entre frontera de módulo, esquema y DbContext (regla
  uno-a-uno-a-uno).

**Negativas**
- Más boilerplate de DI: cada DbContext debe registrarse, configurarse y
  recibir interceptors.
- Operaciones que naturalmente cruzan módulos (bootstrap, jobs de
  reconciliación) pierden la transacción atómica de SQL y deben
  implementarse idempotentes.
- Fan-out de archivos `Migrations/` (uno por módulo).

## Descartadas

**Opción 1 (un solo DbContext)**: viable en proyectos pequeños, pero borra
la frontera modular en código. Cualquier handler puede agregar un join
arbitrario. La separación física de esquemas pierde gran parte de su valor
si el código sigue tratando las tablas como un pool homogéneo.

**Opción 3 (DbContext por familia, un solo `NegocioDbContext`)**: trade-off
intermedio. Reduce boilerplate pero a costa de la frontera por módulo. Si
en el futuro el equipo decide que el costo de un DbContext-por-módulo es
demasiado para módulos chicos, esta opción está disponible como evolución.

## Notas de implementación

Configuración común a todos los DbContexts (vive en
`Program.ConfigureMilletDbContext`):

```csharp
opts.UseNpgsql(connectionString);
opts.UseSnakeCaseNamingConvention();
opts.AddInterceptors(
    metadataInterceptor,
    empresaContextInterceptor,
    auditInterceptor);
```

Reglas para módulos nuevos (PR-7+):

1. Crear `Millet.<Modulo>.csproj` con carpetas `Domain/`, `Application/`,
   `Infrastructure/`.
2. En `Infrastructure/`: crear `<Modulo>DbContext` con su `OnModelCreating`
   apuntando a su esquema (`modelBuilder.HasDefaultSchema("<modulo>")`).
3. Registrar el DbContext en `Program.cs`:
   `builder.Services.AddDbContext<<Modulo>DbContext>((sp, opts) =>
   ConfigureMilletDbContext(opts, sp))`.
4. Agregar al `tools/setup-dev.ps1` como un nuevo contexto en el array
   `$contexts` para que las migraciones se apliquen automáticamente en el
   bootstrap local.
5. Health checks: agregar `MigrationsAppliedHealthCheck<<Modulo>DbContext>`
   al pipeline de readiness en `Program.cs`.

**Cambios en otros ADRs**

- ADR-0005: este ADR complementa la decisión de esquema-por-módulo con la
  de DbContext-por-módulo. No la reemplaza.
- ADR-0009 (outbox): refuerza la elección — la comunicación cross-module
  ya es asíncrona, así que el costo de "no hay transacción cross-context"
  es bajo en la práctica.

**ADRs hijo posibles**

- Si el equipo decide que el costo de boilerplate es excesivo en los
  módulos pequeños, ADR posterior puede consolidar varios módulos en un
  `NegocioDbContext` compartido. Esta decisión sería reactiva, no
  preventiva.

## Addenda

### 2026-05-28 — Tabla N:M `compartido.sucursal_departamentos` (PR-A1)

`CompartidoDbContext` gana una tabla N:M cross-empresa que vincula
`compartido.sucursales` con `compartido.departamentos` para modelar la
realidad operativa de Millet: un departamento puede operar en algunas
sucursales y no en otras (Sistemas en Conkal sí, en Cancún no).

- Schema/DbContext sin cambios: la tabla vive en `compartido` y forma
  parte del catálogo organizacional, igual que sus dos tablas padre.
- Sin `EmpresaId` (mismo patrón que `Sucursal` y `Departamento` — MVP
  mono-empresa). Cuando llegue la migración multi-empresa, se agrega
  columna aditiva sin romper FKs existentes.
- Surrogate `Id` + UNIQUE `(sucursal_id, departamento_id)` para
  consistencia con `BaseEntity` e `IAuditable`; el cambio de estatus
  queda registrado en `core.audit_log` automáticamente.
- Sin contratos de evento — el catálogo lo consumen los módulos que lo
  necesiten via puerto de lectura definido por el consumidor (Compras lo
  define en PR-A2). No publica al Outbox.

Aprobadores (`AprobadorDepartamento`) y umbrales
(`UmbralAprobacionDepartamento`) **no se mueven** del scope
`(EmpresaId, DepartamentoId)`; siguen siendo cross-sucursal por
decisión explícita.

### 2026-06-02 — Enforcement en CrearRequisicion (PR-A2)

PR-A1 (#333) instaló el modelo N:M y su CRUD; PR-A3 (#341) filtró el
selector en frontend. PR-A2 cierra el loop activando la **validación
backend** en `CrearRequisicionHandler`. Si el frontend de PR-A3 se omite
(URL manual, replay, request hecho por otro cliente HTTP), el handler
ahora rechaza la combinación inválida en lugar de aceptarla en silencio.

Dos puertos nuevos en el módulo Compras (`Compras.Domain.Ports.*`):

- **`ISucursalDepartamentoReadPort.OperaAsync(sucursalId, deptoId)`** —
  resuelve si la asignación existe en `Activo` en
  `compartido.sucursal_departamentos`. Devuelve `false` para
  no-existente, `Inactivo` o `EnRevision`.
- **`IAlmacenReadPort.ObtenerAsync(almacenId)`** — devuelve un
  `AlmacenLectura(Id, Clave, SucursalId, EsActivo)` para validar
  coherencia (`Almacen.SucursalId == RQ.SucursalId`).

Ambos adapters viven en `Compras.Infrastructure.PublicAdapters` (no en
los módulos data-owner) porque `Millet.Almacen.csproj` y
`Millet.Compartido.csproj` no pueden referenciar Compras sin crear
ciclos. Patrón ya establecido por
`AlmacenEntregasReadAdapter`/`AlmacenStockReadAdapter`/etc.

3 códigos de error nuevos (mirror exacto en doc 01 §13 Rev. 21):

| Código | HTTP | Cuándo |
|---|---|---|
| `ALMACEN_NO_ENCONTRADO` | 404 | `AlmacenDestinoId` no existe en `almacen.almacenes` |
| `RQ_ALMACEN_NO_PERTENECE_A_SUCURSAL` | 422 | Almacén existe pero `SucursalId` ≠ `RQ.SucursalId` |
| `RQ_DEPTO_NO_OPERA_EN_SUCURSAL` | 422 | Combinación `(sucursal, depto)` no Activa |

**Sin cache** en los adapters: lectura O(1) sobre UNIQUE/PK index, ~1ms
cada una, ejecutadas en paralelo via `Task.WhenAll`. El catálogo cambia
raramente (sólo via panel admin de PR-A3). Decisión reversible si emerge
throughput issue post-launch.

`EditarCabeceraRequisicionHandler` **no se toca** — su comando no acepta
`SucursalId`/`DepartamentoId`/`AlmacenDestinoId` (campos inmutables tras
crear). Si la decisión de inmutabilidad cambia, requiere PR propio.

### 2026-09-23 — Excepción: alta de colaborador (ADR-0052)

El ciclo de vida del colaborador (alta, "dar acceso", baja y reactivación
de Empleado + Usuario) usa una **transacción compartida** entre
`IdentidadDbContext` y `CompartidoDbContext`, encapsulada en
`TransaccionColaborador`. Es una excepción acotada, con condiciones
(misma cadena de conexión, solo esos dos contextos, nada externo dentro de
la transacción). Ver [ADR-0052](./0052-transaccion-compartida-alta-colaborador.md).
La regla general de este ADR no cambia.
