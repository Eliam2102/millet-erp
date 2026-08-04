# Levantamiento — Módulo Almacén (`Millet.Almacen`)

> **Proyecto:** ERP Millet — Módulo Almacén
> **Versión:** 0.1 — Borrador para revisión
> **Fecha:** 2026-05-22
>
> **Origen:** mapa funcional construido a partir del levantamiento entregado
> por Carlos Burgos (Jefe Almacén), sus aclaraciones por correo, las 9
> capturas SCR-001…SCR-009 + 3 capturas nativas adicionales del DOCX, y
> el análisis técnico del proyecto. Las áreas escuetas del levantamiento
> de Carlos se resuelven como **decisiones internas del proyecto**
> (idealmente mejorando el proceso vigente) — están marcadas como
> `[Decisión interna]` en cada sección.
>
> **Estado:** validado contra entrevista con Carlos Burgos y los artefactos
> AS-IS del Portal Millet + SAP B1. Decisiones pendientes con otros
> módulos listadas en §13. **Cierra los puntos abiertos en el
> `[Pendiente — Almacén]` del módulo
> [`cuentas-por-pagar/00-levantamiento.md`](../cuentas-por-pagar/00-levantamiento.md).**
>
> **Patrón:** sigue los exemplares de
> [`docs/modulos/compras-ordenes-compra/00-levantamiento-mapa-funcional.md`](../compras-ordenes-compra/00-levantamiento-mapa-funcional.md)
> y [`docs/modulos/cuentas-por-pagar/00-levantamiento.md`](../cuentas-por-pagar/00-levantamiento.md).
> Hereda decisiones transversales del proyecto: hexagonal + CQRS,
> multi-DbContext por ADR-0030, Outbox por ADR-0009, Idempotency-Key por
> ADR-0020, versionado `/api/v1/` por ADR-0021, RBAC granular por
> ADR-0007, Problem Details por ADR-0010, ETag por ADR-0012,
> PLATFORM-TODO por ADR-0031.

---

## 0. Cómo leer este documento

- `[Verificado]` — leído directamente del sistema actual (SAP B1, Portal Millet `192.168.1.38/PortalSap/`) o confirmado por Carlos.
- `[Inferido]` — deducido de capturas o documentación parcial; no confirmado con Carlos.
- `[Decisión interna]` — punto que Carlos no detalló o detalló escueto, y que el proyecto resuelve sin volver a pedir información al área.
- `[Pendiente — área]` — respuesta de Carlos aún por validar; ver §13.
- `[Pendiente — Finanzas]` o `[Pendiente — otro módulo]` — depende de definición de otro responsable.

---

## Aclaración de scope respecto al CLAUDE.md

El `CLAUDE.md` del proyecto describe este módulo como "Almacén de no-producción — inventario de consumibles e insumos indirectos (no incluye inventario productivo, eso lo lleva A+W)". La aclaración de Carlos durante el levantamiento **amplía el scope**:

- **Sí entran** los materiales directos de producción no-vidrio: interlayer, silicones, sellantes, pinturas (todo lo que hoy se gestiona en SAP bajo responsabilidad del Jefe Almacén).
- **Solo el vidrio crudo** queda fuera (continúa en A+W, consistente con Strangler Fig).
- El nombre del módulo es **`Almacen`** (no `Almacen-no-prod` como aparece en el módulo Administración — ver inconsistencia documentada al cierre).

> **Nota:** el módulo Administración tiene una entidad `Almacen` mínima (MVP-light en `DatosMaestros/Almacen` con su DbContext propio) que se [Diferido] re-mover a este módulo cuando exista. Este levantamiento sustituye al placeholder.

---

## 1. Propósito y alcance

### Qué hace este módulo

Administra el ciclo de vida del inventario físico bajo responsabilidad del Jefe Almacén: recepción de mercancía contra documentos de proveedor (factura o packing list), surtido de materiales contra requisiciones aprobadas, inventarios físicos con conciliación de variaciones, devoluciones del solicitante al almacén, devoluciones al proveedor, ajustes y traspasos internos entre sub-almacenes. Es la fuente única de verdad sobre la existencia y ubicación de los materiales que la empresa tiene en su poder.

### Qué cubre

Todo lo que hoy se gestiona en SAP B1 bajo responsabilidad de Almacén:

| Sub-almacén | Materiales | Volumen actual |
|---|---|---|
| **Insumos / refacciones** | Insumos generales, refacciones, materiales de mantenimiento | 40 facturas/día entrada, 120 salidas/día |
| **Materiales directos de producción** | Interlayer, silicones, sellantes, pinturas y demás componentes del vidrio laminado e insulado | 7 facturas/semana entrada, 3 salidas/día |

### Qué NO hace

- **No gestiona vidrio crudo.** El vidrio sigue en A+W y queda fuera del proyecto Millet ERP en esta fase.
- **No implementa consumo automático contra orden de producción, BOM ni explosión de materiales.** Aunque los materiales directos son conceptualmente MP, se gestionan con el mismo modelo funcional que los insumos: entrada por documento de proveedor, salida por requisición.
- **No emite Requisiciones de Compra.** Cuando el almacén detecta desabasto, crea una requisición que vive en el módulo Requisiciones (ya implementado).
- **No emite Órdenes de Compra ni gestiona proveedores.** Esos artefactos viven en el módulo Compras.
- **No registra facturas para pago.** Las facturas se reciben en el módulo CxP. Almacén consume el evento de factura como insumo del flujo de recepción variante A, pero no captura la factura ni gestiona el pasivo.
- **No autoriza entradas ni salidas.** La autorización vive aguas arriba (Requisiciones para salidas, Compras para entradas). Ver §10.4.
- **No incluye operación móvil con códigos de barras y escáner.** `[Diferido]` a iteración posterior.
- **No incluye gestión formal de bins/ubicaciones físicas** dentro del sub-almacén. `[Diferido]` a vNext.

### Boundary con otros módulos

| Módulo | Qué le pide Almacén | Qué le entrega Almacén |
|---|---|---|
| **Compras** (OC + Requisiciones) | OCs autorizadas y abiertas (para conciliar recepción); RQs aprobadas (para conciliar salida); RQ de compra por desabasto entra al flujo de Requisiciones | Evento `OcRecepcionRegistradaEvent` (actualiza sub-estado Recepcion de OC); evento `SalidaRequisicionRegistradaEvent` (cierra RQ de salida) |
| **CxP** | `FacturaProveedorRegistradaEvent` (variante B: factura llega después por canal CxP); `DiferenciaPrecioFacturaDetectadaEvent`; `NotaCreditoFiscalDevolucionRecibidaEvent` (cierra devolución 8.B) | `OcRecepcionRegistradaEvent` con datos de factura (variante A) o flag `factura_pendiente=true` (variante B); `OcDevolucionRegistradaEvent` (sub-flujo 8.B genera `NotaCargo` en CxP) |
| **Contabilidad** | Mapeo `ConceptoContable` → `CuentaContable` | Eventos `SalidaRequisicionRegistradaEvent`, `DevolucionInternaAplicadaEvent`, `AjusteInventarioAplicadoEvent`, `EntradaValorada` (asentar consumo y valuación) |
| **Administración / DatosMaestros** | Master de Artículos (catálogo de materiales), Sucursales, Empresas, Empleados; catálogo de Almacenes/Sub-almacenes (Almacén es dueño funcional) | Alta de Almacenes/Sub-almacenes (CRUD operado desde este módulo) |
| **Tesorería** | Tipo de cambio diario | — |
| **Finanzas** | Calendario fiscal (periodos abiertos/cerrados); umbrales monetarios para autorización de ajustes | Reporte de cierre mensual; eventos de valuación |
| **Producción / Mantenimiento** | Catálogo de Máquinas (referencia opcional en salidas) | — |
| **RH** | Catálogo de Empleados / Personas (destinatarios de salidas) | — |
| **Identidad** | Roles y permisos canónicos `almacen.*` | — |
| **A+W** (sistema externo) | — (vidrio fuera de alcance; sin integración directa con A+W desde Almacén) | — |

