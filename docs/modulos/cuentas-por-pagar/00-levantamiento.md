# Levantamiento — Módulo Cuentas por Pagar (`Millet.CuentasPorPagar`)

> **Proyecto:** ERP Millet — Módulo de Cuentas por Pagar
> **Versión:** 0.2 — Integra respuestas y anotaciones del área sobre v0.1
> **Fecha:** 2026-05-22
>
> **Origen:** mapa funcional construido a partir de entrevistas con el área
> de CXP y los 46 comentarios del PDF revisado sobre la v0.1.
> Este documento es la fuente de verdad sobre la que se construirán los
> `01-diseno.md` … `07-frontend-pr-breakdown.md` del módulo.
>
> **Estado:** validado funcionalmente con el área de CXP. Las decisiones
> pendientes están listadas en §13. Los puntos de integración con Almacén
> **están cerrados** en [`docs/modulos/almacen/00-levantamiento.md`](../almacen/00-levantamiento.md)
> (Rev. 0.1, 2026-05-22). Los marcadores `[Pendiente — Almacén]` que
> permanecen apuntan al ancla concreta del doc de Almacén que los resuelve.
>
> **Patrón:** sigue los exemplares de
> [`docs/modulos/compras-ordenes-compra/00-levantamiento-mapa-funcional.md`](../compras-ordenes-compra/00-levantamiento-mapa-funcional.md)
> y [`docs/modulos/compras-requisiciones/00-levantamiento-legacy-portalsap.md`](../compras-requisiciones/00-levantamiento-legacy-portalsap.md).
> Hereda decisiones transversales del módulo Compras (hexagonal + CQRS,
> multi-DbContext por ADR-0030, Outbox por ADR-0009, Idempotency-Key por
> ADR-0020, versionado `/api/v1/` por ADR-0021, RBAC granular por
> ADR-0007, Problem Details por ADR-0010, ETag por ADR-0012,
> PLATFORM-TODO por ADR-0031).

---

## 0. Cómo leer este documento

- `[Verificado]` — leído directamente del sistema actual (SAP B1, OneFacture, hojas Excel del área) o confirmado en sesión con el área de CXP.
- `[Inferido]` — deducido por nombres, convenciones o documentación parcial; no confirmado.
- `[Gap]` — agujero de conocimiento que requiere confirmación.
- `[Pendiente — Almacén]` — **histórico**: punto que se cerró en el levantamiento de Almacén v0.1 (2026-05-22). El marcador permanece para trazabilidad; ver [`almacen/00-levantamiento.md`](../almacen/00-levantamiento.md) §8.4 (devoluciones a proveedor) y §11.2 (eventos publicados).
- `[Pendiente — área]` — respuesta del área de CXP aún no recibida; ver §13.

> **Sobre las sugerencias del diseñador.** Las secciones marcadas con `> **Pendiente — sugerencia:**` reflejan puntos del levantamiento que aún no tienen respuesta cerrada del área. En lugar de dejarlos en blanco, se propone una respuesta plausible basada en el contexto del proyecto y patrones típicos de la industria. El responsable del área confirma o ajusta cada uno antes de pasar a `01-diseno.md`.

---

## Cambios mayores respecto a v0.1

Esta versión integra 46 anotaciones del área sobre la v0.1. Los cambios principales:

1. **Actores corregidos.** El Comprador no captura facturas — solo captura OCs. El Auxiliar de CXP captura **todas** las facturas, con y sin OC.
2. **Cuatro variantes sin OC, no tres.** Se agrega Tarjetas de Crédito Empresariales (Amex) como variante 4.
3. **Agencias Aduanales reclasificadas.** Tienen OC en ambos casos (importación y exportación). Salen de "sin OC" y entran al flujo estándar con doble autorización.
4. **Tolerancia por proveedor.** No es un global de $0.99; se configura por proveedor según las condiciones negociadas.
5. **Nueva entidad `CfdiRecibido`** para separar el ciclo de vida del CFDI del ciclo de vida del pasivo.
6. **Catálogo unificado de motivos de revisión** consolidando las dos propuestas del área.
7. **Préstamos a empleados** declarados explícitamente fuera del módulo (boundary).
8. **Devoluciones de mercancía** agregadas como flujo inverso con Almacén.
9. **Pre-carga de datos desde el XML** agregada como requerimiento.
10. **Estados del pasivo reducidos** de 8 a 5.
11. **Serie de anticipos:** FANT (no ANT).

---

## 1. Propósito y alcance

### Qué hace este módulo

Administra el ciclo de vida del pasivo con proveedores: recepción del CFDI, captura y validación de la factura, conciliación con la Orden de Compra (cuando aplica), categorización, autorización del pasivo y del pago al vencimiento (heredada de la OC cuando aplica), gestión de anticipos a proveedores, emisión de notas de cargo, recepción de notas de crédito del proveedor, y mantenimiento del saldo por proveedor. Es la fuente única de verdad sobre cuánto se le debe a cada proveedor y por qué.

**Aclaración sobre autorización:** La OC autorizada autoriza tanto el registro del pasivo como el pago al vencimiento de la factura asociada. CXP no requiere segunda autorización en el flujo estándar.

### Qué NO hace

- **No ejecuta pagos.** La programación, ejecución y registro contable de los pagos vive en Tesorería. CXP entrega el pasivo autorizado; Tesorería lo toma desde ahí.
- **No registra el complemento de pago recibido (REPP) del proveedor.** Cuando el proveedor envía su REPP por el pago que le hicimos, lo registra Tesorería junto con el pago. CXP consulta el estado pero no opera el REPP.
- **No genera Órdenes de Compra.** Las consume desde el módulo de Compras (autorizadas y abiertas).
- **No mantiene el master de Proveedores como dueño único.** El alta es conjunta entre Compras y CXP (ver §5.1). El catálogo vive en el módulo `DatosMaestros` del área Administración (ver [`docs/modulos/administracion/01-diseno.md`](../administracion/01-diseno.md)).
- **No procesa el flujo de viáticos del empleado como anticipo a proveedor.** Los viáticos son un pasivo de préstamo al empleado (ver §7.4.2).
- **No administra préstamos a empleados con códigos `Axxxx`.** Esos préstamos viven en otro módulo (Recursos Humanos / Tesorería, por definir). CXP solo los referencia si una factura específica aplica contra el saldo del préstamo.

### Boundary con otros módulos

| Módulo | Qué le pide CXP | Qué le entrega CXP |
|---|---|---|
| **Compras** (OC + Requisiciones) | OCs autorizadas, abiertas, con datos del Encargado, materiales, montos y tolerancia | Estado de la factura asociada a la OC (registrada, en revisión, autorizada, pagada); rechazo por tolerancia con motivo y monto |
| **Tesorería** | Confirmación de pagos ejecutados y REPPs recibidos (vía evento) | Pasivos autorizados para pago con datos fiscales y bancarios del proveedor (vía evento) |
| **Contabilidad** | Mapeo `ConceptoContable` → `CuentaContable` | Asientos por cada movimiento (registro de pasivo, aplicación de NC, aplicación de anticipo, registro de TC) |
| **Facturación** | — | Información del lado proveedor para conciliar Obras (vía campo `Obra`) |
| **Obras / Proyectos** | — | Pasivos vinculados a número de Obra para el reporte unificado |
| **Almacén** | Eventos `OcRecepcionRegistradaEvent` (variantes A y B); `OcDevolucionRegistradaEvent` (sub-flujo 8.B) | `FacturaProveedorRegistradaEvent` (variante B); `DiferenciaPrecioFacturaDetectadaEvent`; `NotaCreditoFiscalDevolucionRecibidaEvent`. Detalle en §11.6. |
| **Recursos Humanos / Tesorería** (préstamos a empleado) | Saldo de préstamos a empleado (cuando una factura aplica contra él) | — |
| **Administración / DatosMaestros** | Master de Proveedores, catálogos SAT, ConceptoContable, Sucursales, Empleados | Alta conjunta de proveedor (validación CXP de datos fiscales y adjuntos) |
| **Identidad** | Roles y permisos canónicos `cuentas_por_pagar.*` | — |
| **FiscalAPI (PAC)** | Descarga masiva SAT, validación de RFC, consulta de estado de CFDI | — |

---

## 2. Actores

| Actor | Rol en este módulo |
|---|---|
| **Comprador** | Captura **Órdenes de Compra** basadas en sus cotizaciones. **No captura facturas.** Recibe del proveedor el CFDI (PDF + XML) por correo y lo deposita en el repositorio compartido del ERP para que CXP lo procese. Gestiona la autorización informal de anticipos con Dirección. |
| **Auxiliar de CXP** | Captura **todas las facturas** del proveedor — con y sin OC, incluyendo las cuatro variantes sin OC (Caja Chica, Viáticos, TC Empresarial, otras). Captura notas de crédito del proveedor. Captura el CFDI de anticipo cuando llega del proveedor para que Tesorería pueda pagar. **No gestiona la solicitud ni autorización de anticipos** (eso vive en Compras / Dirección). |
| **Encargado del área** (Mantenimiento, Compras MP, Insumos, Transportes, etc.) | Libera proveedores y facturas en estado de revisión que dependen de su área. |
| **Responsable de Comercio Exterior** | Primera autorización de gastos aduanales (registro de pasivo). |
| **Responsable de sucursal o área operativa** | Autoriza reembolsos de caja chica de su sucursal. |
| **Jefe directo del empleado** | Autoriza la comprobación de viáticos del empleado. |
| **Dirección General / Dirección de Finanzas** | Autoriza anticipos a proveedores, notas de cargo, y segunda autorización (de pago) en gastos aduanales y viáticos. DG es primario; DF es suplencia. |
| **Tesorería** | Actor externo al módulo. Consume pasivos autorizados; entrega evidencia de pago vía evento. |
| **Compras** | Origina la OC y su autorización. Responsable de corregir OCs cuando una factura se rechaza por tolerancia excedida. Participa en el alta conjunta de proveedores. |
| **PAC — FiscalAPI** | Actor externo. Provee descarga masiva del SAT, validación de RFC, consulta de estado de CFDIs. |
| **Proveedor** | Emite CFDIs (factura, anticipo, nota de crédito) y los hace llegar por correo, descarga del SAT, o eventualmente portal propio (post-MVP). |

---

## 3. Tipos de documentos que administra el módulo

| Documento | Origen | Quién lo registra |
|---|---|---|
| Factura de proveedor con OC | CFDI emitido por el proveedor, asociado a una OC abierta | Auxiliar de CXP |
| Factura de proveedor sin OC | CFDI emitido por el proveedor, sin OC previa | Auxiliar de CXP |
| Nota de crédito del proveedor | CFDI Egreso (descuento, devolución, amortización de anticipo) | Auxiliar de CXP |
| CFDI de anticipo del proveedor | CFDI Ingreso emitido por el proveedor cuando le pagamos un anticipo | Auxiliar de CXP |
| Nota de cargo a proveedor | Documento interno emitido por nosotros para reclamar daños, mermas, descuentos comerciales | Auxiliar de CXP (originada por Comprador o área operativa) |
| Reembolso de caja chica | Documento interno que agrupa múltiples comprobantes pequeños | Auxiliar de CXP |
| Comprobación de gastos aduanales | Conjunto de CFDIs vinculados a un pedimento, **con OC** | Auxiliar de CXP (originada por Comercio Exterior) |
| Comprobación de viáticos | Conjunto de CFDIs presentados por el empleado tras un viaje | Empleado captura, Auxiliar de CXP liga XML/PDF y libera |
| Movimiento de Tarjeta de Crédito Empresarial | CFDI o ticket pagado con TC; se considera ya pagado al proveedor | Empleado titular o Auxiliar de CXP |
| Devolución de mercancía a proveedor | Salida desde Almacén (sub-flujo 8.B en `almacen/00-levantamiento.md` §8.4) que dispara `NotaCargo` borrador en CxP vía evento `OcDevolucionRegistradaEvent` | Auxiliar de CXP (originada por Almacén / Calidad) |

