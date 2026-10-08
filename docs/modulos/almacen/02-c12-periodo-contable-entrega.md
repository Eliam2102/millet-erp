# C1.2 · Periodo contable real en Almacén

Corte de validación: 08-oct-2026 (trabajo iniciado el 07-oct). Base local verificada: `a72ffc0`, rama
`ficha/C1.2-periodo-consumidores`. Alcance: decisión del 07-oct incluida por
Eliam en el encargo. Implementación local sin commit, push, PR ni integración
en `main`. No acredita cierre de C1.2 completa ni aceptación de Millet.

## Comportamiento y decisiones

- `PeriodoContableReadAdapter` implementa el puerto existente de Almacén y
  consulta `IPeriodoContableConsultaPort` de Contabilidad por fecha. Usa el
  primer día del mes solicitado para resolver siempre un periodo ordinario,
  nunca el 13 de ajustes. No consulta tablas de Contabilidad directamente.
- `EstadoPeriodoContable.AdmiteMovimientos` aplica D9: únicamente admite un
  periodo existente y abierto. Cerrado, no abierto e inexistente bloquean.
  Se reemplazó el registro de DI de Almacén y se eliminó su NoOp sin usos.
- La sobrecarga de `PeriodoCerradoValidator` conserva primero el candado de
  inventario y luego consulta Contabilidad. Se conectaron los cinco handlers:
  recepción con factura, recepción con packing list, salida con requisición,
  salida por vale y devolución interna.
- El rechazo contable es `BusinessRuleException`, que el manejador global
  existente traduce a HTTP 422. Código: `PERIODO_CONTABLE_NO_ADMITE`.
  Mensaje para septiembre:
  «El periodo 2026-09 está cerrado o no está abierto en Contabilidad; no se
  registran movimientos de almacén con esa fecha.»
- D18: abrir o reabrir Contabilidad no elimina el cierre de inventario.
  `EjecutarCierreMensualHandler` conserva sus dependencias y no consulta el
  calendario contable. No se modificaron las firmas de los puertos ni la
  publicación de eventos antes de `SaveChanges`.

## Archivos del cambio

Rutas relativas al repositorio:

- `backend/src/Almacen/Infrastructure/PublicAdapters/PeriodoContableReadAdapter.cs`
- `backend/src/Almacen/Infrastructure/DependencyInjection.cs`
- `backend/src/Almacen/Infrastructure/Stubs/NoOpReadPorts.cs`
- `backend/src/Almacen/Millet.Almacen.csproj` (referencia al contrato de Contabilidad)
- `backend/src/Almacen/Domain/Ports/IPeriodoContableReadPort.cs` (documentación)
- `backend/src/Almacen/Application/Cierre/EjecutarCierreMensualCommand.cs`
- `backend/src/Almacen/Application/Recepciones/RegistrarRecepcionConFacturaCommand.cs`
- `backend/src/Almacen/Application/Recepciones/RegistrarRecepcionConPackingListCommand.cs`
- `backend/src/Almacen/Application/Salidas/RegistrarSalidaConRequisicionCommand.cs`
- `backend/src/Almacen/Application/Vales/RegistrarSalidaPorValeCommand.cs`
- `backend/src/Almacen/Application/DevolucionesInternas/DevolucionInternaCommands.cs`
- `backend/src/Contabilidad/Application/PublicPorts/IPeriodoContableConsultaPort.cs` (pendientes del comentario)
- `backend/src/Facturacion/Infrastructure/Stubs/NoOpReadPorts.cs` y
  `backend/src/Tesoreria/Infrastructure/Stubs/NoOpPeriodoContablePort.cs` (`PLATFORM-TODO(C1.2)`)
- Pruebas unitarias: `Periodos/PeriodoContableReadAdapterTests.cs`,
  `Recepciones/RegistrarRecepcionMultiSubAlmacenTests.cs`,
  `Recepciones/EventosContablesG16Tests.cs`,
  `Decimales/DecimalesGuardWiringTests.cs` y `TestSupport/PeriodoContableStub.cs`,
  bajo `backend/tests/Almacen.UnitTests/`.
