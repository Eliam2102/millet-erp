# ADM08 · Centro de costo heredado del departamento

Fecha de trabajo: 09-oct-2026 (America/Merida). Función F1-ADM-08, P203/P200.

**Estado:** implementación local en `fix/ADM08-ceco-heredado-departamento`; verificación local de compilación/unitarias/frontend. Integración PostgreSQL, revisión visual en runtime, equivalencias definitivas V49 y aceptación de Millet **Por confirmar**. Sin commit, push, publicación ni despliegue.

## Resultado y decisiones

- Catálogo de equivalencias empresa/sucursal/departamento → Dim1/Dim2, administrable en **Centros de costo → Configuración → Centro de costo por departamento** (`/centros-costo/configuracion`). Reutiliza permiso de administración del catálogo, alcance territorial, versión `If-Match`, idempotencia, motivo y bitácora compartida.
- Semilla DEMO opt-in, sólo en ambiente no productivo, para departamentos/sucursales del seed. Etiqueta exacta: **DEMO · por validar con Laura (V49) · equivalencia ficticia, no aprobada**. No reemplaza equivalencias existentes. No se crean parámetros/permisos ni IDs fijos nuevos.
- La línea de RQ hereda el centro del departamento del requisitante. Sin alcance para elegir, UI de solo lectura y rechazo de alteraciones en backend. Con alcance, permite centros superiores o máquinas autorizadas. Se mantiene un único `CentroCostoId`; máquina opcional.
- Asignación existente congelada en Dim3: se ofrecen sólo esas máquinas activas y sus ancestros Dim1/Dim2. No se concede elección sobre máquinas hermanas. El permiso de alcance total conserva su bypass.
- Sin equivalencia ni elección válida: **Tu departamento no tiene centro de costo asignado; pídelo a Contabilidad**. La equivalencia tiene prioridad sobre la única opción del alcance; sin equivalencia, una única máquina puede prellenarse.
- RQ transmitida congela la estructura; no recalcula la equivalencia. La OC desde RQ, la entrada desde OC y la salida con RQ conservan el centro por línea. El alcance del operador aguas abajo no reemplaza ni invalida una elección legítima del origen; la vigencia se sigue validando donde corresponde.
- OC manual: selector autorizado y abierto a centros activos Dim1/Dim2/Dim3, conservando la captura por proxy.
- Lectura histórica: centros de los tres niveles, incluyendo inactivos y sin filtro de alcance. El adaptador contable existente ya soportaba Dim1/Dim2 y se reutiliza. La recepción, salida y comprobante muestran «Centro de costo» sin exigir máquina.
- [ADR-0062](../decisiones/0062-centro-costo-heredado-departamento.md) sustituye parcialmente ADR-0050 por autoridad P203 y decisión de Eliam del 09-oct. Los documentos de diseño anteriores conservan su historia con aviso de precedencia; manuales de RQ/OC actualizados.

## Contraste con «Qué existe hoy»

1. El catálogo y las asignaciones ya existían y se reutilizaron. La referencia 49 centros/352 equipos corresponde a la propuesta M1; `SiembraTests` actual espera **58 Dim2 y 366 Dim3** tras la reconciliación. No se cambió el universo ni sus conteos.
2. RQ exigía Dim3 elegida y validada; no había equivalencia departamento → centro. Se implementó el comportamiento P203.
3. OC desde RQ copiaba el centro, pero P2 volvía a comprobar el alcance del comprador. Eso contradecía la semántica de dato heredado. Ahora conserva validación de vigencia sin exigir el alcance del operador; duplicación de línea manual mantiene su validación anterior.
4. La salida con RQ ya heredaba desde backend. Las dos variantes de **recepción no copiaban el centro** aunque existía la columna en almacén. Se extendió el contrato de lectura de OC y ambos handlers, además del detalle de recepción.
5. Los empleados tienen departamento opcional y no cubren todos los usuarios/RQ de sistema. Se resuelve el empleado activo del requisitante **en la empresa de la RQ**. Sin ficha, se usa el departamento validado de cabecera; una ficha existente sin departamento no toma prestado ese valor. Pruebas cubren requisitante distinto del capturista y empleado de otra empresa.
6. No se encontró un reporte específico de Fase F por centro de costo implementado en este checkout. Se preserva la identificación/jerarquía y lectura histórica de centros sin máquina; la salida y aceptación de un reporte real siguen **Por confirmar**. No se acredita reportería completa por estas pruebas.
7. La rama solicitada ya estaba creada, limpia al inicio, en `f9da5fdd1befc26fc5627256a458a637098c3858`; `main` local era ancestro. Se trabajó únicamente en este worktree, sin mover HEAD.

