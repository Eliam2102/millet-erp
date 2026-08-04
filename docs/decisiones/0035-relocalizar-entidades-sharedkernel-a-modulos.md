## ADR-0035: Re-localizar entidades de negocio de `SharedKernel/Domain/` a módulos dueños

- **Estado**: Aceptada
- **Fecha**: 2026-05-13
- **Decisores**: Eduardo Paredes (owner), Claude (backend)
- **Etiquetas**: arquitectura, sharedkernel, administracion, catalogos, datos-maestros, refactor

## Contexto y problema

`SharedKernel/Domain/` concentra hoy ~15 entidades de negocio:

| Entidad | Naturaleza |
|---|---|
| `Empresa`, `Sucursal`, `Departamento`, `Almacen` | Organización (multi-tenant, ADR-0011) |
| `Moneda`, `RegimenFiscal`, `CondicionesPago`, `Incoterm`, `Transportista`, `TipoPersonaProveedor`, `Naturaleza`, `UsoPrincipal`, `EstatusCatalogo` | Catálogos globales (SAT y operativos) |
| `Proveedor`, `Articulo` | Datos maestros operativos |

Además vive ahí infraestructura auténticamente transversal:

- `BaseEntity`, `Money` (value object).
- Interfaces `IAuditable`, `INotAudited`, `IFiscalmenteRelevante`, `IPerteneceAEmpresa`, `IBelongsToAggregate`.
- `Audit/AuditLogEntry`, exceptions, Outbox infra, Idempotency, `CompartidoDbContext`, `CoreDbContext`.

Las entidades de negocio están en `SharedKernel` por inercia histórica: cuando arrancó el monorepo, no había aún módulos donde ubicarlas. Compras-RQ y Compras-OC consumieron esas entidades vía `using Millet.SharedKernel.Domain;`. Hoy ~100 archivos del backend importan ese namespace.

ADR-0034 introduce los módulos `Administracion`, `Catalogos` y `DatosMaestros`. La pregunta es **cuándo** mover las entidades a sus módulos dueños: ahora (con dos módulos vivos: Compras + Identidad), o después de que entren Facturación, CxC, etc.

Sin esta decisión:

- El módulo Administración nace con su dominio fragmentado entre `Administracion/Domain/` y `SharedKernel/Domain/`. El 00-levantamiento del módulo se vuelve incoherente ("Empresa es de Admin pero vive en SharedKernel").
- Cada módulo nuevo agrega más `using Millet.SharedKernel.Domain;`. Con 2 módulos más (Facturación, CxC) el counter pasa de ~100 a ~250-300.
- `SharedKernel` deja de cumplir la definición DDD de shared kernel (primitivos + interfaces transversales) y queda como un cajón de catálogos.
- El refactor futuro es exactamente el mismo trabajo mecánico, pero amplificado por la inercia de más callsites y más módulos productivos que pausar.

## Drivers de la decisión

- **Costo escala super-lineal** con el número de módulos consumidores. Hacerlo con 2 módulos vivos es marginalmente más barato que con 4 o 5.
- **Coherencia con ADR-0034.** El modelo híbrido define ownership claro; la implementación debe reflejarlo.
- **Naturaleza mecánica del cambio.** El refactor es renombrar namespaces + mover ficheros; el compilador y los tests existentes son la red de seguridad.
- **Riesgo bajo de migración de datos.** El schema físico `compartido` puede quedarse como está; solo cambia la organización del código. La opción de renombrar schema (Fase B) queda explícitamente diferida.
- **Frontera hexagonal.** Cada entidad debe vivir en su módulo dueño para que el módulo sea reasignable, testeable de forma aislada y dueño de sus invariantes.

## Opciones consideradas

1. **Diferir como deuda.** Mantener entidades en `SharedKernel/Domain/`. Marcar con PLATFORM-TODO. Refactorizar "cuando estabilice el ERP".
2. **Refactor en dos fases dentro del mismo PR** — *elegida*. Fase A code-only (mover ficheros + actualizar namespaces; schema físico sin cambios). Fase B opcional/diferida (renombrar schema `compartido` a `admin`/`catalogos`/`datos_maestros`).
3. **Refactor completo en un solo PR (código + schema rename).** Mueve archivos, renombra namespaces y ejecuta `ALTER SCHEMA compartido RENAME TO admin` + duplica a `catalogos` y `datos_maestros` en migración aditiva.

