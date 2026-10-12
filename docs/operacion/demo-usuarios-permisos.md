# Usuarios, roles y permisos de la demo del 12-oct

> Generado desde la base `millet_demo` con la que se ensayó la demo (12-oct-2026). Es la **fuente de verdad** de quién puede qué. El archivo máquina es [`demo-usuarios-permisos.csv`](./demo-usuarios-permisos.csv) (`correo|tipo|valor`, tipo = `rol`, `sucursal` o `permiso`) y se verifica **directo** en la laptop con:

```bash
tools/demo/verificar-usuarios-demo.sh            # compara tu base contra el CSV (sale 1 si falta algo)
tools/demo/verificar-usuarios-demo.sh --exportar # regenera el CSV desde tu base
```

Falta alguna línea del CSV → **ERROR** (falta un usuario, un rol, una sucursal o un permiso). Sobra alguna (por ejemplo tu propia cuenta de administrador o permisos nuevos de migraciones) → solo **AVISO**.

## 1. Usuarios

| Correo | Nombre | Tipo | Rol | Sucursales | Permisos |
|---|---|---|---|---|---|
| `capturista-compras@example.invalid` | DEMO Capturista Compras | técnica (Activo) | `DEMO-ROL-Capturista Compras` | MID | 64 |
| `direccion@example.invalid` | DEMO Dirección | técnica (Activo) | `DEMO-ROL-Dirección` | MID, MTY, QRO | 66 |
| `fio@uzieltzaboutlook.onmicrosoft.com` | fio | real (Activo) | `DEMO-ROL-Jefe Compras` | MID, MTY, QRO | 66 |
| `geovanny@uzieltzaboutlook.onmicrosoft.com` | Geovany | técnica (Activo) | `super-admin` | — (ve todo por rol) | 273 |
| `jefe-compras@example.invalid` | DEMO Jefe de Compras | técnica (Activo) | `DEMO-ROL-Jefe Compras` | MID, MTY, QRO | 66 |
| `mariana.ortega@uzieltzaboutlook.onmicrosoft.com` | Mariana Ortega Valdés | real (Activo) | `DEMO-ROL-Dirección` | MID, MTY, QRO | 66 |
| `qa.auditor@uzieltzaboutlook.onmicrosoft.com` | QA Auditor | real (Activo) | `DEMO-ROL-DAF` | MID, MTY, QRO | 4 |
| `qa.catalogos@uzieltzaboutlook.onmicrosoft.com` | QA Catalogos | real (Activo) | `DEMO-ROL-Facturación` | MID | 32 |

- Las cuentas `@example.invalid` las crea el sembrador de la demo y **no pueden iniciar sesión**; sirven de
  referencia de cómo queda cada perfil. Las cuentas con correo de Microsoft son las que entran de verdad.
  La columna "Tipo" refleja la bandera de la base: Geovany sale "técnica" porque así la marca el arranque del
  super administrador, pero inicia sesión con normalidad.
- Un usuario tiene **un solo rol por empresa**. Quien opera una sucursal necesita estar **asociado** a ella (columna Sucursales) o ser corporativo (casilla "Usuario corporativo de Millet" en el detalle del empleado, permiso `admin.empleados.gestionar-todas-sucursales`).
- Los permisos de una sesión abierta tardan hasta 5 minutos en refrescarse.

## 2. Roles

| Rol | Permisos | Usuarios |
|---|---|---|
| `DEMO-ROL-Capturista Compras` | 64 | `capturista-compras` |
| `DEMO-ROL-DAF` | 4 | `qa.auditor` |
| `DEMO-ROL-Dirección` | 66 | `direccion`, `mariana.ortega` |
| `DEMO-ROL-Facturación` | 32 | `qa.catalogos` |
| `DEMO-ROL-Jefe Compras` | 66 | `fio`, `jefe-compras` |
| `super-admin` | 273 | `geovanny` |

## 3. Permisos clave de la demo por usuario

✓ = lo tiene. Una celda vacía en una pantalla de la demo significa que esa persona **no** debe usarla (o verá el aviso rojo de permiso).

