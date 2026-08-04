# ADR-0016: Estrategia de testing (xUnit + Testcontainers + Playwright)

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: testing, calidad, ci, fundación

## Contexto y problema

Un ERP financiero requiere alta confianza en cambios: una regresión en
cálculo de impuestos, en aplicación de pagos, o en aislamiento entre
empresas no es un bug menor, es un riesgo de cumplimiento. Sin estrategia
de testing definida desde el día 1, cada dev escribe tests con sus propias
convenciones (o no escribe), la cobertura es errática, los integration
tests dependen de infraestructura compartida y se vuelven flaky, y al
cabo de meses el suite es más obstáculo que ayuda.

Necesitamos definir desde Fase 1: qué frameworks usar, qué tipos de tests
escribir, dónde poner cada uno, qué convenciones seguir, y qué garantías
ofrece cada nivel.

## Drivers de la decisión

- Detectar regresiones funcionales antes de llegar a producción
- Tests reproducibles: cero dependencia de máquinas locales o estado compartido
- Velocidad razonable en CI: la suite no debe ser un cuello de botella
- Confianza alta en código de dominio crítico (money, time, audit, multi-tenant)
- Convenciones uniformes: cualquier dev nuevo entiende cómo escribir tests sin reinventar

## Opciones consideradas

1. xUnit + FluentAssertions + Testcontainers + NSubstitute + Bogus + Playwright + Verify (apuesta combinada)
2. xUnit con `InMemoryDatabase` de EF Core para integration tests (sin Testcontainers)
3. NUnit como framework principal en lugar de xUnit
4. SpecFlow / Reqnroll para BDD con escenarios Gherkin
5. Solo unit tests, sin integration ni E2E

## Decisión

Se adopta la **opción 1**: pirámide clásica de tests con xUnit como
framework, integration tests reales contra PostgreSQL vía Testcontainers,
y E2E con Playwright para flujos críticos.

### Pirámide

**Unit tests** (mayoría, rápidos, sin BD ni red):
- Value objects: `Money`, `Rfc`, `Curp`
- Lógica de dominio pura: cálculos, validaciones, transiciones de estado
- Helpers transversales: `FechaContable`, mappers, formatters
- Tiempo objetivo: suite completa termina en menos de 30 segundos
- Sin mocks de BD; el código de dominio no debe depender de BD

**Integration tests** (medianos, con BD real):
- Handlers de comandos y queries
- Repositorios y consultas EF Core (validan que los query filters funcionan, que las migraciones reflejan el modelo, que constraints de BD se respetan)
- Audit interceptor, EmpresaContext interceptor, soft delete
- Outbox: que el evento se inserta en la misma transacción
- BD real vía Testcontainers (PostgreSQL en Docker, levantado por test fixture)
- Tiempo objetivo: suite completa termina en menos de 5 minutos
- **Prohibido `Microsoft.EntityFrameworkCore.InMemory`**: no respeta query filters complejos, no valida constraints, no detecta SQL inválido. Genera falsos positivos peligrosos

**E2E tests** (pocos, lentos):
- Flujos críticos end-to-end: login + crear cliente, login + crear CFDI + timbrar (con PAC sandbox), login + aplicar pago + verificar contabilidad
- Playwright contra Docker Compose local en CI o ambiente QA
- Tiempo objetivo: suite completa termina en menos de 15 minutos
- Se ejecutan en pipeline nocturno y manualmente antes de release; NO bloquean cada PR

### Frameworks y librerías

| Propósito                  | Librería                  | Razón                                                                |
|----------------------------|---------------------------|----------------------------------------------------------------------|
| Test framework             | xUnit                     | De facto en .NET moderno, mejor integración con analyzers, fixtures naturales |
| Asserts legibles           | FluentAssertions          | `result.Should().Be(...)` legible vs `Assert.Equal`                  |
| Contenedores de BD         | Testcontainers            | PostgreSQL real, arranque/destrucción automáticos, sin estado compartido |
| Data generation            | Bogus                     | Faker para crear datos de prueba realistas y deterministas           |
| Mocks                      | NSubstitute               | Más natural que Moq en C# moderno; sintaxis lambda más limpia        |
| E2E browser                | Playwright                | Mejor que Selenium en velocidad, debugging, paralelización; integración con CI sólida |
| Snapshot testing           | Verify                    | Para validar XML de CFDI, JSON de respuestas complejas, PDFs generados |

### Estructura de proyectos

