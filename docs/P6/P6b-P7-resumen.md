# P6b · cierre local de la adenda P7

Corte: 09-oct-2026, `fix/P6b-sucursal-rutas-p4`, base `b8cceaa`. El worktree llegó limpio; P4 ya estaba en `a40b134` y P7 integrado. No se hizo commit, push, cambio de infraestructura ni de entorno.

## Conseguido

- Se preservó P4 y se recotejaron las 133 rutas CxP/Tesorería: las 37 ausencias previas están documentadas; ninguna queda sin fila en el inventario actualizado.
- Se controlaron 14 rutas existentes de Almacén vinculadas a P7 (recepción, salida, vale, regularización y reorden). Permiso de operación primero, alcance territorial después. Los tres listados filtran antes de conteo/paginación.
- Trazabilidad ahora controla también recepción en raíz y nodos relacionados, incluidas las entradas por factura y pago. Un árbol mixto se rechaza completo, conforme a P6.
- El nuevo puerto de Almacén reúne sucursales de bins y documentos persistidos mediante puertos públicos. Sin origen verificable se exige corporativo. Se agregaron seis permisos y migración con snapshot/metadata de EF; no tablas nuevas ni asignaciones a roles operativos.
- Serie de anticipo por proveedor: configuración de empresa/proveedor, sin sucursal; conserva `anticipos.leer` en GET y `anticipos.capturar` en PUT. Reorden sí tiene destino N1/N2 y requiere guarda. Motor automático, apartados, conversiones, obra y eventos conservan las reglas P4/P7.

El detalle de rutas y decisiones está en [inventario-sucursales.md](inventario-sucursales.md). Los archivos cambiados están en [archivos-cambiados.txt](evidencia-p6b-p7/archivos-cambiados.txt): endpoints y guardas API, tres consultas Almacén, lector público, permisos/migración Identidad, fixture P6, dos archivos nuevos de pruebas y ajuste del stub unitario API. Frontend no tiene cambios.

## Pruebas y evidencia

Se agregaron 10 unitarias de aislamiento y casos de integración por detalle/alta/mutación, prioridad de permiso, N1/N2, bandejas/avisos, cinco entradas del árbol, nodo relacionado ajeno, bin/CFDI ajeno, documento sin líneas y obra RQ. Los casos reutilizan el fixture P6 y periodo abierto para CxP/Almacén. Las sucursales son `MID`/`MTY` del seed ficticio; no se simula una validación real de usuarios Cancún/Circuito. Cada prueba limpia datos exclusivos y Outbox por IDs.

| Verificación | Resultado |
|---|---|
| `dotnet build Millet.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false` | 0 errores, 0 advertencias. Incluye las integraciones nuevas. |
| Unitarias Almacén / Compras / CxP / Tesorería | 326 / 565 / 444 / 132 verdes. |
| Unitarias API / Identidad / Compartido | 40 / 113 / 100 verdes. |
| Total unitarias | 1,720; 0 fallidas y 0 omitidas. |
| TypeScript y tipos de pruebas | Salida 0 ambos. |
| Lint | Salida 0; 0 errores, 10 advertencias existentes. |
| Vitest | 364 archivos, 2,085 pruebas verdes. |
| Modelo Identidad | Sin cambios pendientes desde la migración. |
| `git diff --check` | Sin errores. |

Los logs están en [evidencia-p6b-p7](evidencia-p6b-p7/build.txt). VSTest no puede abrir su socket en este sandbox; las unitarias se ejecutaron con xUnit en proceso, usando el cargador de dependencias y bibliotecas nativas de cada suite. El código del [ejecutor](evidencia-p6b-p7/runner-xunit.cs) es temporal y no modifica la configuración de pruebas del repo. Se resolvieron los fallos de carga del primer ejecutor y las tres incidencias de compilación de las pruebas nuevas; la compilación final y las siete suites quedaron verdes.

## Pendiente y siguiente acción

**Por confirmar:** integración PostgreSQL, rojo/verde completo y aceptación territorial con usuarios reales. Docker devuelve `permission denied` al acceder a su socket; no se utilizó una base compartida ni se ejecutó integración contra producción.

Claude debe ejecutar `tools/validate-integration-isolated.sh` completo sobre esta rama, validar la migración y el rojo/verde de P6/P6b/P7, y corregir cualquier fallo de PostgreSQL antes de integrar. Estas evidencias locales no acreditan aceptación ni despliegue.

Mensaje de commit propuesto (sin ejecutarlo):

```text
fix(sucursal): completar el alcance de las rutas de P7 y la trazabilidad
```

## Borrador para ficha y Bitácora Obsidian

09-oct-2026 · P6b/adenda P7: sobre `b8cceaa`, P4 preservado; 133 rutas CxP/Tesorería cotejadas sin ausencias pendientes en inventario. Se agregan controles territoriales a 14 rutas Almacén y a recepción en todo el árbol RQ/OC/recepción/factura/pago. Seis permisos y migración Identidad. Build completo, 1,720 unitarias y 2,085 frontend verdes; lint sin errores (10 advertencias). Integraciones escritas/compiladas, pendientes del gate PostgreSQL de Claude por bloqueo Docker/VSTest. Datos de prueba ficticios MID/MTY; aceptación Cancún/Circuito Por confirmar. Sin commit ni push de esta continuación. Fuente: `docs/P6/P6b-P7-resumen.md` e inventario en este worktree. No se pudo aplicar a la bóveda por estar fuera del alcance de escritura.
