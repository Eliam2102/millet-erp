---
title: "Identidad — plan de permisos personalizados por usuario"
fecha: 2026-10-01
estado: implementado-pendiente-de-revision
rama: fix/permisos-personalizados-por-usuario
tags: [identidad, rbac, permisos, roles, plan]
---

# Permisos personalizados por usuario (override sobre el rol)

## 1. Problema

Hoy los permisos de un usuario salen **solo de su rol** en la empresa
(`UsuarioEmpresaRol` → `RolPermiso` → `Permiso`; ver
`Identidad/Infrastructure/PermissionLoader.cs`). Dos usuarios con el mismo rol
(p. ej. "Administrador") tienen exactamente los mismos permisos. Para que uno
tenga distinto alcance hay que crear un rol nuevo por persona, lo que no
escala y ensucia el catálogo de roles.

**Requisito:** dos usuarios con el mismo rol deben poder tener permisos
distintos, sin crear un rol por usuario.

## 2. Decisión de diseño (mínima)

El **rol sigue siendo la base**. Se agrega, por `(usuario, empresa)`, una lista
de **excepciones** sobre esa base:

```
efectivos = (permisos del rol  ∪  concedidos)  \  denegados
```

- **Conceder** (`Grant`): añade un permiso que el rol no trae.
- **Denegar** (`Deny`): quita un permiso que el rol sí trae. **Deny gana** si
  el mismo permiso aparece en ambas listas (no debería ocurrir; la API lo
  impide).
- Sin excepciones, el comportamiento es idéntico al actual (compatibilidad
  total, sin migración de datos).

No se crea rol por usuario, no se duplica la matriz del rol y no se toca
`UsuarioEmpresaRol` (rol único por usuario/empresa, ADR-0051). Reutiliza
`Permiso`, `IAuditable`, `IPerteneceAEmpresa`, `IPermissionCache` y los
permisos canónicos existentes.

Por qué no otras opciones:
- *Rol por usuario*: explosión de roles, difícil de auditar.
- *Varios roles por usuario*: no personaliza a nivel permiso y rompe el
  supuesto "rol único" ya documentado.

## 3. Modelo de datos

Nueva entidad `UsuarioPermisoOverride` en `Identidad/Domain/` (esquema
`identidad`):

| Campo | Tipo | Nota |
|---|---|---|
| `Id` | Guid | `BaseEntity` |
| `UsuarioId` | Guid | FK a `Usuario` |
| `EmpresaId` | Guid | `IPerteneceAEmpresa` (filtro global, ADR-0011) |
| `PermisoId` | Guid | FK a `Permiso` |
| `Efecto` | enum `EfectoPermiso { Conceder = 1, Denegar = 2 }` | |
| `Motivo` | string? (máx 500) | justificación opcional |
| `AsignadoPorUsuarioId` | Guid? | quién lo configuró |

Índice único `(usuario_id, empresa_id, permiso_id)`: un permiso tiene un solo
efecto por usuario/empresa. Implementa `IAuditable` (el interceptor de
auditoría ya registra altas/bajas/cambios).

Migración EF aditiva en `IdentidadDbContext` (tabla nueva, sin tocar las
existentes).

## 4. Resolución de permisos (un solo punto de cambio)

`PermissionLoader.LoadForUserInEmpresaAsync` es el único lugar que calcula los
permisos efectivos humanos; lo consumen el handler de autorización, el login
(`LoginOrchestrator`) y `CurrentUserPermissions`. Se modifica **solo ahí**:

1. Obtener permisos del rol activo (consulta actual).
2. Obtener overrides del usuario en la empresa.
3. `efectivos = (rol ∪ Conceder) \ Denegar`.

Los service principals (`UsuarioServicioPermiso`) **no cambian**.

## 5. Invalidación de caché

`IPermissionCache` (TTL 5 min, ADR-0007) ya tiene `InvalidateAsync(usuario,
empresa)`. Todo alta/baja/cambio de override debe invalidar esa entrada para
que el cambio aplique de inmediato (no esperar 5 min). Además:

