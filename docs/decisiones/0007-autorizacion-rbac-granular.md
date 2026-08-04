# ADR-0007: Modelo de autorización RBAC con permisos granulares

- **Estado**: Aceptada
- **Fecha**: 2026-05-01
- **Decisores**: Eduardo Paredes
- **Etiquetas**: seguridad, autorización, fundación

## Contexto y problema

La autenticación (ADR-0003) responde "quién es el usuario". La autorización
responde "qué puede hacer". En un ERP empresarial son cosas muy distintas y
la autorización tiene complejidad real:

- Un usuario del área Comercial puede ver clientes y crear cotizaciones, pero
  no puede autorizar notas de crédito ni timbrar CFDIs
- Un supervisor puede autorizar notas de crédito hasta cierto monto
- El director financiero puede ver todos los reportes financieros pero no
  modificar catálogo de cuentas
- Hay reglas de **segregación de funciones** (quien factura no puede aplicar
  pagos en la misma factura, por control interno)
- Los permisos cambian con el tiempo y los roles del personal evolucionan;
  cambiarlos no debe requerir despliegue

Necesitamos un modelo que sea expresivo, mantenible, y que pueda evolucionar
sin tocar código.

## Drivers de la decisión

- Granularidad: permisos por recurso y acción, no solo por módulo
- Auditable: queda registro de quién hizo qué con qué permiso
- Editable por administradores sin redeploy
- Performance: la verificación de permisos en cada request debe ser barata
- Compatible con la autenticación de Entra ID (claims-based)

## Opciones consideradas

1. RBAC con permisos atómicos asignados a roles, roles asignados a usuarios
2. ABAC (Attribute-Based Access Control) puro con políticas evaluadas en runtime
3. Grupos de Entra ID = roles del ERP (delegar todo a Entra)
4. OPA (Open Policy Agent) externo
5. Solo `[Authorize]` con `Roles="Admin,User"` (lo mínimo del framework)

## Decisión

Se propone **RBAC con permisos atómicos**, modelado así:

**Permiso**: cadena con formato `{modulo}.{recurso}.{accion}`. Ejemplos:
- `fiscal.cfdi.timbrar`
- `fiscal.cfdi.cancelar`
- `cobranza.pago.aplicar`
- `cobranza.cartera.leer`
- `comercial.cotizacion.crear`
- `comercial.cotizacion.autorizar`

**Rol**: nombre simbólico que agrupa permisos. Ej: `FacturadorJunior`,
`FacturadorSenior`, `Cobrador`, `SupervisorCobranza`, `AdministradorContable`.

**Asignación**: un usuario tiene uno o más roles. El conjunto de permisos del
usuario es la unión de los permisos de sus roles.

**Restricciones de monto** (cuando aplica): se modelan como un permiso con
parámetro: `comercial.notaCredito.autorizar` con metadato `montoMaximo: 50000`.
Esto permite tener `SupervisorJunior` (hasta 50,000) y `SupervisorSenior`
(hasta 500,000) sin duplicar permisos.

**Implementación técnica**:

- **Tablas en esquema `identidad`**:
  - `usuarios` (referencia al `oid` de Entra como FK lógico, más metadatos del ERP)
  - `usuario_preferencias` (última empresa usada, configuraciones personales)
  - `roles`
  - `permisos`
  - `rol_permisos` (M:N)
  - `usuario_empresa_roles` (clave compuesta `(usuario_id, empresa_id, rol_id)`; ver ADR-0011 — un usuario puede tener roles distintos por empresa)
  - `restricciones_roles` (constraint de SoD estática: `rol_a_id`, `rol_b_id`, `tipo = 'mutuamente_excluyentes'`)

- **Estrategia híbrida de claims/cache**:
  - El JWT (token de sesión del API, no el token de Entra) contiene **solo** `userId`, `current_empresa_id`, y `roleIds[]` (los roles del usuario en la empresa actual). No contiene la lista completa de permisos
  - El claim `current_empresa_id` representa la empresa que el usuario tiene activa en la sesión (ver ADR-0011 para el flujo de selección y cambio)
  - Los permisos efectivos se resuelven **server-side** por request, desde un cache in-memory con clave `(userId, empresaId)` y **TTL de 5 minutos**
  - Cuando un admin modifica roles o permisos del usuario, se invalida explícitamente la entrada del cache para los pares afectados
  - Cuando el usuario cambia de empresa, se re-emite el JWT con el nuevo `current_empresa_id` y se carga el set de permisos correspondiente
  - Si en el futuro se escala a múltiples instancias de App Service, se migra el cache a Redis sin tocar handlers (hay una abstracción `IPermissionCache` que oculta la implementación)
  - **Razón del híbrido**: tokens pequeños, cambios surten efecto en ≤ 5 min sin esperar expiración del JWT, y no requiere infra de Redis desde el día 1

