# P6 · Corrección de la integración tras la fusión · 9-oct-2026

Estado al iniciar: worktree limpio, rama `fix/P6-acceso-y-sucursal`, HEAD `687fe82`. La fusión ya estaba cerrada por Claude; no había conflictos ni cambios pendientes que recuperar. El commit `535c905` contiene P6 y la fusión conserva los cambios de main. No se rehízo ese trabajo.

Corrida PostgreSQL aportada por Eliam/Claude: API **946 aprobadas / 110 fallidas / 1,056 total**, A+W y Compras verdes. Son resultados externos anteriores a esta corrección. El nuevo resultado del gate completo está **Por confirmar**.

## Causas y correcciones

| Fallos reportados | Causa comprobada en código | Cambio |
|---|---|---|
| 107 de `P6SucursalEndpointsTests` | Su limpieza llamaba `ExecuteDeleteAsync` sobre `FacturaVenta`, entidad de una jerarquía TPT. EF no admite esa eliminación masiva. | Cargar únicamente las facturas creadas por el fixture, `RemoveRange` y `SaveChangesAsync`. Se conserva el mapeo TPT y el orden de limpieza entre módulos. |
| `P2_Endpoint_NivelDenegado_403`, niveles 1 y 2 | La guarda de sucursal buscaba un ID inexistente antes del permiso dinámico del nivel y devolvía 404. | Verificar identidad/empresa y permiso del nivel; luego sucursal; finalmente comando. Se conserva 403 `AUTORIZAR_NIVEL_DENEGADO`. |
| `P2_Cancelacion_RequierePermisoDelPaso_403`, solicitud | La solicitud de cancelación consultaba el documento antes de comprobar el permiso N1. | Mismo orden; se conserva 403 `OC_CANCELAR_DOBLE_DENEGADO`. La resolución ya exige N2 mediante policy antes de entrar al endpoint y conserva su guarda. |

El patrón vigente de endpoints con `RequireAuthorization(PermissionPolicyProvider.Prefix + permiso)` valida permiso antes del cuerpo del endpoint. Se aplica esa misma precedencia a los permisos dinámicos anteriores. **No** se cambia el fixture P2 para esconder el error: su ID inexistente comprueba que la falta de permiso gane frente a la consulta del documento.

La autorización de RQ tenía el mismo orden defectuoso; se movió únicamente su guarda, con regresiones de ambos niveles. Con permiso de operación: un documento inexistente sigue devolviendo 404 y uno ajeno devuelve 403 `SUCURSAL_NO_ASOCIADA`. Las guardas permanecen antes del comando; firmas, cancelación y reglas de negocio no se reescriben.

El cuerpo genérico de las pruebas territoriales ahora incluye `Nivel = 1`: al validar nivel antes de sucursal, omitirlo daría `NIVEL_INVALIDO` y no ejercitaría el bloqueo territorial solicitado.

## Cobertura nueva y revisión del paquete

Nueve casos adicionales en `P6SucursalEndpointsTests`:

- Cinco casos de permiso denegado: OC N1/N2, solicitud de cancelación N1 y RQ N1/N2. Cada caso verifica el código específico frente a ID inexistente, documento propio y ajeno.
- Cuatro casos con permiso válido: RQ/OC N1/N2 mantienen el 403 territorial del documento ajeno.

Se conservan las pruebas P2 de camino válido y las 107 pruebas P6 existentes, incluida la escritura propia/corporativa, listados y adjuntos. La limpieza corregida se ejecuta al finalizar cada fixture; su resultado PostgreSQL se verificará en el gate.

La comparación contra los seis puntos de la ficha confirma que ya existen en el commit P6: exclusividad de super-admin/razón social, roles por grupos Entra y revocación, exportación CSV auditada, activación de formas SAT y validación de Caja/Facturación, adjuntos de RQ/factura y alcance territorial. Su descripción y cobertura están en [RESUMEN.md](RESUMEN.md); no se agregaron implementaciones duplicadas.

Se revisaron las guardas de grupo y de referencias en CxP/CxC/Tesorería, las rutas nuevas de main y el inventario conservado en [inventario-sucursales.md](inventario-sucursales.md). Incluye facturas/evidencias/CFDI, anticipos/NC/cargos, comprobaciones/reposiciones/TC, cartera/propuestas/cobranza, pasivos/pagos/depósitos/movimientos/REPP y reportes P5/P8. Las rutas de resolución de cancelación, reclasificación, desvinculación y auxiliar de proveedores conservan sus controles. Esta continuación no modifica la lógica P3/P5/P8 ni sus reportes.

