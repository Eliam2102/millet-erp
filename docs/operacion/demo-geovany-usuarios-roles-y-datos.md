# Demo de Geovany — usuarios, roles, datos y entorno por módulo

> Para que Eliam deje listo **antes de las 9:00** todo lo que Geovany necesita
> para presentar cada módulo sin errores en pantalla: Maestros, Compras,
> Almacén y Cuentas por Pagar con su paso a Tesorería.
> Sale de leer el código y de la base de prueba local (`millet_test`) y de la de
> demo (`millet_demo`) el 2026-10-11. **No se ejecutó ninguna pantalla con
> estos roles completos**: lo que no se pudo comprobar está marcado como
> *(no verificado)*. Reemplaza a `demo-compras-roles-y-flujo.md`.

## 0. Lo esencial

1. **Geovany presenta como `super-admin`** (273 permisos). Con ese rol **no
   puede salir** el aviso rojo *"Ya no tienes permiso para esta acción –
   Requiere el permiso X"*. Lo que le puede fallar son **datos que no
   existen** (sección 4) o el **entorno** (sección 5).
2. Los demás usuarios (sección 1) solo hacen falta para **mostrar roles
   distintos** (quién firma qué, quién ve la CLABE, el auditor). Si alguno
   está mal, el aviso rojo aparece **solo en su pantalla**.
3. Cada usuario tiene **un solo rol por empresa**, y nadie puede cambiar su
   propio rol. Por eso cada rol de abajo repite los permisos transversales.
4. **La causa de casi todos los avisos rojos** son llamadas secundarias de la
   pantalla (catálogos, usuarios, sucursales), no la acción principal. Mira la
   columna "Sin esto sale aviso" de la sección 2.
5. El ambiente de demo es **otra base** (`millet_demo`) distinta a la de
   prueba. **Los roles hay que crearlos ahí** (Admin → Roles y permisos); lo
   hecho en `millet_test` no pasa solo.

## 1. Usuarios y roles

Cuentas Microsoft reales del tenant (deben existir; hay MFA):

| Persona | Rol | Para qué se usa en la demo |
|---|---|---|
| **Geovany** | `super-admin` (ya existe) | Presenta todo. Crea requisición y OC, adjunta cotización, configura, registra recepción en Almacén |
| fio | `compras-n1` — Jefe de Compras (N1) | Primera firma de requisiciones y de OC; da de alta un proveedor (Maestros) |
| Mariana | `compras-n2` — Dirección (N2) | Segunda firma de requisiciones y de OC |
| Luis Ibarra | `cxp-demo` | Valida proveedores, captura la factura contra la OC |
| Dorio | `tesoreria-demo` | Edita datos bancarios, registra el pago |
| Fernando Aguilar | `almacen-jefe` | Recepción parcial 6 de 10, si no la hace Geovany |
| qa.auditor | `auditor` (ya existe en el seed) | Perfil de solo lectura |

Notas:
- Las asignaciones de Luis, Dorio, Fernando y qa.auditor son **una propuesta**:
  elige las cuentas que quieras. Solo fio y Mariana se probaron.
- `cxp` y `tesoreria` del seed son **roles del sistema y no se pueden
  editar** (`ROL_DEL_SISTEMA_NO_EDITABLE`); por eso se crean `cxp-demo` y
  `tesoreria-demo` nuevos.
- **Reglas de firma de la OC:** quien crea la OC **no puede firmarla en ningún
  nivel** (`OC_AUTOAUTORIZACION`) y N1 y N2 deben ser **personas distintas**
  (`OC_FIRMA_MISMA_PERSONA`). Por eso: Geovany crea, fio N1, Mariana N2.
- En el ambiente de demo hoy: Geovany es `super-admin`, fio tiene
  `DEMO-ROL-Contabilidad`, qa.auditor `DEMO-ROL-DAF` (inútil para auditar:
  reasígnalo a `auditor`) y qa.catalogos `DEMO-ROL-Facturación`. No hay nadie
  con rol de Compras, CxP, Tesorería ni Administrador DEMO.
- Los permisos de una sesión abierta se refrescan en **unos 5 minutos** (o
  reiniciando el backend). Si cambias el rol de alguien, que cierre sesión.