---

## 2. Actores y roles

| Rol | Responsabilidades en el módulo | Notas |
|---|---|---|
| **Jefe Almacén** | Aprueba ajustes mayores por inventario físico, autoriza requisiciones de compra que exceden umbrales, supervisa cierre de mes, ejecuta cierre del periodo del módulo. Comparte licencia operativa de SAP con Almacenista de materiales directos. | Carlos Burgos en la operación actual. |
| **Supervisor de Insumos** | Captura entradas (facturas) de insumos a tiempo completo. Aprueba requisiciones de compra de monto medio. | Licencia SAP permanente. |
| **Almacenista de Insumos** | Surte salidas a producción y mantenimiento, registra el surtido en sistema, recibe firma del solicitante. Crea requisiciones de compra por desabasto. | Licencia SAP permanente. |
| **Almacenista de materiales directos** | Captura entradas semanales de materiales directos (con packing list), surte salidas diarias. | Comparte licencia con Jefe Almacén. |
| **Coordinador de área solicitante** | Firma físicamente la requisición que llega al almacén autorizando la salida. | Externo al módulo (vive en módulo Requisiciones). |
| **Solicitante / Receptor** | Persona que físicamente recoge el material y firma la responsiva. Puede ser de Producción, Mantenimiento o áreas administrativas. | Catálogo de Empleados de RH (master en `DatosMaestros`). |
| **Auditor / Despacho externo** | Acceso de solo lectura para conteos anuales. | Acceso temporal; configurable. |

---

## 3. Estructura física: almacenes y sub-almacenes

La estructura jerárquica observada en SAP (Informe de auditoría de stock) es:

```
Localidad / Sucursal
   └── Almacén
         └── Sub-almacén
               └── (Ubicación / Bin — vNext, no en MVP)
```

Ejemplos observados en SAP actual `[Verificado]`:

| Localidad | Almacén | Sub-almacén |
|---|---|---|
| REV-COT (Cotolengo) | COTOLENGO-REV-1 | (varios) |
| 02 CIRCUITO | ACC-CIR | ALMACEN MAT Y SUM CIRCUI |
| 02 CIRCUITO | ACI | (otros) |

**Para el MVP:**

- El modelo de datos soporta los **tres niveles jerárquicos** (Sucursal → Almacén → Sub-almacén).
- El cuarto nivel (ubicación física / bin) **no** se modela en MVP. Las pantallas de movimiento permiten un campo opcional de texto libre "ubicación de referencia" para facilitar el picking físico, pero sin catálogo formal ni validación.
- Cada material tiene un **sub-almacén default** configurable en el catálogo de materiales; los movimientos se prellenan con ese default pero pueden modificarse al capturar.

> **`[Pendiente — área]`:** confirmar la lista exhaustiva de sucursales, almacenes y sub-almacenes operativos hoy en el universo de insumos + materiales directos. Validable directamente en SAP sin requerir a Carlos.

---

## 4. Submódulos del módulo

El módulo Almacén se compone de **cinco submódulos** funcionales:

1. **Entradas (Recepción)** — registra la llegada de mercancía contra OC, con dos variantes según el documento que la dispara.
2. **Salidas (Surtido)** — registra el surtido de mercancía contra requisición aprobada, con dos variantes (normal y por vale).
3. **Inventario físico** — toma de inventario rotativo y anual, conciliación de variaciones, aprobación de ajustes.
4. **Devoluciones** — registra el regreso de material previamente surtido al almacén **y** la salida de material hacia el proveedor por devolución (incorporado tras alinear con CxP §7.7).
5. **Reportes** — submódulo de visualización de los reportes operativos y de cierre del área.

Las secciones 5 a 9 detallan cada uno. La §10 cubre reglas transversales que aplican a todos.

---

## 5. Submódulo Entradas (Recepción)

### 5.1 Propósito

Registrar la llegada física de materiales al almacén, validando contra la Orden de Compra correspondiente y actualizando inventario y costos.

### 5.2 Dos variantes según documento disparador

| Variante | Documento que dispara la entrada | Materiales | Flujo |
|---|---|---|---|
| **A — Recepción con factura** | Factura del proveedor (CFDI) | Insumos, refacciones | El proveedor llega con material + factura impresa. El Supervisor de Insumos verifica contra OC, sella la factura de recibido y registra entrada en sistema. La factura ya fue (o será) capturada por CxP por el canal normal (mailbox / descarga SAT). |
| **B — Recepción con packing list** | Packing list del proveedor (con OC referenciada) | Materiales directos (interlayer, silicones, pinturas, sellantes) | El proveedor llega con material + packing list. El Almacenista verifica contra OC y registra entrada. La factura llega después por canal CxP. |

### 5.3 Ciclo de vida de una recepción

```
Borrador → Validada → Registrada (firme) → [Conciliada con factura, solo variante B]
                  ↘ Cancelada (solo en estado Borrador)
```

- **Borrador:** captura en curso, no afecta inventario.
- **Validada:** se han confirmado cantidades y referencia a OC; afecta inventario provisional. Permite ajustes menores.
- **Registrada (firme):** inmutable. Afecta inventario y costos en firme. Genera evento `OcRecepcionRegistradaEvent`.
- **Conciliada con factura:** aplica solo a variante B. Cuando CxP captura la factura del proveedor referenciando la recepción, esta transiciona a "conciliada".

### 5.4 Flujo paso a paso — Variante A (con factura)

1. Llega el proveedor con material + factura impresa.
2. El Supervisor de Insumos identifica la OC referenciada en la factura. Si no viene referenciada → reporta a Compras para que la consiga antes de continuar.
3. Verifica físicamente: cantidad recibida vs cantidad solicitada en OC.
4. Sella la factura de "recibido" físicamente.
5. Captura recepción en sistema:
   - Folio OC, líneas de OC, cantidades recibidas, lote/fecha si aplica.
   - **Referencia al `CfdiRecibido` de CxP** (si el CFDI ya está en el repositorio del ERP por el canal de CxP — descarga SAT, mailbox o carga manual). Si no está aún, queda referenciado por UUID + folio fiscal y CxP lo enlazará cuando lo procese.
   - **El vínculo fiscal es obligatorio** (decisión 2026-07-14): la recepción variante A no se registra sin CFDI. Tres rutas en la captura: (1) seleccionar el `CfdiRecibido` de la bandeja filtrado por RFC del proveedor de la OC; (2) cargar el XML ahí mismo (reutiliza `POST /cfdis/cargar` de CxP; requiere permiso `cuentas_por_pagar.cfdis.cargar-manual`); (3) capturar el folio fiscal (UUID, viene impreso en toda representación impresa) — columna `cfdi_uuid_fiscal`, normalizado a mayúsculas. El PDF sigue siendo opcional (el XML es el documento fiscal).
   - Adjunta foto/escaneo de la factura sellada (acuse interno).
6. Valida y registra en firme.
7. El sistema emite evento `OcRecepcionRegistradaEvent` que CxP consume para conciliar con la factura.

> **Importante:** Almacén **no crea** el `FacturaProveedor` en CxP. Solo registra la recepción con referencia al CFDI. CxP procesa la factura por su flujo normal (bandeja de `CfdiRecibido`) y al capturarla, la concilia con la recepción vía la referencia OC.

### 5.5 Flujo paso a paso — Variante B (con packing list)

1. Llega el proveedor con material + packing list (con OC referenciada).
2. El Almacenista verifica físicamente cantidad recibida vs OC.
3. Captura recepción en sistema:
   - Folio OC, líneas, cantidades recibidas.
   - Datos del packing list (folio del proveedor, fecha).
   - Adjunta PDF del packing list.
