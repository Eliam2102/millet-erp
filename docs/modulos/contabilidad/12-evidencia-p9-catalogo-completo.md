# P9 · Catálogo de cuentas completo · evidencia de construcción

Fecha: 09-oct-2026. Rama: `fix/P9-catalogo-cuentas`. Worktree: `millet_erp-P9-CON`.
No se hicieron commits ni push. No se modificaron `infra/` ni `.env*` ni se levantaron servidores.

## Continuación · adenda 2 del 09-oct-2026 · verificación vigente

Se conservó el trabajo anterior. La segunda integración reportada por Eliam/Claude (API 868/870) es evidencia comunicada, no una ejecución de este sandbox. Esta sección sustituye los resultados locales anteriores como corte vigente.

**Replay diferente:** `IdempotencyMiddleware` captura los bytes UTF-8, pero `IdempotencyKeyConfiguration` guardaba `ResponseBody` como `jsonb`. PostgreSQL normaliza el JSON antes de devolverlo; el middleware repetía ese texto normalizado. Se verificó también el código de `main`: usa la misma columna `jsonb` y el mismo replay, por lo que la causa era transversal y previa a P9. No se relajó la igualdad: `CatalogoHttpTests` ahora compara explícitamente los bytes y conserva sus comprobaciones de solicitud única, catálogo sin alta y Location.

La migración **`20261009171652_P9RespuestaIdempotenteExacta`**, de `CoreDbContext`, cambia solo `core.idempotency_keys.response_body` a `text`; los headers continúan en `jsonb`. El SQL generado es `ALTER TABLE core.idempotency_keys ALTER COLUMN response_body TYPE text`. No se borran claves ni se vuelven a ejecutar operaciones. Las entradas históricas ya normalizadas se conservan; no se pueden reconstruir sus bytes originales. La conservación exacta aplica a respuestas guardadas después de migrar. El cambio en la infraestructura compartida es necesario para la corrección del middleware solicitada en la adenda; no se modificaron otros módulos de negocio.

**Excepción de cuerpo vacío:** se reprodujo localmente la misma `JsonReaderException` sin base de datos y con un cuerpo no vacío. `ContabTestKit.Json` consumía el stream de `HttpContent`; la prueba concurrente lee `rs[0]` para obtener `solicitudId` y luego `AutorizarRespuesta` vuelve a leer esa misma respuesta. El segundo parse empezaba al final. El helper ahora analiza los bytes conservados por `HttpContent`, sin consumir su stream. La prueba concurrente mantiene dos 202 con el mismo expediente, una sola solicitud y ninguna aplicación previa, y exige expresamente que ambos cuerpos sean no vacíos. No se alteró el manejo de excepciones de producción ni se aceptaron respuestas vacías. El gate real debe confirmar si subsiste algún problema adicional de concurrencia.

Pruebas añadidas o reforzadas en esta continuación:

- `P9IdempotencyTests`: detecta el almacenamiento que normalizaba el cuerpo; falló antes de corregir el modelo y pasó después.
- `ContabTestKitTests`: dos lecturas consecutivas de respuestas 202/409 y rechazo de un cuerpo realmente vacío. Se ejecutaron los tres casos de la DLL de integración con un runner filtrado; estos casos no crean un host ni abren conexiones PostgreSQL. La variable de conexión usada para el inicializador apuntó a `p9_lectura_sin_conexion`, sin acceso a una base.
- `IdempotencyMiddlewareTests`: igualdad de bytes original/replay y una regresión PostgreSQL para espacios, orden, escapes Unicode, texto UTF-8 y decimales. La fila temporal se elimina en `finally`; la regresión PostgreSQL está escrita y compilada, pendiente de ejecución real.
- `CatalogoHttpTests` e `ImportacionHttpTests`: se conservó la intención de idempotencia y concurrencia del flujo pendiente, reforzando las comprobaciones del cuerpo.