## Decisión

**Refactor en dos fases (Opción 2). Fase A se ejecuta como F-Admin-PR0 antes de cualquier feature work del módulo Administración. Fase B queda explícitamente diferida.**

### Fase A — Refactor de código (F-Admin-PR0)

Mover ficheros y renombrar namespaces según el mapa de ownership de ADR-0034:

| Fichero actual | Mueve a |
|---|---|
| `SharedKernel/Domain/Empresa.cs` | `Administracion/Domain/Empresa.cs` (`Millet.Administracion.Domain`) |
| `SharedKernel/Domain/Sucursal.cs` | `Administracion/Domain/Sucursal.cs` |
| `SharedKernel/Domain/Departamento.cs` | `Administracion/Domain/Departamento.cs` |
| `SharedKernel/Domain/Almacen.cs` | `Almacen/Domain/Almacen.cs` (`Millet.Almacen.Domain`) — módulo `Almacen` (almacenes no-productivos) creado en el mismo PR. Decisión cerrada en A1=b del 00-levantamiento. |
| `SharedKernel/Domain/Moneda.cs` | `Catalogos/Domain/Moneda.cs` (`Millet.Catalogos.Domain`) |
| `SharedKernel/Domain/RegimenFiscal.cs` | `Catalogos/Domain/RegimenFiscal.cs` |
| `SharedKernel/Domain/CondicionesPago.cs` | `Catalogos/Domain/CondicionesPago.cs` |
| `SharedKernel/Domain/Incoterm.cs` | `Catalogos/Domain/Incoterm.cs` |
| `SharedKernel/Domain/Transportista.cs` | `Catalogos/Domain/Transportista.cs` |
| `SharedKernel/Domain/TipoPersonaProveedor.cs` | `Catalogos/Domain/TipoPersonaProveedor.cs` |
| `SharedKernel/Domain/Naturaleza.cs` | `Catalogos/Domain/Naturaleza.cs` |
| `SharedKernel/Domain/UsoPrincipal.cs` | `Catalogos/Domain/UsoPrincipal.cs` |
| `SharedKernel/Domain/EstatusCatalogo.cs` | `Catalogos/Domain/EstatusCatalogo.cs` |
| `SharedKernel/Domain/Proveedor.cs` | `DatosMaestros/Domain/Proveedor.cs` (`Millet.DatosMaestros.Domain`) |
| `SharedKernel/Domain/Articulo.cs` | `DatosMaestros/Domain/Articulo.cs` |
| `SharedKernel/Application/Catalogos/{Crear,Actualizar,Desactivar}Proveedor*` | `DatosMaestros/Application/...` |
| `SharedKernel/Application/Catalogos/{Crear,Actualizar,Desactivar}Articulo*` | `DatosMaestros/Application/...` |
| `SharedKernel/Application/Catalogos/ReclasificarNaturalezaArticulosCommand*` | `DatosMaestros/Application/...` |
| `Api/Endpoints/Catalogos/OrganizacionEndpoints.cs` | `Api/Endpoints/Administracion/OrganizacionEndpoints.cs` |
| `Api/Endpoints/Catalogos/CatalogosEndpoints.cs` | Split: parte queda como `Catalogos`, parte va a `DatosMaestros` según el recurso. |
| `Api/Endpoints/Catalogos/CatalogosOcEndpoints.cs` | Permanece como endpoint de Compras OC, pero los DTOs que consumen entidades movidas se actualizan a los nuevos namespaces. |
| `SharedKernel/Infrastructure/Persistence/CompartidoDbContext.cs` | Se divide en `AdministracionDbContext` (`admin`-owned mappings), `CatalogosDbContext` (`catalogos`-owned mappings) y `DatosMaestrosDbContext` (`datos_maestros`-owned mappings). Configuración via `OnModelCreating` apunta al schema `compartido` para preservar las tablas existentes (Fase A no toca BD). |

**Quedan en `SharedKernel/Domain/` (es lo correcto que ahí vivan):**

