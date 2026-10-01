# ADR-0053: Permisos personalizados por usuario como excepciones sobre el rol (Conceder/Denegar)

- **Estado**: Propuesta (pendiente de ratificación del owner)
- **Fecha**: 2026-10-01
- **Decisores**: Eduardo Paredes (owner)
- **Etiquetas**: identidad, rbac, permisos, seguridad

> Refina [ADR-0007](./0007-autorizacion-rbac-granular.md)
> (RBAC con permission cache) y es compatible con
> [ADR-0051](./0051-segmentacion-de-datos-por-sucursal.md): el rol sigue siendo
> único por usuario/empresa y el alcance de datos sigue en `UsuarioSucursal`.

## Contexto y problema

Hasta ahora los permisos de un usuario salían **solo de su rol** en la empresa
(`UsuarioEmpresaRol` → `RolPermiso` → `Permiso`, resuelto en
`PermissionLoader`). Dos usuarios con el mismo rol (por ejemplo
"Administrador") tenían exactamente los mismos permisos. Para que uno tuviera
distinto alcance había que crear un rol nuevo por persona, lo que no escala,
ensucia el catálogo de roles y dificulta la auditoría.

**Requisito:** dos usuarios con el mismo rol deben poder tener permisos
efectivos distintos sin crear un rol por usuario, y sin cambiar el
comportamiento de quien no tenga excepciones.

## Drivers de la decisión

- Compatibilidad total: sin excepciones, el resultado debe ser idéntico al actual.
- Un solo punto de resolución de permisos (el `PermissionLoader` lo consumen la
  autorización, el login y `CurrentUserPermissions`).
- Sin escalada de privilegios y con trazabilidad (auditoría, evento).
- Mínima superficie nueva: reutilizar `Permiso`, `IPermissionCache`,
  `IAuditable`, `IPerteneceAEmpresa` y los permisos canónicos.

## Opciones consideradas

1. **Excepciones por (usuario, empresa) sobre el rol** (Conceder/Denegar).
2. Un rol por usuario.
3. Varios roles por usuario.

## Decisión

Se agrega la entidad `UsuarioPermisoOverride` (tabla
`identidad.usuario_permiso_overrides`, índice único
`(usuario_id, empresa_id, permiso_id)`) con un `Efecto`:

```
efectivos = (permisos del rol  ∪  Conceder)  \  Denegar
```

- **Conceder** añade un permiso que el rol no trae; **Denegar** quita uno que sí
  trae. Deny gana si el mismo permiso apareciera en ambos (la API lo impide).
- La resolución vive **solo** en `PermissionLoader.LoadForUserInEmpresaAsync`.
  Si el usuario no está activo o no tiene un rol activo en la empresa, no hay
  permisos (ni siquiera los concedidos). Los service principals no cambian.
- Cada alta/baja/cambio invalida `IPermissionCache.InvalidateAsync(usuario, empresa)`,
  por lo que aplica de inmediato (sin esperar el TTL de 5 min).
- Permiso canónico nuevo `identidad.usuarios.gestionar-permisos`; la lectura de
  permisos efectivos usa `identidad.usuarios.leer`.
- API bajo `/api/v1/identidad/usuarios/{id}/empresas/{empresaId}`:
  `GET /permisos` (efectivos con origen `Rol`/`Concedido`/`Denegado`),
  `PUT /permisos-override` (reemplazo atómico) y `DELETE /permisos-override`
  (vuelve al rol). Mutaciones con `Idempotency-Key`; evento Outbox
  `identidad.usuario.permisos-override.actualizados.v1` solo con identificadores
  y conteos.