## Pruebas nuevas y cobertura

| Caso | Evidencia local / prueba escrita |
|---|---|
| (a) Sin máquina, cargo al departamento | `ADM08CentroCostoTests`: herencia Dim2 sin alcance; frontend prellenado y selector read-only |
| (b) Única opción prellenada | Adapter real con una máquina asignada; función pura de prellenado y prioridad del departamento |
| (c) Líneas con centros diferentes | Resolución con alcance total y selección Dim2/Dim3; endpoints escriben líneas heredadas/elegidas |
| (d) Transmisión congela | Dominio rechaza edición estructural; prueba HTTP escrita verifica transmisión y rechazo de PATCH |
| Sin alcance / con alcance | Rechazo backend de sustitución; centros ancestros y máquinas permitidas, sin hermanas; picker readonly/editable |
| Departamento sin equivalencia | Mensaje exacto; elección explícita sólo con alcance; pruebas HTTP existentes ajustadas |
| RQ → OC → entrada/salida | P2 para copias de RQ a OC; handlers reales de recepción factura/packing list y salida con RQ; copia autoritativa del centro, caller ignorado |
| Centro/padre inactivo | Adapter real rechaza todos los niveles; P2 conserva casos de inactividad; HTTP escrito para padre inactivo |
| Lectura y contabilidad sin máquina | Lectura histórica de Dim1/2/3; nodo Dim2 contable; detalle de recepción resuelve una vez en batch incluso centro inactivo |
| Equivalencia administrable | HTTP escrito: máquina rechazada como equivalencia, alta/cambio, versión obsoleta, falta de If-Match, permiso denegado y auditoría |
| Catálogos compartidos | Fixture ADM08 usa IDs únicos y limpia sus departamentos, asociaciones, grupos y centros; seed original intacto |

Pruebas backend nuevas: `ADM08CentroCostoTests.cs` (**12 casos**), `ADM08CentroCostoHeredadoTests.cs` (**3 casos**), prueba adicional en `ObtenerRecepcionPorIdHandlerTests.cs` (**1 caso**). Pruebas frontend nuevas: `captura.test.ts` (**5**) y `CentroCostoPicker.test.tsx` (**2**), más una regresión de edición readonly en `LineaInlineForm.smoke.test.tsx` (**1**). Integración nueva: `ADM08DepartamentoEndpointsTests.cs` (**4 casos**, escritos/compilados, no ejecutados aquí). Suites previas de validadores, G1.11 y P2 ajustadas donde P203 cambia la regla.

## Resultados de verificación

| Verificación | Resultado |
|---|---|
| `cd backend && dotnet build Millet.sln --disable-build-servers -m:1 -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true` | **0 errores, 0 advertencias**; incluye compilación de proyectos de integración |
| Unitarias Compras | **577 pasaron**, 0 fallos, 0 omitidas |
| Unitarias Almacén | **330 pasaron**, 0 fallos, 0 omitidas |
| Unitarias API | **40 pasaron**, 0 fallos, 0 omitidas |
| Unitarias Contabilidad | **180 pasaron**, 0 fallos, 0 omitidas |
| `npx tsc --noEmit -p tsconfig.json` | Exit 0 |
| `npm run -s typecheck:test` | Exit 0 |
| `npm run -s lint` | Exit 0; **0 errores**, 10 advertencias existentes fuera de los archivos de esta entrega |
| `npx vitest run` | Exit 0; **368 archivos y 2,106 pruebas en verde** |
| `git diff --check` | Sin errores |

El sandbox negó la apertura del socket usado por `dotnet test`/VSTest. Las unitarias se ejecutaron con **el runner real de xUnit `AssemblyRunner.WithoutAppDomain`**, en proceso, contra las DLL del build; total **1,127**, cero fallos/omitidas. No se considera ejecución de integración. El runner temporal no se incorpora como código productivo; en un entorno normal se deben volver a correr los `dotnet test` habituales.

Los eventos de recepción conservan `PublishAsync` antes de `SaveChangesAsync` (ADR-0009). No se introdujeron eventos de integración nuevos en el catálogo de equivalencias. No se modificó `infra/`, `.env*`, catálogo M1, permisos ni `ParametroGlobal`; no se levantaron servidores en 5080/5173.

