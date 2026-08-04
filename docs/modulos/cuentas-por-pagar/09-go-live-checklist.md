# 09 — Go-live checklist (Cuentas por Pagar)

> **Audiencia:** Eduardo Paredes (owner), DBA, Auxiliar CxP, Coordinador
> CxP, Director de Finanzas.
> **Cuándo se ejecuta:** una vez que el módulo CxP termina F10 y el
> negocio agenda la fecha de corte SAP → ERP.
> **Cómo se ejecuta:** de arriba hacia abajo, sin saltar pasos. Cada
> sección tiene su responsable y su evidencia.
> **Si algún paso falla:** detener el cutover, ejecutar el rollback de
> §8 y reagendar.

Este checklist asume que el módulo ya pasó por la operación-y-runbook
([08-operacion-y-runbook.md](08-operacion-y-runbook.md)) y que todos
sus PRs (F0 → F9) están mergeados a `main` y desplegados en `dev`
(y `qa` si aplica).

---

## 1. Pre go-live — T-7 días

Tareas que no se pueden hacer "el día de". El responsable confirma a
Eduardo por correo o checklist en Notion.

### 1.1 Datos maestros listos en Admin

| Catálogo | Cantidad mínima | Responsable | Evidencia |
|---|---|---|---|
| Proveedores activos cargados | Todos los proveedores con saldo vivo en SAP al corte | Auxiliar CxP | Export `proveedores.csv` validado contra trial balance SAP |
| Sucursales | 1 (Tlalnepantla) — extendible | Admin | Tabla `administracion.sucursales` poblada |
| Conceptos contables CxP | ≥ 15 conceptos del catálogo Contabilidad seed | Contabilidad | `SELECT count(*) FROM contabilidad.conceptos WHERE modulo='CXP'` ≥ 15 |
| Tipos de cambio del día | Tipo USD y EUR del día del corte | Auxiliar CxP | Captura manual en `cuentas_por_pagar.tipos_cambio` o stub log con valor |
| Empleados activos | Todos con `empleado_id` para Viáticos / TC | RH / Admin | Stub seed con ≥ 20 empleados activos |
| Puestos | Auxiliar, Coordinador, Jefe, Director | RH / Admin | 4 puestos seed mínimos |
| Dependencias revisoras | CXP, COMPRAS, OPERACIONES, FINANZAS | Admin | Seed estándar del módulo + extras locales |

> **Decisión Eduardo (F10-PR1):** si Catálogos Contabilidad / Empleados
> / Puestos no están listos en Admin al T-7, el módulo arranca con los
> seeds del repo y se promueven a real en T+30. Los `PLATFORM-TODO`
> del runbook §6 cubren esa deuda.

### 1.2 Roles y permisos en Entra ID

Asignar a usuarios reales los permisos canónicos del módulo (ver
[`backend/src/Identidad/Domain/PermisosCanonicos.cs`](../../../backend/src/Identidad/Domain/PermisosCanonicos.cs)).
Sugerencia de roles mínimos:

- **Auxiliar CxP** → captura, revisión, autoriza nivel 1
- **Coordinador CxP** → todo lo anterior + autoriza nivel 2 + administra
  catálogos + reportes
- **Director de Finanzas** → autoriza viáticos > umbral + dashboards
- **Tesorería** → solo recibe `PasivoAutorizadoParaPagoEvent` (no toca
  UI CxP)
- **Aud / Solo lectura** → solo reportes

Validación:

```sql
-- Cada usuario destinado a operar tiene al menos 1 rol con permiso CxP.
SELECT u.email, count(rp.permiso_id) AS permisos_cxp
FROM identidad.usuarios u
JOIN identidad.usuarios_roles ur ON ur.usuario_id = u.id
JOIN identidad.roles_permisos rp ON rp.rol_id = ur.rol_id
JOIN identidad.permisos p ON p.id = rp.permiso_id
WHERE p.codigo LIKE 'cuentas_por_pagar.%' AND u.activo
GROUP BY u.email
HAVING count(rp.permiso_id) > 0;
```

### 1.3 Service Bus topics y subs

