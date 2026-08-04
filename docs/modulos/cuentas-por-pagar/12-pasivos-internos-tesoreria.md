# 12 — Pasivos internos hacia Tesorería (reposición de caja y viáticos)

> **Estado: v1.0 — DECISIONES CERRADAS (2026-07-16, Eduardo).** D1–D5 con
> las opciones recomendadas; Q1 = ambas (selector `DestinoReposicion`);
> Q2 = `marcar-pagado` queda como fallback manual; Q3 = la diferencia
> negativa se cubre con depósito del empleado; Q4 = la reposición se
> **acumula hasta un mínimo configurable por sucursal** (no 1:1).
> **Implementación COMPLETA** (2026-07-17): GI-PR1 #640, GI-PR2 #641, GI-PR3 #642, GI-PR4a #643+#646, GI-PR4b #645 — todo verificado e2e en dev (evidencia en docs/entrega/pipelines/p7-cxp-gastos-internos.md).
> Origen: verificación e2e P7 (`docs/entrega/pipelines/p7-cxp-gastos-internos.md`),
> hallazgo **P7-H3** y análisis de las relaciones CxP ↔ Tesorería en los
> flujos de gastos internos.

---

## 1. Problema

La única puerta de CxP hacia Tesorería es `pasivo.autorizado-para-pago.v1`,
que se emite cuando una `FacturaProveedor` se **autoriza** y upserta la
bandeja `tesoreria.pasivo_pendiente_pago`. Ese contrato asume que el
beneficiario es un **proveedor** del catálogo (CLABE/banco/beneficiario en
`compartido.proveedores`).

Los gastos internos rompen esa suposición:

| Flujo | Facturas que genera | ¿A quién se le debe realmente? |
|---|---|---|
| Caja chica | 1 por CFDI, para gasto/IVA/DIOT | **A la caja de la sucursal** (reposición) — los proveedores ya cobraron en efectivo |
| Viáticos (anticipo) | ninguna | **Al empleado** (préstamo autorizado por pagar) |
| Viáticos (liquidación) | 1 por línea fiscal, para gasto/IVA/DIOT | **Al empleado** si gastó más que el anticipo; **el empleado debe** si gastó menos |
| Aduanales | ninguna (agrupa facturas con OC) | Al proveedor (agencia) — ✅ ya cubierto por el flujo normal |
| TC empresarial | 1 agregada contra el banco al corte | Al banco — ✅ ya cubierto por el flujo normal |

Hoy (post fixes P7): las facturas de caja chica/viáticos están protegidas
por el candado `FACTURA_GASTO_INTERNO_NO_AUTORIZABLE` (autorizarlas
duplicaría el pago), pero **el pasivo interno real no existe en el
sistema**: la reposición de caja y la liquidación de viáticos se pagan por
fuera, y el anticipo de viáticos usa el proxy manual `marcar-pagado`
(PLATFORM-TODO `<TesoreriaPagoViaticosEvent>`).

## 2. Decisiones a tomar

### D1 — Cómo modelar el pasivo interno — ✅ CERRADA: opción (b)

- **(a) Beneficiario "proveedor interno"**: dar de alta empleados/cajas
  como proveedores y reutilizar todo el flujo actual sin tocar contratos.
  ✚ cero código nuevo en Tesorería. ✖ contamina el catálogo de
  proveedores, la DIOT y los reportes de cartera; un empleado no es un
  proveedor fiscal.
- **(b) Extender el contrato existente** *(recomendada)*: el payload de
  `pasivo.autorizado-para-pago.v1` gana `TipoBeneficiario`
  (`Proveedor` | `Empleado` | `CajaSucursal`, default `Proveedor` —
  cambio aditivo, compatible con consumidores actuales) +
  `BeneficiarioId` + `ReferenciaOrigen` (comprobación / solicitud).
  Tesorería agrega `tipo_beneficiario` a `pasivo_pendiente_pago` y la
  bandeja filtra/etiqueta. La correlación de idempotencia para pasivos
  internos es `comprobacion_id` / `solicitud_viaticos_id` (no hay
  `factura_id`).
