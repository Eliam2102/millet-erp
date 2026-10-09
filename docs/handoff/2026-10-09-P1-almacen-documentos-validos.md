# P1 · Almacén solo acepta documentos válidos

Fecha: 09-oct-2026. Rama: `fix/P1-almacen-documentos-validos`.
Estado: implementación local; validación PostgreSQL y aceptación operativa **Por confirmar**.
Sin commits ni push. Trabajo conservado y completado en el worktree actual.

## Continuación del 09-oct · corrección de la adenda PostgreSQL

Se revisaron los cambios pendientes contra los cinco bloques de la ficha y el código actual (HEAD `4bf1690`). Las reglas, los caminos válidos, la UI y las migraciones P1 ya estaban implementados; se conservaron. En esta continuación se corrigió la preparación de `P1DocumentosValidosTests.Rq_cancelada_o_exceso_autorizado_devuelve_422_sin_movimiento`.

El fixture anterior registraba cubrimiento de 10 unidades de almacén y 0 de compra; después forzaba con reflexión 4 de almacén, 6 recibidas y 5 entregadas, dejando compra en 0. La restricción `ck_requisicion_lineas_recibida_no_excede_compra` rechazaba correctamente recibido = 6 > compra = 0. La preparación ahora usa `RegistrarCubrimiento(4, 6)`, `RegistrarRecepcion(6)` y `RegistrarEntrega(5)`. Ambas variantes mantienen disponible = 5 mediante la propiedad del dominio y verifican compra = recibido = 6. No se modificaron la restricción, las migraciones ni el código de producción para resolver este rojo.

| Verificación actual | Resultado |
|---|---|
| Build de `Millet.sln`, sin restore, con compilación secuencial | 0 errores y 0 advertencias. `/tmp/p1-build-09oct-revision.log`. |
| Unitarias Almacén / Compartido / Compras / API | 279/279, 79/79, 540/540 y 16/16; 0 fallidas y 0 omitidas. `/tmp/p1-unitarias-09oct-revision.log`. |
| Preparación exacta de las dos variantes de la RQ, extraída del test actual y ejecutada contra los assemblies compilados | Ambas válidas: almacén = 4, compra = 6, recibido = 6, entregado = 5, disponible = 5. `/tmp/p1-fixture-lite-09oct-revision.log`. No sustituye el test HTTP/PostgreSQL. |
| TypeScript (`tsconfig.json` y `tsconfig.app.json`) y `typecheck:test` | Código de salida 0. |
| Lint global | Código de salida 0; 0 errores y 10 advertencias en Administración. `/tmp/p1-lint-09oct-revision.log`. |
| Vitest completo con `--pool=forks --maxWorkers=2 --no-file-parallelism` | Código de salida 0; 340/340 archivos y 1999/1999 pruebas; duración 730.00 s. `/tmp/p1-vitest-09oct-revision.log`. |
| `git diff --check` | Sin errores. |
| Integración PostgreSQL | Por confirmar tras la corrección. El sandbox deniega acceso al socket de Docker; Claude debe repetir completo `tools/validate-integration-isolated.sh`. |

VSTest volvió a fallar al abrir su socket (`SocketException (13): Permission denied`); las cuatro suites unitarias se ejecutaron mediante el runner real de xUnit en proceso, sin modificar ni omitir sus pruebas. El ensayo adicional del fixture no accede a PostgreSQL ni al endpoint.

La adenda de Eliam/Claude reporta una corrida anterior con A+W 7/7, Compras 138/138 y API 872/874. Ese dato proviene del mensaje de la tarea: no se releyó el log externo ni se declara verde PostgreSQL después de este cambio.

Se reconsultaron las fuentes oficiales de PROFEDET e INE enlazadas abajo para el calendario precargado; conserva la marca «dato a validar por Millet». La aceptación del lunes 12-oct y el ensayo visual de listado/detalle/filtros siguen Por confirmar.

## Cambio preparado para la bóveda (sin aplicar)