- Cambiar el rol de una persona **borra sus permisos personalizados**.

### Cómo asignar el rol
- Persona con empleado vinculado: Admin → Empleados → detalle → "Roles y
  accesos" → asignar.
- Persona **sin empleado** (fio, Mariana…): la sección "Cuentas de acceso"
  está **oculta del menú**, pero la ruta funciona por URL:
  `/admin/usuarios/<id>` → pestaña "Roles por empresa" → "Asignar rol".

## 2. Roles: permisos exactos

Se crean en **Admin → Roles y permisos** (`/admin/roles`). Todos llevan
`compartido.catalogos.leer`: casi cualquier selector (departamento, sucursal,
almacén, artículo, proveedor) lo pide.

### `compras-n1` — Jefe de Compras (N1) — 22 permisos
- `compartido.catalogos.leer`, `identidad.usuarios.leer`
- `compras.requisiciones.`: `leer`, `autorizar-nivel1`, `rechazar`,
  `adjuntos-ver`, `ver-todos-departamentos`, `leer-todas-sucursales`,
  `gestionar-todas-sucursales`
- `compras.ordenes.`: `leer`, `crear`, `crear-sin-rq`, `adjuntar`,
  `autorizar-nivel1`, `cancelar`, `reportes-partidas-abiertas`,
  `leer-todas-sucursales`, `gestionar-todas-sucursales`
- Para que fio **dé de alta un proveedor** en Maestros:
  `datos_maestros.proveedores.gestionar`, `compartido.catalogos.administrar`,
  `datos_maestros.proveedores.adjuntos-ver`,
  `datos_maestros.proveedores.adjuntos-subir`
  (*`administrar` también permite editar/desactivar proveedores y artículos*).
- Recomendado para abrir la trazabilidad después de la recepción:
  `almacen.entradas.leer-todas-sucursales`,
  `cuentas_por_pagar.facturas.leer-todas-sucursales`,
  `tesoreria.documentos.leer-todas-sucursales`.

> En la base de prueba fio tiene hoy los primeros 18 (sin los de proveedor ni
> los recomendados).

### `compras-n2` — Dirección (N2) — 15 permisos
- `compartido.catalogos.leer`, `identidad.usuarios.leer`
- `compras.requisiciones.`: `leer`, `autorizar-nivel2`, `rechazar`,
  `adjuntos-ver`, `ver-todos-departamentos`, `leer-todas-sucursales`,
  `gestionar-todas-sucursales`
- `compras.ordenes.`: `leer`, `autorizar-nivel2`, `adjuntar`,
  `reportes-partidas-abiertas`, `leer-todas-sucursales`,
  `gestionar-todas-sucursales`
- Recomendados: los mismos tres de trazabilidad que `compras-n1`.

### `cxp-demo` — Cuentas por Pagar
- `compartido.catalogos.leer`, `identidad.usuarios.leer` (TC, viáticos,
  aprobadores)
- `compras.ordenes.leer`, `compras.ordenes.leer-todas-sucursales`,
  `compras.requisiciones.leer-todas-sucursales`,
  `almacen.entradas.leer-todas-sucursales`,
  `tesoreria.documentos.leer-todas-sucursales`
- `cuentas_por_pagar.facturas.`: `leer`, `capturar`, `editar`, `cancelar`,
  `autorizar`, `enviar-revision`, `liberar-revision`, `leer-todas-sucursales`,
  `gestionar-todas-sucursales`, `adjuntos-ver`, `adjuntos-subir`,
  `adjuntos-baja`
- `cuentas_por_pagar.documentos.leer-todas-sucursales`, `…gestionar-todas-sucursales`
- `cuentas_por_pagar.cfdis.`: `leer`, `cargar-manual`, `descartar`
- `cuentas_por_pagar.notas-credito.`: `leer`, `capturar`;
  `…anticipos.`: `leer`, `capturar`; `…notas-cargo.`: `leer`, `crear`, `aplicar`
- `cuentas_por_pagar.reportes.`: `cartera`, `antiguedad`, `tc`,
  `leer-todas-sucursales`; `cuentas_por_pagar.retenciones.leer`
- `datos_maestros.proveedores.`: `gestionar`, `validar`, `tolerancia-editar`,
  `bancarios-ver`, `adjuntos-ver`, `adjuntos-subir`
