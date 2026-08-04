# Mapa funcional — Submódulo Órdenes de Compra

> **Proyecto:** ERP Millet — Módulo de Compras
> **Submódulo:** Órdenes de Compra (OC)
> **Versión:** 0.1 — Borrador para revisión
> **Fecha:** 2026-05-11
>
> **Origen:** scope cliente-driven (no hay legacy de OC en el portal SAP para
> ingeniería inversa; el sistema actual de OC vive en SAP Business One).
> Este documento es la fuente de verdad sobre la que se construyen los
> `01-diseno.md` … `07-frontend-pr-breakdown.md` del submódulo.
>
> **Estado:** validado contra entrevistas con Rodrigo Chay (Jefe de Compras)
> y Eduardo Paredes (owner del proyecto). Las decisiones pendientes están
> listadas en §10.

---

## 1. Propósito y alcance

### Qué hace este submódulo

Captura, autoriza y administra el ciclo de vida de las órdenes de compra
emitidas a proveedores. Una OC formaliza un compromiso comercial: qué se
va a comprar, a qué proveedor, a qué precio, en qué condiciones y con
qué autorización interna. Es la pieza que conecta una requisición
autorizada con la entrada física del material y, posteriormente, con el
registro contable del pasivo y su pago.

### Qué NO hace

- **No gestiona cotizaciones formales.** La negociación con proveedores
  ocurre fuera del sistema (correo, teléfono, visitas). La OC solo
  registra el resultado y permite adjuntar la cotización ganadora como
  evidencia.
- **No es un portal para proveedores.** Los proveedores no acceden a este
  submódulo. La carga de facturas la hace personal interno (CxP). El
  portal queda como fase posterior.
- **No ejecuta pagos.** El pago vive en Tesorería. Este submódulo refleja
  el estado de pago como información, pero no lo opera.
- **No valida CFDI contra el SAT.** La validación fiscal automatizada
  queda como decisión posterior.

### Punto de partida y punto de llegada

- **Entrada principal:** una requisición autorizada (submódulo de
  Requisiciones, ya existente).
- **Entrada secundaria:** captura directa por personal autorizado en
  casos especiales (Dirección, urgencias).
- **Salida principal:** una OC autorizada, comunicada al proveedor en
  formato PDF, lista para iniciar el ciclo de recepción y facturación.

---

## 2. Actores y roles

| Rol | Acciones que ejecuta |
|---|---|
| **Comprador** | Captura la OC a partir de una requisición autorizada o desde cero. Adjunta cotización ganadora y otros documentos de respaldo. Envía a autorización. Da seguimiento. |
| **Jefe de Compras (Rodrigo Chay)** | Autoriza la OC en primer nivel. Puede rechazar con comentario para corrección. |
| **Director** | Autoriza la OC en segundo nivel. Puede rechazar con comentario. Es la firma final que habilita el envío al proveedor. |
| **Almacén de Insumos** | Consume la OC autorizada para registrar la entrada del material (submódulo de Recepción). |
| **Cuentas por Pagar** | Consume la OC autorizada para validar facturas recibidas y crear el pasivo. |

> **Nota sobre la matriz de autorización:** la matriz ya está implementada
> como componente reutilizable en el submódulo de Requisiciones. Este
> submódulo la consume con su propia configuración de niveles. Los
> autorizadores concretos (Comprador, Jefe de Compras, Director) se
> definen como datos de configuración, no como código, lo que permite
> ajustar niveles y asignaciones sin tocar la implementación.

---

## 3. Ciclo de vida de la Orden de Compra

La OC tiene un **estado principal** que refleja en qué punto del flujo de
autorización y operación se encuentra, y un conjunto de **sub-estados**
que reflejan el avance independiente de recepción, facturación y pago.

### 3.1 Estados principales

| Estado | Descripción | Editable | Visible para |
|---|---|---|---|
| **Borrador** | Capturándose. Todavía no se ha enviado a autorización. | Sí | Comprador que la creó |
| **En autorización — Jefe de Compras** | Enviada al primer nivel de autorización. | No | Todos los compradores y Rodrigo |
| **En autorización — Dirección** | Aprobada por Jefe de Compras, pendiente de firma final. | No | Todos los compradores, Rodrigo y Director |
| **Autorizada** | Lista para enviar al proveedor y para que arranque la recepción. | No (excepción documentada) | Compras, Almacén, CxP |
| **Cerrada** | Ciclo completo: recibida, facturada y pagada. | No | Todos |
| **Cancelada** | No procedió. Con motivo registrado. | No | Todos |
| **Rechazada** | Algún autorizador la devolvió con comentarios. Vuelve a Borrador para corrección o se cancela. | Sí (al volver a Borrador) | Comprador y autorizador que rechazó |