**Nota fiscal:** Toda factura, NC y CFDI de anticipo es documento del SAT con XML y UUID. Las notas de cargo, los reembolsos de caja chica, las devoluciones, y los movimientos de TC son documentos internos no fiscales (no llevan UUID propio; el respaldo fiscal son los CFDIs individuales que los componen, cuando aplica).

---

## 4. Entidades del dominio

### 4.1 `CfdiRecibido` (NUEVA)

Representa un CFDI que llegó al ERP pero aún no necesariamente está convertido en pasivo. Resuelve la separación entre "tengo el XML" y "tengo el pasivo registrado".

Atributos:

- UUID (única)
- RFC emisor, RFC receptor
- Tipo de CFDI: Ingreso / Egreso / Pago
- Folio + serie del emisor
- Fecha del CFDI, total, moneda
- Canal de origen: `DescargaSAT` / `Mailbox` / `CargaManual` / `PortalProveedor` (este último reservado para post-MVP)
- Fecha de recepción en el ERP
- Estado: `PorProcesar` / `ConvertidoEnPasivo` / `Duplicado` / `Descartado`
- XML completo (almacenado)
- PDF (almacenado si vino o se descargó del SAT)
- Datos extraídos del XML (para precarga de captura): proveedor identificado, conceptos, impuestos trasladados, retenciones
- Resultado de validación contra catálogos SAT
- Resultado de match con OCs abiertas (sugerencia automática, no decisión final)

Reglas:

- Unicidad por UUID. Si llega el mismo UUID por dos canales, el segundo se marca `Duplicado` y se reporta.
- Un `CfdiRecibido` puede convertirse en `FacturaProveedor`, `NotaCreditoProveedor`, o `AnticipoProveedor` (uno solo) según su tipo y la decisión del Auxiliar de CXP.

### 4.2 `FacturaProveedor`

Entidad central del módulo. Cuando se origina de un CFDI (caso típico) tiene FK a `CfdiRecibido`. Cuando es un documento interno (caja chica con tickets, etc.) puede no tener UUID.

Atributos:

- `CfdiRecibido` (FK, opcional — null para documentos sin CFDI)
- Proveedor (FK)
- UUID (heredado del `CfdiRecibido` si existe, null si es documento interno)
- Folio + serie del proveedor
- Fecha del documento, fecha de contabilización, fecha de vencimiento
- Moneda y tipo de cambio
- Subtotal, descuentos, impuestos trasladados, retenciones, total
- OC asociada (FK, opcional — null si es factura directa)
- Encargado de compras (snapshot desde la OC; null si es directa)
- Subcategoría heredada del proveedor al momento de captura (snapshot)
- Sucursal (FK)
- `ComprobacionGastos` (FK, opcional — cuando la factura es parte de una comprobación de caja chica, aduanales, viáticos o TC)
- `MovimientoTarjetaCredito` (FK, opcional — cuando la factura fue pagada con TC empresarial)
- Estado del pasivo (ver §4.11)
- En revisión (bool) + motivo y dependencia revisora
- Redondeo aplicado (decimal, hasta tolerancia del proveedor)
- Anticipo aplicado total (suma de aplicaciones de CFDIs de anticipo)
- Notas de crédito aplicadas total
- Importe pagado (proyección desde eventos de Tesorería)
- Saldo pendiente derivado: `total − anticipo_aplicado − nc_aplicadas − importe_pagado`
- Conceptos contables aplicados (lista de `ConceptoContable` por línea o por documento)
- Bitácora de cambios de estado
- Datos de auditoría (capturado por, modificado por, autorizado por, fechas)

### 4.3 `LineaFacturaProveedor`

- Posición en el documento
- Producto o servicio (FK al catálogo)
- Cantidad, unidad de medida, precio unitario
- Subtotal de línea, descuentos, impuestos
- `ConceptoContable` asociado (cuando difiere del default del proveedor)
- Línea de OC vinculada (FK, opcional — para facturación parcial de OC)

### 4.4 `NotaCreditoProveedor`

CFDI Egreso emitido por el proveedor. Análoga a `FacturaProveedor` más:

- Motivo (catálogo SAT)
- Factura(s) origen relacionada(s) (vía UUID de relación CFDI tipo 01 o 07)
- Tipo: `Descuento` / `Devolucion` / `AmortizacionAnticipo`
- `NotaCargo` que la formaliza (FK, opcional — cuando la NC del proveedor formaliza fiscalmente una nota de cargo que emitimos)

### 4.5 `AnticipoProveedor`

Cuando pagamos un anticipo a un proveedor antes de recibir la factura final.

- Proveedor (FK)
- UUID del CFDI de anticipo emitido por el proveedor
- Serie: **FANT** (no ANT — corrección del área)
- Monto entregado, moneda
- Monto amortizado (acumulado por NCs del proveedor)
- Saldo amortizable derivado: `entregado − amortizado`
- Estado: `Abierto` / `Amortizado` / `Cancelado`
- OC asociada (FK, si aplica)
- Facturas finales vinculadas (M2M)
- `ConceptoContable`: `ANTICIPO_A_PROVEEDOR_MXP` o `ANTICIPO_A_PROVEEDOR_USD`

**Nota:** El concepto contable distingue moneda igual que en Facturación lado cliente. Lógica fiscal simétrica al anticipo a clientes, invertida (saldo es activo en lugar de pasivo).

### 4.6 `NotaCargo`

Documento interno emitido por nosotros AL proveedor.

- Proveedor (FK)
- Folio interno (consecutivo del ERP, no del SAT)
- Concepto (texto + `ConceptoContable`)
- Monto, moneda
- Factura(s) origen relacionada(s) — opcional
- Devolución de mercancía relacionada (FK opcional a `DevolucionAProveedor` de Almacén sub-flujo 8.B)
- Adjuntos de soporte
- Estado: `Borrador` / `Autorizada` / `Aplicada` / `Formalizada` / `Cancelada`
- `NotaCreditoProveedor` que la formaliza (FK, opcional — cuando el proveedor emite NC fiscal que valida nuestra nota de cargo, el estado pasa a `Formalizada`)
- Datos de autorización

**Aclaración solicitada por el área:** Sí, la nota de cargo es un documento interno que el proveedor puede formalizar emitiendo una NC fiscal. Cuando eso ocurre, la NC del proveedor entra como `NotaCreditoProveedor` con FK de regreso a la nota de cargo que formaliza.

### 4.7 `ComprobacionGastos` (entidad agrupadora)

Para los flujos sin OC que agrupan múltiples CFDIs o documentos.

- Tipo: `ReembolsoCajaChica` / `Viaticos` / `TarjetaCreditoEmpresarial` / `Otros`
- Empleado o responsable (FK)
- Período / pedimento / viaje al que se refiere
- Estado: `EnCaptura` / `EnRevision` / `Aprobada` / `Aplicada`
- Total computado de los CFDIs y movimientos incluidos
- Anticipo recibido (cuando aplica — viáticos)
- Diferencia a favor o en contra del empleado (cuando aplica)
- Adjuntos
- Datos de autorización

**Relación con `FacturaProveedor`:** Cada CFDI individual incluido en la comprobación se registra como `FacturaProveedor` independiente, con FK opcional `comprobacion_gastos_id` apuntando aquí. Esto mantiene `FacturaProveedor` como entidad fiscal canónica (cada CFDI cuenta para gasto, IVA y DIOT, requerimiento del área) y `ComprobacionGastos` como envoltura de proceso.

**Aduanales NO entra aquí.** Tras la aclaración del área, las agencias aduanales tienen OC; entran al flujo estándar con autorización doble (ver §7.2).

### 4.8 `MovimientoTarjetaCredito` (NUEVA)

Para la variante 4 sin OC. Cada movimiento individual realizado con TC empresarial.

- Tarjeta (FK al catálogo de tarjetas)
- Empleado/responsable que la usó
- Fecha del movimiento
- Monto, moneda
- Proveedor (FK, opcional — cuando se puede identificar)
- `FacturaProveedor` (FK, opcional — cuando hay CFDI registrado)
- Concepto, descripción libre
- Adjuntos (ticket si no hay CFDI)
- `ConceptoContable`
- Estado: `Registrado` / `ConciliadoConEstadoCuenta` / `PagadoAlBanco`
- Estado de cuenta de TC al que pertenece (FK a `EstadoCuentaTC`, populated en conciliación)

**Modelo de pago particular:** Cuando se registra el movimiento, la deuda con el proveedor se considera saldada (porque pagó la TC). La deuda queda con el banco. En la fecha de corte, el `EstadoCuentaTC` genera un pasivo agregado contra el banco que se paga como factura normal de un proveedor especial (el banco).

### 4.9 `EstadoCuentaTC` (NUEVA)

Agrega los movimientos de una TC en su periodo de corte.

- Tarjeta (FK)
- Periodo (fecha desde / fecha hasta del corte)
- Movimientos incluidos (M2M con `MovimientoTarjetaCredito`)
- **Archivo del banco cargado** (FK a blob: el Excel/CSV exportado del portal Amex u otro banco — §7.4.3, cerrado en §13.1 punto 6)
- **Líneas crudas del archivo del banco** (parseadas: fecha, monto, merchant, referencia)
- **Líneas no conciliadas** (cargos del archivo que no encontraron match en `MovimientoTarjetaCredito`; el Auxiliar las captura como movimientos retroactivos o ajustes antes de cerrar)
- Total
- Fecha de pago al banco
- `FacturaProveedor` asociada (FK — el pasivo agregado contra el banco emisor)
- Estado: `EnConciliacion` / `Conciliado` / `Cerrado` / `PagadoBanco`

### 4.10 `EvidenciaAutorizacion`

Soporte digital de autorizaciones informales.

- Documento al que pertenece (polimórfico: `FacturaProveedor`, `AnticipoProveedor`, `NotaCargo`, `ComprobacionGastos`)
- Tipo: `CapturaWhatsapp` / `Audio` / `Email` / `FirmaEscaneada` / `Otro`
- Archivo adjunto
- Comentario: quién autorizó (rol + nombre), cuándo, medio
- Estado de firma física: `NoAplica` / `Pendiente` / `Recibida`
- Fecha límite para recepción de firma (cuando aplica)
- Capturado por, fecha de captura

### 4.11 Estados del pasivo (reducidos)