4. Valida y registra en firme.
5. El sistema emite evento `OcRecepcionRegistradaEvent` con flag `factura_pendiente=true`.
6. Cuando CxP recibe posteriormente la factura del proveedor, hace el match contra esta recepción y dispara conciliación. Ver §5.6 sobre diferencia de precio.

### 5.6 Reglas de validación

- **OC obligatoria.** Toda recepción debe referenciar una OC en estado válido (no draft, no cancelada). Materiales no codificados o llegadas sin OC se rechazan; quedan en bandeja "pendiente OC" sin afectar inventario hasta que Compras genere la OC retroactiva.
- **Recepción parcial permitida** por línea de OC. La OC se cierra automáticamente cuando todas las líneas alcanzan su cantidad solicitada (con tolerancia configurable por línea — ver §10.7 sobre el modelo de tolerancias consolidado).
- **Sobrecantidad y faltante:** se permite recepción con variación si está dentro de la tolerancia configurada en el material o la OC; fuera de tolerancia requiere autorización del Supervisor de Insumos (insumos) o del Jefe Almacén (materiales directos). La autorización queda registrada con justificación.
- **Precio de la entrada (fuente de verdad):**
  - **Variante A:** precio de la **OC** se usa para valuar el inventario. La factura (capturada por CxP) ya pasó por la tolerancia de proveedor en CxP; si CxP la rechaza por tolerancia, **el inventario YA está valorado al precio de OC y no se ajusta** (la diferencia se resuelve aguas arriba con la corrección de OC desde Compras).
  - **Variante B:** precio de la **OC** se usa para valuar el inventario al recibir. Si al llegar la factura por CxP el precio difiere dentro de tolerancia → CxP aplica `redondeo` y el inventario **no se ajusta**. Si la diferencia es relevante → emite evento `DiferenciaPrecioFacturaDetectada(recepcion_id, precio_oc, precio_factura)` que Almacén suscribe para ajustar costo del inventario remanente o registrar en cuenta de variación de precios (decisión con Contabilidad).
- **Conversión de unidades de medida** automática usando el catálogo de UM del material.
- **Tipo de cambio:** para entradas en moneda extranjera, el TC se toma del catálogo de Tesorería a la fecha de recepción.

### 5.7 Salida del submódulo (qué produce)

- Movimiento de inventario tipo `EntradaCompra` con vínculo a OC y, opcionalmente, a `CfdiRecibido` / packing list.
- Evento `OcRecepcionRegistradaEvent` consumido por CxP y por Compras (sub-estado OC).
- Actualización del costo promedio ponderado del material (§10.5).
- PDF de comprobante de recepción (acuse interno).

---

## 6. Submódulo Salidas (Surtido)

### 6.1 Propósito

Registrar la entrega de materiales del almacén al área solicitante, dejando trazabilidad de qué se entregó, a quién, contra qué documento y para qué uso.

### 6.2 Dos variantes según documento disparador

| Variante | Documento que dispara la salida | Cuándo se usa |
|---|---|---|
| **A — Salida normal** | Requisición aprobada (firmada por Coordinador del área solicitante en módulo Requisiciones) | Flujo estándar para cualquier solicitud planificada de Producción, Mantenimiento o áreas administrativas. |
| **B — Salida por vale (urgente)** | Vale impreso con formato interno, firmado por Coordinador del área | Casos urgentes, principalmente de Mantenimiento, donde no hay tiempo para esperar la requisición en sistema. **Requiere regularización posterior con RQ.** |

### 6.3 Ciclo de vida de una salida

```
Borrador → Validada → Surtida (firme) → [Regularizada, solo variante B]
                                      ↘ Devuelta (vía submódulo Devoluciones)
                  ↘ Cancelada (solo en estado Borrador)
```

- **Borrador:** captura en curso.
- **Validada:** cantidades confirmadas contra stock; reserva temporal.
- **Surtida (firme):** inmutable. Afecta inventario en firme. Si es variante B, queda con flag `pendiente_regularizacion=true`.
- **Regularizada:** aplica solo a variante B; cuando la RQ se crea a posteriori y se referencia, el flag se limpia.

### 6.4 Flujo paso a paso — Variante A (salida normal)

1. El usuario del área solicitante crea una RQ en el módulo Requisiciones; el Coordinador la firma (autoriza en sistema).
2. El usuario imprime la RQ y la lleva físicamente al almacén.
3. El Almacenista verifica el folio de RQ en sistema:
   - RQ existe y está aprobada.
   - Coincide con el papel firmado.
4. Busca físicamente los materiales y valida cantidad disponible en el sub-almacén correspondiente.
5. Si hay stock suficiente: surte total. Si hay stock parcial: surte parcial y la línea remanente se marca para reabastecimiento (puede disparar creación de RQ de compra; ver §6.7).
6. Captura la salida en sistema:
   - RQ referenciada, líneas surtidas, cantidad real entregada.
   - Persona destinataria (catálogo de Empleados de `DatosMaestros`).
   - Máquina o equipo de destino (opcional, catálogo).
   - Comentario libre (motivo, observación).
7. Imprime comprobante de salida.
8. Recibe firma física del solicitante en el comprobante (responsiva).
9. Archiva el comprobante firmado (físico) y digitaliza/adjunta al registro en sistema.

### 6.5 Flujo paso a paso — Variante B (salida por vale)

1. El solicitante del área (típicamente Mantenimiento) llega con un vale físico impreso firmado por su Coordinador.
2. El Almacenista valida físicamente el vale (firma del Coordinador, materiales solicitados).
3. Surte el material y registra la salida en sistema con tipo `Vale`:
   - Sin RQ referenciada (todavía).
   - Persona destinataria y comentario libre.
   - Adjunta foto/escaneo del vale firmado.
4. La salida queda registrada en firme con flag `pendiente_regularizacion=true` y un contador de tiempo.
5. Dentro de un plazo configurable (sugerido: 48 horas hábiles), el solicitante debe crear la RQ en el módulo Requisiciones referenciando este vale.
6. Cuando la RQ se aprueba, el flag se limpia automáticamente y la salida queda regularizada.

> **`[Decisión interna]`** modelar el vale como flujo de primera clase, no como excepción. Las reglas concretas a confirmar antes de implementar son: plazo máximo de regularización (sugerido 48 horas), si se bloquean nuevas salidas por vale al solicitante con vales no regularizados (sugerido sí), y qué pasa si nunca se regulariza (sugerido: escalamiento al Jefe Almacén tras X días).

### 6.6 Reglas de validación

- **Stock obligatorio.** No se puede surtir más cantidad que la disponible en el sub-almacén origen.
- **RQ válida** (variante A): la RQ debe estar aprobada en el módulo Requisiciones. RQ en cualquier otro estado se rechaza.
- **Una RQ puede surtirse en múltiples salidas** (surtido parcial); el sistema controla saldo restante por línea.
- **Una salida no puede mezclar materiales de distintos sub-almacenes** en el mismo folio; si se necesitan materiales de insumos y de materiales directos para la misma RQ, se generan dos folios de salida.

### 6.7 Caso de desabasto

Si al surtir una RQ no hay stock suficiente para una o más líneas:

1. La línea remanente se marca "pendiente de reabastecimiento" en la RQ.
2. El Almacenista crea una **Requisición de Compra** en el módulo Requisiciones (no en este módulo) por las cantidades faltantes.
3. Esa RQ de compra entra al flujo estándar de autorización del módulo Requisiciones (Almacenistas → Supervisor → Jefe Almacén según monto). Compras la convierte en OC.
4. Cuando llega la mercancía, vuelve al submódulo Entradas y, una vez recibida, se notifica al solicitante original que ya puede recoger el saldo de su RQ.

### 6.8 Salida del submódulo (qué produce)

- Movimiento de inventario tipo `SalidaConsumo` con vínculo a RQ y persona destinataria.
- Comprobante de salida en PDF (para firma del solicitante).
- Evento `SalidaRequisicionRegistradaEvent` consumido por Contabilidad para asentar el consumo en el centro de costo correspondiente.

