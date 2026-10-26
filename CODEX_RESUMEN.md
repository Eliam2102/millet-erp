# FMT-1a · Reanudación del 22 de octubre de 2026

## Estado del worktree y documentos

- Rama conservada: `feature/fmt1a-maestros-documentos`; HEAD `eab7096bcfcd6e85d0addec3ae2b8fb83a5d8c37`.
- Base local `origin/develop`: `5120a920013daae2590aa4bb777664c2365b3db3`, posterior a la base citada en la continuación. El avance parcial está en `abe19d27`; HEAD ya integra develop.
- Se leyeron completos, en orden, `01_Continuacion_22oct.md` (incluida la corrección final), `00_Reglas_comunes.md` y FMT-1a, además del resumen anterior y las guías del repositorio. La corrección del anticipo corresponde a FMT-3; no se cambió Facturación.
- Al iniciar esta reanudación ya estaban modificadas las pruebas `EmpresaLogoTests.cs`, `LogoEmpresaTests.cs` y `EmpresaLogoSection.test.tsx`, además del resumen sin seguimiento. Se conservaron y revisaron contra el código. No había archivos en conflicto ni integración pendiente.
- No se hizo commit, push, cambio de rama ni publicación. No se aplicó ninguna migración a una base de datos.

## Implementación contrastada con la ficha

- **Empresa:** referencia/tipo del logo, fecha y usuario; asignar, reemplazar y quitar. Se usa `IBlobStoragePort`, rutas `empresas/{id}/logo/{guid}.{ext}` y conservación de blobs históricos. Los endpoints GET/PUT/DELETE usan los permisos existentes de lectura/edición de empresa; las mutaciones exigen idempotencia. Se valida firma PNG/JPEG, máximo 1 MiB y ancho mínimo de 200 px.
- **Proveedor:** domicilio opcional con los campos y longitudes de Sucursal; país de tres letras normalizado a mayúsculas y MEX por omisión al clasificar como nacional. Tax ID opcional de hasta 40 caracteres, exclusivo de extranjeros. Alta, edición, detalle, contratos y formularios conectados.
- **Empleado:** teléfono opcional de hasta 20 caracteres y extensión de hasta 10, con validación de dominio y comandos. El teléfono admite dígitos ASCII, espacios, paréntesis, guiones y +. Captura de contacto en edición y presentación en detalle.
- **UI:** sección compartida de logo en Mi empresa y detalle de empresa, vista previa, acciones condicionadas por permiso y confirmación para quitar. Tax ID visible sólo para extranjeros.

## Cambios de esta continuación, por archivo

- `backend/tests/Api.UnitTests/EmpresaLogoTests.cs`: se conservó la cobertura adicional de JPEG normal/progresivo detectado por bytes pese a nombre/MIME PNG, y rechazo de cabeceras JPEG truncadas.
- `backend/tests/Compartido.UnitTests/Administracion/LogoEmpresaTests.cs`: se conservó la prueba con el interceptor real de auditoría (tres cambios de LogoBlobRef, con actor y conservación de blobs). Se corrigieron cuatro llamadas ambiguas de FluentValidation usando explícitamente `SubirLogoEmpresaCommand`.
- `backend/tests/Compartido.UnitTests/FmtProveedorTests.cs`: se corrigieron otras cuatro llamadas ambiguas de FluentValidation indicando el tipo de comando; no cambió la regla validada.
- `frontend/src/modules/administracion/components/EmpresaLogoSection.test.tsx`: flujo de subida, vista previa, retirada confirmada y liberación de URL. Se verifica el FormData y la clave de idempotencia en el límite de `apiRequest`, y la consulta/refresco con MSW. Se restauran los spies después de cada prueba. La serialización multipart de jsdom con fetch de Node convertía el File en un blob distinto; no se alteró el código productivo para adaptarlo al simulador.
- `frontend/src/modules/administracion/components/EmpleadoInlineForm.tsx`: se retiró la refactorización previa a `useWatch`, ajena a FMT-1a, y se restauró el patrón de `form.watch` de la base. Esa refactorización activaba tres errores de efectos existentes en el componente. Se conservan teléfono/extensión, valores iniciales, restablecimiento y PATCH.
- `backend/src/Compartido/Infrastructure/Migrations/Compartido/20261022223516_FmtLogoProveedorEmpleado.cs` y `.Designer.cs`: migración regenerada por EF para los 16 campos opcionales de empresa (4), proveedor (10) y empleado (2).
- `backend/src/Compartido/Infrastructure/Migrations/Compartido/CompartidoDbContextModelSnapshot.cs`: actualizado exclusivamente por EF; diff de 78 líneas agregadas. No se editó a mano.
- `CODEX_RESUMEN.md`: reescrito con el estado y la evidencia de esta reanudación.

