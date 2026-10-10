# P6b · Corrección de preparación de integración · 09-oct-2026

Continuación en `fix/P6b-sucursal-rutas-p4`, sobre `7aeec1d`. El worktree comenzó limpio: las correcciones anteriores ya estaban en `15439be` y `769b7bf`, junto con la fusión de `main`. No hubo cambios pendientes que recuperar. Se conservaron las implementaciones de P4/P7 y la limpieza de Outbox corregida.

La corrida aportada por Eliam/Claude (**1,104 aprobadas / 43 fallidas / 1,147 total API**) corresponde al estado anterior a estos cambios. El resultado PostgreSQL posterior está **Por confirmar**.

## Correcciones

| Fallos reportados | Causa comprobada | Cambio |
|---|---|---|
| 42 casos P7/archivo de vale | El fixture firmaba ambos niveles de OC con `datos.UsuarioId`, quien también la capturó. P2 prohíbe autoautorizar y repetir firmante N1/N2. | Dos IDs de firma ficticios distintos del capturista y entre sí, siguiendo las pruebas P2 existentes. No depende del seed opcional de demo ni crea usuarios/roles en catálogos compartidos. Se sigue autorizando mediante el dominio. |
| Aplicación de cargo sin factura en el body | La nota ya tenía la factura propia como origen, y `VincularFactura` rechaza sustituirla por otra. | Opción explícita del fixture para crear la nota sin factura de origen, usada solo en este caso. Se comprueba el origen vacío antes de vincular la factura ajena, y después 403 `SUCURSAL_NO_ASOCIADA` sin cambios en documentos propios ni ajenos. La foto de estado incluye ahora la factura de origen del cargo. |

Archivos de código cambiados: `P6SucursalEndpointsTests.cs`, `P6bP7SucursalEndpointsTests.cs` y `P6bSucursalEndpointsTests.cs`, todos en `backend/tests/Api.IntegrationTests/P6/`. No se añadieron casos HTTP: se corrigió su preparación y se reforzó la regresión existente. Las reglas P2 y de notas de cargo permanecen vigentes.

## Cotejo del alcance ya construido

Se repitió el cotejo actual de **133 rutas literales CxP/Tesorería + 19 rutas de las familias Almacén/trazabilidad revisadas por P7 = 152**, todas presentes en el inventario. Se normalizan nombres de parámetros y barra final; los adjuntos genéricos se documentan aparte. [Resultado por ruta](evidencia-p6b-adenda3/cotejo-rutas.txt).

El diff de P7 confirma cambios HTTP en salidas, PATCH de requisiciones y árbol documental; sus controles ya están implementados. El árbol verifica raíz y nodos relacionados, incluida recepción y entradas por factura/pago, antes de responder. Las rutas adicionales revisadas en las continuaciones anteriores permanecen cubiertas; no se declara aislamiento de todo Almacén.

La serie de anticipos sigue siendo configuración del proveedor/empresa, sin sucursal: GET exige `cuentas_por_pagar.anticipos.leer`, PUT exige `cuentas_por_pagar.anticipos.capturar`, sin filtro territorial. El listado OC sigue filtrando antes del conteo, con `FechaDocumento DESC`, `Folio DESC`, `Skip` y `Take`; las dos regresiones ya usan referencias exclusivas. No se necesita modificar producción en esta continuación.

## Validación local

Los logs actuales se conservan en [evidencia-p6b-adenda3](evidencia-p6b-adenda3/).

| Check | Resultado |
|---|---|
| `dotnet build backend/Millet.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false` | Salida 0; 0 errores y 0 advertencias; 3 min 37.78 s. Incluye las integraciones corregidas compiladas. |
| Compras / CxP / Almacén / Tesorería unitarias | 565 / 444 / 326 / 132 aprobadas. |
| API / Identidad / Compartido unitarias | 40 / 113 / 100 aprobadas. |
| Total unitarias revisadas | **1,720 aprobadas; 0 fallidas; 0 omitidas**, xUnit en proceso. |
| `npx tsc --noEmit -p tsconfig.json` | Salida 0. |
| `npx tsc --noEmit -p tsconfig.app.json` | Salida 0. |
| `npm run -s typecheck:test` | Salida 0. |
| `npm run -s lint` | Salida 0; 0 errores y 10 advertencias preexistentes. |
| `npx vitest run --maxWorkers=8` | Salida 0; **364 archivos y 2,085 pruebas aprobadas**; 210.91 s. |
| Cotejo literal de rutas | 152 revisadas; 0 ausencias en inventario. |
| `git diff --check` | Salida 0. |

VSTest estándar abortó antes de ejecutar pruebas por `SocketException (13): Permission denied`. Se reutilizó el runner xUnit en proceso de P6b, con resolución de dependencias y bibliotecas nativas desde cada DLL compilada. [Fuente del runner](evidencia-p6b-adenda3/runner-xunit.cs). Docker no puede acceder a `/Users/eliamcv/.orbstack/run/docker.sock`; no se ejecutó integración contra una base compartida como sustituto. Vitest emitió avisos existentes de jsdom sobre canvas/navegación, sin fallos.

## Pendiente y siguiente acción

Claude debe ejecutar **`./tools/validate-integration-isolated.sh` completo** desde este worktree y reportar aprobadas/fallidas/omitidas de API, Compras y A+W. El verde unitario y la compilación no demuestran que las 43 integraciones hayan pasado. La reproducción con usuarios reales de Cancún/Circuito sigue pendiente: el fixture reutiliza las sucursales ficticias MID/MTY del seed P6.

La actualización de la bóveda Obsidian no se aplicó porque está fuera del alcance de escritura. Texto preparado para su ficha/Bitácora: «P6b, 09-oct, continuación sobre 7aeec1d: corrección de preparación P7 con capturista/N1/N2 distintos y nota de cargo sin factura inicial para la regresión de factura persistida. Cotejo actual: 152 rutas inventariadas, 0 ausencias. Integración PostgreSQL posterior: Por confirmar; Claude ejecutará el gate completo. Evidencia local: docs/P6/P6b-adenda3-resumen.md».

No hubo commit ni push, según la regla común vigente. No se tocaron `infra/` ni `.env*`, ni se levantaron servidores. Mensaje propuesto: `test(sucursal): corregir preparación de firmas y nota de cargo en P6b`.