| Verificación local vigente | Resultado |
|---|---|
| `dotnet build Millet.sln --no-restore -m:1 -p:UseSharedCompilation=false` | 0 errores, 0 advertencias; 4.04 s en la corrida final sin builds superpuestos |
| Compilación explícita de `Millet.Api.IntegrationTests.csproj` | 0 errores, 0 advertencias; 39.88 s |
| Unitarias Contabilidad, misma DLL con runner xUnit en proceso | 180 aprobadas, 0 fallidas, 0 omitidas |
| Unitarias API, misma DLL con runner xUnit en proceso | 14 aprobadas, 0 fallidas, 0 omitidas |
| Regresiones puras `ContabTestKitTests`, misma DLL con runner filtrado | 3 aprobadas, 0 fallidas, 0 omitidas |
| `npx tsc --noEmit -p tsconfig.json` | Salida 0 |
| `npm run -s typecheck:test` | Salida 0 |
| `npm run -s lint` | Salida 0; 0 errores, 10 advertencias preexistentes en Administración |
| `npx vitest run --maxWorkers=2 --testTimeout=30000` | 340 archivos, 1,994 pruebas aprobadas; salida 0; 201.34 s |
| EF `has-pending-model-changes`: Core, Contabilidad e Identidad | Sin cambios de modelo pendientes |
| SQL de la nueva migración | Generado e inspeccionado; no aplicado |
| `tools/validate-integration-isolated.sh` | No arrancó: permiso denegado al socket de Docker; ninguna suite PostgreSQL ejecutada |

El runner estándar `dotnet test` volvió a abortar por `SocketException (13): Permission denied` al iniciar VSTest. El runner temporal de xUnit ejecutó las DLL compiladas sin cambiar los proyectos ni omitir sus pruebas unitarias. Vitest emitió avisos de canvas y navegación de jsdom, sin fallos. Una compilación superpuesta produjo un reintento de copia MSB3026; la compilación final sin superposición terminó sin advertencias.

Archivos de esta continuación: `IdempotencyKey.cs`, `IdempotencyKeyConfiguration.cs`, comentario de persistencia en `IdempotencyMiddleware.cs`, snapshot de Core, migración `P9RespuestaIdempotenteExacta` y Designer, `ContabTestKit.cs`, nuevo `ContabTestKitTests.cs`, nuevo `P9IdempotencyTests.cs`, `CatalogoHttpTests.cs`, `ImportacionHttpTests.cs`, `IdempotencyMiddlewareTests.cs` y este documento. Se conservaron los cambios previos del dominio, handlers, permisos, seed, migraciones P9 y frontend; no hubo cambios nuevos en frontend durante esta continuación.

**Pendiente / siguiente acción:** Claude debe ejecutar `tools/validate-integration-isolated.sh` completo después de aplicar las cuatro migraciones P9 de Contabilidad, Identidad y Core, y registrar el rojo/verde y conteos actuales. La prueba real de concurrencia/replay, la revisión visual en navegador y la validación D14/V40 con Laura/Millet siguen **Por confirmar**. La actualización de la bóveda queda preparada en este documento local: su ruta está fuera del permiso de escritura de esta sesión.

Mensaje de commit propuesto (no ejecutado): `feat(contabilidad): completar catálogo con autorización DAF y replay exacto`.

## Continuación · adenda de integración del 09-oct-2026

Se conservó la implementación anterior y se atendieron los cuatro fallos reportados por Eliam/Claude (A+W 7/7, Compras 138/138, API 865/869). Ese rojo es evidencia comunicada por el usuario; no es una ejecución de este sandbox.

- **422 del lote P9:** el archivo de prueba contenía `1`, `1.1` y `1.2`. La regla vigente `PadrePorSegmentos` conserva el ancho y pone el último segmento a ceros, por lo que las hijas requieren `1.0`, ausente del archivo. La regresión unitaria reproduce `CONTAB_IMPORT_PADRE_INEXISTENTE`. Se corrigió la muestra a raíz `100.00.00.00` e hijas `100.10.00.00`/`100.20.00.00`, sin cambiar el importador ni relajar reglas. El test HTTP comprueba además que la vista previa sea válida antes de preparar el lote.
- **Idempotencia individual:** ahora se exige 202, misma respuesta completa, mismo `Location` y `solicitudId`, una sola solicitud pendiente y cero cuentas vigentes.
- **Altas concurrentes:** antes faltaba la restricción sobre propuestas: el índice de cuentas solo protege el catálogo autorizado. `CodigoAlta` normalizado y el índice parcial único por empresa `ux_p9_alta_pendiente` garantizan una sola alta pendiente. El conflicto devuelve 409 y `CONTAB_CUENTA_CODIGO_DUPLICADO`, con explicación en español. La prueba usa también un código en minúsculas para verificar la normalización.
- **Importaciones concurrentes:** se conserva el índice por huella ya implementado. La prueba exige dos respuestas 202 con el mismo expediente, una sola solicitud del archivo y cero cuentas, orígenes y lotes aplicados antes de autorizar; después comprueba la aplicación única.
- **Nueva regresión HTTP:** rechazar un alta libera su código para preparar otra solicitud, sin aplicar la cuenta. Los datos ficticios se limpian por prefijo al terminar.