- Integración: `PeriodoContableFixture.cs`, `RecepcionPeriodoContableTests.cs`
  y `SalidaUbicacionPorLineaTests.cs`, bajo
  `backend/tests/Api.IntegrationTests/Almacen/`.
- `docs/modulos/almacen/00-levantamiento.md`, `01-diseno.md`, este informe y
  `evidencia-c12-2026-10-08.txt` (salidas de validación local).

## Pruebas y evidencia

Unitarias nuevas/ampliadas:

1. Adaptador: abierto, cerrado, no abierto, inexistente y respuesta incoherente
   «abierto pero inexistente». Verifica fecha y propagación de cancelación.
2. DI: el módulo resuelve el adaptador real y bloquea un periodo cerrado.
3. Recepción de septiembre: código y mensaje exactos; sin movimientos ni eventos.
4. Recepción con Contabilidad abierta e inventario cerrado: sigue bloqueada.
5. Cierre mensual de inventario: funciona sin un calendario contable.
6. Los cinco handlers: rama de periodo no admitido sin movimientos nuevos ni
   eventos, además de sus casos existentes de éxito y contrato de eventos.

Integración escrita para PostgreSQL desechable:

- Recepciones con factura y packing list contra septiembre cerrado del ejercicio
  FIX 2098: HTTP 422, código y mensaje exactos, sin movimiento para la OC de
  prueba. Se cierra enero–septiembre en orden mediante el dominio de Contabilidad.
- Las seis pruebas existentes de salidas usan el adaptador real con marzo de
  2027 abierto en una empresa sintética propia. Se limpia cada calendario por
  su ID al terminar; no se modifica el ejercicio de la demo ni catálogos comunes.

Resultados verificados al cierre de la sesión:

| Comprobación | Resultado |
|---|---|
| `dotnet build backend/Millet.sln --no-restore --disable-build-servers -m:1 -nr:false` | Salida 0; 0 errores, 0 advertencias; incluye los proyectos de integración |
| Unitarias de Almacén | 220 aprobadas, 0 fallidas, 0 omitidas con xUnit en proceso; 14 casos añadidos |
| `npx tsc --noEmit -p tsconfig.json` | Salida 0 |
| `npm run -s typecheck:test` | Salida 0 |
| `npm run -s lint` | Salida 0; 0 errores, 10 advertencias en archivos de frontend sin cambios |
| `npx vitest run --maxWorkers=2` | Salida 1; 330 archivos aprobados y 1 fallido; 1,941 pruebas aprobadas y 1 fallida por timeout de 5 s en `EmpleadoInlineForm.test.tsx:246` |
| Repetición de `EmpleadoInlineForm.test.tsx --maxWorkers=1` | Salida 0; 11 aprobadas, 0 fallidas |
| `git diff --check` | Sin errores |

La primera ejecución de Vitest sin límite de procesos se interrumpió al aparecer
fallos y esperas de UI durante la compilación simultánea. La repetición usa dos
procesos sin modificar las pruebas ni ampliar sus tiempos de espera.
El caso fallido del recorrido completo no se reprodujo al ejecutar su archivo
por separado (11/11). La causa de la intermitencia queda **Por confirmar**;
no se declara verde el recorrido global ni se modificaron archivos de frontend.
Antes de integrar, confirmar una corrida global verde en un entorno sin carga
de compilación simultánea.

Extractos de salida y detalle de las pruebas unitarias:
[evidencia local de validación](evidencia-c12-2026-10-08.txt).

El comando estándar de unitarias (`dotnet test ... --no-build --no-restore`)
se anuló antes de ejecutar pruebas: VSTest intentó abrir un socket local y el
sandbox devolvió `SocketException (13): Permission denied`. Se ejecutó el
ensamblado ya compilado con `XunitFrontController` del runner 2.8.2 instalado,
`AppDomainSupport.Denied` y el framework `Microsoft.AspNetCore.App` en un
harness temporal bajo `/tmp/c12-xunit-runner`, sin VSTest ni sockets. El runner
real descubrió y ejecutó las 220 pruebas. Salida completa de esta sesión:
`/tmp/c12-unit-inprocess-final.log`. La primera prueba del harness carecía de
la referencia al framework; esa configuración se corrigió antes del resultado
final. Claude debe confirmar también la ejecución estándar en su entorno.

