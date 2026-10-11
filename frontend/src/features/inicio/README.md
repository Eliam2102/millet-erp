# Inicio por perfil · INI-ROL

Implementación local del 10-oct-2026 en `feat/INI-ROL-inicio-por-perfil`. La base del worktree coincide con `origin/main` (`d560a289`); la referencia local `main` era anterior. Commit, puerta de calidad y PR los hizo Claude el 10-oct-2026 (el sandbox de Codex no tiene Docker).

Se leyeron la propuesta aprobada `N_Propuesta_Inicio_ERP.html`, las instrucciones del repo/frontend y `design-system/DESIGN.md`. Se reutilizan `PanelInicio`, `IndicadoresInicio`, `MisPendientes`, `RecientesInicio`, `AccesosRapidos`, `pendienteVisible` y la prioridad existente. No se cambian navegación, Administración, infraestructura, variables de entorno ni acceso a la aplicación.

## Composición y solicitudes

`config.ts` es el registro único de 13 indicadores, tres visuales y las acciones rápidas. Cada fuente tiene permisos canónicos, prioridad y endpoint. Se elimina la fuente sin permiso **antes** de crear su observador de Query. Una pieza con varias fuentes muestra únicamente las permitidas.

- Hasta cinco indicadores, ordenados por cantidad urgente y prioridad. La fila reparte el ancho entre los existentes. Los ceros llevan contexto; carga y error no se sustituyen por cifras.
- Hasta dos visuales, elegidas por prioridad. Los circuitos aparecen antes del flujo, que utiliza el espacio restante, como la propuesta.
- Sin visuales: «Accesos de tu módulo», derivados de los accesos autorizados. La sesión no expone un módulo principal: se usa el primer módulo permitido del catálogo de navegación, sin inventar un rol.
- Pendientes: conserva listados y prioridad actuales; añade Todo/Autorizar/Revisar/Vencido. Solo esta tarjeta tiene scroll interno en escritorio.
- Cuatro franjas: saludo/resumen, indicadores, trabajo 1.55fr/1fr y recientes/crear. Altura de escritorio `100dvh - 3.5rem` desde 1024px. Móvil conserva una columna y flujo de página.
- Hoy/Semana/Mes filtra los circuitos por fecha de documento/captura en Mérida. No cambia saldos ni pendientes actuales. El flujo corresponde a la semana actual, de lunes a domingo.
- Recientes reutiliza el contrato y constructor de URL de auditoría, limitado a usuario/empresa/sucursal y tres eventos. Sin `admin.auditoria.leer` se omite la sección y su consulta; Crear rápido ocupa la franja. No se presenta una tarjeta bloqueada ni actividad inventada.
- Crear rápido abre los formularios existentes: requisición, OC, recepción, factura de proveedor y gestión de cobranza. Las hojas de recepción/factura se montan al pulsar su acción, evitando consultas ocultas al cargar Inicio.

Las claves incluyen usuario, empresa, sucursal y permisos; los circuitos incluyen periodo. Cada endpoint se deduplica. Resúmenes y pendientes conservan la promesa durante el montaje doble de StrictMode, sin consumir `AbortSignal`; una respuesta tardía permanece aislada por su clave. Hay 60 segundos de frescura, sin refetch por foco/reconexión ni retries en bucle. Recientes mantiene frescura y retención cero con clave propia de Inicio. No se modifica el hook de Administración.

## Piezas, permisos y endpoints

Prefijo común: `/api/v1/`. Son 19 rutas nuevas en siete módulos.

