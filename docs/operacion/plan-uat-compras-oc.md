# Plan de UAT — Submódulo Órdenes de Compra

> Plan de aceptación de usuario para validar el submódulo OC con el
> cliente antes del go-live. Audiencia: ops + product owner +
> comprador piloto + dirección.

---

## Grupo piloto

| Rol | Quién | Responsabilidad |
|---|---|---|
| Product Owner | Eduardo Paredes | Sign-off final |
| Comprador piloto | Rodrigo (TBD) | Captura RQs + OCs día normal |
| Comprador #2 | TBD (1 segundo usuario para concurrencia) | Pruebas simultáneas |
| Jefe Compras (N1) | TBD | Autorizaciones N1 |
| Director (N2) | TBD | Autorizaciones N2 |
| Soporte L2 | TBD | Diagnóstico de issues |

---

## Indicadores de éxito (12, del §12 del 01-diseño)

| # | Indicador | Métrica | Objetivo |
|---|---|---|---|
| 1 | Captura de OC desde RQ funciona | OCs creadas / RQs autorizadas | > 90% en periodo de prueba |
| 2 | Captura sin RQ (FOC11) funciona | OCs sin-RQ válidas / intentos | 100% para casos permitidos |
| 3 | Flujo de doble autorización completo | OCs autorizadas en < 1 día | > 80% |
| 4 | PDF institucional descargable | PDFs descargados / OCs autorizadas | 100% |
| 5 | Bandeja general usable | Tiempo a encontrar una OC | < 10 segundos |
| 6 | Bandeja partidas abiertas usable | Coincide con expectativa de operación | sign-off del comprador |
| 7 | Cancelación sin recepciones | OCs canceladas / intentos válidos | 100% |
| 8 | Cancelación con recepciones parciales | Doble auth funciona | OCs canceladas correctas |
| 9 | Duplicar OC | OCs duplicadas / intentos | 100% |
| 10 | Historial cronológico | Eventos visibles / esperados | > 95% |
| 11 | KPIs reactivos a filtros | KPIs cuadran con bandeja | sign-off |
| 12 | Tiempos de respuesta | P95 < 500ms para bandejas | conforme a F10-PR2 metodología |

---

## Casos de prueba E2E

### Caso 1: ciclo completo OC desde RQ

1. Comprador crea RQ con 3 líneas, la autoriza con matriz.
2. Comprador consolida RQ a OC con un proveedor.
3. Transmite la OC; N1 (Jefe Compras) aprueba.
4. N2 (Dirección) aprueba.
5. PDF descargable.
6. Almacén "registra" recepción completa (vía evento mock).
7. CxP "registra" factura completa.
8. Tesorería "registra" pago completo.
9. OC transiciona automáticamente a `Cerrada`.

**Esperado**: cada paso visible en historial; sub-estados avanzan;
folio único por sucursal/año.

### Caso 2: rechazar y duplicar

1. OC transmitida → N1 rechaza con motivo.
2. La OC queda `Rechazada`. Editable.
3. Comprador duplica con `POST /duplicar`.
4. Edita líneas, ajusta proveedor, transmite.
5. Flujo normal hasta `Cerrada`.

### Caso 3: cancelación con recepciones parciales

1. OC autorizada, 50% recibido en Almacén.
2. Comprador intenta `POST /cancelar` → 422 (recepción != SinRecepcion).
3. Doble firma: comprador con permisos especiales hace
   `POST /cancelar-con-recepciones`.
4. RQ asociada se libera proporcional (saldo no recibido).
5. Cantidad recibida permanece en la línea (trazabilidad).

### Caso 4: concurrencia optimista

1. Usuario A abre OC en UI (Version=5).
2. Usuario B también abre la OC (Version=5).
3. A modifica logística → 204, Version=6.
4. B intenta modificar referencia proveedor con `If-Match: 5` → 409.
5. UI de B muestra "documento modificado, recargar".

### Caso 5: catálogos seed

1. Verificar `GET /catalogos/incoterms` devuelve 11 reglas.
2. `GET /catalogos/regimenes-fiscales` devuelve 8 regímenes.
3. `GET /catalogos/condiciones-pago` devuelve 7 plazos.
4. Validar IDs estables (los seeds usan Guids deterministas
   `00000002-XXXX-0000-0000-...`).

---

## Cronograma sugerido

| Semana | Actividad |
|---|---|
| 1 | Capacitación al grupo piloto + entrega de credenciales staging |
| 2 | Pruebas guiadas: casos 1–3 con facilitador. Recolectar feedback |
| 3 | Pruebas autónomas + casos 4–5 |
| 4 | Cierre: indicadores en App Insights, sign-off, lista de items para post-go-live |

---

## Criterios de sign-off

El submódulo OC se considera **listo para Producción** cuando:

1. Los 12 indicadores cumplen su objetivo.
2. No hay bugs `severity = blocker` abiertos.
3. Eduardo (PO) firma el documento (`<docs/operacion/plan-uat-compras-oc.md>` —
   sección final con fecha + firma).
4. Runbook (`docs/operacion/runbook-compras-oc.md`) revisado por
   soporte L2.
5. Dashboards en App Insights configurados y accesibles.
6. Connection strings en Key Vault productivo.

---

## Firma del sign-off

```
Cliente / PO: ______________________________   Fecha: __________
Compras (Rodrigo): ________________________   Fecha: __________
TI (Eduardo Paredes): _____________________   Fecha: __________
```