```
backend/
├── src/
│   ├── Api/
│   ├── Shared/
│   ├── Identidad/
│   └── (otros módulos)
└── tests/
    ├── Shared.UnitTests/
    ├── Identidad.UnitTests/
    ├── Identidad.IntegrationTests/
    ├── Fiscal.UnitTests/
    ├── Fiscal.IntegrationTests/
    ├── Api.IntegrationTests/      # tests cross-módulo y de pipeline HTTP
    └── (un proyecto de tests por proyecto de producción + un Api.IntegrationTests)

frontend/
└── src/
    └── (componentes)
└── tests/
    ├── unit/                      # Vitest: lógica pura del frontend
    └── e2e/                       # Playwright: flujos completos
```

E2E vive en `e2e/` separado del frontend (usa `frontend/` y `backend/`
como dependencias en runtime, no en build).

### Convenciones de naming

- Test classes: `{ClassUnderTest}Tests` (estilo neutro) o `{Feature}Should` (estilo BDD). Elegir uno por carpeta y mantener consistencia
- Test methods: `Should_ExpectedBehavior_When_Scenario` o `MethodName_Scenario_ExpectedBehavior`
  - Ejemplo bueno: `Should_RejectIncompatibleCurrencies_When_AddingMxnToUsd`
  - Ejemplo malo: `TestMoney1`
- Arrange/Act/Assert: secciones explícitas con comentarios `// arrange`, `// act`, `// assert` cuando el test no es trivial

### Patrones técnicos

**`TestClock`**:
- Implementación de `IClock` controlable: `clock.AdvanceBy(TimeSpan.FromMinutes(5))`, `clock.Set(specificDateTimeOffset)`
- Inyectado vía DI en tests; reemplaza `SystemClock` de producción
- Ya definido como parte de ADR-0013

**`EmpresaContext.Bypass()`**:
- Disponible en tests cuando se prueban entidades de dominio aisladas o se necesita seed
- Definido en Fase 1 para resolver la pregunta 6 del scoping
- Test que escanea código y falla si encuentra uso de `Bypass()` fuera de carpetas marcadas (`tests/`, `Migrations/`, `Seed/`)

**Database fixture para integration tests**:
- `DatabaseFixture` arranca PostgreSQL con Testcontainers, aplica todas las migraciones, retorna `DbContext` configurado
- Compartido entre tests de la misma clase con `IClassFixture<DatabaseFixture>` (xUnit reusa instancia)
- Cada test corre dentro de una transacción que se hace rollback al final: aislamiento sin truncar tablas, mucho más rápido
- Para tests que necesitan commitear (ej. probar que un evento de outbox se publica), `DatabaseFixture` provee `WithCommittedTransactionAsync(...)` que limpia explícitamente al final

**Builders y factories**:
- Cada entidad de dominio importante tiene un builder con defaults razonables:
  ```csharp
  var cliente = new ClienteBuilder()
      .ConRfc("XAXX010101000")
      .EnEmpresa(empresaA)
      .Build();
  ```
- Builders viven en `tests/Shared/TestBuilders/` y se comparten entre proyectos de tests
- Bogus se usa internamente en los builders para defaults realistas (nombres, RFCs aleatorios pero válidos, etc.)

**Invariantes de secuencia de folios (aislamiento por sucursal)**:
- Los tests que asierten invariantes de la secuencia de folios por-sucursal (p. ej. consecutividad `nA → nA+1`) usan una **sucursal seedeada dedicada** (lane `(empresa, sucursal, año)` propio), no la sucursal canónica compartida — así la aserción es determinista aun con el suite en paralelo (varias clases crean RQs en la sucursal canónica concurrentemente). Ver `RequisicionesEndpointsTests.EnsureSucursalDedicadaFoliosAsync`.

**Snapshot testing con Verify**:
- Para validar que la generación de XML de CFDI es estable: snapshot del XML producido se commitea al repo; si cambia, el test falla y el dev confirma o rechaza el cambio
- Usado donde el output es complejo y comparar campo por campo es ruidoso (XML, PDF, reportes JSON grandes)
- NO usado para output trivial (un `Money`, un DTO de 3 campos)

### Cobertura

**Targets por capa** (no son límites duros, son guías):

| Capa                                | Cobertura objetivo |
|-------------------------------------|--------------------|
| Domain (entidades, value objects)   | 90% +              |
| Application (handlers, commands)    | 80% +              |
| Infrastructure (repos, external)    | 60% + (resto cubierto por integration) |
| Controllers / endpoints HTTP        | Sin target unit; cubierto por integration tests |

