# 10 — Catálogo de Puestos y Empleados (habilitador de reglas de viáticos)

**Estado:** plan aprobado en dirección (opción a del análisis 2026-07-16); pendiente arranque de PRs.
**Owner:** Eduardo Paredes. **Módulo dueño:** Administración (persistencia en `compartido` vía `CompartidoDbContext`, ADR-0035).

---

## 1. Contexto y motivación

El flujo de viáticos de CxP está construido de punta a punta (solicitud →
tope contra `politicas_viaticos` → autorización N1/N2 → comprobación →
liberación), pero opera con interinos porque no existe master de
empleados ni de puestos:

- `IEmpleadoReadPort` y `IPuestoReadPort` de CxP son stubs NoOp
  (`PLATFORM-TODO(<EmpleadoReadPort>)`, `PLATFORM-TODO(<PuestoReadPort>)`,
  `PLATFORM-TODO(<PuestosEnAdmin>)`). `NoOpPuestoReadPort` trae 3 puestos
  hardcodeados (EJEC/GER/OPER) para destrabar políticas.
- En el frontend, `NuevaSolicitudViaticosSheet` usa `UsuarioSelector`
  (usuarios de Identidad) como interino para `empleadoId` y un input de
  UUID libre para `puestoId`. El `Responsable` de las comprobaciones de
  gastos tiene el mismo interino.
- Almacén tiene su propio `IEmpleadoReadPort` (también NoOp).

Las **políticas de viáticos se definen por puesto + tipo de destino**
(`politicas_viaticos(puesto, tipo_destino, monto_max_dia, dias_max)`,
00-levantamiento CxP §13.1 punto 3 y §7.4.2); sin catálogo real de
puestos/empleados, la validación automática del tope no puede operar y el
go-live de viáticos queda bloqueado (riesgo declarado en el levantamiento).

**Frontera (sin cambio):** los préstamos formales a empleados (códigos
`Axxxx`) siguen fuera — viven en RH/Tesorería; aquí solo se guarda el
código como referencia. CxP únicamente gestiona el pasivo-anticipo del
ciclo de viáticos (`PRESTAMO_EMPLEADO`).

## 2. Decisiones

| # | Decisión | Resolución |
|---|---|---|
| D1 | ¿Dónde vive el master? | Administración: entidades en `Millet.Administracion.Domain`, Application en `Compartido/Application/Administracion/`, tablas `compartido.puestos` y `compartido.empleados` en `CompartidoDbContext` (patrón vigente hasta que exista `AdministracionDbContext`, ver `PLATFORM-TODO(<SeriesSchemaMigrate>)`). |
| D2 | Jefe directo | Se captura en el Empleado (`JefeDirectoId`, self-FK nullable). La solicitud de viáticos lo **prellena** como autorizador N1, editable en el form (override manual permitido). |
| D3 | Carga inicial | Captura manual desde la UI de admin (volumen esperado: decenas). Carga masiva CSV se difiere; si al llenar resulta pesado, se agrega un PR de importación después. |
| D4 | Código `Axxxx` | Campo opcional de referencia `CodigoNomina` en Empleado. Sin lógica asociada; solo trazabilidad con RH/SAP. |
| D5 | Vínculo con Identidad | `UsuarioId` opcional (Guid, sin FK cross-módulo) para correlacionar el empleado con su usuario cuando captura su propia comprobación. No sustituye a Identidad ni crea usuarios. |
| D6 | Seed de puestos | La migración siembra los 3 puestos del stub (EJEC/GER/OPER) **con los mismos GUIDs** (`00000000-0000-0000-0000-00000000000{1,2,3}`) para no romper las `politicas_viaticos` ya capturadas en dev contra el seed del NoOp. |

## 3. Modelo de datos

**`compartido.puestos`** — catálogo simple, patrón `Sucursal`:
`Id`, `Clave` (business key única, ≤20), `Nombre` (≤254), `Estatus`
(`EstatusCatalogo` + check constraint), auditoría `IAuditable`.

**`compartido.empleados`**:
`Id`, `EmpresaId`, `Clave` (única por empresa, ≤20), `Nombre` (≤254),
`Email` (≤254, nullable), `PuestoId` (FK `puestos`, requerido para
viáticos pero nullable en alta), `JefeDirectoId` (self-FK, nullable),
`SucursalId` (nullable), `DepartamentoId` (nullable — lo exige el
`EmpleadoLectura` de Almacén), `UsuarioId` (nullable, D5),
`CodigoNomina` (nullable, D4), `Estatus`, auditoría.

Índices: únicos `(Clave)` en puestos y `(EmpresaId, Clave)` en empleados;
no únicos en `PuestoId`, `Estatus`, `UsuarioId`.

Los DTOs de los ports existentes **no cambian**:
- CxP `EmpleadoDto(Id, EmpresaId, Nombre, Email, PuestoId, Activo)` y
  `PuestoDto(Id, Codigo, Nombre, Activo)`.
- Almacén `EmpleadoLectura(Id, NombreCompleto, EmpresaId, DepartamentoId, EsActivo)`.