## Decisiones y compatibilidad

- Se reutiliza la auditoría transversal: Empresa implementa `IAuditable` y el API registra `AuditSaveChangesInterceptor` para Compartido; no se agregó una bitácora paralela.
- No se encontró un value object de domicilio reutilizable en SharedKernel/Administración; se conserva la forma de Sucursal.
- En los campos opcionales nuevos de PATCH, null conserva y cadena vacía borra; el frontend envía cadenas vacías al limpiar. Los contratos previos se mantienen mediante parámetros opcionales.
- El seed `DemoSesionMaestros.cs` crea `MILLET INDUSTRIA DE VIDRIO (DEMO)`, RFC `MID010101AAA`, no M INDUSTRIA / MIN120224147. La condición literal para sembrar el logo no se cumple: no se cambió la identidad ni se atribuyó el logo real a esa empresa ficticia; no se copió el recurso.
- La validación del logo comprueba firma/cabecera y dimensiones; no realiza una decodificación completa de la imagen.

## Vínculo usuario–empleado para FMT-2

Existe `Administracion.Domain.Empleado.UsuarioId`, opcional y sin FK entre módulos. Para resolver `CompradorTitularId` (usuario), el consumidor debe declarar su puerto de lectura y buscar, mediante su adaptador, el empleado con ese UsuarioId y la empresa correspondiente, y leer Telefono/Extension. Compartido declara índice único filtrado de UsuarioId (`usuario_id IS NOT NULL`): un usuario no puede vincularse a dos empleados. No se creó un vínculo nuevo ni se modificó Compras. La asignación real y sus datos de contacto deben comprobarse en el entorno de demostración.

## Migración y revisión de esquema

Se ejecutó:

```sh
cd backend
dotnet ef migrations add FmtLogoProveedorEmpleado --project src/Compartido/Millet.Compartido.csproj --startup-project /tmp/fmt1a-ef/FmtEf.csproj --context CompartidoDbContext --output-dir Infrastructure/Migrations/Compartido --no-build
```

Se usó un host temporal fuera del repositorio, con Compartido compilado y la misma configuración de `CompartidoDesignTimeFactory` del API (PostgreSQL y nombres snake_case). El primer build del API estaba bloqueado por recursos/restauración de la base, incluida la falta de assets de Activos Fijos. El host temporal usa las mismas versiones de EF, proveedor Npgsql y NamingConventions del repositorio; no cambia el modelo ni agrega dependencias al producto.

Después se recompiló Compartido con la migración y se verificó (también con el factory real del API una vez compilado):

- `dotnet ef migrations has-pending-model-changes ... --no-build`: **sin cambios pendientes**.
- `dotnet ef migrations script ClienteUnificacionRfc FmtLogoProveedorEmpleado ... --no-build`: **correcto**. SQL revisado: sólo 16 `ALTER TABLE ... ADD`, registro en historial y transacción; ninguna modificación de datos ni eliminación en Up.
- Down elimina únicamente las 16 columnas agregadas; como cualquier reversa de columnas, perdería los valores que se hubieran capturado en ellas.
- Generación y script no equivalen a aplicación en PostgreSQL; aplicación/reversa real: **Por confirmar**.

## Verificación de esta reanudación

