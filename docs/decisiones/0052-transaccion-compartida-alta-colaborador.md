# ADR-0052: Transacción compartida Identidad + Compartido para el alta de colaborador (excepción a ADR-0030)

- **Estado**: Aceptada (2026-09-24, Geovany González; pendiente de ratificación de Eduardo Paredes como owner)
- **Fecha**: 2026-09-23
- **Decisores**: Eduardo Paredes (owner), Geovany González (backend)
- **Etiquetas**: persistencia, ef-core, identidad, administración, excepción

> Excepción acotada a [ADR-0030](./0030-multi-dbcontext-por-modulo.md). No lo
> reemplaza: la regla general (un DbContext por módulo, sin transacciones
> entre contextos, comunicación por outbox) sigue vigente para todo lo demás.

## Contexto y problema

F1-ADM-01 unifica el alta de **Empleado** (`CompartidoDbContext`, tabla
`compartido.empleados`) y **Usuario** (`IdentidadDbContext`, esquema
`identidad`) en una sola operación del administrador: el wizard de alta de
colaborador (`POST /api/v1/admin/colaboradores`). En la misma operación se
crean además la preferencia, la sucursal y el rol del usuario.

ADR-0030 acepta explícitamente que **no haya transacciones entre
contextos** y que la comunicación entre módulos sea asíncrona por outbox
(ADR-0009). Aplicado aquí, el alta quedaría así: se guarda el Empleado, se
publica un evento y otro handler crea el Usuario más tarde (o al revés).

Ese diseño deja una ventana en la que existe un Empleado sin su Usuario, o
un Usuario sin su Empleado:

- **CxP/Viáticos** resuelve al solicitante y al autorizador por
  `Empleado.UsuarioId` (`EmpleadoReadPortAdapter`). Un empleado con acceso
  pero sin usuario vinculado no puede solicitar ni autorizar viáticos.
- El administrador necesita una **respuesta síncrona**: el wizard muestra
  el resultado (usuario creado, rol asignado, o el error de validación que
  lo impidió) en la misma pantalla.
- Si el segundo paso falla (clave de empleado duplicada, rol inválido,
  sucursal de otra empresa), hay que **compensar** el primero, y la
  compensación también puede fallar.

## Drivers de la decisión

- El par Empleado ↔ Usuario es **una sola entidad de negocio** (la persona
  que trabaja en Millet y entra al ERP). Un estado a medias es inválido.
- Respuesta síncrona y atómica para el administrador.
- Auditoría completa y consistente: si se revierte, no debe quedar rastro
  de un usuario que nunca existió.
- No romper la frontera modular para el resto del sistema.
- Ambos contextos ya usan **la misma cadena de conexión** `Postgres`
  (`Program.cs`) y la misma base física (ADR-0005).

## Opciones consideradas

1. **Transacción compartida**: `CompartidoDbContext` se une a la conexión
   y a la transacción de `IdentidadDbContext` durante el alta.
2. **Eventos por outbox** (la regla de ADR-0030): Empleado primero, evento,
   Usuario después.
3. **Compensación síncrona (saga local)**: se guarda un contexto, luego el
   otro, y si el segundo falla se borra lo del primero.
4. **Mover `Empleado` a `IdentidadDbContext`** (o `Usuario` a Compartido).

## Decisión

Se adopta la **opción 1**, con alcance cerrado.

**Mecanismo** (validado en el spike F0,
`AltaColaboradorTransaccionSpikeTests`):

```csharp
await using var tx = await identidad.Database.BeginTransactionAsync(ct);
await compartido.Database.CloseConnectionAsync();
compartido.Database.SetDbConnection(identidad.Database.GetDbConnection());
await compartido.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
// … handlers por MediatR: CrearUsuario, CrearEmpleado, AsignarRol, AsignarSucursal …
await tx.CommitAsync(ct);   // sin commit, el dispose hace rollback
```

Está encapsulado en una sola clase,
`Identidad/Infrastructure/TransaccionColaborador.cs` (`EjecutarAsync`). Los
handlers no conocen la transacción: siguen llamando a su propio
`SaveChangesAsync`.

**Alcance permitido.** Solo estas operaciones del ciclo de vida del
colaborador pueden usar `TransaccionColaborador`:

| Operación | Fase del plan |
|---|---|
| Alta de colaborador (caminos A, B y C) | F3, F4 |
| "Dar acceso" a un empleado existente | F6 |
| Baja / reactivación de colaborador (empleado + usuario) | F6 |
| "Convertir en colaborador" (usuario existente sin empleado) | F8 |

Cualquier otro caso que quiera escribir en dos contextos en la misma
transacción requiere su propio ADR.

**Condiciones.**

1. Los dos contextos usan la **misma cadena de conexión**. Si alguna vez
   Compartido o Identidad se mueven a otra base, esta excepción deja de
   aplicar y el alta tiene que pasar a la opción 3.
2. Solo se unen **Identidad y Compartido**. No se agregan más contextos a
   la transacción.
