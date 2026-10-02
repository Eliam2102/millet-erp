# ADM-10 - Inventario de acciones de mutacion vs permisos (A1-A2)

> Solo lectura (F1-ADM-10, paso A). Fecha: 2026-10-01. Rama `feature/f1-adm-10-navegacion-por-permisos`.
> Alcance pedido: componentes `.tsx` (sin tests) en `frontend/src/modules` y `frontend/src/components`.
> Fuera de alcance de esta tabla: configuracion generica (`admin/$modulo/settings`, `*SettingsEndpoints`): solo se anota (PR #26 la cubre) y `frontend/src/features/**` (ver seccion 4).

## 1. Comando reproducible (A1)

Ejecutar desde `frontend/`:

```sh
PAT='\b(Nuev[oa]s?|Editar|Eliminar|Desactivar|Aprobar|Rechazar|Cancelar|Sincronizar|Reintentar)\b|[Mm]utat|\.mutate'
G='useHasPermission|useHasAnyPermission|useHasAllPermissions|RequirePermission'
rg -l -g '*.tsx' -g '!*.test.tsx' "$PAT" src/modules src/components | sort > comp.txt   # 90 archivos
rg -l "$G" $(cat comp.txt) | sort > gated.txt                                           # 48 con gate en el propio archivo
comm -23 comp.txt gated.txt                                                               # 42 sin gate propio (gate en padre / props / sin mutacion)
```

Endpoint y permiso de API: hooks en `src/modules/*/api/*.ts` -> `backend/src/Api/Endpoints/**` (`RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.X)`, a nivel de grupo o de ruta). Prefijo de permisos omitido (`PermisosCanonicos.`).

## 2. Tabla (A2)

Estados: OK / sin gate UI / sin permiso API / permiso no coincide. "OK" en filas "padre:" significa que el componente no se gatea solo, pero se monta unicamente desde un padre gateado con el mismo permiso que exige la API (o recibe el permiso por prop).

| Componente | Accion | Permiso en UI | Endpoint | Permiso en API | Estado |
|---|---|---|---|---|---|
| `administracion/CanalesVentaPage` | Desactivar canal | AdminEmpresasSucursalesGestionar | PATCH /admin/canales-venta/{id} | AdminEmpresasSucursalesGestionar (grupo) | OK |
| `administracion/CanalVentaInlineForm` | Nuevo/Editar canal | padre: CanalesVentaPage (mismo permiso) | POST /admin/canales-venta; PATCH /{id} | AdminEmpresasSucursalesGestionar | OK |
| `administracion/DepartamentosPanel` | Nuevo/Editar/Desactivar depto | AdminDepartamentosGestionar | POST/PATCH /admin/departamentos, POST /{id}/desactivar/reactivar | AdminDepartamentosGestionar | OK |
| `administracion/DepartamentoInlineForm` | Nuevo/Editar depto | padre: DepartamentosPanel | POST/PATCH /admin/departamentos | AdminDepartamentosGestionar | OK |
| `administracion/PuestosPage` | Desactivar/Reactivar puesto | AdminPuestosGestionar | POST /admin/puestos/{id}/desactivar/reactivar | AdminPuestosGestionar | OK |
| `administracion/PuestoInlineForm` | Nuevo/Editar puesto | padre: PuestosPage | POST/PATCH /admin/puestos | AdminPuestosGestionar | OK |
| `administracion/EmpleadosPage` | Editar/Desactivar/Reactivar empleado; dar/reenviar acceso | AdminEmpleadosGestionar; acceso: IdentidadUsuariosCrear | PATCH/POST /admin/empleados/{id}[/desactivar/reactivar]; POST /admin/colaboradores/{id}/acceso[/reenviar] | AdminEmpleadosGestionar; acceso: IdentidadUsuariosCrear + IdentidadAsignacionesAdministrar | permiso no coincide (menor: boton "Dar acceso" exige solo UsuariosCrear; el panel y la API exigen ademas AsignacionesAdministrar) |
| `administracion/EmpleadoAccesoPanel` | Dar acceso / Reactivar / Reintentar | IdentidadUsuariosCrear + IdentidadAsignacionesAdministrar; reactivar: IdentidadUsuariosEditar | POST /admin/colaboradores/{id}/acceso[/reintentar]; POST /identidad/usuarios/{id}/reactivar | UsuariosCrear + AsignacionesAdministrar; reactivar: UsuariosEditar | OK |
| `administracion/EmpleadoInlineForm` | Editar empleado / crear con acceso | padre: EmpleadosPage, SucursalColaboradoresTab, EmpleadoDetalle; acceso: UsuariosCrear + AsignacionesAdministrar | PATCH /admin/empleados/{id}; POST /admin/colaboradores | AdminEmpleadosGestionar; colaboradores: UsuariosCrear + AsignacionesAdministrar | OK |
| `administracion/EmpleadoDetalle` | Editar datos y transferir sucursal; asignar sucursal | Editar: ninguno en componente (la ruta /admin/empleados/$id tiene beforeLoad AdminEmpleadosGestionar); sucursales: AdminSucursalesUsuariosGestionar | PATCH /admin/empleados/{id}; POST /admin/empresas/sucursales/{s}/usuarios/{u}[/desactivar/reactivar] | AdminEmpleadosGestionar; AdminSucursalesUsuariosGestionar | sin gate UI (boton "Editar datos..." sin gate; mitigado por beforeLoad de la ruta) |
| `administracion/EmpresaDatosForm` | Editar empresa | AdminEmpresasEditar | PATCH /admin/empresas/{id} | AdminEmpresasEditar | OK |
| `administracion/EmpresaDetalle` | Desactivar empresa | AdminEmpresasDesactivar | POST /admin/empresas/{id}/desactivar | AdminEmpresasDesactivar | OK |
| `administracion/ParametrosPage` | Editar parametro | AdminParametrosEditar | PATCH /admin/parametros/{clave} | AdminParametrosEditar | OK |
| `administracion/SeriesPage` | Desactivar serie | AdminSeriesGestionar | POST /admin/series/{id}/desactivar | AdminSeriesGestionar | OK |
| `administracion/SerieFilaEditable` | Editar serie | padre: SeriesPage | PATCH /admin/series/{id} | AdminSeriesGestionar | OK |
| `administracion/SheetNuevaSerie` | Nueva serie | padre: ruta admin/series (beforeLoad + SeriesPage) | POST /admin/series | AdminSeriesGestionar | OK |
| `administracion/SucursalesPanel` | Desactivar sucursal; alta de deptos/puestos | AdminEmpresasSucursalesGestionar; AdminDepartamentosGestionar; AdminPuestosGestionar | POST /admin/empresas/sucursales/{id}/desactivar | AdminEmpresasSucursalesGestionar | OK |
| `administracion/SucursalInlineForm` | Nueva/Editar sucursal | padre: SucursalesPanel/SucursalesLayout (AdminEmpresasSucursalesGestionar) | POST/PATCH /admin/empresas/sucursales | AdminEmpresasSucursalesGestionar | OK |
| `administracion/SucursalOrganizacionTab` | Asignar/Desactivar/Reactivar depto y puesto de sucursal | props de SucursalDetalle: AdminSucursalesDepartamentosGestionar / AdminSucursalesPuestosGestionar; alta maestro: AdminDepartamentosGestionar / AdminPuestosGestionar | /admin/empresas/sucursales/{s}/departamentos/puestos/... | AdminSucursalesDepartamentosGestionar / AdminSucursalesPuestosGestionar | OK |
| `administracion/SucursalDepartamentosTab` | Asignar/Desactivar depto | padre: prop canGestionar (AdminSucursalesDepartamentosGestionar) | POST /admin/empresas/sucursales/{s}/departamentos/{d}[/desactivar/reactivar] | AdminSucursalesDepartamentosGestionar | OK |
| `administracion/SucursalPuestosTab` | Asignar/Editar rol sugerido/Desactivar puesto | padre: prop canGestionar (AdminSucursalesPuestosGestionar) | POST/PATCH /admin/empresas/sucursales/{s}/puestos/{p}/... | AdminSucursalesPuestosGestionar | OK |
| `administracion/SucursalUsuariosTab` | Asignar/Desactivar usuario a sucursal | padre: prop canGestionar (AdminSucursalesUsuariosGestionar) | POST /admin/empresas/sucursales/{s}/usuarios/{u}[/desactivar/reactivar] | AdminSucursalesUsuariosGestionar | OK |
| `administracion/SucursalColaboradoresTab` | Vincular colaborador | padre: prop canGestionar (AdminEmpleadosGestionar, SucursalDetalle) | PATCH /admin/empleados/{id} | AdminEmpleadosGestionar | OK |
| `administracion/FilaAsignacionSucursal` | Desactivar/Reactivar asignacion | padre: prop canGestionar | (delegado a los Tab) | (ver Tab) | OK |
| `administracion/AuditoriaPage` | Reintentar (solo lectura) | sin mutacion | GET /admin/auditoria | AdminAuditoriaLeer (lectura) | OK |
| `catalogos/CategoriasArticuloPage` | Editar/Desactivar categoria | CompartidoCatalogosAdministrar | PATCH /catalogos/categorias-articulo/{id}; POST /catalogos/categorias-articulo/{id}/desactivar | CompartidoCatalogosAdministrar (grupo) | OK |
| `catalogos/SheetNuevaCategoriaArticulo` | Nuevo/a categoria | padre: CategoriasArticuloPage | POST /catalogos/categorias-articulo | CompartidoCatalogosAdministrar | OK |
| `catalogos/CondicionesPagoPage` | Editar/Desactivar condicion de pago | CatalogosCondicionesPagoGestionar | PATCH /catalogos/condiciones-pago/{id}; POST /catalogos/condiciones-pago/{id}/desactivar | CatalogosCondicionesPagoGestionar (grupo) | OK |
| `catalogos/SheetNuevaCondicionesPago` | Nuevo/a condicion de pago | padre: CondicionesPagoPage | POST /catalogos/condiciones-pago | CatalogosCondicionesPagoGestionar | OK |
| `catalogos/IncotermsPage` | Editar/Desactivar incoterm | CatalogosIncotermsGestionar | PATCH /catalogos/incoterms/{id}; POST /catalogos/incoterms/{id}/desactivar | CatalogosIncotermsGestionar (grupo) | OK |
| `catalogos/SheetNuevoIncoterm` | Nuevo/a incoterm | padre: IncotermsPage | POST /catalogos/incoterms | CatalogosIncotermsGestionar | OK |
| `catalogos/TransportistasPage` | Editar/Desactivar transportista | CatalogosTransportistasGestionar | PATCH /catalogos/transportistas/{id}; POST /catalogos/transportistas/{id}/desactivar | CatalogosTransportistasGestionar (grupo) | OK |
| `catalogos/SheetNuevoTransportista` | Nuevo/a transportista | padre: TransportistasPage | POST /catalogos/transportistas | CatalogosTransportistasGestionar | OK |
| `catalogos/UnidadesMedidaPage` | Editar/Desactivar unidad de medida | CatalogosUnidadesMedidaGestionar | PATCH /catalogos/unidades-medida/{id}; POST /catalogos/unidades-medida/{id}/desactivar | CatalogosUnidadesMedidaGestionar (grupo) | OK |
| `catalogos/SheetNuevaUnidadMedida` | Nuevo/a unidad de medida | padre: UnidadesMedidaPage | POST /catalogos/unidades-medida | CatalogosUnidadesMedidaGestionar | OK |
| `catalogos/UsosPrincipalesPage` | Editar/Desactivar uso principal | CompartidoCatalogosAdministrar | PATCH /catalogos/usos-principales/{id}; POST /catalogos/usos-principales/{id}/desactivar | CompartidoCatalogosAdministrar (grupo) | OK |
| `catalogos/SheetNuevoUsoPrincipal` | Nuevo/a uso principal | padre: UsosPrincipalesPage | POST /catalogos/usos-principales | CompartidoCatalogosAdministrar | OK |
| `catalogos/CatalogoEditableTable` | Desactivar fila | padre: cada *Page (ver arriba) | (delegado) | (ver *Page) | OK |
| `catalogos/InlineFormShell` | contenedor de forms inline | padre: cada *Page | (sin mutacion) | - | OK |
| `catalogos/unidad-medida-fields` | campos de form | padre: SheetNuevaUnidadMedida / UnidadesMedidaPage | (sin mutacion) | - | OK |
| `catalogos/ImpuestosPage` | Nueva referencia/Editar | CompartidoCatalogosAdministrar | POST /catalogos/impuestos; PATCH /{id} | CompartidoCatalogosAdministrar | OK |
| `catalogos/MonedasLayout` | Nueva moneda | CatalogosMonedasGestionar | POST /catalogos/monedas | CatalogosMonedasGestionar | OK |
| `catalogos/SheetNuevaMoneda` | Nueva moneda | padre: MonedasLayout | POST /catalogos/monedas | CatalogosMonedasGestionar | OK |
| `catalogos/MonedaDetalle` | Desactivar/Reactivar moneda | CatalogosMonedasGestionar | PATCH /catalogos/monedas/{id} {activa} | CatalogosMonedasGestionar | OK |
| `catalogos/MonedaDatosForm` | Editar moneda | CatalogosMonedasGestionar | PATCH /catalogos/monedas/{id} | CatalogosMonedasGestionar | OK |
| `catalogos/HistoricoTiposCambioPanel` | Registrar tipo de cambio | CatalogosTiposCambioGestionar | POST /catalogos/monedas/{id}/tipos-cambio | CatalogosTiposCambioGestionar | OK |
| `catalogos/TipoCambioInlineForm` | Registrar tipo de cambio | padre: HistoricoTiposCambioPanel | POST /catalogos/monedas/{id}/tipos-cambio | CatalogosTiposCambioGestionar | OK |
| `datos-maestros/ArticulosLayout` | Nuevo articulo | CompartidoCatalogosAdministrar | POST /catalogos/articulos | CompartidoCatalogosAdministrar | OK |
| `datos-maestros/SheetNuevoArticulo` | Nuevo articulo | padre: ArticulosLayout | POST /catalogos/articulos | CompartidoCatalogosAdministrar | OK |
| `datos-maestros/ArticuloDetalle` | Desactivar articulo | CompartidoCatalogosAdministrar | DELETE /catalogos/articulos/{id} | CompartidoCatalogosAdministrar | OK |
| `datos-maestros/ArticuloDatosForm` | Editar articulo | CompartidoCatalogosAdministrar | PATCH /catalogos/articulos/{id} | CompartidoCatalogosAdministrar | OK |
| `datos-maestros/ProveedoresLayout` | Nuevo proveedor | CompartidoCatalogosAdministrar | POST /catalogos/proveedores | CompartidoCatalogosAdministrar | OK |
| `datos-maestros/SheetNuevoProveedor` | Nuevo proveedor | padre: ProveedoresLayout | POST /catalogos/proveedores | CompartidoCatalogosAdministrar | OK |
| `datos-maestros/ProveedorDetalle` | Desactivar proveedor | CompartidoCatalogosAdministrar | DELETE /catalogos/proveedores/{id} | CompartidoCatalogosAdministrar | OK |
| `datos-maestros/ProveedorDatosForm` | Editar proveedor | CompartidoCatalogosAdministrar | PATCH /catalogos/proveedores/{id} | CompartidoCatalogosAdministrar | OK |
| `datos-maestros/ProveedorBancariosSection` | Editar datos bancarios | DatosMaestrosProveedoresBancariosEditar | PATCH /catalogos/proveedores/{id} | CompartidoCatalogosAdministrar (ruta) + BancariosEditar (handler) | permiso no coincide (la API exige CompartidoCatalogosAdministrar ademas del permiso bancario; un usuario solo con BancariosEditar ve el boton y recibe 403) |
| `datos-maestros/ClientesLayout` | Nuevo cliente / Sincronizar | DatosMaestrosClientesGestionar; DatosMaestrosClientesSincronizar | POST /datos-maestros/clientes; POST /clientes/sincronizacion/ejecuciones | ClientesGestionar; ClientesSincronizar | OK |
| `datos-maestros/SheetNuevoCliente` | Nuevo cliente | padre: ClientesLayout | POST /datos-maestros/clientes | DatosMaestrosClientesGestionar | OK |
| `datos-maestros/ClienteDetalle` | Desactivar cliente | DatosMaestrosClientesGestionar | DELETE /datos-maestros/clientes/{id} | DatosMaestrosClientesGestionar | OK |
| `datos-maestros/ClienteDatosForm` | Editar cliente (fiscal) | ClientesGestionar; ClientesFiscalEditar | PATCH /datos-maestros/clientes/{id} | DatosMaestrosClientesGestionar | OK |
| `datos-maestros/ClienteOrigenAwSection` | Reintentar sincronizacion | DatosMaestrosClientesSincronizar | POST /clientes/sincronizacion/reintentos | DatosMaestrosClientesSincronizar | OK |
| `datos-maestros/SheetSincronizacionClientes` | Sincronizar/Reintentar | padre: ClientesLayout (Sincronizar) | POST /clientes/sincronizacion/ejecuciones/reintentos | DatosMaestrosClientesSincronizar | OK |
| `datos-maestros/ErroresEjecucionSync` | lista de errores | padre: SheetSincronizacionClientes | (sin mutacion) | - | OK |
| `datos-maestros/ProductosAwLayout` | Nuevo producto / Sincronizar | DatosMaestrosProductosAwGestionar | POST /datos-maestros/productos-aw[/sincronizacion] | DatosMaestrosProductosAwGestionar | OK |
| `datos-maestros/SheetNuevoProductoAw` | Nuevo producto A+W | padre: ProductosAwLayout | POST /datos-maestros/productos-aw | DatosMaestrosProductosAwGestionar | OK |
| `datos-maestros/SheetSincronizacionProductosAw` | Sincronizar/Reintentar | padre: ProductosAwLayout | POST /productos-aw/sincronizacion[/{ref}] | DatosMaestrosProductosAwGestionar | OK |
| `datos-maestros/ProductoAwDetalle` | Desactivar producto | DatosMaestrosProductosAwGestionar | DELETE /datos-maestros/productos-aw/{id} | DatosMaestrosProductosAwGestionar | OK |
| `datos-maestros/ProductoAwDatosForm` | Editar producto | DatosMaestrosProductosAwGestionar | PATCH /datos-maestros/productos-aw/{id} | DatosMaestrosProductosAwGestionar | OK |
| `datos-maestros/ProductoAwOrigenSection` | Reintentar sincronizacion | DatosMaestrosProductosAwGestionar | POST /productos-aw/{id}/sincronizacion | DatosMaestrosProductosAwGestionar | OK |
| `identidad/RolesLayout` | Nuevo rol | IdentidadRolesCrear | POST /identidad/roles | IdentidadRolesCrear | OK |
| `identidad/SheetNuevoRol` | Nuevo rol | padre: RolesLayout | POST /identidad/roles | IdentidadRolesCrear | OK |
| `identidad/RolDatosForm` | Editar rol | IdentidadRolesEditar | PATCH /identidad/roles/{id} | IdentidadRolesEditar | OK |
| `identidad/RolDetalle` | Eliminar rol; asignar permisos | IdentidadRolesEliminar; IdentidadRolesAsignarPermisos | DELETE /identidad/roles/{id}; PUT /{id}/permisos | RolesEliminar; RolesAsignarPermisos | OK |
| `identidad/MatrizPermisos` | Guardar permisos del rol | padre: RolDetalle (IdentidadRolesAsignarPermisos) | PUT /identidad/roles/{id}/permisos | IdentidadRolesAsignarPermisos | OK |
| `identidad/GruposEntraIdPanel` | Agregar/Quitar grupo Entra ID | IdentidadRolesGruposEntraIdGestionar | POST /roles/{id}/grupos-entra-id; DELETE /roles/grupos-entra-id/{id} | IdentidadRolesGruposEntraIdGestionar | OK |
| `identidad/GrupoEntraIdInlineForm` | Agregar grupo | padre: GruposEntraIdPanel | POST /roles/{id}/grupos-entra-id | IdentidadRolesGruposEntraIdGestionar | OK |
| `identidad/UsuariosLayout` | Nuevo usuario | IdentidadUsuariosCrear; AdminEmpleadosGestionar | POST /identidad/usuarios | IdentidadUsuariosCrear | OK |
| `identidad/SheetNuevoUsuario` | Nuevo usuario / cuenta independiente | padre: UsuariosLayout | POST /identidad/usuarios | IdentidadUsuariosCrear | OK |
| `identidad/UsuarioDatosForm` | Editar usuario | IdentidadUsuariosEditar | PATCH /identidad/usuarios/{id} | IdentidadUsuariosEditar | OK |
| `identidad/UsuarioDetalle` | Desactivar/Reactivar usuario; centros de costo | IdentidadUsuariosDesactivar; reactivar: UsuariosEditar; CentrosCostoAsignacionesAdministrar | POST /identidad/usuarios/{id}/desactivar/reactivar | UsuariosDesactivar; UsuariosEditar | OK |
| `identidad/RolesPorEmpresaPanel` | Asignar/Quitar rol por empresa | IdentidadAsignacionesAdministrar | POST /usuarios/{id}/asignaciones; DELETE /usuarios/asignaciones/{id} | IdentidadAsignacionesAdministrar | OK |
| `identidad/RolesPorEmpresaInlineForm` | Asignar rol | padre: RolesPorEmpresaPanel | POST /usuarios/{id}/asignaciones | IdentidadAsignacionesAdministrar | OK |
| `identidad/PermisosPersonalizadosPanel` | Guardar/Restablecer permisos personalizados | IdentidadUsuariosGestionarPermisos | PUT/DELETE /usuarios/{id}/empresas/{e}/permisos-override | IdentidadUsuariosGestionarPermisos | OK |
| `components/erp/adjuntos/AdjuntosManager` | Subir/Eliminar adjunto | (presentacional; el gate lo decide el wrapper de cada modulo) | callbacks | (segun wrapper) | OK |
| `components/erp/collaboration/ConflictDialogProvider` | Cancelar (dialogo) | n/a | (sin mutacion propia) | - | OK |
| `components/erp/collaboration/ConflictResolutionDialog` | Cancelar (dialogo) | n/a | (sin mutacion propia) | - | OK |
| `components/erp/feedback/EmptyState` | accion generica (ejemplo "Nueva RQ" en JSDoc) | (el caller pasa la accion) | - | - | OK |
| `components/erp/feedback/ErrorState` | Reintentar (refetch) | n/a | GET | - | OK |
| `components/erp/selectors/OrdenCompraSelector` | selector (lectura) | n/a | GET | - | OK |
| `components/ui/sheet` | primitivo UI | n/a | - | - | OK |

Resumen: 90 componentes. 87 OK, 2 permiso no coincide, 1 sin gate UI, 0 sin permiso API (todo endpoint de mutacion llamado desde estos componentes tiene `RequireAuthorization` por grupo o ruta).
Notas: (a) `admin/canales-venta` reutiliza `AdminEmpresasSucursalesGestionar` (UI y API coinciden). (b) Endpoints sin permiso detectados en backend fuera de la UI inventariada: `POST /admin/series/reservar` (SeriesEndpoints), `POST /api/v1/integraciones/aw/pedidos/nudge` (anonimo, por diseno de la integracion); no los llama ningun componente de esta tabla, solo se anotan. (c) Settings genericos (`/admin/$modulo/settings`, `*SettingsEndpoints`): no inventariados por decision de alcance.

## 3. Brechas priorizadas

Prioridad 1 - Administracion / Identidad
1. `EmpleadoDetalle`: boton "Editar datos y transferir sucursal base" sin gate de componente. Mitigado por `beforeLoad` (`AdminEmpleadosGestionar`) en `/admin/empleados/$id` y por la API; aun asi conviene `useHasPermission(AdminEmpleadosGestionar)` (consistencia con `EmpleadosPage`). Bajo riesgo.
2. `EmpleadosPage` "Dar acceso": el boton exige `IdentidadUsuariosCrear`, la API de `POST /admin/colaboradores/{id}/acceso` exige ademas `IdentidadAsignacionesAdministrar` (el `EmpleadoAccesoPanel` ya exige ambos). Un usuario con solo Crear ve el boton y el panel no le deja operar. Bajo riesgo (no hay fuga, solo UX).
3. Sin otras brechas en Administracion/Identidad: todos los gates de UI coinciden con el permiso de API.

Prioridad 2 - Datos maestros / catalogos (compartidos con compras/almacen/CxP)
4. `ProveedorBancariosSection`: UI gatea con `DatosMaestrosProveedoresBancariosEditar`, la API (`PATCH /catalogos/proveedores/{id}`) exige `CompartidoCatalogosAdministrar` y valida el permiso bancario en el handler. Decidir: exigir ambos en UI o dejar solo el de API. Sin cambio de backend necesario.

Prioridad 3 - Compras / Almacen / CxP / resto (fuera del alcance `modules`+`components`, ver seccion 4)
5. Esos modulos viven en `src/features/**`, no se inventariaron fila por fila. Pendiente decidir con el supervisor si entran en B.

## 4. Nota sobre `src/features/**` (no inventariado fila por fila)

Con el mismo comando aplicado a `src/features`: 173 archivos con acciones/mutaciones, 62 con gate por hook, 111 sin hook en el propio archivo; de estos, 78 contienen un hook de mutacion (almacen 19, cxp 21, compras 12, facturacion 8, centros-costo 6, tesoreria 6, cxc 5, integraciones-fiscal 1). Muchos son sheets/dialogs gateados desde la bandeja padre; compras (`AccionesRequisicion`, `AccionesOC`, `EditorLineas`, formularios de OC) no usan el hook sino los helpers `accion*(entidad, permisos)`/`permisos` (por eso el grep por hook da falso "sin gate"). Requiere una segunda pasada para confirmar permiso UI vs API (el backend ya expone `RequireAuthorization` por endpoint en Compras/Almacen/CxP/CxC/Facturacion/Tesoreria). Lista: `comm -23` del comando sobre `src/features`.

## 5. Rutas bajo `routes/_app` sin `beforeLoad` propio (A3)

Comando: `cd frontend && grep -rL beforeLoad src/routes/_app | grep -v -e test -e '\.md'` -> 30 archivos (el plan decia 31; hoy hay 30, incluida Inicio; `adm10-review.test.ts` no existe en el arbol).

Todas dependen de la guarda de `_app` (`rutaPermitida(path, permisos)`). Resultado con permisos vacios: 29 rutas -> false (cubiertas), Inicio `/` -> true (esperado). Sin brechas de ruta.

| Ruta | Cobertura en `rutaPermitida` |
|---|---|
| `/admin/$modulo/settings` | Exige `permisoRequerido` del modulo en `adminRegistry`; modulo desconocido -> false (PR #26) |
| `/almacen`, `/cxc`, `/cxp`, `/facturacion`, `/facturacion/ayuda`, `/tesoreria` | Raiz de modulo: exige ver el modulo (alguna card visible) |
| `/centros-costo` | Card del menu |
| `/compras/ordenes`, `/compras/ordenes/$id`, `/compras/ordenes/partidas-abiertas`, `/compras/ordenes/pendientes-autorizacion` | Card `/compras/ordenes` (la mas especifica) |
| `/compras/requisiciones`, `/compras/requisiciones/$id` | Card `/compras/requisiciones` |
| `/compras/pendientes`, `/compras/pendientes/$id` | Card o raiz del modulo compras |
| `/compras/articulos/$id/historial-compras`, `/compras/trazabilidad/oc/$id` | `rutasFueraDelMenu` (ComprasOrdenesLeer) |
| `/tesoreria/conciliacion`, `/corridas`, `/cuentas`, `/depositos`, `/movimientos`, `/movimientos/$id`, `/pagos-cuenta`, `/pagos`, `/repp`, `/reportes/auxiliar-bancos`, `/reportes/flujo-efectivo` | Cards de Tesoreria / raiz del modulo |
| `/` (Inicio) | Siempre permitida (excepcion documentada) |

Pantallas que llaman APIs: la guarda de ruta es solo navegacion; la seguridad la da el backend por endpoint (C3 del plan lo cubre donde falte).

## 6. Test permanente (A4)

`frontend/src/lib/nav-rutas-sin-guarda.test.ts`: descubre con `import.meta.glob` las rutas sin `beforeLoad`, sustituye `$param` por `ejemplo`, normaliza barra final y afirma `rutaPermitida(ruta, []) === (ruta === '/')`. Una ruta nueva sin `beforeLoad` que no quede cubierta por `nav.ts` rompe el test.

## 7. Segunda pasada: src/features

> Solo lectura. Alcance: `frontend/src/features/{almacen,catalogos,centros-costo,compras,cxc,cxp,facturacion,integraciones-fiscal,tesoreria}`. `features/catalogos` no tiene `.tsx` con acciones (0 archivos). Permisos de API leidos de `backend/src/Api/Endpoints/**` (informativo, sin prefijo `PermisosCanonicos.`). Settings genericos ignorados.

### 7.1 Comando (mismo criterio que la seccion 1)

Desde `frontend/`:

```sh
PAT='\b(Nuev[oa]s?|Editar|Eliminar|Desactivar|Aprobar|Rechazar|Cancelar|Sincronizar|Reintentar)\b|[Mm]utat|\.mutate'
G='useHasPermission|useHasAnyPermission|useHasAllPermissions|RequirePermission'
D="src/features/almacen src/features/catalogos src/features/centros-costo src/features/compras src/features/cxc src/features/cxp src/features/facturacion src/features/integraciones-fiscal src/features/tesoreria"
rg -l -g '*.tsx' -g '!*.test.tsx' "$PAT" $D | sort > comp.txt      # 173 archivos
rg -l "$G" $(cat comp.txt) | sort > gated.txt                      # 62 con gate por hook en el propio archivo
comm -23 comp.txt gated.txt                                         # 111 sin hook propio
```

Los 111 se resolvieron asi: (a) mapa hook de mutacion -> endpoint (190 hooks `useMutation` en `features/*/api`), (b) importadores del componente (gate en el padre o por prop), (c) `beforeLoad` de la ruta. En compras los helpers `accion*(entidad, permisos)` (`compras/lib/acciones-disponibles.ts` y `compras/ordenes/lib/acciones-disponibles.ts`) cuentan como gate y se leyo su permiso real. No hay archivo con hook de mutacion fuera de los 173.

### 7.2 Conteo por estado (173 archivos)

| Estado | Archivos |
|---|---|
| OK | 162 (59 gate directo coincidente, 103 sin gate propio: padre/ruta/helper `accion*` coincidente o sin mutacion propia, ~33 de ellos sin hook de mutacion) |
| sin gate UI | 4 |
| permiso no coincide | 7 |
| sin permiso API | 0 |

Por modulo (total / gate propio / sin gate propio): almacen 32/12/20, centros-costo 10/1/9, compras 32/6/26, cxc 14/6/8, cxp 33/12/21, facturacion 37/18/19, integraciones-fiscal 3/2/1, tesoreria 12/5/7.

Endpoints con `RequireAuthorization()` sin policy en `Api/Endpoints` de estos modulos: `compras/requisiciones/{id}/autorizaciones`, `compras/ordenes/{id}/autorizaciones`, `.../rechazar`, `.../cancelar-con-recepciones`, `compras/ordenes` POST (solo `crear-sin-rq` en handler; la policy base `ComprasOrdenesCrear` si esta) y GET de sesiones de cajas. Todos validan permiso dentro del handler/endpoint (AutorizarNivel1/2 segun nivel o estado; cancelar con recepciones exige CancelarDoble + AutorizarNivel1 + AutorizarNivel2). Por eso 0 "sin permiso API" en mutaciones.

### 7.3 Tabla (agrupada por pantalla; hijos "padre:" montados solo desde el padre indicado)

| Componente | Accion | Permiso en UI | Endpoint | Permiso en API | Estado |
|---|---|---|---|---|---|
| `almacen/AlmacenesPage` + `AlmacenSheet` | Nuevo/Editar almacen | AlmacenAlmacenesAdministrar | POST/PATCH /almacen/almacenes | AlmacenAlmacenesAdministrar | OK |
| `almacen/SubAlmacenesPage` + `SubAlmacenSheet` | Nuevo/Editar sub-almacen | AlmacenAlmacenesAdministrar | POST/PATCH /almacen/sub-almacenes | AlmacenAlmacenesAdministrar | OK |
| `almacen/UbicacionesPage` + `UbicacionSheet` | Nueva/Editar/Desactivar/Reactivar ubicacion | AlmacenUbicacionesAdministrar | /almacen/ubicaciones[/{id}/desactivar/reactivar] | AlmacenUbicacionesAdministrar | OK |
| `almacen/AsignacionesPage` + `AsignacionSheet` | Nueva/Desasignar | AlmacenAsignacionesAdministrar | POST /almacen/asignaciones[/{id}/desasignar] | AlmacenAsignacionesAdministrar | OK |
| `almacen/ReordenPage` + `ReordenSheet` | Nuevo/Editar/Desactivar reorden | AlmacenReordenAdministrar | /almacen/reorden[/{id}[/desactivar]] | AlmacenReordenAdministrar | OK |
| `almacen/RecepcionesPage` + `NuevaRecepcionSheet`, `PackingListUpload` | Nueva recepcion (factura/packing list), subir PL | AlmacenEntradasRegistrar; "Cargar XML": CuentasPorPagarCfdisCargarManual | POST /almacen/recepciones[/packing-list[/blob]] | AlmacenEntradasRegistrar | OK |
| `almacen/SalidasPage` + `NuevaSalidaSheet`, `ValeUpload` | Nueva salida RQ / por vale | AlmacenSalidasRegistrar; AlmacenSalidasPorVale | POST /almacen/salidas[/vale[/blob]] | AlmacenSalidasRegistrar; AlmacenSalidasPorVale | OK |
| `almacen/SalidaDetallePage` + `RegularizarValeSheet` | Regularizar vale | AlmacenSalidasPorVale | POST /almacen/salidas/{id}/regularizar | AlmacenSalidasPorVale | OK |
| `almacen/InventariosPage` + `NuevoConteoSheet` | Nuevo conteo | AlmacenInventariosCrear | POST /almacen/conteos | AlmacenInventariosCrear | OK |
| `almacen/InventarioDetallePage` | Iniciar conteo / enviar a conciliacion | AlmacenInventariosCrear; AlmacenInventariosCapturar | POST /conteos/{id}/iniciar; /enviar-a-conciliacion | InventariosCrear; InventariosCapturar | OK |
| `almacen/CapturaConteoPage` | Capturar linea | ruta `$id.captura`: AlmacenInventariosCapturar | POST /conteos/{id}/lineas/{l}/capturar | AlmacenInventariosCapturar | OK |
| `almacen/AprobacionConteoPage` | Aprobar/Rechazar/Aplicar/Aprobar linea; Evaluar variaciones; Recontar | ruta `$id.aprobacion`: AlmacenInventariosAprobarNivel1 o Nivel2 o Nivel3; sin gate por boton | POST /conteos/{id}/aprobar, /rechazar, /aplicar, /lineas/{l}/aprobar-individualmente; /evaluar-variaciones, /lineas/{l}/recuento | AprobarNivel1 (aprobar/rechazar/aplicar/linea); Capturar (evaluar, recuento) | permiso no coincide |
| `almacen/CierreMesPage` | Ejecutar cierre de mes | ruta: AlmacenCierreMesEjecutar | POST /almacen/cierre-mes | AlmacenCierreMesEjecutar | OK |
| `almacen/DevolucionesPage` + `NuevaDevolucionProveedorSheet`, `AplicarDevolucionInternaSheet`, `MatRevSheet` | Iniciar dev. proveedor; interna / baja / reincorporar | AlmacenDevolucionesProveedorIniciar; AlmacenDevolucionesInternasCapturar | POST /almacen/devoluciones-proveedor; /devoluciones-internas; /mat-rev/* | ProveedorIniciar; InternasCapturar | OK |
| `almacen/DevolucionProveedorDetallePage` + `DevolucionAccionesDialogs`, `EvidenciasManager`, `EvidenciaUpload` | Solicitar/Autorizar/Rechazar/Registrar salida; evidencias | Iniciar; Autorizar; Registrar (por boton) | POST /devoluciones-proveedor/{id}/solicitar-autorizacion, /autorizar, /rechazar, /registrar-salida, /evidencias | Iniciar; Autorizar; Registrar | OK |
| `centros-costo/ConfiguracionCentrosCostoPage` + `ArbolCentrosCosto`, `Dim1/2/3Dialog`, `GrupoDimDialog`, `GruposManagerDialog`, `ConfirmarEstatusDialog` | Nuevo/Editar/Desactivar/Reactivar dim1-3 y grupos | CentrosCostoCatalogoAdministrar (prop `puedeAdministrar`) | POST/PATCH/POST desactivar/reactivar /centros-costo/dim1..3 | CentrosCostoCatalogoAdministrar | OK |
| `centros-costo/AsignacionCentrosCostoPage` + `AsignacionUsuarioPanel` | Marcar alcance de usuario | ruta `asignaciones`: CentrosCostoAsignacionesAdministrar (UsuarioDetalle ya gateado en 1a pasada) | POST /centros-costo/asignaciones/{u}/marcar | CentrosCostoAsignacionesAdministrar | OK |
| `compras/AccionesRequisicion` (+ `ModalMotivo`, `MotivoRechazoSelector`) | Transmitir/Aprobar N1-N2/Rechazar/Eliminar/Cancelar/Cerrar manual/Convertir a OC | helpers `accion*` de `compras/lib`: RequisicionesEditar / AutorizarNivel1 / AutorizarNivel2 / Rechazar / Eliminar / Cancelar / CerrarManual / OrdenesCrear | POST /compras/requisiciones/{id}/transmitir, /autorizaciones (nivel), /rechazar, /eliminar, /cancelar, /cerrar-manual | mismos; `/autorizaciones` valida AutorizarNivel1/2 en handler | OK |
| `compras/EditorLineas`, `LineaInlineForm` (RQ) | Agregar/Editar/Eliminar linea, notas | `accionAgregarLinea/EditarLinea/EliminarLinea/EditarNotasLinea` = ComprasRequisicionesEditar | POST/PATCH/DELETE /compras/requisiciones/{id}/lineas[/{l}[/notas]] | ComprasRequisicionesEditar (grupo) | OK |
| `compras/NuevaRequisicion` + `NuevaRequisicionProvider` | Nueva RQ | abridores: ComprasRequisicionesCrear (Bandeja, Layout, QuickCreate) | POST /compras/requisiciones | ComprasRequisicionesCrear | OK |
| `compras/AdminAprobadores` + `DesignarAprobadorDialog` | Designar/Revocar aprobador | ruta `admin/aprobadores`: ComprasAprobadoresAdministrar | POST/DELETE /compras/aprobadores | ComprasAprobadoresAdministrar | OK |
| `compras/ordenes/AccionesOC` (+ `ModalMotivoOC`, `ConfirmDuplicarDialog`, `DobleFirmaDialog`) | Transmitir/Aprobar N1-N2/Duplicar/Cancelar 1 firma | `accion*` OC: OrdenesCrear; AutorizarNivel1; AutorizarNivel2; OrdenesCrear; OrdenesCancelar | POST /ordenes/{id}/transmitir, /autorizaciones, /duplicar, /cancelar | Crear; AutorizarNivel1/2 (handler); Crear; Cancelar | OK |
| `compras/ordenes/AccionesOC` | Rechazar OC | `accionRechazar`: AutorizarNivel1 o AutorizarNivel2 | POST /ordenes/{id}/rechazar | N1 si estado EnAutorizacionJefeCompras, N2 si EnAutorizacionDireccion (handler) | permiso no coincide (menor) |
| `compras/ordenes/AccionesOC` + `DobleFirmaDialog` | Cancelar con doble firma | `accionCancelarDobleFirma`: ComprasOrdenesCancelarDoble | POST /ordenes/{id}/cancelar-con-recepciones | CancelarDoble + AutorizarNivel1 + AutorizarNivel2 (handler) | permiso no coincide |
| `compras/ordenes/InformacionLogisticaForm` | Editar info logistica | `accionEditarInfoLogistica`: OrdenesCrear o OrdenesLogistica | PATCH /ordenes/{id}/informacion-logistica | ComprasOrdenesLogistica | permiso no coincide |
| `compras/ordenes/InformacionImportacionForm` | Editar info importacion | `accionEditarInfoImportacion`: OrdenesCrear o OrdenesLogistica | PATCH /ordenes/{id}/informacion-importacion | ComprasOrdenesCrear | permiso no coincide |
| `compras/ordenes/TotalesFinancierosForm` | Editar totales/cabecera | `accionEditarCabecera`: OrdenesCrear | PATCH /ordenes/{id} | ComprasOrdenesCrear | OK |
| `compras/ordenes/EditorLineas`, `LineaInlineFormOc`, `SelectorRequisicionesConsolidacion` | Agregar/Editar/Eliminar linea, texto adicional | `accion*Linea*`: OrdenesCrear | POST/PATCH/DELETE /ordenes/{id}/lineas[...] | ComprasOrdenesCrear | OK |
| `compras/ordenes/AdjuntosManagerOc` | Adjuntar / Remover adjunto | `accionAdjuntarDocumento`: OrdenesAdjuntar; `accionRemoverAdjunto`: OrdenesCrear | POST /ordenes/{id}/adjuntos; DELETE .../{adjuntoId} | OrdenesAdjuntar; OrdenesCrear | OK |
| `compras/ordenes/SheetNuevaOC` + `NuevaOrdenCompraProvider`, `UploadCorreoAutorizacion` (stub sin mutacion) | Nueva OC (vacia / desde RQ); sin RQ | abridores: ComprasOrdenesCrear; sin RQ: ComprasOrdenesCrearSinRq | POST /ordenes, /ordenes/desde-requisicion, /{id}/lineas/desde-requisicion | OrdenesCrear (+ CrearSinRq en handler) | OK |
| `cxc/BandejaLineasCredito`, `DetalleLineaCredito`, `NuevaLineaCredito`(+Provider), `BloquearLineaDialog` | Nueva/Actualizar/Bloquear/Desbloquear linea | CuentasPorCobrarLineasCreditoGestionar | POST/PUT /cxc/lineas-credito[/{id}[/bloquear/desbloquear]] | LineasCreditoGestionar | OK |
| `cxc/BandejaLiberaciones` + `DecidirLiberacionSheet`, `NuevaAutorizacionDialog` | Decidir liberacion; crear/cancelar autorizacion (override) | LiberacionDecidir; LiberacionOverride | POST /liberaciones; /autorizaciones[/{id}/cancelar] | LiberacionDecidir; LiberacionOverride | OK |
| `cxc/BandejaAplicaciones`, `DetalleAplicacion`, `NuevaPropuestaAplicacion`(+Provider) | Proponer / Confirmar / Rechazar propuesta | AplicacionPagoProponer; AplicacionPagoConfirmar | POST /propuestas-aplicacion[/{id}/confirmar/rechazar] | Proponer; Confirmar | OK |
| `cxc/AlertasPage`, `CobranzaPage` + `RegistrarGestion`(+Provider) | Atender alerta / registrar gestion | CuentasPorCobrarCobranzaRegistrar | POST /alertas/{id}/atender; /cobranza | CobranzaRegistrar | OK |
| `cxp/FacturasPage`, `CfdisPage` + `CapturarFacturaSheet` | Capturar factura | FacturasCapturar | POST /cxp/facturas | FacturasCapturar | OK |
| `cxp/CfdisPage` + `CargarCfdiSheet`, `DescartarCfdiSheet`, `MarcarDuplicadoSheet` | Cargar CFDI / Descartar / Marcar duplicado | CfdisCargarManual; CfdisDescartar | POST /cxp/cfdis/cargar, /{id}/descartar, /{id}/marcar-duplicado | CfdisCargarManual; CfdisDescartar | OK |
| `cxp/FacturaDetallePage` + `CancelarFacturaSheet`, `EnviarRevisionSheet`, `LiberarRevisionSheet`, `AdjuntarEvidenciaSheet` | Autorizar/Cancelar/Enviar rev./Liberar rev./Evidencia | FacturasAutorizar/Cancelar/EnviarRevision/LiberarRevision | POST /cxp/facturas/{id}/... , /evidencias | mismos (evidencias: FacturasEnviarRevision) | OK |
| `cxp/AnticiposPage` + `NuevoAnticipoSheet` | Capturar anticipo | AnticiposCapturar | POST /cxp/anticipos | AnticiposCapturar | OK |
| `cxp/NotasCreditoPage` + `NuevaNotaCreditoSheet` | Capturar NC | NotasCreditoCapturar | POST /cxp/notas-credito | NotasCreditoCapturar | OK |
| `cxp/NuevaNotaCreditoSheet`, `cxp/NuevoAnticipoSheet`, `tesoreria/RegistrarReppSheet` | Boton "Cargar XML" -> CargarCfdiSheet | ninguno | POST /cxp/cfdis/cargar | CuentasPorPagarCfdisCargarManual | sin gate UI |
| `cxp/NotasCargoPage` + `NuevaNotaCargoSheet` | Crear/Autorizar/Aplicar nota de cargo | NotasCargoCrear/Autorizar/Aplicar | POST /cxp/notas-cargo[/{id}/autorizar/aplicar] | mismos | OK |
| `cxp/ComprobacionesPage` + `NuevaComprobacionCajaChicaSheet`, `NuevaComprobacionAduanalesSheet` | Capturar/Enviar/Autorizar N1-N2/Aplicar/Rechazar | ComprobacionesCapturar; AprobarNivel1; AprobarNivel2 | POST /cxp/comprobaciones/... | mismos (aplicar y rechazar: AprobarNivel1) | OK |
| `cxp/ViaticosPage` + `NuevaSolicitudViaticosSheet`, `CapturarComprobacionViaticosSheet` | Solicitar/Autorizar jefe/DF/Pagado/Comprobacion/Liberar | ViaticosSolicitar/AutorizarJefe/AutorizarDf/MarcarPagado/CapturarComprobacion/Liberar | POST /cxp/viaticos/... | mismos | OK |
| `cxp/ViaticosPage` (:538) | Rechazar solicitud | `puedeAutJefe` o `puedeAutDf` | POST /cxp/viaticos/{id}/rechazar | ViaticosAutorizarJefe | permiso no coincide |
| `cxp/EstadosCuentaTcPage` + `NuevoEstadoCuentaTcSheet` | Nuevo EC, subir archivo, conciliar auto., cerrar, pagado banco | TcRegistrarMovimiento; TcCerrarEstadoCuenta | POST /cxp/estados-cuenta-tc[/{id}/archivo, conciliar-automatico, cerrar, marcar-pagado-banco] | TcRegistrarMovimiento; TcCerrarEstadoCuenta | OK |
| `cxp/EstadosCuentaTcPage` (:452) | Marcar conciliado | `puedeRegistrar` (TcRegistrarMovimiento) | POST /cxp/estados-cuenta-tc/{id}/marcar-conciliado | CuentasPorPagarTcCerrarEstadoCuenta | permiso no coincide |
| `cxp/MovimientosTcPage` + `NuevoMovimientoTcSheet`, `RegistrarRefundTcSheet`, `RegistrarMovimientoEspecialSheet`, `DisputarMovimientoTcSheet` | Registrar/Refund/Especial/Disputar | TcRegistrarMovimiento; TcDisputar | POST /cxp/movimientos-tc/... | TcRegistrarMovimiento; TcDisputar | OK |
| `cxp/TarjetasPage` + `NuevaTarjetaSheet` | Nueva/Bloquear/Reactivar/Cancelar tarjeta | CuentasPorPagarTcAdministrar | POST /cxp/tarjetas[/{id}/bloquear/reactivar/cancelar] | TcAdministrar | OK |
| `cxp/PoliticasViaticosPage` + `NuevaPoliticaViaticosSheet` | Nueva politica | CatalogosPoliticasAdministrar | POST /cxp/catalogos/politicas-viaticos | CatalogosPoliticasAdministrar | OK |
| `cxp/AprobadoresLimitesPage` + `NuevoAprobadorSheet` | Nuevo/Cerrar aprobador | CatalogosAprobadoresAdministrar | POST /cxp/catalogos/aprobadores-limites[/{id}/cerrar] | CatalogosAprobadoresAdministrar | OK |
| `cxp/ReposicionesCajaPage` | Emitir/Configurar reposicion | CuentasPorPagarReposicionesAdministrar | POST /reposiciones-caja/emitir; PUT /configuracion | ReposicionesAdministrar | OK |
| `facturacion/BandejaFacturas`, `EmitirFacturaPage`, `EmitirFacturaForm`, `TabPosiciones` | Emitir factura | ruta `facturas/nueva` + boton: FacturacionFacturasEmitir | POST /facturacion/facturas | FacturasEmitir | OK |
| `facturacion/DetalleFactura` + `CancelarComprobanteForm` | Reenviar correo, pedimento, cancelar, NC bonificacion | FacturasEmitir; CancelacionesSolicitar; NotasCreditoBonificacion | POST /facturas/{id}/..., /comprobantes/{id}/cancelar, /notas-credito/bonificacion | mismos | OK |
| `facturacion/DetalleFacturaAnticipo`, `TimbradoFallidoBanner` | Cancelar; Reintentar / Descartar timbrado | CancelacionesSolicitar; ComprobantesReintentarTimbrado; ComprobantesDescartar | POST /comprobantes/{id}/cancelar, /reintentar-timbrado, /descartar | mismos | OK |
| `facturacion/BandejaPedidos`, `DetallePedido`, `NuevoPedidoManual`(+Provider) | Nuevo/Editar pedido | FacturacionPedidosCapturar | POST/PUT /pedidos-facturables | PedidosCapturar | OK |
| `facturacion/BandejaExcepciones` | Resolver excepcion | PedidosExcepcionesResolver | POST /pedidos-facturables/excepciones/{id}/resolver | PedidosExcepcionesResolver | OK |
| `facturacion/ControlAnticipos`, `NuevoAnticipo`(+Provider) | Emitir anticipo | AnticiposEmitir | POST /facturacion/anticipos | AnticiposEmitir | OK |
| `facturacion/BandejaRepp`, `NuevoRepp`(+Provider) | Emitir REPP | ReppEmitir | POST /facturacion/repp | ReppEmitir | OK |
| `facturacion/Activos` | Autorizar venta de activo | ActivosAutorizar | POST /facturacion/activos/autorizar | ActivosAutorizar | OK |
| `facturacion/BandejaCartaPorte`, `DetalleCartaPorte`, `NuevaCartaPorte`(+Provider), `CartaPorteCatalogosPage`, `OperadorInlineForm`, `VehiculoInlineForm` | Emitir CP, siguiente tramo, CRUD operador/vehiculo | CartaPorteEmitir | POST/PATCH /carta-porte[/...] | CartaPorteEmitir | OK |
| `facturacion/BandejaCajas`, `CajasLayout`, `DetalleCaja`, `NuevaCaja`(+Provider) | Nueva caja, alcances, sucursales/canales/usuarios | ruta `cajas`: FacturacionCajaAdministrar | POST/PUT /facturacion/cajas[...] | CajaAdministrar | OK |
| `facturacion/MiCaja` | Cerrar sesion; Reabrir; Cancelar cobro; Autorizar apertura | CajaLiquidar; CajaSupervisar | POST /cajas/sesiones/{id}/cierre, /reabrir; /cobros/{id}/cancelar; /cajas/{id}/autorizaciones-apertura | CajaLiquidar; CajaSupervisar | OK |
| `facturacion/MiCaja` (:238, :303, :328, :332) + `RegistrarCobroCard` | Abrir sesion; Iniciar arqueo; Liquidar ruta; Registrar movimiento (el cobro SI esta gateado) | solo ruta `facturacion/caja`: CajaOperar o CajaSupervisar o CajaLiquidar | POST /cajas/{id}/sesiones, /sesiones/{id}/arqueo, /cobros/liquidacion-ruta, /sesiones/{id}/movimientos | FacturacionCajaOperar | sin gate UI |
| `integraciones-fiscal/ConfiguracionPacForm`, `RfcReceptorList`, `SubirFielDialog` | Guardar/Probar PAC; CRUD RFC; subir FIEL | IntegracionesFiscalAdministrar | PUT/POST/PATCH/DELETE /integraciones/fiscal/... | IntegracionesFiscalAdministrar | OK |
| `tesoreria/BandejaCuentas` + `CuentaBancariaSheet` | Nueva/Editar/Activar/Desactivar cuenta | TesoreriaCuentasAdministrar | POST/PUT /tesoreria/cuentas[/{id}/activar/desactivar] | TesoreriaCuentasAdministrar | OK |
| `tesoreria/BandejaDepositos` + `ConfirmarDepositoDialog`, `MotivoDialog` | Confirmar / Rechazar deposito | DepositosConfirmar; DepositosRechazar | POST /depositos/{id}/confirmar, /rechazar | mismos | OK |
| `tesoreria/ConfirmarDepositoDialog` (:275, `setAltaRapida`) | Alta rapida de ingreso | `puedeConfirmar` (padre) | POST /tesoreria/movimientos | TesoreriaMovimientosRegistrar | permiso no coincide |
| `tesoreria/BandejaPagos` + `RegistrarPagoSheet`, `MotivoDialog` | Registrar pago; solicitar cancelacion | PagosAplicar; PasivosSolicitarCancelacion | POST /pagos; /pasivos-pendientes/{id}/solicitar-cancelacion | mismos | OK |
| `tesoreria/DetalleMovimiento` | Revertir pago | PagosRevertir | POST /pagos/{id}/revertir | PagosRevertir | OK |
| `tesoreria/BandejaPagosACuenta` + `NuevoPagoACuentaSheet`, `LigarPagoACuentaDialog` | Registrar / Ligar pago a cuenta | PagosCuentaRegistrar; PagosCuentaLigar | POST /pagos-cuenta[/{id}/ligar] | mismos | OK |
| `tesoreria/BandejaReppPendientes` + `RegistrarReppSheet` | Registrar REPP recibido | TesoreriaReppRegistrar | POST /tesoreria/repp-recibidos | TesoreriaReppRegistrar | OK |

(Filas con `NotasCreditoSheet` arriba se refiere a `NuevaNotaCreditoSheet`.)

### 7.4 Brechas priorizadas (nuevas, features)

Prioridad 1 - sin gate UI (la API ya devuelve 403; el usuario ve el boton)
1. `facturacion/pages/MiCaja.tsx:238` (Abrir sesion, `AperturaSesionCard`), `:303` (Iniciar arqueo), `:328` (Liquidar ruta) y `:332` (Registrar movimiento): la ruta `routes/_app/facturacion/caja.tsx:14-17` deja pasar con Operar OR Supervisar OR Liquidar, pero los 4 endpoints exigen `FacturacionCajaOperar`. Permiso sugerido: `PermisosCanonicos.FacturacionCajaOperar` (mismo patron ya usado en `RegistrarCobroCard.tsx:53`).
2. `cxp/components/NuevaNotaCreditoSheet.tsx:232`, `cxp/components/NuevoAnticipoSheet.tsx:169`, `tesoreria/components/RegistrarReppSheet.tsx:154`: boton "Cargar XML" abre `CargarCfdiSheet` (POST /cxp/cfdis/cargar) sin gate; `NuevaRecepcionSheet.tsx:488` si lo gatea. Permiso sugerido: `PermisosCanonicos.CuentasPorPagarCfdisCargarManual` (ocultar el boton; el flujo con CFDI ya cargado sigue funcionando).

Prioridad 2 - permiso no coincide (UI muestra algo que la API rechaza o al reves)
3. `cxp/pages/EstadosCuentaTcPage.tsx:415-452`: "Marcar conciliado" gateado con `TcRegistrarMovimiento`, API exige `CuentasPorPagarTcCerrarEstadoCuenta` (`EstadosCuentaTcEndpoints.cs:203`). Mover ese boton al gate `puedeCerrar`.
4. `cxp/pages/ViaticosPage.tsx:538`: "Rechazar" visible con AutorizarJefe OR AutorizarDf; API exige `CuentasPorPagarViaticosAutorizarJefe` (`ViaticosEndpoints.cs:185`). Usar solo `puedeAutJefe` (o decidir con Millet si DF tambien rechaza y entonces ajustar API).
5. `compras/ordenes/lib/acciones-disponibles.ts:242-252` (`accionEditarInfoLogistica`): UI Crear OR Logistica, API `ComprasOrdenesLogistica` (`OrdenesCompraEndpoints.cs:499`). Dejar solo `ComprasOrdenesLogistica`.
6. `compras/ordenes/lib/acciones-disponibles.ts:269-280` (`accionEditarInfoImportacion`): UI Crear OR Logistica, API `ComprasOrdenesCrear` (`OrdenesCompraEndpoints.cs:532`). Dejar solo `ComprasOrdenesCrear`. Nota: `accionEditarNumeroPedimentoImportacion` (:295, Crear OR Logistica vs API Logistica, `:1294`) no tiene boton en UI hoy; alinear al corregir 5-6.
7. `tesoreria/components/ConfirmarDepositoDialog.tsx:275` (alta rapida, POST /tesoreria/movimientos): el dialogo solo se abre con `DepositosConfirmar` y la alta exige `TesoreriaMovimientosRegistrar`. Ocultar la opcion "alta rapida" sin `TesoreriaMovimientosRegistrar`.
8. `almacen/pages/AprobacionConteoPage.tsx` + `routes/_app/almacen/inventarios/$id.aprobacion.tsx:17-22`: ruta acepta Nivel1 OR Nivel2 OR Nivel3 y no hay gate por boton; API: aprobar/rechazar/aplicar/aprobar-individualmente = `AlmacenInventariosAprobarNivel1`, evaluar-variaciones y recuento = `AlmacenInventariosCapturar`. Un perfil solo Nivel2/Nivel3 entra y recibe 403. Confirmar semantica de niveles con Millet antes de tocar (matriz por confirmar).
9. `compras/ordenes/lib/acciones-disponibles.ts:450-460` (`accionCancelarDobleFirma`): UI solo `ComprasOrdenesCancelarDoble`; API exige ademas AutorizarNivel1 y AutorizarNivel2 (`OrdenesCompraEndpoints.cs:~1015`). Requerir los tres en el helper.
10. `compras/ordenes/lib/acciones-disponibles.ts:386-398` (`accionRechazar` OC): UI Nivel1 OR Nivel2 sin mirar estado; API pide el nivel del estado actual. Bajo riesgo; afinar por estado (EnAutorizacionJefeCompras -> Nivel1, EnAutorizacionDireccion -> Nivel2).

Sin brechas de "sin permiso API": las mutaciones de estos modulos tienen policy o chequeo en handler. No hace falta cambio de backend para B; si se decide alinear por el lado API (puntos 4, 8), es C3.

Hermanos / declarado fuera: `ProveedorBancariosSection` ya esta en la seccion 3. Los providers de "Nueva ..." (`Nueva*Provider`) se montan en `_app.tsx` pero solo se abren desde pantallas gateadas y `QuickCreateMenu` (filtra por permiso).