### 3.2 Sub-estados de la OC autorizada

Estos avanzan independientemente y combinados forman la vista de
**partidas abiertas** que el documento de levantamiento describe como
funcionalidad crítica.

| Dimensión | Valores posibles |
|---|---|
| **Recepción** | Sin recepción · Parcial · Completa |
| **Facturación** | Sin factura · Parcial · Completa |
| **Pago** | Sin pago · Parcial · Pagada |

Una OC pasa a estado **Cerrada** cuando las tres dimensiones llegan a
Completa / Completa / Pagada.

---

## 4. Flujos principales

### 4.1 Creación 1:1 desde una requisición autorizada

Este es el flujo cuando el comprador parte desde la bandeja del submódulo
de Requisiciones y elige convertir una requisición específica en OC.

1. Comprador entra a la bandeja de requisiciones autorizadas pendientes
   de OC.
2. Selecciona una requisición y ejecuta la acción "convertir en OC".
3. El sistema crea una OC nueva en estado **Borrador**, pre-llenada con:
   artículos, cantidades, almacenes destino, departamento solicitante,
   referencia a la requisición de origen.
4. La requisición queda **comprometida en OC borrador**: deja de estar
   disponible para selección en otros flujos de creación. Si la OC se
   cancela posteriormente estando aún en borrador, la requisición regresa
   al pool de disponibles (ver §4.6).
5. Comprador selecciona proveedor. Al seleccionarlo, el sistema muestra
   el historial de últimas 100 compras del material a ese proveedor con
   cantidades y precios.
6. Comprador captura precio unitario por línea, condiciones de pago,
   fecha de entrega esperada, información logística y observaciones.
7. Comprador adjunta cotización ganadora como documento de respaldo
   (obligatorio salvo autorización explícita registrada).
8. Sistema calcula automáticamente: subtotal, IVA, retenciones de ISR
   según régimen del proveedor, total.
9. Comprador revisa y envía a autorización. El estado pasa a
   **En autorización — Jefe de Compras**.

> **Relación cardinal:** una requisición → una OC. Este flujo no permite
> consolidar; si el comprador necesita agrupar varias requisiciones, debe
> usar el flujo §4.2.

### 4.2 Creación consolidada desde el módulo de Compras

Este es el flujo cuando el comprador parte desde el módulo de Compras
(no desde la bandeja de Requisiciones) para crear una OC nueva, con la
posibilidad de **consolidar varias requisiciones** en un solo pedido al
proveedor.

1. Comprador entra al módulo de Compras y ejecuta la acción "nueva OC".
2. El sistema crea una OC vacía en estado **Borrador**.
3. Comprador selecciona proveedor.
4. Comprador abre el selector de requisiciones disponibles. El selector
   muestra únicamente requisiciones que cumplen todas estas condiciones:
   - Están autorizadas.
   - No están comprometidas en otra OC (ni borrador ni autorizada).
   - **Restricción por sucursal/planta destino:** a definir
     (ver pregunta abierta en §10). La **mezcla de departamentos**
     solicitantes está explícitamente permitida.
5. Comprador selecciona una o varias requisiciones. Cada renglón
   seleccionado se agrega como una línea independiente en la OC,
   conservando trazabilidad línea-a-línea con la requisición de origen.
6. Las requisiciones seleccionadas quedan **comprometidas en OC
   borrador**: dejan de aparecer en el selector para otras OCs.
7. El resto del flujo (precios, adjuntos, autorización) es idéntico al
   flujo §4.1.

> **Relación cardinal:** N requisiciones → 1 OC. El caso degenerado N=1
> también es válido: si el comprador entra desde Compras y selecciona
> solo una requisición, el resultado es equivalente al flujo §4.1.

**Política de líneas en consolidación.** Cuando varias requisiciones
contienen el mismo artículo, el sistema **conserva las líneas separadas
internamente** en lugar de sumar cantidades en una sola línea. Esto
preserva la trazabilidad 1:1 entre cada línea de OC y su renglón de
requisición de origen, lo que facilita la recepción parcial atribuida a
cada requisitante original y el reporteo por departamento solicitante.

**Política de impresión / vista para el proveedor.** El PDF que se envía
al proveedor **agrupa las líneas por artículo**, sumando cantidades
cuando el mismo material aparece en varios renglones internos. El
proveedor recibe una vista limpia, sin desglose de requisiciones
internas, mientras que el desglose se mantiene visible en las pantallas
internas, en el reporteo y en la bandeja de partidas abiertas.

