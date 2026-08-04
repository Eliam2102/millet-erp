# ADR-0041: Autorización por operación + modelo de lectura de catálogos cross-empresa (refinamiento de ADR-0007)

- **Estado**: Aceptada
- **Fecha**: 2026-06-05
- **Decisores**: Eduardo Paredes (owner), Claude (backend)
- **Etiquetas**: seguridad, autorización, compras, administración, refinamiento

> Refina (no reemplaza) [ADR-0007](./0007-autorizacion-rbac-granular.md). ADR-0007
> sigue vigente para el modelo RBAC granular (`{modulo}.{recurso}.{accion}`, roles,
> cache de permisos). Este ADR precisa **a qué granularidad se aplica un permiso
> sobre un grupo de rutas** y **cómo se autoriza la lectura de catálogos
> cross-empresa**.

## Contexto y problema

El CRUD de la asignación N:M Sucursal ↔ Departamento expuso dos defectos de
autorización que comparten una raíz: la autorización del catálogo no estaba
alineada con *quién lee* vs *quién gestiona*.

- **D1 — sobre-otorgación al operativo (auth a nivel de grupo).** El
  `.RequireAuthorization(admin.sucursales.departamentos-gestionar)` estaba en el
  **grupo** de rutas, no por ruta. El grupo mezcla un **GET** de lectura (que
  alimenta el combo de captura de Nueva Requisición y el Sheet admin) con **3
  POST** de mutación. Resultado: el GET heredaba el permiso de **gestión**. Un
  requisitante —que ya tiene `compartido.catalogos.leer` (lo exigen los
  selectores hermanos de Sucursal y Almacén del mismo formulario)— recibía
  **403** al cargar el combo, porque no tiene el permiso de gestión.

- **D2 — sub-otorgación al administrador (predicado por prefijo incompleto).**
  El permiso `admin.sucursales.departamentos-gestionar` vive bajo el prefijo
  `admin.sucursales.*`, mientras que el resto de la gestión organizacional vive
  bajo `admin.empresas.*` por legacy. El predicado del rol
  **admin-organizacional** del bootstrap solo capturaba `admin.empresas*` y
  `admin.departamentos*`, así que **no** concedía el permiso del N:M: el
  administrador de la estructura organizacional no podía gestionarla.

De fondo: un mismo grupo de rutas no debe autorizarse con un solo permiso
cuando contiene operaciones de distinta naturaleza (lectura vs mutación), y la
**lectura** de un catálogo cross-empresa no estaba modelada como tal.

## Drivers de la decisión

- Que el permiso refleje la **operación** (leer vs mutar), no la agrupación de
  rutas.
- Que la lectura de catálogos de referencia sea consistente entre selectores
  hermanos (sucursales, almacenes, departamentos) y no dependa de heredar un
  permiso de gestión.
- Que el rol que gestiona el dominio de un catálogo pueda también leerlo, sin
  proliferar permisos de lectura acotados por sub-recurso.
- Mínima superficie de cambio y cero invalidación de claims JWT en este PR.

## Opciones consideradas

1. **Autorización por operación (split por-ruta) + lectura vía `compartido.catalogos.leer`** — *elegida*.
2. Crear un permiso de lectura acotado por dominio (`admin.sucursales.departamentos-leer`) y concederlo a requisitantes y admins.
3. Política "leer **O** gestionar" (any-of) sobre el GET.

## Decisión

**(1) Autorización por operación.** Las rutas de **lectura** exigen un permiso
de **lectura**; las rutas de **mutación**, el de **gestión**. No se aplica el
permiso de gestión a nivel de grupo sobre grupos de rutas mixtas. En la práctica:
el `.RequireAuthorization` se declara **por ruta** cuando un grupo combina GET y
POST de distinta naturaleza.

