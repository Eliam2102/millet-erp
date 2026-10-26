# FMT-1b · Continuación del 22-oct-2026

## Estado y alcance

Rama `feature/fmt1b-oc-datos-factura`, HEAD inicial `9fcfc6cf59d8101f6cf101d55f7bac98bac406c9`. Integración preexistente con `origin/feature/86e3jvu8q-envio-oc` en curso. Sin commit, push ni cambio de rama.

Leídos completos, en orden, `01_Continuacion_22oct.md` (incluida la corrección final del anticipo), `00_Reglas_comunes.md` y `FMT-1b_OC_datos_factura.md`; luego las normas del repositorio y de UI. No existía CODEX_RESUMEN.md. La corrección del anticipo corresponde a FMT-3 y no requiere cambiar esta ficha.

## Inventario verificado contra código

- `backend/src/Compras/Domain/Oc/OrdenCompra.cs`: uso CFDI, método y forma opcionales en borrador; edición con estados de cabecera; método PUE/PPD; error `OC_DATOS_FACTURA_INCOMPLETOS` que enumera los faltantes al enviar.
- `Domain/Ports/Catalogos/ICatalogoSatReadPort.cs`, `Infrastructure/Catalogos/CatalogoSatReadAdapter.cs`, `Application/Oc/DatosFacturaOcGuard.cs`: lectura de claves SAT compartidas mediante puerto de Compras; existencia y actividad exigidas al capturar y al enviar; históricos conservan descripción.
- Comandos, validadores y handlers en `Application/Oc/CrearOrdenCompraVacia`, `CrearOrdenCompraDesdeRequisicion`, `ActualizarCabecera`, `EnviarAAutorizacion`: propagan los tres campos. Duplicación conserva los datos fiscales. Se conserva la herencia de condiciones de pago de la cotización y la posibilidad de cambiarlas.
- `Application/Oc/ObtenerOrdenCompraPorId/{ObtenerOrdenCompraPorIdHandler.cs,OrdenCompraResponse.cs}`: combina datos fiscales, descripción SAT y datos de impresión con los enriquecimientos de la base: incidencias, usuarios y recepciones de importación.
- `Domain/Oc/OrdenCompraPdf.cs`, configuraciones de OC/PDF y `Application/Oc/RegistrarImpresion/RegistrarImpresionOcCommand.cs`: contador, último usuario y fecha; bloqueo PostgreSQL `FOR UPDATE` y token de concurrencia. Usuario vacío rechazado.
- `Application/Oc/Envios/RegistrarEnvioOc.cs`: envío por correo exitoso/aceptado o simulado llama al mismo comando de impresión; fallo no cuenta; registro manual no cuenta otra vez porque su descarga ya lo hizo. Se conserva el historial de envíos de la base.
- `Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs`: descarga registra y devuelve `X-Oc-Impresion`; `vistaPrevia=true` no registra; PDF guardado se conserva. `Api/Program.cs`: DI del puerto SAT y exposición CORS del header.
- `Api/Seed/DemoSesionCompras.cs`, `DemoSesionComercioExterior.cs`, `DemoSesionActivosFijos.cs`, `DemoSesionComercioExteriorFinanzas.cs`, `DemoSesionOrdenesServicioCe.cs`: fixtures de OC que se autorizan reciben G03/PPD/99. Los tres últimos ajustes ya estaban presentes al iniciar esta continuación; son adaptación de fixtures al nuevo requisito, no cambios de negocio en esos módulos.
- `frontend/src/features/compras/ordenes/pages/SheetNuevaOC.tsx`, `schemas/crear-oc-vacia.ts`, `components/{CabeceraOrdenCompra.tsx,CampoFacturaOc.tsx}`: bloque fiscal, catálogos SAT activos y etiquetas clave/descripción, condiciones y moneda agrupadas; uso interno separado con ayuda de que no se imprime. Borrador sigue admitiendo datos fiscales vacíos; la revisión previa exige completarlos al transmitir.
- `api/{types.ts,useActualizarCabeceraOc.ts,useCrearOrdenCompraDesdeRequisicion.ts,usePdfOrdenCompra.ts}`, `components/TabPdf.tsx`, `lib/{revision-previa-oc.ts,mensajes-transmision.ts}`: contratos y mensajes; vista previa y descarga distintas, detalle refrescado tras descarga, número de impresiones con fecha/nombre. Se conserva `descargarPdfOrdenCompra` para envío manual; tercer parámetro `vistaPrevia` opcional y false por omisión.
- Pruebas de dominio, validadores, envíos, endpoint PDF, formularios y revisión previa presentes. Fixtures existentes adaptados a datos fiscales obligatorios.
- `Api/Web/ComprasDesignTimeDbContext.cs` ya estaba eliminado al inicio: la base nueva incluye `ComprasDesignTimeFactory` en `P6DesignTimeDbContexts.cs`; se evita una factory duplicada.