> **Pregunta abierta:** ¿El selector de requisiciones debe restringir por
> sucursal/planta destino (solo requisiciones que vayan a la misma
> sucursal de la OC), o permite mezclar? La mezcla de departamentos
> solicitantes ya está decidida como permitida. La de sucursales sigue
> pendiente. Mi recomendación es **restringir a sucursal única** porque
> la dirección de entrega al proveedor es una sola, pero el almacén
> destino dentro de esa sucursal sí puede variar por línea; conviene
> confirmar con Rodrigo.

### 4.3 Creación sin requisición previa (caso especial)

Aplica solo en escenarios autorizados:

- Compras especializadas instruidas por Dirección.
- Urgencias de producción autorizadas por Dirección.
- Servicios recurrentes mensuales (mantenimiento, transporte).

El flujo es similar al estándar pero el sistema marca la OC con una
bandera **"sin requisición previa"** y exige captura de motivo y
autorización registrada (correo o documento adjunto). La autorización
formal sigue el flujo normal de dos niveles, no se salta.

### 4.4 Autorización secuencial

1. **Jefe de Compras** revisa la OC. Puede:
   - **Autorizar** → estado pasa a *En autorización — Dirección*.
   - **Rechazar con comentario** → estado pasa a *Rechazada*, regresa
     al comprador.
2. **Director** revisa. Puede:
   - **Autorizar** → estado pasa a *Autorizada*. Sistema genera PDF y
     notifica al comprador para envío al proveedor.
   - **Rechazar con comentario** → estado pasa a *Rechazada*, regresa
     al comprador.

Cualquier rechazo registra autor, fecha, motivo y queda en bitácora de
la OC.

### 4.5 Modificación

Una OC en estado **Borrador** o **Rechazada** es editable libremente.
Esto incluye agregar o quitar líneas vinculadas a requisiciones (las
requisiciones removidas regresan al pool de disponibles).

Una OC ya **Autorizada** no es editable, salvo casos excepcionales con
**doble autorización (Jefe de Compras + Director)**. Una modificación
post-autorización registra una nueva versión en bitácora; las versiones
previas no se borran.

### 4.6 Cancelación

Una OC puede cancelarse en cualquier estado anterior a tener factura
asociada. Requiere motivo registrado.

**Efecto sobre las requisiciones asociadas:**

- **Cancelar una OC en estado Borrador:** todas las requisiciones
  asociadas regresan automáticamente al pool de disponibles y vuelven a
  aparecer en los selectores de los flujos §4.1 y §4.2. No requiere
  autorización adicional porque la OC nunca alcanzó autorización formal.
- **Cancelar una OC ya Autorizada sin recepciones:** las requisiciones
  regresan al pool de disponibles. Requiere doble autorización (Jefe de
  Compras + Director) porque la OC ya tenía compromiso formal con el
  proveedor.
- **Cancelar una OC ya Autorizada con recepciones parciales:** requiere
  doble autorización, implica devolución al proveedor o nota de crédito,
  y las requisiciones solo regresan **parcialmente** al pool (por la
  cantidad no recibida). Las cantidades ya recibidas permanecen
  asociadas a la OC cancelada para mantener trazabilidad contable y de
  inventario.

### 4.7 Cierre

El cierre es **automático** cuando recepción, facturación y pago llegan
a Completa. No requiere acción manual.

---

## 5. Estructura de la Orden de Compra

### 5.1 Cabecera

| Campo | Origen | Editable |
|---|---|---|
| Folio interno | Auto-generado | No |
| Fecha de documento | Default fecha de creación | Sí (en borrador) |
| Fecha de contabilización | Default fecha de autorización | No (post-autorización) |
| Fecha de entrega esperada | Captura | Sí |
| Proveedor | Catálogo de Proveedores | Sí (en borrador) |
| Persona de contacto del proveedor | Heredado del proveedor, editable | Sí |
| Número de referencia del proveedor | Captura libre (folio de la cotización del proveedor o su número interno de pedido) | Sí |
| Sucursal o planta destino | Catálogo de Sucursales | Sí |
| Almacén destino default | Catálogo de Almacenes (puede sobrescribirse por línea) | Sí |
| Moneda | Catálogo (default MXN) | Sí |
| Tipo de cambio | Servicio externo o manual | Sí (cuando moneda ≠ MXN) |
| Condiciones de pago | Catálogo (heredable del proveedor) | Sí |
| Uso principal / clasificación contable | Catálogo (gastos en general, inventario, activo fijo, etc.) | Sí |
| Encargado de compras | Usuario actual o asignable | Limitado |
| Comprador responsable / titular del documento | Usuario actual | No |
| Observaciones | Texto libre | Sí |
| Bandera "sin requisición previa" | Auto si aplica el flujo §4.3 | No |
| Bandera "es importación" | Captura | Sí |

