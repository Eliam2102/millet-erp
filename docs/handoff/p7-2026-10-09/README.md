# P7 · Compras y Almacén · entrega local del 09-oct-2026

> Informe histórico de la primera corrida. Claude incorporó posteriormente P7 y `main` en `893f096`/`cb76942`. Para el estado actual, las correcciones de la adenda y la validación posterior, ver [Revisión de la adenda](REVISION-ADENDA.md). Las cifras y límites que siguen describen aquella primera corrida.

**Estado:** construcción local de los puntos 1–6 y 8; ADM-08 pendiente de decisión. P7 **no está cerrado**: falta conciliación con el main que avanzó durante la sesión, integración PostgreSQL y ADM-08. No hay commit, push, despliegue ni aceptación de Millet.

## Base y alcance

Worktree: `millet_erp-P7-RES`. Rama: `fix/P7-resto-compras-almacen`. Base verificada al iniciar: `6718ffe` (main con P1, P2, P3 y P8). Al cierre, la referencia local `origin/main` es `b72f990`, después de incorporar P5 y P9 durante la sesión. No se hizo fetch ni se incorporaron esas modificaciones a este worktree. La evidencia siguiente corresponde a `6718ffe` con el diff local de P7.

Las intersecciones directas con el nuevo main son `backend/src/Api/Program.cs` y `frontend/src/features/tesoreria/pages/DetalleMovimiento.tsx`. Antes de integrar, conciliar los registros de DI y el botón de trazabilidad con P5/P9 y repetir las validaciones. Los nuevos proveedores de pagos son de lectura; no cambian la aplicación ni reversa de pagos. El cambio operativo mínimo en CxP es tomar la obra de la OC al capturar, solicitado por D11.

No se tocaron `infra/` ni `.env*`, no se arrancaron servidores y no se ejecutó ninguna migración contra una base de datos. Los datos propios de las pruebas son DEMO; no se alteraron originales del cliente.

## Resultado por regla

| Punto | Comportamiento construido | Evidencia y límite |
| --- | --- | --- |
| D3 / CA3.4 | Autorización aparta existencia libre por línea y sucursal, en unidad de inventario. La disponibilidad para otra RQ descuenta apartados. Salida consume el apartado; cancelación, cierre manual y cierre automático por entrega total liberan remanentes. Bloqueo PostgreSQL por sucursal/artículo y transacción compartida Compras/Almacén. | Unitarias verdes. Integración escrita para 10 solicitadas/2 físicas, apartado 2, OC real por 8, segunda RQ sin esas 2, parámetro apagado y rollback tras fallo. PostgreSQL pendiente. |
| D4 / COM-04 | Cotización obligatoria. `CotizacionExcepcionada` ya no exonera. Se conserva la columna y el contrato histórico para no romper datos/clientes; se retiró la indicación visual de excepción. | Handler con las tres guardas. Integración escrita para `OC_MOTIVO_SIN_RQ_REQUERIDO`, `OC_SIN_RQ_CORREO_REQUERIDO`, `OC_COTIZACION_REQUERIDA` y camino válido. Pendiente de ejecución. |
| COM-12 / CA3.11 | Árbol recursivo RQ → OC → recepción → factura → pago aplicado; conserva enlace directo OC → factura. Entrada desde recepción, factura y aplicación de pago. Filtro de empresa y prevención de ciclos/duplicados por nivel. | Prueba frontend de cadena profunda verde. Integración escrita con OC pagada y proveedores reales. El pago se identifica por aplicación de factura, no por movimiento bancario; no se inventa un vínculo recepción/factura si no está registrado. |
| ALM-07 | Inicio muestra vales vencidos y por vencer en las próximas 24 horas; enlaces a bandeja filtrada. Se omiten los regularizados. | Unitarias de filtros verdes; endpoint probado por prueba de integración escrita. `GraphMailboxClient` disponible procesa correo entrante y no ofrece envío utilizable sin configurar remitente. El worker conserva `NoOpNotificacionService`; no se envió correo. V08 pendiente. |
| D6 / ALM-10 | Sólo generar RQ borrador cuando físico + pedido vivo ≤ punto; reponer hasta máximo o usar cantidad fija positiva. Dedup RQ/OC por línea. Incluye pedido manual por sucursal y sistema por almacén. | Regla, orquestador y supresión del siguiente ciclo en unitarias verdes. Se actualizaron expectativas antiguas de integración que generaban sobre el punto. Sin destino de almacén en documentos manuales, N2 considera la cobertura de la sucursal de forma conservadora. |
| D16 / ADM-04 | Recepción con factura/packing y salida con RQ/vale convierten usando la razón de `FactorABase`, validan dimensión y decimales, preservan captura y almacenan cantidad en unidad de inventario del artículo. Los eventos a Compras usan la unidad documental de OC/RQ y costo equivalente. | Unitarias de factor y de ambos handlers: 2 CAJA de 12 → 24 PZA. Adaptador real de catálogo probado en ambos sentidos y con errores de equivalencia/dimensión. Integración HTTP escrita: recibir 24 PZA y salir 12, con saldo por trigger PostgreSQL. |
| ADM-08 / P203 | Sin implementación de la herencia por departamento. Se conserva ADR-0050 vigente. | Conflicto de regla pendiente de Eliam; no es bloqueo por ausencia del dato V49. Ver apartado siguiente. |
| D11 | Obra opcional en texto de hasta 120 caracteres en RQ; herencia a OC y pasivo de CxP. Congelada fuera de borrador de RQ. OC rechaza consolidación de obras distintas. | Unitarias de obra verdes; integración RQ → OC → captura de factura escrita. No se encontró un catálogo de obras utilizable en el maestro. P8 sigue permitiendo captura manual cuando la OC no trae obra. |