## Cambios adicionales de esta continuación

- `backend/tests/Compras.UnitTests/Oc/Application/DatosFacturaValidatorTests.cs`: los mismos casos de claves y método ahora cubren también crear desde requisición, además de alta vacía y edición.
- `frontend/src/features/compras/ordenes/components/TabPdf.test.tsx`: dos clics deben generar dos solicitudes reales de descarga; limpieza de mocks/globales para no contaminar otras pruebas.
- `frontend/src/features/compras/ordenes/components/EnviosOcPanel.tsx` y su prueba: refrescar el detalle inmediatamente después de la descarga manual para que el contador visible no espere al registro del envío. Se conserva la descarga normal (sin vista previa).
- `CODEX_RESUMEN.md`: inventario y evidencia de la continuación.

## Conflictos

Los cuatro archivos del índice sin resolver tienen contenido combinado y ningún marcador de conflicto:

1. `backend/src/Compras/Application/Oc/ObtenerOrdenCompraPorId/ObtenerOrdenCompraPorIdHandler.cs`
2. `backend/src/Compras/Application/Oc/ObtenerOrdenCompraPorId/OrdenCompraResponse.cs`
3. `backend/tests/Api.IntegrationTests/Compras/Oc/CotizacionesEndpointsTests.cs`
4. `frontend/src/features/compras/ordenes/api/usePdfOrdenCompra.ts`

En la prueba de cotización se conserva `CondicionesPagoId = Guid.Empty` para comprobar la herencia, añadiendo G03/PPD/99.

**Pendiente externo al contenido:** `git add` fue rechazado: `Unable to create .../millet_erp-review/.git/worktrees/millet_erp-fmt-oc/index.lock: Operation not permitted`. El índice vive fuera de la raíz escribible. No se intentó evadir el sandbox. Marcar estos cuatro archivos con `git add` desde un entorno con permiso; no hacer commit de integración todavía.

## Verificación

**No está lista para integración.** La máquina presentó carga extrema (load average de 1 minuto >200; todavía >170 al cierre), con comandos que dejaron de producir resultados durante varios minutos. Se detuvieron los procesos propios para no añadir carga. No se declara ninguna suite completa en verde en esta continuación.

| Comprobación | Resultado de esta continuación | Log |
|---|---|---|
| `dotnet build Millet.sln -warnaserror` | No concluyó. Reintento serial con `DOTNET_PROCESSOR_COUNT=2`, `--no-restore -m:1 -p:UseSharedCompilation=false` tampoco concluyó; llegó a compilar dependencias hasta CentrosCosto. Al cancelar terminó con MSB6006, csc código 130 en Identidad, tras 9 min 42 s; es una interrupción, no evidencia de error de código. | `/tmp/fmt-oc-build.log`, `/tmp/fmt-oc-build-final.log` |
| Unitarias Compras (`dotnet test ... --no-restore -m:1`) | No concluyeron; canceladas, sin resultado de pruebas. | `/tmp/fmt-oc-unit-current.log` |
| `npx tsc -b tsconfig.app.json tsconfig.node.json` | No concluyó. Reintento directo con `node node_modules/typescript/bin/tsc` tampoco terminó; no hay resultado aprobatorio. | `/tmp/fmt-oc-tsc.log`, `/tmp/fmt-oc-tsc-direct.log` |
| `npx eslint src` | No concluyó; cancelado sin resultado aprobatorio. | `/tmp/fmt-oc-eslint.log` |
| Vitest carpeta OC | Dos intentos cancelados; aparecieron fallos en CancelacionDialogs y DetalleOrdenCompra.smoke y avisos de MSW. No se pudo completar ni determinar si cada fallo es funcional o consecuencia de los tiempos de ejecución. | `/tmp/fmt-oc-vitest.log`, `/tmp/fmt-oc-vitest-final.log` |
| Vitest seis archivos afectados, un worker y timeout 60s | No concluyó; sin resultado aprobatorio. Incluye TabPdf, EnviosOcPanel, CabeceraOrdenCompra, crear-oc-vacia, revision-previa-oc y mensajes-transmision. | `/tmp/fmt-oc-vitest-affected.log` |
| `dotnet ef --version` | Correcto: 10.0.12. SDK 10.0.401 y Node 22.23.0 disponibles. | `/tmp/fmt-oc-ef-version.log` |
| `dotnet ef migrations add FmtOcDatosFacturaImpresiones --no-build --project src/Compras --startup-project src/Api --context ComprasDbContext` | Intentado, sin terminar ni generar archivos. Cancelado. Los ensamblados de Api y Compras seguían fechados el 20-oct; **es necesario compilar primero**, para no generar contra un modelo viejo. | `/tmp/fmt-oc-migration.log`, `/tmp/fmt-oc-migration-final.log` |
| Marcadores de conflicto | Ningún marcador de conflicto real en backend ni frontend/src. | `rg` anclado al principio de línea |
| `git diff --check` en Compras y frontend OC | Sin errores en archivos de la ficha. La integración staged global trae una línea en blanco final preexistente en `Api/Web/AF04DesignTimeDbContexts.cs`, fuera de alcance. | comprobación directa |