> **Nota sobre folios y búsqueda:** tanto el **folio interno**
> (auto-generado por el ERP) como el **número de referencia del
> proveedor** son campos **indexados de primer nivel**. Ambos sirven como
> criterio de búsqueda principal en bandejas y reportes — el folio
> interno para conciliación dentro del ERP, y el número de referencia
> para conciliar contra documentos externos que envía el proveedor
> (cotizaciones, confirmaciones de pedido, facturas, guías de embarque).
> La indexación es requisito de MVP.

### 5.2 Líneas (renglones)

Cada renglón representa un artículo o servicio comprado.

| Campo | Comportamiento |
|---|---|
| Artículo | Catálogo de Artículos. Búsqueda por código o descripción. |
| Descripción extendida | Heredada del catálogo, editable. |
| Cantidad | Numérica. |
| Unidad de medida | Heredada del artículo. |
| Precio unitario | Captura. El sistema sugiere a partir del historial de compras. |
| Descuento | Opcional, porcentaje o monto. |
| IVA | Calculado automático según régimen del artículo y proveedor. |
| Retención ISR | Calculada automático cuando aplica. |
| Subtotal de línea | Cálculo automático. |
| Almacén destino de la línea | Default al de cabecera, editable. |
| Departamento solicitante de la línea | Heredado de la requisición de origen, o capturado libremente en OCs sin requisición previa. Una OC puede tener líneas de departamentos distintos (consolidación cross-departamento permitida). |
| Referencia a línea de requisición | Auto si la OC viene de una RQ. |
| Fecha de entrega de la línea | Default a la de cabecera, editable. |
| Cantidad pendiente (calculada) | Cuánto del renglón aún no ha sido recibido. Inicialmente igual a la cantidad capturada; decrece con cada recepción parcial. Visible en pantallas de seguimiento y en bandejas de partidas abiertas. |
| Existencia actual del material (informativa) | Stock disponible en el almacén destino al momento de la captura. Se muestra junto al renglón pero **no se persiste** como campo de la OC. Sirve de referencia al comprador para evitar duplicidad y exceso de inventario. Funcionalidad explícitamente marcada como crítica en el levantamiento. |
| Indicador de impuestos | Heredado del proveedor o del artículo, editable. |
| Texto adicional de la línea | Notas específicas del renglón, libre. |

### 5.3 Documentos de respaldo (adjuntos genéricos)

Una OC puede tener N documentos adjuntos. Cada adjunto registra:

- Nombre del archivo
- Tipo de documento (catálogo extensible: cotización, correo de
  autorización, ficha técnica, otro)
- Fecha de carga
- Usuario que cargó

La **cotización ganadora** es obligatoria salvo autorización explícita
registrada al momento de la creación.

### 5.4 Información adicional para importaciones

Cuando la OC tiene la bandera "es importación", se habilitan campos y
exigencias adicionales:

- **Incoterm**
- **País de origen**
- **Documentos requeridos:** ficha técnica del material (obligatoria),
  pedimento (al recibir), factura del proveedor extranjero, otros según
  aduana.
- Estos documentos se cargan a través del componente de adjuntos como
  tipos específicos.

### 5.5 Información complementaria (logística y finanzas)

Más allá de cabecera y líneas, la OC contiene dos áreas adicionales que
agrupan información especializada. En el sistema actual existen como
tabs separadas dentro de la pantalla de OC; en el ERP nuevo conviene
mantener una organización equivalente para no saturar la captura.

**Información logística — campos formales desde el MVP.**

Hoy en SAP esta información se captura como texto libre embebido en el
campo de comentarios, con un formato manual del estilo
`RUTA:6515 CONTENEDOR:SEGU6397527 CLIENTE:BCI CRAWFORD SEMANA:05/DICIEMBRE/2025`.
Esto es trabajo manual del comprador para suplir la falta de campos
nativos, y tiene tres consecuencias negativas: no se puede filtrar por
estos valores en bandejas, no se puede reportar agregado, y el dato es
propenso a inconsistencias de captura (mayúsculas, abreviaturas,
errores). En el ERP nuevo estos pasan a ser **campos estructurados de
primer nivel desde el MVP**.

Campos:

- Dirección de entrega (puede diferir del domicilio fiscal del
  proveedor).
- Transportista o medio de envío.
- Número de guía o referencia de transporte (FedEx, DHL, transportista
  propio).
- **Cliente final destinatario**, cuando la OC se compra para un cliente
  identificado y no para stock. Aplica en escenarios de proyectos
  específicos. *(Pendiente: definir master del catálogo, modo de
  referencia y casos en que aplica — ver §10.)*
- Instrucciones de envío.

Para importaciones, además de los anteriores:

- Número de contenedor.
- Código de ruta.
- Semana de embarque.
- Número de pedimento (cuando aplique al recibir).
- País de origen.

Todos los campos anteriores deben ser **indexables y filtrables** en
bandejas, reportes y búsquedas globales.

**Información financiera:**

- Condiciones de pago detalladas con fecha de vencimiento calculada.
- Indicador de impuestos por defecto para las líneas.
- Retenciones aplicables (ISR cuando proceda).
- Gastos adicionales (fletes, maniobras, otros costos no reflejados en
  líneas).
- Descuento global (porcentaje o monto).
- Redondeo.
- Totales calculados: subtotal antes de descuento, descuento global,
  gastos adicionales, impuesto, retenciones, total a pagar.

---

## 6. Reglas de negocio

### 6.1 Validaciones obligatorias

- Toda OC debe tener al menos un renglón con cantidad mayor a cero y
  precio mayor a cero.
- El proveedor debe estar activo en el catálogo (no bloqueado).
- El artículo debe estar activo en el catálogo.
- **Toda compra, incluyendo gastos menores en efectivo, debe tener OC.**
  No existe el concepto de "compra directa sin OC".
- La cotización ganadora debe estar adjunta antes de enviar a
  autorización, salvo autorización explícita por escrito registrada.
- Para OCs sin requisición previa: motivo obligatorio y autorización
  adjunta.
- Para OCs de importación: ficha técnica obligatoria antes de
  autorización.
- **Exclusividad de requisiciones:** una requisición autorizada solo
  puede estar comprometida en una OC activa a la vez. El sistema bloquea
  su selección en otros flujos mientras esté asociada a una OC en estado
  Borrador, En autorización o Autorizada. Una requisición regresa al
  pool de disponibles únicamente si la OC que la contiene se cancela o
  si se elimina del documento durante edición en Borrador.

### 6.2 Cálculos automáticos

- IVA por línea según régimen fiscal del artículo y proveedor.
- Retenciones de ISR cuando el régimen del proveedor aplique.
- Subtotal, total general, total con impuestos.
- Conversión a moneda local cuando la OC esté en moneda extranjera.

### 6.3 Restricciones operativas

- No se acepta captura de OCs durante el cierre de mes salvo urgencias
  de producción autorizadas (regla del documento, §10.1).
- Una OC autorizada no se modifica salvo doble autorización registrada.
- No se puede cancelar una OC con factura asociada; primero hay que
  cancelar la factura (proceso aparte).
- La validación de stock al capturar una OC es **informativa, no
  bloqueante** (el comprador ya validó stock al revisar la requisición).
  Sirve como recordatorio y para evitar duplicidad cuando la OC se
  captura sin RQ.

---

## 7. Integraciones con otros submódulos y módulos

### 7.1 Entrada — desde Requisiciones

El submódulo consume requisiciones autorizadas. Lo que recibe:

- Identificador de la requisición
- Líneas con artículo, cantidad, almacén destino
- Departamento solicitante y solicitante original
- Naturaleza de la requisición

Una requisición puede dar origen a una o más OCs (si se compra a varios
proveedores). Una OC puede agrupar líneas de varias requisiciones
(consolidación). El sistema mantiene la trazabilidad bidireccional.

### 7.2 Consulta — Inventario

Al capturar una OC el comprador puede consultar:

- Existencia actual del material en cada almacén
- Pedidos en tránsito (otras OCs autorizadas pendientes de recepción)
  del mismo material
- Historial de **últimas 100 compras del material** con proveedor,
  cantidad, precio y fecha

Esta vista reusa la lógica ya implementada en el submódulo de
Requisiciones.

### 7.3 Adjuntos — componente transversal

El componente de adjuntos sirve a OC para cotizaciones, correos, fichas
técnicas. El mismo componente se reusa en otros módulos del ERP que
necesiten anexar archivos.

### 7.4 Salida — Recepción de Materiales

Una OC autorizada queda visible en la bandeja del submódulo de
Recepción. Almacén registra entradas parciales o totales contra los
renglones de la OC.

### 7.5 Salida — Factura de Proveedor

Una OC autorizada permite la creación de una o más facturas de
proveedor asociadas. La factura es entidad propia (PDF + XML, datos
fiscales estructurados, múltiple por OC). Operada por CxP.

### 7.6 Catálogos maestros que consume

- Proveedores
- Artículos / materiales
- Condiciones de pago
- Monedas y tipos de cambio
- Almacenes
- Unidades de medida
- Departamentos
- Regímenes fiscales (para cálculo de IVA y retenciones)
- Incoterms (para importaciones)
- Tipos de documento adjunto