---

## 7. Submódulo Inventario Físico

### 7.1 Propósito

Conciliar el inventario teórico (lo que el sistema dice que hay) con el inventario físico (lo que efectivamente está en el almacén), registrar variaciones y aplicar ajustes contables.

### 7.2 Dos tipos de inventario

| Tipo | Frecuencia | Quién lo ejecuta | Bloqueo de operaciones |
|---|---|---|---|
| **Rotativo** | Cada trimestre (configurable) | Personal interno del almacén | Sin bloqueo: el almacén opera normalmente |
| **Anual** | Una vez al año (fin de año) | Dirección + despacho externo | Bloqueo de salidas durante la ventana de conteo |

### 7.3 Ciclo de vida de un conteo

```
Planificado → En curso → En conciliación → Aprobado → Aplicado
                                       ↘ Rechazado (recuento)
```

- **Planificado:** el conteo está agendado pero no iniciado; snapshot de cantidades teóricas aún no tomado.
- **En curso:** snapshot tomado, captura de conteo en proceso. El inventario teórico congelado para este conteo no se afecta por operaciones del día; las cantidades teóricas que verá el aprobador son las del momento del snapshot.
- **En conciliación:** todas las líneas capturadas; se identifican variaciones; se calcula el valor monetario de la diferencia.
- **Aprobado:** los ajustes calculados están firmados por el nivel correspondiente.
- **Aplicado:** los movimientos de ajuste se han registrado en el sistema; inventario teórico ahora coincide con el conteo.

### 7.4 Flujo paso a paso

1. El Jefe Almacén crea un nuevo conteo definiendo: tipo (rotativo/anual), sub-almacén objetivo (todo, una familia, ABC-A — cuando exista, ver §12.2), responsable y fecha.
2. El sistema toma snapshot de cantidades teóricas al momento del inicio.
3. Genera lista de conteo (PDF imprimible o vista en pantalla) con: clave del material, descripción, sub-almacén, ubicación de referencia si existe. **No** se muestra la cantidad teórica al contador (§7.6).
4. El contador (uno o dos según política) captura las cantidades reales en pantalla o en papel para transcripción posterior.
5. Conforme se capturan líneas, el sistema calcula la variación contra el teórico (no visible para el contador, sí para el aprobador).
6. Líneas con variación superior al umbral (sugerido: >5% en cantidad o >$1,000 MXN en valor) se marcan para **recuento obligatorio** antes de proceder.
7. Una vez completado el conteo y los recuentos, el conteo pasa a estado "En conciliación".
8. El Jefe Almacén revisa las variaciones, agrega justificación por línea cuando aplica y propone los ajustes.
9. Aprobación por monto:
   - Variación pequeña (< $1,000 MXN por línea): aprueba Almacenista.
   - Variación mediana ($1,000 – $10,000 MXN): aprueba Supervisor de Insumos / Jefe Almacén.
   - Variación grande (> $10,000 MXN): aprueba Jefe Almacén con notificación a Finanzas.
   - Montos exactos a confirmar con Finanzas antes de implementar `[Pendiente — Finanzas]`.
10. Una vez aprobado, el conteo pasa a "Aplicado": se generan movimientos de ajuste positivo/negativo por línea.

> **`[Decisión interna]`** el flujo descrito en §7.4 representa **rediseño del proceso** respecto a la operación actual. Carlos respondió escuetamente sobre inventarios; el equipo del proyecto diseña el flujo mejorado sin pedir más detalle.

### 7.5 Reglas de validación

- **Conteo inmutable** una vez aprobado: no se reabre. Si después aparece un error, se hace un nuevo conteo correctivo.
- **Snapshot de teórico** se mantiene incluso si el almacén opera durante el conteo (rotativo): el sistema usa el snapshot del inicio, no el actual.
- **Anual con bloqueo:** durante un conteo anual, el módulo bloquea automáticamente la captura de salidas para los sub-almacenes incluidos. Las entradas se mantienen (no afectan el snapshot del conteo).
- **Auditor externo:** acceso de solo lectura habilitable temporalmente desde administración del módulo.

### 7.6 Captura sin sesgo

El contador no ve la cantidad teórica al capturar la cantidad real. Esto es una **decisión deliberada**: la práctica común en SAP de mostrar el teórico junto al campo de captura induce a "cuadrar" capturando lo que el sistema dice cuando hay duda. La captura ciega obliga a contar realmente; las diferencias aparecen claras al cerrar.

### 7.7 Salida del submódulo

- Movimientos de inventario tipo `AjusteInventarioPositivo` / `AjusteInventarioNegativo`.
- Documento de conteo firmado por los aprobadores.
- Evento `AjusteInventarioAplicadoEvent` para Contabilidad.

---

## 8. Submódulo Devoluciones

### 8.1 Propósito

Registrar el regreso de material y restituir el inventario. **Dos sub-flujos distintos:**

| Sub-flujo | Origen | Destino del material | Cruza con |
|---|---|---|---|
| **8.A — Devolución de salida (interna)** | Solicitante regresa material previamente surtido | Sub-almacén origen | — (no cruza con CxP) |
| **8.B — Devolución a proveedor (externa)** | Almacén envía material recibido del proveedor de vuelta al proveedor | Salida del almacén → camión del proveedor | **CxP** (genera `NotaCargo`; eventualmente se cruza con NC fiscal del proveedor) |

> **Alineación cross-módulo:** el sub-flujo 8.B se incorpora en este levantamiento para cerrar el `[Pendiente — Almacén]` que CxP §7.7 dejó abierto. El levantamiento original de Carlos solo describió 8.A.

### 8.2 Justificación del submódulo

En la operación actual, las devoluciones internas no se procesan en sistema: el material devuelto "se queda en resguardo" y se reasigna informalmente. Esto crea divergencia permanente entre inventario teórico y físico. Las devoluciones a proveedor tampoco tienen flujo formal (se pactan por correo o llamada). El nuevo módulo formaliza ambos flujos.

> **`[Decisión interna]`** formalizar las devoluciones como movimiento. Carlos confirmó que el volumen es bajo, pero la trazabilidad importa más que el volumen.

### 8.3 Sub-flujo 8.A — Devolución de salida (interna)

#### Ciclo de vida

```
Borrador → Validada → Aplicada (firme)
        ↘ Cancelada (solo en Borrador)
```

#### Flujo paso a paso

1. El solicitante regresa al almacén con material previamente surtido.
2. El Almacenista identifica la salida original (folio o búsqueda por persona/material/fecha).
3. Captura una devolución referenciando la salida original:
   - Líneas a devolver (subconjunto de las líneas de la salida original).
   - Cantidad devuelta (igual o menor a la surtida).
   - Estado del material (íntegro, usado parcialmente, dañado).
   - Motivo de devolución.
4. Valida y aplica.
5. El sistema regresa la cantidad al sub-almacén origen (al mismo sub-almacén de donde salió, salvo que se especifique otro destino).

#### Reglas

- **Solo se puede devolver contra una salida previa.** No se permiten devoluciones "huérfanas".
- **La cantidad devuelta no puede exceder la cantidad surtida** menos lo previamente devuelto (control de saldo).
- **Material dañado** se contabiliza en un sub-almacén especial "Material en revisión" o se marca para destrucción según política a definir con Calidad `[Pendiente — área]`.
- **Plazo de devolución:** configurable (sugerido: 30 días desde la salida). Después de ese plazo, la devolución requiere autorización del Supervisor.

#### Salida del sub-flujo

- Movimiento tipo `DevolucionSalida` con vínculo a la salida original.
- Restitución de inventario al sub-almacén origen.
- Evento `DevolucionInternaAplicadaEvent` para Contabilidad (reversa parcial del consumo).

### 8.4 Sub-flujo 8.B — Devolución a proveedor (externa)