- Al **cambiar de rol** (decisión final del owner, reemplaza la idea de
  "conservar"): las excepciones del `(usuario, empresa)` **se borran** cuando el
  rol efectivamente cambia:
  - `AsignarRolAUsuarioCommand` (se agrega una asignación nueva) y
    `RevocarRolDeUsuarioCommand` eliminan los `UsuarioPermisoOverride` de ese
    `(usuario, empresa)` **en la misma transacción** (`SaveChanges`).
  - Reasignar el mismo rol ya responde 409 (`USUARIO_ASIGNACION_DUPLICADA`) y no
    borra nada.
  - Se invalida `IPermissionCache.InvalidateAsync(usuario, empresa)` y se publica
    `UsuarioPermisosOverrideActualizadosEvent` (0/0) **solo si se borró algo**.
  - La UI avisa antes de confirmar: "se perderán N permiso(s) personalizado(s)"
    (N > 0) al asignar un rol y al revocar.
- Al **cambiar la matriz del rol** (`AsignarPermisosARolCommand`): hoy no
  invalida caché de los usuarios del rol (depende del TTL). No es parte de
  este fix; dejar nota en el PR.

## 6. Reglas de seguridad (obligatorias)

1. **Sin escalada de privilegios:** un usuario solo puede *conceder* permisos
   que él mismo posee en esa empresa. Denegar sí se permite sobre cualquier
   permiso.
2. **No auto-edición:** nadie edita sus propios overrides.
3. **Solo el super-admin está protegido:** no se admiten excepciones sobre un
   usuario cuyo rol en la empresa sea `super-admin` (código
   `RevocarRolDeUsuarioHandler.SuperAdminCodigo`, el mismo del bootstrap; su
   matriz se gestiona por bootstrap). Los demás roles de sistema
   (`es_del_sistema = true`: `admin-catalogos`, `admin-compras`,
   `admin-datos-maestros`, `admin-identidad`, `admin-organizacional`,
   `auditor`) **sí** admiten excepciones.
4. **Cuentas técnicas / service principals** quedan fuera.
   *(Implementación: además, el actor debe tener `gestionar-permisos` en la
   empresa objetivo, resuelto con el loader; si no, 403
   `PERMISOS_OVERRIDE_SIN_ALCANCE_EMPRESA`. Códigos: escalada y auto-edición →
   403; usuario inactivo, sin rol o super-admin → 422.)*
5. El `PermisoId` debe existir en `identidad.permisos`; el usuario debe
   existir, estar activo y tener rol en esa empresa.
6. Los overrides **nunca** exponen ni requieren secretos.

## 7. API (Identidad, `/api/v1/identidad/usuarios`)

Nuevo permiso canónico en `PermisosCanonicos` (con su seed/migración de
catálogo, mismo patrón que `IdentidadUsuariosDesactivar`):

- `identidad.usuarios.gestionar-permisos` — administrar excepciones.
- Lectura de permisos efectivos bajo `identidad.usuarios.leer` (existente).

Endpoints (MediatR + FluentValidation, Problem Details, Idempotency-Key en
mutaciones, ETag/If-Match donde aplique; mismo patrón que
`UsuariosEndpoints.cs`):

| Método | Ruta | Propósito |
|---|---|---|
| GET | `/{id}/empresas/{empresaId}/permisos` | Permisos efectivos con su **origen**: `Rol` / `Concedido` / `Denegado` |
| PUT | `/{id}/empresas/{empresaId}/permisos-override` | Reemplaza atómicamente la lista de excepciones (batch, como `AsignarPermisosARol`) |
| DELETE | `/{id}/empresas/{empresaId}/permisos-override` | Quita todas las excepciones (vuelve al rol) |

Validaciones del PUT: sin duplicados, sin permiso en ambos efectos, reglas de
la sección 6. Publica `UsuarioPermisosOverrideActualizadosEvent` (Outbox,
ADR-0009) con `usuario_id`, `empresa_id` y conteos — **sin** listar secretos.

## 8. Frontend (`frontend/src/modules/identidad`)