**Filosofía**: el objetivo no es perseguir % de cobertura ciegamente. El
objetivo es que cualquier cambio que rompa una regla de negocio tenga un
test que lo detecte. Métricas son indicadores secundarios; revisión humana
de los tests escritos es lo que da confianza real.

**Reportes**: Coverlet genera reporte en cada PR; ReportGenerator publica
HTML como artifact del pipeline; se monitorea tendencia, no valor absoluto.

### Tests obligatorios para multi-tenant

Suite dedicada `TenantIsolationTests` que es **obligatoria** para cualquier
entidad nueva que implemente `IPerteneceAEmpresa`. Verifica:

1. **SELECT**: con contexto en empresa A, queries no retornan registros de empresa B
2. **INSERT**: con contexto en empresa A, asignar manualmente `empresa_id = B` lanza `CrossTenantViolationException`
3. **UPDATE**: con contexto en empresa A, modificar registro existente que pertenece a B falla (no debería ser visible primero, pero por defensa-en-profundidad se valida)
4. **FK**: con contexto en empresa A, FK que apunte a registro de empresa B lanza excepción
5. **Bypass**: solo el rol con permiso `compartido.cross_empresa.leer` puede saltarse el filtro

Enforcement: linter/test que escanea entidades `IPerteneceAEmpresa` y verifica
que cada una tenga al menos un test en la suite. Falla CI si no.

### CI

**En cada PR**:
- Build (debe compilar sin warnings)
- Lint (analyzers de Roslyn, BannedApiAnalyzers, etc.)
- Unit tests (todos los proyectos `*.UnitTests`)
- Integration tests (todos los proyectos `*.IntegrationTests` con Testcontainers)
- Cobertura reportada como artifact

**Nightly + pre-release**:
- E2E tests con Playwright contra Docker Compose
- Performance tests (si se implementan)
- Security scans (dependabot, CodeQL)

**Política de PRs**:
- Tests unit + integration deben pasar antes de merge: no hay excepciones
- Si un test es flaky, se quarantine inmediatamente con `[Skip("flaky, see #XXX")]` y se abre issue. Lo flaky NO bloquea PRs ajenos pero queda visible para arreglarse
- Tests E2E pueden fallar en nightly sin bloquear PRs; cuando fallan, dispara investigación

### No-objetivos

- **TDD estricto no es requisito**: pragmático sí (test antes que código cuando ayuda a diseñar; después cuando es más natural). Lo que se exige es que el código mergeado tenga tests, no el orden en que se escribieron
- **BDD con Gherkin**: descartado. SpecFlow/Reqnroll agregan capa de indirección que rara vez paga su costo. Tests legibles en xUnit + FluentAssertions cumplen el mismo objetivo de claridad sin la fricción
- **Mutation testing**: interesante pero no obligatorio. Si después un módulo crítico (Fiscal) lo justifica, se evalúa Stryker.NET puntualmente
- **Property-based testing**: descartado como default. Útil en value objects con invariantes claros (`Money`, `Rfc`); cuando aplique, FsCheck o un equivalente
- **Selenium**: descartado. Playwright es superior en todos los ejes relevantes

## Consecuencias

**Positivas**
- Tests reproducibles: Testcontainers elimina dependencias de BD compartida; cero "funciona en mi máquina"
- Velocidad: unit < 30s, integration < 5min, suite completa en CI razonable
- Confianza alta en tenant isolation: la suite obligatoria detecta regresiones temprano
- Cero falsos positivos por `InMemoryDatabase`: integration tests reflejan comportamiento real de PostgreSQL
- Convenciones uniformes: cualquier dev nuevo escribe tests consistentes desde el primer día
- Snapshot testing reduce fricción en validar outputs complejos (CFDI XML, reportes)

**Negativas**
- Testcontainers requiere Docker en máquinas de desarrollo y CI: documentado como prerequisito
- Integration tests son más lentos que unit (2-5 min vs 30s). Aceptable, pero hay que vigilar que no crezca a 20 min sin razón
- Más librerías que conocer (FluentAssertions, NSubstitute, Bogus, Verify, Playwright). Curva de aprendizaje real pero amortizable; cada una resuelve algo concreto
- Builders y factories son boilerplate inicial; pagan a partir del 5to test que los usa

## Descartadas