## Conflicto que debe resolver Eliam · ADM-08

[ADR-0050](../../decisiones/0050-consumo-centro-costo-maquina-dim3.md) está aceptado y dice que la RQ lleva máquina Dim3 **obligatoria**, filtrada por alcance, prellenada con una única máquina. También dice que el catálogo es independiente de sucursal/departamento y que no existe un eje departamento → máquina. La ficha P7 pide herencia del departamento y un caso sin máquina.

Se solicitó la decisión de si P7 reemplaza esa regla y permite máquina opcional con equivalencia DEMO por departamento, hasta validar V49. **Por confirmar.** No se implementó el punto ni sus cuatro casos mientras falta esa decisión, conforme a la regla de detener sólo el tramo cuyo negocio contradice una regla vigente. Los demás puntos continuaron.

## Parámetro D3 y migraciones

`ComprasSettings.ApartarExistenciaAlAutorizar` es `true` por defecto, incluida la columna nueva para empresas existentes. Control declarativo en `/admin/compras/settings`, etiqueta **Apartar existencia al autorizar requisición**. API: `PATCH /api/v1/compras/configuracion` con `{"apartarExistenciaAlAutorizar": false}` (permisos e idempotencia existentes).

Apagarlo afecta autorizaciones futuras, no libera apartados anteriores. Éstos se consumen/liberan por sus flujos. La conversión manual a OC usa sólo `CantidadDeCompra`; se mantiene el comportamiento existente de `AutoGenerarOcAlAutorizar` (default false; puerto automático histórico todavía stub). No se promete creación automática de OC.

**No se sembró `ParametroGlobal`: ningún ID ocupado ni nuevo.**

Migraciones generadas, no aplicadas:

- Almacén: `20261009202740_P7ApartadosConversionReorden` (apartados por RQ, captura de unidades, cantidad fija de reorden).
- Compras: `20261009202742_P7ObraYApartadosConfigurables` (obra RQ/OC, parámetro con default true).

Los snapshots y designers se generaron con EF 10.0.12. La comprobación unitaria de correspondencia modelo/snapshot de Almacén está verde. Se eliminaron las fábricas temporales usadas para generación; no son parte del diff.

## Validación ejecutada

| Comprobación | Resultado exacto |
| --- | --- |
| `dotnet build Millet.sln --no-restore -m:1` | 0 errores, 0 advertencias; 18.22 s en la última pasada |
| Almacén unitarias | 314 ejecutadas, 0 fallos, 0 omitidas |
| Compras unitarias | 563 ejecutadas, 0 fallos, 0 omitidas |
| Compartido unitarias | 93 ejecutadas, 0 fallos, 0 omitidas |
| CxP unitarias | 427 ejecutadas, 0 fallos, 0 omitidas |
| Tesorería unitarias | 107 ejecutadas, 0 fallos, 0 omitidas |
| SharedKernel unitarias | 258 ejecutadas, 0 fallos, 0 omitidas |
| Total backend ejecutado | **1,762** pruebas, 0 fallos, 0 omitidas |
| `npx tsc --noEmit -p tsconfig.json` | Exit 0 |
| `npm run -s typecheck:test` | Exit 0 |
| `npm run -s lint` | Exit 0; 0 errores, 10 advertencias existentes en Administración |
| `npx vitest run --maxWorkers=4` | **354 archivos, 2,061 pruebas aprobadas**; 276.68 s |
| `git diff --check` | Exit 0 |
| Integración PostgreSQL | **No ejecutada**; todas las pruebas de integración escritas compilan en la solución |