| Permiso | Para qué | `capturista-compras` | `direccion` | `fio` | `geovanny` | `jefe-compras` | `mariana.ortega` | `qa.auditor` | `qa.catalogos` |
|---|---|---|---|---|---|---|---|---|---|
| `compartido.catalogos.leer` | Catálogos (casi toda pantalla) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `identidad.usuarios.leer` | Lista de usuarios (pantallas de OC) |  | ✓ | ✓ | ✓ | ✓ | ✓ |  |  |
| `compras.requisiciones.crear` | Crear requisición | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |  |  |
| `compras.requisiciones.autorizar-nivel1` | Firma N1 requisición | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |  |  |
| `compras.requisiciones.autorizar-nivel2` | Firma N2 requisición | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |  |  |
| `compras.ordenes.crear` | Crear OC | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |  |  |
| `compras.ordenes.adjuntar` | Adjuntar cotización | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |  |  |
| `compras.ordenes.autorizar-nivel1` | Firma N1 OC |  |  | ✓ | ✓ | ✓ |  |  |  |
| `compras.ordenes.autorizar-nivel2` | Firma N2 OC |  | ✓ |  | ✓ |  | ✓ |  |  |
| `compras.aprobadores.administrar` | Aprobadores | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |  |  |
| `almacen.entradas.registrar` | Registrar recepción | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |  |  |
| `almacen.almacenes.leer` | Almacenes (filtros de Almacén) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |  |  |
| `cuentas_por_pagar.facturas.capturar` | Capturar factura |  |  |  | ✓ |  |  |  |  |
| `cuentas_por_pagar.facturas.autorizar` | Autorizar factura |  |  |  | ✓ |  |  |  |  |
| `tesoreria.pagos.aplicar` | Registrar pago |  |  |  | ✓ |  |  |  |  |
| `tesoreria.cuentas.ver` | Cuentas de Tesorería |  |  |  | ✓ |  |  |  |  |
| `datos_maestros.proveedores.gestionar` | Proveedores (ver) |  |  |  | ✓ |  |  |  |  |
| `compartido.catalogos.administrar` | Alta de proveedor/artículo |  |  |  | ✓ |  |  |  |  |
| `datos_maestros.proveedores.validar` | Validar/rechazar proveedor |  |  |  | ✓ |  |  |  |  |
| `datos_maestros.proveedores.bancarios-editar` | Editar datos bancarios |  |  |  | ✓ |  |  |  |  |
| `datos_maestros.articulos.gestionar` | Artículos |  |  |  | ✓ |  |  |  |  |
| `datos_maestros.clientes.sincronizar` | Sincronizar clientes A+W |  |  |  | ✓ |  |  |  | ✓ |
| `datos_maestros.productos-aw.gestionar` | Productos A+W |  |  |  | ✓ |  |  |  |  |
| `admin.empleados.gestionar-todas-sucursales` | Alcance corporativo (todas las sucursales) |  |  |  | ✓ |  |  |  |  |
| `admin.auditoria.leer` | Auditoría |  |  |  | ✓ |  |  |  |  |

## 4. Permisos completos por rol

Agrupados por módulo (el prefijo antes del primer punto).

### `DEMO-ROL-Capturista Compras` — 64 permisos

Usuarios: `capturista-compras@example.invalid`

- **admin** (1): `departamentos.leer`
- **almacen** (35): `ajustes.manual`, `almacenes.administrar`, `almacenes.leer`, `asignaciones.administrar`, `asignaciones.leer`, `cierre-mes.ejecutar`, `devoluciones-internas.capturar`, `devoluciones-internas.leer`, `devoluciones-proveedor.autorizar`, `devoluciones-proveedor.iniciar`, `devoluciones-proveedor.registrar`, `entradas.cancelar-borrador`, `entradas.capturar`, `entradas.leer`, `entradas.registrar`, `inventarios.aprobar-nivel1`, `inventarios.aprobar-nivel2`, `inventarios.aprobar-nivel3`, `inventarios.capturar`, `inventarios.crear`, `inventarios.leer`, `lectura.total`, `reorden.administrar`, `reorden.leer`, `reportes.alfak`, `reportes.movimientos`, `reportes.mp-cnk`, `reservas.administrar`, `salidas.capturar`, `salidas.leer-propias`, `salidas.leer-todas`, `salidas.por-vale`, `salidas.registrar`, `ubicaciones.administrar`, `ubicaciones.leer`
- **compartido** (1): `catalogos.leer`
- **compras** (27): `aprobadores.administrar`, `configuracion.editar`, `configuracion.leer`, `ordenes.adjuntar`, `ordenes.cancelar`, `ordenes.cancelar-doble`, `ordenes.cerrar-manual`, `ordenes.crear`, `ordenes.crear-sin-rq`, `ordenes.leer`, `ordenes.logistica`, `ordenes.reportes-partidas-abiertas`, `requisiciones.adjuntos-baja`, `requisiciones.adjuntos-subir`, `requisiciones.adjuntos-ver`, `requisiciones.autorizar-nivel1`, `requisiciones.autorizar-nivel2`, `requisiciones.cancelar`, `requisiciones.cerrar-manual`, `requisiciones.crear`, `requisiciones.editar`, `requisiciones.editar-de-otros-usuarios`, `requisiciones.eliminar`, `requisiciones.leer`, `requisiciones.rechazar`, `requisiciones.seleccionar-requisitante`, `requisiciones.ver-todos-departamentos`

