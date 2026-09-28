# Handoff: Fix de Idempotencia y Puesto en Dashboard

**Fecha:** 2026-09-27
**Alcance:** ADM02 - Matriz de Permisos / Home Dashboard

## Â¿QuÃ© se resolviÃ³?

### 1. Fix de Idempotencia en Matriz de Permisos (UI)
**Problema original:** Al habilitar y guardar permisos en la pantalla de la Matriz (como SuperAdmin) e intentar desactivar un permiso en la misma sesiÃ³n sin recargar la pÃ¡gina, el API devolvÃ­a un error HTTP 422 (`IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY`).
**Causa:** React utilizaba el hook `useFormIdempotencyKey()` que generaba un UUID Ãºnico *por montura de componente*. Al reintentar guardar con un cuerpo de solicitud diferente (desmarcando un permiso), el backend detectaba que la llave de idempotencia ya se habÃ­a usado pero con datos distintos.
**SoluciÃ³n:** Se reemplazÃ³ por el hook `useBodyScopedIdempotencyKey(command)`. Este hook aplica un hash al payload, de manera que la llave de idempotencia cambia si la informaciÃ³n a enviar es diferente, resolviendo el bloqueo y permitiendo la actualizaciÃ³n de permisos fluida.

> *Nota sobre la CachÃ©:* Esto NO modifica el comportamiento de cachÃ© de JWT (ADR-0007). Si a un usuario se le revocan permisos, este no verÃ¡ los cambios reflejados hasta que inicie sesiÃ³n de nuevo o su token caduque, ya que los permisos viven en su JWT actual.

### 2. InyecciÃ³n del "Puesto" en el Dashboard Principal
**Problema original:** La pantalla de "Inicio" (`_app/index.tsx`) mostraba el Correo y el User ID, pero no mostraba el Puesto organizacional del empleado.
**Causa:** El endpoint `/api/auth/me` y `/api/auth/sesion` no recuperaban ni exponÃ­an el Puesto de la base de datos (mÃ³dulo Compartido).
**SoluciÃ³n end-to-end:**
- **Backend:** Se inyectÃ³ `CompartidoDbContext` en `AuthEndpoints.cs` (GetMe) y `LoginOrchestrator.cs` (Sesion). Ahora, durante el inicio de sesiÃ³n o la validaciÃ³n del token, el sistema hace una consulta para obtener `Puesto.Nombre` vinculado a ese `UsuarioId`.
- **Contratos:** Se extendieron `MeResponse.cs`, `LoginResponse.cs` y las interfaces de TypeScript correspondientes (`types.ts`) para incluir `puestoNombre`.
- **Frontend:** En `index.tsx`, se agregÃ³ un bloque en la cuadrÃ­cula de informaciÃ³n para renderizar el `puestoNombre` acompaÃ±ado del Ã­cono visual de un maletÃ­n (ðŸ’¼).

## Notas para PR (Pull Request)
Los cambios ya fueron validados localmente y el backend compilÃ³ sin problemas con todas las dependencias. 
**Al empujar (push) estos cambios a tu rama remota, el Pull Request actual de tu compaÃ±ero se actualizarÃ¡ automÃ¡ticamente.** Ya no necesitas crear otro PR; simplemente empuja los commits y aprueben el PR existente.


## Resumen de Pruebas (QA y Backend)

### QA (Validación Manual en UI)
1. **Idempotencia:** Se validó la Matriz de Permisos guardando, quitando y volviendo a guardar permisos en una misma sesión sin recargar. Resultado: HTTP 200 OK continuo sin colisión de llaves.
2. **Puesto en Dashboard:** Se comprobó con cuentas operativas (ej. Gerente de Almacén) y usuarios recién aprovisionados que el ícono (??) refleja el puesto de Recursos Humanos o un fallback elegante.
3. **Visibilidad Aislada (Catálogos vs Operación):** Se verificó que los perfiles operativos vean los catálogos globales pero sus transacciones y consultas (ej. Entradas/Salidas) se limiten estricta y visualmente a su sucursal de origen.

### Backend (Integration Tests & Seguridad)
La solución cumple con la cobertura automatizada de Api.IntegrationTests sobre PostgreSQL aislado:
- **PermisoFaltante403Tests.cs**: Confirma que el middleware detiene accesos no autorizados, retornando HTTP 403 ProblemDetails si un usuario carece del permiso granular exacto, evitando filtraciones desde la API.
- **EmpleadoSucursalScopeTests.cs**: Valida el SucursalScopeGuard, demostrando que un perfil sin el permiso Bypass (gestionar-todas-sucursales) es bloqueado (HTTP 403) al intentar consultar o mutar datos de una sucursal a la cual no está vinculado en la tabla UsuarioSucursal.


### 3. Badge de Superadministrador en Dashboard
**Contexto y Aprendizaje:** El usuario Superadministrador (cuenta técnica de máxima autoridad) no tiene un registro de empleado físico en el módulo Compartido/RH, por lo que su puesto resolvía como nulo.
**Solución en UI:** En \rontend/src/routes/_app/index.tsx\, en lugar de mostrar 'Sin puesto asignado', se implementó una evaluación reactiva con el hook nativo \useHasAnyPermission(['admin.sucursales.gestionar-todas-sucursales', 'identidad.roles.administrar'])\.
- Si el usuario cuenta con puesto asignado en RH, se muestra dicho puesto.
- Si no tiene puesto pero cuenta con permisos de superadmin, se renderiza un \<Badge>\ distintivo ('Superadministrador').
- Si es un usuario regular sin asignación, muestra el estado neutro ('Sin puesto asignado').
- **Arquitectura:** \useAuth()\ no retorna \permisos\ directamente para prevenir re-renders globales innecesarios; la consulta canónica de permisos en el frontend de Millet debe realizarse siempre a través de los selectores especializados como \useHasAnyPermission\ o \useHasPermission\ de \@/lib/auth/useHasPermission\.