> **Nota sobre los catálogos:** el master de Proveedores, Artículos y
> demás catálogos maestros **es el ERP nuevo** a partir del go-live.
>
> **En MVP** (alcance de este submódulo), los catálogos se operan a
> través de **datos seed versionados con el código** + endpoints `GET`
> **read-only**. **No se construyen pantallas de CRUD ni se importa
> nada desde SAP en este alcance.** Los seeds se enriquecen cuando
> aparezcan escenarios de prueba que los requieran.
>
> **Diferidos post-MVP** (bloque separado, posterior al go-live de
> OC): las pantallas de CRUD para administración interna de catálogos
> y la migración inicial desde SAP B1 (que entonces convierte al ERP
> en master operativo y deja a SAP obsoleto para estos catálogos).

---

## 8. Reportes y vistas operativas

### 8.1 Bandejas

Cada bandeja permite búsqueda y filtrado por los campos clave de la OC:
folio interno, número de referencia del proveedor, nombre o código del
proveedor, comprador titular, rango de fechas, estado, importe, cliente
final destinatario, y para importaciones por contenedor, ruta y semana
de embarque. Los folios y campos de referencia están **indexados** para
búsqueda inmediata.

- Mis OCs en borrador (por comprador)
- Pendientes de autorización por mí (por autorizador, según nivel)
- OCs autorizadas pendientes de recepción (vista global y por proveedor)
- OCs recibidas pendientes de factura
- OCs facturadas pendientes de pago
- OCs cerradas (histórico)
- OCs canceladas o rechazadas (auditoría)

### 8.2 Reportes operativos

- **Partidas abiertas:** vista consolidada de todas las OCs activas con
  su sub-estado de recepción, facturación y pago. Esta es la vista que
  el documento de levantamiento llama "seguimiento a las partidas
  abiertas" y que se considera funcionalidad **crítica** a preservar.
  Incluye filtro por tipo de documento (pedidos pendientes, entradas
  pendientes, facturas en parcialidades, facturas de reserva sin pagar)
  y columna calculada de días atrasados contra la fecha de vencimiento.
- **Últimas 100 compras del material:** disponible al capturar una OC y
  como reporte independiente. Muestra proveedor, cantidad, precio
  unitario, fecha.
- **Análisis de compras por proveedor / artículo / usuario:** para el
  cierre mensual.
- **Reportes mensuales especializados:** servicios de mantenimiento
  mensual, servicios de transporte mensual.

### 8.3 Salida hacia el proveedor

Al autorizarse una OC, el sistema genera un PDF con formato
institucional que el comprador envía al proveedor. El PDF incluye datos
fiscales del proveedor, datos de Millet, líneas de la OC, condiciones
de pago, fecha de entrega, instrucciones de envío y firma autorizada.

**Agrupación por artículo.** Aunque internamente una OC consolidada
puede tener varias líneas del mismo artículo (una por cada requisición
de origen, para preservar trazabilidad), el PDF al proveedor las
**agrupa en una sola línea por artículo**, sumando cantidades. El
proveedor recibe una vista limpia, sin información de departamentos
internos ni desglose por requisición. El desglose detallado permanece
en las pantallas internas del sistema.

El envío al proveedor es **manual** (correo) en esta fase; la
automatización se considera para fase posterior.

### 8.4 Árbol de documentos y trazabilidad visual

Una vista que muestra la cadena completa de documentos del ciclo:
**Requisición → Orden de Compra → Recepción → Factura → Pago**. Cada
nodo presenta folio, fecha y monto del documento correspondiente, con
enlaces para abrir cualquiera de ellos directamente. Disponible desde
cualquier documento del ciclo, en cualquier dirección.

La conexión visual permite responder preguntas operativas frecuentes —
"¿esta OC ya se pagó?", "¿qué requisición originó esta entrada?", "¿qué
facturas amparan esta OC y cuáles ya están pagadas?" — sin tener que
navegar manualmente entre pantallas o construir consultas.

Esta funcionalidad existe en el sistema actual como "árbol de
documentos" y es altamente valorada tanto por Compras como por CxP.
Conviene replicarla **desde el inicio del bloque**, no como agregado
posterior.

### 8.5 Reporte de últimos precios del material

Disponible como pantalla independiente y embebida al capturar una línea
de OC. Permite al comprador ver el histórico reciente de compras de un
artículo:

- Filtros por tipo de documento: facturas, pedidos, entradas de
  mercancía, devoluciones, notas de crédito, ofertas de compra.
- Filtro por proveedor (uno, varios o todos).
- Cantidad de registros a mostrar (default 100, configurable).
- Filtros por fecha y por cantidad mínima.

Resultado muestra: proveedor, documento de origen, fecha, cantidad,
precio unitario, descuento aplicado y precio neto. Esta es la pantalla
concreta que el levantamiento describe como funcionalidad crítica para
evitar duplicidad y comparar precios entre proveedores.

