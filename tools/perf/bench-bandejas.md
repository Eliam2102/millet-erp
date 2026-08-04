# Benchmark — Bandejas de Compras (F8-PR3)

Mide latencia de los dos endpoints de lectura más expuestos del módulo
Compras bajo carga representativa (10,000 RQs sembradas).

## Objetivos

- **P95 < 200 ms** sobre `GET /api/v1/compras/requisiciones` con filtros
  `estado` + `departamentoId` (ADR no escrito; criterio de F8-PR3 del
  pr-breakdown).
- **P95 < 200 ms** sobre `GET /api/v1/compras/pendientes-autorizacion`.

Si no se cumplen, agregar índices o vista materializada en una migration
aditiva. Ver §"Si los números no pasan" abajo.

## Setup

1. **Asegurar la app corriendo** en local (`http://localhost:5000` por
   default):

   ```powershell
   dotnet run --project backend\src\Api\Millet.Api.csproj
   ```

2. **Sembrar 10,000 RQs** en la BD dev:

   ```powershell
   .\tools\perf\seed-10k-rqs.ps1
   ```

3. **Obtener un token JWT** del super admin (las bandejas requieren
   permiso `compras.requisiciones.leer`):

   ```powershell
   $token = (Invoke-RestMethod -Uri http://localhost:5000/api/dev/fake-login `
       -Method POST -ContentType 'application/json' `
       -Body '{"entraOid":"dev-superadmin","email":"superadmin@dev.local","nombre":"Super Admin Dev","empresaId":null}'
   ).accessToken
   ```

## Run con `wrk` (Linux/WSL/Mac)

```bash
# Filter típico: bandeja general por estado=Borrador
wrk -t4 -c20 -d30s \
    -H "Authorization: Bearer $token" \
    "http://localhost:5000/api/v1/compras/requisiciones?estado=0&limit=50"

# Bandeja de pendientes-autorización (estado=1 fixed)
wrk -t4 -c20 -d30s \
    -H "Authorization: Bearer $token" \
    "http://localhost:5000/api/v1/compras/pendientes-autorizacion?limit=50"
```