La migración nueva `20261009164709_P9AltasPendientesUnicas` agrega la columna, rellena el código de altas ya pendientes desde su comando y crea el índice. Se conservan las dos migraciones P9 anteriores. EF no detectó diferencias entre el modelo y el snapshot; el SQL generado se inspeccionó. Esto no prueba su aplicación en PostgreSQL.

| Verificación en esta continuación | Resultado |
|---|---|
| `dotnet build Millet.sln --no-restore -m:1 -p:UseSharedCompilation=false` | 0 errores, 0 advertencias; 94.24 s en la corrida final |
| Compilación explícita de `Millet.Api.IntegrationTests.csproj` | 0 errores, 0 advertencias; 13.00 s |
| Unitarias Contabilidad con runner xUnit en proceso | 180 aprobadas, 0 fallidas, 0 omitidas |
| Unitarias API con runner xUnit en proceso | 13 aprobadas, 0 fallidas, 0 omitidas |
| `npx tsc --noEmit -p tsconfig.json` | Salida 0 |
| `npm run -s typecheck:test` | Salida 0 |
| `npm run -s lint` | 0 errores, 10 advertencias en Administración |
| `npx vitest run --maxWorkers=2 --testTimeout=30000` | 340 archivos y 1,994 pruebas aprobadas, 0 fallidas; salida 0, duración 393.26 s |
| `git diff --check` | Aprobada |
| Gate PostgreSQL completo | No arrancó: acceso denegado al socket de Docker; sin pruebas ejecutadas |

El runner estándar `dotnet test` abortó por `SocketException (13): Permission denied` al iniciar VSTest. El runner xUnit temporal ejecutó las mismas DLL dentro del proceso. No se cambió la configuración de tests ni se omitieron casos.
Vitest mostró avisos de capacidades de jsdom (canvas y navegación), sin fallos. Los resultados anteriores se conservan abajo como historial; esta tabla es la verificación vigente de la continuación.

**Pendiente:** Claude debe ejecutar `tools/validate-integration-isolated.sh` completo y registrar los conteos actuales y el rojo/verde. Las pruebas HTTP están escritas y compiladas; su resultado real, las migraciones aplicadas, la revisión visual en sesión y la validación D14/V40 con Laura/Millet siguen **Por confirmar**. El registro de la bóveda no se modificó: está fuera de las rutas autorizadas; esta sección deja preparado el contenido local para incorporarlo con esos límites.

Archivos ajustados en esta continuación: `SolicitudCatalogo.cs`, `SolicitudesCatalogo.cs`, `Configurations.cs`, snapshot de Contabilidad, migración `P9AltasPendientesUnicas` y su Designer, `CatalogoHttpTests.cs`, `ImportacionHttpTests.cs`, `P9SolicitudesHttpTests.cs`, `P9CatalogoTests.cs` y este documento. El frontend previo se conservó y se volvió a validar.

## Resultado y contraste con la ficha

Se retomaron los cambios sin commit de la corrida interrumpida; no se descartaron ni se reinició la implementación.
El código anterior al diff no incluía `NoAfectableManual`; sus comandos individuales e importaciones guardaban directamente el catálogo. La restricción de cuentas colectivas continúa vigente.