Aplicable a `FacturaProveedor`. Se redujo de 8 a 5 estados aplicando el aprendizaje de Requisiciones.

| Estado | Significado |
|---|---|
| `Capturada` | Datos cargados, conciliación con OC pendiente o no aplica |
| `EnRevision` | Tiene observación, discrepancia, o el proveedor está en revisión |
| `Autorizada` | Lista para que Tesorería la programe a pago |
| `Pagada` | Saldo igual a cero (parcial o total se distingue por el campo `saldo_pendiente`) |
| `Cancelada` | Anulada por cualquier motivo (incluye rechazo por tolerancia, cancelación de CFDI por el proveedor, error de captura) — con campo `motivo_cancelacion` |

**Estados removidos respecto a v0.1:**

- `PagadaParcial` y `PagadaTotal` colapsados en `Pagada` (la parcialidad se ve por saldo).
- `VencidaSinPago` removido (es cálculo derivado: `Autorizada` AND `fecha_vencimiento < hoy`).
- `Rechazada` plegado en `Cancelada` con motivo "rechazada por tolerancia, devuelta a Compras".

Transiciones permitidas:

```
Capturada → EnRevision        (al detectar observación)
Capturada → Autorizada        (todo OK, OC autorizada autoriza factura)
Capturada → Cancelada         (error en captura)
EnRevision → Autorizada       (todas las observaciones liberadas)
EnRevision → Cancelada        (no se puede resolver, devuelta a Compras o anulada)
Autorizada → EnRevision       (detección posterior de observación)
Autorizada → Pagada           (evento de Tesorería de pago total)
Autorizada → Cancelada        (cancelación de CFDI por proveedor antes de pago)
Pagada → EnRevision           (raro: reversión de pago, requiere conciliación)
```

---

## 5. Catálogos

### 5.1 Catálogo de Proveedores

Master en el ERP — vive en el módulo `DatosMaestros` del área Administración (ver [`docs/modulos/administracion/01-diseno.md`](../administracion/01-diseno.md)). **Alta conjunta:** la solicita Compras, valida CXP, ambas firman antes de activar. Esto sustituye el modelo actual donde el alta la hace solo Compras y CXP encuentra los datos incompletos después.

Atributos relevantes (los que CXP requiere del master):

- Datos fiscales (RFC, régimen, CP fiscal)
- Datos bancarios (cuenta para depósitos)
- Subcategoría del catálogo de categorización (§5.2)
- Flag `en_revision` con motivo, fecha de entrada, dependencia revisora
- Encargado de compras default (cuando aplica)
- Día(s) de facturación / día(s) de pago (acuerdo comercial)
- Plazo de pago (días)
- Moneda preferida
- Es nacional o extranjero (afecta retenciones y obligaciones fiscales)
- Email para envío de complementos de pago
- Email del que recibimos sus CFDIs (mailbox de origen reconocido)
- **Tolerancia de conciliación factura vs. OC** — monto absoluto o porcentaje, configurable por proveedor (cambio importante respecto a v0.1)
- **Adjuntos obligatorios para el alta:** Constancia de Situación Fiscal, contrato vigente, acta constitutiva (si aplica), identificación del representante legal, comprobante de domicilio. Sin estos no se activa el proveedor.

**Migración desde SAP:** depuración previa obligatoria. **No se migran proveedores sin movimientos en los últimos 2 años.** Para los que sí se migran, hay que completar los campos que SAP no tenga, antes del go-live.

### 5.2 Catálogo de categorización de proveedores

Dos niveles: categoría padre + subcategoría. Aprobado por el área en v0.1.

| Categoría padre | Subcategorías |
|---|---|
| Materia prima e insumos | Materia Prima · Insumos |
| Gastos de personal | Sueldos y salarios · Transporte de personal · Gastos de comedor · Impuestos y cuotas · Sueldos y salarios indirectos |
| Gastos operativos | Gastos y fletes aduanales · Energía eléctrica · Combustible · Comisiones por ventas al extranjero · Cuotas y suscripciones · Mantenimiento y refacciones · Transportes (mantenimiento) |
| Gastos administrativos | Gastos administrativos (Amex) · Gastos corporativos · Servicios especializados · Seguridad y limpieza · Seguros y fianzas · Servicios profesionales · Sistemas y comunicación · Reembolso caja chica / viáticos · Otros gastos |
| Financiamiento | Préstamos · Arrendamientos · Intereses · Inversión · Comisiones bancarias |

**Reglas:**

- Subcategoría asignada al proveedor, no a la factura. La factura hereda la categoría vía snapshot.
- Cambios de subcategoría con bitácora.
- Catálogo estable, cambios requieren autorización del responsable de CXP.

### 5.3 Catálogo de motivos de revisión (unificado)

Consolida los dos listados que dio el área (anots 7.1 y 16.1):

| Motivo | Cuándo aplica | SLA específico | Quién libera |
|---|---|---|---|
| Discrepancia con OC fuera de tolerancia | Monto factura supera tolerancia del proveedor | 5 días hábiles | Compras (al corregir OC) |
| Daños o defectos en mercancía | Recepción reporta daño físico (vía `RecepcionRegistrada.merma` de Almacén §5) | 5 días hábiles | Calidad o Almacén |
| Diferencia de precio | Precio de factura diferente al acordado | 5 días hábiles | Compras |
| Devolución pendiente | Devolución a proveedor abierta sin liquidar (Almacén sub-flujo 8.B) | 5 días hábiles | Almacén |
| No conformidad con servicio o producto | Servicio no cumple especificación | 5 días hábiles | Área usuaria |
| Tiempo de respuesta del proveedor | Entrega fuera de plazo | 5 días hábiles | Compras |
| Calidad de servicio insuficiente | Servicio recibido no satisfactorio | 5 días hábiles | Área usuaria |
| Reclamo de garantía abierto | Reclamo no resuelto | 5 días hábiles | Compras |
| **Falta complemento de pago + 5 días** | REPP no emitido pasados 5 días del pago | 5 días hábiles desde detección | CXP (al recibir REPP) |
| Documentación fiscal incompleta | CFDI con datos inválidos, RFC mal, fechas inconsistentes | 5 días hábiles | CXP |
| Disputa contractual | Reclamación legal | 15 días hábiles | Legal o Dirección |
| Pendiente firma o autorización | Autorización formal pendiente | 5 días hábiles | El autorizador correspondiente |
| Indicación expresa | Bloqueo manual por instrucción | Sin SLA, libera el solicitante | El solicitante |
| Otros motivos | Caso no clasificable | 5 días hábiles | El área dueña |

**SLA general:** 5 días hábiles para resolver, con notificación al gerente del área en día 3 y día 5; al día 10 escala a Director del área. Disputas contractuales tienen SLA extendido de 15 días.

### 5.4 Flag de revisión (separado del catálogo)

El estado de revisión es atributo independiente del proveedor (replicado como snapshot en la factura para reportes). Esto resuelve la confusión del reporte SAP actual donde "COMPRAS MP" y "COMPRAS MP REVISION" parecían categorías paralelas.

```
Proveedor:
  subcategoria_id          (FK al catálogo 5.2 — taxonomía estable)
  en_revision              (bool — operativo)
  motivo_revision          (FK a motivos, ver 5.3)
  fecha_entrada_revision
  dependencia_revisora_id  (FK al área)
```

### 5.5 Otros catálogos

- **Sucursal** — master en `Administracion`.
- **Empleados** — master en `Administracion` (para responsables de viáticos, caja chica, TC empresarial). Atributo `puesto` requerido para política de viáticos (§7.4.2).
- **Puestos** — master en `Administracion` / RH. Requerido para la tabla `politicas_viaticos` (cerrado en §13.1 punto 3).
- **Dependencias / áreas revisoras** — **catálogo compartido en `Administracion`/`DatosMaestros`** (cerrado en §13.1 punto 7). Tabla canónica: `datos_maestros.dependencias` o `administracion.areas_organizacionales` (naming exacto pendiente de coordinación con módulo Administración). Reusable por Notificaciones, RH, Obras.
- **Tarjetas de crédito empresariales** — **master local en CxP** (`cuentas_por_pagar.tarjetas_credito`). Modelo: varias TC corporativas con titular fijo por tarjeta (cerrado en §13.1 punto 4). Atributos: emisora (banco), número enmascarado, titular default (FK `Empleado`), límite, fecha de corte, fecha límite de pago, estado (`Activa` / `Bloqueada`).
- **Aprobadores con límites** — **catálogo local en CxP** (`cuentas_por_pagar.aprobadores_limites`). Modelo por usuario + tipo de gasto + monto máximo (cerrado en §13.1 punto 2). Atributos: `(empleado_id, tipo_gasto, monto_max, vigencia_desde, vigencia_hasta)`. Mantenimiento por RH o por el responsable de CxP.
- **Política de viáticos** — **catálogo local en CxP** (`cuentas_por_pagar.politicas_viaticos`). Modelo por puesto + tipo de destino (cerrado en §13.1 punto 3). Atributos: `(puesto_id, tipo_destino, monto_max_dia, dias_max, moneda)` donde `tipo_destino ∈ { Nacional, Internacional }`.
- **Catálogos SAT** — espejo local que comparte con Facturación, via FiscalAPI (ADR-0027).
- **`ConceptoContable`** — compartido con Facturación, vive en módulo Contabilidad cuando exista.
- **Documentos soporte de proveedor** (CSF, contrato, etc.) — adjuntos en blob storage (ADR-0024).

---

## 6. Workflow de revisión

Mecanismo para tener un pasivo bloqueado hasta resolver una observación.

### 6.1 Disparadores de entrada en revisión

Tres formas de entrar a revisión, en orden de frecuencia esperada:

1. **Automático por validación.** El sistema detecta condición que dispara revisión: RFC inválido, OC con tolerancia excedida (la tolerancia es por proveedor), fecha de vencimiento anterior a la de contabilización, recepción con merma reportada por Almacén (campo `merma` del evento `OcRecepcionRegistradaEvent`), etc. Se asigna automáticamente al área correspondiente.
2. **Manual por el Auxiliar de CXP.** Al capturar la factura, marca "Enviar a revisión" indicando motivo (del catálogo §5.3) y dependencia.
3. **Por el estado del proveedor.** Si el proveedor tiene `en_revision = true`, todas sus facturas nuevas entran automáticamente a revisión hasta que el proveedor sea liberado.

### 6.2 Liberación

La dependencia revisora tiene bandeja de "Facturas en revisión asignadas a mi área". Para liberar, el responsable del área indica:

- Acción tomada (texto + plantillas)
- Si la liberación cambia el monto (NC entrante, por ejemplo), se vincula el documento que ajusta
- La factura pasa a `Autorizada` automáticamente si no quedan otras observaciones; si quedan, sigue en revisión

### 6.3 Liberar al proveedor

Cuando un proveedor está `en_revision = true` y se resuelven los motivos, un usuario con permisos (responsable de CXP o el área que lo puso en revisión) lo libera. Bitácora del cambio.

**Comportamiento de facturas existentes:**

- Si el único motivo de revisión de la factura era el flag del proveedor, se libera automáticamente.
- Si la factura tenía otros motivos acumulados, se mantiene en revisión hasta que esos se resuelvan individualmente.

