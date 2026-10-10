# P6b · continuación y corrección de integración

09-oct-2026 · rama `fix/P6b-sucursal-rutas-p4`, HEAD inicial `15439be`. El worktree llegó limpio: P4/P7 ya estaban guardados en `a40b134`/`15439be`. No se revirtió ese trabajo ni se hizo commit o push en esta continuación.

## Cambios y decisiones

- Se corrigen las cuatro consultas problemáticas sobre Outbox: tres limpiezas en el fixture P6 y el conteo de estado P6b. `Payload` conserva `jsonb`; la búsqueda por referencia se hace en memoria después de proyectar IDs/JSON y el borrado usa IDs. No se modifican migraciones ni tipos de columnas.
- El historial del listado OC confirma que P6 agregó solo el filtro territorial antes de conteo/paginación. Conserva `FechaDocumento DESC`, `Folio DESC`, `Skip` y `Take`. Las dos pruebas reportadas ahora seleccionan sus OC por referencia exclusiva y verifican conjuntos exactos, sin depender de datos de otras pruebas en la primera página. La contaminación por la limpieza fallida es compatible con el código y el error reportado; su reproducción PostgreSQL aquí sigue **Por confirmar**.
- El cotejo actual de CxP/Tesorería confirma 133 rutas literales sin filas faltantes. El cotejo de recepción/salida/vale/reorden/trazabilidad confirma 19 rutas tras incorporar cuatro omisiones del inventario. La serie de anticipos conserva sus permisos de lectura/captura y su configuración por proveedor sin sucursal.
- Se corrige una omisión adicional anterior a P7: `GET /almacen/salidas/{id}/vale` ahora exige el permiso de lectura primero y verifica el alcance de la salida antes de consultar/abrir el archivo. Las cargas previas sin documento y el selector Dim3 abierto conservan sus reglas explícitas. No cambian las reglas de negocio de P4/P7.

## Archivos de código y pruebas

| Archivo | Cambio |
|---|---|
| `backend/src/Api/Endpoints/Almacen/Vales/ValeBlobEndpoints.cs` | Guarda territorial de descarga del vale. |
| `backend/tests/Api.IntegrationTests/P6/P6SucursalEndpointsTests.cs` | Limpieza JSON, tres casos Outbox y un caso de paginación OC; Blob Storage ficticio en memoria para el fixture territorial. |
| `backend/tests/Api.IntegrationTests/P6/P6bSucursalEndpointsTests.cs` | Conteo de eventos sin `Contains` traducido sobre JSON. |
| `backend/tests/Api.IntegrationTests/P6/P6bP7SucursalEndpointsTests.cs` | Descarga de vale en las dos teorías de autorización y dos casos de contenido/descarga. |
| `backend/tests/Api.IntegrationTests/Compras/Oc/OrdenesCompraEndpointsTests.cs` | Aislamiento de las dos pruebas reportadas por referencia propia. |
| `docs/P6/inventario-sucursales.md` | Rutas, excepciones y corrección del fixture documentadas. |

Se agregan **8 casos de integración**: 3 de JSON anidado/limpieza selectiva y vacía, 1 de conteo/paginación/orden por sucursal, 2 de autorización por ruta de vale (403 ajeno y prioridad del permiso, con éxito propio/corporativo) y 2 de contenido/descarga del vale. Se reutilizan el fixture y periodo abierto P6. El PDF es ficticio; no acredita almacenamiento Azure. Las sucursales son las del seed P6, no usuarios reales Cancún/Circuito.

## Verificación local

| Comprobación | Resultado |
|---|---|
| `dotnet build Millet.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false` | Salida 0, 0 errores y 0 advertencias; compila las integraciones nuevas. |
| Unitarias API / Almacén / Compras / CxP / Tesorería / Identidad / Compartido | 40 / 326 / 565 / 444 / 132 / 113 / 100; total **1,720**, 0 fallidas y 0 omitidas. API y Almacén verificadas nuevamente después de la última corrección del endpoint. |
| `npx tsc --noEmit -p tsconfig.json` | Salida 0. |
| `npm run -s typecheck:test` | Salida 0. |
| `npm run -s lint` | Salida 0; 0 errores y 10 advertencias existentes. |
| `npx vitest run` | Salida 0; **364 archivos y 2,085 pruebas** en verde. |
| Cotejo literal de rutas | CxP/Tesorería 133; familias Almacén/trazabilidad 19; sin ausencias en las tablas. |
| `git diff --check` | Sin errores. |

Logs y ejecutor: [evidencia-p6b-adenda2](evidencia-p6b-adenda2/build.txt). VSTest falla antes de ejecutar por `SocketException (13): Permission denied`; las unitarias se ejecutan con el runner xUnit temporal en proceso ya utilizado en P6b, cargando las bibliotecas de cada suite. Frontend no tuvo cambios.

## Pendiente y siguiente acción

**Por confirmar:** integración PostgreSQL completa después de estas correcciones y aceptación con usuarios reales. El intento de `tools/validate-integration-isolated.sh` termina con salida 1 en `docker info`: acceso denegado al socket de OrbStack; no crea PostgreSQL ni ejecuta pruebas. No se usa una base compartida como alternativa.

Claude debe ejecutar **el gate completo** en PostgreSQL desechable, confirmar que la limpieza mantiene documentos/roles/catálogos aislados y revisar todos los fallos restantes. El reporte de entrada anuncia 179 fallos pero desglosa 176 + 2 = 178; el fallo restante no tiene detalle y queda **Por confirmar**.

Mensaje de commit propuesto, sin ejecutarlo conforme a las reglas comunes finales:

```text
fix(p6b): corregir limpieza JSON y proteger descarga de vales por sucursal
```
