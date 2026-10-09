# P3 · Factura exacta y elegibilidad para pago · 09-oct-2026

Rama: `fix/P3-factura-exacta-G1.4`. Cambios locales sin commit ni push. Se continuó el trabajo anterior del mismo worktree; no se revirtió. Este reporte no acredita integración publicada, operación en Service Bus ni aceptación de Millet.

## Resultado por requisito

| Requisito | Implementación y prueba |
|---|---|
| Conciliación por línea | `ConciliacionFacturaOc` valida cantidades pendientes de facturas vivas, precio, base después de descuentos, importes de conceptos y neto fiscal. Acumula diferencias absolutas para evitar compensaciones entre líneas. El handler cancela y publica el rechazo con motivo en español. |
| Factura y NC juntas | Lee XML de factura y NC; comprueba proveedor, moneda, receptor, relación 01 exclusiva a la factura, asignación de base e importes. Aplica la NC y consume ambos CFDI únicamente si pasa la conciliación. El ejemplo 50 − 30 = 20 está probado. |
| Sin revaluación | CxP no publica la diferencia de precio. Almacén marca como procesados también los eventos antiguos, sin generar movimientos ni eventos de valoración. Conserva las entidades y el historial. ADR: `docs/decisiones/0060-factura-exacta-sin-revaluacion.md`. |
| G1.4 | Calcula recepción disponible por línea sin compartirla entre facturas; muestra elegible y retenido; vuelve a publicar el elegible al recibir o aplicar NC. Tesorería comprueba el límite acumulado vigente y resta sus pagos, incluso antes de que CxP los proyecte. |
| CXP-09 | Valida total = subtotal − descuentos + traslados − retenciones con la tolerancia ya existente; compara importes y conceptos con XML. Lee descuento de cabecera y toma las retenciones detalladas del XML en el servidor. Acepta IVA 8 %, exento y retenciones sin imponer el 16 % de la OC. |
| CFDI reutilizable | Una captura cancelada por conciliación deja factura y NC en `PorProcesar`, sin documento destino; permite reintentar con la NC o con la OC corregida. |
| Frontend | Reutiliza el selector de línea de OC, exige vincular cada concepto, permite adjuntar NC y repartir su base, muestra el motivo exacto del rechazo, ISR/IVA retenido y elegible/retenido. |

## Decisiones y diferencias factuales

- Se conserva exactamente la resolución actual de tolerancia por proveedor en el handler, incluido el valor por omisión 0.99; G1.13 no se modifica.
- La comparación usa la base pactada de la OC, incluidos sus descuentos, y los impuestos del XML; las retenciones no son una diferencia de precio.
- Una NC de precio no libera cantidad facturada de OC: no devuelve mercancía.
- No se cambia silenciosamente una transferencia que excede el elegible: se rechaza antes de crear el pago y el mensaje indica el máximo permitido.
- Capturas de una misma OC y pagos manuales usan advisory locks transaccionales; factura y NC compartidas se bloquean en orden estable. Los eventos de integración se publican antes de `SaveChanges`.
- El listener anterior de Almacén publicaba una valoración por variación de precio; no creaba el movimiento físico descrito en la ficha. Se eliminó su disparo completo, según la corrección expresa de Eliam.
- El selector de línea de OC y el modelo de retenciones detalladas ya existían; se reutilizaron.
- Se corrigió el tipado del formulario para admitir `null` mientras se selecciona la línea, conservando la validación al enviar. Se añadió una prueba de regresión.
- Se corrigió el mensaje cuando varios conceptos comparten una línea de OC: identifica el concepto con precio distinto, aunque no sea el primero del grupo. Se añadió una prueba de regresión.

## Evidencia de esta continuación

| Comprobación | Resultado |
|---|---|
| `dotnet build Millet.sln --no-restore -m:1 -p:UseSharedCompilation=false` | 0 errores y 0 advertencias. Compiló también las pruebas de integración. |
| Unitarias de CxP | 377 ejecutadas, 0 fallidas, 0 omitidas. |
| Unitarias de Almacén | 220 ejecutadas, 0 fallidas, 0 omitidas. |
| Unitarias de Tesorería | 107 ejecutadas, 0 fallidas, 0 omitidas. |
| Ejecutor estándar `dotnet test` | Anulado por `SocketException (13): Permission denied` al abrir el socket de VSTest. Las cifras anteriores provienen del ejecutor xUnit en proceso de `/tmp/p3-xunit`, contra los assemblies actuales. |
| `npx tsc --noEmit -p tsconfig.json` y `npm run -s typecheck:test` | Pasaron tras corregir el esquema. |
| `npm run -s lint` | Revisión final: 0 errores y 10 advertencias. |
| `npx vitest run --pool=threads --maxWorkers=2 --testTimeout=30000 --hookTimeout=30000` | 341 archivos y 1,995 pruebas aprobadas; código de salida 0. Se limitaron workers por carga del equipo y se dio margen de tiempo a las pruebas de UI, sin modificar la configuración del repositorio. |
| `git diff --check` | Sin errores. |

Logs de esta continuación e inventario completo de archivos: `artifacts/P3-2026-10-09/`.

Pruebas de P3: `backend/tests/CuentasPorPagar.UnitTests/FacturaProveedor/P3FacturaConOcTests.cs`, `P3ReglasTests.cs`, `backend/tests/Api.IntegrationTests/P3PagoYRevaluacionTests.cs`, `frontend/src/features/cxp/lib/conciliacion-p3.test.ts`, `components/NotasCreditoAdjuntas.test.tsx` y `pages/FacturaDetallePage.p3.test.tsx`. También se adaptaron pruebas existentes de Almacén y Tesorería; el selector de línea conserva sus pruebas existentes.

`P3FacturaConOcTests` se enlaza a la suite API con `P3_POSTGRES`; allí comprueba el handler y Outbox contra PostgreSQL, incluida captura concurrente. Las pruebas no agregan registros a catálogos compartidos y limpian sus datos. `P3PagoYRevaluacionTests` comprueba pago manual y descarte idempotente de eventos antiguos.

## Pendiente y siguiente acción

1. Claude debe ejecutar `./tools/validate-integration-isolated.sh` completo y verificar el rojo/verde contra PostgreSQL desechable. En este sandbox no se ejecuta Docker ni se usa una base de desarrollo.
2. Revisar captura y detalle en navegador con el backend de esta rama, y ensayar los casos obligatorios con datos ficticios identificados antes de la prueba del 12-oct. La revisión visual y el recorrido real entre módulos están **Por confirmar**.
3. Laura valida V20 el 16-oct. El ADR marca además **Por confirmar** el prorrateo fiscal: el neto después de NC se distribuye según la base recibida; anticipos y pagos reducen lo disponible. Durante el desfase de recepción entre CxP y Compras, el pago puede permanecer retenido hasta que Compras procese el evento.
4. Registro de avances en Obsidian: preparar la actualización de G1.4, CXP-09, ALM-13/MP-06 y Bitácora con esta evidencia; no se escribió fuera del worktree autorizado.

No hay migraciones nuevas, cambios en `infra/` o `.env*`, modificaciones de los flujos P1/P2, commits, push ni publicaciones externas.

Commit propuesto: `fix(cxp): conciliar factura y NC por línea y limitar pagos a lo recibido`.