La restauración se hizo con los paquetes de NuGet ya disponibles: `dotnet restore Millet.sln --source /Users/eliamcv/.nuget/packages --disable-parallel -p:NuGetAudit=false -m:1`. Después se construyó sin nueva restauración. CLI home temporal: `/private/tmp/p7-dotnet-home`.

**Límite del runner estándar:** `dotnet test` aborta porque VSTest intenta abrir su socket local y el sandbox devuelve `SocketException (13): Permission denied`. Para verificar unitarias se ejecutaron las DLL completas mediante el runner real de xUnit (`XunitFrontController.RunAll`) en proceso, sin TCP, con paralelismo desactivado, `Microsoft.AspNetCore.App` y el directorio base de cada ensamblado. No se seleccionaron sólo pruebas P7 ni se sustituyeron las reglas por una comprobación manual. Evidencia: [unitarias](evidencia/unitarias.txt), [runner](evidencia/runner-xunit.cs), [límite VSTest](evidencia/vstest-limite-sandbox.txt).

Las suites ejecutadas incluyen las unitarias de P1/P2 existentes. **No acredita P1/P2 de integración**, sockets reales, concurrencia PostgreSQL, entrega real por Outbox, ni aceptación de Millet. Tampoco se verificó la UI en navegador/servidor.

[Build](evidencia/build.txt), [frontend](evidencia/frontend.txt), [lint](evidencia/lint.txt). Inventario completo: [archivos cambiados](archivos-cambiados.txt).

## Pruebas nuevas y adaptaciones

- `Almacen.UnitTests/P7/ReglasP7Tests.cs`: umbral/max/fijo, recepción con ambas variantes, salida convertida, reserva ajena/consumo/liberación, avisos, motor que no genera sobre punto y no repite con cobertura viva.
- `Compartido.UnitTests/P7/ConversionUnidadAdapterTests.cs`: adaptador real de catálogo, CAJA↔PZA y errores de dimensión/equivalencia.
- `Compras.UnitTests/P7/ObraTests.cs`: normalización, congelamiento, consolidación incompatible y parámetro por defecto.
- `Api.IntegrationTests/Compras/Oc/P7RestoComprasAlmacenTests.cs`: cuatro casos documentales, tres casos CA3.4 (on/off/fallo), herencia de obra y OC pagada/árbol desde cada entrada.
- `Api.IntegrationTests/Compras/Oc/P7ConversionEndpointsTests.cs`: endpoints reales y saldo por trigger, captura CAJA, vale y aviso vencido.
- `frontend/.../ArbolDocumentos.p7.test.tsx`: render de RQ → OC → recepción → factura → pago.
- Fixtures P1/G16 y salida por línea actualizados por dependencias obligatorias y existencia física para las salidas. Se conservan sus aserciones de documentos, cantidad/costo y eventos.
- La factory de Compras con artículos y stock ficticios usa identidad de unidades y desactiva apartado real para mantener su contrato de ratios. Los casos nuevos P7 verifican adaptadores reales en Api.IntegrationTests; no confundir ese stub con prueba de apartado.
- Las pruebas nuevas que crean artículos/unidades/cuenta bancaria/ubicaciones limpian sus datos en `finally`; el resto reutiliza seed. Datos identificados como DEMO.

## Siguiente acción · Claude

1. Resolver ADM-08 con Eliam y conciliar P7 con main actual, preservando los cambios de P5/P9/P4 y sin mezclar sus reglas con P7.
2. Repetir build y unitarias con el runner estándar en un ambiente con sockets locales.
3. Ejecutar **completo**, sin filtro, `tools/validate-integration-isolated.sh`; conservar salida y conteos de sus tres suites. El script levanta PostgreSQL desechable y aplica todos los contextos, incluidas las migraciones P7.
4. Ejecutar el rojo/verde de los casos nuevos en un entorno aislado: mostrar que los casos fallan al retirar temporalmente la guarda/apartado/recursión/conversión/herencia correspondiente, restaurar exactamente el diff P7 y repetir verde. No usar una migración rota o un error de infraestructura como el rojo funcional.
5. Ensayar la UI y los casos con datos DEMO. Confirmar D3 con Millet el 12-oct y registrar V08/V49 como pendientes; no llamarlo aceptación ni producción.

La actualización de Obsidian está preparada en [borrador local](borrador-obsidian.md); falta aplicarla porque la bóveda queda fuera de las raíces de escritura de esta sesión. No se publicaron documentos ni se modificaron tareas externas.

Mensaje de commit propuesto (no ejecutado):

```text
fix(compras-almacen): aplicar apartado, reorden, trazabilidad y unidades de P7
```