Las pruebas de integración no se ejecutan en este sandbox, conforme a la ficha
(sin Docker). Claude debe ejecutar `tools/validate-integration-isolated.sh`
completo, verificar el rojo/verde y adjuntar evidencia. Compilar sus archivos
no acredita ejecución PostgreSQL ni HTTP.

## Demo · escena 10

Precondición a verificar en `millet_demo` mediante la ficha 01: ejercicio 2026
creado para la empresa activa, septiembre cerrado y octubre abierto. D9 también
rechaza octubre si el ejercicio o su apertura no existen. No se modificó esa base.

Recorrido pendiente de ejecutar y documentar:

1. Con OC, artículo y ubicación DEMO válidos, intentar una recepción con fecha
   `2026-09-15`: esperar 422 y el mensaje anterior; verificar que no haya movimiento.
2. Repetir con fecha de octubre y ambos periodos abiertos: verificar registro.
3. Con inventario cerrado, reabrir Contabilidad e intentar registrar otra vez:
   debe seguir rechazando por `PERIODO_CERRADO` (D18).

Evidencia de demo, captura/video y aceptación: **Por confirmar**.

## Siguientes pasos fuera de esta entrega

- Facturación y Tesorería: reemplazar NoOp, preparar ejercicios/periodos de todas
  sus suites y revisar la fecha efectiva D13 en Facturación.
- Tipo de cambio en Almacén y CxP: adaptador desde `Catalogos/Domain/TipoCambio`,
  error claro cuando no exista TC para la fecha; sin valor inventado. No se
  cambió el TC en esta entrega.
- `IConceptoContableReadPort`: C1.4. Retiro de `IContabilidadAsientoPort`: C1.6.
  Descarga Banxico: K10.6. CA10.9 (REP sin conciliar): CON-12.
- C1.2-a original (Facturación), C1.2-b y C1.2-c (TC) no se acreditan aquí.

## Borrador de reporte para ClickUp · no enviado

```text
Estado: propuesta para validar (implementación local del alcance Almacén; integración/demo pendientes)
PR: no creado · Commit integrado en main: ninguno de esta entrega
Pruebas: build 0 errores/0 advertencias; 220 unitarias de Almacén aprobadas con xUnit en proceso; TypeScript OK; lint 0 errores/10 advertencias; Vitest global 1941 aprobadas/1 timeout (archivo aislado 11/11). Integración PostgreSQL pendiente. · CA probados en demo: ninguno; escena 10 Por confirmar
Propuestas que necesitan validación: ninguna regla nueva; alcance autorizado en el encargo del 07-oct
Insumos que faltan del cliente: ninguno; pruebas con datos FIX/DEMO identificados
Desviaciones respecto de la tarea: alcance acotado a periodo en Almacén por decisión incluida en el encargo; Facturación/Tesorería y TC quedan para siguiente entrega; CA10.9 pertenece a CON-12
```

## Borrador para la bóveda · pendiente de aplicar

La bóveda está fuera de las raíces escribibles de esta sesión. En la ficha
`C1.2`, el plano C1 y Bitácora, añadir el siguiente registro sin sustituir su historial:

> 08-oct-2026: implementación local del encargo del 07-oct en `ficha/C1.2-periodo-consumidores`, base
> `a72ffc0`. Adaptador de periodo de Almacén conectado al puerto público de
> Contabilidad, falla cerrada D9 y cierre propio D18 conservado en cinco flujos.
> Evidencia y resultados: `docs/modulos/almacen/02-c12-periodo-contable-entrega.md`.
> Build correcto y 220 unitarias aprobadas con xUnit en proceso; Vitest global
> 1941/1942 y archivo fallido aprobado al repetirlo (11/11). Sin commit, PR
> integrado ni demo acreditada. Facturación/Tesorería y TC
> siguen pendientes; CA10.9 en CON-12. Próxima acción: validación aislada por
> Claude y ejecución documentada de la escena 10 con ejercicio 2026 preparado.

Mensaje de commit propuesto (no ejecutado):
`feat(almacen): validar el periodo contable real en movimientos`