- Solo si se enseñan: `…comprobaciones.*`, `…viaticos.*`, `…tc.*`,
  `…reposiciones.*`, `…catalogos.*.administrar`.
- **No** le des `bancarios-editar` (CxP ve la CLABE enmascarada y no edita).

### `tesoreria-demo` — Tesorería
- `compartido.catalogos.leer`
- `tesoreria.`: `pasivos.ver`, `pagos.aplicar`, `pagos.revertir`,
  `pasivos.solicitar-cancelacion`, `cuentas.ver` (+`cuentas.administrar` si
  crea la cuenta bancaria en vivo), `movimientos.ver`, `reportes.ver`,
  `repp.registrar`, `documentos.leer-todas-sucursales`,
  `documentos.gestionar-todas-sucursales`
- `datos_maestros.proveedores.`: `gestionar`, `bancarios-ver`, `bancarios-editar`
- `cuentas_por_pagar.cfdis.leer` (vincular el complemento de pago)
- `tesoreria.movimientos.ver-cuenta-completa`: **decisión tuya**; con él ve la
  CLABE completa, sin él enmascarada.

### `almacen-jefe` — Jefe de Almacén
- Transversales: `compartido.catalogos.leer`, `compras.ordenes.leer`,
  `compras.ordenes.leer-todas-sucursales`, `identidad.usuarios.leer`,
  `compras.requisiciones.leer`, `cuentas_por_pagar.cfdis.leer`,
  `cuentas_por_pagar.facturas.leer`
- `almacen.`: `entradas.leer`, `entradas.registrar`,
  `entradas.leer-todas-sucursales`, `entradas.gestionar-todas-sucursales`,
  `almacenes.leer`, `almacenes.administrar`, `ubicaciones.leer`,
  `ubicaciones.administrar`, `asignaciones.leer`, `asignaciones.administrar`
- Si se enseñan Salidas / Devoluciones / Inventarios / Reorden / Cierre:
  `salidas.leer-todas`, `salidas.registrar`, `salidas.por-vale`,
  `devoluciones-internas.leer`, `devoluciones-internas.capturar`,
  `devoluciones-proveedor.iniciar`, `devoluciones-proveedor.autorizar`,
  `devoluciones-proveedor.registrar`, `inventarios.leer`, `inventarios.crear`,
  `inventarios.capturar`, `inventarios.aprobar-nivel1/2/3`, `reorden.leer`,
  `reorden.administrar`, `cierre-mes.ejecutar`, `reportes.alfak`,
  `reportes.mp-cnk` (más los `*-todas-sucursales` de salidas y reorden).
- *(Todos con prefijo `almacen.`.)*

### Rol `auditor` (ya existe)
Trae `admin.auditoria.leer`, `admin.empresas.leer`, `identidad.usuarios.leer`,
`identidad.roles.leer`, `compartido.catalogos.leer`. Abre `/admin/auditoria`
completa. **No** abre Maestros, y **Salidas de Almacén le da aviso** (no tiene
`salidas.leer-*`).

### Ajustes a los roles DEMO que ya existen en `millet_demo`
Los permisos de un rol solo se asignan al crearlo; los permisos nuevos de
migraciones **no se agregan solos**.
- `DEMO-ROL-Administrador` y `DEMO-ROL-CxP`: les falta
  `datos_maestros.proveedores.tolerancia-editar`.
- `DEMO-ROL-CxP`: **quitar** `bancarios-editar`.
- `DEMO-ROL-Tesorería`: no abre Proveedores; agregar `proveedores.gestionar`,
  `bancarios-ver`, `bancarios-editar`.
- `DEMO-ROL-Compras`/Capturista/Jefe/Dirección: no tienen ningún permiso de
  `datos_maestros`.
- `DEMO-ROL-Facturación`: cubre Clientes; para sincronizar Productos A+W
  agregar `datos_maestros.productos-aw.gestionar`.

## 3. Pantalla por pantalla: qué sale en rojo y por qué