#### Cuándo aplica

- Mercancía recibida con defecto o no conforme — detectada en recepción o después (durante uso).
- Sobrecantidad recibida que el proveedor pide devolver.
- Acuerdo comercial de devolución por descontinuación.

#### Ciclo de vida

```
Iniciada → Autorizada → Mercancía preparada → Entregada al proveedor → Aplicada (firme)
                                                                    ↘ Conciliada con NC fiscal (cuando llega)
        ↘ Cancelada
```

#### Flujo paso a paso

1. Quien detecta el problema (Almacenista, Calidad o el área usuaria) inicia una **Devolución a proveedor** en el sistema:
   - Proveedor (FK).
   - Recepción original (FK) y/o factura original (referencia).
   - Líneas a devolver, cantidades, motivo.
   - Adjuntos de evidencia (fotos, reportes de calidad).
2. La devolución requiere **autorización de Dirección** (mismo nivel que `NotaCargo`, ver `cuentas-por-pagar/01-diseno.md` §8.1). Solicitud de autorización se gestiona desde CxP (que ya tiene la pantalla de evidencias de autorización informal).
3. Una vez autorizada, el Almacenista prepara la mercancía físicamente.
4. Cuando se entrega al proveedor (camión, mensajería), el Almacenista registra la salida física con folio de remisión.
5. El sistema emite evento `OcDevolucionRegistradaEvent` con `proveedor_id`, `recepcion_origen_id?`, `factura_origen_id?`, `motivo`, `lineas`, `evidencias`.
6. CxP suscribe el evento y crea una `NotaCargo` borrador asociada a esta devolución.
7. **Si el proveedor responde con NC fiscal** (CFDI Egreso relación tipo 03):
   - CxP captura la NC por su flujo normal.
   - CxP emite evento `NotaCreditoFiscalDevolucionRecibida(proveedor_id, uuid_nc, devolucion_id?)` que Almacén suscribe para marcar la devolución como `Conciliada con NC fiscal`.
   - La NC se aplica al saldo del proveedor en CxP.
8. **Si el proveedor NO responde con NC fiscal**, la `NotaCargo` queda como ajuste interno sin formalización fiscal en CxP; la devolución física en Almacén queda como `Aplicada` con flag `sin_nc_fiscal`.

#### Reglas

- **Solo se puede devolver contra una recepción previa.** No se permiten devoluciones "huérfanas" hacia el proveedor.
- **La cantidad devuelta no puede exceder la cantidad recibida** menos lo previamente devuelto.
- **El inventario se descuenta del sub-almacén** al momento de la entrega al proveedor (no antes).
- **Costo de la devolución** se valora al **costo de la recepción original** (no al costo promedio actual), para preservar consistencia contable con CxP.
- **Si la factura ya fue capturada y autorizada** en CxP cuando se inicia la devolución, la `NotaCargo` se vincula a esa factura como factura origen.

#### Salida del sub-flujo

- Movimiento tipo `SalidaPorDevolucionAProveedor` con vínculo a recepción original y, opcionalmente, factura origen.
- Evento `OcDevolucionRegistradaEvent` → CxP genera `NotaCargo`.
- Cuando llega la NC fiscal: evento `NotaCreditoFiscalDevolucionRecibidaEvent` desde CxP → Almacén marca conciliada.
- PDF de remisión de devolución (firmado por el proveedor al recibir).

---

## 9. Submódulo Reportes

### 9.1 Decisiones de arquitectura

- El nuevo ERP **asume las tres funciones del Portal Millet** (Requisiciones, Salidas de mercancía, Reportes). El portal queda **deprecado** al cerrar este módulo.
- Los reportes se implementan con el **motor nativo del ERP**, no Crystal embebido. Los `.rpt` originales quedan como referencia funcional.
- **Decisión transversal cerrada** en [ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md): motor nativo basado en componentes React + endpoints JSON + exportación client-side (`@react-pdf/renderer` para PDF, `exceljs` para Excel). Aplica a este módulo y al resto del back-office.

### 9.2 Reportes en MVP (recompilar)

| # | Reporte | Uso |
|---|---|---|
| 1 | **ALFAK - HISTORIAL ALMACEN** | Cierres de mes. Consulta tablas SAP (no A+W, a pesar del nombre). |
| 2 | **SAP REPORTE DE EXISTENCIA ALMACEN MP-CNK** | Inventario diario de materiales directos (interlayer, silicones, pinturas). |

### 9.3 Reportes en vNext (diferidos a iteración posterior)

| # | Reporte |
|---|---|
| 3 | SAP-REPORTE DE MOVIMIENTOS ALMACEN |
| 4 | REPORTE DE REQUISICIONES |
| 5 | REPORTE INVENTARIO TODOS LOS ALMACENES RP |
| 6 | REPORTE DE REQUISICIONES SIMPLIFICADO |

### 9.4 Reportes descartados

- ALFAK - ENTRADAS Y FACTURAS
- RQ_REPORTE RQ_SAP RELACION DOCUMENTOS
- REPORTE MOV. INVENTARIO REQUISICIONES
- REPORTE INVENTARIO SAP POR CLAVE

### 9.5 Reportes operativos internos (no del Portal Millet)

El Almacenista genera diariamente:

- **Comprobante de salida** (por folio, para firma del solicitante).
- **Reporte de salidas del día** (consolidado de todas las salidas del turno).
- **Reporte de entradas del día** (consolidado de recepciones registradas).

Estos no son Crystal del portal; son pantallas/PDFs nativos del módulo Almacén.

---

## 10. Reglas transversales

### 10.1 Inmutabilidad de movimientos

Una vez que un movimiento de inventario pasa al estado "firme" (Recepción registrada, Salida surtida, Ajuste aplicado, Devolución aplicada), **no se edita**. El flujo correctivo es siempre por contramovimiento + nuevo movimiento, no por edición en sitio. Esto se alinea con ADR-0008 (auditoría) y ADR-0012 (concurrencia optimista).

Las cinco restricciones que la operación actual ya impone (no mover fechas, tipos de cambio, precios, cantidades, sub-almacén) se mantienen. Captura en ADR específico del módulo Almacén `[Pendiente — ADR]`.

### 10.2 Auditoría

Todo movimiento registra: usuario, fecha-hora, sub-almacén afectado, material, cantidad, costo, documento que lo dispara. Esto permite reconstruir el saldo de cualquier material en cualquier punto en el tiempo (ADR-0008).

### 10.3 Periodos contables

El módulo Almacén consume el calendario fiscal del módulo Finanzas. **No se permiten movimientos** con fecha en periodos cerrados. El cierre del periodo de Almacén lo dispara el Jefe Almacén (mensual), y a partir de ahí Finanzas cierra el periodo contable global.

### 10.4 Autorización

El módulo Almacén **no implementa flujo de autorización propio** para entradas ni salidas. Las autorizaciones viven aguas arriba:

| Operación | Documento que la origina | Donde se autoriza |
|---|---|---|
| Salida normal | Requisición aprobada | Módulo Requisiciones |
| Salida por vale | Vale físico firmado por Coordinador | Externo al sistema (vale impreso); regularización posterior en módulo Requisiciones |
| Entrada (recepción) | Orden de Compra aprobada | Módulo Compras |
| Requisición de compra por desabasto | RQ del propio Almacén | **Módulo Requisiciones** — autoriza Almacenistas, Supervisor de Insumos o Jefe Almacén según monto |
| **Devolución a proveedor (§8.4)** | Solicitud interna | **Dirección** (vía evidencia de autorización informal, mismo modelo que `NotaCargo` en CxP) |

Los únicos movimientos que requieren autorización dentro del módulo Almacén son **ajustes por inventario** (§7) y, en su caso, **traspasos internos** entre sub-almacenes. Para ambos, la política de autorización se basa en monto: pequeños los aprueba Almacenista, medianos Supervisor, grandes Jefe Almacén con notificación a Finanzas.

