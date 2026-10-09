# P6 · continuación de la fusión con main · 9-oct-2026

Base P6: `535c905` (`fix(administracion): completar acceso y separación por sucursal (P6)`, commit previo hecho por Claude). Entrada de la fusión: `b72f990cba0d55dd7d77b99807136d9fd78866a3`. Se trabaja únicamente en `millet_erp-P6-ADM`.

## Resolución y decisiones

- Los cuatro reportes CxP conservan íntegra la lógica P8: corte histórico, nombres/RFC, moneda, obra, validadores y totales. Anticipos vuelve a filtrar por IDs permitidos de P6. Antigüedad de saldos, cartera y pasivos usan la guarda de asociación/bypass del lector común `SaldosHistoricos` de main; también cubre el nuevo auxiliar de proveedores. Se conserva su permiso `cuentas_por_pagar.reportes.leer-todas-sucursales`.
- Los dos reportes Tesorería conservan el saldo inicial/final y conceptos de P5. P6 filtra cuentas completas antes de calcular. La sucursal de una cuenta se deriva de todos los documentos origen de todos sus movimientos: una cuenta mixta exige acceso a todas esas sucursales; un origen desconocido o una cuenta sin movimientos requiere corporativo. Así no se muestra el saldo inicial de una cuenta excluida ni se altera el saldo de P5 restándole movimientos no visibles. El auxiliar y el flujo con cuenta explícita verifican el alcance antes del handler.
- `FacturaDetallePage` conserva todo main (P3/P8: elegible/retenido, notas de crédito, retenciones desglosadas, edición, obra y avisos) y agrega únicamente import y sección de adjuntos P6.
- La nueva ruta OC `resolver-cancelacion` tenía pendiente la guarda; se agregó sobre la sucursal del documento sin modificar la resolución/firma P2.
- La reclasificación de movimientos y la desvinculación de aplicaciones de pagos a cuenta ya heredan la guarda P6 del grupo; se añadieron regresiones. El handler de desvinculación valida que la aplicación pertenece al movimiento padre.
- P5 retiró confirmar/rechazar de propuestas CxC y trasladó la resolución a depósitos de Tesorería. Se eliminan solamente esos casos de rutas retiradas del fixture P6; se mantienen las regresiones de confirmar/rechazar depósitos.
- Conceptos, retenciones y saldo inicial de cuentas maestras conservan sus permisos de administración por empresa. No se reescribió lógica de P1/P2/P3/P5/P8/P9. Los cambios de `infra/` que están preparados pertenecen a la fusión ya iniciada por Claude; esta continuación no los editó.

Inventario actualizado: [inventario-sucursales.md](inventario-sucursales.md). Causas de los 105 fallos de la corrida PostgreSQL anterior y sus correcciones previas: [fallos-integracion-09oct.md](fallos-integracion-09oct.md). El fixture ya contenía `destinoReposicion: CuentaSucursal`; la regla de caja chica permanece intacta. `P6DesignTimeDbContexts.cs` conserva `ConnectionStrings__Postgres`.

## Pruebas agregadas en esta continuación

- `TesoreriaReportesSucursalP6Tests`: adapter real en EF InMemory, cuenta propia/ajena/mixta/sin origen, filtro de cuentas, saldo inicial y final de P5, auxiliar y camino corporativo.
- `FacturaDetallePage.p3/p8.test.tsx`: fixtures MSW de lista/tipos de adjuntos; prueban la sección P6 junto con importes y obra/alertas de main, sin solicitudes de red reales.
- `P6SucursalEndpointsTests`: OC resolver cancelación y movimiento reclasificar rechazan documento ajeno; desvincular aplicación ajena rechaza; por cada uno de los dos reportes P5 y cuatro reportes P8, ajeno recibe 403 y propio/corporativo 200. Las pruebas PostgreSQL están escritas y deben ejecutarse en el gate completo.

## Validación

| Check | Resultado actual |
|---|---|
| Solución backend, incluida integración PostgreSQL compilada | exit 0; 0 errores, 0 advertencias; 20.58 s |
| 15 suites unitarias backend | 3277 aprobadas; 0 fallidas; 0 omitidas |
| TypeScript raíz y aplicación, tipos de pruebas | exit 0 en los tres comandos |
| Lint | exit 0; 0 errores, 10 advertencias de hooks |
| Vitest completo, ocho trabajadores, reporter verbose | exit 0; 361 archivos y 2078 pruebas aprobados; 214.49 s |
| Modelos EF Identidad/Compartido | exit 0; sin cambios pendientes respecto de la última migración |
| PostgreSQL desechable | no ejecutado: Docker rechaza acceso a su socket |
| Git add/commit | exit 128: índice compartido fuera de los permisos del sandbox |

