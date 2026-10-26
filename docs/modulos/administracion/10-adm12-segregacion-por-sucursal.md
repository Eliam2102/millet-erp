# ADM-12 — Segregación de datos por sucursal: plan de cierre

> Estado: propuesta · Fecha: 2026-09-28 · Base: [ADR-0051](../../decisiones/0051-segmentacion-de-datos-por-sucursal.md)

## Regla de negocio

Millet es **una sola empresa con varias sucursales**. Un usuario solo ve y
opera los datos de las sucursales que tiene asignadas (`UsuarioSucursal`
activa). Quien necesita ver todas (corporativo: administración, contabilidad)
lo hace **solo** con el permiso explícito del recurso
(`{modulo}.{recurso}.leer-todas-sucursales` / `.gestionar-todas-sucursales`).
Cambiar un id o un filtro en la URL o en la API no debe dar acceso a otra sucursal.

La separación **por empresa** (ADR-0011, `empresa_id` + filtro global) sigue
como base técnica, pero no es el eje de negocio: hay una sola razón social. La
mención a "dos empresas" en la descripción antigua de ClickUp debe corregirse.

## Estado actual (2026-09-28)

| Pieza | Estado |
|---|---|
| `UsuarioSucursal`, `IUsuarioSucursalReadPort`, `SucursalScopeGuard` (403 `SUCURSAL_NO_ASOCIADA`) | ✅ Existe |
| Administración: departamentos, puestos, usuarios y empleados por sucursal | ✅ Protegido |
| Sidebar y URLs directas por permiso (rama `fix/navegacion-por-permisos`) | ✅ Hecho |
| Compras, Almacén, Cuentas por Pagar, Facturación | ❌ `SucursalId` solo como filtro opcional; ni listas ni detalle por id validan la asignación del usuario |
| Permisos de bypass por módulo | ❌ Solo `admin.empleados.*-todas-sucursales` |

Entidades con sucursal por módulo:

| Módulo | Entidades (campo) |
|---|---|
| Compras | `Requisicion` (`SucursalId`), `OrdenCompra` (`SucursalDestinoId`) |
| Almacén | `Almacen` (`SucursalId`) → recepciones, salidas, inventarios, devoluciones heredan por almacén |
| Cuentas por Pagar | `FacturaProveedor`, `NotaCargo`, `ComprobacionGastos`, `ReposicionCajaChica`, `ConfiguracionReposicionCaja` |
| Facturación | `PedidoFacturable`, `Comprobante`, `Caja`, `CajaSesion`, `CobroMostrador` |
| CxC, Tesorería, Centros de Costo | Sin `SucursalId` (a nivel razón social) → fuera de ADM-12 |

> **Facturación ya tiene un alcance propio** (`facturacion.usuario_alcance` +
> cajas + `facturacion.caja.leer-todas`, Decisión 12-6). Antes de tocarlo hay
> que decidir si se conserva como especialización (sucursal **y** canal) o se
> alinea a `UsuarioSucursal`. No duplicar mecanismos.

## Diseño transversal (una vez, en Compartido)

1. **Alcance del usuario** — nuevo método en `IUsuarioSucursalReadPort`:
   `SucursalesDelUsuarioAsync(usuarioId)` → `IReadOnlySet<Guid>`.
2. **`SucursalScope`** (servicio scoped en `Compartido/Application/Administracion/Abstractions`):
   - `ResolverAsync(permisoTodas)` → `Todas` (tiene el permiso) o `Solo(set)`.
   - `AplicarFiltro(IQueryable<T>, Expression<Func<T, Guid>> sucursal)` para listas.
   - `VerificarAsync(Guid sucursalId, permisoTodas)` para detalle/acciones
     (reusa `SucursalScopeGuard.VerificarAsync`).
3. **Semántica de las listas**: sin filtro → solo sus sucursales; filtro por una
   sucursal ajena → 403 `SUCURSAL_NO_ASOCIADA` (no lista vacía, para que el
   error sea visible). Detalle/acción por id de otra sucursal → **404** (no
   revelar existencia) — decisión a confirmar con el owner.
4. **Permisos** en `PermisosCanonicos` (ADR-0041, granularidad por operación):
   `compras.requisiciones.leer-todas-sucursales`, `compras.ordenes.leer-todas-sucursales`,
   `almacen.leer-todas-sucursales`, `cuentas_por_pagar.leer-todas-sucursales`,
   y sus `gestionar-todas-sucursales` donde haya escritura. Seed: el rol
   `super-admin` los recibe automáticamente (bootstrap).
5. **Eventos y workers** (triada Compras ↔ Almacén ↔ CxP): los consumidores
   de Service Bus corren como usuario de servicio → quedan fuera del scope
   (no hay usuario humano). Solo se protegen endpoints HTTP.

## Fases (un PR por fase)

| Fase | Alcance | Criterio de cierre |
|---|---|---|
| 0 | Diseño transversal (arriba) + tests unitarios de `SucursalScope` | Guard y filtro con cobertura; permisos creados |
| 1 | **Compras**: RQ (lista, detalle, acciones) y OC por `SucursalDestinoId`; bandejas de pendientes | Usuario de Mérida recibe 404 al pedir RQ/OC de otra sucursal; corporativo con permiso ve todas |
| 2 | **Almacén**: por `Almacen.SucursalId` en recepciones, salidas, devoluciones, inventarios, saldos | Idem; catálogos de almacenes filtrados |
| 3 | **Cuentas por Pagar**: facturas, notas de cargo, comprobaciones, reposiciones | Idem; reportes (antigüedad, cartera) respetan el alcance |
| 4 | **Facturación**: decidir alineación con `usuario_alcance` y aplicar | Idem; sin doble mecanismo |
| 5 | Frontend: el selector "Sucursal activa" solo ofrece las permitidas; mensajes 403/404 claros | QA manual con usuario operativo y corporativo |

## Pruebas por fase

- Integración (patrón `Api.IntegrationTests`, BD desechable con
  `tools/validate-integration-isolated.sh`): por cada recurso,
  (a) lista sin filtro solo trae sus sucursales, (b) filtro ajeno → 403,
  (c) detalle/acción de otra sucursal → 404, (d) con permiso `todas` → acceso total.
- Dos usuarios semilla: operativo (1 sucursal) y corporativo (permiso `todas`).

## Riesgos

- Reportes y totales consolidados deben usar el mismo filtro o mostrarán cifras de otras sucursales.
- Usuarios operativos sin ninguna `UsuarioSucursal` quedarán sin datos: revisar asignaciones antes del despliegue (script de verificación).
- Entidades hijas sin `SucursalId` propio (líneas, adjuntos) se protegen por su agregado padre, nunca por id directo.