## No verificado y siguiente acción

- **Claude:** correr `tools/validate-integration-isolated.sh` **completo**, con PostgreSQL desechable y evidencia rojo/verde; incluir nuevos endpoints, G1.11, P2 y P7. La migración se generó y revisó, **no se aplicó a una BD real** aquí. Las pruebas de cadena por tramos no acreditan un evento completo persistido en el ambiente real.
- **Revisión visual:** la política del navegador bloqueó abrir el HTML local de la vista aislada. No se eludió ese bloqueo. Sin screenshot acreditado ni prueba visual en runtime; el selector sí cuenta con pruebas automatizadas readonly/editable.
- **Laura/V49:** validar equivalencias, centros vigentes y datos canónicos; fecha prevista 16-oct según el paquete, cumplimiento **Por confirmar**. Millet debe probar los cuatro casos en vivo. La semilla no acredita aprobación.
- **Obsidian:** [borrador local de actualización](ADM08-actualizacion-Obsidian-borrador.md) preparado; la bóveda queda fuera de los permisos de escritura de esta sesión. No se ha aplicado ni sincronizado.
- **Reportería real por centro:** confirmar reporte/escenario y verificar acumulados con línea Dim1/Dim2 sin máquina en el entorno que lo implemente.

## Mensaje de commit propuesto (no ejecutado)

```text
fix(centros-costo): heredar el centro del departamento en compras (ADM08)

Aplica P203 con máquina opcional y elección según alcance.
Agrega equivalencias auditadas y DEMO por validar con Laura (V49).
Conserva el centro en RQ, OC y movimientos; reemplaza parcialmente ADR-0050.
```

## Archivos cambiados

El listado completo incluye nuevos y modificados; [evidencia local](evidencia/ADM08/verificacion.md) conservada junto al informe.


<!-- INVENTARIO ADM08 -->

**Backend y migración**