### 10.5 Valoración de inventario

**Método: costo promedio ponderado** (`[Pendiente — Finanzas]` para confirmar). Cada entrada actualiza el costo promedio del material en el sub-almacén afectado:

```
nuevo_costo_promedio = (cantidad_actual * costo_actual + cantidad_entrada * costo_entrada)
                       / (cantidad_actual + cantidad_entrada)
```

- **Salidas** se valoran al costo promedio al momento de la salida.
- **Ajustes positivos por inventario** se valoran al costo promedio vigente.
- **Ajustes negativos por inventario** se valoran al costo promedio vigente y se registran como pérdida en una cuenta de variación.
- **Devoluciones de salida (8.A)** se restituyen al costo de la salida original (no al promedio actual), para preservar consistencia contable.
- **Devoluciones a proveedor (8.B)** se descuentan al costo de la **recepción original**.

### 10.6 Tipo de cambio

Para movimientos en moneda extranjera, el TC se toma del catálogo de Tesorería:

- **Entradas:** TC del día de la recepción.
- **Salidas:** TC del día de la salida.
- **Cierre de mes:** se calcula valuación del inventario al TC de cierre y se ajusta diferencia cambiaria si aplica.

El servicio de TC es compartido (provisto por Tesorería). El módulo Almacén lo consume pero no lo mantiene.

### 10.7 Tolerancias — modelo consolidado

Para evitar dispersión, el módulo distingue **tres tolerancias** con propósitos diferenciados:

| Tolerancia | Aplica a | Dimensión | Configurada en | Quién la usa |
|---|---|---|---|---|
| **Tolerancia de cantidad por línea de OC** | Cierre automático de OC al completar recepción | Cantidad | Compras (al crear OC) | Almacén (en recepción) |
| **Tolerancia de cantidad por material** | Recepción con sobre/sub-cantidad | Cantidad | Catálogo de Materiales (`DatosMaestros`) | Almacén (en recepción) |
| **Tolerancia de monto por proveedor** | Conciliación factura vs OC en monto | Monto absoluto o % | Catálogo de Proveedores (`DatosMaestros`) | CxP (en captura de factura) |

Estas tres tolerancias son ortogonales (no se duplican). El proveedor X puede tener tolerancia de monto 1% para CxP, mientras que el material Y (de ese mismo proveedor) puede tener tolerancia de cantidad 5%.

### 10.8 Cierre de mes

El Jefe Almacén ejecuta el cierre del módulo Almacén el primer día hábil del mes siguiente:

1. Valida que todas las recepciones y salidas del mes están registradas en firme.
2. Ejecuta inventario rotativo de los sub-almacenes que tocan según calendario.
3. Genera el reporte ALFAK - HISTORIAL ALMACEN para Finanzas.
4. Cierra el periodo del módulo (los movimientos con fecha del mes cerrado se vuelven imposibles).
5. Notifica a Finanzas que el periodo de Almacén está cerrado.

---

## 11. Datos de entrada y salida (integraciones)

### 11.1 Entradas

| Información | Origen | Frecuencia |
|---|---|---|
| Órdenes de Compra | Módulo Compras (master) | Continuo |
| Requisiciones aprobadas | Módulo Requisiciones (master) | Continuo |
| Catálogo de Materiales | `DatosMaestros` (master) | Bajo demanda |
| Catálogo de Proveedores | `DatosMaestros` (master) | Bajo demanda |
| Catálogo de Personas (Empleados) | `DatosMaestros` (master, derivado de RH) | Bajo demanda |
| Catálogo de Centros de costo / Departamentos | Finanzas (master) | Bajo demanda |
| Catálogo de Proyectos | Por definir (probablemente Finanzas) `[Pendiente — área]` | Bajo demanda |
| Catálogo de Sucursales | `DatosMaestros` | Bajo demanda |
| Catálogo de Máquinas | Producción / Mantenimiento `[Pendiente — área]` | Bajo demanda |
| Tipo de cambio | Tesorería | Diaria |
| Calendario fiscal (periodos abiertos) | Finanzas | Mensual |
| Eventos de CxP (factura capturada, NC fiscal recibida, diferencia de precio) | CxP | Continuo |
| Eventos de Requisiciones (RQ aprobada) | Requisiciones | Continuo |
| Eventos de Compras (OC autorizada, OC cancelada) | Compras | Continuo |

### 11.2 Salidas (eventos publicados)

> **Convención de naming** (alineada con Compras-OC §8.5–8.6): `{Agregado}{Verbo}Event` con sufijo `Event` y prefijo del agregado de origen. Esta tabla refleja la convención canónica; ver [`compras-ordenes-compra/01-diseno.md`](../compras-ordenes-compra/01-diseno.md) §8.6 como referencia y CLAUDE.md "Triada Compras ↔ Almacén ↔ CxP".

| Evento | Destinos | Trigger |
|---|---|---|
| `OcRecepcionRegistradaEvent` | **CxP**, **Compras** | Recepción pasa a firme. Compras actualiza `CantidadRecibida` y `SubEstadoRecepcion`. CxP la usa para conciliar factura (variantes A y B). Payload incluye flag `factura_pendiente=true` para variante B (materiales directos). |
| `OcDevolucionRegistradaEvent` (sub-flujo 8.B) | **CxP**, **Compras** | Salida física hacia el proveedor aplicada. CxP genera `NotaCargo` borrador. Compras decrementa `CantidadRecibida`. Contrato ya previsto en Compras-OC §8.6 como "post-MVP del OC submódulo". |
| `SalidaRequisicionRegistradaEvent` | **Contabilidad**, **Requisiciones** | Salida normal (variante A) o por vale (variante B) pasa a firme. Contabilidad asienta consumo; Requisiciones actualiza saldo de RQ. |
| `DevolucionInternaAplicadaEvent` (sub-flujo 8.A) | **Contabilidad** | Devolución interna aplicada. Reversa parcial del consumo. |
| `AjusteInventarioAplicadoEvent` | **Contabilidad** | Ajustes positivos o negativos de inventario físico aplicados. |
| `EntradaInventarioValoradaEvent` | **Contabilidad** | Valuación de recepción (cargo a inventario, abono a contraparte). |

> **Nota sobre cierre de OC:** Almacén **no** emite un evento "OC cerrada por recepción completa". Compras-OC evalúa si la OC pasa a `Cerrada` combinando los 3 sub-estados (Recepción + Facturación + Pago) y emite `OrdenCompraCerradaEvent` cuando los 3 están completos. Almacén solo dispara `OcRecepcionRegistradaEvent`; el cierre es responsabilidad de Compras.

### 11.3 Eventos suscritos por Almacén

| Evento | Origen | Acción |
|---|---|---|
| `OrdenCompraAutorizadaEvent` | Compras | Habilita la OC para recepción (proyección local opcional). |
| `OrdenCompraCanceladaEvent` | Compras | Si hay recepciones en borrador asociadas, se notifica al Almacenista. |
| `OrdenCompraCerradaEvent` | Compras | Cierra el ciclo logístico de la OC (informativo; histórico). |
| `FacturaProveedorRegistradaEvent` | CxP (variante B) | Marca la recepción correspondiente como `ConciliadaConFactura` cuando llega la factura del proveedor por canal CxP. |
| `DiferenciaPrecioFacturaDetectadaEvent` | CxP | Ajusta costo del inventario remanente o registra en cuenta de variación (según política con Contabilidad). |
| `NotaCreditoFiscalDevolucionRecibidaEvent` | CxP | Marca la devolución a proveedor (sub-flujo 8.B) como `ConciliadaConNcFiscal`. |
| `RequisicionAprobadaEvent` | Requisiciones | Habilita la RQ para surtido. |
| `RequisicionCanceladaEvent` | Requisiciones | Si hay salidas en borrador, se notifica al Almacenista. |
| `PeriodoContableCerradoEvent` | Finanzas (futuro) | Bloquea operaciones con fecha del periodo cerrado. |