Destino: `11 Manual y plan de construcción/Módulo 04 · Almacén y Materia Prima.md` y Bitácora. Conservar las tablas históricas y añadir una actualización del 09-oct: la auditoría sobre `main` `4bf1690` encontró documentos inválidos aceptados; P1 implementa localmente las correcciones de ALM-02/06/07/08/11 y la elegibilidad del centro de costo del vale. D7 y D8 proceden de las decisiones explícitas de Eliam en esta ficha. Build, cuatro suites unitarias y controles completos del frontend verificados en esta continuación; integración completa posterior a la corrección, demo y aceptación Por confirmar. Siguiente acción: corrida aislada de Claude y ensayo con Millet. La bóveda está fuera de las raíces de escritura de este sandbox; esta nota queda preparada localmente para su incorporación.

## Reglas implementadas

| Función | Comportamiento |
|---|---|
| ALM-02 | Ambos handlers distinguen OC inexistente y no autorizada; exigen línea de OC, artículo correcto y costo positivo de la OC. Agrupan las cantidades de un mismo envío por línea antes de comprobar saldo y tolerancia. Los rechazos ocurren antes de crear movimiento o publicar evento. |
| ALM-06 | La RQ debe existir y estar Autorizada o EnSurtido. Cada línea debe referenciar su línea de RQ y artículo correcto. Se agrupa por línea y se usa `LineaRequisicion.CantidadPendienteEntregar`, mediante el adaptador, como fuente de disponibilidad. |
| ALM-07 / ADM-08 | Plazo de 48 horas hábiles desde FechaMovimiento; calendario compartido, lunes a viernes sin festivos; centro de costo validado mediante IDim3ElegibilidadPort, con alcance. Regularización exige una RQ válida con cantidades disponibles suficientes para todos los artículos. |
| ALM-08 | Devolución interna conserva la línea de salida y acumula lo devuelto. Devolución a proveedor exige recepción/línea y proveedor de la OC, acumula devoluciones y toma costo/unidad de la recepción. El evento para CxP utiliza ese costo. |
| ALM-11 | Periodo cerrado bloquea baja por daño, reincorporación, salida a proveedor y aplicación de conteos, además de los caminos ya protegidos. Conteos no terminales que afectan al mes y vales registrados pendientes impiden el cierre. |

Las salidas exponen PendienteRegularizacion, FechaLimiteRegularizacion y Vencido en listado/detalle. La UI usa Badge con «Vencido», «Por regularizar» o «Regularizado», y filtros para pendientes/vencidos.

## Decisiones y diferencias respecto al corte de auditoría

- D7: los vales pendientes del mes impiden cerrar.
- D8: FechaMovimiento es DateOnly; se toma medianoche de la zona horaria global. Cada día hábil aporta 24 horas; no se inventa una jornada por turnos. Ejemplo: viernes 13-nov-2026 + 48 horas hábiles, con lunes 16 festivo, vence miércoles 18 a medianoche local.
- Fórmula vigente: el adaptador lee CantidadPendienteEntregar del dominio de Compras; Almacén no replica el cálculo.
- El calendario se declara en SharedKernel y se implementa en Compartido. El puerto propio de elegibilidad se adapta en el composition root para evitar referencias circulares.
- El costo enviado por clientes anteriores en la devolución se acepta por compatibilidad, pero se ignora; en UI se presenta como campo de solo lectura.
- Los acumulados de devolución incluyen las anteriores; se serializa por origen mediante una transacción y un advisory lock en PostgreSQL.
- Para referencias históricas sin vínculo por línea, se cuenta lo devuelto por artículo de forma conservadora. No se inventan correspondencias históricas.
- Se encontró un camino adicional de ajustes (AplicarConteoHandler) sin protección de periodo: también quedó protegido.
- Un conteo iniciado antes de su fecha planificada también puede afectar al mes; el cierre considera FechaInicio además de FechaPlanificada.
- La prueba del adaptador que carga Money se trasladó de EF InMemory a PostgreSQL, conservando la comprobación de la fórmula.
- Se corrigió una aserción de la corrida interrumpida que rechazaba incorrectamente una colección vacía al filtrar un único vale vencido.
- AGENTS.md no existe en la raíz; se aplicaron las instrucciones entregadas en el mensaje y frontend/AGENTS.md, además de CLAUDE.md, CONTRIBUTING.md y el design system.

## Migraciones P1

