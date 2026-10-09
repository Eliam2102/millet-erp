# P2 · Firmas y saldo de Compras · 09-oct-2026

Trabajo local en `fix/P2-compras-firmas-y-saldo`, base `4bf1690`. Sin commit ni push. Decisiones D1, D2 y D5 de Eliam en la ficha del 09-oct; no acredita integración, despliegue ni aceptación de Millet.

## Continuación · correcciones tras la corrida PostgreSQL de Claude

Estas comprobaciones sustituyen los totales locales de la sección anterior a esta continuación. Los 27 fallos API reportados por Claude (861/888; Compras 138/138 y A+W 7/7) son el resultado externo recibido en la ficha, no una corrida de esta sesión.

- **Causa del detalle 500 reproducida offline:** al añadir propiedades `init` al record posicional `OrdenCompraResponse`, Mapster deja de inferir su construcción y lanza `CompileException` / `No default constructor for type 'OrdenCompraResponse'`. El fallo ocurre incluso con OC vacía, sin EF ni PostgreSQL. `OcMapsterConfig` selecciona explícitamente el constructor con `MapToConstructor(true)` y deja los dos historiales al handler, que ya los compone y enriquece. No se quita ni renombra ningún campo HTTP. Prueba nueva de mapeo y JSON con OC borrador y OC con firmas/cancelación pendiente. Evidencia roja: `validacion/postgres-mapeo-rojo.log`.
- **Seed:** `DemoSesionCompras` usaba `ActorId` como capturista y ambos firmantes. Ahora captura `CapturistaComprasDemoId`, firma N1 `JefeComprasDemoId` y firma N2 `DireccionDemoId`. `DemoSesionUsuarios` no contenía tres identidades ficticias para esos pasos: se añaden idempotentemente con ids/OIDs estables, nombres DEMO y cuentas técnicas locales. El capturista tiene alcance MID; jefe y Dirección tienen las tres sucursales DEMO. Solo jefe recibe permiso N1 y solo Dirección N2; capturista no recibe ninguno. No se relaja el dominio ni se provisionan cuentas en Entra. La prueba de dos arranques comprueba identidades, roles, alcances y conservación de recepciones; usa su propia base desechable.
- **Permiso por nivel:** `P2_Endpoint_NivelDenegado_403` autenticaba una identidad sin asignación a empresa y podía recibir `EMPRESA_NO_SELECCIONADA` antes de evaluar el nivel. Ahora usa una asignación real con el permiso opuesto; elimina su usuario/rol en `finally` para no alterar catálogos de otras suites. Los permisos del endpoint de producción no cambian.
- **Migraciones:** se revisaron `P2FirmasCancelacionOc`, `P2CiclosAutorizacionOc`, el snapshot y el mapeo. La comprobación EF offline no encuentra cambios pendientes. No se añade ni se modifica una migración para corregir un fallo de Mapster.
- **P2 previo conservado:** autorización por ciclo, doble firma de cancelación, bloqueo de recepción/facturación, devolución solo del faltante, saldo compartido entre flujos, artículo heredado fijo y CeCo vigente/dentro del alcance. Se mantiene el bloqueo de duplicación de rechazada con RQ; corregir y reenviar la misma OC abre un ciclo nuevo. El único cambio en el adaptador compartido con P1 sigue siendo el guard de cancelación solicitada.

| Validación actual | Resultado |
|---|---|
| Solución .NET completa, `--no-restore -m:1 -p:UseSharedCompilation=false` | 0 errores y 0 advertencias |
| Unitarias de Compras | 559 aprobadas, 0 fallidas, 0 omitidas |
| Unitarias API, incluidas identidades/permisos DEMO | 15 aprobadas, 0 fallidas, 0 omitidas |
| `npx tsc --noEmit -p tsconfig.json` y `tsconfig.app.json` | Código 0 |
| `npm run -s typecheck:test` | Código 0 |
| `npm run -s lint` | 0 errores; 10 advertencias preexistentes fuera de P2 |
| `npx vitest run --pool=threads --maxWorkers=2` completo | 342 archivos y 2004 pruebas aprobadas; 0 fallidas; 222.52 s |
| EF `migrations has-pending-model-changes` offline | Código 0; modelo coincide con última migración |
| `git diff --check` | Sin errores |