---

## 9. Casos especiales y variantes

### 9.1 Compras especializadas de Dirección

Sin requisición previa, sin frecuencia fija, autorización por correo de
Dirección.

**Cómo lo soporta el submódulo:** flujo §4.3 (creación sin RQ) con
bandera correspondiente, motivo obligatorio y correo adjunto. Sigue los
dos niveles de autorización formal.

### 9.2 Importaciones

Aplican reglas adicionales:

- Bandera "es importación" en cabecera.
- Ficha técnica del material obligatoria antes de autorización.
- Recopilación de documentos para aduana (pedimento, factura del
  proveedor extranjero, packing list, otros) gestionada vía adjuntos.
- Incoterm y país de origen obligatorios.

### 9.3 Compras urgentes de pago de contado

Se crea OC normal. Al recibir el material y la factura, CxP marca la
factura como "pago de contado" en lugar de programar pago a crédito.
La OC misma no cambia su flujo; el pago acelerado se gestiona desde
Tesorería.

### 9.4 Servicios recurrentes mensuales (mantenimiento, transporte)

Se crea una OC mensual estándar. El cierre del ciclo y los reportes
mensuales asociados se generan desde el módulo de reportes con filtros
específicos. Considerar para fase posterior la posibilidad de contratos
marco o OCs abiertas que generen recepciones recurrentes.

### 9.5 Devoluciones a proveedores

Las devoluciones entran como funcionalidad del MVP. Conceptualmente son
un sub-flujo del submódulo de Recepción más que de Órdenes de Compra,
pero afectan el ciclo de vida de la OC porque pueden cambiar su estado
de cierre.

**Cuándo aplican:** cuando el material recibido no corresponde a lo
solicitado, está dañado, no cumple especificaciones, o el proveedor
envió de más.

**Disparo:** el responsable de almacén o el comprador identifica el
problema durante o después de la recepción.

**Tipos:**

- **Devolución total:** se devuelve toda la mercancía recibida en una
  entrada.
- **Devolución parcial:** solo parte de la entrada se devuelve.

**Efectos sobre el inventario:** se genera un movimiento de salida que
reversa total o parcialmente la entrada original. Queda trazabilidad
bidireccional con la entrada que reversa.

**Efectos sobre la factura:**

- Si la factura aún no está registrada, el proveedor emite la factura
  ajustada por la cantidad efectivamente aceptada.
- Si la factura ya está registrada, se requiere **nota de crédito** del
  proveedor (CFDI tipo Egreso). El sub-flujo registra la nota de crédito
  como entidad asociada a la factura original y ajusta el pasivo.

**Efectos sobre el pago:**

- Si el pago no se ha programado, se ajusta el monto a programar.
- Si el pago ya se ejecutó, queda saldo a favor con el proveedor para
  aplicar a una compra futura o solicitar reembolso (decisión operativa
  de CxP).

**Efectos sobre la OC:** una OC con devolución pendiente no avanza a
Cerrada hasta que el ajuste fiscal y de inventario esté completo.

---

## 10. Decisiones pendientes que afectan este submódulo

1. **Validación de CFDI contra SAT al cargar factura.** Afecta el
   submódulo de Factura de Proveedor más que el de OC, pero se resuelven
   juntas.
2. **Continuidad operativa sin internet.** No es decisión funcional sino
   de infraestructura, pero afecta el diseño de la captura. Pendiente
   con el área de TI.
3. **Campo "cliente final destinatario" en OC.** Pendiente discutir con
   el encargado del área en Millet. Sub-decisiones a cerrar:
   - **Master del catálogo de Clientes:** ¿vive en A+W (sistema
     comercial) o en este ERP? La arquitectura general apunta a que A+W
     es master, pero conviene confirmarlo explícitamente.
   - **Modo de referencia:** si A+W es master, ¿la OC guarda solo el ID
     externo del cliente, o también un snapshot del nombre al momento de
     captura para tolerar fallas de conexión y para mantener legibilidad
     del documento si el cliente se modifica posteriormente?
   - **Casos en que aplica:** ¿solo importaciones para proyectos
     específicos, o cualquier OC que el comprador identifique con un
     cliente final? Definir si es obligatorio en algún caso o siempre
     opcional.
5. **Restricción del selector de requisiciones por sucursal/planta.**
   Pendiente confirmar con Rodrigo si la consolidación se restringe a
   una sola sucursal por OC (la dirección de entrega al proveedor es
   única) o si permite mezclar. Mi recomendación es **restringir a
   sucursal única**; el almacén destino dentro de esa sucursal sí puede
   variar por línea. La mezcla de departamentos solicitantes ya está
   confirmada como permitida.