### Maestros
| Pantalla | Permiso | Sin esto sale aviso |
|---|---|---|
| Proveedores (lista y detalle) | `datos_maestros.proveedores.gestionar` | Sin él redirige en silencio al inicio |
| Alta/editar/desactivar proveedor | `compartido.catalogos.administrar` (**no** `gestionar`) | Con solo `gestionar` ve la lista pero sin botón "Nuevo" |
| Detalle de proveedor → expediente | `proveedores.adjuntos-ver` | Un rol con `validar` sin `adjuntos-ver` da 403 |
| Validar / rechazar / poner en revisión | `proveedores.validar` | — |
| Datos bancarios | ver `bancarios-ver`, editar `bancarios-editar` | Sin `ver` la sección no aparece (sin error) |
| Artículos | `datos_maestros.articulos.gestionar` (alta: `catalogos.administrar`) | Formularios piden `catalogos.leer` |
| Clientes (+ sincronizar, fiscales, origen) | `datos_maestros.clientes.gestionar` (+ `sincronizar`, `fiscal-editar`, `origen-ver`) | Sin `origen-ver` el backend omite los datos sin error |
| Productos A+W | `datos_maestros.productos-aw.gestionar` | — |
| `/admin/auditoria` | `admin.auditoria.leer` **más** `admin.empresas.leer`, `identidad.usuarios.leer`, `identidad.roles.leer`, `compartido.catalogos.leer` | Con solo `auditoria.leer` salen **3 avisos** |

### Compras
| Pantalla | Permiso | Sin esto sale aviso |
|---|---|---|
| Requisiciones | `compras.requisiciones.leer` + alcance de sucursal | — |
| Nueva requisición | `…crear` + (`gestionar-todas-sucursales` **o** asociación a la sucursal) | El selector de departamentos exige estar **asociado a la sucursal**: `gestionar-todas-sucursales` **no** basta (`SUCURSAL_NO_ASOCIADA`) |
| Pendientes de requisición | `autorizar-nivel1` o `autorizar-nivel2` | — |
| Órdenes (bandeja, detalle, todas) | `compras.ordenes.leer` | **`identidad.usuarios.leer`**: todas las pantallas de OC lo cargan |
| Pestaña Adjuntos de la OC | `compras.ordenes.adjuntar` aunque solo mires | Sin él, 403 al abrir la pestaña |
| Rechazar / cancelar (RQ y OC) | `compras.requisiciones.leer` (motivos de rechazo) | Un rol solo de OC lo necesita |
| Línea manual de OC | `ordenes.crear` + `ordenes.crear-sin-rq` | El picker de centro de costo pide `crear-sin-rq` |
| Partidas abiertas | `ordenes.reportes-partidas-abiertas` | — |
| Trazabilidad **después** de recibir/facturar/pagar | `almacen.entradas.leer-todas-sucursales`, `cuentas_por_pagar.facturas.leer-todas-sucursales`, `tesoreria.documentos.leer-todas-sucursales` | Sin ellos, 403 en la trazabilidad |
| Aprobadores | `compras.aprobadores.administrar` (+ `usuarios.leer`) | — |

### Almacén
| Pantalla | Permiso | Sin esto sale aviso |
|---|---|---|
| Recepciones (lista) | `almacen.entradas.leer` | **`almacen.almacenes.leer`**: carga los sub-almacenes |
| Nueva recepción | `entradas.registrar` | Selector de OC `compras.ordenes.leer`; ubicaciones `ubicaciones.leer`; bin `asignaciones.leer` |
| Saldos | `almacen.almacenes.leer` | Filtro de artículo pide `catalogos.leer` |
| Salidas | ruta con `salidas.leer-propias` o `leer-todas`, **pero el GET exige solo `leer-todas`** | Con solo `leer-propias`, aviso |
| Devoluciones | ruta acepta varios; la lista exige `devoluciones-internas.leer` | Con solo permisos de proveedor, aviso |
| Almacenes, ubicaciones, asignaciones | `…leer` (el menú se oculta sin `almacenes.administrar`) | — |