VSTest sigue bloqueado por `SocketException (13): Permission denied`. Las unitarias se ejecutaron con `AssemblyRunner.WithoutAppDomain`, desde el directorio original de salida para conservar las rutas de las pruebas de auditoría; se contabilizan también errores del runner. Se retiran los auxiliares al finalizar. No se cambió ninguna prueba para omitirla.

**Por confirmar:** el nuevo verde del gate PostgreSQL completo y la desaparición de los 27 fallos. Claude debe volver a ejecutar `tools/validate-integration-isolated.sh` sin filtros, incluyendo el seed de dos arranques, detalle/adjuntos, firmas por ciclo, cancelación y saldo. Esta sesión no ejecuta PostgreSQL/Docker, no inicia servidores, no despliega y no acredita ensayo visual ni aceptación de Millet. La corrección local reproduce y elimina la causa compartida del mapeo, pero no demuestra que los 888 endpoints estén verdes.

Archivos adicionales de esta continuación: `DemoSesionCompras.cs`, `DemoSesionUsuarios.cs`, `OcMapsterConfig.cs`, `OcMapsterConfigTests.cs`, las pruebas DEMO de API unitarias/integración y la fixture de permisos de `P2FirmasYSaldoTests.cs`. El inventario completo vive en `archivos-cambiados.txt`.

Mensaje propuesto: `fix(compras): separar firmas por ciclo, proteger saldo y corregir detalle y seed demo`.

## Implementación y decisiones

- COM-05: el dominio rechaza al capturista (`CompradorTitularId`, inmutable y resuelto del JWT al crear) en N1 y N2 con `OC_AUTOAUTORIZACION`; N2 igual a N1 produce `OC_FIRMA_MISMA_PERSONA`. Los permisos de super-admin no omiten estas reglas.
- COM-08: el endpoint existente `cancelar-con-recepciones` registra la solicitud con permiso N1. El nuevo `resolver-cancelacion` requiere N2 y una persona distinta (`OC_CANCELACION_MISMA_PERSONA`). Ambas decisiones requieren motivo. `CancelacionSolicitada = 7` bloquea recepción y facturación; rechazar recupera el estado anterior y conserva el historial. Confirmar cancela y libera el compromiso de RQ únicamente cuando hay cantidad no recibida.
- Historial persistente de solicitante, resolutor, fechas, motivos y resultado en `oc_solicitudes_cancelacion`. Migración `20261009145644_P2FirmasCancelacionOc`, con índice único para una solicitud pendiente por OC.
- COM-03: saldo por línea = cantidad de compra − cantidades en OC no canceladas − cantidades recibidas en OC canceladas. Crear desde RQ, consolidar/agregar, editar cantidad y duplicar utilizan el cálculo común. Se serializa el consumo de saldo en PostgreSQL mediante advisory lock transaccional antes de leer. No se depende de un consumidor de `LineaRqLiberadaEvent`.
- Duplicación: conserva referencias a RQ y copia solo lo no recibido. Se bloquea duplicar una rechazada vinculada a RQ: conserva el compromiso y debe corregirse/reenviarse en esa misma OC. Mensaje `OC_RECHAZADA_RQ_COMPROMETIDA`. Se eligió bloquear para no liberar silenciosamente una OC que aún puede corregirse. La UI explica este bloqueo y el de una OC completamente recibida. El dominio vigente no permite cancelar desde Rechazada; se corrigió la sugerencia anterior de cancelarla primero.
- Artículo heredado inmutable (`OC_LINEA_RQ_ARTICULO_FIJO`) y cantidad acotada por saldo (`OC_EXCEDE_SALDO_RQ`). CeCo vigente y dentro del alcance en los tres flujos solicitados (`CECO_INVALIDO`, puerto `IDim3ElegibilidadPort`).
- Adenda: `CicloAutorizacion` en la OC y `Ciclo` en cada firma/rechazo. Reenviar tras rechazo abre un ciclo sin firmas vigentes; no se eliminan las anteriores. La unicidad SQL ahora es por OC + ciclo + nivel. El detalle muestra persona, fecha y hora, nivel, resultado, motivo del catálogo, descripción adicional y notas por ciclo. Migración `20261009161448_P2CiclosAutorizacionOc`, con reclasificación de historia previa; el downgrade se detiene si perdería la capacidad de representar firmas repetidas entre ciclos.
- UI: solicitud de una firma, confirmación/rechazo con motivo, historial visible en detalle y acceso desde pendientes N2. Formularios con `zId`, sin casillas que representen a otra persona.
- Eventos de integración encolados antes de `SaveChanges`, estado y Outbox en la misma transacción.