- **Flujo de emisión del JWT del API**:
  - El frontend autentica al usuario contra Entra ID y obtiene el token de Microsoft. Ese token se envía al endpoint `POST /api/auth/sesion` del backend, que lo valida contra los metadatos públicos del tenant Millet, resuelve el usuario en `identidad.usuarios`, calcula sus empresas accesibles, y emite un JWT propio firmado con clave del backend con shape compacto: `{ sub: userId, current_empresa_id, roleIds[], exp, iss: "millet-erp-api", aud: "millet-erp-api" }`
  - Ese token es el que se usa para todas las requests subsecuentes a REST y a SignalR (consistente con la aclaración de ADR-0001)
  - Al cambiar de empresa, `POST /api/auth/cambiar-empresa` re-emite el JWT con el nuevo `current_empresa_id`, validando que el usuario tenga roles en la empresa destino

- **Provisión de usuarios**:
  - **Auto-provisión silenciosa al primer login**: si Entra ID autentica al usuario y no existe registro en `usuarios`, se crea automáticamente con cero filas en `usuario_empresa_roles` (sin acceso a ninguna empresa)
  - El usuario puede autenticarse pero recibe un mensaje "no tienes acceso a ninguna empresa, contacta al administrador" hasta que se le asigne al menos un rol en al menos una empresa
  - Razón: evita el escenario operativo de "el director no puede entrar el día 1 porque TI no lo dio de alta a tiempo". La protección real son los roles asignados, no la existencia del registro

- **Bootstrap del primer admin**:
  - Variable de configuración `Auth:InitialAdminEntraOid` (un `oid` o lista de `oid`s) en `appsettings` por ambiente (almacenada en Key Vault para QA/Prod)
  - En el arranque de la app, si no existe ningún usuario con rol `SuperAdmin`, los `oid`s configurados reciben automáticamente ese rol
  - Reproducible y declarativo: cualquier ambiente nuevo se bootstrappea sin intervención manual

- **Segregación de funciones (SoD)**:
  - **SoD estática**: cubierta por esta ADR mediante la tabla `restricciones_roles`. Al asignar un rol a un usuario, el sistema valida que no entre en conflicto con roles ya asignados. Ejemplo: `Facturador` y `AplicadorDePagos` declarados como mutuamente excluyentes
  - **SoD dinámica**: NO se cubre con un sistema general; se implementa caso por caso en los handlers que la requieran. Ejemplo: el handler de "aplicar pago" verifica que el `usuarioId` actual sea distinto al `createdBy` de la factura
  - **Workflow de aprobación** (ej. "nota de crédito requiere aprobación de supervisor y, sobre $50K, también de director financiero"): **fuera del alcance de esta ADR**. Es ruteo de tareas, no autorización. Será un ADR futuro cuando se diseñe el motor de workflows.

- **Cancelación de CFDIs (caso especial)**:
  - Por las reglas del SAT post-CFDI 4.0, cancelar requiere motivo justificado y a veces aprobación. Se modela con permisos separados:
    - `fiscal.cfdi.cancelar.solicitar`
    - `fiscal.cfdi.cancelar.aprobar`
  - Misma estructura aplica a otras acciones de alto riesgo donde "solicitar" y "aprobar" son responsabilidades distintas

- **Código y convenciones**:
  - Atributo `[RequirePermission("modulo.recurso.accion")]` sobre acciones de controladores
  - Constantes de permisos por módulo en clases `XPermissions` (ej. `FiscalPermissions.CfdiTimbrar`) para evitar typos y permitir refactor seguro
  - En el frontend: hook `useHasPermission(permission)` y contexto `<PermissionsProvider>` que carga al login. La verificación final SIEMPRE la hace el backend; el frontend solo oculta UI por usabilidad.

## Consecuencias