No se generó `FmtOcDatosFacturaImpresiones` ni se editó a mano el snapshot. El esquema desplegado **no** está actualizado para estos campos.

## Siguiente acción reproducible

1. Desde un entorno con permiso para el índice, marcar con `git add` los cuatro conflictos enumerados; no hacer commit todavía.
2. Cuando el equipo recupere recursos, ejecutar desde backend:
   - `DOTNET_PROCESSOR_COUNT=2 dotnet build Millet.sln -warnaserror -m:1 -p:UseSharedCompilation=false`
   - `dotnet ef migrations add FmtOcDatosFacturaImpresiones --no-build --project src/Compras --startup-project src/Api --context ComprasDbContext`
   - Revisar que Up/Down sólo agreguen/retiren las tres columnas fiscales de OC y las tres de impresión de PDF (contador 0 por omisión; fechas y usuario nullable), sin cambios ajenos.
   - `dotnet build Millet.sln -warnaserror -m:1`
   - `dotnet test tests/Compras.UnitTests/Millet.Compras.UnitTests.csproj --no-restore -m:1`
3. Desde frontend: `npx tsc -b tsconfig.app.json tsconfig.node.json`, `npx eslint src`, `npx vitest run src/features/compras/ordenes --maxWorkers=1`. Investigar cualquier fallo repetido con recursos normales.
4. Claude corre las pruebas de integración Api/Compras en Docker, incluida descarga 1/2, vista previa sin incremento y reemplazo de PDF que reinicia el contador; revisar visualmente formulario y PDF embebido.
5. Actualizar este resumen con resultados reales antes de declarar la ficha lista.

## Fuera del alcance

No se modifica `QuestPdfOrdenCompraGenerator.cs`, ni el diseño/marca visual del PDF (FMT-2), ni la captura de anticipos/facturación (FMT-3). No hay publicación, despliegue ni validación de aceptación del cliente. Integración Docker y revisión visual real pendientes conforme al alcance de la ficha.

## Correcciones del 22-oct

Aplicada únicamente la sección **FMT-1b** de `02_Correcciones_revision_22oct.md`, después de leer completos ese documento, `00_Reglas_comunes.md` y `01_Continuacion_22oct.md`. El estado actual ya no tiene conflictos sin resolver y ya contiene la migración `20261022224312_FmtOcDatosFacturaImpresiones`; las notas anteriores describen la corrida previa, no el estado verificado en esta revisión.

### Cambios por archivo y decisiones

- `backend/src/Compras/Application/Oc/Envios/RegistrarEnvioOc.cs`: el fallo del comando de impresión posterior a guardar un correo aceptado o simulado se captura y se registra con `ILogger` como advertencia, incluyendo los identificadores de envío y OC. Devuelve el envío conservando su resultado; no induce un reenvío por un error del contador.
- `backend/src/Compras/Application/Oc/ObtenerOrdenCompraPorId/ObtenerOrdenCompraPorIdHandler.cs`: los tres faltantes fiscales sólo se agregan a `CamposPorCapturar` en Borrador o Rechazada. Se conserva el cálculo preexistente de proveedor, condiciones y uso interno. El método pasa a `internal` para probar la regla con el patrón de pruebas puras que ya tiene este handler.
- `frontend/src/features/compras/ordenes/components/CabeceraOrdenCompra.tsx`: el aviso de captura sólo se muestra en Borrador o Rechazada, independientemente del permiso. Se retiran controles, estado de formulario y envío de moneda/tipo de cambio; se conserva su consulta dentro de «Datos para la factura del proveedor». Se corrige la indentación del JSX del bloque y se deja una sola ayuda sobre el uso interno que no se imprime.
- `frontend/src/features/compras/ordenes/pages/SheetNuevaOC.tsx`: se elimina la línea en blanco sobrante.
- `OrdenCompraPdfConfiguration.cs` **sin cambios**: se conserva `HasDefaultValue(0)` porque el snapshot y el modelo de la migración Fmt ya incluyen ese default; eliminarlo cambiaría el modelo relacional y exigiría una alteración del default persistente. No se generan migraciones nuevas ni se edita el snapshot.