- `BaseEntity`, `Money`.
- Interfaces transversales: `IAuditable`, `INotAudited`, `IFiscalmenteRelevante`, `IPerteneceAEmpresa`, `IBelongsToAggregate`.
- `Audit/AuditLogEntry` (modelo de auditoría es transversal, ADR-0008).
- Application/Idempotency, Application/Exceptions, Infrastructure/Outbox, Infrastructure/Persistence/{Core,Interceptors}.
- `CoreDbContext` permanece en SharedKernel — gestiona idempotency keys, outbox, audit log, que son transversales.

**Migraciones existentes en `SharedKernel/Infrastructure/Persistence/Migrations/Compartido/` no se mueven.** Permanecen donde están con su historial. Las migraciones **nuevas** ya nacen en el módulo dueño:

```
Administracion/Infrastructure/Migrations/
Catalogos/Infrastructure/Migrations/
DatosMaestros/Infrastructure/Migrations/
```

**Schema físico:** sigue siendo `compartido`. El mapping EF Core apunta a `compartido` para todas las tablas movidas (ej. `ToTable("empresas", schema: "compartido")` desde `AdministracionDbContext`). Cero cambios físicos en BD.

**Rutas HTTP estables:** `/api/catalogos/monedas`, `/api/organizacion/empresas`, etc. siguen funcionando. Solo cambia el código que las sirve.

### Fase B — Schema rename (diferida)

Eventualmente:

```sql
ALTER SCHEMA compartido RENAME TO admin;
CREATE SCHEMA catalogos;
CREATE SCHEMA datos_maestros;
-- Mover tablas de admin a sus schemas respectivos.
```

**No se ejecuta en F-Admin-PR0 ni en el roadmap del Admin MVP.** Razones:

- No hay prod aún (ADR-0028 — solo `dev` hasta MVP), pero hay tooling local, scripts de seed, y dashboards de BI internos potenciales que consultan el schema `compartido`.
- Renombrar schema requiere coordinar con consumidores externos (incluso si hoy no hay, los habrá pronto).
- El beneficio de organizar el código se obtiene **sin** tocar el schema. El rename es cosmético.

Fase B se evalúa cuando exista una razón concreta (cliente externo pide acceso BI por schema, política de seguridad de Postgres por rol/schema, etc.). Mientras tanto, el schema `compartido` queda como nombre histórico documentado.

## Consecuencias

**Positivas**

- Cada módulo es dueño de sus entidades — frontera hexagonal limpia.
- `SharedKernel` recobra su definición DDD (primitivos + interfaces transversales).
- ADR-0034 (modelo híbrido de Admin) tiene base coherente: el 00-levantamiento del módulo Administración refleja la realidad.
- Inercia futura eliminada: módulos nuevos importan `Millet.Catalogos.Domain.Moneda`, no `Millet.SharedKernel.Domain.Moneda`. El antipatrón no se asienta.
- Sin riesgo de migración de datos: Fase A es solo código.
- Migraciones existentes permanecen (no se rebasean), preservando historial.

**Negativas**

- 1 PR de tamaño M-L mecánico. ~15 ficheros movidos, ~100 archivos actualizan imports. Es trabajo guiado por el compilador y tests existentes, pero no es trivial.
- Schemas físicos vs ownership lógico quedan desalineados temporalmente (todas las tablas en schema `compartido`, código repartido en 3 módulos). Documentado y aceptado.
- `Almacen` queda provisionalmente en `Administracion` hasta que se construya el módulo Almacén-no-prod. Pequeño churn esperado en el futuro.

## Descartadas

**Opción 1 (diferir).** Costo escala super-lineal con módulos consumidores. El antipatrón de "SharedKernel como cajón de catálogos" se asienta. El 00-levantamiento de Administración se vuelve incoherente. Refactor futuro pausa equipos productivos.

**Opción 3 (Fase A + Fase B en un solo PR).** El rename de schema (`ALTER SCHEMA compartido RENAME TO admin` + split a 3 schemas) introduce riesgo desproporcionado al beneficio en MVP. No hay prod, pero el rename requiere actualizar seeds, scripts de dev, eventuales dashboards locales, y endpoints de health. Mejor obtener el beneficio principal (código organizado) sin el costo del rename.

## Notas de implementación

### F-Admin-PR0 — checklist