### 6.4 SLA

**Confirmado el 2026-05-22 (§13.1 punto 8):** SLA único de **5 días hábiles** como estándar para resolver una revisión, con escalamientos automáticos:

- **Día 3:** notificación al gerente del área revisora.
- **Día 5:** notificación al gerente + alerta visible en bandeja.
- **Día 10:** escalamiento automático al Director del área revisora.

**Excepción:** motivo "Disputa contractual" tiene SLA extendido de **15 días hábiles** (mismos escalamientos: día 8, día 12, día 20).

Instrumentación: cada transición de estado y notificación queda en bitácora con timestamp para que en v1.1 se pueda diferenciar SLA por motivo si los reportes muestran patrones útiles (por ejemplo, "discrepancia con OC" típicamente se resuelve en 2 días pero el SLA único de 5 lo deja pasar sin presión).

---

## 7. Flujos de captura

### 7.1 Recepción y captura de CFDI estándar

Todos los flujos parten del mismo punto: un `CfdiRecibido` que llega al ERP por uno de los canales (§9). El Auxiliar de CXP abre la bandeja de CFDIs por procesar y trabaja desde ahí.

**Pre-carga desde el XML:** El sistema parsea el XML al recibirlo y precarga los campos de captura (RFC emisor → proveedor, conceptos, montos, impuestos, retenciones). El Auxiliar solo revisa y completa lo que falte (OC, sucursal, conceptos contables específicos, etc.). Requerimiento explícito del área.

### 7.2 Factura con OC (flujo estándar, incluye Aduanales)

Caso típico — el grueso del volumen.

1. CFDI llega al `CfdiRecibido` por cualquiera de los canales.
2. Auxiliar abre captura, selecciona el CFDI de la bandeja. Los datos del XML se precargan.
3. Sistema sugiere OC asociada (por RFC + monto aproximado + folio del proveedor si viene en XML). Auxiliar confirma o selecciona manualmente.
4. Sistema concilia montos: factura vs. OC.
   - Diferencia ≤ **tolerancia del proveedor** → aplica `Redondeo`, pasa.
   - Diferencia > tolerancia → `Cancelada` con motivo "rechazada por tolerancia, devuelta a Compras". Se notifica a Compras para corrección de OC.
5. Si pasa, el sistema:
   - Hereda Encargado de compras desde la OC (snapshot)
   - Hereda Subcategoría desde el Proveedor (snapshot)
   - Verifica si el Proveedor está `en_revision` → si sí, factura entra a revisión
   - Verifica otras condiciones para revisión automática
6. Si no hay observaciones, la factura pasa a `Autorizada` (OC autorizada autoriza factura y pago al vencimiento).
7. La factura aparece en la bandeja de Tesorería (vía evento, ver §11).

**Caso especial: Aduanales.** Tienen OC (exportación: OC fija vía OTR; importación: una OC por cotización). Pero requieren **doble autorización**:

- **Autorización 1:** Responsable de Comercio Exterior → habilita el registro del pasivo
- **Autorización 2:** Dirección → habilita el pago

Esta doble autorización ya existe operativamente en una pantalla específica. La replicamos en el ERP con el mismo modelo.

### 7.3 Nota de crédito del proveedor

El proveedor emite NC contra una factura previa (descuento, devolución, amortización de anticipo).

1. CFDI de la NC llega al `CfdiRecibido`.
2. Auxiliar la captura, vinculándola a la factura origen vía relación CFDI tipo 01 o 07.
3. Sistema valida que la factura origen exista y tenga saldo > 0.
4. Si la NC es por amortización de anticipo (relación 07), se asocia al `AnticipoProveedor` correspondiente y reduce su saldo.
5. Si la NC formaliza fiscalmente una `NotaCargo` previa (descuento por daños, mermas, etc.), se vincula y la nota de cargo pasa a estado `Formalizada`.
6. La NC reduce el saldo de la factura origen.

**Caso particular: NC pre-factura** (cerrado en §13.1 punto 5 como **operativamente raro**). Cuando la NC llega antes que la factura final (ajuste preventivo del proveedor), se modela como excepción:

- Se captura con FK a factura origen = `null` y estado `EnEspera`.
- Worker periódico (`NotaCreditoEnEsperaMatchWorker`, cada 24h) intenta hacer match contra facturas nuevas del mismo proveedor cuyo UUID coincida con el `cfdiRelacionado` declarado en el XML de la NC.
- Al detectar match → vincula la NC, mueve estado a `Aplicada`, reduce saldo de la factura.
- Si pasan 30 días sin match → genera alerta al Auxiliar de CxP para que investigue manualmente (puede ser que el proveedor olvidó emitir la factura o canceló).
- Si el volumen real crece a >5 NCs pre-factura por mes, se reabre como **bandeja dedicada** "NCs pendientes de factura origen" con UI propia en v1.1.

**Devolución física (relación CFDI tipo 03):** ver §7.7 — el saldo de la NC se aplica solo cuando Almacén confirma la salida física vía evento `OcDevolucionRegistradaEvent` (sub-flujo 8.B de Almacén §8.4).

### 7.4 Variantes sin OC (4)

#### 7.4.1 Reembolso de Caja Chica

Modelado como `ComprobacionGastos` agrupadora con N CFDIs y/o tickets sin factura.

**Aplica cuando:** una sucursal o área operativa gasta efectivo en compras menores recurrentes (papelería, mensajería local, refrigerios, refacciones pequeñas) sin OC.

**Frecuencia:** semanal o quincenal por sucursal/área.

**Paso a paso:**

1. Responsable de caja chica conserva tickets y facturas durante el período.
2. Al cierre, captura `ComprobacionGastos` tipo `ReembolsoCajaChica`, adjunta CFDIs y tickets.
3. Sistema valida que cada CFDI esté a nombre de Millet.
4. Cada CFDI individual se registra como `FacturaProveedor` ligado a la comprobación, con subcategoría "Reembolso caja chica / viáticos".
5. Total autorizado por responsable de la sucursal/área (con catálogo de límites por persona — el área indicó que esto se integra).
6. Pasa a Tesorería para reponer la caja.

#### 7.4.2 Comprobación de Viáticos (pasivo de préstamo a empleado)

**Aplica cuando:** un empleado (de los roles que viajan: ventas, obras, ingresos, inventarios) viaja por trabajo, recibe anticipo de viáticos, gasta, y comprueba con CFDIs al regresar.

**Diferencia fiscal clave:** el anticipo de viáticos es **un pasivo de préstamo al empleado**, no un anticipo a proveedor. No genera CFDI de anticipo. Es un movimiento interno que se cancela contra los comprobantes.

**Política de viáticos** (cerrada en §13.1 punto 3): tope automático por **puesto + tipo de destino**. Catálogo `politicas_viaticos` con `(puesto_id, tipo_destino, monto_max_dia, dias_max, moneda)`. `tipo_destino ∈ { Nacional, Internacional }`. El sistema valida al solicitar el anticipo: `monto_solicitado <= monto_max_dia * dias_estimados`.

**Paso a paso (formato nuevo, sustituye el papel actual):**

1. Empleado solicita anticipo de viáticos en pantalla del ERP. Indica **destino** (con captura libre + clasificación Nacional/Internacional), periodo (fecha desde/hasta → `dias_estimados`), monto estimado.
2. **Sistema valida automáticamente** contra `politicas_viaticos`: lee el puesto del empleado y el tipo de destino; calcula `tope = monto_max_dia * dias_estimados`. Si `monto_estimado > tope` o `dias_estimados > dias_max` → la solicitud se marca como "excede política" y requiere autorización adicional de Dirección de Finanzas además del Jefe directo.
3. Jefe directo autoriza en la misma pantalla (Nivel 1). Si excede política → Dirección de Finanzas firma como Nivel 2 (las dos firmas se gestionan vía `EvidenciaAutorizacion`).
4. Una vez autorizado, se genera el pasivo de préstamo al empleado (`ConceptoContable: PRESTAMO_EMPLEADO`) y Tesorería paga al empleado.
5. Al regreso, el empleado captura su propia comprobación en la pantalla. Sube facturas, tickets, remisiones. Estado: `PorRevisarCXP`.
6. Auxiliar de CXP liga los archivos PDF y XML de los CFDIs presentados, valida que estén a nombre de Millet, registra cada CFDI individual como `FacturaProveedor` con subcategoría "Reembolso caja chica / viáticos".
7. Sistema calcula:
   - Total comprobado (suma de CFDIs y tickets)
   - Diferencia contra anticipo entregado
8. CXP libera la comprobación.
9. Si gastó más, Tesorería reembolsa al empleado. Si gastó menos, el empleado devuelve la diferencia.
10. Pasivo de préstamo al empleado se cancela contra la suma de comprobantes + diferencia liquidada.

> **Pendiente para Administración / RH:** llenar el catálogo `puestos` (master en `DatosMaestros`) con los puestos que viajan + los topes acordados con Dirección. Sin estos datos el módulo bloquea solicitudes de viáticos al go-live.

#### 7.4.3 Tarjetas de Crédito Empresariales — VARIANTE NUEVA

**Aplica cuando:** un empleado autorizado realiza pagos con una TC empresarial. Los pagos pueden tener CFDI (preferido) o solo ticket. Una sola TC genera muchos movimientos de muchos proveedores distintos en un periodo.

**Alcance del modelo** (cerrado en §13.1 punto 4): **varias TC corporativas con titular fijo por tarjeta**. Cada `Tarjeta` tiene un titular único responsable que firma el estado de cuenta y autoriza el pago al banco. Catálogo `cuentas_por_pagar.tarjetas_credito` con `(emisora, numero_enmascarado, titular_id, banco_proveedor_id, limite, fecha_corte, dia_pago, estado)`.

**Modelo conceptual:** Cuando se registra el movimiento de TC, **la deuda con el proveedor se considera saldada** (porque la TC ya pagó). La deuda se transfiere al banco emisor de la TC. En la fecha de corte, el estado de cuenta del banco genera un único pasivo agregado que se paga como cualquier otra factura.

**Paso a paso:**

1. Empleado realiza pago con TC empresarial. Recibe ticket y, cuando aplica, solicita CFDI.
2. **Titular de la TC** (responsable principal) o **Auxiliar de CxP** captura `MovimientoTarjetaCredito` por cada cargo, asociado a una `tarjeta_id` específica:
   - Si hay CFDI: se liga al `CfdiRecibido` y al `FacturaProveedor` correspondiente. El estado del pasivo del proveedor pasa directamente a `Pagada`.
   - Si no hay CFDI: el movimiento queda con el ticket adjunto y concepto contable directo (no genera `FacturaProveedor` ni afecta proveedor; afecta gasto e IVA si el ticket es CFDI simplificado, o solo gasto si es ticket no fiscal).