- [backend/src/Almacen/Application/Recepciones/RecepcionQueries.cs](../../backend/src/Almacen/Application/Recepciones/RecepcionQueries.cs)
- [backend/src/Almacen/Application/Recepciones/RegistrarRecepcionConFacturaCommand.cs](../../backend/src/Almacen/Application/Recepciones/RegistrarRecepcionConFacturaCommand.cs)
- [backend/src/Almacen/Application/Recepciones/RegistrarRecepcionConPackingListCommand.cs](../../backend/src/Almacen/Application/Recepciones/RegistrarRecepcionConPackingListCommand.cs)
- [backend/src/Almacen/Domain/Ports/ICentroCostoReadPort.cs](../../backend/src/Almacen/Domain/Ports/ICentroCostoReadPort.cs)
- [backend/src/Almacen/Domain/Ports/IComprasOcReadPort.cs](../../backend/src/Almacen/Domain/Ports/IComprasOcReadPort.cs)
- [backend/src/Api/Endpoints/CentrosCosto/DepartamentoCentrosCostoEndpoints.cs](../../backend/src/Api/Endpoints/CentrosCosto/DepartamentoCentrosCostoEndpoints.cs)
- [backend/src/Api/Endpoints/Compras/LineasEndpoints.cs](../../backend/src/Api/Endpoints/Compras/LineasEndpoints.cs)
- [backend/src/Api/Program.cs](../../backend/src/Api/Program.cs)
- [backend/src/Api/Web/ADM08DesignTimeDbContext.cs](../../backend/src/Api/Web/ADM08DesignTimeDbContext.cs)
- [backend/src/CentrosCosto/Application/Departamentos/CentroCostoCapturaRegla.cs](../../backend/src/CentrosCosto/Application/Departamentos/CentroCostoCapturaRegla.cs)
- [backend/src/CentrosCosto/Application/Departamentos/EquivalenciasCommands.cs](../../backend/src/CentrosCosto/Application/Departamentos/EquivalenciasCommands.cs)
- [backend/src/CentrosCosto/Application/PublicPorts/ICentroCostoCapturaPort.cs](../../backend/src/CentrosCosto/Application/PublicPorts/ICentroCostoCapturaPort.cs)
- [backend/src/CentrosCosto/Application/PublicPorts/IDim3ElegibilidadPort.cs](../../backend/src/CentrosCosto/Application/PublicPorts/IDim3ElegibilidadPort.cs)
- [backend/src/CentrosCosto/Domain/DepartamentoCentroCosto.cs](../../backend/src/CentrosCosto/Domain/DepartamentoCentroCosto.cs)
- [backend/src/CentrosCosto/Infrastructure/DependencyInjection.cs](../../backend/src/CentrosCosto/Infrastructure/DependencyInjection.cs)
- [backend/src/CentrosCosto/Infrastructure/Persistence/CentrosCostoDbContext.cs](../../backend/src/CentrosCosto/Infrastructure/Persistence/CentrosCostoDbContext.cs)
- [backend/src/CentrosCosto/Infrastructure/Persistence/Configurations/DepartamentoCentroCostoConfiguration.cs](../../backend/src/CentrosCosto/Infrastructure/Persistence/Configurations/DepartamentoCentroCostoConfiguration.cs)
- [backend/src/CentrosCosto/Infrastructure/Persistence/Migrations/20261010001748_ADM08EquivalenciaDepartamentoCentroCosto.Designer.cs](../../backend/src/CentrosCosto/Infrastructure/Persistence/Migrations/20261010001748_ADM08EquivalenciaDepartamentoCentroCosto.Designer.cs)
- [backend/src/CentrosCosto/Infrastructure/Persistence/Migrations/20261010001748_ADM08EquivalenciaDepartamentoCentroCosto.cs](../../backend/src/CentrosCosto/Infrastructure/Persistence/Migrations/20261010001748_ADM08EquivalenciaDepartamentoCentroCosto.cs)
- [backend/src/CentrosCosto/Infrastructure/Persistence/Migrations/CentrosCostoDbContextModelSnapshot.cs](../../backend/src/CentrosCosto/Infrastructure/Persistence/Migrations/CentrosCostoDbContextModelSnapshot.cs)
- [backend/src/CentrosCosto/Infrastructure/PublicAdapters/CentroCostoCapturaAdapter.cs](../../backend/src/CentrosCosto/Infrastructure/PublicAdapters/CentroCostoCapturaAdapter.cs)
- [backend/src/CentrosCosto/Infrastructure/PublicAdapters/CentroCostoCatalogoLectura.cs](../../backend/src/CentrosCosto/Infrastructure/PublicAdapters/CentroCostoCatalogoLectura.cs)
- [backend/src/CentrosCosto/Infrastructure/PublicAdapters/Dim3ElegibilidadAdapter.cs](../../backend/src/CentrosCosto/Infrastructure/PublicAdapters/Dim3ElegibilidadAdapter.cs)
- [backend/src/CentrosCosto/Infrastructure/PublicAdapters/Dim3ReadAdapter.cs](../../backend/src/CentrosCosto/Infrastructure/PublicAdapters/Dim3ReadAdapter.cs)
- [backend/src/CentrosCosto/Infrastructure/Seed/ADM08EquivalenciasDemoHostedService.cs](../../backend/src/CentrosCosto/Infrastructure/Seed/ADM08EquivalenciasDemoHostedService.cs)
- [backend/src/Compras/Application/CentroCostoRqResolver.cs](../../backend/src/Compras/Application/CentroCostoRqResolver.cs)
- [backend/src/Compras/Application/EnviarAAutorizacion/EnviarAAutorizacionHandler.cs](../../backend/src/Compras/Application/EnviarAAutorizacion/EnviarAAutorizacionHandler.cs)
- [backend/src/Compras/Application/Lineas/ActualizarLinea/ActualizarLineaHandler.cs](../../backend/src/Compras/Application/Lineas/ActualizarLinea/ActualizarLineaHandler.cs)
- [backend/src/Compras/Application/Lineas/ActualizarLinea/ActualizarLineaValidator.cs](../../backend/src/Compras/Application/Lineas/ActualizarLinea/ActualizarLineaValidator.cs)
- [backend/src/Compras/Application/Lineas/AgregarLinea/AgregarLineaHandler.cs](../../backend/src/Compras/Application/Lineas/AgregarLinea/AgregarLineaHandler.cs)
- [backend/src/Compras/Application/Lineas/AgregarLinea/AgregarLineaValidator.cs](../../backend/src/Compras/Application/Lineas/AgregarLinea/AgregarLineaValidator.cs)
- [backend/src/Compras/Application/ObtenerRequisicionPorId/ObtenerCentroCostoCapturaQuery.cs](../../backend/src/Compras/Application/ObtenerRequisicionPorId/ObtenerCentroCostoCapturaQuery.cs)
- [backend/src/Compras/Application/Oc/CrearOrdenCompraDesdeRequisicion/CrearOrdenCompraDesdeRequisicionHandler.cs](../../backend/src/Compras/Application/Oc/CrearOrdenCompraDesdeRequisicion/CrearOrdenCompraDesdeRequisicionHandler.cs)
- [backend/src/Compras/Application/Oc/DuplicarOrdenCompra/DuplicarOrdenCompraHandler.cs](../../backend/src/Compras/Application/Oc/DuplicarOrdenCompra/DuplicarOrdenCompraHandler.cs)
- [backend/src/Compras/Application/Oc/Lineas/AgregarLineaDesdeRequisicion/AgregarLineaDesdeRequisicionHandler.cs](../../backend/src/Compras/Application/Oc/Lineas/AgregarLineaDesdeRequisicion/AgregarLineaDesdeRequisicionHandler.cs)
- [backend/src/Compras/Infrastructure/PublicAdapters/ComprasOcReadAdapter.cs](../../backend/src/Compras/Infrastructure/PublicAdapters/ComprasOcReadAdapter.cs)