### `DEMO-ROL-DAF` — 4 permisos

Usuarios: `qa.auditor@uzieltzaboutlook.onmicrosoft.com`

- **admin** (1): `departamentos.leer`
- **compartido** (1): `catalogos.leer`
- **contabilidad** (2): `catalogo.autorizar`, `catalogo.leer`

### `DEMO-ROL-Dirección` — 66 permisos

Usuarios: `direccion@example.invalid`, `mariana.ortega@uzieltzaboutlook.onmicrosoft.com`

- **admin** (1): `departamentos.leer`
- **almacen** (35): `ajustes.manual`, `almacenes.administrar`, `almacenes.leer`, `asignaciones.administrar`, `asignaciones.leer`, `cierre-mes.ejecutar`, `devoluciones-internas.capturar`, `devoluciones-internas.leer`, `devoluciones-proveedor.autorizar`, `devoluciones-proveedor.iniciar`, `devoluciones-proveedor.registrar`, `entradas.cancelar-borrador`, `entradas.capturar`, `entradas.leer`, `entradas.registrar`, `inventarios.aprobar-nivel1`, `inventarios.aprobar-nivel2`, `inventarios.aprobar-nivel3`, `inventarios.capturar`, `inventarios.crear`, `inventarios.leer`, `lectura.total`, `reorden.administrar`, `reorden.leer`, `reportes.alfak`, `reportes.movimientos`, `reportes.mp-cnk`, `reservas.administrar`, `salidas.capturar`, `salidas.leer-propias`, `salidas.leer-todas`, `salidas.por-vale`, `salidas.registrar`, `ubicaciones.administrar`, `ubicaciones.leer`
- **compartido** (1): `catalogos.leer`
- **compras** (28): `aprobadores.administrar`, `configuracion.editar`, `configuracion.leer`, `ordenes.adjuntar`, `ordenes.autorizar-nivel2`, `ordenes.cancelar`, `ordenes.cancelar-doble`, `ordenes.cerrar-manual`, `ordenes.crear`, `ordenes.crear-sin-rq`, `ordenes.leer`, `ordenes.logistica`, `ordenes.reportes-partidas-abiertas`, `requisiciones.adjuntos-baja`, `requisiciones.adjuntos-subir`, `requisiciones.adjuntos-ver`, `requisiciones.autorizar-nivel1`, `requisiciones.autorizar-nivel2`, `requisiciones.cancelar`, `requisiciones.cerrar-manual`, `requisiciones.crear`, `requisiciones.editar`, `requisiciones.editar-de-otros-usuarios`, `requisiciones.eliminar`, `requisiciones.leer`, `requisiciones.rechazar`, `requisiciones.seleccionar-requisitante`, `requisiciones.ver-todos-departamentos`
- **identidad** (1): `usuarios.leer`

### `DEMO-ROL-Facturación` — 32 permisos

Usuarios: `qa.catalogos@uzieltzaboutlook.onmicrosoft.com`

