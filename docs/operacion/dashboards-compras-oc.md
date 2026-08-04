# Observabilidad — submódulo Órdenes de Compra (F10-PR1)

> Custom dimensions, traces y consultas KQL recomendadas para
> monitorear el submódulo OC desde Application Insights.

---

## Custom dimensions inyectadas

El módulo Compras inyecta las siguientes propiedades vía
`Serilog.Context.LogContext.PushProperty` en los handlers críticos:

| Propiedad | Tipo | Handlers que la inyectan | Notas |
|---|---|---|---|
| `OrdenCompraId` | Guid | `AutorizarOc`, `CancelarOc`, `Duplicar`, autorizada-pdf-listener | Trace-cross logs/traces dentro del request |
| `OrdenCompraOrigenId` | Guid | `Duplicar` | Solo en flujo C4 |
| `RequisicionId` | Guid | Handlers RQ heredados | Pre-existente |

Las propiedades aparecen en `customDimensions` de cada `traces` (logs)
y como tags en cada `dependencies`/`requests` cuando el `Activity`
asociado las setea.

## Activity spans

Spans declarados en handlers de OC (`StartActivity(name)`):

| Span name | Handler | Tags adicionales |
|---|---|---|
| `Compras.AutorizarOc` | `AutorizarOrdenCompraHandler` | `compras.oc.id`, `compras.autorizacion.nivel`, `compras.oc.folio`, `compras.oc.estado` |
| `Compras.CancelarOc` | `CancelarOrdenCompraHandler` | `compras.oc.id` |
| `Compras.DuplicarOc` | `DuplicarOrdenCompraHandler` | `compras.oc.origen.id` |

Otros handlers (`Cerrar`, `Reabrir`, registración recepción/factura/pago)
heredan logs estructurados a través del listener `OrdenCompraAutorizadaPdfListener`
y el infra de Outbox pero no abren span propio en F10-PR1 — se puede
extender en una iteración futura cuando se identifique un hotspot.

---

## Consultas KQL recomendadas

### Errores 4xx/5xx en endpoints OC

```kql
requests
| where name has "ordenes"
| where success == false
| project timestamp, name, resultCode, customDimensions.OrdenCompraId, operation_Id, duration
| order by timestamp desc
```

### P95 de autorización N2 (handler crítico para el cierre del ciclo)

```kql
dependencies
| where name == "Compras.AutorizarOc"
| where customDimensions["compras.autorizacion.nivel"] == "Nivel2"
| summarize p95 = percentile(duration, 95), count = count() by bin(timestamp, 5m)
| order by timestamp desc
```

### Tracing end-to-end por OC

```kql
union traces, requests, dependencies, exceptions
| where customDimensions.OrdenCompraId == "<paste-guid>"
| project timestamp, itemType, name, message, customDimensions
| order by timestamp asc
```

### Tasa de cancelación con recepciones parciales (F5-PR4)

```kql
requests
| where name has "cancelar-con-recepciones"
| where success == true
| summarize count() by bin(timestamp, 1d)
```

---

## Dashboards sugeridos

1. **Salud OC**: requests OC con success rate, P50/P95, top failures.
2. **Cierre del ciclo**: autorizaciones N1/N2 por día, OCs cerradas
   automáticamente por día, tiempo promedio desde autorización a
   cierre.
3. **Excepciones de negocio**: top `BusinessRuleException.Code` (e.g.,
   `PROVEEDOR_INACTIVO`, `OC_TRANSMITIR_BORRADOR_MINIMO`) — útil para
   detectar capacitación faltante o reglas que se disparan demasiado.

Los dashboards se definen como JSON en Application Insights >
Workbooks. Cuando exista un workbook compartido, su id va aquí. F10-PR3
puede automatizar la deployment.