```bash
az servicebus topic list -g <rg> --namespace-name <sb-ns> -o table
az servicebus topic subscription list -g <rg> --namespace-name <sb-ns> --topic-name almacen-events -o table
az servicebus topic subscription list -g <rg> --namespace-name <sb-ns> --topic-name compras-events -o table
az servicebus topic subscription list -g <rg> --namespace-name <sb-ns> --topic-name tesoreria-events -o table
```

Debe verse:

- Topic `cuentas-por-pagar-events` existe (publica CxP).
- Subscription `cuentas-por-pagar-subscription` en `compras-events` y
  `almacen-events`.
- Subscription `cuentas-por-pagar-tesoreria-sub` en `tesoreria-events`
  (F9-PR1).
- Tesorería tiene su subscription en `cuentas-por-pagar-events`
  (`tesoreria-subscription`).

### 1.4 Infra Azure

| Recurso | Verificación |
|---|---|
| `psql-millet-prod-mxc-01` | Existe, accesible desde App Service, backup automático activo |
| `kv-millet-prod-mxc-01` | Secretos `Db__ConnectionString`, `ServiceBus__ConnectionString` presentes |
| App Service `app-millet-api-prod-mxc-01` | Plan ≥ P1v3, slot `staging` opcional |
| `st-milletprod*` (Blob para evidencias) | Container `cxp-evidencias` creado, RBAC del App Service asignado |
| Application Insights | Wired al App Service, retención ≥ 30 días |

> **Decisión Eduardo:** evidencias en producción se promueven de
> `LocalFilesystemEvidenciaBlobStorage` a Azure Blob **antes del
> go-live**. El stub local sólo es aceptable en `dev`. Si llega el día
> y no hay adapter Azure Blob, **se detiene el go-live**.

---

## 2. Pre go-live — T-1 día

### 2.1 Snapshot SAP

- Trial balance proveedores @ corte.
- Antigüedad de saldos @ corte.
- Saldos por cuenta contable CxP.
- Listado de pasivos vivos con monto, fecha de vencimiento y UUID
  cuando aplique.

Estos archivos se conservan como **fuente de verdad pre-corte** y se
usan para conciliar el primer mes en paralelo.

### 2.2 Last deploy a prod

```bash
# Workflow build-and-test debe estar verde en main.
gh run list --branch main --workflow build-and-test --limit 3

# Deploy a prod (workflow manual o branch deploy según infra).
# REGLA: nunca skipear hooks ni forzar.
```

Smoke post-deploy:

```bash
curl -i https://app-millet-api-prod-mxc-01.azurewebsites.net/health/ready
curl -i https://app-millet-api-prod-mxc-01.azurewebsites.net/health/live
```

Ambas 200 OK. Si `/health/ready` da 503, revisar §2.3 (migraciones).

### 2.3 Migraciones EF aplicadas

```sql
SELECT migration_id, applied_on
FROM __EFMigrationsHistory
WHERE migration_id LIKE '%CuentasPorPagar%'
ORDER BY applied_on DESC
LIMIT 10;
```

Última migración debe ser la del último PR mergeado (ver `git log`
de `backend/src/CuentasPorPagar/Infrastructure/Persistence/Migrations/`).

### 2.4 Comunicación al equipo

Correo del owner al equipo CxP:

- Fecha y hora exacta del corte.
- A partir de qué hora dejan de capturar en SAP.
- A partir de qué hora pueden capturar en ERP.
- Quién es contacto si algo falla (Eduardo + ingeniero de guardia).

---

## 3. Día del corte (T-0)

### 3.1 Freeze SAP

A la hora acordada, SAP queda **read-only** para CxP. Auxiliar y
Coordinador NO capturan en SAP a partir de este punto.

### 3.2 Final SAP snapshot

Re-tomar los reportes de §2.1 ya en frío. Estos son los archivos
**oficiales** contra los que se va a conciliar.

### 3.3 Carga inicial de pasivos vivos a ERP

**Decisión Eduardo (F7-PR4 deferred → F10):** la migración de pasivos
vivos de SAP a CxP se hace **manual** vía Excel + endpoint de
captura. **No** se construye un importador automatizado en MVP.

Procedimiento:

1. Auxiliar CxP toma el listado SAP de pasivos vivos.
2. Para cada uno, captura en la UI de CxP:
   - Con OC asociada → flujo "Captura con OC" (Compras debe tener la
     OC migrada también).
   - Sin OC asociada → flujo "Captura sin OC" (F7-PR1).
