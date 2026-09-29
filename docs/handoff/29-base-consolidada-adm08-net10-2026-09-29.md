# Base consolidada de desarrollo · 29/09/2026

Esta conciliación reúne el trabajo publicado por Geovany, Uziel y la rama de migración, más los cambios paralelos del login y navegación. El árbol de código probado es `d08bbfd`; los ajustes siguientes a ese punto son documentación de entrega.

## Procedencia y conservación

| Fuente | Commit incorporado | Contenido |
| --- | --- | --- |
| `origin/main` | `3c3a319` | Base anterior, ADM-05, login visual y ADM-10 ya fusionados |
| `feature/f1-adm-08-centros-costo` | `7146430` | Trabajo de Uziel en ADM-08 y merge de proveedores, .NET 10 e idempotencia de Geovany |
| `codex/dotnet10-migration` | `1f10322` | Migración, idempotencia y sincronización con main |
| `codex/adm05-main-clean` | `d7d1fda` | Maestro de proveedores y corrección de RFC concurrentes |
| `codex/login-branding-clean` | `f2af061` | Título e iconos del ERP |
| `codex/adm10-navigation-clean` | `7b2a277` | Visibilidad de módulos y guard de rutas por permisos |

Las seis referencias son ancestros de la base consolidada. Frente a la rama de Uziel, la unión inicial sólo añade los nueve archivos de identidad visual y navegación que se publicaron después: su código de Centros de costo y sus correcciones se conservaron.

También se compararon los cambios locales de idempotencia, navegación y login. Las diferencias de idempotencia se explican por la corrección posterior de RFC, las pruebas ajustadas al comportamiento vigente de auditoría y una anotación de tipos de TypeScript. No se sustituyeron con versiones locales más antiguas. Las cuatro configuraciones personales de arranque y los borradores históricos permanecen en sus directorios originales; no son código pendiente de integración ni se publican con credenciales.

## Verificación ejecutada

| Comprobación | Resultado |
| --- | --- |
| Compilación completa Debug de la solución | 0 advertencias, 0 errores |
| Pruebas unitarias backend | 2,385 aprobadas en 12 proyectos |
| Integración A+W con fixtures | 7 aprobadas |
| Integración Compras | 131 aprobadas |
| Integración API, incluidos ADM-08, proveedores, permisos e idempotencia | 560 aprobadas |
| Migraciones de instalación limpia | 12 contextos aplicados en PostgreSQL desechable |
| Pruebas frontend | 1,663 aprobadas en 300 archivos |
| Build frontend y tipos de pruebas | Aprobados |
| Lint frontend | 0 errores; 10 advertencias preexistentes de hooks |
| Paquete Release de la API (`dotnet publish`) | Generado y arrancado localmente |
| `/health/live` y `/health/ready` del paquete Release | HTTP 200 |
| `POST /api/dev/fake-login` con JSON válido, en modo Entra | HTTP 404 |

## Actualización con datos anteriores

Se clonó la base local de prueba en `millet_consolidada_20260929`. La base original quedó intacta. Antes de aplicar M1 se guardaron los 57 Dim2 y 361 Dim3 y se crearon cuatro asignaciones ficticias a los equipos que cambian de padre.

Después de aplicar `ReconciliacionCatalogoM1`:

- 0 IDs de Dim2 y Dim3 anteriores perdidos.
- 0 padres de equipos históricos modificados.
- 0 asignaciones eliminadas o cambiadas; se conservaron las cuatro.
- 58 Dim2 y 366 Dim3 totales: se agregan registros y permanecen los históricos.
- Equipos activos comprobados: `CHDIR01 → 50DD00`, `VU056 → 20DD00`, `VU106 → 50DD00`, `VV060 → 40DD01`.

Los usuarios que deban operar sobre los nuevos IDs requieren asignación explícita al nuevo alcance. La prueba acredita conservación de las referencias antiguas; no otorga automáticamente permisos a los nuevos IDs. La reversa de M1 es manual según su diseño y requiere una copia previa.

## Base del equipo

Después de fusionar el PR de consolidación, las nuevas ramas de tarea se crean desde `origin/main` actualizado. No tomar como base una copia vieja de ADM-05, ADM-08 o de la migración. Para tareas ya iniciadas, incorporar el main actualizado a su rama y resolver cualquier diferencia preservando su trabajo sin publicar directamente sobre main.

```bash
git fetch origin
git switch main
git pull --ff-only origin main
git switch -c feature/<id-de-la-tarea>
```

Si el directorio tiene cambios locales, conservarlos antes de cambiar de rama. Instalar el SDK definido en `global.json` y aplicar las migraciones en una copia antes de actualizar una base con datos.

## Pendientes externos y límites

- La validación Bicep contra Azure requiere la identidad OIDC y las variables de repositorio `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` y `AZURE_SUBSCRIPTION_ID`, además de los recursos referenciados por el workflow. No se modificó ni desactivó ese control. Su fallo por configuración no acredita un defecto de compilación, pero tampoco permite declarar la infraestructura validada.
- La publicación de código en main es la base de desarrollo; el despliegue a Azure sigue siendo una acción manual posterior a la validación de infraestructura.
- El inicio de sesión completo con una cuenta Microsoft real, la provisión de cuentas y correo, A+W real y la aceptación operativa de Millet requieren evidencia externa. Las pruebas de A+W aquí usaron fixtures.
- Los documentos ADM-08 registran la decisión M1 comunicada al autor de esa tarea. Esta revisión técnica no constituye una nueva aprobación de Millet de su catálogo o de sus reglas contables.
- Persisten advertencias preexistentes de hooks y tamaño de bundles, sin errores de build ni de lint.