- Casilla `NoAfectableManual` en dominio, respuestas, lectura, formulario y detalle. El origen Manual sobre una cuenta afectable marcada devuelve `CUENTA_NO_AFECTABLE_MANUAL` con mensaje en español; los auxiliares siguen su validación habitual.
- Importación admite la columna opcional `no_afectable_manual`: Sí/No, true/false o 1/0. Ausencia o celda vacía conserva la marca existente. Una marca explícita participa en la huella; archivos históricos sin ella conservan su huella anterior.
- Altas, cambios, bajas, reactivaciones e importaciones se preparan como solicitudes pendientes (HTTP 202 con `solicitudId` y Location al expediente). El catálogo, orígenes y lotes aplicados no cambian durante la preparación.
- DAF autoriza o rechaza. Autorizar aplica en una transacción; rechazar conserva motivo, actor y fecha. Quien preparó no puede autorizar, también si es superadministrador. Resolver exige el permiso nuevo y `If-Match`; mantiene la idempotencia del API.
- Bandeja en **Contabilidad → Catálogo de cuentas → Autorizaciones**: pendientes, autorizadas y rechazadas; diferencias antes/después, responsables, fechas y motivo. La casilla está en el formulario de cuenta y su valor en el detalle.
- Solicitudes y cuentas usan la bitácora central mediante `IAuditable`. Se conserva el comando original del expediente y las diferencias de la propuesta.
- Seed DEMO marca banco, inventario e IVA como **Por confirmar V40**. No se usaron cuentas originales de Millet para inventar una política definitiva.

La ficha suponía un rol DAF ya sembrado. En este corte el bootstrap tenía super-admin y ocho roles adicionales, sin DAF. Se agregó idempotentemente `direccion-administracion-finanzas` (Dirección de Administración y Finanzas), con leer y autorizar catálogo. En DEMO se separa DAF de Contabilidad; Contabilidad prepara sin `autorizar`. No se asignaron permisos a personas reales.

## Decisiones técnicas

- D14 implementada por lote de importación y por cambio individual. Su validación con Laura sigue pendiente.
- Se reutilizan las reglas del catálogo al preparar y al autorizar; la propuesta se descarta del ChangeTracker antes de guardar su expediente. Se conservan los ids propuestos para altas e importaciones.
- Por seguridad, la solicitud guarda la huella del catálogo de su empresa. Si cambia antes de autorizar (incluso otra cuenta), responde 409 y exige rechazar/preparar de nuevo. Esto evita aplicar una propuesta cuya revisión quedó desactualizada.
- La transacción Serializable, el token de versión y el índice único de importación pendiente protegen decisiones concurrentes. Los conflictos de PostgreSQL se traducen a mensajes en español.
- No se agregan eventos de integración: el catálogo actual no tiene consumidores ni Outbox. Se mantiene ese alcance existente; ADR-0009 aplica cuando se emitan eventos.
- Las herramientas temporales para EF y xUnit están fuera del repo; no agregan dependencias al producto. EF se generó con fábricas de diseño sin conexión a PostgreSQL.

## Migraciones

- `20261009154545_P9CatalogoSolicitudesYNoAfectableManual`: casilla (false para registros previos), tabla de solicitudes, índices y snapshot de Contabilidad.
- `20261009154808_P9PermisoAutorizarCatalogo`: permiso canónico y snapshot de Identidad.

Ambos contextos se verificaron con `dotnet ef migrations has-pending-model-changes`: no hay cambios de modelo pendientes. Las migraciones no se aplicaron a una base real en este sandbox.

## Pruebas nuevas y ajustes

`P9CatalogoTests`: 12 casos unitarios para marca/origen, valores de importación, conservación y huella, segregación, motivo y resolución única.
`P9SolicitudesHttpTests`: seis pruebas de integración escritas y compiladas: altas/cambios/bajas/reactivación y bitácora, 403/422/428/rechazo, importación pendiente/autorizada/idempotente, conflicto de catálogo, movimiento manual marcado 422/no marcado 201 y permiso del seed DAF.
Frontend: casilla enviada en el formulario; diferencias puras; revisión, autorización y rechazo de solicitudes con versión e idempotencia; bloqueos de permiso y autor. Se sincronizó la prueba transversal de permisos.
La prueba nueva de movimiento manual crea y elimina su propia sucursal, tipo de documento y movimientos; no depende del orden de otras suites. Las suites anteriores del catálogo recorren solicitud → otro DAF → lectura vigente. Los datos de cuenta se limpian por prefijo y los usuarios/roles DAF temporales se eliminan; los tests P9 usan HTTP directo para verificar el contrato 202.