1. `20261009150105_P1CalendarioHabilFestivos` (Compartido): agrega system.dias-festivos, editable por el mecanismo existente `/api/v1/admin/parametros` con `admin.parametros.editar` y la bitácora de ParametroGlobal.
2. `20261009150405_P1AlmacenDocumentosValidos` (Almacén): agrega LineaOcId y LineaSalidaOrigenId con índices. Recalcula el plazo de vales históricos pendientes desde su fecha usando los festivos y zona globales.

El orden de tools/migration-contexts.txt coloca Compartido antes de Almacén. Las migraciones no se aplicaron a una base real en esta sesión. El recálculo histórico requiere la validación aislada de PostgreSQL.

El seed 2026/2027 conserva la leyenda «dato a validar por Millet». Se contrastó con [PROFEDET, descansos obligatorios](https://www.profedet.gob.mx/micrositio/index.php/dias-de-descanso) y con el [INE, jornada federal de 6-jun-2027](https://portal.ine.mx/voto-y-elecciones/elecciones-2027/). Las fechas electorales locales adicionales permanecen configurables para su validación por Millet.

## Cobertura de pruebas

| Archivo / suite | Reglas cubiertas |
|---|---|
| Almacen.UnitTests/P1/DocumentosValidosTests | OC inexistente y estados inválidos en ambas variantes; línea obligatoria y artículo; exceso sumando líneas; costo cero; recepciones válidas; RQ inválida, línea/artículo y exceso; salida válida; RQ regularizadora inválida/válida; fin de semana, festivo y cambio de año. |
| Almacen.UnitTests/P1/DevolucionesYCierreTests | Segunda devolución interna excesiva; recepción/proveedor/cantidad/costo de devolución a proveedor; costo del evento para nota de cargo; tres caminos en periodo cerrado y caminos válidos; conteos no terminales y terminales; inicio previo a planificación; vales pendientes y cierre tras regularización; ceco inválido y vale válido; ajuste por conteo en periodo cerrado. |
| Almacen.UnitTests/Salidas | DTO de listado/detalle, vencimiento y filtros. |
| Compartido.UnitTests/Administracion/CalendarioHabilTests | Festivos modificados por el handler existente; parámetros inválidos no se guardan. |
| Api.UnitTests/Almacen/P1CentroCostoValeTests | Centro inactivo, inexistente o fuera de alcance; activo permitido y aplicación del alcance. |
| Api.IntegrationTests/Almacen/P1DocumentosValidosTests | HTTP 422 para OC inexistente/cancelada/borrador en ambas variantes y RQ inexistente/cancelada/exceso acumulado; ausencia de movimientos. Adaptador real conserva estado y disponibilidad del dominio. |
| Api.IntegrationTests/Almacen/SalidaUbicacionPorLineaTests | Fixture con RQ real autorizada/en surtido y limpieza; ya no depende de RqPortNulo. |
| Frontend estado-regularizacion.test.ts y devolucion.test.ts | Labels y variantes de regularización; recepción/línea obligatoria y UUID v7. |

## Validación de la corrida anterior (histórico)

| Verificación | Resultado |
|---|---|
| `dotnet build Millet.sln --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false` | 0 errores, 0 advertencias. Log `/tmp/p1-build-entrega.log`. |
| Unitarias Almacén (xUnit en proceso) | 279/279, 0 omitidas. |
| Unitarias Compartido (xUnit en proceso) | 79/79, 0 omitidas. |
| Unitarias Compras (xUnit en proceso) | 540/540, 0 omitidas. |
| Unitarias API (xUnit en proceso) | 16/16, 0 omitidas. |
| `npx tsc --noEmit -p tsconfig.json` y comprobación adicional `tsconfig.app.json` | Código de salida 0. |
| `npm run -s typecheck:test` | Código de salida 0. |
| `npm run -s lint` | Código de salida 0: 0 errores y 10 advertencias existentes en Administración. |
| Vitest de Almacén | 22 archivos y 116 pruebas correctas. |
| Primera corrida `npx vitest run` | 323 archivos correctos y 17 con fallos: 1977 pruebas correctas y 22 fallidas (principalmente tiempos de espera). |
| Repetición global con menor concurrencia | Código de salida 0: 340/340 archivos y 1999/1999 pruebas correctas, duración 665.78 s. Comando `npx vitest run --pool=forks --maxWorkers=2 --no-file-parallelism`, log `/tmp/p1-vitest-entrega.log`. |
| `git diff --check` | Sin errores. |
| Integración PostgreSQL | No ejecutada; pendiente de Claude. |

VSTest no puede abrir el socket de comunicación en este sandbox (`SocketException (13): Permission denied`). Se usó `Xunit.Runners.AssemblyRunner.WithoutAppDomain` para descubrir/ejecutar las pruebas reales en proceso, con Fact/Theory y el ciclo de vida de xUnit. El ejecutor se ubicó en un directorio bin ignorado para que las auditorías que parten de AppContext.BaseDirectory encuentren el repo; los archivos de pruebas no se alteraron para eludirlas. No se omitieron pruebas de estas cuatro suites.

La primera corrida global de Vitest coincidió con otras verificaciones y falló principalmente por tiempos de espera fuera de Almacén. Se repitió completa con menor concurrencia y terminó en verde sin modificar esas otras áreas para resolver la corrida. El resultado confirma la suite con las condiciones de ejecución indicadas; la primera corrida fallida se conserva como evidencia.

## Pendientes y siguiente acción

- Claude debe correr **completo** `tools/validate-integration-isolated.sh` y entregar rojo/verde. Este sandbox no dispone de Docker.
- Verificar migraciones/backfill, triggers, transacciones de devoluciones y Outbox en PostgreSQL desechable.
- Ensayar visualmente listado/detalle, filtros y devolución a proveedor con los datos del entorno autorizado; no se levantaron servidores en 5080 ni 5173.
- Millet debe validar el calendario precargado y los flujos en vivo del lunes 12-oct. La aceptación operativa permanece Por confirmar.
- El servicio de notificaciones continúa NoOp por alcance explícito de la ficha.

Mensaje de commit propuesto (no ejecutado): `fix(almacen): exigir documentos válidos y respetar el cierre mensual`.

## Inventario de archivos cambiados

- `backend/src/Almacen/Application/Cierre/EjecutarCierreMensualCommand.cs`
- `backend/src/Almacen/Application/Conteos/AprobacionCommands.cs`
- `backend/src/Almacen/Application/DevolucionesInternas/DevolucionInternaCommands.cs`
- `backend/src/Almacen/Application/DevolucionesProveedor/DevolucionOrigenLock.cs`
- `backend/src/Almacen/Application/DevolucionesProveedor/DevolucionProveedorCommands.cs`
- `backend/src/Almacen/Application/DevolucionesProveedor/DevolucionProveedorOrigenGuard.cs`
- `backend/src/Almacen/Application/EstadoDocumentoTexto.cs`
- `backend/src/Almacen/Application/Integration/OcDevolucionRegistradaIntegrationEvent.cs`
- `backend/src/Almacen/Application/Recepciones/RecepcionOcGuard.cs`
- `backend/src/Almacen/Application/Recepciones/RegistrarRecepcionConFacturaCommand.cs`
- `backend/src/Almacen/Application/Recepciones/RegistrarRecepcionConPackingListCommand.cs`
- `backend/src/Almacen/Application/Salidas/RegistrarSalidaConRequisicionCommand.cs`
- `backend/src/Almacen/Application/Salidas/SalidaQueries.cs`
- `backend/src/Almacen/Application/Salidas/SalidaRqGuard.cs`
- `backend/src/Almacen/Application/Vales/RegistrarSalidaPorValeCommand.cs`
- `backend/src/Almacen/Domain/Movimientos/LineaMovimiento.cs`
- `backend/src/Almacen/Domain/Movimientos/MovimientoInventario.cs`
- `backend/src/Almacen/Domain/Ports/ICentroCostoElegibilidadPort.cs`
- `backend/src/Almacen/Domain/Ports/IComprasOcReadPort.cs`
- `backend/src/Almacen/Domain/Ports/IComprasRequisicionReadPort.cs`
- `backend/src/Almacen/Infrastructure/Persistence/Configurations/LineaMovimientoConfiguration.cs`
- `backend/src/Almacen/Infrastructure/Persistence/Migrations/20261009150405_P1AlmacenDocumentosValidos.Designer.cs`
- `backend/src/Almacen/Infrastructure/Persistence/Migrations/20261009150405_P1AlmacenDocumentosValidos.cs`
- `backend/src/Almacen/Infrastructure/Persistence/Migrations/AlmacenDbContextModelSnapshot.cs`
- `backend/src/Api/Adapters/AlmacenCentroCostoElegibilidadAdapter.cs`
- `backend/src/Api/Endpoints/Almacen/Salidas/SalidasEndpoints.cs`
- `backend/src/Api/Program.cs`
- `backend/src/Compartido/Application/Administracion/Parametros/ActualizarParametroCommand.cs`
- `backend/src/Compartido/Infrastructure/Calendario/CalendarioHabilService.cs`
- `backend/src/Compartido/Infrastructure/Migrations/Compartido/20261009150105_P1CalendarioHabilFestivos.Designer.cs`
- `backend/src/Compartido/Infrastructure/Migrations/Compartido/20261009150105_P1CalendarioHabilFestivos.cs`
- `backend/src/Compartido/Infrastructure/Migrations/Compartido/CompartidoDbContextModelSnapshot.cs`
- `backend/src/Compartido/Infrastructure/Persistence/CompartidoDbContext.cs`
- `backend/src/Compras/Infrastructure/PublicAdapters/ComprasOcReadAdapter.cs`
- `backend/src/Compras/Infrastructure/PublicAdapters/ComprasRequisicionReadAdapter.cs`
- `backend/src/SharedKernel/Application/Calendario/CalendarioHabil.cs`
- `backend/src/SharedKernel/Application/Calendario/ICalendarioHabil.cs`
- `backend/tests/Almacen.UnitTests/Decimales/DecimalesGuardWiringTests.cs`
- `backend/tests/Almacen.UnitTests/DevolucionesProveedor/RegistrarSalidaDevolucionAProveedorPr6aTests.cs`
- `backend/tests/Almacen.UnitTests/Movimientos/MovimientoInventarioTests.cs`
- `backend/tests/Almacen.UnitTests/P1/DevolucionesYCierreTests.cs`
- `backend/tests/Almacen.UnitTests/P1/DocumentosValidosTests.cs`
- `backend/tests/Almacen.UnitTests/Recepciones/EventosContablesG16Tests.cs`
- `backend/tests/Almacen.UnitTests/Recepciones/RegistrarRecepcionMultiSubAlmacenTests.cs`
- `backend/tests/Almacen.UnitTests/Salidas/ListarSalidasHandlerTests.cs`
- `backend/tests/Almacen.UnitTests/Salidas/ObtenerSalidaPorIdHandlerTests.cs`
- `backend/tests/Almacen.UnitTests/TestSupport/P1Fixture.cs`
- `backend/tests/Api.IntegrationTests/Administracion/ParametrosEndpointsTests.cs`
- `backend/tests/Api.IntegrationTests/Almacen/P1DocumentosValidosTests.cs`
- `backend/tests/Api.IntegrationTests/Almacen/RecepcionPeriodoContableTests.cs`
- `backend/tests/Api.IntegrationTests/Almacen/SalidaUbicacionPorLineaTests.cs`
- `backend/tests/Api.IntegrationTests/Almacen/TriggerBinExplicitoTests.cs`
- `backend/tests/Api.UnitTests/Almacen/P1CentroCostoValeTests.cs`
- `backend/tests/Compartido.UnitTests/Administracion/CalendarioHabilTests.cs`
- `backend/tests/Compras.UnitTests/PublicAdapters/ComprasRequisicionReadAdapterTests.cs`
- `frontend/src/features/almacen/api/keys.ts`
- `frontend/src/features/almacen/api/types.ts`
- `frontend/src/features/almacen/components/NuevaDevolucionProveedorSheet.tsx`
- `frontend/src/features/almacen/components/RegularizacionValeBadge.tsx`
- `frontend/src/features/almacen/components/impresion/salida-display.test.ts`
- `frontend/src/features/almacen/lib/estado-regularizacion.test.ts`
- `frontend/src/features/almacen/lib/estado-regularizacion.ts`
- `frontend/src/features/almacen/lib/salidas-search-schema.ts`
- `frontend/src/features/almacen/pages/SalidaDetallePage.tsx`
- `frontend/src/features/almacen/pages/SalidasPage.tsx`
- `frontend/src/features/almacen/schemas/devolucion.test.ts`
- `frontend/src/features/almacen/schemas/devolucion.ts`