**Pruebas backend**

- [backend/tests/Almacen.UnitTests/Recepciones/ADM08CentroCostoHeredadoTests.cs](../../backend/tests/Almacen.UnitTests/Recepciones/ADM08CentroCostoHeredadoTests.cs)
- [backend/tests/Almacen.UnitTests/Recepciones/ObtenerRecepcionPorIdHandlerTests.cs](../../backend/tests/Almacen.UnitTests/Recepciones/ObtenerRecepcionPorIdHandlerTests.cs)
- [backend/tests/Api.IntegrationTests/CentrosCosto/ADM08DepartamentoEndpointsTests.cs](../../backend/tests/Api.IntegrationTests/CentrosCosto/ADM08DepartamentoEndpointsTests.cs)
- [backend/tests/Api.IntegrationTests/Compras/LineasEndpointsTests.cs](../../backend/tests/Api.IntegrationTests/Compras/LineasEndpointsTests.cs)
- [backend/tests/Api.IntegrationTests/Compras/Oc/P2FirmasYSaldoTests.cs](../../backend/tests/Api.IntegrationTests/Compras/Oc/P2FirmasYSaldoTests.cs)
- [backend/tests/Compras.UnitTests/Application/ADM08CentroCostoTests.cs](../../backend/tests/Compras.UnitTests/Application/ADM08CentroCostoTests.cs)
- [backend/tests/Compras.UnitTests/Application/Lineas/ActualizarLineaValidatorTests.cs](../../backend/tests/Compras.UnitTests/Application/Lineas/ActualizarLineaValidatorTests.cs)
- [backend/tests/Compras.UnitTests/Application/Lineas/AgregarLineaValidatorTests.cs](../../backend/tests/Compras.UnitTests/Application/Lineas/AgregarLineaValidatorTests.cs)

**Frontend y pruebas**