### Cuentas por Pagar y Tesorería
| Pantalla | Permiso | Sin esto sale aviso |
|---|---|---|
| Facturas (lista/detalle) | `…facturas.leer` + `…facturas.leer-todas-sucursales` | Sin el bypass la lista sale **vacía**; por id, 403 |
| Capturar factura con OC | `…facturas.capturar` + `…gestionar-todas-sucursales` | Selector de OC: `compras.ordenes.leer` y `leer-todas-sucursales`; solo lista OC **Autorizadas** |
| CFDI | `…cfdis.leer` + `…documentos.leer-todas-sucursales` | Lo piden también captura, NC, anticipo y REPP |
| "Ver trazabilidad" de la factura | `compras.ordenes.leer` + los 4 bypass | Si falta uno, 403 |
| Proveedor (expediente) | `proveedores.adjuntos-ver` | El rol `cxp` del seed da 403 aquí |
| Tesorería → Pagos (bandeja) | `tesoreria.pasivos.ver` + `tesoreria.documentos.leer-todas-sucursales` | Sin el bypass, vacía |
| Registrar pago | `tesoreria.pagos.aplicar` + `…documentos.gestionar-todas-sucursales` | Sin `tesoreria.cuentas.ver` falla el selector de cuenta |

## 4. Datos que deben existir (aquí es donde falla Geovany)

### Maestros
- **Sincronización A+W** (si falta, el botón falla con `lectura_deshabilitada` /
  `origen_deshabilitado`): variables del API
  `IntegracionesAw__Clientes__LecturaHabilitada=true`,
  `…AplicacionHabilitada=true`, `IntegracionesAw__Productos__OrigenHabilitado=true`,
  `IntegracionesAw__OrigenDemo__Permitido=true`,
  `ConnectionStrings__AwOrigenPgDb` y el **mapeo de moneda**
  `PESOSMX=MXN`, `USD=USD`, `Euro=EUR` (sin mapeo la moneda del cliente no se
  crea). `tools/demo/demo.sh api` ya los exporta. Origen activo: `Demo`.
- Catálogos (monedas, regímenes, usos CFDI, formas de pago, categorías,
  unidades): en `millet_demo` ya existen.
- Proveedores: `DEMO-PROV-ACT` (activo, 5 documentos) y `DEMO-PROV-REV` (en
  revisión, **3 de 5 documentos**: hay que cargar los 2 que faltan en vivo
  para validar). Banco/CLABE/beneficiario: los edita Tesorería.
- **Trampa de la bitácora:** `/admin/auditoria` arranca filtrando por la
  **sucursal activa** y las 546 filas de Maestros no tienen sucursal →
  **sale vacía**. Cambiar el filtro de sucursal a "Todas".
- Datos reales: la copia completa de A+W trae clientes reales; la guía pide no
  usarlos en la demo (usar el sandbox sintético).

### Compras
- **Umbral para tener dos firmas en la requisición:**
  `compras.umbrales_aprobacion_departamento` **no tiene pantalla**. Sin fila
  vigente, la requisición pide **una sola firma**. Para dos: insertar
  `(empresa_id, departamento_id, vigente_desde, umbral_monto bajo p. ej. 1,
  moneda 'MXN')` o usar un artículo de naturaleza Crítico/Riesgo.
  *(Nombres de columna inferidos, no verificados.)*
- **Aprobadores** (`/compras/admin/aprobadores`): hoy **ningún flujo de
  autorización los lee**; cargarlos es cosmético (solo llena esa pantalla). El
  diálogo siempre pide Departamento, aun para `AutorizadorN2` (que en el
  modelo es por sucursal).
- **Centro de costo:** la línea de la requisición lo hereda de la
  equivalencia sucursal × departamento (`/centros-costo/configuracion`). Sin
  ella: `CECO_DEPARTAMENTO_SIN_EQUIVALENCIA`. Debe estar activo.
- El **solicitante** necesita estar asociado a la sucursal y tener un empleado
  vinculado a un departamento con equivalencia. El departamento debe estar
  ligado y activo en esa sucursal (`RQ_DEPTO_NO_OPERA_EN_SUCURSAL`).
- **Artículo sin existencias** en esa sucursal: si el stock cubre la
  cantidad, la requisición no deja saldo de compra (`RQ_SIN_SALDO_DE_COMPRA`)
  y no se puede crear la OC.
- Proveedor **Activo** (se revalida al enviar y en cada firma), con
  condiciones de pago y uso principal. Artículos activos.
- Setting `auto_generar_oc_al_autorizar` en **false** (en true usa un stub en
  memoria).
