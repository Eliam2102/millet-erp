# P7 · Revisión de la adenda · 09-oct-2026

Las causas de los fallos reportados están corregidas en código y preparación de pruebas. Build, unitarias y frontend están verdes localmente. **Los siete casos PostgreSQL reportados no se ejecutaron aquí; su verde sigue Por confirmar. P7 no está cerrado: ADM-08 conserva el conflicto con ADR-0050.**

## Base verificada

Worktree `millet_erp-P7-RES`, rama `fix/P7-resto-compras-almacen`. Al comenzar no había cambios sin commit: Claude ya había guardado P7 en `893f096` y fusionado `origin/main` en `cb76942`. Se comprobó que la referencia local `origin/main` es antecesora de HEAD; no se hizo fetch ni se afirma el estado remoto actual. Se continuó desde ese código.

Se leyeron las instrucciones del proyecto, CLAUDE, CONTRIBUTING, Inicio/Cobertura de Obsidian, contratos de la triada y ADR-0050/0061. No se tocaron flujos P4, frontend, `infra/`, `.env*`, catálogos del cliente ni migraciones. No se sembró ningún `ParametroGlobal`, por lo que no se utilizó ningún ID nuevo. No se levantaron servidores.

## Correcciones

| Fallo reportado | Causa y cambio | Evidencia local |
| --- | --- | --- |
| Recepción de periodo cerrado: 404 en vez de 422, ambas variantes | P7 consultaba conversión/artículo antes del cierre. Ambos handlers ahora comprueban cierre de inventario y Contabilidad antes de convertir; conservan las guardas de estado/línea/costo de P1 y los límites equivalentes posteriores. | Dos casos nuevos exigen `PERIODO_CONTABLE_NO_ADMITE`, cero consultas de conversión, cero movimientos y cero eventos. Las pruebas HTTP originales no se alteraron. |
| Recibir 2 CAJA: 422 | El catálogo permite claves con minúsculas; el adaptador las convertía a mayúsculas y perdía la equivalencia. Se respeta el código capturado, quitando sólo espacios exteriores. La OC de la prueba ya trae artículo/línea, 24 PZA pendientes, costo positivo y dos firmas; Contabilidad se abre explícitamente. | Prueba real del adaptador con `CajaDemo` y factor 12. La integración conserva la clave mixta, comprueba estado Autorizada y muestra el Problem Details si falla recepción/salida. Se corrigió el filtro del aviso a `noRegularizados`, nombre real del endpoint. |
| Apartado: `ObjectDisposedException`, tres casos | La causa también estaba en producción: `SetDbConnection` libera la conexión anterior cuando EF es dueño; después se reinstalaba ese objeto liberado. Se clona la conexión propia antes del intercambio y se restaura con la propiedad adecuada. Las conexiones externas prestadas se conservan. La conexión/transacción de Compras nunca pasa a ser propiedad de Almacén. También se restaura ante fallo de unión. | Dos pruebas de ciclo de vida usan EF/Npgsql reales sin abrir sockets; prueban dos uniones consecutivas y conexión propia/prestada. No simulan atomicidad SQL. La prueba PostgreSQL ahora lee rollback desde otro scope, exige el error del puerto simulado, sin autorización/apartado/OC persistidos, conserva físico 2 y comprueba que el scope original sigue utilizable. |
| OC sin RQ, caso motivo: `23514` al preparar | La preparación quitaba el motivo antes de guardar, chocando con `ck_oc_sin_rq_motivo`. Se guarda primero una OC válida; el motivo se quita sólo en el agregado rastreado inmediatamente antes de ejecutar el handler. | Se mantienen los tres códigos y el camino válido. Se limpia la OC propia en `finally`. La restricción de BD permanece. |