- **admin** (1): `departamentos.leer`
- **compartido** (1): `catalogos.leer`
- **datos_maestros** (4): `clientes.fiscal-editar`, `clientes.gestionar`, `clientes.origen-ver`, `clientes.sincronizar`
- **facturacion** (26): `activos.autorizar`, `anticipos.emitir`, `anticipos.leer`, `anticipos.vincular`, `caja.administrar`, `caja.leer-todas`, `caja.liquidar`, `caja.operar`, `caja.supervisar`, `cancelaciones.consultar`, `cancelaciones.solicitar`, `carta-porte.emitir`, `carta-porte.leer`, `comprobantes.descartar`, `comprobantes.reintentar-timbrado`, `facturas.editar-articulo`, `facturas.editar-receptor`, `facturas.emitir`, `facturas.leer`, `notas-credito.bonificacion-emitir`, `notas-credito.leer`, `pedidos.capturar`, `pedidos.excepciones-resolver`, `pedidos.importar`, `reportes.leer`, `repp.emitir`

### `DEMO-ROL-Jefe Compras` — 66 permisos

Usuarios: `fio@uzieltzaboutlook.onmicrosoft.com`, `jefe-compras@example.invalid`

- **admin** (1): `departamentos.leer`
- **almacen** (35): `ajustes.manual`, `almacenes.administrar`, `almacenes.leer`, `asignaciones.administrar`, `asignaciones.leer`, `cierre-mes.ejecutar`, `devoluciones-internas.capturar`, `devoluciones-internas.leer`, `devoluciones-proveedor.autorizar`, `devoluciones-proveedor.iniciar`, `devoluciones-proveedor.registrar`, `entradas.cancelar-borrador`, `entradas.capturar`, `entradas.leer`, `entradas.registrar`, `inventarios.aprobar-nivel1`, `inventarios.aprobar-nivel2`, `inventarios.aprobar-nivel3`, `inventarios.capturar`, `inventarios.crear`, `inventarios.leer`, `lectura.total`, `reorden.administrar`, `reorden.leer`, `reportes.alfak`, `reportes.movimientos`, `reportes.mp-cnk`, `reservas.administrar`, `salidas.capturar`, `salidas.leer-propias`, `salidas.leer-todas`, `salidas.por-vale`, `salidas.registrar`, `ubicaciones.administrar`, `ubicaciones.leer`
- **compartido** (1): `catalogos.leer`
- **compras** (28): `aprobadores.administrar`, `configuracion.editar`, `configuracion.leer`, `ordenes.adjuntar`, `ordenes.autorizar-nivel1`, `ordenes.cancelar`, `ordenes.cancelar-doble`, `ordenes.cerrar-manual`, `ordenes.crear`, `ordenes.crear-sin-rq`, `ordenes.leer`, `ordenes.logistica`, `ordenes.reportes-partidas-abiertas`, `requisiciones.adjuntos-baja`, `requisiciones.adjuntos-subir`, `requisiciones.adjuntos-ver`, `requisiciones.autorizar-nivel1`, `requisiciones.autorizar-nivel2`, `requisiciones.cancelar`, `requisiciones.cerrar-manual`, `requisiciones.crear`, `requisiciones.editar`, `requisiciones.editar-de-otros-usuarios`, `requisiciones.eliminar`, `requisiciones.leer`, `requisiciones.rechazar`, `requisiciones.seleccionar-requisitante`, `requisiciones.ver-todos-departamentos`
- **identidad** (1): `usuarios.leer`

### `super-admin` — 273 permisos

Usuarios: `geovanny@uzieltzaboutlook.onmicrosoft.com`

