# Cambio preparado para la bóveda · P6 · 9-oct-2026

No aplicado a Obsidian: la sesión autoriza escribir únicamente dentro de `millet_erp-P6-ADM` y temporales. Conserva las notas históricas al incorporar esta actualización.

Destino: Módulo 01, fichas F1-ADM-01/02/03/04/11/12 y Bitácora. Fuente: ficha y adenda de Eliam del 9-oct, código sin commit de `fix/P6-acceso-y-sucursal`, [RESUMEN.md](RESUMEN.md), [inventario-sucursales.md](inventario-sucursales.md) y [fallos-integracion-09oct.md](fallos-integracion-09oct.md).

Estado a registrar: implementación local de exclusividad super-admin para empresas, roles por grupos Entra recalculados al login y revocación inmediata por desactivación, CSV de auditoría registrado, formas de pago activables con validación y selectores de Caja, adjuntos RQ/factura proveedor y guardas/filtros territoriales. Compilación y unitarias verificadas; no publicar estas funciones como aceptadas por Millet ni integradas en `main`.

La corrida PostgreSQL aportada por Claude obtuvo 861/966 API y 105 fallos. Se corrigieron las causas identificadas: fixture caja chica (98 casos), aserciones JSONB (2), contexto de empresa (1), fixtures sin sucursal de documento (3), incompatibilidad del bypass de adjuntos OC (1). La nueva corrida PostgreSQL, Entra/Graph real, almacenamiento real, revisión visual en navegador y aceptación Millet quedan **Por confirmar**.

Decisiones a conservar: D15 de esta ficha autoriza roles por grupos Entra; no confundir el identificador con otra D15 histórica de la bóveda. La sucursal del documento define alcance territorial, independientemente del centro de costo. Adjuntos OC conservan su bypass previo por la adenda. No se cambiaron las reglas de saldo, firmas, cancelación o conciliación de las ramas paralelas.

Siguiente responsable técnico: Claude, gate completo PostgreSQL. Tras el verde, ensayo con usuarios operativos/corporativos y aceptación de Millet. Migraciones, merge, despliegue y actualizaciones externas no ejecutados.

## Continuación de la fusión con main · 9-oct

Anterior → nuevo: trabajo P6 sin commit → Claude realizó el commit `535c905`. Fusión pendiente con `b72f990`: los siete archivos conservan la lógica main de P3/P5/P8 y reciben únicamente alcance de sucursal/adjuntos P6. La nueva resolución de cancelaciones OC incorpora guarda; reportes P5 filtran cuentas completas para conservar sus saldos. P8 usa su lector histórico con guarda y permiso corporativo propios, incluido el auxiliar nuevo. Confirmar/rechazar CxC ahora corresponde a depósitos de Tesorería por P5.

Fuente y resultados actuales: [fusion-main-09oct.md](fusion-main-09oct.md). El sandbox impide preparar el índice y cerrar la fusión; intentos `git add`/`git commit --no-edit` rechazados por acceso a `index.lock` fuera del worktree. Gate PostgreSQL, integración en main, Entra/Graph real y aceptación Millet: **Por confirmar**. Preparado para incorporar en Módulo 01, fichas ADM-01/02/03/04/11/12 y Bitácora; esta continuación no escribió en la bóveda.

## Adenda 3 · integración después de la fusión · 9-oct

Anterior → nuevo: fusión pendiente → commit `687fe82` cerrado por Claude, verificado con worktree limpio al inicio. Último gate externo aportado: API 946 aprobadas / 110 fallidas / 1,056 total; A+W y Compras verdes. Se corrige limpieza TPT de FacturaVenta (107 fallos compartidos) sin cambiar el mapeo, y precedencia de permiso del nivel/solicitud de cancelación frente a guarda territorial (3 fallos P2). Se corrige también el mismo orden en autorización de RQ y se agregan nueve casos de regresión compilados. Con permiso válido permanece el 403 por sucursal ajena.

Validación local posterior: build 0 errores/advertencias; 15 suites unitarias, 3,277 aprobadas sin fallos/omisiones; TypeScript y tipos de pruebas exit 0; lint 0 errores/10 advertencias preexistentes; Vitest 361 archivos/2,078 pruebas aprobadas. PostgreSQL posterior **Por confirmar** por acceso denegado al socket Docker; siguiente acción de Claude: gate completo sin filtros. Fuente: [adenda3-integracion-09oct.md](adenda3-integracion-09oct.md) y [evidencia-adenda3](evidencia-adenda3/). No declarar integración en main ni aceptación Millet. Esta actualización continúa preparada localmente, sin escritura en la bóveda.
