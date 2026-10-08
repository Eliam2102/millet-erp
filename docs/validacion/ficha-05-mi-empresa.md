# Ficha 05 · Mi empresa

Implementación local en `millet_erp-codex-05`, sin commit ni push.

## Contraste con el código

- Ya existen el detalle de empresa, PATCH fiscal, permisos `admin.empresas.leer` y `admin.empresas.editar`, idempotencia y auditoría mediante `IAuditable` / `AuditSaveChangesInterceptor`.
- El domicilio estructurado existe en el dominio, pero no estaba expuesto por el DTO ni por el PATCH.
- El RFC es inmutable en `Empresa.ActualizarDatos`; se conserva como lectura.
- PAC, última prueba de conexión y vigencia del CSD ya están en `ConfiguracionPacResponse`.
- Las sucursales y sus claves A+W ya vienen en el detalle de empresa.

## Decisiones y alcance

- Ruta `/admin/mi-empresa`, tarjeta en Administración y guarda `rutaPermitida` con `admin.empresas.leer`. La edición sigue exigiendo `admin.empresas.editar` en API y formulario.
- Se usa la empresa de la sesión, sin selector, bandeja ni alta. Se conserva `/admin/empresas`.
- Se reutilizan `EmpresaDetalle`, `EmpresaDatosForm`, los hooks y los endpoints actuales. **No hay endpoint nuevo ni migración.**
- El PATCH existente ahora exige `If-Match` con la versión: 428 si falta/es inválido y 409 si quedó obsoleta. GET y PATCH devuelven ETag. Se adaptó el único consumidor de la mutación y su prueba existente.
- El formulario conserva la versión inicial mientras haya una edición pendiente, incluso si una consulta se actualiza en segundo plano. Las claves idempotentes se reutilizan para reintentos idénticos y cambian al corregir el contenido.
- El régimen fiscal usa el selector SAT existente. La lectura del catálogo, la configuración PAC y los enlaces de configuración conservan sus permisos específicos.
- El domicilio fiscal se expone en todos los DTO de empresa y se edita con las validaciones del dominio y FluentValidation.
- No se agregaron eventos. Se conserva la auditoría transaccional y el orden existente de publicación del evento de creación antes de SaveChanges.
- `EMISOR_SIN_LUGAR_EXPEDICION` dirige al usuario a Administración → Mi empresa.

## Pruebas añadidas

- Componentes: tres secciones, domicilio, estados CSD y prueba PAC, enlaces, ausencia de alta, edición de CP, bloqueo sin permisos, error/ausencia de configuración, catálogo SAT, paginación, conflicto y conservación de la versión durante una edición.
- Navegación: visibilidad de tarjeta y acceso directo con la misma regla.
- Hook PATCH: verifica `If-Match` e `Idempotency-Key`.
- Unitarias de aplicación: CP, domicilio, versión requerida/obsoleta y preservación del RFC (9 casos).
- API: CP/domicilio, ETag, 428, 409, idempotencia/replay, bitácora con actor, GET/PATCH 403 sin permiso. La empresa creada por la prueba nueva se elimina en `finally`.

## Resultados locales

- `dotnet build Millet.sln --no-restore -m:1 /nodeReuse:false`: compilación correcta, 0 advertencias y 0 errores (22 min 31.24 s). Incluye la compilación de las pruebas nuevas. Se restauró antes toda la solución desde la caché local de NuGet.
- `npx tsc --noEmit -p tsconfig.json`: aprobado.
- `npx tsc --noEmit -p tsconfig.app.json`: aprobado (comprueba además el proyecto de aplicación, pues el tsconfig raíz contiene referencias).
- `npm run -s typecheck:test`: aprobado.
- `npm run -s lint`: 0 errores, 10 advertencias en archivos ajenos al cambio.
- Pruebas específicas: 43/43 aprobadas en 5 archivos. Después del ajuste final de manejo de submit y bloqueo durante el guardado, los 12 casos de los dos componentes se repitieron y aprobaron.
- `npx vite build`: aprobado; advertencias de CSS `@import` y tamaño de chunks.
- Primer `npx vitest run`: 317 archivos aprobados / 13 fallidos; 1,917 pruebas aprobadas / 20 fallidas; 2 errores al iniciar workers. Duración: 919.51 s. Predominan tiempos de espera con alta carga de la máquina; los archivos fallidos no pertenecen a esta ficha. No se declara la suite completa en verde.
- Reglas nuevas del backend: 9/9 casos de `ActualizarEmpresaTests` aprobados por invocación directa de los métodos compilados, con sus aserciones xUnit y EF InMemory, desde un ejecutable temporal en `/private/tmp/millet-mi-empresa-reglas`. Evidencia complementaria: no equivale a la suite estándar de VSTest, que sigue bloqueada por sockets.
- Segundo pase completo, `npx vitest run --maxWorkers=2`: 330 archivos aprobados / 2 fallidos; 1,953 pruebas aprobadas / 2 fallidas (1,277.62 s). Los dos fallos son timeouts de 5 s en `EmpleadoInlineForm.test.tsx` (rol sugerido) y `SucursalPuestosTab.test.tsx` (asignación de varios departamentos), sin cambios en esos archivos.
- Repetición aislada de los dos archivos que agotaron tiempo, `npx vitest run --maxWorkers=1 src/modules/administracion/components/EmpleadoInlineForm.test.tsx src/modules/administracion/components/SucursalPuestosTab.test.tsx`: 17/17 pruebas aprobadas, 2/2 archivos (20.60 s). Esto no cambia el resultado rojo del segundo pase completo; repetirlo con carga estable antes de aceptación.
- `git diff --check`: sin incidencias.