3. **Nada externo dentro de la transacción.** Las llamadas a Microsoft
   Graph (crear cuenta, enviar correo) van **después del commit** por
   outbox (ADR-0009), con un worker idempotente. La transacción solo
   escribe en Postgres.
4. La auditoría usa una **correlación común** (`IAuditCorrelationContext`)
   para que todas las filas del alta queden agrupadas en la bitácora.

## Consecuencias

**Positivas**

- Empleado, Usuario, rol, sucursal, auditoría y filas de outbox hacen
  commit o rollback juntos. El spike F0 demostró que las filas que agregan
  los interceptores (`AuditSaveChangesInterceptor`,
  `OutboxSaveChangesInterceptor`) caen en la misma transacción.
- Sin código de compensación ni estados intermedios visibles para
  Viáticos.
- Los handlers existentes (`CrearUsuarioCommand`, `CrearEmpleadoCommand`,
  `AsignarRolAUsuarioCommand`, `AsignarUsuarioASucursalCommand`) se
  reutilizan sin cambios, con sus validaciones.
- Un evento de outbox emitido en el alta (camino B:
  `ColaboradorCuentaEntraSolicitadaEvent`) solo se publica si el alta hizo
  commit, así que no se crean cuentas huérfanas en Entra.

**Negativas**

- Hay acoplamiento físico entre dos contextos: el orquestador vive en
  `Identidad` y depende de `CompartidoDbContext`. `Identidad` ya
  referenciaba el dominio de Administración, así que no se agrega una
  dependencia de proyecto nueva.
- La excepción depende de un detalle de despliegue (la misma base). Queda
  documentado en la condición 1.
- El orden importa: Compartido no puede tener su conexión abierta antes de
  unirse. `TransaccionColaborador` lo resuelve cerrándola primero, y por
  eso el mecanismo **no se usa suelto** fuera de esa clase.

## Descartadas

**Opción 2 (outbox)**: deja una ventana Empleado-sin-Usuario que rompe
Viáticos y no da respuesta síncrona al administrador. Además, los errores
de validación del segundo paso (clave duplicada, rol inválido) llegarían
de forma asíncrona, cuando la pantalla ya confirmó el alta.

**Opción 3 (compensación)**: más código y más modos de falla (la
compensación puede fallar y dejar basura). La auditoría registraría un
usuario creado y borrado que nunca existió para el negocio. Queda como
plan B si se incumple la condición 1.

**Opción 4 (mover la entidad)**: `Empleado` es catálogo transversal que
consumen varios módulos (Viáticos, Compras, Almacén) y `Usuario` es la
frontera de seguridad de Identidad. Mover cualquiera de los dos rompe la
alineación esquema ↔ DbContext de ADR-0030 para muchos más consumidores
que los que afecta esta excepción.

## Notas de implementación

- Clase: `backend/src/Identidad/Infrastructure/TransaccionColaborador.cs`
  (scoped; comparte las instancias de DbContext del request).
- Orquestador: `backend/src/Identidad/Application/Colaboradores/AltaColaboradorCommand.cs`.
- Pruebas que sostienen la decisión:
  - `AltaColaboradorTransaccionSpikeTests` (F0): commit, rollback y
    control negativo sin enlistamiento.
  - `ColaboradoresEndpointsTests` (F3): rollback de extremo a extremo
    (una clave de empleado duplicada no deja el usuario creado) y
    correlación única en auditoría.
- Condición 3 (nada externo en la transacción): se verifica en F4, donde el
  evento de outbox debe persistirse o revertirse junto con el alta.
- Trazabilidad: plan `15-plan-unificar-usuarios-empleados.md` §5.4 y §7
  (F2½); evidencias `16-evidencia-f0-spike-transaccion.md` y
  `17-evidencia-f1-dominio.md` §4.

**Verificación de aceptación (2026-09-24, cierre ADM-01, criterio 01-10)**

- Alcance: `TransaccionColaborador` sólo se usa en `AltaColaboradorCommand`,
  `DarAccesoColaboradorCommand` y `GestionColaboradorCommands` (baja /
  reactivación), dentro de la tabla de alcance permitido.
- Condición 3: las consultas al directorio (`BuscarPorCorreoAsync`) ocurren
  antes de abrir la transacción; la creación de cuenta y el correo los hace
  `ProvisionCuentaEntraWorker` después del commit.
- Atomicidad de extremo a extremo: `ColaboradoresEndpointsTests`
  (`Falla_Del_Empleado_No_Deja_Usuario_Creado`, `Rol_Inexistente_No_Deja_Filas_Parciales`,
  `Sucursal_Fuera_De_La_Empresa_No_Deja_Filas_Parciales`, `Dato_Invalido_No_Deja_Filas_Parciales`)
  y correlación única de auditoría; suite `Api.IntegrationTests` completa en
  PostgreSQL aislado.

**Cambios en otros ADRs**

- ADR-0030: agregar una addenda que enlace esta excepción.
