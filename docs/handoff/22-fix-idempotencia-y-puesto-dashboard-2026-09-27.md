# Handoff: Fix de Idempotencia y Puesto en Dashboard

**Fecha:** 2026-09-27
**Alcance:** ADM02 - Matriz de Permisos / Home Dashboard

## ¿Qué se resolvió?

### 1. Fix de Idempotencia en Matriz de Permisos (UI)
**Problema original:** Al habilitar y guardar permisos en la pantalla de la Matriz (como SuperAdmin) e intentar desactivar un permiso en la misma sesión sin recargar la página, el API devolvía un error HTTP 422 (`IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY`).
**Causa:** React utilizaba el hook `useFormIdempotencyKey()` que generaba un UUID único *por montura de componente*. Al reintentar guardar con un cuerpo de solicitud diferente (desmarcando un permiso), el backend detectaba que la llave de idempotencia ya se había usado pero con datos distintos.
**Solución:** Se reemplazó por el hook `useBodyScopedIdempotencyKey(command)`. Este hook aplica un hash al payload, de manera que la llave de idempotencia cambia si la información a enviar es diferente, resolviendo el bloqueo y permitiendo la actualización de permisos fluida.

> *Nota sobre la Caché:* Esto NO modifica el comportamiento de caché de JWT (ADR-0007). Si a un usuario se le revocan permisos, este no verá los cambios reflejados hasta que inicie sesión de nuevo o su token caduque, ya que los permisos viven en su JWT actual.

### 2. Inyección del "Puesto" en el Dashboard Principal
**Problema original:** La pantalla de "Inicio" (`_app/index.tsx`) mostraba el Correo y el User ID, pero no mostraba el Puesto organizacional del empleado.
**Causa:** El endpoint `/api/auth/me` y `/api/auth/sesion` no recuperaban ni exponían el Puesto de la base de datos (módulo Compartido).
**Solución end-to-end:**
- **Backend:** Se inyectó `CompartidoDbContext` en `AuthEndpoints.cs` (GetMe) y `LoginOrchestrator.cs` (Sesion). Ahora, durante el inicio de sesión o la validación del token, el sistema hace una consulta para obtener `Puesto.Nombre` vinculado a ese `UsuarioId`.
- **Contratos:** Se extendieron `MeResponse.cs`, `LoginResponse.cs` y las interfaces de TypeScript correspondientes (`types.ts`) para incluir `puestoNombre`.
- **Frontend:** En `index.tsx`, se agregó un bloque en la cuadrícula de información para renderizar el `puestoNombre` acompañado del ícono visual de un maletín (💼).

## Notas para PR (Pull Request)
Los cambios ya fueron validados localmente y el backend compiló sin problemas con todas las dependencias. 
**Al empujar (push) estos cambios a tu rama remota, el Pull Request actual de tu compañero se actualizará automáticamente.** Ya no necesitas crear otro PR; simplemente empuja los commits y aprueben el PR existente.


## Resumen de Pruebas (QA y Backend)

### QA (Validaci�n Manual en UI)
1. **Idempotencia:** Se valid� la Matriz de Permisos guardando, quitando y volviendo a guardar permisos en una misma sesi�n sin recargar. Resultado: HTTP 200 OK continuo sin colisi�n de llaves.
2. **Puesto en Dashboard:** Se comprob� con cuentas operativas (ej. Gerente de Almac�n) y usuarios reci�n aprovisionados que el �cono (??) refleja el puesto de Recursos Humanos o un fallback elegante.
3. **Visibilidad Aislada (Cat�logos vs Operaci�n):** Se verific� que los perfiles operativos vean los cat�logos globales pero sus transacciones y consultas (ej. Entradas/Salidas) se limiten estricta y visualmente a su sucursal de origen.

### Backend (Integration Tests & Seguridad)
La soluci�n cumple con la cobertura automatizada de Api.IntegrationTests sobre PostgreSQL aislado:
- **PermisoFaltante403Tests.cs**: Confirma que el middleware detiene accesos no autorizados, retornando HTTP 403 ProblemDetails si un usuario carece del permiso granular exacto, evitando filtraciones desde la API.
- **EmpleadoSucursalScopeTests.cs**: Valida el SucursalScopeGuard, demostrando que un perfil sin el permiso Bypass (gestionar-todas-sucursales) es bloqueado (HTTP 403) al intentar consultar o mutar datos de una sucursal a la cual no est� vinculado en la tabla UsuarioSucursal.