## Validación pendiente fuera del sandbox

El sandbox no permite Docker. Claude debe ejecutar la suite completa con PostgreSQL desechable y documentar su rojo/verde:

```sh
./tools/validate-integration-isolated.sh
```

La ejecución de VSTest también requiere un socket local que este sandbox rechaza (`SocketException (1): Operation not permitted`); repetir fuera del sandbox:

```sh
cd backend
dotnet build Millet.sln
dotnet test tests/Administracion.UnitTests/Millet.Administracion.UnitTests.csproj
dotnet test tests/Compartido.UnitTests/Millet.Compartido.UnitTests.csproj
```

La comprobación visual quedó bloqueada: Vite no pudo escuchar en `127.0.0.1:5195` (`EPERM`). No se levantaron servidores en 5080 ni 5173. Verificar con una sesión real: tarjeta, edición de CP, persistencia tras recargar y asiento de bitácora; después probar el perfil sin permiso.

## Archivos cambiados

- `backend/src/Api/Endpoints/Administracion/EmpresasEndpoints.cs`
- `backend/src/Compartido/Application/Administracion/Empresas/ActualizarEmpresaCommand.cs`
- `backend/src/Compartido/Application/Administracion/Empresas/CrearEmpresaCommand.cs`
- `backend/src/Compartido/Application/Administracion/Empresas/DesactivarEmpresaCommand.cs`
- `backend/src/Compartido/Application/Administracion/Empresas/EmpresaResponse.cs`
- `backend/src/Compartido/Application/Administracion/Empresas/ListarEmpresasQuery.cs`
- `backend/src/Compartido/Application/Administracion/Empresas/ObtenerEmpresaQuery.cs`
- `backend/src/Facturacion/Domain/Comprobantes/DatosFiscalesEmisor.cs`
- `backend/tests/Api.IntegrationTests/Administracion/EmpresasEndpointsTests.cs`
- `backend/tests/Compartido.UnitTests/Administracion/ActualizarEmpresaTests.cs`
- `docs/validacion/ficha-05-mi-empresa.md`
- `frontend/src/lib/admin/use-admin-registry.test.ts`
- `frontend/src/lib/nav.ts`
- `frontend/src/modules/administracion/admin.ts`
- `frontend/src/modules/administracion/api/empresas.test.tsx`
- `frontend/src/modules/administracion/api/empresas.ts`
- `frontend/src/modules/administracion/api/types.ts`
- `frontend/src/modules/administracion/components/EmpresaDatosForm.tsx`
- `frontend/src/modules/administracion/components/EmpresaDetalle.tsx`
- `frontend/src/modules/administracion/components/MiEmpresaContenido.tsx`
- `frontend/src/modules/administracion/components/MiEmpresaPage.test.tsx`
- `frontend/src/modules/administracion/components/MiEmpresaPage.tsx`
- `frontend/src/modules/administracion/schemas/empresa.ts`
- `frontend/src/routeTree.gen.ts`
- `frontend/src/routes/_app/admin/mi-empresa.tsx`

## Commit propuesto

`feat(administracion): incorporar Mi empresa con configuración fiscal y control de concurrencia`
