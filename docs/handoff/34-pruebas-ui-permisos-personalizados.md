# Permisos personalizados por usuario — guía de pruebas manuales en la interfaz

Fecha: 1-oct-2026. Rama `fix/permisos-personalizados-por-usuario`. Referencias:
[ADR-0053](../decisiones/0053-permisos-personalizados-por-usuario.md) y
[plan](../modulos/identidad/01-plan-permisos-personalizados-por-usuario.md).

No escribir en esta guía, en capturas ni en el repositorio: contraseñas, tokens ni datos personales reales.

## Qué se prueba

Dos usuarios con el **mismo rol** pueden tener permisos efectivos distintos mediante excepciones por
(usuario, empresa): `efectivos = (rol + concedidos) − denegados`. Sin excepciones, el usuario tiene exactamente
los permisos de su rol.

**Punto de entrada:** Administración → Empleados → detalle del empleado → pestaña **Roles y accesos** →
panel **Permisos personalizados** (debajo de "Roles por empresa"). No se usa `/admin/usuarios`.

## Prerrequisitos

- Backend y frontend en local con las migraciones de `tools/migration-contexts.txt` aplicadas (incluye
  `AgregarPermisosOverridePorUsuario`, que crea la tabla y el permiso `identidad.usuarios.gestionar-permisos`).
  Reiniciar el backend para que el bootstrap otorgue el permiso nuevo al super-admin.
- Un **administrador** con `identidad.usuarios.gestionar-permisos`, `identidad.usuarios.leer`,
  `identidad.permisos.leer`, `identidad.roles.leer` e `identidad.asignaciones.administrar`.
- Dos empleados con cuenta de acceso (A y B) con el **mismo rol** no super-admin en la empresa activa, y un
  tercer empleado sin cuenta de acceso.
- Un usuario que no tenga `gestionar-permisos` (para el caso C1).

## A. Panel, interruptores y grupos

Cada permiso es un **interruptor** que muestra el estado efectivo (encendido = la persona lo tiene). Guardar
sigue enviando solo las excepciones respecto al rol.

| # | Acción | Resultado esperado |
|---|---|---|
| A1 | Entrar como administrador, abrir el empleado A → **Roles y accesos**. | Se ve el panel "Permisos personalizados" con el código del rol. Módulos colapsados, cada uno con su interruptor de grupo, su botón ↺ y su conteo. Resumen "0 concedido(s) / 0 denegado(s) respecto al rol". |
| A2 | Abrir el empleado sin cuenta de acceso. | No hay panel de permisos personalizados (solo el aviso de que aún no tiene cuenta). |
| A3 | Entrar con un usuario sin `gestionar-permisos` y abrir el empleado A. | El panel no aparece. |
| A4 | Expandir un módulo. | Cada permiso muestra código, descripción, "El rol lo incluye / no lo incluye" y un interruptor. Encendido en los permisos que el rol incluye. |
| A5 | Apagar un permiso que el rol incluye. | La fila se marca en rojo ("Personalizado: quitado"), aparece el botón ↺ de la fila, el resumen pasa a "0 concedido(s) / 1 denegado(s)" y aparece "Hay cambios sin guardar". |
| A6 | Encender un permiso que el rol **no** incluye y que tú sí posees. | La fila se marca en verde ("Personalizado: añadido") y el resumen se actualiza. |
| A7 | Volver a mover el interruptor al valor del rol (o pulsar ↺ de la fila). | La marca desaparece y el permiso queda heredado; el resumen baja. |
| A8 | Buscar un permiso que tú **no** posees y que el rol no incluye. | Su interruptor está deshabilitado con el texto/tooltip "solo puedes conceder permisos que tú posees" (sin escalada de privilegios). |
| A9 | Pulsar **Descartar cambios**. | Vuelven los estados guardados y desaparece el aviso de cambios. |
| A10 | **Rol de sistema que no es super-admin:** abrir un empleado cuyo rol sea `admin-catalogos` (o `auditor`, `admin-compras`...). | El panel **es editable** (sin aviso de bloqueo); se pueden guardar excepciones. |
| A11 | **Interruptor de grupo con estado mixto:** en un módulo donde el rol incluye solo parte de los permisos. | El interruptor de grupo aparece en estado mixto (intermedio; `aria-checked="mixed"`). |
| A12 | Pulsar el interruptor de grupo en estado mixto/apagado (**encender grupo**). | Se quitan las denegaciones y se conceden los permisos que el rol no incluye **solo si tú los posees**. Si alguno no se puede conceder, aparece "Se omitieron N permiso(s) que no puedes conceder porque tú no los posees" y el grupo queda mixto. |
| A13 | Con el grupo todo encendido, pulsarlo (**apagar grupo**). | Se deniegan los permisos que el rol incluye y se descartan los concedidos; todo el grupo queda apagado. El resumen refleja los denegados. |
| A14 | Pulsar el ↺ del encabezado del grupo (**Restablecer grupo**). | Se eliminan del borrador todas las excepciones de ese grupo (queda heredado); no toca otros módulos. Con un módulo grande (decenas de permisos) la respuesta es inmediata. |