| Pieza | Permiso exigido | Endpoint |
|---|---|---|
| Por autorizar RQ | `compras.requisiciones.leer` y firma N1 o N2 del mismo recurso | `compras/inicio/autorizar-rq` |
| Por autorizar OC | `compras.ordenes.leer` y firma N1 o N2 del mismo recurso | `compras/inicio/autorizar-oc` |
| Tus requisiciones | `compras.requisiciones.leer` | `compras/inicio/requisiciones` |
| Recepciones pendientes | `almacen.entradas.leer`; alternativamente `compras.ordenes.leer` si falta el primero | `almacen/inicio/recepciones`, `compras/inicio/recepciones` |
| Conteos abiertos | `almacen.inventarios.leer` | `almacen/inicio/conteos` |
| Vales por regularizar | `almacen.salidas.leer-todas` | `almacen/inicio/vales` |
| Facturas de proveedor en revisión | `cuentas_por_pagar.facturas.leer` | `cuentas-por-pagar/inicio/facturas` |
| Por pagar esta semana | `tesoreria.pasivos.ver` | `tesoreria/inicio/pagos` |
| Saldo en bancos | `tesoreria.cuentas.ver` | `tesoreria/inicio/bancos` |
| Por cobrar vencido | `cuentas_por_cobrar.cartera.leer` | `cuentas-por-cobrar/inicio/vencido` |
| Pedidos / Timbrado con problema | `facturacion.facturas.leer` | `facturacion/inicio/pedidos`, `facturacion/inicio/timbrado` |
| Depósitos por confirmar | `tesoreria.depositos.confirmar` | `tesoreria/inicio/depositos` |
| Periodo contable | `contabilidad.periodo.leer` | `contabilidad/inicio/periodo` |
| Circuito de compra | `compras.ordenes.leer`; Tus RQ también exige `compras.requisiciones.leer` | `compras/inicio/circuito` |
| Circuito de venta | `facturacion.facturas.leer` y/o `cuentas_por_cobrar.cartera.leer`, por fuente | `facturacion/inicio/circuito`, `cuentas-por-cobrar/inicio/circuito` |
| Flujo de la semana | `tesoreria.movimientos.ver` | `tesoreria/inicio/flujo` |

Firmas canónicas: `compras.requisiciones.autorizar-nivel1` / `autorizar-nivel2` y `compras.ordenes.autorizar-nivel1` / `autorizar-nivel2`.

**Sustituciones factuales:** no existen permisos canónicos `recepciones.leer`, `pedidos.leer`, `comprobantes.leer` ni lectura separada de depósitos. Se usan las lecturas de los recursos más cercanos; depósitos conserva el permiso de su bandeja. No se crean ni asignan permisos. «Tus requisiciones» y «Conteos abiertos» satisfacen los ejemplos de Compras y Almacén.

## Servidor y reglas de negocio

Cada endpoint tiene `RequirePermission`, autenticación y query MediatR en su módulo dueño. Autorizaciones exigen además firma N1/N2. Validators comprueban piezas, sucursal no vacía y periodo válido. No hay escrituras ni eventos nuevos.

`SucursalScopeQueryBehavior` aplica ADR-0051 y el bypass corporativo del recurso. Una selección del topbar valida asociación con `SucursalScopeGuard`, incluso para corporativos. Estos ven todas sin selección y únicamente la seleccionada cuando existe. Documentos mixtos requieren todas sus sucursales; los de origen indeterminado conservan la restricción corporativa.

Los resúmenes reciben `sucursalId`. Los pendientes reutilizados envían `X-Inicio-Sucursal-Id` para que el mismo pipeline restrinja sus queries scoped; catálogos globales conservan el alcance por empresa. Recientes usa el filtro existente de auditoría.

- Compras: RQ propias y abiertas. Recepciones/circuito operativos son OC del comprador titular; corporativos conservan todas en su alcance. Autorizaciones reutilizan criterios N1/N2 de las bandejas con `PendientesAutorizacionLectura`; no se inventa una asignación individual de firma que el listado actual no aplica. Las 48 horas se miden desde solicitud/captura, no desde un evento nuevo de asignación. El circuito muestra conteos de OC, incluido subestado de pago, sin montos ni consultas a Tesorería.
- Almacén: obtiene OC por recibir mediante `IComprasRecepcionesReadPort`, implementado por Compras. Vales usan la regularización existente. Conteos abiertos se scoped por subalmacenes/almacenes, sin exponer existencias teóricas.
- Cuentas por pagar: facturas Capturada/EnRevision y diferencia contra OC existente.
- Tesorería: pasivos reutilizan elegibilidad de la bandeja con `PasivosPendientesLectura`: no bloqueados, saldo positivo, vencimiento hasta siete días, incluyendo vencidos. Bancos reutilizan `SaldosPorCuentaQuery`, ahora scoped. Flujo comparte cuentas/movimientos del reporte (`FlujoLectura`); agrupar por día es nuevo porque el reporte agrupa por concepto. Son ingresos/egresos registrados, sin proyecciones ni saldos de arranque en la gráfica.
- Cartera vencida reutiliza `AntiguedadSaldosQuery` y sus abonos, notas y buckets. Venta compone etapas de Facturación y/o Cartera según permisos; no consulta el módulo ausente.
- Facturación conserva alcance de caja además de sucursal; pedidos A+W Importado, excepciones y TimbradoFallido.
- Periodo contable es global de la empresa, conforme a F1-CON-03; no se inventa cierre por sucursal.
- Montos **solo MXN**, indicado en contexto. No se mezclan monedas ni se crea conversión. Sin decimales y con `tabular-nums`, siguiendo la ficha sobre el formato resumido general del design system.