3. En fecha de corte (`tarjeta.fecha_corte`), el Auxiliar de CxP o el titular **descarga el estado de cuenta del portal del banco** (Amex web u otro) **como Excel/CSV** (cerrado en §13.1 punto 6) y lo carga a la pantalla de conciliación de la tarjeta.
4. **Sistema parsea el archivo** y hace match automático contra `MovimientoTarjetaCredito` por **fecha + monto + merchant** (campo libre comparable por similitud). Movimientos conciliados se marcan `ConciliadoConEstadoCuenta`; los no conciliados quedan en bandeja para revisión humana (cargos que faltaron capturar, devoluciones del banco, intereses, comisiones).
5. Se genera `EstadoCuentaTC` que agrupa todos los movimientos del periodo + las líneas del archivo del banco no conciliadas (que se capturan como movimientos retroactivos o ajustes).
6. El total del estado de cuenta genera un `FacturaProveedor` contra el **banco emisor de esa tarjeta** (proveedor especial, `tipo = BancoEmisorTC`), que se autoriza por Dirección de Finanzas y pasa a Tesorería para pago en la fecha indicada.
7. Al pagarse, los movimientos individuales pasan a `PagadoAlBanco`.

**Implicación contable:** Cada CFDI debe registrarse individualmente para que afecte gasto, IVA y DIOT (requerimiento explícito del área). El pasivo contra el banco es solo el agregador financiero, no el pasivo fiscal.

> **Diferido a vNext** (§13.2 punto 13): integración directa con API del banco (Amex Open Banking u otro). MVP arranca con descarga manual del portal + carga Excel/CSV. Re-evaluación cuando el volumen de movimientos justifique la inversión en credenciales OAuth bancarias y monitoreo.

> **Diseño del parser de Excel/CSV:** soportar al menos 2 formatos distintos (Amex y otro banco hipotético) con configuración por proveedor: nombre de columnas + formato de fecha + separador. Cuando entre un tercer banco, agregar perfil sin tocar código.

#### 7.4.4 Otros gastos sin OC

Casos puntuales: pagos de servicios menores, reembolsos no clasificados. Captura directa como `FacturaProveedor` con OC null y autorización del responsable del área. Sin `ComprobacionGastos` agrupadora.

### 7.5 Anticipos a proveedores

Cuando le pagamos a un proveedor antes de recibir la factura final.

1. Compras o área correspondiente **solicita** el anticipo (esto vive fuera de CXP — en Compras o como flujo independiente).
2. La solicitud requiere autorización de Dirección General (suplencia: Dirección de Finanzas). Sin importe mínimo.
3. Una vez autorizada, Tesorería ejecuta el pago.
4. Proveedor emite CFDI de anticipo (serie **FANT**) y nos lo envía.
5. CFDI llega al `CfdiRecibido`. Auxiliar de CXP lo captura como `AnticipoProveedor`: UUID, monto entregado, OC asociada (si aplica), proveedor.
6. El anticipo queda con saldo amortizable = monto entregado.
7. Cuando lleguen facturas finales del proveedor que aplican contra el anticipo, se vinculan y reducen su saldo.
8. Cuando el proveedor emita NC por amortización (relación 07), se registra contra el anticipo y reduce su saldo.
9. Anticipo cambia a `Amortizado` cuando saldo = 0.

**Aclaración:** El Auxiliar de CXP **no gestiona** la solicitud ni la autorización del anticipo. Solo registra el CFDI de anticipo cuando el proveedor lo emite (requerimiento del área).

### 7.6 Notas de cargo

Documento interno emitido por nosotros al proveedor por daños, mermas, descuentos comerciales no documentados como NC.

1. Comprador, Almacén o Calidad identifica la causa.
2. Auxiliar de CXP captura la nota de cargo con concepto, monto y factura origen (si aplica).
3. Adjunta evidencia (fotos, reporte de calidad, etc.).
4. Solicita autorización de Dirección (sin importe mínimo).
5. Una vez autorizada, se aplica contra el saldo del proveedor.
6. Se notifica al proveedor.
7. **Si el proveedor emite NC fiscal formalizando la nota de cargo**, la NC entra como `NotaCreditoProveedor` con FK a la nota de cargo, y el estado pasa a `Formalizada`. Sin esta NC, la nota de cargo queda como ajuste interno sin formalización fiscal.

### 7.7 Devoluciones de mercancía a proveedor (NUEVO)

