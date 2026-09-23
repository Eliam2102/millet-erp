# ADR-0051: Segmentación de datos por sucursal como patrón transversal (refinamiento de ADR-0011)

- **Estado**: Aceptada
- **Fecha**: 2026-09-22
- **Decisores**: Eduardo Paredes (owner), Claude (backend)
- **Etiquetas**: seguridad, autorización, administración, multi-sucursal, refinamiento

> Refina (no reemplaza) [ADR-0011](./0011-multi-empresa-empresa-id.md). ADR-0011
> sigue vigente como base técnica de aislamiento por empresa (`empresa_id` +
> global query filter) y no se descarta — el modelo ya lo soporta si Millet
> operara más de una razón social. Pero esa hipótesis quedó cerrada: **Millet
> confirmó formalmente (2026-09-22) el modelo multisucursal — Millet es la
> empresa principal y única razón social, segmentada internamente por
> sucursal**, no un grupo corporativo con varias razones sociales/RFC (ver
> negociación de alcance resuelta en la nota operativa "Cambios de alcance
> pendientes"). Este ADR formaliza como convención transversal el patrón que
> F1-ADM-01 ya implementó para Departamentos, Puestos y Usuarios de sucursal.

## Contexto y problema

Millet opera con una sola razón social (una `Empresa`) y varias sucursales
(CDMX, Mérida, Cancún, etc.). El requisito de negocio no es "aislar datos
entre razones sociales" (eso ya lo cubre ADR-0011, hoy con un solo tenant) —
es **segmentar qué usuarios pueden ver/operar los datos de qué sucursales**:

- Personal operativo de una sucursal solo debe ver/operar los datos de la(s)
  sucursal(es) donde trabaja.
- Personal corporativo (administradores, contadores a nivel Millet) necesita
  ver **todas** las sucursales, porque muchos procesos (fiscal, consolidado
  contable) son a nivel razón social, no por sucursal individual.

F1-ADM-01 ya construyó este mecanismo completo para tres sub-recursos:

- `SucursalDepartamento` / `SucursalPuesto`: catálogos maestros
  (`Departamento`, `Puesto`) asignables N:M a sucursales.
- `UsuarioSucursal`: asignación N:M de qué sucursales puede operar un
  usuario.
- `SucursalScopeGuard` (`Compartido/Application/Administracion/Abstractions`):
  al listar datos de una `SucursalId`, exige que el usuario esté asociado
  (`UsuarioSucursal` activa) **o** tenga el permiso admin de bypass del
  recurso — si no cumple ninguna, 403 `SUCURSAL_NO_ASOCIADA`.
- `IUsuarioSucursalReadPort`: puerto de lectura cross-módulo (Compartido →
  Identidad) que resuelve la asociación sin acoplar los módulos.

El problema: este patrón hoy solo está cableado para Departamentos, Puestos y
Usuarios de sucursal (los 3 sub-recursos de F1-ADM-01). Los módulos de
negocio que siguen (Compras, Almacén, Cuentas por Pagar, Facturación, etc.)
van a necesitar exactamente el mismo mecanismo para sus propias entidades
scoped por sucursal (RQs, OCs, recepciones, cajas, etc.), y sin
documentarlo como convención cada módulo lo reinventaría distinto —
el mismo tipo de inconsistencia que ya causó los defectos D1/D2 de
[ADR-0041](./0041-autorizacion-por-operacion-y-lectura-de-catalogos.md).

## Drivers de la decisión

- Las reglas de acceso reales de Millet se definen por sucursal, no por
  empresa — ADR-0011 sigue siendo la base técnica de aislamiento, pero deja
  de ser el eje de trabajo de negocio mientras exista una sola empresa.
- Necesidad de dos perfiles de usuario conviviendo con el mismo mecanismo:
  scoped-a-sucursal (la mayoría) y vista corporativa/todas-las-sucursales
  (administradores, contadores).
- Reusar el mecanismo ya construido y probado (`UsuarioSucursal` +
  `SucursalScopeGuard` + `IUsuarioSucursalReadPort`) en vez de que cada
  módulo de negocio futuro invente su propio guard o, peor, filtre por
  sucursal sin guard de autorización.
- Mínima superficie de cambio: no se toca `UsuarioEmpresaRol` ni el modelo
  de roles — el rol sigue siendo una sola asignación por usuario dentro de
  la empresa (ver "Descartadas").

## Opciones consideradas

1. Dejar el patrón implícito — cada módulo de negocio decide cómo scoped
   por sucursal cuando le toque.
2. Formalizar "Segmentación por Sucursal" como convención transversal
   documentada, reutilizando `SucursalScopeGuard` +
   `IUsuarioSucursalReadPort` y un permiso de bypass por recurso siguiendo
   el naming `{modulo}.{recurso}.{accion}` de ADR-0007/ADR-0041.
3. Mover el scoping al modelo de roles (`UsuarioSucursalRol`: un rol
   distinto por usuario y sucursal).
4. Colapsar `Empresa` a una fila única sin jerarquía (`EmpresaPadreId`),
   ya que hoy solo existe una.

## Decisión

Se adopta la **opción 2**. Se formaliza "Segmentación por Sucursal" como
patrón transversal obligatorio para toda entidad operativa de un módulo de
negocio cuyos datos deban acotarse por sucursal:

- **Dato**: la entidad expone `SucursalId` (convive con `EmpresaId` /
  `IPerteneceAEmpresa` de ADR-0011 cuando aplique; no lo reemplaza).
- **Guard de autorización**: todo endpoint "listar/consultar X de una
  sucursal" llama a `SucursalScopeGuard.VerificarAsync`, con:
  - Un permiso admin de bypass **propio del recurso**, nombrado
    `{modulo}.{recurso}.gestionar-todas-sucursales` (o
    `.leer-todas-sucursales` cuando el bypass es solo de lectura),
    siguiendo la granularidad de operación que exige ADR-0041 (no se
    hereda un permiso de gestión más amplio para un GET).
  - `IUsuarioSucursalReadPort.EstaAsociadoAsync` como fuente de "el usuario
    está asociado a esta sucursal" — el mismo puerto ya implementado, sin
    que cada módulo duplique la consulta.
- **Dos perfiles de usuario sobre el mismo mecanismo**, sin tabla ni rol
  especial adicional:
  - *Operativo*: una o más filas `UsuarioSucursal` activas, sin el permiso
    de bypass del recurso ⇒ ve/opera solo esas sucursales.
  - *Corporativo* (admin general, contador a nivel Millet): tiene el
    permiso de bypass del recurso ⇒ ve/opera todas las sucursales de la
    empresa, sin necesidad de una fila `UsuarioSucursal` por cada una.
- `UsuarioEmpresaRol` no cambia: sigue siendo un único rol por usuario
  dentro de la empresa. El rol define **qué** puede hacer (capacidad);
  `UsuarioSucursal` + el guard definen **dónde** (alcance de datos). No se
  crea `UsuarioSucursalRol`.
- `Empleado.SucursalId` no cambia: permanece 1:1 (dato de RH — "sucursal
  base" del empleado). El acceso multi-sucursal de un usuario del sistema
  es independiente de dónde trabaja como empleado; no se modela
  `EmpleadoSucursal` N:M.
- ADR-0011 se mantiene **vigente sin cambios** en su implementación técnica
  (`IPerteneceAEmpresa`, global query filter, `EmpresaPadreId`). Se aclara
  únicamente su alcance de negocio actual: es la base de aislamiento
  técnico, no el eje de trabajo activo — hoy existe una sola empresa
  operativa (Millet) y las reglas de acceso que el negocio pide son las de
  este ADR.

## Consecuencias

**Positivas**
- Cada módulo de negocio nuevo (Compras, Almacén, CxP, Facturación, ...)
  reusa un mecanismo probado en vez de reinventar su propio guard de
  sucursal — menos superficie para bugs de autorización tipo ADR-0041 D1/D2.
- Los dos perfiles de usuario (operativo vs corporativo) quedan cubiertos
  sin estructuras nuevas: solo hay que declarar el permiso de bypass del
  recurso y llamar al guard existente.
- `UsuarioEmpresaRol` y el modelo de roles no se tocan — cero migración de
  autorización existente.
- Deja constancia explícita de que ADR-0011 sigue siendo la base técnica
  correcta aunque el negocio actual no la ejercite con más de una empresa.

**Negativas**
- El scoping por sucursal **no es automático** como el global query filter
  de `IPerteneceAEmpresa` — cada módulo debe recordar declarar su permiso
  de bypass y llamar explícitamente a `SucursalScopeGuard` en cada handler
  relevante. Riesgo de omisión; se mitiga con checklist en el PR-breakdown
  de cada módulo y code review explícito sobre endpoints "listar por
  sucursal".
- Proliferan permisos de bypass "todas-sucursales" por recurso (uno por
  módulo/recurso que lo necesite) — es el mismo costo que ya aceptó
  ADR-0041 para granularidad por operación; se documenta como aceptable.

## Descartadas

**Rol distinto por sucursal (`UsuarioSucursalRol`)**. Permitiría casos como
"Admin en CDMX, solo lectura en Mérida", pero implica rediseñar el modelo de
roles, la UI de asignación, el cache de permisos y todos los guards
existentes. El owner confirmó que el caso de uso real de Millet es más
simple: un rol único por usuario y, aparte, el conjunto de sucursales donde
puede operar — cubierto por el patrón elegido sin ese costo.

**Colapsar `Empresa` a fila única sin jerarquía**. Simplificaría
ligeramente el dominio, pero `EmpresaPadreId` no genera complejidad
operativa mientras no se use (no hay UI ni flujos que lo exploten hoy) y
mantiene el sistema listo si Millet incorpora una segunda razón social. Se
prefiere no tocar código que ya funciona y está probado.

## Notas de implementación

- Actualizar [`CLAUDE.md`](../../CLAUDE.md) (sección de convenciones) para
  referenciar este ADR junto a la triada Compras↔Almacén↔CxP, como
  convención transversal a seguir desde el primer módulo de negocio que
  necesite scoping por sucursal.
- Reencuadrar las menciones a "multi-empresa" como feature activa en los
  documentos del módulo Administración (`00-levantamiento-estado-actual.md`,
  `01-diseno.md`, `02-plan-implementacion.md`, `04-cuidados-infra.md`,
  `09-go-live-checklist.md`): dejar claro que el aislamiento por empresa es
  base técnica (ADR-0011) y que el trabajo de negocio y las pruebas de
  aceptación activas son de segmentación por sucursal (este ADR).
- Pendiente por módulo, a resolver cuando cada uno implemente su primer
  endpoint "listar por sucursal": nombrar y sembrar (seed) el permiso de
  bypass `{modulo}.{recurso}.gestionar-todas-sucursales` /
  `.leer-todas-sucursales` correspondiente en `PermisosCanonicos`, siguiendo
  el mismo patrón que `admin.sucursales.*-gestionar`.
- No se crean tablas nuevas — el patrón reutiliza `UsuarioSucursal`,
  `SucursalScopeGuard` e `IUsuarioSucursalReadPort` tal como existen hoy.