- **(c) Evento nuevo** `cuentas_por_pagar.pasivo-interno.autorizado.v1`
  con tabla y bandeja propias en Tesorería. ✚ no toca el contrato
  probado. ✖ duplica bandeja, corridas de pago y reportes; dos caminos
  que mantener.

### D2 — Cuándo nace la reposición de caja chica — ✅ CERRADA (acumulación con mínimo, Q4)

Decisión final (combina (a) y (b) por Q4): al **`Aplicar`**, el monto de
la comprobación se **acumula** en un saldo por reponer por
`(sucursal, destino)`; cuando el saldo acumulado alcanza el **monto
mínimo configurado para la sucursal**, se emite UNA reposición agregada
(entidad `ReposicionCajaChica`, análoga al corte de TC) que liga las
comprobaciones cubiertas y publica el pasivo interno por el total.

- **Mínimo configurable por sucursal** (catálogo
  `configuracion_reposicion_caja`); sucursal sin configuración = mínimo
  `0` = emisión inmediata (equivale a 1:1).
- **Corte manual**: un permiso permite "emitir reposición ahora" para
  vaciar el saldo aunque no alcance el mínimo (fin de mes, caja corta).
- La acumulación agrupa por destino: comprobaciones con
  `DestinoReposicion` distinto llevan saldos separados.

**Q1 — CERRADA (2026-07-16, Eduardo): se soportan AMBAS.** La
comprobación gana un campo `DestinoReposicion`
(`CuentaSucursal` | `Responsable`) que el capturista elige en el Sheet;
al `Aplicar`, el pasivo se emite con `TipoBeneficiario = CajaSucursal`
(+ `BeneficiarioId = sucursal_id`) o `TipoBeneficiario = Empleado`
(+ `BeneficiarioId = responsable_id`) según el destino. Default del
selector: el último destino usado por esa sucursal (o `CuentaSucursal`
si es la primera). El contrato de D1-b ya distingue ambos tipos, así que
no agrega complejidad al lado de Tesorería — solo etiqueta distinta en
la bandeja.

### D3 — Anticipo de viáticos (préstamo): ida y vuelta con Tesorería — ✅ CERRADA (Q2: fallback manual se queda)

Propuesta (cierra el PLATFORM-TODO `<TesoreriaPagoViaticosEvent>`):

1. **Ida**: al quedar `AutorizadaPorJefe` o `AutorizadaCompleta`, CxP
   emite el pasivo interno (`TipoBeneficiario = Empleado`, monto
   solicitado, `solicitud_viaticos_id`). Aparece en la bandeja de
   Tesorería como préstamo por pagar.
2. **Vuelta**: Tesorería paga y emite
   `tesoreria.pago-prestamo-viaticos.aplicado.v1`
   (`solicitud_viaticos_id`, fecha, referencia). Un listener en el
   `TesoreriaEventListenerWorker` de CxP transiciona la solicitud a
   `Anticipada` (idempotente por solicitud).
3. **Q2 cerrada**: el endpoint `marcar-pagado` queda como **fallback
   manual** con el mismo permiso, para pagos en efectivo de ventanilla
   que no pasan por el flujo de Tesorería. La vía normal es la
   automática del punto 2.

### D4 — Liquidación de viáticos: la diferencia — ✅ CERRADA: opción (a) (Q3: depósito del empleado)

- **Positiva** (gastó más): pasivo interno de reembolso al empleado por la
  diferencia, emitido al `Liberar`. Misma vía que D1/D3.
- **Negativa** (debe devolver): NO es un pasivo. Opciones:
  - **(a)** *(recomendada para MVP)* cuenta por cobrar interna: registro
    en CxP + aviso a Tesorería para esperar el depósito del empleado
    (conciliable con el flujo de depósitos RN-6); la solicitud queda
    `Liquidada` con saldo pendiente visible.
  - **(b)** descuento por nómina: fuera del alcance del ERP; se exporta
    reporte a RH. Evolución natural cuando exista el módulo/integración.
  - **(c)** solo informativo (statu quo). No recomendado: hoy la deuda
    del empleado no se ve en ningún lado.

