# Equivalencia de permisos: Portal de Requisiciones a roles del ERP

Fuente: Documentación funcional de los portales (Millet, 05-oct-2026), §1.1 (80 usuarios) y §1.5 (modelo de autorización, 16 permisos). Validación V51 (Fernando Alejos). Tarea ClickUp 86e3xd4uz.

En el portal la autorización la decide el permiso del usuario más una condición de estatus. En el ERP la decide el rol del usuario; el estatus lo valida el flujo de la requisición. Los roles se siembran al arrancar el backend (`BootstrapSuperAdminHostedService`, idempotente) y no requieren permisos ni migraciones nuevas.

Lo marcado «propuesta, confirma Fernando (V51)» no es regla de Millet: es lo que proponemos y está pendiente de su visto bueno.

## Roles del ERP

| Código del rol | Nombre | Permisos del ERP |
|---|---|---|
| `requisitante` | Requisitante | `compras.requisiciones.leer`, `.crear`, `.editar` |
| `requisitante-a-nombre-de-otros` | Requisitante a nombre de otros | los de Requisitante + `compras.requisiciones.seleccionar-requisitante` |
| `autorizador-requisiciones` | Autorizador de requisiciones | `compras.requisiciones.leer`, `.autorizar-nivel1`, `.autorizar-nivel2` |
| `almacen-salida-contra-requisicion` | Almacén · salida contra requisición | `compras.requisiciones.leer`, `almacen.salidas.leer-todas`, `.capturar`, `.registrar` |
| `almacen-vale-urgente` | Almacén · vale urgente | `almacen.salidas.leer-todas`, `almacen.salidas.por-vale` |
| `cancelacion-requisicion-autorizada` | Cancelación de requisición autorizada | `compras.requisiciones.leer`, `.cancelar` (el motivo es obligatorio) |

- El nivel de firma (1 o 2) que le toca a cada autorizador no sale del rol: sale de los aprobadores por departamento (`AprobadorDepartamento`, `RolAprobador`).

## Plantilla CSV de usuarios

`plantilla-usuarios-portal-requisiciones.csv`: columnas `usuario_portal`, `correo_microsoft`, `empleado`, `roles`, `sucursales`, `departamento`. La fila con `usuario_portal = prueba` es de ejemplo y se borra antes de cargar. `roles` y `sucursales` llevan varios valores separados por `|` (códigos de rol de la tabla de arriba; sucursal 101 = Conkal en el servidor de pruebas).

**Asignación requerida:** además de esta carga, Jorge debe asignar cada usuario en Entra (grupo de acceso al ERP). Sin eso el usuario no entra, aunque tenga rol.

## Equivalencia de los 16 permisos

| # | Permiso del portal (condición de estatus) | Destino en el ERP | Rol |
|---|---|---|---|
| 1 | Autorizar nivel 1 (creado, autorización parcial o surtido parcial) | `compras.requisiciones.autorizar-nivel1` | Autorizador de requisiciones |
| 2 | Autorizar nivel 2 (autorización parcial, completa o surtido parcial) | `compras.requisiciones.autorizar-nivel2` | Autorizador de requisiciones |
| 3 | Generar movimiento de inventario sin autorización (desde creado, salvo eliminado) | Desaparece como permiso general. Propuesta, confirma Fernando (V51): el vale urgente (`almacen.salidas.por-vale`), que se regulariza con requisición en 48 h hábiles | Almacén · vale urgente |
| 4 | Generar movimiento de inventario con autorización nivel 1 (solo autorización parcial) | `almacen.salidas.registrar`; el ERP valida el estatus de la requisición | Almacén · salida contra requisición |
| 5 | Generar movimiento de inventario con autorización nivel 2 (solo autorización completa) | `almacen.salidas.registrar`; el ERP valida el estatus de la requisición | Almacén · salida contra requisición |
| 6 | Transmitir a SAP (autorización completa o surtido parcial, con segunda firma) | Desaparece: el ERP reemplaza a SAP y no hay transmisión | Ninguno |
| 7 | Eliminar (iniciado o creado) | Propuesta, confirma Fernando (V51): `compras.requisiciones.cancelar` con motivo | Cancelación de requisición autorizada |
| 8 | Eliminar con autorización nivel 1 (solo autorización parcial) | Propuesta, confirma Fernando (V51): `compras.requisiciones.cancelar` con motivo | Cancelación de requisición autorizada |
| 9 | Eliminar con autorización nivel 2 (solo autorización completa) | Propuesta, confirma Fernando (V51): `compras.requisiciones.cancelar` con motivo | Cancelación de requisición autorizada |
| 10 | Eliminar con solicitud de compra generada (solo finalizado) | Propuesta, confirma Fernando (V51): `compras.requisiciones.cancelar` con motivo | Cancelación de requisición autorizada |
| 11 | Modificar (iniciado o creado) | `compras.requisiciones.editar` | Requisitante |
| 12 | Nuevo (según modo de la pantalla) | `compras.requisiciones.crear` | Requisitante |
| 13 | Seleccionar fecha (según modo de la pantalla) | Desaparece. Propuesta, confirma Fernando (V51): vale la fecha de captura | Ninguno |
| 14 | Seleccionar requisitante (según modo de la pantalla) | `compras.requisiciones.seleccionar-requisitante` | Requisitante a nombre de otros |
| 15 | Por confirmar con la exportación de usuarios | Por confirmar con la exportación de usuarios (`DERECHOS_USUARIOS`) | Por confirmar |
| 16 | Por confirmar con la exportación de usuarios | Por confirmar con la exportación de usuarios (`DERECHOS_USUARIOS`) | Por confirmar |

Notas:

- §1.5 nombra 14 de los 16 permisos; los otros 2 salen de la exportación `DERECHOS_USUARIOS` que no tenemos. No se inventan.
- §1.5 lista **cuatro** permisos de eliminar (filas 7 a 10), no tres. Todos quedan en uno solo: cancelar con motivo (propuesta).
- Cancelación de una RQ de materia prima ya enviada al ERP desde la app de Almacenes (Jorge, 30-oct): no se cancela allí. Compras cancela la compra y la RQ se cancela sola (`COMPRA_CANCELADA`). Aplica a las RQ de materia prima de Almacenes, no a las del portal.
- Cancelar con motivo hoy aplica a requisiciones autorizadas o en surtido (endpoint `POST /api/v1/compras/requisiciones/{id}/cancelar`). Descartar un borrador propio (fila 7) usa `compras.requisiciones.eliminar`, que ningún rol de esta tabla incluye todavía: pendiente de V51.

## Vale urgente y las 48 horas

El vale urgente ya existe (`RegistrarSalidaPorValeCommand`, `RegularizarSalidaPorValeCommand`, `RegularizacionValeSlaWorker`). El plazo se calcula con `CalendarioHabil.SumarHoras`: salta sábados, domingos y festivos, pero cuenta **24 horas por día hábil**, desde la medianoche local de la fecha del vale. No hay jornada laboral. En la práctica son **2 días hábiles**, no 48 horas de oficina. Si Millet espera otra cosa, es un cambio aparte.

## Un usuario, varios roles: decisión abierta (Eliam)

El panel de Empleados (`EmpleadoAccesoPanel.tsx`) y los ADR-0051 y 0053 eligen **un** rol por usuario; el backend acepta varios. Quien requisita **y** autoriza o surte necesita 2 roles o un rol compuesto. Se deja así hasta que Eliam decida; esta tarea no cambia la interfaz.

## Fuera de alcance

Traslado entre almacenes (V57), cheques (V58), autorización de pagos de importación (I13.3) y la carga real de los 80 usuarios (TR5-USR).