**`InMemoryDatabase` de EF Core**. No respeta query filters globales con
expresiones complejas, no valida constraints (UNIQUE, FK), no detecta SQL
inválido. Tests pasan en memoria y fallan en producción. Inaceptable en un
ERP donde el comportamiento de BD es central.

**NUnit**. Funcional pero con menor adopción en proyectos .NET nuevos. La
ergonomía de fixtures de xUnit (`IClassFixture`, `IAsyncLifetime`) es
superior para el patrón que adoptamos. MSTest tampoco se considera por ser
el menos usado.

**SpecFlow / Reqnroll (BDD con Gherkin)**. Agrega capa de traducción
inglés→tests que rara vez es leída por non-devs. La promesa de
"stakeholders escriben los escenarios" casi nunca se cumple. Tests xUnit
con buenos nombres son igual de claros sin la indirección.

**Solo unit tests**. Tentador pero deja sin cubrir el comportamiento real
de EF Core con PostgreSQL: query filters, interceptores, audit, soft delete,
multi-tenant. Esa lógica solo se valida realmente con BD real.

**Selenium para E2E**. Playwright lo supera en velocidad, debugging
(`page.pause()`), paralelización, y soporte de contextos isolados. Sin
razón para elegir Selenium en proyectos nuevos.

## Notas de implementación

**Setup inicial** (Fase 1)

- Agregar paquetes a `Directory.Packages.props`:
  - `xunit`, `xunit.runner.visualstudio`
  - `FluentAssertions`
  - `Testcontainers.PostgreSql`
  - `NSubstitute`
  - `Bogus`
  - `Verify.Xunit`
  - `Microsoft.NET.Test.Sdk`
  - `coverlet.collector`
- Crear proyectos `Shared.UnitTests` e `Identidad.UnitTests` como ejemplo de la convención
- Crear `tests/Shared/TestBuilders/` para builders compartidos
- Crear `DatabaseFixture` en `tests/Shared/Infrastructure/`
- Configurar GitHub Actions (o pipeline equivalente) con jobs: build, lint, unit, integration

**Testcontainers configuration**

```csharp
public class DatabaseFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; private set; }
    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        Container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("millet_test")
            .Build();
        await Container.StartAsync();

        // Aplicar migraciones de todos los DbContexts
        await ApplyMigrationsAsync();
    }

    public async Task DisposeAsync() => await Container.DisposeAsync();
}
```

**Convenciones a documentar en `CLAUDE.md`**:
- Cómo escribir un test unit (estructura, naming, assertions)
- Cómo escribir un integration test (uso de fixture, transacción, EmpresaContext)
- Cómo agregar una nueva entidad `IPerteneceAEmpresa` a la suite de tenant isolation
- Cómo crear un builder de prueba
- Cuándo usar Verify (snapshot) vs asserts campo a campo
- Cómo correr la suite local: `dotnet test`, filtros por proyecto, etc.

## Flakes conocidos / backlog

Tests no deterministas detectados al correr el suite de integración en
paralelo, **diferidos** (cada uno con su causa raíz; no mezclar con otros
fixes). Descubiertos durante la verificación amplia del fix de la secuencia
de folios (`chore/flake-folio-consecutividad`).

- **`Compras.AutorizacionesEndpointsTests.Autorizar_Nivel1_FailOpen_StockTotal_TransicionaA_Cerrada_Retorna_204`**
  — falla intermitente bajo paralelismo (`Expected: 4 / Actual: 3`, y RQs
  encontradas en estado inesperado: `EnSurtido`). **REQUIERE TRIAJE: puede
  ser un BUG REAL de concurrencia de stock**, no asumir que es test-only
  hasta investigar el camino de bifurcación stock-aware / reserva.
- **Settings (`Compras.ComprasSettingsEndpointsTests.Patch_Establece_Flag_Y_Get_Posterior_Lo_Refleja`
  y `Settings.SettingsEndpointsTests.Patch_Setting_Compras_Aplica_Y_GetSchema_Lo_Refleja`)**
  — dos clases parchean el mismo setting de Compras de la empresa en paralelo
  y se pisan (clobber). Probable **aislamiento de estado en tests** (setting
  por empresa dedicada / serializar), pero confirmar que no esconde una
  condición de carrera real en el patch.

**ADRs hijo posibles**
- Política específica de mutation testing si se adopta en algún módulo
- Estrategia de performance testing (cuándo, cómo, qué métricas)
- Convenciones de tests de carga / stress
- Política de Verify snapshots: cuándo regenerar, cómo manejar cambios intencionales