## 4. API y permisos

- **Write (admin):** `backend/src/Api/Endpoints/Administracion/PuestosEndpoints.cs`
  y `EmpleadosEndpoints.cs` — `POST /api/v1/admin/puestos`, `PATCH .../{id}`,
  `POST .../{id}/desactivar` (ídem empleados). Idempotency-Key (ADR-0020),
  Problem Details (ADR-0010).
- **Read (catálogo):** `GET /api/v1/catalogos/puestos` y
  `GET /api/v1/catalogos/empleados` (paginado, filtros `q`/`estatus`),
  permiso `compartido.catalogos.leer`, mismo estilo que
  `OrganizacionEndpoints`.
- **Permisos canónicos nuevos** bajo el namespace `00000005-*` de
  Administración (sub-namespaces nuevos, no reutilizar `-0006-*` reservado):
  `admin.puestos.gestionar` y `admin.empleados.gestionar`.
  ⚠️ Tocar `PermisosCanonicos` exige **migración en `IdentidadDbContext`**
  (HasData; sin migration el deploy aborta con `PendingModelChangesWarning`).
  Asignar los permisos nuevos al rol administrador en dev tras el deploy.

## 5. PR breakdown

| PR | Rama | Alcance | DoD |
|---|---|---|---|
| **ADM-PR1** (backend) | `admin/puestos-empleados-backend` | Entidades `Puesto`/`Empleado` + config EF + migración en `CompartidoDbContext` (con seed D6); comandos CRUD en `Compartido/Application/Administracion/{Puestos,Empleados}/`; endpoints write + read; permisos canónicos + migración `IdentidadDbContext`. | Build + unit tests verdes; CRUD e2e por API en dev; `GET /catalogos/puestos` devuelve el seed. |
| **ADM-PR2** (backend) | `admin/puestos-empleados-readports` | Adapters reales: `PuestoReadAdapter`/`EmpleadoReadAdapter` en `Compartido/Infrastructure/PublicAdapters/` (proyección desde `CompartidoDbContext` con `Bypass()` + `AsNoTracking`, patrón `SucursalReadAdapter`) para los ports de CxP **y** el `IEmpleadoReadPort` de Almacén; registro en `Program.cs`; eliminar `NoOpEmpleadoReadPort`/`NoOpPuestoReadPort` de CxP y el NoOp de Almacén; cerrar los `PLATFORM-TODO` y las filas de "Dependencias de plataforma pendientes" en los docs de CxP/Almacén. | La validación de tope de viáticos opera con puesto real; grep `PLATFORM-TODO` sin `<EmpleadoReadPort>`/`<PuestoReadPort>`/`<PuestosEnAdmin>`. |
| **ADM-FE-PR1** (frontend) | `admin/puestos-empleados-fe` | Módulo admin FE: rutas `admin/administracion/{puestos,empleados}` (bandeja + sheet de alta + detalle, clonando `modules/datos-maestros/`); hooks `usePuestos`/`useEmpleados` en `features/catalogos/api`; selectores `PuestoSelector` y `EmpleadoSelector` (`CatalogoEagerCombobox`, patrón `SucursalSelector`). | Alta/edición/desactivación funcionando en dev; selectores consumibles desde cualquier módulo. |
| **CXP-FE-PR** (frontend) | `cxp-fe/viaticos-empleado-selector` | `NuevaSolicitudViaticosSheet`: `EmpleadoSelector` reemplaza a `UsuarioSelector`; al elegir empleado se **prellenan** `puestoId` (reemplaza el input de UUID libre) y `jefeDirectoId` (N1, editable); `Responsable` de comprobaciones de gastos (caja chica/aduanales) migra a `EmpleadoSelector`. Quitar los `PLATFORM-TODO` correspondientes del FE. | Solicitud de viáticos e2e en dev con empleado/puesto reales y tope validado contra `politicas_viaticos`. |

Sin cambios de infra: **no hay DbContext nuevo** (no aplica el checklist
de `Program.cs`/`deploy-app-dev.yml`), no hay settings nuevos, no hay
topics de Service Bus.

## 6. Riesgos y cuidados

- **Migración de permisos**: la de `IdentidadDbContext` va en el mismo
  PR que el cambio a `PermisosCanonicos` (ADM-PR1) — incidente conocido.
- **GUIDs del stub de puestos**: cualquier política de viáticos capturada
  en dev referencia los GUIDs del NoOp; el seed D6 los preserva. Verificar
  en dev tras ADM-PR2 que `politicas_viaticos` resuelve nombre de puesto.
- **Dato de negocio pendiente (fuera de código):** los montos reales de
  `politicas_viaticos` (tope diario por puesto/destino) siguen pendientes
  con Dirección/RH (levantamiento CxP §13.1 punto 3). El catálogo
  desbloquea la mecánica, no sustituye esa definición.
- **Almacén**: el destinatario de salidas sigue resolviéndose por
  `IUsuarioReadPort` (ADR-0042); este catálogo no lo cambia.