### Pruebas nuevas

- `backend/tests/Compras.UnitTests/Oc/Envios/EnviosOcTests.cs`: dos casos (correo aceptado y simulado) fuerzan el fallo de impresión y comprueban devolución del resultado, persistencia del envío tras limpiar el tracking, transporte invocado y advertencia con excepción e identificador del envío. Se adapta la única construcción directa del handler a su logger.
- `backend/tests/Compras.UnitTests/Oc/Application/ObtenerOrdenCompraPorIdHandlerTests.cs`: ocho estados cubren que una OC histórica sin datos fiscales sólo reporte los tres faltantes en Borrador o Rechazada.
- `frontend/src/features/compras/ordenes/components/CabeceraOrdenCompra.test.tsx`: cinco casos nuevos cubren aviso en Borrador/Rechazada, ausencia en Autorizada/Cerrada aunque la respuesta enumere faltantes, y moneda/tipo de cambio de sólo lectura con ayuda única durante la edición. La prueba existente de guardado sigue comprobando que se envíen únicamente los campos modificados.

### Verificación de esta revisión

| Comprobación | Resultado |
|---|---|
| Build de Compras y su proyecto de pruebas | **Correcto**, 0 advertencias y 0 errores: `TMPDIR=/private/tmp DOTNET_PROCESSOR_COUNT=2 dotnet build backend/tests/Compras.UnitTests/Millet.Compras.UnitTests.csproj --no-restore -warnaserror -m:2 -p:BuildInParallel=false -p:UseSharedCompilation=false -nr:false`. Log: `/tmp/fmt1b-correcciones-build-compras.log`. |
| Unitarias de Compras | **Ejecución anulada por el entorno**, después de compilar: `TMPDIR=/private/tmp DOTNET_PROCESSOR_COUNT=2 dotnet test backend/tests/Compras.UnitTests/Millet.Compras.UnitTests.csproj --no-restore -m:2 -p:BuildInParallel=false -p:UseSharedCompilation=false -nr:false`. VSTest no puede abrir su socket TCP local (`System.Net.Sockets.SocketException (13): Permission denied`). No se declara ninguna prueba de backend aprobada. Log: `/tmp/fmt1b-correcciones-unit-final.log`. |
| Intentos iniciales de .NET y build de la solución | La ejecución paralela de MSBuild también encontró `Permission denied` al crear named pipes; el build global no produjo un resultado aprobatorio. Se usó `BuildInParallel=false` con el máximo pedido `-m:2` para completar el build del proyecto afectado. Logs: `/tmp/fmt1b-correcciones-unit.log`, `/tmp/fmt1b-correcciones-build.log`, `/tmp/fmt1b-correcciones-build-final.log`. |
| `npx tsc -b tsconfig.app.json tsconfig.node.json` | **Correcto**, código de salida 0. Log: `/tmp/fmt1b-correcciones-tsc.log`. |
| ESLint de CabeceraOrdenCompra, su prueba y SheetNuevaOC | **Correcto**, código de salida 0. Log: `/tmp/fmt1b-correcciones-eslint.log`. |
| `npx vitest run src/features/compras/ordenes --maxWorkers=2` | **Correcto: 46 archivos, 270 pruebas aprobadas**. Log: `/tmp/fmt1b-correcciones-vitest.log`. |
| `git diff --check` de los archivos de esta corrección | **Correcto**, sin errores de whitespace. |
| Índice Git | Comparación binaria del diff staged antes/después sin diferencias; sin `git add`, commit, push ni cambio de rama. |

### Pendientes y siguiente acción

- Ejecutar las unitarias de Compras con `-m:2` en un entorno que permita el socket local de VSTest; después Claude ejecuta la suite completa e integración según la revisión.
- No se hizo revisión visual en navegador, envío real, despliegue, aceptación del cliente ni cambios de otras fichas. La verificación visual e integración permanecen **Por confirmar**.
- Este resumen es una nota operativa del worktree; no forma parte del producto.