---

## 12. Decisiones de diseño tomadas y pendientes

### 12.1 Decisiones tomadas

| # | Decisión | Justificación |
|---|---|---|
| 1 | Alcance: todo lo que está en SAP (insumos + materiales directos). Vidrio queda en A+W. | Aclaración de Carlos. |
| 2 | Modelo operativo unificado: insumos y materiales directos siguen el mismo flujo funcional. No hay BOM ni consumo automático contra orden de producción. | Decisión del proyecto. |
| 3 | Portal Millet deprecado al cerrar este módulo: el ERP asume Requisiciones, Salidas y Reportes. | Decisión del proyecto. |
| 4 | Reportes en motor nativo del ERP, no Crystal embebido. | Decisión del proyecto. |
| 5 | El módulo no autoriza entradas ni salidas (vive aguas arriba en Requisiciones/Compras). Los ajustes y traspasos sí se autorizan dentro del módulo. Devoluciones a proveedor autorizan en CxP. | Decisión del proyecto. |
| 6 | Vale para salidas urgentes modelado como flujo de primera clase. | Decisión del proyecto. |
| 7 | Devoluciones formalizadas como movimiento con dos sub-flujos: interna (8.A) y a proveedor (8.B). | Decisión del proyecto + alineación cross-módulo con CxP §7.7. |
| 8 | Inventario físico rediseñado: captura sin sesgo, recuento por umbral, aprobación por monto. | Decisión del proyecto. |
| 9 | Costo promedio ponderado como método de valoración. | Estándar de industria; pendiente confirmar con Finanzas. |
| 10 | Movimientos inmutables; correcciones por contramovimiento. | ADR-0008, ADR-0012. |
| 11 | Tolerancias ortogonales: cantidad por OC + cantidad por material + monto por proveedor. | Alineación cross-módulo con CxP. |
| 12 | Precio de inventario = OC (variantes A y B). Si factura difiere, evento de diferencia ajusta o registra variación. | Alineación cross-módulo con CxP. |
| 13 | Almacén **no crea** el `FacturaProveedor` en CxP — solo referencia el CFDI. | Alineación cross-módulo con CxP. |

### 12.2 Decisiones diferidas a vNext

- **Operación móvil con códigos de barras y escáner.**
- **Gestión formal de bins/ubicaciones físicas** dentro del sub-almacén.
- **Clasificación ABC** del catálogo de materiales y planificación automática de conteos cíclicos.
- **Reportes 3 a 6 del Portal Millet.**
- **Putaway y picking dirigidos por sistema.**
- **Traspasos internos** entre sub-almacenes — `[Pendiente]` confirmar si entra en MVP o vNext.

### 12.3 Pendientes técnicos por validar (sin requerir a Carlos)

- Nomenclatura exacta del sub-almacén de materiales directos: ¿MP-CNK (Carlos en su respuesta) o VP-CNK (levantamiento original)? Validar en SAP.
- Lista exhaustiva de sucursales, almacenes y sub-almacenes operativos hoy. Validar en SAP.
- Operación que tarda 8 minutos hoy en SAP (probable: captura de factura en variante A). Validar con cualquier almacenista en visita rápida; sirve para línea base de medición de mejora.

### 12.4 Pendientes a resolver con otros módulos

- **Con CxP y Compras** (sesión conjunta — ver reporte cross-módulo al final de este levantamiento):
  - Confirmar contratos exactos de los eventos (nombres, campos, idempotencia).
  - Cierre del three-way match completo en código (OC + Recepción + Factura).
- **Con Finanzas:**
  - Confirmar método de valoración (promedio ponderado).
  - Umbrales monetarios para los tres niveles de autorización de ajustes por inventario.
  - Política para diferencias de precio en variante B (cuenta de variación de precio vs ajuste de costo de inventario).
- **Con Tesorería:**
  - Cotización de TC que usa Almacén (FIX, compra, venta) y horario de captura diaria.
  - TC de cierre de mes para valuación.
- **Con Calidad / Producción:**
  - Política para material dañado (sub-almacén "Material en revisión" vs destrucción inmediata).
- **Con Producción / Mantenimiento:**
  - Confirmar si las salidas deben referenciar una Orden de Trabajo (OT) además de la RQ.

---

## 13. Plan de implementación sugerido

Siguiendo el patrón de Requisiciones y OC (vertical slices, 200–500 líneas por PR, una capacidad funcional completa por slice):

1. **Modelo de datos base** — entidades `Almacen`, `SubAlmacen`, `MovimientoInventario`, `SaldoInventario`. Migración semilla desde SAP de catálogo de almacenes/sub-almacenes. Re-localización del placeholder `Almacen` del módulo Administración.
2. **Submódulo Entradas — Variante A (factura)** — flujo end-to-end de recepción con factura, incluyendo evento a CxP y Compras.
3. **Submódulo Entradas — Variante B (packing list)** — agrega la variante con factura pendiente, suscripción a `FacturaProveedorRegistradaEvent` + `DiferenciaPrecioFacturaDetectadaEvent` de CxP, y manejo de race condition contra `OrdenCompraAutorizadaEvent`.
4. **Submódulo Salidas — Variante A (normal)** — flujo end-to-end con RQ del módulo Requisiciones.
5. **Submódulo Salidas — Variante B (vale)** — agrega la variante con regularización.
6. **Submódulo Devoluciones — Sub-flujo 8.A (interna)** — flujo end-to-end.
7. **Submódulo Devoluciones — Sub-flujo 8.B (a proveedor)** — flujo end-to-end con eventos bidireccionales hacia CxP.
8. **Submódulo Inventario Físico** — conteos rotativos primero, anual con bloqueo después.
9. **Submódulo Reportes** — ALFAK HISTORIAL primero (MVP cierre de mes), MP-CNK después (MVP inventario diario).
10. **Cierre de mes** — capacidad transversal que se construye una vez que los flujos anteriores están firmes.

Cada submódulo incluye: backend + frontend + pruebas de integración + documentación. Antes del PR final de cada uno, validación con Carlos en una sesión corta de demostración.

---

## 14. Dependencias de plataforma pendientes

> Sección obligatoria (ADR-0031). Tabla *Pieza · Ticket · NoOp en uso · Cómo se wirea*.

| Pieza | Ticket | NoOp en uso | Cómo se wirea |
|---|---|---|---|
| **Esquema Postgres `almacen` + `AlmacenDbContext`** | — | n/a (se crea desde inicio) | Agregar a `MigrationsHealthCheckOptions.ContextTypes` y al bucle de migraciones en `deploy-app-dev.yml` (memoria `feedback_dbcontext_nuevo_checklist`). Coordinar con la re-localización del placeholder `Almacen` de `DatosMaestros`. |
| **Outbox para eventos de Almacén** | — | `NoOpIntegrationEventBusSender` si la cs de Service Bus está vacía | Reutilizar `OutboxSaveChangesInterceptor` + `OutboxPublisherWorker<AlmacenDbContext>` (mismo patrón que Compras, CxP). |
| **Contratos de eventos con CxP** | — | `NoOpCxpEventPublisher` y `NoOpCxpEventConsumer` mientras CxP no esté en runtime | **Publica:** `OcRecepcionRegistradaEvent`, `OcDevolucionRegistradaEvent`. **Suscribe:** `FacturaProveedorRegistradaEvent` (variante B), `DiferenciaPrecioFacturaDetectadaEvent`, `NotaCreditoFiscalDevolucionRecibidaEvent`. Naming canónico alineado con Compras-OC §8.6. |
| **Contratos de eventos con Compras** | — | Adapter directo (Compras ya en runtime) | **Publica:** `OcRecepcionRegistradaEvent`, `OcDevolucionRegistradaEvent`. **Suscribe:** `OrdenCompraAutorizadaEvent`, `OrdenCompraCanceladaEvent`, `OrdenCompraCerradaEvent`. El cierre de OC lo evalúa Compras, no Almacén. |
| **Servicio de Tipo de Cambio** | — | Stub `ITipoCambioReadPort` que retorna TC fijo | Adapter en `Administracion`/`Tesoreria` (futuro). |
| **Calendario fiscal de Finanzas** | — | Stub `IPeriodoContableReadPort` que siempre retorna "abierto" | Adapter cuando exista módulo Finanzas. |
| **Catálogo de Centros de Costo, Proyectos, Máquinas** | — | Catálogos vacíos / texto libre | Coordinar con dueños funcionales (Finanzas, Producción, Mantenimiento). |
| **Motor de reportes nativo del ERP** | — | Endpoints `GET /reportes/...` que retornan JSON estructurado, sin presentación nativa | Cierre del **ADR transversal de reportería** (§9.1). |
| **RBAC: permisos canónicos `almacen.*`** | — | Registrar al arrancar el módulo | `almacen.entradas.read/capturar/registrar`, `.salidas.*`, `.devoluciones.*`, `.inventarios.*`, `.reportes.*`, `.cierre_mes.ejecutar`. |
| **Generación de PDF** (comprobantes, listas de conteo, remisiones) | — | Stub que devuelve byte[] vacío | Cuando exista el servicio compartido (ADR-0025). |

