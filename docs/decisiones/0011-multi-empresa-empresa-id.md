# ADR-0011: Multi-empresa con `empresa_id` como columna y esquema compartido

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: arquitectura, multi-tenant, base-de-datos, fundación

## Contexto y problema

El grupo Millet opera con varias razones sociales (RFCs) que comparten
infraestructura, equipos administrativos y procesos. Un mismo usuario (por
ejemplo, un contador) puede operar en varias empresas con responsabilidades
distintas en cada una. La operación es de un mismo grupo corporativo: comparten
sistemas y la frontera entre empresas es lógica, no política.

Necesitamos una estrategia de aislamiento de datos por empresa que:

- Mantenga aislamiento lógico estricto (un usuario en empresa A no debe ver datos de empresa B salvo permiso explícito)
- Sea barata operacionalmente (una sola BD, un solo backup, un solo despliegue)
- Permita catálogos compartidos entre empresas (catálogo SAT, tipos de cambio del DOF, etc.)
- Permita usuarios cross-empresa con roles distintos en cada una

## Drivers de la decisión

- Aislamiento de datos por empresa garantizado por convención y por código (no por disciplina humana)
- Operación simple: una sola BD, un solo deploy, no multiplicar infra por empresa
- Soporte para usuarios que operan en varias empresas (escenario común en grupos corporativos)
- Catálogos nacionales y de SAT compartidos (no replicados por empresa)
- Performance aceptable: el filtro `empresa_id` debe ser barato vía índices

## Opciones consideradas

1. `empresa_id` como columna en cada tabla relevante + global query filter (aislamiento lógico)
2. Esquema PostgreSQL por empresa: `empresa_a.cfdis`, `empresa_b.cfdis` (aislamiento físico)
3. Esquema `{empresa}_{modulo}` (combinatorial: aislamiento físico + módulos)
4. Una BD por empresa (aislamiento máximo)
5. Single-tenant: una instancia del sistema por empresa

## Decisión

Se adopta la **opción 1: `empresa_id` como columna con global query filter de
EF Core**, complementada con un esquema `compartido` para datos
cross-empresa.

### Esquemas de BD

A los esquemas existentes (definidos en ADR-0005) se agrega:

- **`compartido`**: catálogos nacionales y datos cross-empresa
  - `compartido.empresas` (catálogo de las RFCs del grupo: `id`, `rfc`, `razon_social`, `nombre_comercial`, `regimen_fiscal`, `activa`, etc.)
  - `compartido.catalogo_sat_*` (regímenes fiscales, productos/servicios SAT, unidades, monedas SAT, etc.)
  - `compartido.tipos_de_cambio_dof` (snapshot diario del DOF de Banxico)
  - `compartido.codigos_postales` (catálogo SAT 4.0)

El esquema `identidad` (definido en ADR-0007) sigue siendo **global** (no por
empresa): `usuarios`, `roles`, `permisos` aplican a todo el sistema. La
relación usuario-rol-empresa se modela en una nueva tabla (ver más abajo).

### Convención `IPerteneceAEmpresa`

- Toda entidad de dominio que tenga datos pertenecientes a una empresa específica implementa `IPerteneceAEmpresa`
- Esa interfaz exige una columna `empresa_id` (FK a `compartido.empresas`)
- El `BaseDbContext` aplica un **global query filter** automáticamente: cualquier `SELECT` filtra por la empresa actual del usuario
- Un `SaveChangesInterceptor` asigna `empresa_id` automáticamente en `INSERT` desde el contexto del request — el dev no lo escribe manualmente
- Validación adicional en el interceptor: ningún `INSERT/UPDATE` puede referenciar (FK) a un registro de otra empresa. Si se detecta, se lanza excepción

### Entidades por categoría