| Verificación | Resultado |
|---|---|
| Compartido con `-warnaserror --no-restore -m:1 -nr:false -p:UseSharedCompilation=false`, incluida la migración | Correcto: 0 advertencias, 0 errores |
| Build de Compartido.UnitTests con las mismas opciones | Correcto: 0 advertencias, 0 errores |
| `npx tsc -b tsconfig.app.json tsconfig.node.json`, y repetición tras restaurar form.watch | Correcto, código 0 |
| `eslint src`, repetido tras restaurar form.watch | Código 0: 0 errores, 1 advertencia `react-hooks/incompatible-library` por el patrón heredado form.watch |
| `vitest run src/modules/administracion src/modules/datos-maestros --pool=threads --maxWorkers=2` | 85 archivos, 366 pruebas aprobadas |
| Repetición sobre el formulario/detalle/listado de empleados tras restaurar form.watch | 3 archivos, 22 pruebas aprobadas |
| Pruebas específicas de logo y Tax ID en frontend | 2 archivos, 6 pruebas aprobadas |
| Ejecución directa de pruebas xUnit de FMT-1a en host temporal sin VSTest | 15 casos aprobados: dominio, validadores, blobs históricos, auditoría con el interceptor real y lector multipart del endpoint (PNG/JPEG, firma, tamaño y ancho mínimo) |
| VSTest de Administración, Compartido y API | Anulados por `SocketException (13): Permission denied` al abrir el socket local del runner; no se cuenta como suite aprobada |
| `dotnet build Millet.sln -warnaserror` | Correcto tras restaurar paquetes: 0 advertencias, 0 errores, incluidos los proyectos de pruebas; opciones `--no-restore -m:1 -nr:false -p:UseSharedCompilation=false` |
| `git diff --check` | Correcto |

La primera compilación sin restaurar encontró `NETSDK1004` en Activos Fijos y conflictos `MSB3277` de System.Memory.Data; también hubo reintentos por archivos de salida ocupados. El reintento final compiló correctamente toda la solución. La restauración final se ejecutó con fuente local de NuGet, `-m:1 -nr:false`, sin cambiar versiones ni archivos de configuración. El host tuvo carga superior a 200 durante parte de la corrida. Se limitaron workers de Vitest y se deshabilitó el compilador compartido de .NET para los reintentos. Estas incidencias no se ocultan como resultados verdes.

Los 15 casos directos llaman a los métodos reales de las cuatro clases de pruebas FMT-1a compiladas, incluyendo todos sus InlineData; las aserciones son las de xUnit. El host temporal está en `/tmp/fmt1a-check` y el resultado en `/tmp/fmt1a-direct-tests-final.log`. Esto no sustituye la ejecución completa de las suites por VSTest.

## Pendiente, fuera de alcance y siguiente acción

- Suites completas de backend por VSTest y pruebas integrales con PostgreSQL/Docker: **Por confirmar**; el sandbox impide el socket del runner y no se ejecutó Docker.
- Subir, reemplazar, consultar y quitar un logo desde un navegador conectado al API, y comprobar la bitácora en ese entorno: **Por confirmar**. Los tests simulados y EF InMemory no acreditan E2E ni aceptación de Millet.
- No se cambió Compras, Facturación, Activos Fijos, generadores de PDF, datos reales ni infraestructura; no se publicaron documentos ni se modificaron servicios externos.
- Siguiente acción recomendada: revisión del diff por Claude, aplicación/reversa de la migración en PostgreSQL desechable, suites integrales y revisión visual con permisos antes de integrar. No hacer commit sin autorización.

## Archivos de la ficha heredados del avance previo

