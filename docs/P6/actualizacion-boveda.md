# Cambio preparado para la bóveda · P6 · 9-oct-2026

No aplicado a Obsidian: la sesión autoriza escribir únicamente dentro de `millet_erp-P6-ADM` y temporales. Conserva las notas históricas al incorporar esta actualización.

Destino: Módulo 01, fichas F1-ADM-01/02/03/04/11/12 y Bitácora. Fuente: ficha y adenda de Eliam del 9-oct, código sin commit de `fix/P6-acceso-y-sucursal`, [RESUMEN.md](RESUMEN.md), [inventario-sucursales.md](inventario-sucursales.md) y [fallos-integracion-09oct.md](fallos-integracion-09oct.md).

Estado a registrar: implementación local de exclusividad super-admin para empresas, roles por grupos Entra recalculados al login y revocación inmediata por desactivación, CSV de auditoría registrado, formas de pago activables con validación y selectores de Caja, adjuntos RQ/factura proveedor y guardas/filtros territoriales. Compilación y unitarias verificadas; no publicar estas funciones como aceptadas por Millet ni integradas en `main`.

La corrida PostgreSQL aportada por Claude obtuvo 861/966 API y 105 fallos. Se corrigieron las causas identificadas: fixture caja chica (98 casos), aserciones JSONB (2), contexto de empresa (1), fixtures sin sucursal de documento (3), incompatibilidad del bypass de adjuntos OC (1). La nueva corrida PostgreSQL, Entra/Graph real, almacenamiento real, revisión visual en navegador y aceptación Millet quedan **Por confirmar**.

Decisiones a conservar: D15 de esta ficha autoriza roles por grupos Entra; no confundir el identificador con otra D15 histórica de la bóveda. La sucursal del documento define alcance territorial, independientemente del centro de costo. Adjuntos OC conservan su bypass previo por la adenda. No se cambiaron las reglas de saldo, firmas, cancelación o conciliación de las ramas paralelas.

Siguiente responsable técnico: Claude, gate completo PostgreSQL. Tras el verde, ensayo con usuarios operativos/corporativos y aceptación de Millet. Migraciones, merge, despliegue y actualizaciones externas no ejecutados.