**Positivas**
- Granularidad real: cada acción del ERP tiene su permiso explícito
- Mantenible: agregar un permiso nuevo no toca código del flujo principal, solo el atributo en la acción correspondiente
- Auditable: cada uso de un permiso queda en log estructurado (correlación con ADR-0006)
- UI consciente: los menús/botones se ocultan cuando el usuario no tiene permiso, sin pelearse con errores 403 en producción
- Editable por admins: los roles y asignaciones se administran desde una pantalla del propio ERP, sin redeploy
- Cambios de permisos surten efecto en ≤ 5 min sin necesidad de re-login (gracias al cache con TTL e invalidación explícita)
- Tokens JWT compactos: no se inflan con la lista completa de permisos
- SoD estática validada automáticamente al asignar roles (imposible asignar combinaciones prohibidas)

**Negativas**
- Más tablas y más complejidad inicial que solo `User.IsInRole("Admin")`
- Hay que disciplinarse para que cada acción nueva agregue su permiso (mitigable con linter/test que falle si un controlador no declara `[RequirePermission]` y no está en una lista blanca de endpoints públicos)
- Los administradores pueden crear configuraciones inseguras si no hay buena UI guiada (mitigable con plantillas de roles predefinidas)
- Cache in-memory por instancia: si el día de mañana hay múltiples instancias del App Service, los cambios de permisos toman hasta 5 min en propagarse a todas (aceptable para fase inicial, migrable a Redis con cero cambios en handlers)
- SoD dinámica queda dispersa en handlers (no es una ventaja conceptual); se mitiga creando un helper `ISegregationChecker` reutilizable

## Descartadas

**ABAC puro**. Más expresivo pero más complejo de razonar y de auditar. Y la
mayoría de las reglas del ERP son por rol, no por atributos contextuales.
Cuando aparecen reglas tipo "el dueño de la cotización puede modificarla", se
agregan como check explícito en el handler, no como política global.

**Grupos de Entra = roles**. Tentador pero riesgoso: los grupos los administra
TI (a veces sin contexto del ERP) y cambiar los grupos requiere coordinarse
con el equipo de TI corporativo. Mejor que el ERP tenga su propia capa de
roles, alimentada idealmente por miembros del grupo Entra (en una primera fase
manual, después automatizable).

**OPA externo**. Excelente para sistemas distribuidos heterogéneos. En un
monolito modular .NET, agrega un servicio más por evaluar, latencia, y
operación. Sobrediseño.

**Solo `[Authorize(Roles=...)]`**. Obliga a inflar los nombres de los roles
con la lista de cosas que pueden hacer, o a mantener mappings rol→acción
implícitos. Termina mal.

## Notas de implementación

- Crear esquema `identidad` con las tablas: `usuarios`, `roles`, `permisos`, `rol_permisos`, `usuario_empresa_roles`, `restricciones_roles`
- Implementar `IAuthorizationHandler` personalizado (`PermissionAuthorizationHandler`) para `PermissionRequirement`
- Crear abstracción `IPermissionCache` con implementación in-memory por defecto (`MemoryCache` de .NET); diseño que permita sustituir por Redis en el futuro sin cambiar handlers
- Definir constantes de permisos por módulo en clases estáticas tipo `FiscalPermissions`, `ComercialPermissions`, etc. — un solo lugar donde agregar permisos nuevos
- Atributo `[RequirePermission("modulo.recurso.accion")]` para usar en controladores
- Test parametrizado que recorre todos los endpoints y verifica que cada uno tenga `[RequirePermission]` o esté en lista blanca explícita de endpoints públicos
- Plantillas de roles predefinidas en seed por ambiente: `SuperAdmin`, `Administrador`, `Facturador`, `Cobrador`, `Comprador`, `Almacenista`, `Contador`, `Lectura` (read-only para auditores). El cliente las ajusta después.
- En frontend: contexto de React `PermissionsContext` y hook `useHasPermission()`. Cargar al login, refrescar si el backend devuelve un header indicando que el cache server-side cambió
- Configurar `Auth:InitialAdminEntraOid` en Key Vault de cada ambiente (dev/qa/prod) con el `oid` del usuario que será SuperAdmin inicial
- Endpoint admin: `POST /api/identidad/permisos/invalidar-cache/{usuarioId}` que el módulo admin invoca cuando cambia roles, para forzar invalidación inmediata
- Documentar en `CLAUDE.md`: convenciones de naming, dónde declarar permisos nuevos, cómo aplicar `[RequirePermission]`, y patrón estándar para validaciones de SoD dinámica