- Tipos de documento de OC `cotizacion`, `ficha_tecnica`, `correo_autorizacion`
  (si faltan: `TIPO_DOCUMENTO_SEED_INCOMPLETO`) y motivos de rechazo activos.
- **Enviar a autorización exige la cotización adjunta.** Una OC sin
  requisición exige además motivo y el adjunto `correo_autorizacion`.
- La cancelación con doble firma solo aplica si **ya hay recepción**.

### Almacén (recepción parcial 6 de 10)
- **Ubicación real:** el seed solo trae ubicaciones "ÚNICA", que **no sirven
  para entradas** (`ENTRADA_A_UBICACION_UNICA`). Crear al menos una ubicación
  real (rack) en el sub-almacén y una **asignación activa artículo → rack**
  para cada artículo de la OC (`ENTRADA_SIN_ASIGNACION`).
- Todas las líneas de la recepción en el **mismo sub-almacén**.
- **OC en estado Autorizada**, con líneas que no sean servicio y con artículo,
  precio MXN > 0 y **sucursal destino = la del almacén**.
- Unidad del artículo activa y de la misma dimensión que la de la OC (en el
  seed hay PZA y M2; **no hay HOJA**).
- **Periodo contable 2026-10 abierto** (ejercicio y periodo en Contabilidad,
  permiso `contabilidad.periodo.administrar`); si no: `PERIODO_CONTABLE_NO_ADMITE`.
- Variante A: CFDI vinculado o UUID manual. Variante B: archivo de packing
  list (máx. 20 MB).
- **No existe "asignar usuarios a almacenes"**: el alcance es por sucursal
  (asociación) o por los bypass.

### Cuentas por Pagar y Tesorería
- Proveedor **Activo** con **5 documentos** y datos bancarios (banco, CLABE,
  beneficiario) capturados por Tesorería. Para enseñar "En revisión", poner
  uno en revisión desde la pantalla.
- **OC Autorizada** con el mismo proveedor, sucursal y moneda que la factura
  (`OC_SUCURSAL_DISTINTA`, `OC_MONEDA_DISTINTA`).
- **Recepción registrada** en Almacén: sin ella lo elegible es 0 y Tesorería
  responde `PASIVO_NO_PAGABLE_CXP` (y `PAGO_EXCEDE_ELEGIBLE` si el importe supera lo elegible).
- Periodo contable abierto (cubre la fecha de contabilización, hoy y la fecha
  valor del pago): si no, `CXP_PERIODO_CERRADO` / `MOV_PERIODO_CERRADO`.
- **Cuenta bancaria de Tesorería** en MXN activa (hoy hay 0).
- Aprobadores de CxP (`/cxp/admin/aprobadores`), políticas de viáticos,
  tarjetas y reposición: hoy hay 0 → esas pantallas salen **vacías** (sin
  error).
- CFDI/XML (opcional): tipo Ingreso, RFC emisor = RFC del proveedor, mismos
  UUID, TC y moneda y totales cuadrados.
- Tolerancia general 0.99 MXN; ninguna por proveedor.

## 5. Entorno (sin esto Compras no se actualiza)

- **Service Bus:** el "Recibido / Por surtir" de la OC, el paso de Almacén a
  CxP y el de CxP a Tesorería viajan por eventos. Sin Service Bus el backend
  usa un emisor que **descarta el evento** y Compras no se entera. Hace falta
  `ServiceBus:Modo=EmuladorLocal` (`tools/demo/demo.sh eventos`,
  `tools/servicebus-emulator/levantar.sh`) o la conexión a Azure. Hay un
  pequeño retardo del publicador.
- **Almacenamiento de adjuntos:** en local se usa un stub de archivos; sin
  cadena de blob, las cotizaciones y packing lists se guardan ahí. *(No
  verificado en el ambiente de demo.)*
- **Login real de Microsoft** con MFA: cada persona debe iniciar sesión una
  vez antes de la demo.

## 6. Orden recomendado de preparación (para Eliam)

1. Crear los 5 roles de la sección 2 y asignarlos (sección 1). Cerrar sesión
   y volver a entrar con cada cuenta, o esperar 5 minutos.
2. Contabilidad: crear y abrir ejercicio 2026 / periodo 10.
3. Tesorería: crear la cuenta bancaria MXN; Tesorería captura la CLABE del
   proveedor de la demo.