**Cambio de alcance (opción B del owner):** el panel **"Permisos personalizados"**
es un componente **independiente** (`PermisosPersonalizadosPanel`, props
`usuarioId` y `empresaId`, sin depender de ninguna ruta) y se monta en el
**detalle del empleado** (Administración → Empleados → detalle → pestaña "Roles y
accesos", debajo de la cuenta de acceso y de `RolesPorEmpresaPanel`), solo cuando
el empleado ya tiene `usuarioId` y para la empresa activa. Razón: `/admin/usuarios`
puede quedar oculta del menú y la función no debe depender de esa pantalla. No se
montó en `UsuarioDetalle.tsx` (opcional, no necesario). El panel:

- Matriz agrupada por módulo (reusar la lógica de `MatrizPermisos.tsx`).
- Cada permiso es un **interruptor** (`role="switch"`) que muestra el estado
  **efectivo** (ON = la persona lo tiene). El modelo guardado no cambia (una fila
  Conceder/Denegar por permiso): apagar un permiso del rol = Denegado; encender
  uno que el rol no incluye = Concedido; volver al valor del rol borra la
  excepción del borrador. Marca "Personalizado: añadido/quitado" (verde/rojo) y
  botón ↺ por fila. Sin escalada: un permiso que el rol no incluye y que el actor
  no posee no se puede encender (switch deshabilitado con explicación).
- **Interruptor por grupo (módulo)** con estado mixto (`aria-checked="mixed"`):
  apagar = todo efectivo OFF (deniega lo del rol, descarta lo concedido); encender
  = todo ON (concede solo lo que el actor posee, quita denegaciones; avisa cuántos
  permisos se omitieron por falta de posesión). Botón ↺ "Restablecer grupo" borra
  del borrador las excepciones del grupo. Todo opera sobre el borrador y se
  persiste con "Guardar cambios".
- El panel solo se deshabilita para auto-edición, usuario sin rol y rol
  `super-admin` (`rolEsSuperAdmin` del GET de efectivos; reemplaza a
  `rolEsDelSistema`).
- Resumen "N concedidos / M denegados respecto al rol".
- Botón **Restablecer al rol** (DELETE) con confirmación.
- Avisos de cambio de rol (sección 5): `RolesPorEmpresaInlineForm` (asignar) y el
  diálogo de revocar de `RolesPorEmpresaPanel` muestran cuántas excepciones se
  perderán (consulta de efectivos de la empresa elegida).
- Guardar envía el PUT con `Idempotency-Key`; manejar 403/409/422 con toasts
  (patrón existente). Visible/editable solo con
  `identidad.usuarios.gestionar-permisos`.
- Hooks nuevos en `api/usuarios.ts` y claves en `api/keys.ts`.

## 9. Pruebas

**Unitarias (backend):**
- Loader: sin overrides = igual que hoy; Conceder añade; Denegar quita;
  conceder y denegar combinados; rol inactivo → nada; empresa distinta no
  mezcla overrides.
- Handler PUT: sin duplicados, sin ambos efectos, escalada rechazada,
  auto-edición rechazada, super-admin protegido, permiso inexistente.

**Integración HTTP (PostgreSQL):**
- Dos usuarios con el **mismo rol** y overrides distintos obtienen permisos
  efectivos distintos en `/me`/endpoint protegido (el caso del requisito).
- Un endpoint protegido responde 403 tras denegar su permiso y 200 tras
  conceder uno que el rol no trae; el cambio aplica sin esperar el TTL.
- Aislamiento multiempresa de overrides; 403 sin
  `gestionar-permisos`.

**Frontend:** smoke tests del panel (estados heredado/concedido/denegado,
restablecer, guardado, error 409/403) + `typecheck`.

## 10. Documentación

- ADR nuevo `0053-permisos-personalizados-por-usuario.md` (formato
  `docs/decisiones/template.md`): contexto, decisión (override Grant/Deny),
  alternativas descartadas, consecuencias.
- Actualizar `Identidad.md` de la nota Obsidian solo si el usuario lo pide;
  en el repo, actualizar este plan con el resultado.
- Guía de pruebas manuales en la UI: `docs/handoff/34-pruebas-ui-permisos-personalizados.md` (el 33 está reservado a ADM-09 en otra rama).

## 11. Fases y commits propuestos

| Fase | Alcance | Commit |
|---|---|---|
| 1 | Entidad, enum, configuración EF, migración, permiso canónico + seed | `feat(identidad): modelo de excepciones de permisos por usuario` |
| 2 | `PermissionLoader` con override + invalidación de caché + pruebas unitarias | `feat(identidad): resolver permisos efectivos con excepciones por usuario` |
| 3 | Commands/queries, validators, endpoints, evento Outbox + pruebas HTTP | `feat(identidad): API de permisos personalizados por usuario` |
| 4 | Panel UI + hooks + pruebas frontend | `feat(identidad): panel de permisos personalizados en detalle de usuario` |
| 5 | ADR, guía de pruebas UI, evidencia | `docs(identidad): ADR y guía de pruebas de permisos personalizados` |

## 11.1 Resultado de la implementación (2026-10-01)

Todo queda **sin commitear** en `fix/permisos-personalizados-por-usuario`.

Desviaciones menores respecto al plan:

- Una sola migración EF (`AgregarPermisosOverridePorUsuario`) con la tabla y el
  seed del permiso `identidad.usuarios.gestionar-permisos`
  (`00000002-0002-0000-0000-00000000000e`).
- `Efecto` se serializa como cadena (`"Conceder"`/`"Denegar"`) en la API; el origen
  en el GET es `Rol`/`Concedido`/`Denegado` y solo lista esas filas (el catálogo
  completo viene de `GET /permisos`; la base del rol, de `GET /roles/{id}`).
- Sin `ETag/If-Match` (el PUT reemplaza por lote y no hay versión de agregado).
- El PUT valida duplicados con FluentValidation (400); un permiso repetido cubre
  también "mismo permiso en ambos efectos".
- `AsignarRolAUsuario`/`RevocarRolDeUsuario` ahora también invalidan la caché del
  usuario/empresa aunque no hubiera excepciones.
- Editar la matriz de un rol sigue sin invalidar la caché (no es parte del fix;
  dejar nota en el PR).

Pruebas: ver el reporte de la fase (unitarias `Identidad.UnitTests`, integración
HTTP `PermisosPersonalizadosEndpointsTests`, frontend
`PermisosPersonalizadosPanel.smoke.test.tsx` y
`RolesPorEmpresaPanel.cambioRol.test.tsx`).

### 11.2 Ajuste posterior (2026-10-01): protección solo del super-admin y toggles

Tras probar en la UI, el owner pidió tres cambios (sin migraciones nuevas):

1. **Proteger solo al super-admin.** Backend: `ObtenerPermisosEfectivosUsuarioHandler`
   y `ActualizarPermisosOverrideUsuarioHandler` comparan el código del rol con
   `RevocarRolDeUsuarioHandler.SuperAdminCodigo` en vez de `es_del_sistema`; el DTO
   `PermisosEfectivosUsuarioResponse.RolEsDelSistema` pasa a `RolEsSuperAdmin`
   (tipos TS y consumidores actualizados). El DELETE no tenía chequeo de rol (sin
   cambios).
2. **Toggles** en vez de selectores de tres estados (frontend).
3. **Interruptor por grupo** con estado mixto y ↺ de grupo (frontend).

Desviación: no existía un componente Switch en `@/components/ui`; se añadió un
`Interruptor` local (botón `role="switch"`) dentro del panel, sin dependencias nuevas.
Pruebas: unitarias `PermisosOverrideHandlersTests` (rol de sistema no super-admin
editable; GET marca `RolEsSuperAdmin`), integración HTTP
`PermisosPersonalizadosEndpointsTests` (admin-catalogos editable; super-admin 422) y
`PermisosPersonalizadosPanel.smoke.test.tsx` reescrito para toggles/grupos. ADR:
[0053](../../decisiones/0053-permisos-personalizados-por-usuario.md). Guía de pruebas
manuales: [`docs/handoff/34-pruebas-ui-permisos-personalizados.md`](../../handoff/34-pruebas-ui-permisos-personalizados.md).

## 12. Fuera de alcance

- Varios roles por usuario; permisos por sucursal (sigue en ADR-0051 con
  `UsuarioSucursal`); invalidación de caché al editar la matriz de un rol;
  expiración/temporalidad de excepciones; aprobación de dos personas.