- `backend/src/Administracion/Domain/Empleado.cs` — Campos opcionales y reglas de dominio del maestro.
- `backend/src/Administracion/Domain/Empresa.cs` — Campos opcionales y reglas de dominio del maestro.
- `backend/src/Api/Endpoints/Administracion/EmpleadosEndpoints.cs` — Rutas y contratos HTTP conectados a los campos nuevos.
- `backend/src/Api/Endpoints/Administracion/EmpresasEndpoints.cs` — Rutas y contratos HTTP conectados a los campos nuevos.
- `backend/src/Api/Endpoints/Catalogos/CatalogosEndpoints.cs` — Rutas y contratos HTTP conectados a los campos nuevos.
- `backend/src/Api/Endpoints/Catalogos/OrganizacionEndpoints.cs` — Rutas y contratos HTTP conectados a los campos nuevos.
- `backend/src/Api/Endpoints/DatosMaestros/DatosMaestrosEndpoints.cs` — Rutas y contratos HTTP conectados a los campos nuevos.
- `backend/src/Compartido/Application/Administracion/Empleados/ActualizarEmpleadoCommand.cs` — Comandos, validadores, mapeo de respuestas y persistencia de los campos nuevos.
- `backend/src/Compartido/Application/Administracion/Empleados/CrearEmpleadoCommand.cs` — Comandos, validadores, mapeo de respuestas y persistencia de los campos nuevos.
- `backend/src/Compartido/Application/Administracion/Empleados/EmpleadoResponse.cs` — Comandos, validadores, mapeo de respuestas y persistencia de los campos nuevos.
- `backend/src/Compartido/Application/Administracion/Empresas/LogoEmpresaCommands.cs` — Validación de firma/dimensiones y casos de uso de almacenamiento, lectura y retirada.
- `backend/src/Compartido/Application/Catalogos/ActualizarProveedorCommand.cs` — Comandos, validadores, mapeo de respuestas y persistencia de los campos nuevos.
- `backend/src/Compartido/Application/Catalogos/CrearProveedorCommand.cs` — Comandos, validadores, mapeo de respuestas y persistencia de los campos nuevos.
- `backend/src/Compartido/Infrastructure/Persistence/CompartidoDbContext.cs` — Mapeo persistente y longitudes de empresa, proveedor y empleado.
- `backend/src/DatosMaestros/Domain/Proveedor.cs` — Campos opcionales y reglas de dominio del maestro.
- `backend/tests/Administracion.UnitTests/Domain/FmtDocumentosTests.cs` — Campos opcionales y reglas de dominio del maestro.
- `backend/tests/Api.UnitTests/EmpresaLogoTests.cs` — Pruebas de dominio, validación, permisos o presentación de los campos de esta ficha.
- `backend/tests/Compartido.UnitTests/Administracion/LogoEmpresaTests.cs` — Pruebas de dominio, validación, permisos o presentación de los campos de esta ficha.
- `backend/tests/Compartido.UnitTests/FmtProveedorTests.cs` — Pruebas de dominio, validación, permisos o presentación de los campos de esta ficha.
- `frontend/src/features/catalogos/api/types.ts` — Contratos TypeScript compatibles con los datos del backend.
- `frontend/src/modules/administracion/api/types.ts` — Contratos TypeScript compatibles con los datos del backend.
- `frontend/src/modules/administracion/components/EmpleadoDetalle.tsx` — Captura o presentación de domicilio/Tax ID o teléfono/extensión según el maestro.
- `frontend/src/modules/administracion/components/EmpleadoInlineForm.tsx` — Captura o presentación de domicilio/Tax ID o teléfono/extensión según el maestro.
- `frontend/src/modules/administracion/components/EmpresaDetalle.tsx` — Incluye la sección compartida del logo.
- `frontend/src/modules/administracion/components/EmpresaLogoSection.test.tsx` — Pruebas de dominio, validación, permisos o presentación de los campos de esta ficha.
- `frontend/src/modules/administracion/components/EmpresaLogoSection.tsx` — Carga multipart, vista previa, permisos y confirmación para quitar.
- `frontend/src/modules/administracion/components/MiEmpresaContenido.tsx` — Incluye la sección compartida del logo.
- `frontend/src/modules/administracion/schemas/empleado.ts` — Validación de captura y tipos inferidos para los campos nuevos.
- `frontend/src/modules/datos-maestros/api/types.ts` — Contratos TypeScript compatibles con los datos del backend.
- `frontend/src/modules/datos-maestros/components/ProveedorDatosForm.tsx` — Captura o presentación de domicilio/Tax ID o teléfono/extensión según el maestro.
- `frontend/src/modules/datos-maestros/components/ProveedorDocumentos.test.tsx` — Pruebas de dominio, validación, permisos o presentación de los campos de esta ficha.
- `frontend/src/modules/datos-maestros/components/SheetNuevoProveedor.tsx` — Captura o presentación de domicilio/Tax ID o teléfono/extensión según el maestro.
- `frontend/src/modules/datos-maestros/schemas/proveedor.ts` — Validación de captura y tipos inferidos para los campos nuevos.