- **admin** (19): `auditoria.leer`, `departamentos.gestionar`, `departamentos.leer`, `empleados.gestionar`, `empleados.gestionar-todas-sucursales`, `empleados.leer-todas-sucursales`, `empresas.crear`, `empresas.desactivar`, `empresas.editar`, `empresas.leer`, `empresas.sucursales-gestionar`, `parametros.editar`, `parametros.leer`, `puestos.gestionar`, `puestos.leer`, `series.gestionar`, `sucursales.departamentos-gestionar`, `sucursales.puestos-gestionar`, `sucursales.usuarios-gestionar`
- **almacen** (41): `ajustes.manual`, `almacenes.administrar`, `almacenes.leer`, `asignaciones.administrar`, `asignaciones.leer`, `cierre-mes.ejecutar`, `devoluciones-internas.capturar`, `devoluciones-internas.leer`, `devoluciones-proveedor.autorizar`, `devoluciones-proveedor.iniciar`, `devoluciones-proveedor.registrar`, `entradas.cancelar-borrador`, `entradas.capturar`, `entradas.gestionar-todas-sucursales`, `entradas.leer`, `entradas.leer-todas-sucursales`, `entradas.registrar`, `inventarios.aprobar-nivel1`, `inventarios.aprobar-nivel2`, `inventarios.aprobar-nivel3`, `inventarios.capturar`, `inventarios.crear`, `inventarios.leer`, `lectura.total`, `reorden.administrar`, `reorden.gestionar-todas-sucursales`, `reorden.leer`, `reorden.leer-todas-sucursales`, `reportes.alfak`, `reportes.movimientos`, `reportes.mp-cnk`, `reservas.administrar`, `salidas.capturar`, `salidas.gestionar-todas-sucursales`, `salidas.leer-propias`, `salidas.leer-todas`, `salidas.leer-todas-sucursales`, `salidas.por-vale`, `salidas.registrar`, `ubicaciones.administrar`, `ubicaciones.leer`
- **catalogos** (7): `condiciones-pago.gestionar`, `formas-pago.gestionar`, `incoterms.gestionar`, `monedas.gestionar`, `tipos-cambio.gestionar`, `transportistas.gestionar`, `unidades-medida.gestionar`
- **centros_costo** (4): `asignaciones.administrar`, `catalogo.administrar`, `catalogo.leer`, `dim3.leer-todos`
- **compartido** (2): `catalogos.administrar`, `catalogos.leer`
- **compras** (33): `aprobadores.administrar`, `configuracion.editar`, `configuracion.leer`, `ordenes.adjuntar`, `ordenes.autorizar-nivel1`, `ordenes.autorizar-nivel2`, `ordenes.cancelar`, `ordenes.cancelar-doble`, `ordenes.cerrar-manual`, `ordenes.crear`, `ordenes.crear-sin-rq`, `ordenes.gestionar-todas-sucursales`, `ordenes.leer`, `ordenes.leer-todas-sucursales`, `ordenes.logistica`, `ordenes.reportes-partidas-abiertas`, `requisiciones.adjuntos-baja`, `requisiciones.adjuntos-subir`, `requisiciones.adjuntos-ver`, `requisiciones.autorizar-nivel1`, `requisiciones.autorizar-nivel2`, `requisiciones.cancelar`, `requisiciones.cerrar-manual`, `requisiciones.crear`, `requisiciones.editar`, `requisiciones.editar-de-otros-usuarios`, `requisiciones.eliminar`, `requisiciones.gestionar-todas-sucursales`, `requisiciones.leer`, `requisiciones.leer-todas-sucursales`, `requisiciones.rechazar`, `requisiciones.seleccionar-requisitante`, `requisiciones.ver-todos-departamentos`
- **contabilidad** (12): `catalogo.administrar`, `catalogo.autorizar`, `catalogo.importar`, `catalogo.leer`, `dimensiones.administrar`, `dimensiones.leer`, `movimientos.gestionar-todas-sucursales`, `movimientos.validar`, `periodo.administrar`, `periodo.cerrar`, `periodo.leer`, `periodo.reabrir`
- **cuentas_por_cobrar** (10): `aplicacion-pago.confirmar`, `aplicacion-pago.proponer`, `cartera.gestionar-todas-sucursales`, `cartera.leer`, `cartera.leer-todas-sucursales`, `cobranza.registrar`, `liberacion.decidir`, `liberacion.override`, `lineas-credito.gestionar`, `lineas-credito.leer`
- **cuentas_por_pagar** (55): `anticipos.capturar`, `anticipos.leer`, `catalogos.aprobadores.administrar`, `catalogos.politicas-viaticos.administrar`, `cfdis.cargar-manual`, `cfdis.descartar`, `cfdis.leer`, `comprobaciones.aprobar-nivel1`, `comprobaciones.aprobar-nivel2`, `comprobaciones.capturar`, `comprobaciones.leer`, `documentos.gestionar-todas-sucursales`, `documentos.leer-todas-sucursales`, `facturas.adjuntos-baja`, `facturas.adjuntos-subir`, `facturas.adjuntos-ver`, `facturas.autorizar`, `facturas.cancelar`, `facturas.capturar`, `facturas.editar`, `facturas.enviar-revision`, `facturas.gestionar-todas-sucursales`, `facturas.leer`, `facturas.leer-todas-sucursales`, `facturas.liberar-revision`, `notas-cargo.aplicar`, `notas-cargo.autorizar`, `notas-cargo.crear`, `notas-cargo.leer`, `notas-credito.capturar`, `notas-credito.leer`, `proveedores.ajustar-tolerancia`, `proveedores.liberar-revision`, `proveedores.poner-revision`, `reportes.antiguedad`, `reportes.cartera`, `reportes.diot`, `reportes.leer-todas-sucursales`, `reportes.tc`, `reposiciones.administrar`, `reposiciones.leer`, `retenciones.administrar`, `retenciones.leer`, `tc.administrar`, `tc.cerrar-estado-cuenta`, `tc.disputar`, `tc.leer`, `tc.registrar-movimiento`, `viaticos.autorizar-df`, `viaticos.autorizar-jefe`, `viaticos.capturar-comprobacion`, `viaticos.leer`, `viaticos.liberar`, `viaticos.marcar-pagado`, `viaticos.solicitar`
- **datos_maestros** (14): `articulos.gestionar`, `clientes.fiscal-editar`, `clientes.gestionar`, `clientes.origen-ver`, `clientes.sincronizar`, `productos-aw.gestionar`, `proveedores.adjuntos-baja`, `proveedores.adjuntos-subir`, `proveedores.adjuntos-ver`, `proveedores.bancarios-editar`, `proveedores.bancarios-ver`, `proveedores.gestionar`, `proveedores.tolerancia-editar`, `proveedores.validar`
- **facturacion** (26): `activos.autorizar`, `anticipos.emitir`, `anticipos.leer`, `anticipos.vincular`, `caja.administrar`, `caja.leer-todas`, `caja.liquidar`, `caja.operar`, `caja.supervisar`, `cancelaciones.consultar`, `cancelaciones.solicitar`, `carta-porte.emitir`, `carta-porte.leer`, `comprobantes.descartar`, `comprobantes.reintentar-timbrado`, `facturas.editar-articulo`, `facturas.editar-receptor`, `facturas.emitir`, `facturas.leer`, `notas-credito.bonificacion-emitir`, `notas-credito.leer`, `pedidos.capturar`, `pedidos.excepciones-resolver`, `pedidos.importar`, `reportes.leer`, `repp.emitir`
- **identidad** (15): `asignaciones.administrar`, `asignaciones.leer`, `permisos.leer`, `roles.administrar`, `roles.asignar-permisos`, `roles.crear`, `roles.editar`, `roles.eliminar`, `roles.grupos-entra-id-gestionar`, `roles.leer`, `usuarios.crear`, `usuarios.desactivar`, `usuarios.editar`, `usuarios.gestionar-permisos`, `usuarios.leer`
- **infra** (2): `audit_log.leer`, `health.leer`
- **integraciones** (11): `aw.administracion.configuracion`, `aw.administracion.servicios`, `aw.articulos.consultar`, `aw.clientes.consultar`, `aw.cotizaciones.consultar`, `aw.cotizaciones.crear`, `aw.cotizaciones.reintentar`, `aw.inventario.consultar`, `aw.pedidos.consultar`, `fiscal.administrar`, `fiscal.leer`
- **tesoreria** (22): `conciliacion.cerrar`, `conciliacion.operar`, `corridas.autorizar`, `corridas.crear`, `corridas.ejecutar`, `cuentas.administrar`, `cuentas.ver`, `depositos.confirmar`, `depositos.rechazar`, `documentos.gestionar-todas-sucursales`, `documentos.leer-todas-sucursales`, `movimientos.registrar`, `movimientos.ver`, `movimientos.ver-cuenta-completa`, `pagos-cuenta.ligar`, `pagos-cuenta.registrar`, `pagos.aplicar`, `pagos.revertir`, `pasivos.solicitar-cancelacion`, `pasivos.ver`, `reportes.ver`, `repp.registrar`

## 5. Cómo se usa para verificar en la laptop

1. Restaura el respaldo de la base (ver `demo-12oct-replicar-en-laptop.md`, sección 5) y corre las migraciones.
2. Ejecuta `tools/demo/verificar-usuarios-demo.sh`. Debe terminar en `OK`.
3. Si falta un permiso, corrige el rol en **Admin → Roles y permisos**; si falta un usuario, créalo (alta de colaborador) o restaura el respaldo.
4. Cada persona debe además entrar **una vez** con su cuenta Microsoft real antes de la demo, para comprobar que no sale ningún aviso rojo de permiso.
