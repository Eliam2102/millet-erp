# P6 · Periodo del fixture y roles del bootstrap · 9-oct-2026

La continuación empezó con worktree limpio, rama `fix/P6-acceso-y-sucursal` y HEAD `fa4c0df`. P6 y la fusión ya estaban registrados en `535c905`, `687fe82` y `fa4c0df`; se conservó ese trabajo. No había conflictos ni cambios locales interrumpidos que recuperar.

Último resultado PostgreSQL aportado por Eliam/Claude: API **947 aprobadas / 118 fallidas / 1,065 total** (116 del fixture P6 y 2 de bootstrap). Es una corrida externa anterior a estos cambios. El resultado PostgreSQL posterior continúa **Por confirmar**.

## Causas y correcciones

| Fallos reportados | Causa verificada en el código actual | Corrección |
|---|---|---|
| 116 en `P6SucursalEndpointsTests` | `CuentasPorPagarDbContext.SaveChangesAsync` consulta el puerto real de periodos de P8. La preparación crea facturas, anticipos y notas con fecha actual, sin abrir ese periodo. | Factory de prueba exclusiva de P6 con `IPeriodoContablePort` de CxP que admite movimientos. El host se comparte dentro de la suite; el reemplazo no se propaga a otras suites. |
| `Bootstrap_Should_Seed_Nine_RolesMvp` | La prueba solo consultaba la primera página de 100 roles ordenados por código. Los roles temporales aumentan el catálogo y desplazan `super-admin` fuera de esa página. | Recorrer todas las páginas con `offset` y `total`; mantener la exigencia de todos los códigos originales. |
| `RolesCxpYTesoreria_Should_Tener_Permisos_Bancarios_Esperados` | El mismo lector de primera página buscaba `tesoreria` mediante `First`; al quedar fuera de la página lanzaba «Sequence contains no matching element». | Buscar en el conjunto completo, con `Single`, conservando todas las aserciones bancarias positivas y negativas. |

La contaminación tiene dos fuentes concretas: la preparación P6 creaba usuario, rol, RQ y OC antes del error de periodo, y su limpieza solo se adquiría cuando `PrepararAsync` terminaba; además, `ConfiguracionPacEndpointsTests` crea los roles `adm09-…` mencionados en el fallo, sin eliminarlos. Se corrigen ambas fuentes: P6 registra los IDs antes de persistir y limpia también si falla parcialmente; ADM-09 conserva sus IDs/OIDs y elimina únicamente sus usuarios y roles al terminar cada prueba, incluso si falla. No se borran datos ajenos ni se ajustan conteos esperados para tolerar residuos.

La lectura paginada sigue siendo necesaria aunque todos los fixtures se limpien: el catálogo real puede tener más de 100 roles. `RolesMvp_Should_Be_EsDelSistema` ahora también exige encontrar cada rol antes de comprobar su marca; ya no puede pasar si falta un rol en la página. No se debilitó el bootstrap ni se cambiaron sus permisos de producción.

Tesorería y Facturación ya registran puertos de periodo que admiten movimientos en este corte; CxC no contiene un puerto equivalente. No se añadieron reemplazos innecesarios. La regla contable P8, la regla de destino de caja chica y el mapeo TPT siguen intactos. `P6DesignTimeDbContexts.cs` conserva `ConnectionStrings__Postgres` del entorno.

## Regresiones agregadas

- `Preparacion_fallida_limpia_documentos_y_rol_temporal_y_conserva_roles_del_sistema`: provoca un error después de guardar RQ/OC y comprueba que sus IDs, los roles y las concesiones de los roles del sistema vuelven a la fotografía anterior.
- `Roles_del_sistema_y_permisos_bancarios_se_verifican_mas_alla_de_la_primera_pagina`: crea 101 roles ficticios que preceden al seed, demuestra que `super-admin` no está en la primera página y ejecuta las verificaciones completas de códigos, marca del sistema y permisos bancarios. Limpia solo esos 101 IDs en `finally`.

Ambas pruebas están escritas y compiladas; sus aserciones PostgreSQL no se ejecutaron aquí.

## Comparación contra el paquete P6

Se revisaron nuevamente las seis familias de la ficha contra el código actual:

1. Exclusión de crear/desactivar empresa en bootstrap y `PermissionLoader`, con razón social de sesión en Sucursales y pruebas de ausencia de alta.
2. Roles por grupos Entra en el cargador, renovación de pertenencia al iniciar sesión e invalidación de caché al desactivar rol; pruebas de claims directos/overage, salida del grupo y revocación.
3. Exportación CSV con filtros, autorización y registro, y botón de Auditoría; pruebas existentes de CA1.2, CA1.6 y alta/cambio SAT auditado.
4. Activación de formas SAT, listado administrativo e interruptor; guardas del catálogo activo en Caja y Facturación y prueba de desactivar/reactivar 02.
5. Propietarios y permisos de adjuntos de RQ y factura proveedor, con sus secciones de detalle y pruebas de permiso/sucursal.
6. Guardas y filtros territoriales en Compras/CxP/CxC/Tesorería, incluida la precedencia de permiso dinámico corregida en `fa4c0df` y las rutas incorporadas por main.