---

## 15. Riesgos

| Riesgo | Probabilidad | Impacto | Mitigación | Dueño |
|---|---|---|---|---|
| **Resistencia operativa al cambio del Portal Millet al nuevo ERP.** Carlos y su equipo han usado el portal por años. | Media | Alto | Migración gradual con sombreo (operar ambos en paralelo durante una semana); capacitación dedicada; iteración rápida sobre feedback. | Owner del proyecto |
| **Captura de 40 facturas/día como cuello de botella** si la operación que tarda 8 minutos hoy no mejora. | Alta | Alto | Optimización agresiva del UX de captura (variante A); pre-carga desde el `CfdiRecibido` de CxP cuando esté disponible. | Owner UX |
| **Doble flujo de recepción con lógica de tolerancia compleja.** Tolerancia por línea de OC + por material + por proveedor (CxP). | Media | Medio | Documentar §10.7 con ejemplos concretos; pruebas integradas cross-módulo en cada variante. | Owner técnico |
| **Devolución a proveedor (sub-flujo 8.B) requiere coordinación bidireccional con CxP** que es nueva en el alcance. | Media | Alto | Especificar contratos de eventos antes de implementar; pruebas de integración con CxP en PR 7. | Owner técnico + CxP |
| **Sincronización de `OcRecepcionRegistradaEvent` con OC en Compras** si llega antes que `OrdenCompraAutorizadaEvent` por race condition. | Media | Medio | Outbox + idempotencia en consumidores; al recibir `OcRecepcionRegistradaEvent` sin OC conocida, queda pendiente y se procesa al recibir `OrdenCompraAutorizadaEvent`. | Owner técnico |
| **Inventarios anuales con bloqueo de salidas** chocan con operación de dos turnos. | Media | Medio | Programar conteo anual en ventana de baja operación (fin de año, semana santa); comunicación temprana. | Owner Almacén |
| **Calidad de datos del master de Materiales migrado desde SAP.** Códigos, UM, conversiones, sub-almacén default. | Alta | Alto | Auditoría previa al go-live con tabla "material × campo × valor SAP × valor a migrar"; criterio de no migrar materiales sin movimiento 2+ años (mismo criterio que Proveedores en CxP). | Owner Almacén + Compras |
| **Diferencias de precio en variante B (OC vs factura).** Cómo se ajusta el inventario y dónde se contabiliza la diferencia. | Media | Medio | Cerrar política con Contabilidad antes del PR 3; instrumentar evento `DiferenciaPrecioFacturaDetectadaEvent` con dimensiones que permitan análisis. | Owner técnico + Finanzas |
| **Costo promedio ponderado con devoluciones a costo original** introduce divergencia con el costo actual. | Baja | Bajo | Documentación contable clara; validación con auditor externo al cierre del primer mes. | Owner Finanzas |
| **El módulo Administración tiene un placeholder `Almacen`** que hay que re-localizar sin romper migración existente. | Baja | Medio | Migración aditiva; alinear con Eduardo (memoria sobre módulo Administración Fase B diferida). | Owner técnico |

---

## 16. Glosario

- **OC** — Orden de Compra. Documento del módulo Compras. Ver [`docs/modulos/compras-ordenes-compra/`](../compras-ordenes-compra/).
- **RQ** — Requisición. Documento del módulo Requisiciones (ya implementado).
- **Sub-almacén** — Tercer nivel jerárquico (Sucursal → Almacén → Sub-almacén). Es el contenedor lógico donde residen los materiales.
- **Bin / Ubicación física** — Cuarto nivel jerárquico (Sub-almacén → Bin). No modelado en MVP.
- **Packing list** — Documento del proveedor que detalla qué materiales entrega con la OC. Llega físicamente con la mercancía; la factura llega después.
- **Vale** — Documento interno físico firmado por un Coordinador de área para retirar mercancía sin RQ previa (urgencias de Mantenimiento). Requiere regularización posterior.
- **Responsiva** — Firma física del solicitante en el comprobante de salida, asumiendo responsabilidad por el material recibido.
- **Conteo / Inventario físico** — Proceso de validar inventario teórico vs físico.
- **Variación** — Diferencia entre teórico y conteo físico.
- **Costo promedio ponderado** — Método de valoración. Cada entrada actualiza el costo promedio según fórmula de §10.5.
- **Recepción** — Movimiento de entrada de mercancía contra OC.
- **Surtido** — Acción de entregar mercancía contra RQ; sinónimo de salida normal.
- **Devolución interna (8.A)** — Material previamente surtido que regresa al almacén. NO cruza con CxP.
- **Devolución a proveedor (8.B)** — Material recibido del proveedor que se le regresa por defecto/sobrecantidad. SÍ cruza con CxP (genera NotaCargo).
- **Three-way match** — Conciliación clásica: OC + Recepción + Factura. Almacén entrega Recepción; CxP entrega Factura; Compras valida OC.
- **Cierre de mes** — Proceso del primer día hábil del mes siguiente para sellar movimientos del mes anterior.
- **Portal Millet / PortalSap** — Web actual del Jefe Almacén en `192.168.1.38/PortalSap/`. Se deprecate completo al cerrar este módulo.
- **A+W** — Sistema externo que gestiona vidrio crudo. Fuera de scope.
- **ALFAK** — Nombre legacy de un reporte SAP. Se conserva por reconocimiento del usuario; el reporte se recompila en motor nativo (§9.2).
- **MP-CNK** — Sub-almacén de materiales directos no-vidrio (interlayer, silicones, pinturas). Posible variante de nombre `VP-CNK` en levantamiento original; a validar en SAP.

---

## Rev.

- **2026-05-22 — v0.1.1** — Alineación de naming de eventos cross-módulo con la convención de Compras-OC (`{Agregado}{Verbo}Event`); §11.2 y §11.3 reescritas; removido `OcCerradaPorRecepcionCompleta` (redundante con `OrdenCompraCerradaEvent` que ya emite Compras). [ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md) referenciado en §9.1 (cierre del ADR transversal de reportería).
- **2026-05-22 — v0.1** — Borrador inicial. Construido sobre el levantamiento de Carlos Burgos + aclaraciones por correo + capturas SCR-001…SCR-009 + 3 capturas nativas. Incorpora alineación con [`cuentas-por-pagar/00-levantamiento.md`](../cuentas-por-pagar/00-levantamiento.md) (cierra los `[Pendiente — Almacén]` de CxP). Pendientes principales: cerrar políticas de Finanzas (valoración, umbrales de autorización, diferencia de precio variante B).