## Resultados de la corrida anterior · evidencia conservada

| Verificación | Resultado |
|---|---|
| `dotnet build Millet.sln --no-restore -m:1 -p:UseSharedCompilation=false` | Aprobada: 0 errores y 0 advertencias |
| Unitarias Contabilidad (misma DLL, xUnit en proceso) | 179 aprobadas, 0 fallidas, 0 omitidas |
| Unitarias API (misma DLL, xUnit en proceso) | 13 aprobadas, 0 fallidas, 0 omitidas |
| `npx tsc --noEmit -p tsconfig.json` | Aprobada, salida 0 |
| `npm run -s typecheck:test` | Aprobada, salida 0 |
| `npm run -s lint` | Aprobada: 0 errores y 10 advertencias preexistentes en Administración |
| `npx vitest run --maxWorkers=2 --testTimeout=30000` | Aprobada: 340 archivos, 1,994 pruebas; salida 0, duración 320.44 s |
| `git diff --check` | Aprobada |
| Integración PostgreSQL | Por confirmar: sin Docker en este sandbox |
| Revisión visual en navegador | Por confirmar: control de aplicaciones rechazó abrir Chrome por falta de aprobación |

La primera corrida completa de Vitest terminó con 339 archivos aprobados, 1,993 pruebas aprobadas y un fallo: faltaba el nuevo permiso en el set esperado de la prueba transversal. Se corrigió ese set y la segunda corrida completa aprobó los 340 archivos y las 1,994 pruebas. Los timeouts de la corrida inicial del módulo coincidieron con la máquina saturada; la verificación completa usa `--maxWorkers=2 --testTimeout=30000`, sin modificar la configuración del producto. Vitest mostró avisos de capacidades de jsdom (canvas y navegación), sin fallos.

El runner estándar `dotnet test` fue intentado: aborta al abrir el socket de VSTest (`SocketException (13): Permission denied`). El runner temporal xUnit ejecuta las mismas pruebas dentro de su proceso, sin ese socket.

Se preparó `/private/tmp/p9-visual-build/p9.html` con los componentes reales y datos DEMO, compilado con Vite. Es una vista aislada sin conexión al API; no acredita render verificado, sesión real ni aceptación de Millet. Los archivos temporales de entrada/configuración se retiraron del worktree.

## Pendiente y siguiente acción

Claude debe ejecutar **completo** `tools/validate-integration-isolated.sh` desde este worktree, registrar el rojo/verde y corregir cualquier fallo antes de integrar. El script crea PostgreSQL desechable, aplica migraciones y ejecuta las tres suites completas. No sustituirlo por un filtro P9 para declarar aprobado el gate.
Después, revisar la pantalla dentro del App Shell y ensayar con Laura/DAF la preparación, revisión, autorización, rechazo, bloqueo de autoautorización y la propuesta V40. Datos definitivos, D14 validada y aceptación de Millet: **Por confirmar**.

Commit propuesto (no ejecutado): `feat(contabilidad): completar catálogo con autorización DAF y bloqueo manual`.

## Archivos del cambio