El [inventario de endpoints](inventario-sucursales.md) conserva el detalle por ruta. Comprende RQ/líneas/OC/PDF/adjuntos; CxP facturas, evidencias, CFDI, anticipos, notas, comprobaciones, reposiciones, TC y reportes; CxC cartera, propuestas, cobranza, alertas y crédito; Tesorería pasivos, pagos, depósitos, movimientos, REPP y reportes. Las rutas nuevas de resolución de cancelación, reclasificación, desvinculación de aplicaciones y auxiliar de proveedores mantienen su control. Esta ronda cambia solo pruebas y documentación; la lógica de P3/P5/P8 y los endpoints de producción no se editaron.

`DemoSesionSeedTests` conserva la regresión HTTP de Compras DEMO: MID permitida, MTY bloqueada y excluida del listado. No se cambiaron usuarios ni datos de la demo. Su ejecución PostgreSQL y el ensayo del lunes con Millet siguen **Por confirmar**.

## Validación local actual

| Check | Resultado |
|---|---|
| `dotnet build Millet.sln --no-restore -m:1 /nr:false /p:UseSharedCompilation=false --nologo` | exit 0; 0 errores, 0 advertencias; 7.92 s; incluye integración compilada |
| 15 suites unitarias backend, runner oficial xUnit en proceso | 3,277 aprobadas; 0 fallidas; 0 omitidas; exit 0 en cada suite |
| `npx tsc --noEmit -p tsconfig.json` | exit 0 |
| `npx tsc --noEmit -p tsconfig.app.json` | exit 0 |
| `npm run -s typecheck:test` | exit 0 |
| `npm run -s lint` | exit 0; 0 errores, 10 advertencias preexistentes |
| `npx vitest run --maxWorkers=8 --reporter=verbose` | exit 0; 361 archivos y 2,078 pruebas aprobadas; 115.54 s |
| `git diff --check` | exit 0 |
| `./tools/validate-integration-isolated.sh` | exit 1 antes de preparar PostgreSQL: socket Docker denegado |

Evidencias: [evidencia-adenda4](evidencia-adenda4/). VSTest estándar se intentó y abortó por `SocketException (13): Permission denied`; se usó el mismo runner oficial xUnit documentado en [runner.cs.txt](evidencia-fusion/runner.cs.txt), con resolución de dependencias y directorio base del assembly. No se instalaron paquetes ni se alteró la configuración de los proyectos para ejecutarlo. El verde unitario no acredita las aserciones HTTP PostgreSQL.

## Archivos de código de esta ronda

- `backend/tests/Api.IntegrationTests/P6/P6SucursalEndpointsTests.cs`: factory con periodo admitido, preparación/limpieza parcial y regresión de residuos.
- `backend/tests/Api.IntegrationTests/Identidad/BootstrapRolesMvpTests.cs`: paginación completa, comprobación explícita de cada rol del sistema y regresión con 101 roles.
- `backend/tests/Api.IntegrationTests/IntegracionesFiscal/ConfiguracionPacEndpointsTests.cs`: limpieza de usuarios/roles temporales ADM-09 y disposición de su host.

Documentación: esta nota, encabezado vigente de `RESUMEN.md`, manifest, actualización preparada para la bóveda y evidencias. No se agregaron migraciones. `P6-fallos-integracion.txt` estaba ausente al iniciar y sigue ausente. No se tocaron `infra/`, `.env*` ni otros worktrees; no hubo servidores, push, despliegues ni mutaciones externas.

## Pendientes y siguiente acción

1. Claude: ejecutar `./tools/validate-integration-isolated.sh` completo, sin filtros, desde este worktree y entregar conteos exactos de API, Compras y A+W. Los dos casos nuevos cambian el total esperado respecto del corte externo; el gate dará el total efectivo. Confirmar los 116 casos territoriales anteriores, bootstrap y ausencia de residuos tras la preparación fallida.
2. Después del verde: ensayo DEMO y pruebas Millet de permisos, sucursal propia/ajena/corporativa, grupos Entra, revocación, CSV filtrado, forma 02 y adjuntos. Entra/Graph real, almacenamiento y aceptación: **Por confirmar**.
3. Incorporar [actualizacion-boveda.md](actualizacion-boveda.md) desde una sesión con escritura en la bóveda. Aquí quedó preparado dentro del worktree.

Mensaje de commit autorizado por la adenda 4: `fix(administracion): aislar periodo y limpiar roles en pruebas P6`. El resultado del intento está en [git-commit.txt](evidencia-adenda4/git-commit.txt).

El intento de `git add` terminó con exit 128: no pudo crear `millet_erp-review/.git/worktrees/millet_erp-P6-ADM/index.lock` (`Operation not permitted`), fuera de las raíces de escritura. `git commit` no se ejecutó. Los cambios quedan sin staging ni commit; HEAD conserva `fa4c0df`. Claude puede preparar y registrar estos mismos archivos desde este worktree.