**Por empresa (con `empresa_id`)**:
- Clientes, proveedores, cotizaciones, pedidos, CFDIs, complementos de pago
- Cuentas por cobrar, cuentas por pagar, antigüedad de saldos
- Catálogo de cuentas contables (cada empresa puede tener el suyo)
- Centros de costo
- Series y folios de CFDI (cada RFC con sus series)
- Almacenes, ubicaciones, existencias
- Activos fijos
- Pólizas y movimientos contables
- Asientos
- Audit log (con `empresa_id` para filtrar; ver actualización a ADR-0008)

**Globales (sin `empresa_id`)**:
- Catálogo SAT: regímenes fiscales, productos/servicios, unidades, monedas SAT
- Tipos de cambio del DOF
- Códigos postales (SAT 4.0)
- Tabla `compartido.empresas` misma
- `identidad.usuarios`, `identidad.roles`, `identidad.permisos`

### Cambio en el modelo de identidad (ADR-0007)

La tabla `usuario_roles` se renombra a **`usuario_empresa_roles`** con clave
compuesta `(usuario_id, empresa_id, rol_id)`. Razón: un mismo usuario puede
tener roles distintos en empresas distintas. Ejemplo: María García es
`Cobrador` en Empresa A y `Auditor` en Empresa B.

La existencia de cualquier fila para `(usuario_id, empresa_id)` también
funciona como "el usuario tiene acceso a esa empresa". No se necesita una
tabla `usuario_empresas` separada.

### Source de `empresa_id` actual

- El JWT del API agrega un claim `current_empresa_id` al login
- Si el usuario tiene acceso a una sola empresa, se asigna automáticamente
- Si tiene acceso a varias, la UI le obliga a seleccionar una antes de proceder
- Cambiar de empresa = re-emitir el token validando que el usuario tenga roles en la empresa destino

### UX de selección de empresa

- Selector de empresa siempre visible en el header del frontend (cuando el usuario tiene acceso a >1)
- Default al primer login: la primera empresa por nombre alfabético
- Defaults posteriores: la última empresa usada (preferencia almacenada en `identidad.usuario_preferencias`)
- Cambio de empresa: provoca re-emisión del JWT y refresh del estado del frontend (las queries en cache se invalidan)

### Filtro automático en queries

```csharp
// En BaseDbContext.OnModelCreating:
foreach (var entityType in modelBuilder.Model.GetEntityTypes())
{
    if (typeof(IPerteneceAEmpresa).IsAssignableFrom(entityType.ClrType))
    {
        // Aplicar filtro global: WHERE empresa_id = @currentEmpresaId
        // (la implementación usa Expression Trees para construirlo)
    }
}
```

### Validación de integridad cross-empresa

En el `SaveChangesInterceptor`:

- Antes de cada `INSERT/UPDATE` sobre una entidad `IPerteneceAEmpresa`:
  - Verificar que `empresa_id` coincide con `currentEmpresaId`
  - Verificar que ninguna FK apunta a un registro de otra empresa
- Si la verificación falla, se lanza `CrossTenantViolationException` (resulta en HTTP 403 con código de error específico)

## Consecuencias

**Positivas**
- Operación simple: una sola BD, una sola conexión, un solo backup
- Aislamiento garantizado por código (query filter automático), no por disciplina
- Catálogos compartidos sin replicación (un solo lugar donde el catálogo SAT vive)
- Soporte natural a usuarios cross-empresa con roles distintos
- Migraciones siguen ejecutándose una sola vez (no N veces, una por empresa)
- Performance buena con índices en `empresa_id` (típicamente la primera columna en índices compuestos)
- Fácil agregar empresas nuevas: insertar fila en `compartido.empresas` y asignar usuarios/roles
- Reportes consolidados (cuando se requieran y el usuario tenga permiso) son consultas SQL normales con `empresa_id IN (...)` o sin filtro