La prueba `DemoSesionSeedTests` conserva la regresión HTTP del usuario DEMO Compras: RQ/OC MID permitidas, OC MTY bloqueada y ausente del listado. No se cambiaron usuarios, seed ni permisos de demo. El ensayo vivo del lunes sigue **Por confirmar**.

## Validación local actual

| Check | Resultado |
|---|---|
| `dotnet build Millet.sln --no-restore -m:1 /nr:false /p:UseSharedCompilation=false --nologo` | exit 0; 0 errores, 0 advertencias; 29.72 s; incluye integración compilada |
| 15 suites unitarias backend | 3,277 aprobadas; 0 fallidas; 0 omitidas |
| `npx tsc --noEmit -p tsconfig.json` | exit 0 |
| `npx tsc --noEmit -p tsconfig.app.json` | exit 0 |
| `npm run -s typecheck:test` | exit 0 |
| `npm run -s lint` | exit 0; 0 errores, 10 advertencias preexistentes de hooks |
| `npx vitest run --maxWorkers=8 --reporter=verbose` | exit 0; 361 archivos, 2,078 pruebas aprobadas; 140.27 s |
| `git diff --check` | exit 0 |

[Evidencias de esta corrida](evidencia-adenda3/). Backend usa `DOTNET_CLI_HOME=/tmp/p6-dotnet` y build usa `MSBUILDDISABLENODEREUSE=1`. VSTest estándar se intentó y abortó por `SocketException (13): Permission denied`. Se reutilizó el runner oficial xUnit en proceso de la continuación anterior, con `AssemblyDependencyResolver`, ruta base en el directorio compilado y código de salida comprobado en cada suite. No se instalaron paquetes ni se modificó la configuración de tests. El runner está documentado en [evidencia-fusion/runner.cs.txt](evidencia-fusion/runner.cs.txt).

Docker volvió a rechazar el acceso a `/Users/eliamcv/.orbstack/run/docker.sock`. No se ejecutaron las aserciones HTTP PostgreSQL ni el gate completo aquí. No se convierte el verde unitario en verde de integración. Los tests nuevos están escritos y compilados.

## Archivos de código modificados

- `backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs`: mover dos guardas después de permisos dinámicos.
- `backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs`: misma corrección en autorización por nivel.
- `backend/tests/Api.IntegrationTests/P6/P6SucursalEndpointsTests.cs`: limpieza TPT, nivel válido y nueve regresiones.

Documentación: esta nota, encabezados de RESUMEN/fusión/inventario, actualización preparada para Obsidian y evidencia de la corrida. `P6DesignTimeDbContexts.cs` sigue leyendo `ConnectionStrings__Postgres`; no se editó. No hay nuevas migraciones ni cambios de modelo. `P6-fallos-integracion.txt` ya estaba ausente al iniciar y no se recreó.

## Pendientes y siguiente acción

1. Claude: ejecutar `./tools/validate-integration-isolated.sh` completo desde este mismo worktree y reportar API, Compras y A+W con aprobadas/fallidas/omitidas. Resolver cualquier rojo restante. Las nueve regresiones adicionales aumentan los casos de API respecto del corte externo; el total efectivo lo dará el gate.
2. Después del verde: ensayo de demo y pruebas Millet, incluyendo operativo/corporativo, grupo Entra, revocación, CSV filtrado, desactivar/reactivar 02 y adjuntos. Entra/Graph y almacenamiento reales siguen **Por confirmar**.
3. Incorporar [actualizacion-boveda.md](actualizacion-boveda.md) a Obsidian desde una sesión con permiso de escritura. No se aplicó fuera del worktree.

No se tocaron `infra/` ni `.env*`; no hubo servidores, push, despliegue, mensajes ni mutaciones externas.

Mensaje de commit autorizado por la adenda 3: `fix(administracion): corregir limpieza y precedencia de permisos de P6`. Resultado del intento de commit documentado en la evidencia y en el cierre de esta nota.

El intento de `git add` terminó con exit 128: no se pudo crear `millet_erp-review/.git/worktrees/millet_erp-P6-ADM/index.lock` (`Operation not permitted`), fuera de las raíces de escritura de esta sesión. No se ejecutó `git commit` después del fallo de preparación. Los cambios quedan sin staging ni commit, listos para que Claude los prepare y registre desde este mismo worktree. HEAD conserva `687fe82`; no hay fusión pendiente. [Error exacto](evidencia-adenda3/git-commit.txt).
