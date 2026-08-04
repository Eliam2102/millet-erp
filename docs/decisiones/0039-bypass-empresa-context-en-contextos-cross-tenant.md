# ADR-0039: Permitir bypass del contexto de empresa en contextos cross-tenant legítimos

- **Estado**: Aceptada
- **Fecha**: 2026-05-27
- **Decisores**: Eduardo Paredes
- **Etiquetas**: multi-tenancy, identidad, testing, sharedkernel

## Contexto y problema

ADR-0011 establece multi-tenancy por `EmpresaId` con global query filter y
`EmpresaContextSaveChangesInterceptor`. `ICurrentEmpresaContext.Bypass()`
permite escaparse del filtro/interceptor en escenarios sin contexto HTTP.

El `BypassUsageGuardTest` (test estructural en
`backend/tests/Api.IntegrationTests/Persistence/`) impide que `Bypass()`
se use fuera de carpetas legítimas: hasta hoy, `Migrations/`, `Seed/`,
`Workers/`, y archivos `*Seed.cs`.

Esa whitelist quedó corta: existen 4 contextos legítimos adicionales
donde operar cross-empresa es el comportamiento correcto y no una
violación de ADR-0011. Como consecuencia, el guard test viene
fallando en `main` desde hace tiempo por archivos como
`LoginOrchestrator.cs`, `BootstrapSuperAdminHostedService.cs`,
`*ReadAdapter.cs`, `ConfiguracionPacResolver.cs`, etc. El equipo lo
aceptó como deuda informal; el costo es que cuando un developer agrega
un `Bypass()` legítimo nuevo, no sabe si está rompiendo el guard o
ampliando la deuda.

## Drivers de la decisión

- Mantener la garantía estructural de ADR-0011: no `Bypass()` en
  handlers de negocio comunes.
- Reflejar formalmente los 4 patrones cross-tenant ya establecidos por
  el código que el guard no reconocía.
- Evitar fricción para developers que trabajan en esos contextos.
- ADR pequeño y reversible: extender whitelist es preferible a un
  refactor masivo del codebase.

## Opciones consideradas

1. **Extender whitelist por patrón de archivo/carpeta** en el guard
   test. Es la decidida.
2. **Attribute `[AllowsTenancyBypass("razón")]`** en cada clase o
   método que usa `Bypass()`, con el guard verificando presencia del
   attribute. Más robusto y autodocumentado pero requiere migrar ~20
   archivos y agregar custom logic de reflection/parsing.
3. **Eliminar el guard** y confiar solo en code review. Pierde la
   garantía estructural.

## Decisión

Se extiende `BypassUsageGuardTest.IsWhitelisted` con 4 categorías
nuevas, además de las preexistentes (`Migrations/`, `Seed/`,
`Workers/`, `*Seed.cs`):

1. **Adapters cross-bounded-context** — `**/Adapters/*.cs` y
   `**/PublicAdapters/*.cs`. Puertos públicos que un módulo expone a
   otros suelen leer catálogos cross-empresa (sucursales, proveedores,
   artículos, etc.) sin contexto HTTP del caller.
2. **Auth y bootstrap globales** — `LoginOrchestrator.cs`,
   `PermissionLoader.cs`, archivos `Bootstrap*HostedService.cs`.
   Operan antes del contexto de empresa (login resuelve qué empresa
   activa darle al JWT; bootstrap provisiona estado inicial).
3. **Resolvers cross-empresa de configuración** — `*Resolver.cs` en
   carpetas `Infrastructure/`. Resuelven datos compartidos entre
   tenants (config del PAC, service principals, etc.).
4. **Admin handlers del módulo Identidad** —
   `src/Identidad/Application/Usuarios/*.cs` y
   `src/Identidad/Application/Roles/*.cs`. El admin de identidad
   opera sobre `UsuarioEmpresaRol` y entidades relacionadas, que son
   cross-empresa por diseño: un super-admin asigna usuarios a
   cualquier razón social. El RBAC granular
   (`identidad.usuarios.leer`, `identidad.asignaciones.administrar`,
   etc.) es el gate efectivo.

Cada uso de `Bypass()` debe seguir llevando un comentario explicando
la razón concreta. La whitelist define *dónde* es legítimo; el
comentario explica *por qué* en cada caso.

## Consecuencias

**Positivas**

- El guard test pasa en `main` sin deuda informal.
- Cualquier `Bypass()` nuevo fuera de las 6 categorías se marca como
  violación → fuerza revisión y posiblemente refactor.
- Nuevas excepciones requieren editar el guard + actualizar este ADR,
  lo que mantiene la decisión auditable.

**Negativas**

- La whitelist crece por categoría. Si surge una 5ta categoría
  legítima, hay que evaluar si extender el guard o si esa categoría
  debería refactorizarse para no necesitar `Bypass()`.
- La granularidad por archivo/carpeta no documenta la razón
  específica de cada `Bypass()` — se delega al comentario en el
  handler, lo cual depende de disciplina del developer.
- El guard pierde algo de poder preventivo: agregar un handler nuevo
  en `Identidad/Application/Usuarios/` con `Bypass()` no dispara
  alarma. La trazabilidad cae al comentario.

## Descartadas

**Opción 2 (Attribute)** — el costo de migración (~20 archivos) y la
infraestructura adicional (custom reflection/parsing en el guard, o un
analizador Roslyn) excede el beneficio para el equipo actual. Sigue
siendo opción válida si el número de bypasses se duplica o el equipo
crece. El whitelist actual no impide migrar a attribute más adelante.

**Opción 3 (eliminar guard)** — perder la garantía estructural deja
la puerta abierta a `Bypass()` accidentales en handlers de negocio,
que es justamente lo que ADR-0011 quiere evitar. El guard, incluso
extendido, sigue cumpliendo esa función para handlers fuera de las
categorías whitelisted.

## Notas de implementación

- `backend/tests/Api.IntegrationTests/Persistence/BypassUsageGuardTest.cs`
  — extender `IsWhitelisted` con las 4 categorías nuevas y actualizar
  el XML doc para referenciar este ADR.
- En cualquier `Bypass()` nuevo legítimo: agregar comentario
  explicando por qué cruza empresa. Si no encaja en una categoría
  existente, evaluar refactor primero; si genuinamente es una 5ta
  categoría, abrir PR con extensión del guard + nota a este ADR.
- Posible follow-up: si emergen >3 nuevos bypasses sin patrón claro,
  reabrir la decisión y reconsiderar la opción attribute.