- [frontend/src/features/almacen/api/types.ts](../../frontend/src/features/almacen/api/types.ts)
- [frontend/src/features/almacen/components/NuevaSalidaSheet.tsx](../../frontend/src/features/almacen/components/NuevaSalidaSheet.tsx)
- [frontend/src/features/almacen/components/impresion/ComprobanteSalidaDocument.tsx](../../frontend/src/features/almacen/components/impresion/ComprobanteSalidaDocument.tsx)
- [frontend/src/features/almacen/pages/RecepcionDetallePage.tsx](../../frontend/src/features/almacen/pages/RecepcionDetallePage.tsx)
- [frontend/src/features/almacen/pages/SalidaDetallePage.tsx](../../frontend/src/features/almacen/pages/SalidaDetallePage.tsx)
- [frontend/src/features/centros-costo/api/captura.ts](../../frontend/src/features/centros-costo/api/captura.ts)
- [frontend/src/features/centros-costo/components/CentroCostoPicker.test.tsx](../../frontend/src/features/centros-costo/components/CentroCostoPicker.test.tsx)
- [frontend/src/features/centros-costo/components/CentroCostoPicker.tsx](../../frontend/src/features/centros-costo/components/CentroCostoPicker.tsx)
- [frontend/src/features/centros-costo/components/EquivalenciasDepartamento.tsx](../../frontend/src/features/centros-costo/components/EquivalenciasDepartamento.tsx)
- [frontend/src/features/centros-costo/lib/captura.test.ts](../../frontend/src/features/centros-costo/lib/captura.test.ts)
- [frontend/src/features/centros-costo/lib/captura.ts](../../frontend/src/features/centros-costo/lib/captura.ts)
- [frontend/src/features/centros-costo/pages/ConfiguracionCentrosCostoPage.tsx](../../frontend/src/features/centros-costo/pages/ConfiguracionCentrosCostoPage.tsx)
- [frontend/src/features/compras/components/LineaInlineForm.idempotency.test.tsx](../../frontend/src/features/compras/components/LineaInlineForm.idempotency.test.tsx)
- [frontend/src/features/compras/components/LineaInlineForm.smoke.test.tsx](../../frontend/src/features/compras/components/LineaInlineForm.smoke.test.tsx)
- [frontend/src/features/compras/components/LineaInlineForm.tsx](../../frontend/src/features/compras/components/LineaInlineForm.tsx)
- [frontend/src/features/compras/components/LineaInlineForm.um.test.tsx](../../frontend/src/features/compras/components/LineaInlineForm.um.test.tsx)
- [frontend/src/features/compras/components/ListaLineas.tsx](../../frontend/src/features/compras/components/ListaLineas.tsx)
- [frontend/src/features/compras/ordenes/components/LineaInlineFormOc.cc.test.tsx](../../frontend/src/features/compras/ordenes/components/LineaInlineFormOc.cc.test.tsx)
- [frontend/src/features/compras/ordenes/components/LineaInlineFormOc.idempotency.test.tsx](../../frontend/src/features/compras/ordenes/components/LineaInlineFormOc.idempotency.test.tsx)
- [frontend/src/features/compras/ordenes/components/LineaInlineFormOc.tsx](../../frontend/src/features/compras/ordenes/components/LineaInlineFormOc.tsx)
- [frontend/src/features/compras/ordenes/schemas/agregar-linea-manual.test.ts](../../frontend/src/features/compras/ordenes/schemas/agregar-linea-manual.test.ts)
- [frontend/src/features/compras/ordenes/schemas/agregar-linea-manual.ts](../../frontend/src/features/compras/ordenes/schemas/agregar-linea-manual.ts)
- [frontend/src/features/compras/schemas/linea.ts](../../frontend/src/features/compras/schemas/linea.ts)

**Documentación y evidencia**

- [docs/decisiones/0050-consumo-centro-costo-maquina-dim3.md](../../docs/decisiones/0050-consumo-centro-costo-maquina-dim3.md)
- [docs/decisiones/0062-centro-costo-heredado-departamento.md](../../docs/decisiones/0062-centro-costo-heredado-departamento.md)
- [docs/decisiones/README.md](../../docs/decisiones/README.md)
- [docs/entrega/modulos/ordenes-compra-usuario.md](../../docs/entrega/modulos/ordenes-compra-usuario.md)
- [docs/entrega/modulos/requisiciones-usuario.md](../../docs/entrega/modulos/requisiciones-usuario.md)
- [docs/entregas/ADM08-actualizacion-Obsidian-borrador.md](../../docs/entregas/ADM08-actualizacion-Obsidian-borrador.md)
- [docs/entregas/ADM08-ceco-heredado-departamento.md](../../docs/entregas/ADM08-ceco-heredado-departamento.md)
- [docs/entregas/evidencia/ADM08/backend-build.txt](../../docs/entregas/evidencia/ADM08/backend-build.txt)
- [docs/entregas/evidencia/ADM08/backend-unitarias.txt](../../docs/entregas/evidencia/ADM08/backend-unitarias.txt)
- [docs/entregas/evidencia/ADM08/frontend-lint.txt](../../docs/entregas/evidencia/ADM08/frontend-lint.txt)
- [docs/entregas/evidencia/ADM08/frontend-vitest.txt](../../docs/entregas/evidencia/ADM08/frontend-vitest.txt)
- [docs/entregas/evidencia/ADM08/verificacion.md](../../docs/entregas/evidencia/ADM08/verificacion.md)
- [docs/modulos/centros-costo/00-levantamiento.md](../../docs/modulos/centros-costo/00-levantamiento.md)
- [docs/modulos/centros-costo/01-diseno.md](../../docs/modulos/centros-costo/01-diseno.md)
- [docs/modulos/centros-costo/05-frontend-diseno.md](../../docs/modulos/centros-costo/05-frontend-diseno.md)
- [docs/modulos/centros-costo/08-consumo-compras.md](../../docs/modulos/centros-costo/08-consumo-compras.md)
- [docs/modulos/centros-costo/09-adm08-analisis-plan.md](../../docs/modulos/centros-costo/09-adm08-analisis-plan.md)