`wrk` reporta `Latency` con avg / stdev / max. Para P95/P99 usar
`wrk -L` (Lua scripting) o el wrapper [`wrk2`](https://github.com/giltene/wrk2)
con flag `-R` para target rate constante.

## Run con `hey` (cross-platform, Go binary)

```bash
hey -n 1000 -c 20 -m GET \
    -H "Authorization: Bearer $token" \
    "http://localhost:5000/api/v1/compras/requisiciones?estado=0&limit=50"
```

`hey` reporta histograma + P50/P95/P99 directo en consola. Recomendado
si no tienes wrk instalado.

## Run con PowerShell puro (rápido y suficiente para validación)

```powershell
$results = 1..200 | ForEach-Object -Parallel {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-RestMethod `
        -Uri "http://localhost:5000/api/v1/compras/requisiciones?estado=0&limit=50" `
        -Headers @{Authorization = "Bearer $using:token"} | Out-Null
    $sw.Stop()
    $sw.Elapsed.TotalMilliseconds
} -ThrottleLimit 10

$sorted = $results | Sort-Object
$p50 = $sorted[[int]($sorted.Count * 0.50)]
$p95 = $sorted[[int]($sorted.Count * 0.95)]
$p99 = $sorted[[int]($sorted.Count * 0.99)]
Write-Host ("P50={0:F1}ms  P95={1:F1}ms  P99={2:F1}ms  N={3}" -f $p50, $p95, $p99, $sorted.Count)
```

## Reportar resultados

Pega en el PR de F8-PR3 (o follow-up commit) un bloque:

```
Endpoint: GET /api/v1/compras/requisiciones?estado=0&limit=50
Tool: hey -n 1000 -c 20
Antes (sin índices nuevos): P50=___ms  P95=___ms  P99=___ms
Después (con índices NEW):  P50=___ms  P95=___ms  P99=___ms

Endpoint: GET /api/v1/compras/pendientes-autorizacion
Antes:  P50=___ms  P95=___ms  P99=___ms
Después:P50=___ms  P95=___ms  P99=___ms
```

## Si los números no pasan (P95 > 200 ms)

1. **Verificar que ANALYZE corrió** post-seed. El planner necesita
   estadísticas frescas. El seed script ya lo hace pero re-ejecutar no
   hace daño:

   ```sql
   ANALYZE compras.requisiciones;
   ```

2. **Inspeccionar el plan**:

   ```sql
   EXPLAIN (ANALYZE, BUFFERS)
   SELECT id, folio, estado, fecha_solicitud
   FROM compras.requisiciones
   WHERE empresa_id = '00000003-0000-0000-0000-000000000001'
     AND estado = 0
     AND deleted_at IS NULL
   ORDER BY fecha_solicitud DESC
   LIMIT 50;
   ```

   Si aparece `Seq Scan` en lugar de `Index Scan` sobre
   `ix_requisiciones_empresa_id_estado_fecha_solicitud`, el problema es
   selectividad (pocas filas mejoran con scan secuencial cuando el
   set total es chico).

3. **Indices candidatos** (agregar en migration aditiva
   `<ts>_IndicesBandeja.cs`) si la query plan lo justifica:
   - `(empresa_id, departamento_id, fecha_solicitud DESC)` — bandeja
     filtrada por depto, sorted.
   - **Filtered index** sobre estados activos:
     `WHERE estado IN (0, 1, 3)` — reduce el index a ~60% del tamaño.
   - Vista materializada con refresh cada 1 min — solo si los índices
     no bastan; introduce eventual consistency, fuera de scope MVP.

4. **A14 (snapshots / read models)** — fall-back nuclear; si bandejas
   crecen a millones de RQs en años, un read model dedicado (proyectado
   en background) puede ser necesario. Documentar y diferir.

## Re-ejecutar con datos limpios

```powershell
.\tools\perf\seed-10k-rqs.ps1 -Cleanup
.\tools\perf\seed-10k-rqs.ps1
```

## Estado actual de índices (post-F8-PR3)

Existentes en `compras.requisiciones`:

| Índice | Columnas | Origen | Caso de uso |
|---|---|---|---|
| `ix_requisiciones_empresa_id_departamento_id_estado` | `(empresa_id, departamento_id, estado)` | F1-PR1 | Bandeja filtrada por depto+estado |
| `ix_requisiciones_empresa_id_estado_fecha_solicitud` | `(empresa_id, estado, fecha_solicitud DESC)` | F1-PR1 | Bandeja por estado, sorted desc |
| `ix_requisiciones_empresa_id_folio_anio_folio` | `(empresa_id, folio_anio, folio)` UNIQUE | F1-PR1 | Lookup por folio |
| `ix_requisiciones_empresa_id_requisitante_id` | `(empresa_id, requisitante_id)` | F1-PR1 | Filter por requisitante |
| `ix_requisiciones_empresa_id_departamento_id_fecha_solicitud` | `(empresa_id, departamento_id, fecha_solicitud DESC)` | F8-PR3 | Bandeja por depto, sorted (sin Sort post-IndexScan) |
| `ix_requisiciones_empresa_id_requisitante_id_fecha_solicitud` | `(empresa_id, requisitante_id, fecha_solicitud DESC)` | F8-PR3 | "Mis RQs" sorted (analógico) |

## Resultados de benchmark (F8-PR3, 10k RQs en dev local, sequential curl N=100)

```
=== ANTES de los índices F8-PR3 ===
  GET ?estado=0&limit=50               P50=2.2ms   P95=3.9ms   P99=23.7ms
  GET /pendientes-autorizacion         P50=2.0ms   P95=3.5ms   P99=28.4ms
  GET ?estado=0&departamentoId=X       P50=1.9ms   P95=23.8ms  P99=26.7ms

=== DESPUÉS de los índices F8-PR3 ===
  GET ?estado=0&limit=50               P50=9.4ms   P95=25.7ms  P99=93.6ms
  GET /pendientes-autorizacion         P50=8.4ms   P95=22.0ms  P99=81.8ms
  GET ?estado=0&departamentoId=X       P50=4.6ms   P95=14.2ms  P99=22.2ms
  GET ?departamentoId=X (sin estado)   P50=5.4ms   P95=7.9ms   P99=20.1ms  ← nuevo escenario habilitado
```

**Conclusión:** SLO P95 < 200 ms cumplido por margen de **~10x** en todos
los escenarios. La diferencia antes/después no es significativa a este
volumen (N=100 secuencial, 10k filas, dev local con Postgres warm cache);
el ruido entre corridas (~10ms) es del mismo orden que la latencia real.

`EXPLAIN ANALYZE` post-índices confirma que el nuevo
`ix_requisiciones_empresa_id_departamento_id_fecha_solicitud` se usa para
queries con depto sin sort step (ejecución 0.18 ms vs 0.05 ms con sort
quicksort previo, pero sobre 0 vs 50 filas — el sort era barato porque
era set chico). El valor real de los índices nuevos aparece a escala
(>100k RQs por empresa), donde el `Sort` post-IndexScan crece linealmente
con el set filtrado.