## Correcciones del 22-oct

### Alcance y cambios

Se leyeron completos `02_Correcciones_revision_22oct.md`, `00_Reglas_comunes.md` y `01_Continuacion_22oct.md`. Se aplicó exclusivamente FMT-1a en la rama existente. Se conservaron los cambios que ya tenía el worktree; esta revisión no modifica la migración ni el snapshot existentes y no requiere cambios de esquema.

- `backend/src/Api/Endpoints/Administracion/EmpresasEndpoints.cs`: PUT con `RequestSizeLimitAttribute` de 1 MiB + 64 KiB para el cuerpo y `FormOptions.MultipartBodyLengthLimit` de 1 MiB por archivo. Rechazo anticipado por Content-Length y límite del servidor cuando su feature permite establecerlo. Los excesos del lector multipart se traducen a `EMPRESA_LOGO_TAMANO`, y los formularios malformados a `EMPRESA_LOGO_FORMATO`. GET agrega `X-Content-Type-Options: nosniff`. Se retiró la validación de formato del lector HTTP.
- `backend/src/Api/Web/IdempotencyMiddleware.cs`: el hash se ejecuta antes del endpoint; ahora respeta `IRequestSizeLimitMetadata` para no leer completo un cuerpo que exceda el límite de la ruta, también cuando no hay Content-Length. Conserva el límite global como máximo.
- `backend/src/Api/Web/GlobalExceptionHandler.cs`: un rechazo HTTP 413 de la ruta nombrada `SubirLogoEmpresa` devuelve `EMPRESA_LOGO_TAMANO`; las demás rutas conservan sus códigos de error.
- `backend/src/Compartido/Application/Administracion/Empresas/LogoEmpresaCommands.cs`: la validación de formato queda únicamente en el handler. Se elimina el `Custom` del validador; éste conserva Id requerido y contenido no nulo con código de logo. Se aceptan los SOF JPEG C0–CF excepto C4, C8 y CC.
- `frontend/src/modules/administracion/components/EmpresaLogoSection.tsx` y `api/keys.ts`: confirmación de retirada con `AlertDialog` y `ConfirmacionContent`, fallback «No se pudo guardar el logo. Intenta de nuevo.» cuando falta detalle, clave `adminKeys.logoEmpresa(id)` y ayuda terminada en punto.
- `frontend/src/modules/administracion/components/EmpleadoInlineForm.tsx`: teléfono y extensión se colocan después del correo corporativo, con `Field`, labels asociados por `htmlFor`, error visible en ambos campos y `aria-invalid` para activar el estilo del Input compartido. Se conserva el patrón heredado `form.watch` que ya estaba en el worktree.
- `backend/src/DatosMaestros/Domain/Proveedor.cs` y `backend/src/Compartido/Application/Catalogos/Proveedores/ActualizarClasificacionFiscalProveedorCommand.cs`: al dejar de ser extranjero se limpia el Tax ID y el registro explícito de clasificación incluye su valor antes/después. No se exige una limpieza manual previa al cambio a nacional.
- `backend/src/Compartido/Application/Catalogos/CrearProveedorCommand.cs`, `ActualizarProveedorCommand.cs` y el dominio de proveedor: longitud de Tax ID mayor a 40 con código propio `PROVEEDOR_TAXID_INVALIDO`.
- Se restauraron los saltos de línea ajenos a la ficha frente a `origin/develop` en `frontend/src/modules/administracion/api/types.ts`, `frontend/src/modules/datos-maestros/api/types.ts`, `frontend/src/features/catalogos/api/types.ts` y `frontend/src/modules/administracion/schemas/empleado.ts`. La ruta real de Catálogos es `features/catalogos`, no `modules/catalogos`. En esos cuatro archivos quedan solamente los campos nuevos (42 líneas agregadas, ninguna eliminada frente a la base).

### Pruebas y resultados de esta revisión

Pruebas actualizadas/agregadas en `backend/tests/Api.UnitTests/EmpresaLogoTests.cs`, `backend/tests/Compartido.UnitTests/Administracion/LogoEmpresaTests.cs`, `backend/tests/Compartido.UnitTests/FmtProveedorTests.cs`, `frontend/src/modules/administracion/components/EmpresaLogoSection.test.tsx` y `EmpleadoInlineForm.test.tsx`.