## Diferencias menores frente a la ficha

- El worktree sí parte de `4bf1690`, pero ya contenía implementación parcial sin commit; se continuó ese trabajo.
- Consolidar comparte `AgregarLineaDesdeRequisicionHandler`; no hay un handler separado de consolidación.
- Las RQ convertibles están en `EnSurtido`; se conserva esa condición vigente.
- CxP ya restringe la captura de factura a estados facturables. El estado nuevo queda excluido por ese guard existente; se añade su prueba por endpoint.
- La cancelación sin recepciones se mantiene con una firma.
- Las pruebas de handlers que materializan líneas con propiedades complejas se ejecutan en PostgreSQL: el proveedor EF InMemory no soporta ese recorrido completo. Las unitarias prueban las reglas y el cálculo con proyecciones de saldo; las de integración prueban los tres flujos y la edición real.

## Coordinación con P1

Único cambio en `ComprasOcReadAdapter`: devolver 422 `OC_CANCELACION_SOLICITADA` antes de la lectura recibible. `ComprasRequisicionReadAdapter` no se modifica. Al integrar P1 y P2, conservar este guard junto con las validaciones de P1.

D3 (volver a apartar al autorizar RQ), D4 (retirar excepción sin cotización), `infra/` y `.env*` quedan fuera. No se abren servidores en 5080 ni 5173.

## Pruebas

Nuevas pruebas en:

- `backend/tests/Compras.UnitTests/Oc/Domain/P2FirmasYSaldoTests.cs`: identidades, cancelación, rechazo, bloqueo de movimientos, cantidades y artículo fijo; tres nuevas pruebas de reautorización por ciclo (rechazo N1/N2, mismo jefe otra vez, fechas iguales y cambio de roles entre ciclos).
- `backend/tests/Compras.UnitTests/Oc/Application/P2SaldoHandlersTests.cs`: saldo persistido y consumo por OC viva.
- `backend/tests/Api.IntegrationTests/Compras/Oc/P2FirmasYSaldoTests.cs`: permisos, super-admin, 422 de identidades, dos usuarios, rechazo, recepción/facturación bloqueadas, nueva OC por faltante, duplicación/consolidación por faltante, artículo/cantidad, duplicado rechazado y CeCo inactivo/fuera de alcance. Adenda: dos recorridos rechazo → corregir → transmitir → N1 → N2 con recargas de la OC, historia HTTP de ambos ciclos, misma persona bloqueada dentro del nuevo ciclo; y reclasificación SQL de firmas antiguas en tablas temporales exclusivas de una conexión.
- `frontend/src/features/compras/ordenes/components/CancelacionDialogs.test.tsx` y `lib/cancelacion.test.ts`: solicitud, decisiones, motivos y permisos/personas.
- `frontend/src/features/compras/ordenes/lib/acciones-disponibles.test.ts`: bloqueo visible de duplicación rechazada con RQ y de cantidades recibidas por completo.
- `frontend/src/features/compras/ordenes/components/HistorialFirmasOc.test.tsx` y `lib/ciclos-autorizacion.test.ts`: ciclos visibles con firmantes/fechas, rechazo y motivos; agrupación pura sin mutar el historial, incluso con fechas iguales.
- `frontend/src/features/compras/lib/glosario.test.ts`: contrato actualizado a ocho estados de OC, incluida `CancelacionSolicitada`.

Se adaptan las pruebas previas de cancelación y la fixture de Outbox, que antes usaba a una persona como capturista y ambos firmantes.

## Validación local final · incluida la adenda