**(2) Lectura de catálogos cross-empresa vía el permiso establecido.** Las
lecturas de catálogos cross-empresa (esquema `compartido`) usan el permiso ya
establecido `compartido.catalogos.leer` —el mismo de sucursales, almacenes,
departamentos, monedas, SAT, proveedores, artículos—. Los **roles admin que
gestionan el dominio de un catálogo** (p. ej. `admin-organizacional` sobre la
estructura Sucursal↔Departamento) **reciben esa lectura** por coherencia con sus
roles hermanos (`admin-catalogos`, `admin-datos-maestros`, `auditor`, que ya la
tienen), en vez de crear permisos de lectura acotados por sub-recurso.

## Consecuencias

**Positivas**

- El requisitante carga el combo de captura con el permiso de lectura que ya
  posee; deja de heredar gestión.
- El administrador organizacional gestiona el N:M (su dominio) y lo lee para el
  Sheet, alineado con sus roles hermanos.
- La autorización del grupo de rutas se vuelve legible y testeable ruta-por-ruta.

**Negativas / trade-offs**

- `compartido.catalogos.leer` es **amplio y crece** a medida que se agregan
  catálogos cross-empresa: los roles que lo reciben leerán **todo** catálogo de
  referencia futuro. **Aceptado**: son datos de referencia read-only y roles
  administrativos; la mutación sigue protegida por su permiso de gestión
  específico.
- La alternativa estricta (Opción 2, read acotado por dominio) queda **diferida**
  como follow-up si se adopta un mínimo privilegio más estricto sobre lecturas.
- El re-sync de roles del bootstrap es **aditivo** (agrega permisos faltantes,
  nunca revoca): corrige los roles existentes en el próximo arranque, pero **no
  retira** over-grants si los hubiera. Quitar un permiso requiere intervención
  explícita.
- **Diferido relacionado (PR aparte):** consolidación de la convención de
  prefijos —mover el N:M de `admin.sucursales.*` a `admin.empresas.*` y endurecer
  los `StartsWith` con punto final—. Va separado porque **renombrar el código de
  un permiso invalida los claims de los JWT emitidos hasta el re-login**.

## Descartadas

**Read acotado por dominio** (Opción 2): más granular, pero obliga a conceder un
permiso nuevo a cada requisitante y a los roles admin, con fricción operativa y
proliferación de permisos `*-leer` por sub-recurso. El permiso establecido cubre
el caso con la sensibilidad correcta (referencia read-only).

**Política "leer O gestionar" any-of sobre el GET** (Opción 3): el modelo de
autorización actual resuelve **un solo código** por policy con match exacto
(`PermissionPolicyProvider` + `PermissionAuthorizationHandler`, ADR-0007). Un
"cualquiera de N permisos" requeriría infraestructura nueva (un requirement/handler
multi-código). Innecesario: con el split por operación, el GET solo necesita el
permiso de lectura, que el admin de gestión también posee por la Decisión (2).

## Notas de implementación

- **Split (D1)**: en `SucursalDepartamentosEndpoints.cs` el grupo queda solo con
  `.WithTags`; el GET declara `.RequireAuthorization(compartido.catalogos.leer)` y
  cada POST `.RequireAuthorization(admin.sucursales.departamentos-gestionar)`.
- **Predicado del rol (D2 + lectura)**: en `BootstrapSuperAdminHostedService` el
  predicado de `admin-organizacional` agrega `StartsWith("admin.sucursales.")`
  (prefijo con punto: capta el único permiso bajo ese prefijo hoy) y, como
  `compartido.catalogos.leer` vive en `compartido.*` (no `admin.*`), se agrega
  **explícito con match exacto**.
- **Red de seguridad**: un test inspecciona la **metadata** de los endpoints y
  asserta el permiso exacto por ruta (GET=lectura, cada POST=gestión) y que
  ninguna quede anónima —complementa el test parametrizado de cobertura de
  `[RequirePermission]` de ADR-0007 (línea 164), bajando a la granularidad por
  operación—. Un test de bootstrap verifica el resultado del re-sync sobre el
  rol existente.
- **Frontend**: sin cambios. El combo del requisitante no tiene gate de permiso
  (solo dispara la query); el gate de gestión sigue aplicando para abrir el Sheet
  admin.