Flujo inverso de factura, cruza con Almacén (sub-flujo 8.B en [`almacen/00-levantamiento.md`](../almacen/00-levantamiento.md#84-sub-flujo-8b--devolución-a-proveedor-externa)). **Contrato de eventos cerrado** — ver §11.6.

1. Almacén o Calidad detecta defecto, sobrante o necesidad de devolver mercancía recibida.
2. Registra salida de mercancía en Inventario (vive en módulo de Inventario, no en CXP).
3. La salida genera evento que CXP recibe (contrato a definir con Almacén).
4. Auxiliar de CXP emite `NotaCargo` ligada a la devolución y a la factura original del proveedor.
5. Autorización de Dirección.
6. Se aplica contra el saldo del proveedor.
7. Si el proveedor emite NC fiscal por la devolución (relación 03), entra como `NotaCreditoProveedor` y formaliza la nota de cargo.

> **Coordinación requerida:** este flujo es uno de los puntos críticos a cerrar en el levantamiento de Almacén — define cómo se sincroniza la NC fiscal del proveedor con la salida física registrada por Almacén, qué pasa si uno llega antes que el otro, y cómo se invalida una NC si la salida física se cancela.

---

## 8. Autorizaciones

### 8.1 Autorización del pasivo y del pago

| Tipo de pasivo | Autorización |
|---|---|
| Factura con OC dentro de tolerancia (no Aduanales) | Automática — la OC autorizada autoriza el pasivo y el pago al vencimiento |
| Factura con OC fuera de tolerancia | `Cancelada`, devuelta a Compras |
| Factura Aduanales (con OC) | **Doble:** Responsable Comercio Exterior (registro de pasivo) + Dirección de Finanzas (autorización de pago) |
| Factura sin OC — Caja Chica | Responsable de sucursal/área con **límite del catálogo `aprobadores_limites`** (por usuario + tipo de gasto + monto, §5.5). Si excede, escala automáticamente a Dirección de Finanzas. |
| Factura sin OC — Viáticos | Jefe directo del empleado con **límite del catálogo `aprobadores_limites`** + Dirección de Finanzas si excede política. Validación adicional contra `politicas_viaticos` por puesto + destino al momento de la solicitud del anticipo (§7.4.2). |
| Movimiento de TC Empresarial | Validación contra estado de cuenta. Pago al banco autorizado por Dirección de Finanzas. Capturar el movimiento individual no requiere autorización si el usuario titular está en `aprobadores_limites` para `TarjetaCreditoEmpresarial` y no excede su monto. |
| Anticipo a proveedor | Dirección General (suplencias DF → DG-otra-área) — sin importe mínimo |
| Nota de cargo | Dirección General (suplencias DF → DG-otra-área) — sin importe mínimo |

> **Lógica de evaluación del catálogo `aprobadores_limites`** (cerrado en §13.1 punto 2): el sistema consulta `(empleado_id = usuario_que_aprueba, tipo_gasto = tipo_de_comprobacion, vigencia_desde <= hoy <= vigencia_hasta)`. Si el monto a aprobar **≤** `monto_max` del registro, la autorización es válida. Si **>** `monto_max`, requiere un segundo nivel (Dirección de Finanzas por default, con cadena de suplencias §8.3).

### 8.2 Autorizaciones informales con evidencia

Realidad operativa: las autorizaciones se obtienen por WhatsApp, voz, email; la firma autógrafa llega después.

**Formalización en el ERP:**

- Pantalla de autorización con upload de evidencia (capturas, audios, emails, fotos).
- Comentario obligatorio: quién autorizó (nombre + rol), cuándo, medio.
- Flag "Firma física pendiente" con fecha límite por defecto de 5 días hábiles (configurable).
- Bandeja de "Autorizaciones con firma pendiente" para seguimiento.
- El pasivo no avanza a Tesorería sin al menos un adjunto + comentario completo.
- Cuando llega la firma física, se sube como adjunto adicional.

**Vencimiento de firma física:** notificación al día 3 y día 5; escalamiento al día 7 a Dirección General con copia al auditor interno. El pasivo no se revierte automáticamente.

### 8.3 Suplencias

Cadena de suplencia formalizada (cerrada en §13.1 punto 1):

1. **Nivel primario:** Dirección General firma.
2. **Suplencia 1:** Director de Finanzas firma cuando DG no está disponible.
3. **Suplencia 2 (tercer nivel):** **Director General de otra área** (Operaciones o Comercial, según política vigente) firma cuando ni DG ni DF están disponibles. La asignación de quién ocupa esta tercera posición vive en el catálogo `aprobadores_limites` (§13.1 punto 2) con vigencia configurable; RH es responsable de actualizarla cuando cambia la persona.

El sistema lee la cadena al momento de presentar la pantalla de autorización: si DG está activa para esa fecha, muestra DG; si está marcada como ausente, baja a DF; si DF también, baja al tercer nivel. Las ausencias se capturan en `aprobadores_limites` como ventanas (vigencia desde/hasta = vacaciones, comisión, etc.).

---

## 9. Recepción de CFDI

### 9.1 Canales de recepción

El ERP centraliza la recepción de CFDIs en tres canales para MVP, con un cuarto reservado para post-MVP:

1. **Descarga automática del SAT vía FiscalAPI** (ADR-0027). Programada nocturna o varias veces al día. Trae todos los CFDIs emitidos a nuestros RFCs receptores y por nuestros RFCs emisores (este último para CXC).
2. **Mailbox dedicado** (ej. `cfdi@millet.com.mx`). Proveedores envían sus CFDIs a este buzón. El ERP parsea adjuntos (XML + PDF), identifica al proveedor por RFC emisor del XML, valida estructura, almacena.
3. **Carga manual** como respaldo, desde la pantalla del Auxiliar.
4. **Portal propio de proveedores** (RESERVADO POST-MVP). El proveedor cargaría XML+PDF a un portal del ERP y vería estado de pago. El modelo de `CfdiRecibido.canal_origen` ya incluye `PortalProveedor` para no migrar después.

**Aclaración solicitada por el área:** El mailbox **no es** el portal propio. Son cosas distintas: el mailbox es pasivo (proveedor manda correo, el ERP lo procesa); el portal sería interactivo (proveedor entra, sube archivo, ve estado, descarga su REPP). El mailbox cubre MVP; el portal se evalúa para fase posterior.

### 9.2 Reglas de mapping del mailbox

Cuando llega un correo al mailbox dedicado:

- **Llave primaria de identificación:** RFC emisor del XML. Esto es lo que ata el CFDI al proveedor del catálogo.
- El email de origen es solo metadato (se compara con el `email_origen_reconocido` del proveedor para alertar si difiere).
- Si el RFC emisor no existe en el catálogo de proveedores, el CFDI queda en `PorProcesar` con bandera "Proveedor no registrado"; alerta al área de alta para que decida si dar de alta o descartar.
- Validación de estructura del XML antes de aceptar (CFDI 4.0, sellos, etc.).

### 9.3 Almacenamiento

Cada CFDI recibido se almacena íntegro (XML + PDF si vino o se descargó) en el repositorio del ERP (Azure Blob, ADR-0024), queda asociado al proveedor por RFC emisor, se valida contra catálogos SAT, se intenta hacer match con OC abierta, y aparece en la bandeja del Auxiliar.

### 9.4 Deduplicación entre canales

Unicidad por UUID. Si llega el mismo UUID por dos canales (ej. mailbox y descarga SAT), el segundo se marca como `Duplicado` y se reporta. El primero conserva el canal original.

### 9.5 Beneficios

- Reemplaza OneFacture y elimina carpetas compartidas como repositorio de CFDIs.
- Bandeja consolidada de "CFDIs por capturar".
- Reduce riesgo de capturas perdidas.
- Permite alertas: CFDI sin capturar pasados 5 días → notifica al área.
- El servicio cubre también CFDIs emitidos por Millet para uso de CXC (Ingresos).

---

## 10. Reportes

### 10.1 Antigüedad de saldos de proveedores

Equivalente al reporte SAP actual, mejorado:

- Filtros: rango de fechas, sucursal, categoría padre, subcategoría, estado de revisión (incluir/excluir o solo en revisión), proveedor, encargado de compras
- Agrupación: por proveedor, por categoría, por encargado
- Buckets de antigüedad: corriente, 1-15, 16-30, 31-60, 61-90, 91-120, mayor a 120
- Cruzable por las dos dimensiones del nuevo modelo: subcategoría × estado de revisión

### 10.2 Antigüedad de anticipos a proveedores

Equivalente al SAP "Antigüedad de las cuentas por pagar-anticipos", mejorado:

- Filtro incluir / no incluir anticipos (gap del reporte actual)
- Para cada anticipo: monto entregado, monto amortizado, saldo no pagado, saldo pendiente de aplicar a facturas (columnas separadas, requerimiento explícito)
- Antigüedad del anticipo

### 10.3 Cartera por categoría y revisión

Equivalente al reporte de cartera al 28/abr, reconstruido con el nuevo modelo:

- Eje vertical: subcategoría del proveedor
- Eje horizontal: estado de revisión
- Buckets de antigüedad por morosidad
- Totales y porcentajes

### 10.4 Pasivos en revisión por área

Bandeja operativa para responsables de áreas revisoras: facturas asignadas a su revisión, días en revisión, monto, proveedor, motivo.

### 10.5 CFDIs recibidos sin capturar

Bandeja de control: `CfdiRecibido` en estado `PorProcesar`. Antigüedad para detectar capturas atrasadas.

### 10.6 Movimientos de TC pendientes de conciliar

Para la variante 4: movimientos de TC sin estado de cuenta asociado todavía. Permite detectar omisiones antes del corte.

### 10.7 Estado de cuenta de TC consolidado

Para cada periodo de corte: total, conciliados vs no conciliados, fecha límite de pago, estado de autorización.

---

## 11. Integraciones

### 11.1 Con Compras

- CXP **lee** OCs autorizadas y abiertas para conciliar facturas (puerto de lectura `IComprasOcReadPort`).
- CXP **publica eventos** de estado de la factura asociada (registrada / rechazada / autorizada / pagada) que Compras consume para sincronizar `SubEstadoFacturacion` de la OC.
- OCs rechazadas por tolerancia regresan a Compras vía evento `FacturaRechazadaPorTolerancia` con motivo y monto detectado.
- Alta conjunta de proveedores: Compras inicia, CXP valida, ambas firman.

> El submódulo de OC ya prevé el sub-estado `Facturacion` (Sin factura / Parcial / Completa) y un listener para eventos de CXP — ver [`docs/modulos/compras-ordenes-compra/01-diseno.md`](../compras-ordenes-compra/01-diseno.md) §1.2 punto 14. El contrato exacto se cierra en `01-diseno.md` de CXP §8.1.

> **Wiring Service Bus cerrado en PR #288 (2026-05-24):**
> `CxpEventListenerWorker` en Compras consume `cuentas-por-pagar-events`
> (subscription `compras-subscription`). El evento
> `cuentas_por_pagar.factura.registrada.v1` lleva
> `lineas_acumuladas_oc[]` (acumulado total por `LineaOcId` calculado en
> CxP al publicar). Compras aplica el acumulado directo en
> `OrdenCompra.RegistrarFacturacionLinea` sin proyecciones espejo.
> Eventos pendientes — ver [§11.6](#116-con-inventario--producción--almacén) y CLAUDE.md "Estado de los workers de la triada".

### 11.2 Con Tesorería (modelo de eventos)

CXP y Tesorería se comunican vía cola/evento (Outbox + Service Bus, ADR-0009), no llamadas síncronas. Operativamente:

- Cuando CXP autoriza un pasivo, **publica un evento** `PasivoAutorizadoParaPagoEvent` con todos los datos: proveedor, montos, vencimiento, datos bancarios, conceptos contables, evidencias.
- Tesorería **suscribe** la cola y procesa cuando puede. No bloquea a CXP.
- Cuando Tesorería paga, **publica** `PagoFacturaProveedorEvent` con UUID del pasivo, monto, fecha.
- CXP **suscribe** y actualiza el campo `importe_pagado` y el estado de la factura como proyección local.
- Igualmente para `PagoRevertido`, `ReppRecibido` (informativo para CXP), y `CancelacionSolicitada` (Tesorería puede pedir cancelar un pasivo si detecta error).

**Ventajas operativas** (para incluir en el correo al área que preguntó por esto): Si Tesorería está caída o procesando otra cosa, CXP sigue trabajando. Los mensajes no se pierden — quedan en la cola hasta procesarse. Para el usuario es transparente: ve el pasivo pasar a `Autorizada` y después a `Pagada`, igual que si fuera síncrono. Para el sistema permite que Tesorería se construya y modifique sin tocar CXP.

**Implicación de diseño:** El campo `importe_pagado` de `FacturaProveedor` es una proyección local de los eventos de Tesorería, no una consulta síncrona. Si Tesorería se cae, el saldo refleja el último evento procesado.

### 11.3 Con Contabilidad

Cada movimiento de CXP genera asientos vía `ConceptoContable`:

- **Registro de pasivo:** cargo a `GASTO_*` (según subcategoría) / abono a `PROVEEDOR_*`
- **Aplicación de anticipo:** cargo a `PROVEEDOR_*` / abono a `ANTICIPO_A_PROVEEDOR_*`
- **Aplicación de NC del proveedor:** cargo a `PROVEEDOR_*` / abono a `DESCUENTO_PROVEEDOR_*`
- **Aplicación de nota de cargo (no formalizada):** cargo a `PROVEEDOR_*` / abono a `INGRESO_NO_FISCAL_*`
- **Movimiento de TC con CFDI:** cargo a `GASTO_*` / abono a `BANCO_TC_*`
- **Movimiento de TC sin CFDI (ticket simplificado):** cargo a `GASTO_*` (sin IVA acreditable o con IVA simplificado) / abono a `BANCO_TC_*`
- **Pago de estado de cuenta de TC:** cargo a `BANCO_TC_*` / abono a `BANCO_*` (cuenta operativa)
- **Pasivo de préstamo a empleado por viáticos:** cargo a `ANTICIPO_VIATICOS_EMPLEADO` / abono a `BANCO_*`
- **Comprobación de viáticos:** cargo a `GASTO_*` / abono a `ANTICIPO_VIATICOS_EMPLEADO`; diferencia a favor/contra a cuenta de empleados

### 11.4 Con FiscalAPI

Usos:

- Descarga masiva del SAT de CFDIs recibidos (todos los proveedores) y emitidos (para CXC)
- Validación de RFC contra LRFC al alta de proveedor
- Consulta de estado del CFDI (cancelado, vigente) — para detectar si un proveedor cancela un CFDI ya capturado

### 11.5 Con Facturación / Obras

CXP expone pasivos vinculados a número de Obra para que Obras los consolide junto con el lado cliente.

### 11.6 Con Inventario / Producción / Almacén

> **Cerrado en** [`docs/modulos/almacen/00-levantamiento.md`](../almacen/00-levantamiento.md)
> §5 (recepción, dos variantes), §8.4 (devolución a proveedor), §11.2
> (eventos publicados) y §11.3 (eventos suscritos). Lo que sigue resume
> los contratos canónicos desde la perspectiva de CXP.

Cuatro puntos de integración con el módulo Almacén:

- **Recepción de mercancía** vinculada a OC dispara la conciliación con la factura.
  - **Variante A (insumos con factura):** la factura suele llegar al ERP por descarga SAT / mailbox antes o junto con la mercancía. Almacén registra la recepción referenciando el `CfdiRecibido` (no crea `FacturaProveedor`). CxP captura la factura por su flujo normal y al hacerlo concilia automáticamente con la recepción vía OC.
  - **Variante B (materiales directos con packing list):** Almacén registra la recepción con flag `factura_pendiente=true`; la factura llega después por canal CxP. Al capturarla, CxP emite `FacturaProveedorRegistradaEvent` que Almacén suscribe para conciliar.
  - **Suscribe CxP:** `OcRecepcionRegistradaEvent { oc_id, lineas, cantidades, observaciones, merma?, factura_pendiente, cfdi_recibido_id? }`
  - **Publica CxP (variante B):** `FacturaProveedorRegistradaEvent { factura_id, oc_id, recepcion_id?, total, lineas, lineas_acumuladas_oc[] }`
    — el campo `lineas_acumuladas_oc[]` (PR #288, `{ linea_oc_id, cantidad_acumulada }`) habilita a Compras a actualizar `SubEstadoFacturacion` por línea sin proyección espejo. Almacén variante B sigue consumiendo `lineas[]` (ignora el campo nuevo).
- **Diferencia de precio en variante B.** Si el precio de la factura difiere del precio de la OC pero está dentro de tolerancia, CxP no rechaza pero sí publica diferencia para que Almacén ajuste costo de inventario remanente.
  - **Publica CxP:** `DiferenciaPrecioFacturaDetectadaEvent { recepcion_id, oc_id, factura_id, precio_oc, precio_factura, diferencia, por_linea[] }`
- **Devolución a proveedor (Almacén sub-flujo 8.B).** Almacén envía mercancía recibida al proveedor; CxP genera `NotaCargo` borrador a partir del evento.
  - **Suscribe CxP:** `OcDevolucionRegistradaEvent { proveedor_id, recepcion_origen_id, factura_origen_id?, motivo, lineas, evidencias }`
- **Sincronización de NC fiscal con devolución física.** Cuando el proveedor responde con NC fiscal (relación CFDI tipo 03), CxP la captura y notifica a Almacén para cerrar la devolución conciliada.
  - **Publica CxP:** `NotaCreditoFiscalDevolucionRecibidaEvent { proveedor_id, uuid_nc, devolucion_a_proveedor_id?, nota_cargo_id, monto }`

### 11.7 Con Recursos Humanos / Tesorería (préstamos a empleados)

CXP **no administra** préstamos a empleados (códigos `Axxxx`). El módulo dueño es por definir (RH o Tesorería). CXP solo:

- Referencia el saldo del préstamo si una factura aplica contra él
- Consulta para reportes de cartera consolidada

---

## 12. Reglas críticas

| Regla | Origen |
|---|---|
| La subcategoría es por proveedor, no por factura | Respuesta del área |
| El estado de revisión es flag independiente de la subcategoría | Análisis de catálogo definitivo |
| Tolerancia de conciliación factura vs. OC: **configurable por proveedor** | Anotación 10.1 |
| Sobre tolerancia: factura cancelada y devuelta a Compras, no se puede forzar | Respuesta del área |
| Encargado de compras viaja desde la OC como snapshot; en facturas directas queda vacío | Respuesta del área |
| Los pagos no son CXP. Tesorería los opera | Respuesta del área |
| El REPP recibido del proveedor lo registra Tesorería | Respuesta del área |
| La OC autorizada autoriza el pasivo Y el pago al vencimiento | Anotación 1.1 |
| Anticipos a proveedores: sin importe mínimo de autorización; siempre Dirección | Respuesta del área |
| El Auxiliar de CXP no gestiona la solicitud de anticipos; solo captura el CFDI cuando llega | Anotación 2.4 |
| El Comprador no captura facturas, solo OCs basadas en cotizaciones | Anotación 2.3 |
| El Auxiliar de CXP captura todas las facturas, con y sin OC | Anotación 2.4 |
| Toda autorización informal queda respaldada con evidencia digital + comentario obligatorio antes de avanzar el pasivo | Diseño propuesto, aceptado |
| Saldo amortizable de anticipo a proveedor = `Entregado − Amortizado` | Consistente con Facturación |
| Cuando un proveedor entra en revisión, todas sus facturas nuevas entran a revisión automáticamente | Inferido del reporte actual |
| Los viáticos son pasivo de préstamo al empleado. No generan CFDI de anticipo | Análisis fiscal + anot 10.3-4 |
| Movimientos de TC empresarial se consideran ya pagados al proveedor al registrarse | Anotación 11.2 |
| Cada CFDI de TC debe registrarse individualmente para gasto, IVA, DIOT | Anotación 3.2 |
| Las notas de cargo son documentos internos, no CFDI; pueden formalizarse fiscalmente con NC del proveedor | Análisis fiscal + anot 11.3 |
| Serie de anticipos del proveedor: **FANT** (no ANT) | Anotación 17.2 |
| Alta de proveedor: conjunta Compras + CXP, ambas validan | Anotación 6.2 |
| Adjuntos obligatorios al alta: CSF, contrato, acta, identificación, comprobante de domicilio | Anotación 6.1 |
| Migración SAP: no migrar proveedores sin movimientos en 2+ años | Anotación 5.1 |
| Préstamos a empleados (códigos Axxxx) NO son CXP | Decisión Eduardo + anot 1.2 |
| Pre-carga de campos de factura desde parsing del XML al recibirlo | Anotación 2.7-8 |
| Inmutabilidad: factura `Autorizada` no se puede modificar; cancelar y recapturar | Diseño propuesto |
| Optimistic concurrency en todas las entidades | ADR-0012 |
| Audit trail completo (capturó, modificó, autorizó + fechas) | ADR-0008 |
| Soft delete en entidades del módulo | Lineamiento de proyecto |
| Devolución de mercancía requiere registro de salida desde Almacén antes de aplicar NC al saldo | Anotación + cerrado en `almacen` §8.4 (sub-flujo 8.B) |

---

## 13. Decisiones pendientes

### 13.1 ~~Esperando respuesta del área~~ — Cerrados 2026-05-22

> **Cerrados con el owner el 2026-05-22.** Resumen de decisiones:

1. ✅ **Suplencia DG/DF: tercer nivel = Director General de otra área** (Operaciones / Comercial). Se configura en el catálogo de aprobadores con vigencia. RH mantiene la asignación. Aplicación en §8.3.
2. ✅ **Catálogo de aprobadores: por usuario + tipo de gasto + monto máximo.** Tabla `aprobadores_limites` con `(empleado_id, tipo_gasto, monto_max, vigencia_desde, vigencia_hasta)`. Granular para permitir que dos jefes del mismo puesto tengan límites distintos. Aplicación en §8 y modelo `4.AprobadorLimite`.
3. ✅ **Política de viáticos: tope por puesto + tipo de destino (nacional/internacional).** Tabla `politicas_viaticos` con `(puesto, tipo_destino, monto_max_dia, dias_max)`. Validación automática al solicitar anticipo; bloqueo si excede. Aplicación en §7.4.2.
4. ✅ **TC Empresariales: varias TC corporativas con titular fijo por tarjeta.** Modelo: 2–5 tarjetas, cada una con un titular único responsable. Catálogo `tarjetas_credito` local al módulo. Aplicación en §4.8 (entidad `Tarjeta`) y §7.4.3.
5. ✅ **NC pre-factura: operativamente raro, se modela como excepción.** Estado `EnEspera` con FK origen `null`; vinculación automática cuando llega la factura (match por proveedor + UUID de relación CFDI). Si el volumen crece a >5/mes se reabre como bandeja dedicada en v1.1. Aplicación en §7.3 y §4.4.
6. ✅ **Conciliación de TC vs banco: captura manual del estado de cuenta + carga de Excel/CSV exportado del portal del banco.** Parser intenta match por fecha+monto+merchant; movimientos no conciliados quedan en bandeja para revisión. Integración API del banco diferida a vNext (§13.2). Aplicación en §7.4.3 y §10.7.
7. ✅ **Catálogo de dependencias revisoras: compartido en Administración / DatosMaestros.** Tabla `datos_maestros.dependencias` (o `administracion.areas_organizacionales` — naming exacto se cierra con Eduardo). Reusable por Notificaciones, RH, Obras. Aplicación en §5.5.
8. ✅ **SLA: único de 5 días hábiles + excepción de 15 días para disputas contractuales.** Instrumentación permite diferenciar por motivo en v1.1 si los reportes muestran patrones útiles. Aplicación en §6.4.

### 13.2 Diferidas a fase posterior

9. **Portal propio de proveedores.** El área lo pidió tres veces. El modelo de `CfdiRecibido.canal_origen` ya lo contempla. Implementación post-MVP.
10. **Conciliación automática completa CFDI ↔ OC** más allá del match básico por proveedor + monto + folio.
11. **CRUD de catálogos administrables.** Vive en módulo de Administración cuando exista. En MVP, seeds desde catálogo aprobado.
12. **Reporte unificado de Obras** (cliente + proveedores). Vive en módulo de Obras. CXP expone los datos.
13. **Sincronización automática del estado de cuenta del banco (TC empresarial).** MVP arranca con captura/carga manual.

### 13.3 Validaciones técnicas

14. **Migración inicial del master de Proveedores desde SAP.** Cobertura crítica: RFC válido, subcategoría asignada, datos bancarios, días de pago, plazo, encargado de compras default, email REPP, email origen, tolerancia, adjuntos obligatorios. Sin estos, el módulo no puede operar al 100%. Proceso de captura previa al go-live para llenar lo que SAP no tenga, con criterio "no migrar sin movimientos 2+ años".
15. **Reglas de mapping del mailbox.** Match por RFC emisor del XML como llave primaria; email de origen es solo metadato. Resuelto en §9.2.
16. **Coordinación con módulo de Tesorería.** Definir interface de eventos antes de que Tesorería se construya. Resuelto a nivel funcional en §11.2; pendiente el contrato técnico (cierra en `01-diseno.md`).
17. **Workflow engine vs. máquina de estados ad-hoc.** Reusar matriz de Requisiciones extendida con `dependencia_revisora_id` para mantener consistencia. Aceptado por el área.
18. ~~**Modelo TC Empresarial.**~~ **Cerrado 2026-05-22** — anexo técnico dedicado en [`01a-anexo-tc-empresarial.md`](01a-anexo-tc-empresarial.md). Cubre modelo de dominio detallado, esquema PG, 5 flujos paso a paso, parser configurable por perfil de banco, algoritmo de conciliación, 8 casos especiales, RBAC, UX. Asunciones técnicas D9–D14 listadas pendientes de validación con área y Contabilidad.
19. ~~**Contratos de eventos con Almacén.**~~ **Cerrado 2026-05-22** — ver §11.6 con contratos canónicos (`OcRecepcionRegistradaEvent`, `FacturaProveedorRegistradaEvent`, `DiferenciaPrecioFacturaDetectadaEvent`, `OcDevolucionRegistradaEvent`, `NotaCreditoFiscalDevolucionRecibidaEvent`).

---

## 14. Dependencias de plataforma pendientes

> Sección obligatoria (ADR-0031). Tabla *Pieza · Ticket · NoOp en uso · Cómo se wirea*. Cada `PLATFORM-TODO` en el código del módulo CXP tendrá una fila aquí.

| Pieza | Ticket | NoOp en uso | Cómo se wirea |
|---|---|---|---|
| **Esquema Postgres `cuentas_por_pagar` + `CuentasPorPagarDbContext`** | — | n/a (se crea desde inicio) | Agregar a `MigrationsHealthCheckOptions.ContextTypes` y al bucle de migraciones en `deploy-app-dev.yml` (ver memoria [feedback_dbcontext_nuevo_checklist]). |
| **Outbox para eventos de integración CXP** | — | `NoOpIntegrationEventBusSender` si la cs de Service Bus está vacía | Reutilizar `OutboxSaveChangesInterceptor` + `OutboxPublisherWorker<CuentasPorPagarDbContext>` (mismo patrón que `OutboxPublisherWorker<ComprasDbContext>`). |
| **Cliente FiscalAPI (descarga SAT + LRFC + estado CFDI)** | — | Stub `IFiscalApiClient` que devuelve fixtures de CFDIs cuando la API key esté vacía | App registration / credentials al KV; rate-limiting; circuit breaker para descarga masiva. Ver ADR-0027. |
| **Worker `CfdiMailboxIngestionWorker`** | — | NoOp si la cs IMAP/Graph está vacía | `IHostedService` dentro de `Millet.Api` (mismo patrón que `OutboxPublisherWorker`); credenciales del mailbox al KV. |
| **Worker `CfdiDescargaMasivaSatWorker`** | — | NoOp si FiscalAPI no está configurado | `IHostedService` con schedule (nocturno + intra-día). |
| **Worker `CfdiEstadoSatRefreshWorker`** | — | NoOp si FiscalAPI no está configurado | `IHostedService` que refresca el estado de CFDIs capturados para detectar cancelaciones del proveedor. |
| **Tesorería: contraparte de eventos** | — | `NoOpTesoreriaEventConsumer` (CXP publica al Outbox; nadie consume todavía) | Cuando Tesorería se construya, suscribirse a `PasivoAutorizadoParaPagoEvent`; CXP suscribe `PagoFacturaProveedorEvent` / `PagoFacturaProveedorRevertidoEvent` / `ReppProveedorRecibidoEvent` / `CancelacionPasivoSolicitadaEvent`. |
| **Contabilidad: motor de asientos por `ConceptoContable`** | — | Stub `IContabilidadAsientoPort` que loggea sin persistir | Cuando exista el módulo Contabilidad, sustituir por adapter que consume eventos del Outbox. |
| **Almacén: adapter `IAlmacenEventConsumer` y publisher de eventos a Almacén** | — | `NoOpAlmacenEventConsumer` hasta que el módulo Almacén esté en runtime | Contratos canónicos cerrados en §11.6 (naming `{Agregado}{Verbo}Event` alineado con Compras-OC §8.6). Suscribir `OcRecepcionRegistradaEvent`, `OcDevolucionRegistradaEvent`; publicar `FacturaProveedorRegistradaEvent`, `DiferenciaPrecioFacturaDetectadaEvent`, `NotaCreditoFiscalDevolucionRecibidaEvent`. |
| **DatosMaestros: master de Proveedor con flag `en_revision` + tolerancia + adjuntos** | — | Compartido con `DatosMaestros` MVP (ver módulo Administración) | Extender entidad `Proveedor` de `DatosMaestros` con los atributos de §5.1 que CXP requiere. |
| **RBAC: permisos canónicos `cuentas_por_pagar.*`** | — | Registrar al arrancar el módulo | Permisos: `cuentas_por_pagar.facturas.read/write/autorizar`, `.notas_credito.*`, `.notas_cargo.*`, `.anticipos.*`, `.comprobaciones.*`, `.tc.*`, `.revision.liberar`, `.reportes.*`. Asignar a roles operativos. |
| **Notificaciones email para SLA de revisión** | — | NoOp si el módulo Notificaciones no tiene SMTP configurado | Reutilizar `INotificacionService` cuando exista (ADR-0026). |
| **Workflow engine vs. máquina de estados ad-hoc** | — | Máquina de estados ad-hoc dentro del agregado | Reusar la matriz de Requisiciones extendida con `dependencia_revisora_id` cuando se confirme. |
| **Generación de PDF para nota de cargo y reporte de cartera** | — | Stub que devuelve byte[] vacío | Cuando exista el servicio compartido de PDF (ADR-0025), sustituir por el adapter. |
| **Catálogo de aprobadores con límites (Caja Chica, Viáticos, TC)** | — | Stub `IAprobadorLimitePort` que siempre retorna "no excede" | Cuando llegue el catálogo del área (§13.1 punto 2), persistir como tabla `cuentas_por_pagar.aprobadores_limites` o en `DatosMaestros`. |

---

## 15. Riesgos

| Riesgo | Probabilidad | Impacto | Mitigación | Dueño |
|---|---|---|---|---|
| **Calidad del master de Proveedores migrado desde SAP.** Faltan datos críticos (cuenta bancaria, tolerancia, adjuntos) en muchos registros → bloqueo operativo al go-live. | Alta | Alto | Proceso de captura previa con catálogo "campo × proveedor" en Excel, validación CXP por proveedor, criterio "no migrar sin movimiento 2+ años". | Owner CXP + Compras |
| **FiscalAPI inestable o rate-limited.** La descarga masiva del SAT falla o devuelve datos incompletos → bandeja de CFDIs incompleta. | Media | Medio | Circuit breaker, retry exponencial, monitoreo de gaps por día, fallback al mailbox. | Owner técnico |
| **Mailbox dedicado se llena o pierde correos.** Proveedores envían a buzones equivocados, adjuntos no parseables, spam. | Media | Medio | Filtros por dominio del proveedor reconocido, alerta a CXP cuando llega XML no parseable, retención larga del mailbox. | Owner técnico |
| **Cancelación de CFDI por el proveedor después de capturado.** Factura ya autorizada en CXP queda inválida fiscalmente. | Media | Alto | Worker `CfdiEstadoSatRefreshWorker` que detecta cancelaciones y mueve la factura a `Cancelada` con bitácora. | Owner técnico |
| **Doble registro de un mismo CFDI por dos canales.** Mailbox + descarga SAT → pasivo duplicado. | Media | Alto | Unicidad por UUID con `Duplicado` marcado al segundo (§9.4). Reporte de duplicados. | Owner técnico |
| **Acoplamiento síncrono con Tesorería.** Si CXP llamara síncronamente a Tesorería, una caída de Tesorería bloquearía CXP. | Media | Alto | Modelo de eventos con Outbox (§11.2). Validar que ningún flujo de CXP haga llamada síncrona. | Owner técnico |
| **Coordinación CXP ↔ Almacén ↔ Compras en three-way match.** Devoluciones donde la NC fiscal y la salida física se desincronizan → saldos incorrectos. | Media | Alto | Contratos cerrados en §11.6; correlación bidireccional explícita vía `devolucion_a_proveedor_id` en `OcDevolucionRegistradaEvent` y `NotaCreditoFiscalDevolucionRecibidaEvent`. Pruebas de integración cross-módulo en PR 7 de Almacén. | Owner CXP + Almacén |
| **TC empresarial: discrepancia movimientos vs. estado de cuenta del banco.** Movimientos no capturados al cierre, cargos fantasma, fraude. | Media | Medio | Bandeja de conciliación (§10.6, §10.7), validación de suma vs. total del estado de cuenta antes de cerrar. | Owner CXP |
| **Política de viáticos no definida.** El módulo de viáticos electrónico se construye sin reglas claras de monto por puesto. | Media | Medio | Bloquear go-live del flujo de viáticos hasta que §13.1 punto 3 esté cerrado. | Owner área |
| **Adopción del flujo electrónico de viáticos por empleados.** El cambio de papel a pantalla puede generar resistencia. | Media | Bajo | UX clara en español, capacitación previa, posibilidad de "captura asistida" por CXP en transición. | Owner UX |
| **Volumetría de CFDIs recibidos.** Si la descarga SAT trae miles de CFDIs históricos al primer corrido, la bandeja se desborda. | Media | Bajo | Fecha de corte para la descarga inicial, paginación, política de archivado para `CfdiRecibido` antiguos no convertidos. | Owner técnico |

---

## 16. Glosario

- **CFDI 4.0** — Comprobante Fiscal Digital por Internet, versión 4.0 del SAT. Es el documento fiscal estándar en México (XML + sellos SAT).
- **DIOT** — Declaración Informativa de Operaciones con Terceros. Reporte mensual al SAT que detalla los proveedores con los que se tuvo operaciones. Cada CFDI debe poder reportarse individualmente.
- **REPP** — Recibo Electrónico de Pago (Complemento de Pago). CFDI tipo Pago que el proveedor emite cuando se le paga la factura.
- **LRFC** — Listado de RFCs del SAT. Servicio que valida que un RFC esté vigente y bien estructurado.
- **PAC** — Proveedor Autorizado de Certificación. Tercero que conecta al SAT para timbrar/descargar CFDIs. En este proyecto: FiscalAPI (ADR-0027).
- **FiscalAPI** — el PAC seleccionado. Ofrece timbrado, descarga masiva del SAT, validación de RFC, consulta de estado de CFDI.
- **OC / Orden de Compra** — documento del módulo Compras que compromete una compra a un proveedor. Ver [`docs/modulos/compras-ordenes-compra/`](../compras-ordenes-compra/).
- **Pasivo** — saldo a favor del proveedor que tenemos pendiente de pagar.
- **Anticipo a proveedor** — pago hecho al proveedor antes de recibir la factura final. Genera CFDI de anticipo (serie FANT en el caso del proveedor), saldo amortizable.
- **Nota de crédito (NC) del proveedor** — CFDI Egreso que el proveedor emite a nuestro favor (descuento, devolución, amortización de anticipo). Reduce nuestro pasivo.
- **Nota de cargo** — documento interno que nosotros emitimos al proveedor reclamando un monto. No es CFDI; se formaliza fiscalmente cuando el proveedor responde con NC.
- **Tolerancia** — margen permitido en la conciliación factura vs. OC. Hasta v0.1 era global ($0.99 MXP); a partir de v0.2 es por proveedor.
- **Redondeo** — campo de la factura donde se aplica la diferencia tolerada vs. la OC.
- **Three-way match** — conciliación clásica de CxP: OC + Recepción + Factura. La triada que se cierra cuando Almacén esté levantado.
- **Tesorería** — módulo externo a CXP (por construir) que opera los pagos.
- **Comprobación de gastos** — entidad agrupadora para flujos sin OC que consolidan múltiples CFDIs (Caja Chica, Viáticos, TC Empresarial).
- **TC Empresarial** — Tarjeta de Crédito corporativa (Amex u otra) usada para pagar gastos a múltiples proveedores. El pasivo agregado es con el banco emisor, no con cada proveedor.
- **FANT** — serie de los CFDIs de anticipo emitidos por el proveedor. Corrección importante respecto a v0.1 (que decía ANT, que es la serie del lado cliente / Facturación).
- **OneFacture** — la herramienta actual de descarga manual de CFDIs. Se reemplaza por FiscalAPI + mailbox.
- **Mailbox** — buzón de correo dedicado (ej. `cfdi@millet.com.mx`) donde los proveedores envían sus CFDIs.
- **Portal propio** — alcance reservado post-MVP: web pública del ERP donde el proveedor sube sus CFDIs interactivamente.
- **OTR** — Orden de Transporte. Documento del flujo de exportación que da origen a la OC marco con la agencia aduanal.
- **Pedimento** — documento aduanero que identifica una operación de comercio exterior.
- **Préstamo a empleado (códigos Axxxx)** — pasivo a empleados que vive fuera de CXP. CXP solo lo referencia.
- **Subcategoría** — taxonomía de gasto del proveedor (Materia Prima, Insumos, Gastos administrativos, etc.). Catálogo §5.2.
- **En revisión** — flag operativo que bloquea facturas hasta resolver observación. Independiente de la subcategoría.

---

## Rev.

- **2026-05-22 — v0.4** — Anexo técnico de TC Empresarial creado en [`01a-anexo-tc-empresarial.md`](01a-anexo-tc-empresarial.md), cerrando §13.3 punto 18.
- **2026-05-22 — v0.3** — Cierre de los 8 puntos de §13.1 con el owner: tercer nivel de suplencia (DG-otra-área), catálogo `aprobadores_limites` por usuario, política de viáticos por puesto+destino, varias TC corporativas con titular fijo, NC pre-factura como excepción `EnEspera` con worker periódico, conciliación TC por carga Excel/CSV del banco, dependencias revisoras en Administración/`DatosMaestros`, SLA único 5d con excepción 15d para disputas. Aplicación en §5.5, §6.4, §7.3, §7.4.2, §7.4.3, §8.1, §8.3, §4.8, §4.9.
- **2026-05-22 — v0.2.2** — Alineación de naming canónica con Compras-OC §8.6 (`{Agregado}{Verbo}Event`): renombrados todos los eventos en §11.6, §14 y §15. Confirmado que `IAlmacenEventConsumer` no requiere cambio estructural — solo nombres.
- **2026-05-22 — v0.2.1** — Cierre cross-módulo con Almacén v0.1: §11.6 reescrita con contratos canónicos (`OcRecepcionRegistradaEvent`, `FacturaProveedorRegistradaEvent`, `DiferenciaPrecioFacturaDetectadaEvent`, `OcDevolucionRegistradaEvent`, `NotaCreditoFiscalDevolucionRecibidaEvent`); pendientes 13.3 actualizados; tabla de dependencias §14 y de riesgos §15 actualizadas.
- **2026-05-22 — v0.2** — Mapa funcional integrando 46 anotaciones del área sobre v0.1. Pendientes principales: contratos de eventos con Almacén, 8 puntos del área (§13.1), modelo TC empresarial.
- **2026-05-XX — v0.1** — Borrador inicial (no entró al repo; se sustituye por v0.2).