- [ ] Crear estructura de carpetas `Administracion/Domain/`, `Catalogos/Domain/`, `DatosMaestros/Domain/`, con `Application/` y `Infrastructure/` paralelas según los módulos exemplar (Compras, Identidad).
- [ ] Mover ficheros uno por uno. Para cada entidad:
  - [ ] Actualizar el `namespace` declarado.
  - [ ] Compilar para detectar consumidores.
  - [ ] Reemplazar `using Millet.SharedKernel.Domain;` por `using Millet.Administracion.Domain;` / `Millet.Catalogos.Domain;` / `Millet.DatosMaestros.Domain;` según corresponda.
  - [ ] Si una clase consume varias, agregar varios `using`.
- [ ] Splittear `CompartidoDbContext` en `AdministracionDbContext`, `CatalogosDbContext`, `DatosMaestrosDbContext`. Cada uno apunta a tablas en schema `compartido` por `ToTable(..., schema: "compartido")`.
- [ ] Actualizar `Program.cs` (cada DbContext nuevo requiere `AddDbContext<>` y `MigrationsHealthCheckOptions` — checklist obligado por memoria `feedback_dbcontext_nuevo_checklist`).
- [ ] Actualizar `.github/workflows/deploy-app-dev.yml` con los nuevos DbContexts (mismo checklist).
- [ ] Splittear `Api/Endpoints/Catalogos/` en `Api/Endpoints/Administracion/`, `Api/Endpoints/Catalogos/`, `Api/Endpoints/DatosMaestros/`. Mantener rutas HTTP estables (no cambiar URLs).
- [ ] Mover Application commands (`Crear*Command`, `Actualizar*Command`, `Desactivar*Command` para Proveedor/Articulo, `ReclasificarNaturalezaArticulosCommand`) a `DatosMaestros/Application/`.
- [ ] `SharedKernelDbContextFactory` y factories de test: actualizar imports y, si aplica, partir en factories por módulo.
- [ ] Correr suite completa: `dotnet test`. Compilador + tests son la red de seguridad.

### Detalle: cross-module references

- **`Identidad.Usuario` referencia `Departamento`.** Hoy es navegación EF directa. Tras refactor, `Departamento` vive en `Administracion`. Opciones:
  - (a) Mantener navegación EF (acepta dependencia `Identidad → Administracion`). Pragmático en MVP, viola purismo hexagonal.
  - (b) Cambiar a FK por id sin navegación (`DepartamentoId : Guid`). Recomendado, alinea con hexagonal.
  - **Decisión en PR0:** opción (b). Sin navegación cross-módulo a nivel EF. Resolver `Departamento` por query separada cuando se necesite.

- **`Compras` referencia `Proveedor`, `Articulo`, `Moneda`, `CondicionesPago`, `Incoterm`, `Transportista`, `RegimenFiscal`, `Sucursal`, `Almacen`, `Empresa`.**
  - Misma regla: `using Millet.<ModuloDueño>.Domain;` + FK por id en lugar de navegación cuando sea posible.
  - Migración aditiva: si hoy hay navegación EF, mantenerla en PR0 y registrar PLATFORM-TODO para limpiarla módulo por módulo. No bloquea el refactor de namespaces.

### Riesgos y mitigaciones

- **Compilación masiva rota.** Mitigación: refactor incremental, una entidad a la vez. Compilador guía cada paso.
- **Tests rotos por imports.** Mitigación: `dotnet test` se ejecuta después de cada batch de cambios.
- **Factories y seeds.** Mitigación: revisar `SharedKernel/Infrastructure/Seed/CatalogosTestSeedHostedService.cs` y `Identidad/Infrastructure/BootstrapSuperAdminHostedService.cs` para imports.
- **EF Migrations Designer files.** Mitigación: los `.Designer.cs` regeneran al hacer `dotnet ef`. No requieren edición manual.

### Pendientes derivados

- Módulo `Almacen` (almacenes no-productivos) nace en F-Admin-PR0 como módulo MVP-light con solo la entidad `Almacen`. Su superficie crece cuando se construya el alcance completo del módulo (inventario, movimientos, recepciones cross-Compras). Sin ADR adicional necesario.
- Fase B (schema rename) sin fecha. Evaluable cuando exista consumidor externo del schema.
- Endpoints de catálogos: revisar si las URLs `/api/catalogos/*` son las definitivas o si conviene reorganizarlas a `/api/admin/*`, `/api/catalogos/*`, `/api/datos-maestros/*`. **Decisión en PR0:** mantener URLs actuales (estabilidad). Reorganización de URLs queda como ADR futuro si se prioriza.