4. Sembrar el umbral de aprobación del departamento (sección 4, Compras).
5. Centros de costo: equivalencia sucursal × departamento; asociar al
   solicitante a la sucursal.
6. Almacén: ubicación real y asignación artículo → rack.
7. Verificar el setting de OC y los tipos de documento.
8. Encender el emulador de Service Bus.
9. **Ensayo completo con las cuentas reales:** requisición → 2 firmas → OC →
   cotización → 2 firmas → recepción 6 de 10 → "Recibido / Por surtir" →
   factura → pago.
10. Revisar la bitácora con el filtro de sucursal en "Todas".

## 7. Lista de verificación antes de las 9:00

- [ ] Roles creados y asignados; cada cuenta entra **sin aviso rojo** en su
      pantalla principal.
- [ ] `compartido.catalogos.leer` e `identidad.usuarios.leer` están en todos
      los roles de Compras, CxP y Almacén.
- [ ] Origen A+W en `Demo`, mapeos de moneda cargados, botón Sincronizar
      probado.
- [ ] `DEMO-PROV-REV` con los 2 documentos que faltan listos para cargar.
- [ ] Umbral sembrado y centro de costo con equivalencia.
- [ ] Ubicación real y asignación en Almacén; periodo contable abierto.
- [ ] Cuenta bancaria MXN y CLABE del proveedor.
- [ ] Emulador de Service Bus encendido; "Recibido / Por surtir" cambia
      tras la recepción.
- [ ] Ensayo completo de punta a punta cronometrado.
- [ ] Bitácora con sucursal "Todas".

## 8. No verificado

- Ninguna pantalla se ejecutó con `cxp-demo`, `tesoreria-demo` ni
  `almacen-jefe`; sus listas salen de leer los endpoints.
- Contenido de `millet_dev`: solo se consultaron `millet_test` y `millet_demo`.
- Nombres de columna del `INSERT` de umbrales; qué documentos marca el
  backend como obligatorios del expediente (se asume 5).
- Barra superior, búsqueda global y página de Inicio: no se revisaron sus
  llamadas.
- Que el emulador de Service Bus entregue los eventos y que los workers de
  Almacén → CxP → Tesorería estén corriendo.

## 9. Anexo — atajo SQL (solo base de prueba descartable)

Es lo que se ejecutó en `millet_test`. **No usar en dev ni en demo**: ahí se
crean los roles por pantalla.

```sql
-- Ejemplo con compras-n1; compras-n2 es igual con sus permisos (sección 2).
begin;
insert into identidad.roles (id,codigo,nombre,descripcion,es_del_sistema,activo,version,created_at,updated_at,created_by)
values ('00000002-0003-0000-0000-0000000000a1','compras-n1','Jefe de Compras (N1)','Autoriza N1 de requisiciones y OC. Todas las sucursales.',false,true,1,now(),now(),'seed-demo');

insert into identidad.rol_permisos (id,rol_id,permiso_id,version,created_at,updated_at,created_by)
select gen_random_uuid(),'00000002-0003-0000-0000-0000000000a1',p.id,1,now(),now(),'seed-demo'
from identidad.permisos p where p.codigo in (
 'compartido.catalogos.leer','identidad.usuarios.leer',
 'compras.requisiciones.leer','compras.requisiciones.autorizar-nivel1','compras.requisiciones.rechazar',
 'compras.requisiciones.adjuntos-ver','compras.requisiciones.ver-todos-departamentos',
 'compras.requisiciones.leer-todas-sucursales','compras.requisiciones.gestionar-todas-sucursales',
 'compras.ordenes.leer','compras.ordenes.crear','compras.ordenes.crear-sin-rq','compras.ordenes.adjuntar',
 'compras.ordenes.autorizar-nivel1','compras.ordenes.cancelar','compras.ordenes.reportes-partidas-abiertas',
 'compras.ordenes.leer-todas-sucursales','compras.ordenes.gestionar-todas-sucursales');

update identidad.usuario_empresa_roles set rol_id='00000002-0003-0000-0000-0000000000a1', updated_at=now()
 where usuario_id = :'id_fio' and deleted_at is null;
commit;
```