| Comprobación | Resultado verificado |
|---|---|
| `dotnet build Millet.sln --no-restore -m:1 -p:UseSharedCompilation=false` | Código 0; 0 errores, 0 advertencias; solución completa, incluidas las pruebas de integración nuevas |
| Unitarias de Compras, xUnit en el mismo proceso con la asamblea final | 557 aprobadas, 0 fallidas, 0 omitidas |
| `npx tsc --noEmit -p tsconfig.json` | Código 0 |
| `npx tsc --noEmit -p tsconfig.app.json` | Código 0; comprueba la aplicación real, porque el tsconfig raíz solo contiene referencias |
| `npm run -s typecheck:test` | Código 0, repetido tras la última modificación de pruebas |
| `npm run -s lint` | Código 0; 0 errores, 10 advertencias en otros componentes |
| Vitest completo, `npx vitest run --pool=threads --maxWorkers=2` | 342 archivos aprobados; 2004 pruebas aprobadas, 0 fallidas; 178.77 s |
| Vitest de Órdenes de Compra tras el último ajuste de motivo visible | 25 archivos; 161 aprobadas, 0 fallidas; 13.74 s |
| EF `migrations has-pending-model-changes`, fábrica temporal offline | Código 0; modelo y snapshot coinciden |
| Generación SQL de `P2CiclosAutorizacionOc` | Código 0; script revisado, no ejecutado contra PostgreSQL |
| `git diff --check` | Sin errores |

`dotnet test --no-build --no-restore -m:1` devuelve código 1 por `SocketException (13): Permission denied` al abrir el canal local de VSTest. Se ejecutó la misma asamblea final con `Xunit.Runners.AssemblyRunner.WithoutAppDomain` desde el directorio de salida de las pruebas. No se cambió ni omitió ninguna prueba. El auxiliar temporal se retiró del directorio de salida después de ejecutarlo.

El build detectó inicialmente una referencia de namespace, una regla del analizador xUnit y dos reglas CA1861 en las pruebas nuevas. Se corrigieron; el build final completo tiene 0 errores y 0 advertencias. La suite completa del frontend imprimió avisos de jsdom sobre canvas/navegación; no produjo pruebas fallidas ni se modificaron timeouts.

La corrida anterior registró 554 unitarias y 2000 pruebas frontend; los resultados actuales son los de la tabla anterior. Los registros finales están en `validacion/ciclos-*`; `archivos-cambiados.txt` contiene el inventario del paquete completo. `actualizacion-boveda.md` es un borrador local pendiente de aplicar a Obsidian, fuera del alcance de escritura de esta sesión.

## Validación que debe ejecutar Claude

En PostgreSQL desechable, ejecutar `tools/validate-integration-isolated.sh` completo (sin recortar a P2), con su rojo/verde. Esta sesión no dispone de Docker. Comprobar migración desde el corte anterior, ida/vuelta HTTP, Outbox y convivencia con P1.

Recorrido visual pendiente: capturista, jefe N1 y Dirección N2 distintos; OC de 10, recibir 4, solicitar cancelación, intentar recibir/facturar, rechazar, solicitar otra vez, confirmar y crear/duplicar una OC de 6. El historial debe mostrar las dos solicitudes y sus decisiones. No equivale a aceptación de Millet.

El bloqueo de reautorización observado en la corrida anterior queda corregido por la adenda: el mismo jefe puede firmar N1 otra vez en un ciclo nuevo tras rechazo en N1 o N2. La persona de N2 debe seguir siendo distinta del N1 vigente y del capturista. Los ciclos anteriores permanecen visibles. Las pruebas unitarias y de UI pasaron; la persistencia HTTP y la reclasificación SQL están escritas y compiladas, pendientes de la corrida aislada de Claude.

Para el ensayo, añadir: rechazar en N2, corregir el precio, reenviar, firmar N1 con el jefe original y N2 con Dirección; revisar los dos ciclos y sus motivos en la pestaña Autorización. Repetir con rechazo en N1. No acredita aceptación de Millet.

Mensaje de commit propuesto: `fix(compras): separar firmas por ciclo y proteger el saldo de requisiciones`.