**Negativas**
- Aislamiento lógico, no físico: un bug en el query filter o un SQL crudo mal escrito podría exponer datos cross-empresa. Mitigado por: tests automatizados que validan el filtro, prohibición de SQL crudo sin revisión, code review estricto en cualquier query que usa `IgnoreQueryFilters()`
- Tablas crecen con todas las empresas juntas; performance puede degradarse con volúmenes muy grandes en una empresa específica. Mitigable con particionado por `empresa_id` si llega el caso
- Backups y restauraciones son globales: no se puede restaurar solo una empresa fácilmente (mitigable con exports lógicos por empresa si se requiere)
- Operaciones cross-empresa requieren bypass explícito del filtro (ej. reporte consolidado del grupo). Hay que documentar el patrón y restringirlo a permiso `compartido.cross_empresa.leer`

## Descartadas

**Esquema por empresa (`empresa_a.cfdis`)** y **esquema combinado
`{empresa}_{modulo}`**. Aislamiento físico real, pero costo operacional muy
alto (N×M esquemas, migraciones repetidas, gestión compleja). Solo se justifica
cuando los tenants son externos (clientes en un SaaS) y necesitan
aislamiento del estilo "su DB es legalmente suya". No es el caso de un grupo
corporativo del mismo dueño.

**BD por empresa**. Máximo aislamiento, máximo costo. Multiplicaría todo: App
Service connections, backups, SignalR, monitoreo. Innecesario para empresas
del mismo grupo.

**Single-tenant** (una instancia del sistema por empresa). Multiplicaría toda
la infra y dificultaría la operación. Además bloquearía el caso de usuarios
cross-empresa.

## Notas de implementación

**Tablas y esquemas**
- Crear esquema `compartido` con tabla `empresas` (seed con las RFCs del grupo Millet)
- Crear catálogos SAT en `compartido` (carga inicial vía seed o sync periódico contra el WS del SAT)
- Crear tabla `compartido.tipos_de_cambio_dof` (job nocturno fetcheará el DOF; ver ADR-0014)

**Convenciones de código**
- Interfaz marker `IPerteneceAEmpresa` con propiedad `Guid EmpresaId { get; set; }`
- `BaseDbContext` aplica global query filter automáticamente para todas las entidades que implementan la interfaz
- `EmpresaContextSaveChangesInterceptor` asigna `empresa_id` en INSERT y valida cross-empresa en UPDATE
- Excepción `CrossTenantViolationException` mapeada a HTTP 403 con código `cross_empresa_violation` (Problem Details, ADR-0010)
- Atributo `[BypassEmpresaFilter]` para queries explícitamente cross-empresa (requiere permiso `compartido.cross_empresa.leer`)

**Frontend**
- Componente `<SelectorEmpresa />` en el header global cuando `userEmpresas.length > 1`
- Hook `useCurrentEmpresa()` expone la empresa actual desde el contexto de auth
- Cambio de empresa: llama a endpoint `POST /api/identidad/sesion/cambiar-empresa`, recibe nuevo JWT, invalida React Query cache, refresh
- Persistir última empresa usada en `identidad.usuario_preferencias`

**Tests**
- Suite específica de "tenant isolation tests" que verifican:
  - Que un usuario en empresa A no ve datos de empresa B vía SELECT
  - Que un INSERT no puede asignar `empresa_id` distinto al del contexto
  - Que un UPDATE no puede mover un registro entre empresas
  - Que las FK no pueden apuntar cross-empresa

**ADRs afectadas (ya actualizadas)**
- ADR-0005: agrega esquema `compartido` y referencia a esta ADR para multi-empresa
- ADR-0007: `usuario_roles` → `usuario_empresa_roles`; JWT del API agrega `current_empresa_id`
- ADR-0008: `audit_log` agrega columna `empresa_id`

**ADRs hijo posibles**
- Política de carga/sync del catálogo SAT
- Estrategia de tipos de cambio del DOF (frecuencia, fallback, histórico)
- Reportes consolidados cross-empresa (cuando surjan los primeros casos)