- `backend/src/Api/Endpoints/Contabilidad/ContabilidadCatalogoEndpoints.cs`
- `backend/src/Api/Seed/DemoSesionFinanzas.cs`
- `backend/src/Api/Seed/DemoSesionSeedHostedService.cs`
- `backend/src/Api/Seed/DemoSesionUsuarios.cs`
- `backend/src/Contabilidad/Application/Catalogo/CuentasCommands.cs`
- `backend/src/Contabilidad/Application/Catalogo/SolicitudesCatalogo.cs`
- `backend/src/Contabilidad/Application/CatalogoOpciones.cs`
- `backend/src/Contabilidad/Application/Dimensiones/ValidadorDimensiones.cs`
- `backend/src/Contabilidad/Application/Importacion/ImportacionCommands.cs`
- `backend/src/Contabilidad/Application/Importacion/ImportadorCatalogo.cs`
- `backend/src/Contabilidad/Application/PublicPorts/ICuentaContableReadPort.cs`
- `backend/src/Contabilidad/Domain/CuentaContable.cs`
- `backend/src/Contabilidad/Domain/SolicitudCatalogo.cs`
- `backend/src/Contabilidad/Infrastructure/DependencyInjection.cs`
- `backend/src/Contabilidad/Infrastructure/Persistence/Configurations/Configurations.cs`
- `backend/src/Contabilidad/Infrastructure/Persistence/ContabilidadDbContext.cs`
- `backend/src/Contabilidad/Infrastructure/Persistence/Migrations/20261009154545_P9CatalogoSolicitudesYNoAfectableManual.Designer.cs`
- `backend/src/Contabilidad/Infrastructure/Persistence/Migrations/20261009154545_P9CatalogoSolicitudesYNoAfectableManual.cs`
- `backend/src/Contabilidad/Infrastructure/Persistence/Migrations/20261009164709_P9AltasPendientesUnicas.Designer.cs`
- `backend/src/Contabilidad/Infrastructure/Persistence/Migrations/20261009164709_P9AltasPendientesUnicas.cs`
- `backend/src/Contabilidad/Infrastructure/Persistence/Migrations/ContabilidadDbContextModelSnapshot.cs`
- `backend/src/Contabilidad/Infrastructure/PublicAdapters/CuentaContableReadAdapter.cs`
- `backend/src/Identidad/Domain/PermisosCanonicos.cs`
- `backend/src/Identidad/Infrastructure/BootstrapSuperAdminHostedService.cs`
- `backend/src/Identidad/Infrastructure/Migrations/20261009154808_P9PermisoAutorizarCatalogo.Designer.cs`
- `backend/src/Identidad/Infrastructure/Migrations/20261009154808_P9PermisoAutorizarCatalogo.cs`
- `backend/src/Identidad/Infrastructure/Migrations/IdentidadDbContextModelSnapshot.cs`
- `backend/tests/Api.IntegrationTests/Administracion/DemoSesionSeedTests.cs`
- `backend/tests/Api.IntegrationTests/Contabilidad/CatalogoHttpTests.cs`
- `backend/tests/Api.IntegrationTests/Contabilidad/ContabTestKit.cs`
- `backend/tests/Api.IntegrationTests/Contabilidad/ImportacionHttpTests.cs`
- `backend/tests/Api.IntegrationTests/Contabilidad/P9SolicitudesHttpTests.cs`
- `backend/tests/Api.IntegrationTests/Contabilidad/PuertoLecturaTests.cs`
- `backend/tests/Api.IntegrationTests/Contabilidad/ReglasLauraHttpTests.cs`
- `backend/tests/Api.UnitTests/DemoSesionSeedTests.cs`
- `backend/tests/Contabilidad.UnitTests/P9CatalogoTests.cs`
- `frontend/src/features/contabilidad/api/hooks.ts`
- `frontend/src/features/contabilidad/api/types.ts`
- `frontend/src/features/contabilidad/components/ConfirmarEstatusCuenta.tsx`
- `frontend/src/features/contabilidad/components/CuentaForm.test.tsx`
- `frontend/src/features/contabilidad/components/CuentaForm.tsx`
- `frontend/src/features/contabilidad/components/SolicitudesCatalogo.test.tsx`
- `frontend/src/features/contabilidad/components/SolicitudesCatalogo.tsx`
- `frontend/src/features/contabilidad/lib/diferencias-catalogo.test.ts`
- `frontend/src/features/contabilidad/lib/diferencias-catalogo.ts`
- `frontend/src/features/contabilidad/lib/textos.ts`
- `frontend/src/features/contabilidad/pages/CatalogoPage.smoke.test.tsx`
- `frontend/src/features/contabilidad/pages/CatalogoPage.tsx`
- `frontend/src/features/contabilidad/pages/CuentaDetallePage.test.tsx`
- `frontend/src/features/contabilidad/pages/CuentaDetallePage.tsx`
- `frontend/src/features/contabilidad/pages/ImportacionPage.test.tsx`
- `frontend/src/features/contabilidad/pages/ImportacionPage.tsx`
- `frontend/src/features/contabilidad/pages/MovimientosPruebaPage.test.tsx`
- `frontend/src/features/contabilidad/schemas/cuenta.ts`
- `frontend/src/lib/auth/permission-codes.test.ts`
- `frontend/src/lib/auth/permission-codes.ts`