- **Reglas de seguridad**: (1) no se concede lo que el actor no posee en esa
  empresa (403 `PERMISOS_OVERRIDE_ESCALADA`); denegar sí se permite;
  (2) nadie edita sus propios overrides (403 `PERMISOS_OVERRIDE_AUTO_EDICION`);
  (3) **solo el rol `super-admin`** está protegido (422
  `SUPER_ADMIN_PERMISOS_PROTEGIDOS`; se identifica por el código del rol, la misma
  constante que usa el bootstrap). El resto de roles de sistema
  (`es_del_sistema = true`, p. ej. `admin-catalogos`, `auditor`) admite excepciones;
  el GET de efectivos expone `rolEsSuperAdmin`;
  (4) el actor debe tener `gestionar-permisos` en la empresa objetivo (403);
  (5) el usuario debe existir, estar activo y tener rol activo en la empresa
  (404/422); el permiso debe existir (404). Los overrides no manejan secretos.
- **Cambio de rol (decisión del owner):** cuando el rol del usuario en una
  empresa cambia —se asigna un rol nuevo (`AsignarRolAUsuarioCommand`) o se
  revoca uno (`RevocarRolDeUsuarioCommand`)— las excepciones de ese
  `(usuario, empresa)` **se borran en la misma transacción**, se invalida la
  caché y se publica el evento de overrides solo si realmente se borró algo.
  Reasignar el mismo rol no cambia nada (el comando ya responde 409). La UI avisa
  cuántos permisos personalizados se perderán antes de confirmar.
- **UX de interruptores:** cada permiso es un switch que muestra el estado
  efectivo; sobre el modelo guardado (una fila Conceder/Denegar por permiso)
  apagar un permiso del rol guarda Denegar, encender uno ajeno al rol guarda
  Conceder y volver al valor del rol elimina la excepción. Cada módulo tiene un
  switch de grupo con estado mixto (encender omite lo que el actor no posee y lo
  avisa) y "Restablecer grupo". Todo opera sobre un borrador que se guarda con el
  PUT en bloque; el backend no cambia.
- **UI**: panel independiente "Permisos personalizados" (props `usuarioId`,
  `empresaId`), montado en Administración → Empleados → detalle del empleado
  (pestaña "Roles y accesos"), para no depender de `/admin/usuarios`.

## Consecuencias

**Positivas**
- Permisos distintos para el mismo rol sin explosión de roles.
- Cambio localizado (loader + tabla nueva); sin migración de datos.
- Las excepciones no sobreviven a un cambio de rol: no quedan permisos
  "huérfanos" que sigan aplicando sobre una base distinta.

**Negativas**
- Una capa más al razonar los permisos efectivos (mitigado con el endpoint
  de efectivos con origen y el panel).
- Cambiar el rol obliga a recapturar las excepciones.
- El actor necesita ser miembro con `gestionar-permisos` de la empresa objetivo
  (también un super-admin en una empresa donde no tiene rol).
- Editar la matriz de un rol sigue sin invalidar la caché de sus usuarios (depende
  del TTL); fuera de alcance.
- El evento sigue sujeto al `PLATFORM-TODO(<AdminOutbox>)` de Identidad.

## Descartadas

- **Rol por usuario:** explosión de roles, difícil de auditar y de mantener.
- **Varios roles por usuario:** no personaliza a nivel permiso y rompe el
  supuesto de rol único documentado (ADR-0051).
- **Conservar las excepciones al cambiar de rol:** descartado por el owner; una
  excepción pensada para el rol anterior puede ser incorrecta o peligrosa sobre
  el nuevo.

## Notas de implementación

- Backend: `Identidad/Domain/UsuarioPermisoOverride.cs`, `EfectoPermiso.cs`,
  `Infrastructure/PermissionLoader.cs`,
  `Application/Usuarios/PermisosPersonalizadosUsuario.cs`, migración
  `AgregarPermisosOverridePorUsuario`, endpoints en `UsuariosEndpoints.cs`.
- Frontend: `modules/identidad/components/PermisosPersonalizadosPanel.tsx` y
  hooks en `modules/identidad/api/usuarios.ts`.
- Sin `ETag/If-Match`: el recurso se reemplaza por lote y no tiene versión de
  agregado; la concurrencia es "último PUT gana" con `Idempotency-Key`.
- Fuera de alcance: varios roles por usuario, permisos por sucursal, expiración de
  excepciones, aprobación de dos personas, invalidación de caché al editar la
  matriz de un rol.