La cobertura incluye: límite declarado de la ruta, GET real del delegate con nosniff y no-store, multipart real excesivo con/sin Content-Length sin lectura completa, límite antes del hash de idempotencia y respuesta Problem Details con código de logo, los 13 SOF JPEG admitidos y los tres marcadores excluidos, rechazo de formato/tamaño en el handler, auditoría real de limpieza del Tax ID sobre EF InMemory, código propio de longitud en alta/edición/dominio, confirmación/cancelación Radix, fallback sin detalle y errores/orden de los campos de contacto.

| Verificación | Resultado |
|---|---|
| `dotnet build tests/Compartido.UnitTests/Millet.Compartido.UnitTests.csproj --no-restore -m:2 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false -warnaserror` | Correcto; 0 advertencias y 0 errores |
| `dotnet build tests/Api.UnitTests/Millet.Api.UnitTests.csproj --no-restore -m:2 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false -warnaserror` | Correcto; 0 advertencias y 0 errores; recompilación posterior de los tests con `-p:BuildProjectReferences=false`, también correcta |
| `dotnet test` de Compartido y API, `--no-build --no-restore -m:2`, con filtros de logo, FMT proveedor, Proveedores e idempotencia | Anulados por `SocketException (13): Permission denied` del socket local de VSTest; no se cuentan como suites aprobadas |
| Ejecución directa de los métodos xUnit compilados de `EmpresaLogoTests`, `P9IdempotencyTests`, `LogoEmpresaTests`, `FmtProveedorTests` y las clases de `Compartido.UnitTests.Proveedores` | **56 casos aprobados, 0 fallidos**, incluidos todos sus InlineData; mismas aserciones xUnit, sin VSTest |
| `npx vitest run src/modules/administracion src/modules/datos-maestros --pool=threads --maxWorkers=2` | **85 archivos, 367 pruebas aprobadas** |
| Ejecución enfocada adicional de `EmpresaLogoSection.test.tsx` y `EmpleadoInlineForm.test.tsx`, dos workers | **2 archivos, 17 pruebas aprobadas** |
| `npx tsc -b tsconfig.app.json tsconfig.node.json` | Correcto, código 0 |
| ESLint de los dos módulos tocados y `features/catalogos/api/types.ts` | Código 0; 0 errores y 1 advertencia heredada `react-hooks/incompatible-library` por `form.watch` |
| `git diff --check` | Correcto |

Los primeros intentos de MSBuild sin `BuildInParallel=false` quedaron esperando los nodos de compilación en este sandbox; se solicitaron sus cancelaciones. Los builds verificados conservaron `-m:2`, desactivaron reutilización/compilador compartido y recorrieron las dependencias secuencialmente. No se cambió configuración del producto para resolverlo. El host temporal `/tmp/fmt1a-correcciones-check` se compiló con `-m:2`; sus fuentes y binarios no forman parte del repositorio. Evidencia de ejecución en `/tmp/fmt1a-correcciones-direct-tests.log`; frontend en `/tmp/fmt1a-correcciones-vitest.log`. La prueba HTTP usa las rutas y sus delegates reales con servicios de prueba, sin iniciar servidor ni abrir sockets.

### Pendiente y siguiente acción

- VSTest convencional, integración con PostgreSQL y revisión visual en navegador conectado al API: **Por confirmar**. EF InMemory, MSW y los delegates HTTP no acreditan integración ni E2E.
- No se ejecutó la suite completa ni se compiló nuevamente `Millet.sln`: se limitaron builds/pruebas a los proyectos y módulos afectados, como pide la revisión. No se aplicó una migración a ninguna base de datos.
- No se modificó FMT-1b, FMT-3, Activos Fijos, infraestructura ni servicios externos. No se hizo `git add`, commit, push ni cambio de rama; `CODEX_RESUMEN.md` queda como documento de revisión local, fuera del producto.
- Siguiente acción recomendada: Claude revisa el diff de FMT-1a y corre VSTest/suite completa e integración fuera de las restricciones del sandbox antes de integrar.