### D5 — Datos bancarios del empleado — ✅ CERRADA: opción (b)

El catálogo `compartido.empleados` no tiene CLABE.

- **(a)** agregar CLABE/banco al catálogo (Administración) — implica
  captura y resguardo de datos bancarios de personas.
- **(b)** *(recomendada para MVP)* el pasivo interno viaja **sin** datos
  bancarios y Tesorería registra el pago con referencia libre
  (transferencia manual / efectivo), igual que hoy opera.

## 3. Contratos propuestos (con D1-b)

`cuentas_por_pagar.pasivo.autorizado-para-pago.v1` — campos NUEVOS
(aditivos):

| Campo | Tipo | Notas |
|---|---|---|
| `TipoBeneficiario` | string enum: `Proveedor` \| `Empleado` \| `CajaSucursal` | ausente/`Proveedor` = comportamiento actual |
| `BeneficiarioId` | guid | `proveedor_id` \| `empleado_id` \| `sucursal_id` según tipo |
| `OrigenTipo` | string enum: `Factura` \| `ReposicionCajaChica` \| `PrestamoViaticos` \| `LiquidacionViaticos` | correlación e idempotencia |
| `OrigenId` | guid | `factura_id` \| `reposicion_id` \| `solicitud_viaticos_id` |

`tesoreria.pago-prestamo-viaticos.aplicado.v1` (nuevo, topic
`tesoreria-events`):

| Campo | Tipo |
|---|---|
| `SolicitudViaticosId` | guid |
| `MontoPagado` | decimal |
| `FechaPago` | datetimeoffset UTC |
| `ReferenciaPago` | string |

Bicep: el filtro de `cuentas-por-pagar-tesoreria-sub` (CxP ← Tesorería)
gana `pago-prestamo-viaticos.aplicado.v1` **en el mismo PR** que agregue
el listener (regla de oro de #612/#614: cero drift manual).

## 4. Plan de PRs (si se aprueba)

| PR | Alcance |
|---|---|
| ✅ GI-PR1 #640 (`cxp/`) | Contrato extendido (`TipoBeneficiario`/`OrigenTipo`/`OrigenId`) + `DestinoReposicion` en la comprobación (Q1) + agregado `ReposicionCajaChica` con acumulación por (sucursal, destino), mínimo configurable y corte manual (Q4) + pasivo de préstamo al autorizar viáticos (D3-ida) + guard en el listener de Tesorería (ignora pasivos internos hasta GI-PR2, sin dead-letter) |
| ✅ GI-PR2 #641 (`tesoreria/`) | `tipo_beneficiario` en `pasivo_pendiente_pago` (migration) + listener tolerante + bandeja FE con etiqueta de beneficiario |
| ✅ GI-PR3 #642 (`tesoreria/` + `cxp/`) | Vuelta del préstamo: evento de pago aplicado + listener CxP → `Anticipada`; decisión Q2 sobre `marcar-pagado` |
| ✅ GI-PR4 #643/#645/#646 (`cxp/`) | Liquidación: reembolso (positiva) + CxC interna (negativa, D4-a) + FE estados |

Anti-scope: contabilización de las pólizas (módulo Contabilidad), nómina
(RH), REPP (no aplica — el beneficiario interno no emite CFDI de pago).

## 5. Preguntas — TODAS CERRADAS (2026-07-16, Eduardo)

- **Q1 — ✅**: se soportan **ambas** — selector `DestinoReposicion`
  (`CuentaSucursal` | `Responsable`) por comprobación; ver D2.
- **Q2 — ✅**: `marcar-pagado` **se queda como fallback manual** con
  permiso restringido (pagos en efectivo de ventanilla); la vía normal
  es el pago registrado en Tesorería (D3).
- **Q3 — ✅**: la diferencia negativa se cubre con **depósito del
  empleado** — cuenta por cobrar interna en CxP conciliable con los
  depósitos de Tesorería (D4-a). Nómina queda como evolución futura.
- **Q4 — ✅**: la reposición **se acumula hasta un monto mínimo
  configurable por sucursal** (mínimo 0 = inmediata) con corte manual
  para vaciar el saldo; ver D2.