## B. Guardar y efecto inmediato

| # | Acción | Resultado esperado |
|---|---|---|
| B1 | En A: apagar un permiso que el rol incluye y encender uno que no; **Guardar cambios**. | Toast "Permisos personalizados actualizados". Al recargar, los estados persisten y el resumen es "1 concedido(s) / 1 denegado(s)". |
| B2 | Abrir el empleado B (mismo rol). | B no tiene excepciones: exactamente los permisos del rol. |
| B3 | Entrar como A (otra sesión) y probar la pantalla ligada al permiso denegado y la del permiso concedido. | La denegada responde 403 / no aparece; la concedida funciona. Aplica **sin esperar 5 minutos** (la caché se invalida al guardar; si A ya tenía sesión, basta recargar la página para refrescar el menú). |
| B4 | Como B, probar las mismas pantallas. | B conserva el comportamiento del rol. |
| B5 | Pulsar **Guardar** con la API caída o devolviendo error. | Toast con el título del problema; el borrador se conserva y se puede reintentar. |

## C. Reglas de seguridad

| # | Acción | Resultado esperado |
|---|---|---|
| C1 | Con un usuario con `leer` pero sin `gestionar-permisos`, llamar `PUT .../permisos-override` (por ejemplo desde la consola/REST client). | 403. |
| C2 | Como administrador, abrir **tu propio** empleado → Roles y accesos. | El panel muestra el aviso "No puedes editar tus propios permisos personalizados" y los interruptores están deshabilitados. Por API: 403 `PERMISOS_OVERRIDE_AUTO_EDICION`. |
| C3 | Abrir el empleado que sea super-admin. | Aviso "Los permisos del rol super-admin se gestionan por bootstrap; no admiten excepciones."; interruptores (también el de grupo) deshabilitados. Por API: 422 `SUPER_ADMIN_PERMISOS_PROTEGIDOS`. Es el **único** rol protegido (ver A10). |
| C4 | Intentar conceder por API un permiso que el administrador no posee. | 403 `PERMISOS_OVERRIDE_ESCALADA`. Denegar cualquier permiso sí se permite. |
| C5 | Desactivar al usuario o quitarle el rol y reintentar guardar. | 422 (`USUARIO_INACTIVO` / `USUARIO_SIN_ROL_EN_EMPRESA`), mostrado como toast. |

## D. Restablecer

| # | Acción | Resultado esperado |
|---|---|---|
| D1 | En A (con excepciones) pulsar **Restablecer al rol**. | Diálogo de confirmación con el número de excepciones. |
| D2 | Confirmar. | Toast "Permisos restablecidos al rol"; todos los interruptores vuelven al valor del rol; A recupera los permisos del rol de inmediato. |
| D3 | Sin excepciones guardadas. | El botón **Restablecer al rol** está deshabilitado. |

## E. Cambio de rol borra las excepciones

Decisión del owner: al cambiar el rol del usuario en la empresa, sus excepciones se **borran**.

| # | Acción | Resultado esperado |
|---|---|---|
| E1 | A con 2 excepciones. En **Roles por empresa** pulsar **Asignar rol**, elegir la misma empresa y un rol distinto. | Antes de guardar aparece el aviso "Al asignar un rol distinto en esta empresa se perderán 2 permiso(s) personalizado(s)". |
| E2 | Guardar la asignación. | El panel "Permisos personalizados" queda sin excepciones (0 / 0). |
| E3 | Reintentar asignar el **mismo** rol que ya tiene. | Error "El usuario ya tiene ese rol en esa empresa"; las excepciones no se tocan. |
| E4 | Volver a crear excepciones en A y pulsar **Revocar** en su rol. | El diálogo avisa "Se perderán N permiso(s) personalizado(s) del usuario en esta empresa". Al confirmar, el rol se revoca y las excepciones desaparecen. |
| E5 | Asignar de nuevo un rol al usuario. | Empieza sin excepciones. |

## F. Revisiones transversales

- Pestaña Network: las respuestas del panel (`/permisos`, `/permisos-override`) solo llevan códigos de permiso,
  efecto y motivo; ningún secreto. Las mutaciones envían `Idempotency-Key`.
- Auditoría (`Administración → Auditoría`): altas/bajas de excepciones quedan registradas por el interceptor de
  auditoría (`UsuarioPermisoOverride`).
- Registrar para cada caso: fecha/hora, usuario, empresa, resultado y evidencia anonimizada.

## G. Fuera de esta guía

- Editar la matriz de un rol sigue sin invalidar la caché de sus usuarios (depende del TTL de 5 min).
- Varios roles por usuario, permisos por sucursal, expiración y aprobación de dos personas.