Comando de build: `cd backend && DOTNET_CLI_HOME=/tmp/p6-dotnet MSBUILDDISABLENODEREUSE=1 dotnet build Millet.sln --no-restore -m:1 /nr:false /p:UseSharedCompilation=false --nologo`. Los intentos iniciales con compilador compartido y Vitest sin límite / con dos trabajadores se interrumpieron y no cuentan como validación aprobada. Se ajustó únicamente la concurrencia del comando, sin cambiar la configuración del repositorio. La corrida final fue `cd frontend && npx vitest run --maxWorkers=8 --reporter=verbose`; terminó con exit 0. El lint completo se repitió tras completar los fixtures P3/P8 y conservó 0 errores y 10 advertencias.

La primera compilación tras resolver conflictos detectó tres errores CS7036 en los fixtures de API: main/P8 añadió periodo contable y reloj obligatorios al contexto CxP. Se adaptaron `AdjuntosP6Tests`, `OrganizacionP6Tests` y `CxpDocumentosRelacionadosP6Tests` con un helper de contexto aislado y periodos ficticios abiertos. No se modificó ni debilitó el gate contable productivo. La compilación y las 40 pruebas API posteriores pasaron.

VSTest estándar abortó por `SocketException (13): Permission denied`. Se utilizó el runner oficial xUnit de los paquetes existentes en proceso, sin instalar paquetes ni modificar pruebas productivas. Al ampliar a A+W se detectaron dos fallos de configuración SQL: el runner cargaba el DLL genérico de SqlClient en vez de su versión Unix. Se corrigió solamente el runner temporal con `AssemblyDependencyResolver` (usa `.deps.json`); A+W pasó después sus 313 pruebas. Esto no acredita conexión SQL real.

[Salidas y runner utilizado](evidencia-fusion/). [Conteos por módulo](evidencia-fusion/unitarias.txt). Esta nota no acredita integración PostgreSQL ni aceptación en vivo.

## Fusión pendiente por restricciones del sandbox

Se intentaron `git add` de los siete archivos resueltos y `git commit --no-edit`, ambos con código 128. El sandbox rechaza crear el índice compartido:

```text
fatal: Unable to create '/Users/eliamcv/Documents/Vidrios millet/millet_erp-review/.git/worktrees/millet_erp-P6-ADM/index.lock': Operation not permitted
```

Los archivos no contienen marcadores de conflicto, pero el índice conserva los siete estados `UU`. No se cambió el índice, no se hizo commit ni push. Este bloqueo es del sistema de archivos; la autorización del usuario para el commit de fusión ya existe.

Para cerrar desde una terminal con acceso al índice, en el mismo worktree:

```bash
bash docs/P6/cerrar-fusion-local.sh
./tools/validate-integration-isolated.sh
```

El chequeo del índice completo detecta tres líneas finales en blanco ya recibidas de main (`docs/entregas/G1.13-validacion.txt` y dos evidencias A4.5); no se editaron. El script limita la comprobación de espacios a los archivos de esta continuación.

El script de cierre prepara únicamente los archivos resueltos/cambiados en esta continuación y hace `git commit --no-edit` para finalizar la fusión existente. No hace push. El gate PostgreSQL debe ejecutarse completo y aportar el rojo/verde de API, Compras y A+W. Después corresponden Entra/Graph reales y el ensayo Millet. La actualización Obsidian está preparada localmente, no aplicada por permisos de escritura.

## Mensaje de commit

La instrucción autorizada es cerrar la fusión con `git commit --no-edit`, conservando el mensaje ya preparado. Si se necesita un título convencional para describir esta corrección: `fix(administracion): conservar el alcance de sucursal al integrar main`. No se creó un commit adicional.

## Archivos resueltos/cambiados de esta continuación

Los siete conflictos originales: cuatro queries de reportes CxP, dos de reportes Tesorería y `frontend/src/features/cxp/pages/FacturaDetallePage.tsx`. Los tres reportes de saldos/cartera/obras quedan idénticos a main porque su lector P8 ya aporta el alcance requerido.

Además de esos siete:

- `backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs`: guarda nueva de resolución de cancelación.
- `backend/src/Api/Endpoints/Tesoreria/ReportesEndpoints.cs`: guardas por cuenta explícita.
- `backend/src/Tesoreria/Infrastructure/TesoreriaSucursalReadAdapter.cs`: relaciones de cuenta con todos sus orígenes.
- `backend/tests/Api.IntegrationTests/P6/P6SucursalEndpointsTests.cs`: regresiones de nuevas rutas y adaptación de rutas retiradas por main.
- `backend/tests/Api.UnitTests/{AdjuntosP6Tests,CxpDocumentosRelacionadosP6Tests,OrganizacionP6Tests,CxpP6TestContext,TesoreriaReportesSucursalP6Tests}.cs`: fixtures compatibles con P8 y nueva prueba de reportes.
- `frontend/src/features/cxp/pages/FacturaDetallePage.p3.test.tsx` y `.p8.test.tsx`: fixtures y aserción de adjuntos.
- `docs/P6/{RESUMEN,actualizacion-boveda,inventario-sucursales,fusion-main-09oct}.md`, `cerrar-fusion-local.sh` y `evidencia-fusion/`: estado, inventario y validación verificables.