### Decisiones resueltas (registro)

- **Cotizaciones:** adjunto en OC, no submódulo formal.
- **Factura de proveedor:** entidad propia con PDF + XML, múltiple por
  OC.
- **Portal de proveedores:** fuera de alcance, modelo reservado.
- **Validación de stock al capturar requisición:** implementada.
- **Matriz de autorización:** componente reusable existente del
  submódulo de Requisiciones.
- **Master de catálogos:** ERP nuevo a partir del go-live. En MVP los
  catálogos se operan con **seeds versionados** + endpoints `GET`
  read-only. No hay pantallas de CRUD ni importación desde SAP en
  este alcance.
- **Importación inicial desde SAP B1:** **diferida post-MVP**. No
  forma parte del scope del submódulo OC v1. Cuando se reabra (bloque
  separado posterior al go-live), el modelo de dominio actual soporta
  migración aditiva.
- **CRUD de catálogos:** diferido post-MVP. Desarrollo y QA operan
  con datos seed versionados; las pantallas de CRUD y la migración
  inicial desde SAP se construyen como bloque separado posterior al
  go-live.
- **Devoluciones a proveedores:** incluidas en MVP.
- **Información logística estructurada** (transportista, guía, cliente
  final destinatario, contenedor, ruta, semana de embarque, país de
  origen): incluida en MVP como campos formales de primer nivel, no
  como texto libre dentro de comentarios.
- **Folio interno y número de referencia del proveedor:** ambos
  indexados de primer nivel desde MVP, para búsqueda inmediata en
  bandejas y reportes.
- **Cardinalidad Requisición ↔ OC:** 1:1 desde el flujo de bandeja de
  Requisiciones; N:1 desde el flujo de consolidación en el módulo de
  Compras. Una requisición solo puede estar comprometida en una OC
  activa a la vez; cancelar una OC en borrador libera sus requisiciones
  automáticamente. En consolidación, las líneas se mantienen separadas
  (no se suman cantidades del mismo artículo) para preservar
  trazabilidad línea-a-línea con cada requisición de origen.
- **Mezcla de departamentos en consolidación:** permitida. El
  departamento solicitante vive a nivel línea de OC, no a nivel
  cabecera. Una sola OC puede tener líneas de departamentos distintos.
- **Impresión / PDF al proveedor:** agrupa líneas por artículo, sumando
  cantidades. Internamente el sistema mantiene las líneas separadas con
  su trazabilidad a la requisición de origen.

---

## 11. Volúmenes esperados

Tomados del levantamiento de Rodrigo Chay, sección 1.3:

- **15 a 45 OCs nuevas por día** (360 a 400 mensuales)
- **15 a 50 entradas de mercancía** por día
- **15 a 50 facturas** por día
- Picos los **viernes, lunes y fin de mes**
- **3 usuarios concurrentes** típicos en Compras

El sistema debe responder cómodamente a este volumen con margen para
crecimiento, considerando picos de cierre mensual.

---

## 12. Indicadores de éxito del submódulo

Una OC bien diseñada debería permitir:

- Capturar una OC estándar en **menos de 5 minutos** (regla del
  documento).
- Visualizar en una sola pantalla el historial de compra del material y
  los pedidos en tránsito.
- Tener trazabilidad completa: requisición de origen → OC → recepción →
  factura → pago.
- Bandejas de partidas abiertas que reflejen el estado real en tiempo
  real.
- Validación de stock al momento de la captura, evitando compras
  duplicadas.
- Generación inmediata del PDF al autorizarse, sin pasos manuales
  adicionales.

---

## 13. Lo que sigue después de validar este mapa

1. Confirmar las decisiones pendientes de §10.
2. Bajar a diseño del modelo de datos (entidades, atributos, relaciones,
   índices) en `01-diseno.md`.
3. Definir las pantallas de captura, autorización y consulta en
   `05-frontend-diseno.md`.
4. Implementar el submódulo en orden: modelo → captura → autorización →
   bandejas → integraciones de salida (Recepción y Factura).

---

## Revisiones

| Rev. | Fecha | Cambio |
|---|---|---|
| 0.1 | 2026-05-11 | Borrador inicial del mapa funcional, basado en sesión con Rodrigo Chay y Eduardo Paredes. |
| 0.2 | 2026-05-11 | Resolución §10.3 "Migración SAP": diferida post-MVP. MVP usa seeds versionados + endpoints GET read-only; CRUD y migración SAP forman bloque separado posterior al go-live. Eliminado §10 ítem 3 de "pendientes"; entradas relacionadas actualizadas en "Decisiones resueltas" y en la nota de §7.6. |