La mecánica de liberación de conexiones se contrastó con el [código oficial de EF Core](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Storage/RelationalConnection.cs#L164-L178) y se reprodujo localmente con las dependencias instaladas.

## Contraste del paquete completo

| Punto | Estado verificado en el código actual |
| --- | --- |
| D3: apartado configurable | Autorizar, consumir/liberar, disponibilidad descontada y transacción compartida existen. ADR-0061 reemplaza la prohibición de ADR-0047. Parámetro `ComprasSettings.ApartarExistenciaAlAutorizar`, default true; no requiere `ParametroGlobal`. |
| D4: OC sin RQ | Cotización obligatoria; las guardas de motivo/correo siguen. El flag histórico ya no exonera. |
| COM-12: trazabilidad | Servicio recursivo y proveedor real de pagos aplicados por puerto de lectura; entrada desde recepción, factura y aplicación de pago. Se conservó lo incorporado con P5. |
| ALM-07: aviso | Inicio consulta y enlaza vales vencidos/por vencer para el permiso de Almacén. `GraphMailboxClient` es de entrada, sin envío utilizable; se conserva NoOp del worker y correo pendiente V08. No se envió correo. |
| D6: reorden | Físico + pedido vivo ≤ punto; repone al máximo o cantidad fija. Unitarias incluyen no generar sobre punto y no repetir con pedido vivo. |
| D16: unidades | Recepción y salida convierten por `FactorABase`, conservan captura y cantidad base. Se corrigió la capitalización descrita arriba. |
| ADM-08 | **Pendiente.** ADR-0050 exige Dim3 por línea y declara independencia de departamento; la ficha pide herencia y caso sin máquina. Se pidió resolución a Eliam; sin respuesta en esta revisión. Los cuatro casos nuevos de ADM-08 no se implementaron. La falta de datos V49 por sí sola no es bloqueo. |
| D11: obra | RQ → OC → pasivo ya implementado; captura CxP usa `oc.Obra ?? command.Obra`. Integración escrita con herencia y árbol con pago. Su ejecución PostgreSQL sigue pendiente. |

## Validación posterior a la fusión de main

| Comprobación | Resultado |
| --- | --- |
| `cd backend && dotnet build Millet.sln --no-restore -m:1` | Exit 0, 0 errores, 0 advertencias. Incluye compilación de las pruebas de integración modificadas. |
| Almacén unitarias | 316, 0 fallos, 0 omitidas |
| Compras unitarias | 565, 0 fallos, 0 omitidas |
| Compartido unitarias | 94, 0 fallos, 0 omitidas |
| CxP unitarias | 427, 0 fallos, 0 omitidas |
| Tesorería unitarias | 120, 0 fallos, 0 omitidas |
| SharedKernel unitarias | 258, 0 fallos, 0 omitidas |
| Total unitarias | **1,780**, 0 fallos, 0 omitidas; incluye las unitarias existentes de P1/P2 |
| `npx tsc --noEmit -p tsconfig.json` | Exit 0 |
| `npm run -s typecheck:test` | Exit 0 |
| `npm run -s lint` | Exit 0; 0 errores, 10 advertencias existentes |
| `npx vitest run --maxWorkers=4` | Exit 0; **358 archivos / 2,072 pruebas** aprobados |
| `git diff --check` | Exit 0 |
| `bash tools/validate-integration-isolated.sh` | Exit 1 por acceso denegado al socket Docker; no creó PostgreSQL ni ejecutó las suites |

VSTest aborta al abrir su socket local (`SocketException (13)`). Las seis DLL completas se ejecutaron con `XunitFrontController.RunAll`, sin paralelismo ni TCP, utilizando el runner de la primera corrida. [Runner](evidencia-adenda/runner-xunit.cs), [unitarias](evidencia-adenda/unitarias.txt), [build](evidencia-adenda/build.txt), [tipos](evidencia-adenda/frontend-tipos.txt), [lint](evidencia-adenda/frontend-lint.txt), [Vitest](evidencia-adenda/frontend-vitest.txt), [límite de VSTest](evidencia-adenda/vstest-limite-sandbox.txt), [límite del gate](evidencia-adenda/gate-limite-sandbox.txt).

**Rojo/verde local ejecutado:** temporalmente se instalaron los tres archivos de producción de HEAD, manteniendo las pruebas nuevas. El build siguió verde: Almacén tuvo exactamente 2 fallos por adelantarse conversión al periodo; Compras 1 por conexión liberada. En una segunda comprobación, el adaptador anterior produjo 1 fallo con `CajaDemo`. Se restauró el código corregido en `finally`, se compiló y se repitieron las seis suites completas en verde. [Rojo Almacén](evidencia-adenda/rojo-almacen.txt), [Rojo Compras](evidencia-adenda/rojo-compras.txt), [Rojo conversión](evidencia-adenda/rojo-conversion.txt). Esto prueba esas regresiones; **no sustituye el rojo/verde PostgreSQL de los flujos nuevos completos**.

## Archivos de código cambiados en esta continuación

- `backend/src/Almacen/Application/Recepciones/RegistrarRecepcionConFacturaCommand.cs`
- `backend/src/Almacen/Application/Recepciones/RegistrarRecepcionConPackingListCommand.cs`
- `backend/src/Compartido/Infrastructure/PublicAdapters/ConversionUnidadAdapter.cs`
- `backend/src/Compras/Infrastructure/PublicAdapters/TransaccionApartadosRq.cs`
- `backend/tests/Almacen.UnitTests/P7/ReglasP7Tests.cs`
- `backend/tests/Compras.UnitTests/P7/TransaccionApartadosRqTests.cs` (nuevo)
- `backend/tests/Compartido.UnitTests/P7/ConversionUnidadAdapterTests.cs`
- `backend/tests/Api.IntegrationTests/Compras/Oc/P7ConversionEndpointsTests.cs`
- `backend/tests/Api.IntegrationTests/Compras/Oc/P7RestoComprasAlmacenTests.cs`

Se añadió este informe, sus evidencias y el borrador de Obsidian; el README anterior se identificó como histórico. Los 125 archivos del paquete inicial están documentados en el inventario previo, no se reconstruyeron ni revirtieron.

## Pendiente y siguiente acción

1. Claude: ejecutar el gate completo, sin filtro, y guardar el resultado de las tres suites. Deben pasar los siete casos reportados, P1/P2 y el resto de integración. Conservar el rojo/verde de PostgreSQL de P7.
2. Eliam: resolver si ADM-08 reemplaza ADR-0050. Tras resolver, construir/probar los cuatro casos; usar equivalencia DEMO hasta validar V49.
3. Ensayo visual y aceptación Millet, confirmación D3 del 12-oct, remitente V08 y equivalencias reales V49 siguen **Por confirmar**.
4. Aplicar el [borrador de Obsidian](borrador-obsidian-adenda.md) cuando la bóveda admita escritura. No se actualizó ni publicó fuera del worktree.

No se hizo commit ni push: se respetó la regla común explícita de esta ficha. Mensaje propuesto:

```text
fix(p7): corregir recepción por periodo, unidades y conexiones de apartados
```