3. Los pasivos se marcan con `OrigenMigracion = "SAP_CORTE_<fecha>"`
   en metadata para trazabilidad (campo libre opcional o vía
   `motivo_captura`).

> **PLATFORM-TODO(<ImportadorPasivosSAP>):** si el volumen pasa de
> ~200 pasivos vivos, evaluar importador batch con dry-run antes de
> próximo cierre.

### 3.4 Validación de saldos

Al terminar la carga:

```sql
-- Total facturado vs SAP.
SELECT moneda, sum(total) FROM cuentas_por_pagar.facturas_proveedor
WHERE estado <> 'Cancelada' GROUP BY moneda;

-- Por bucket (debe coincidir con antigüedad SAP).
-- Usar el reporte /reportes/antiguedad-saldos en la UI.
```

Diferencias aceptables: redondeos < $1 MXN por proveedor. Diferencias
> $1 → revisar pasivo por pasivo antes de continuar.

### 3.5 Habilitar ERP CxP para captura productiva

Una vez §3.4 verde:

- Habilitar acceso al módulo CxP en el shell para los roles operativos.
- Auxiliar puede empezar a capturar facturas del día.
- Las facturas nuevas (no migradas) entran por el flujo normal.

---

## 4. Smoke tests funcionales post go-live

Smoke manual del owner o coordinador, dentro de la primera hora del
go-live. Cada uno debe pasar sin errores 500 ni timeouts.

### 4.1 Flujo A — factura contra OC

1. Crear OC en Compras con 1 línea (Compras debe estar operativo).
2. Almacén recibe la OC (genera `OcRecepcionRegistradaEvent`).
3. CxP captura factura referenciando la OC → estado `Capturada`.
4. Conciliar → si dentro de tolerancia, pasa a `EnRevision` o
   `Autorizada` según reglas.
5. Autorizar (nivel 1 + nivel 2 con segregación de funciones).
6. CxP emite `PasivoAutorizadoParaPagoEvent` → Tesorería lo recibe.
7. Tesorería emite `PagoFacturaProveedorEvent` → CxP marca como
   `Pagada`.

Validar:

```sql
SELECT estado, importe_pagado, total FROM cuentas_por_pagar.facturas_proveedor WHERE id = '<factura-id>';
-- Debe estar Pagada con importe_pagado = total.
```

### 4.2 Flujo C — factura sin OC

1. CxP captura factura sin OC (F7-PR1, requiere `motivo_captura`).
2. Asignar concepto contable + sucursal.
3. Pasa a revisión → autorización nivel 1 → nivel 2 → autorizada.
4. Tesorería paga → marcada Pagada.

### 4.3 Comprobación de gastos

1. Auxiliar captura comprobación con 2-3 líneas.
2. Si es gasto aduanal, requiere `numero_pedimento` (F7-PR2).
3. Autorización nivel 1 + nivel 2 (segregación).
4. Pasa a autorización → genera pasivo si aplica.

### 4.4 Viáticos

1. Empleado solicita viáticos (F7-PR3, monto X).
2. Jefe autoriza (si < umbral) o Dirección de Finanzas (si > umbral).
3. Tesorería entrega anticipo (evento Tesorería).
4. Empleado captura comprobación con facturas.
5. Liquidación.

### 4.5 Tarjeta de crédito empresarial

1. Auxiliar carga estado de cuenta Excel (F7-PR5).
2. Parser procesa N líneas.
3. Conciliación automática asigna las que tienen score ≥ 90.
4. Coordinador revisa sugerencias (60-89) manualmente.
5. Cierra estado de cuenta (F7-PR6).
6. Tesorería marca como pagado al banco.

### 4.6 Reportes

Cada uno debe responder en < 5s para el dataset migrado:

- `/reportes/antiguedad-saldos`
- `/reportes/cartera-por-proveedor`
- `/reportes/cartera-por-categoria-revision`
- `/reportes/tarjetas-credito`
- `/reportes/pasivos-obras?sucursalId=...`

Export PDF y Excel desde la UI debe funcionar (`@react-pdf/renderer` +
`exceljs`).

---

## 5. Smoke técnico post go-live

### 5.1 Service Bus consumer health

Application Insights, primera hora:

```kusto
traces
| where timestamp > ago(1h)
| where customDimensions.SourceContext startswith "Millet.CuentasPorPagar.Infrastructure.Workers"
| summarize count() by tostring(customDimensions.SourceContext), severityLevel
```

Esperar: 0 mensajes Error en `AlmacenEventListenerWorker`,
`ComprasEventListenerWorker`, `TesoreriaEventListenerWorker`,
`OutboxPublisherWorker`.

### 5.2 Outbox drenando

```sql
SELECT count(*) FROM cuentas_por_pagar.integration_events_outbox
WHERE published_at IS NULL;
```

Debe converger a 0 cada minuto (el worker corre cada 5s).

### 5.3 Eventos procesados (idempotencia)

```sql
SELECT count(*) FROM cuentas_por_pagar.eventos_procesados WHERE procesado_en > now() - interval '1 hour';
```

Crece monotónicamente sin duplicar `(evento_id, evento_tipo)`.

### 5.4 Health checks

Curl cada 5 minutos por la primera hora:

```bash
curl -i https://app-millet-api-prod-mxc-01.azurewebsites.net/health/ready
```

Si en algún momento da 503, revisar log + runbook §4.

---

## 6. Validación post go-live — T+7 días

A los 7 días del go-live, el coordinador CxP corre estas validaciones
con Eduardo:

| Validación | Esperado | Si falla |
|---|---|---|
| Total cartera ERP vs último SAP + capturas del periodo | Match al peso (tolerancia $5 MXN agregado) | Conciliación detallada por proveedor |
| Pasivos vencidos > 90 días | ≤ 5% del total cartera | Revisar buckets en reporte antigüedad |
| `eventos_procesados` sin DLQs | DLQ count = 0 en SB topics suscritos | Re-procesar mensajes DLQ y registrar incidente |
| `integration_events_outbox` sin pendientes > 1h | 0 filas con `published_at IS NULL` y `created_at < now() - 1h` | Revisar SB connection / worker health |
| Latencia P95 endpoints CxP | < 800ms para captura / autorización | App Insights → query de latencia y profiling |
| Errores 5xx en CxP | < 0.1% del tráfico | App Insights → exceptions + correlar a release |

---

## 7. Validación post go-live — T+30 días

A los 30 días, sesión de retro con el equipo:

- ¿Cuántos pasivos se cancelaron via `CancelacionPasivoSolicitadaEvent`
  desde Tesorería (F9-PR1)?
- ¿Cuántas notas de crédito quedaron en `EnEspera` > 7 días?
- ¿La conciliación TC funcionó? % automático vs manual.
- ¿Los reportes son lo que se necesita? ¿Faltan columnas / filtros?
- PLATFORM-TODOs por cerrar — ¿prioridad?

Esta retro alimenta el roadmap post-MVP del módulo y el cierre de
PLATFORM-TODOs prioritarios (Empleados, Conceptos Contables, Blob
Storage Azure).

---

## 8. Rollback

Si durante §3 o §4 algo va mal **antes** de habilitar al equipo a
capturar productivamente, el rollback es **trivial**:

1. Revertir flag de acceso al módulo (Identidad → revocar permisos
   `cuentas_por_pagar.*` a los roles operativos).
2. Re-habilitar SAP para captura.
3. Notificar al equipo que se reagenda.

Las migraciones EF aplicadas **se quedan**; no se hace `Down()` en
prod. Los datos migrados en §3.3 se marcan como `OrigenMigracion =
"ROLLBACK_<fecha>"` para auditoría y se ignoran en el siguiente
intento.

Si **ya hay capturas productivas reales** post-cutover y se detecta
un bug bloqueante, **no se hace rollback** — se aplica hotfix forward
siguiendo runbook §4.

---

## 9. Sign-off

| Rol | Persona | Fecha | Firma |
|---|---|---|---|
| Owner | Eduardo Paredes | | |
| Auxiliar CxP | | | |
| Coordinador CxP | | | |
| Director de Finanzas | | | |
| Ingeniero de guardia | | | |

Una vez firmado, el módulo CxP queda **oficialmente productivo** y el
módulo 7 (Cuentas por Pagar) del backlog se marca como **cerrado MVP**.
Siguientes fases (catálogos reales, evidencias en Blob Azure, pg_trgm,
importador batch) entran en backlog post-MVP del módulo y se priorizan
contra los demás módulos del ERP.