## Pruebas y evidencia

Evidencia local en `outputs/INI-ROL/evidencia/`, desde la raíz del worktree. Resultados finales:

| Verificación | Resultado |
|---|---|
| Build completo backend, restore cacheado y un nodo | 0 errores, 0 advertencias |
| Unitarias API | 54 aprobadas |
| Unitarias Compras | 578 aprobadas |
| Unitarias Almacén | 335 aprobadas |
| Unitarias Cuentas por pagar | 446 aprobadas |
| Unitarias Cuentas por cobrar | 121 aprobadas |
| Unitarias Tesorería | 132 aprobadas |
| Unitarias Facturación | 453 aprobadas |
| Unitarias Contabilidad | 180 aprobadas |
| `tsc` raíz, `tsc` app, `typecheck:test` | salida 0 |
| `npm run -s lint` | 0 errores; 10 advertencias preexistentes fuera de Inicio |
| `npx vitest run` | 374 archivos, 2,143 pruebas aprobadas |
| Build aislado de layout | salida 0; advertencias de bundle/importación dinámica |
| `git diff --check` | salida 0 |

Las **2,299 unitarias backend** se ejecutaron con el runner oficial xUnit en proceso, sin adaptador VSTest. VSTest no pudo abrir su socket (`SocketException (13): Permission denied`). Se conserva runner y resultados por suite para reproducibilidad; no se presenta ese intento de `dotnet test` como verde.

Pruebas frontend: cinco perfiles en el store con MSW y StrictMode; ausencia de tarjetas y llamadas bancarias/cartera/pasivos para Compras; piezas de Almacén, Cobranza y Tesorería; máximo cinco para Administrador; tres piezas sin huecos; fallback sin visuales; revocación; error sin cero; cambio de sucursal; tabs; periodo que solo consulta circuitos; cada URL una vez al montar y sin refetch al rerenderizar.

Backend: pipeline propio/ajeno/corporativo/selección; encabezado de pendientes; cuentas propias/ajenas/mixtas/sin origen; RQ por capturista y sucursal; alcance al puerto; fechas y validators. PostgreSQL añade 19 rutas con 403 sin permiso o permiso ajeno, alcance operativo/corporativo/seleccionado y firma adicional; crea y limpia RQ en dos sucursales usando catálogos del seed.

## Por confirmar y siguiente acción

1. Ejecutar **completo** `tools/validate-integration-isolated.sh` y rojo/verde con Claude en PostgreSQL desechable. Integración escrita y compilada; el sandbox no tiene Docker. No se afirma haber verificado el 403 HTTP ni SQL real de esta suite.
2. Ejecutar `frontend/tools/ini-rol-layout/verificar.cjs` donde Chromium pueda arrancar. Recorre cinco perfiles a 1440×900, 1280×800 y móvil, comprueba scroll/recorte y guarda mediciones/captura. Aquí Chromium no arrancó; 5184 también fue rechazado. No se usaron 5080 ni 5173. **Ausencia de scroll y coincidencia visual exacta: Por confirmar.**
3. Revisar con sesiones reales autorizadas y sucursales del cliente. Los perfiles/datos de pruebas son fixtures; no prueban UAT, aceptación ni operación publicada.
4. Trasladar a Obsidian el borrador siguiente cuando se autorice esa escritura. La bóveda queda fuera del worktree y no se modificó.

### Borrador de bitácora

10-oct-2026: INI-ROL construido localmente sobre `origin/main`, con catálogo por permisos, resúmenes por módulo y alcance por sucursal, cuatro franjas y pruebas locales. Build, unitarias backend y frontend aprobadas según la evidencia de este paquete. PostgreSQL, medición visual a 1440×900/1280×800 y UAT pendientes. Sin commit, push, despliegue ni aceptación Millet.

Mensaje de commit propuesto, **sin ejecutarlo**: `feat(inicio): adaptar el inicio por permisos y sucursal`.
