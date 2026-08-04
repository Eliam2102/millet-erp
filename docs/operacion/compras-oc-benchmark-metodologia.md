# Perf benchmark — submódulo Órdenes de Compra (F10-PR2)

> Metodología para medir P50/P95 de queries pesadas con dataset de
> referencia. La medición real se ejecuta en staging cuando hay datos
> y se adjunta al PR-update; este documento define el procedimiento.

---

## Queries objetivo (P95 < 300ms)

| Query | Endpoint | Filtros típicos | Notas |
|---|---|---|---|
| `ListarPartidasAbiertasQuery` | `GET /ordenes/partidas-abiertas` | estado + fecha rango + proveedor | Cubierto por `ix_oc_partidas_abiertas` |
| `ListarOrdenesCompraQuery` | `GET /ordenes` | estado + fecha rango | Cubierto por `ix_oc_estado_fecha` (F10-PR2) |
| `ListarPendientesAutorizacionOcQuery` | `GET /ordenes/pendientes-autorizacion` | nivel | Cubierto por `ix_oc_pendientes_autorizacion` (F10-PR2) |
| `ObtenerArbolDocumentosQuery` | `GET /trazabilidad/arbol-documentos` | desde RQ o OC | Profundidad acotada |
| `ListarUltimas100ComprasQuery` | `GET /articulos/{id}/historial-compras` | artículo_id | Join sobre líneas — idx existente en `articulo_id` |
| `ObtenerKpisPartidasAbiertasQuery` | `GET /ordenes/partidas-abiertas/kpis` | mismos filtros que partidas | Una sola pasada con agregación condicional |

## Dataset de referencia

- **5 000 OCs activas** (mix de Borrador, EnAutorización, Autorizada con sub-estados parciales).
- **50 000 OCs históricas** (Cerrada, Cancelada, Rechazada).
- **Distribución**: 80% activas con sub-estado != Completa; 20% con alguna dimensión cerrada.
- Por OC: 3 líneas en promedio, 0–2 adjuntos, 0–4 autorizaciones.

Script de seed: `tools/perf/seed-5k-50k-ocs.csx` (pendiente de
implementar — se hace cuando exista staging con DB Postgres dedicada).

## Procedimiento

1. **Aplicar migraciones** en una BD limpia con los índices de
   F7-PR1 (`ix_oc_partidas_abiertas`) + F10-PR2
   (`ix_oc_estado_fecha`, `ix_oc_pendientes_autorizacion`,
   `ix_oc_origen`).
2. **Correr el seed** (50 000 + 5 000 OCs).
3. **Ejecutar 1000 requests por query** con clientes paralelos (e.g.
   `k6` o `bombardier`) variando filtros.
4. **Capturar P50 / P95 / P99** desde Application Insights
   (`requests | where name has X | summarize percentiles(duration, 50, 95, 99)`).
5. **Adjuntar reporte** al PR de seguimiento si alguna query supera
   300ms en P95 — agregar índice adicional o vista materializada.

## Resultados esperados (sin medir)

Con los índices actuales, las queries de bandeja deberían quedar bien
debajo de 100ms para datasets MVP (< 10k filas activas). El reporte de
partidas abiertas es el más exigente porque combina filtros sobre
sub-estados; el índice parcial cubre el caso pesimista.

Si la perf no es satisfactoria, el siguiente paso es una **vista
materializada** `compras.vw_partidas_abiertas` con refresh
incremental disparado por listeners (F10-PR2 follow-up).
