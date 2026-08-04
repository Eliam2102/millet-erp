# Levantamiento — Módulo Facturación (`Millet.Facturacion`)

> **Proyecto:** ERP Millet — Módulo 3 del back-office (Facturación CFDI 4.0).
> **Versión:** 1.0 — derivado del mapa funcional v0.4 (mayo 2026), ya
> revisado por el área (25 comentarios del PDF + 3 escenarios nuevos del
> correo + decisiones de arquitectura).
> **Fecha:** 2026-05-29
>
> **Owner del módulo:** Eduardo Paredes — `eduardo.paredes@tiglass.net`
>
> **Patrón:** sigue el exemplar de **Compras** y **Cuentas por Pagar**
> (hexagonal + CQRS con MediatR; Application/Domain/Infrastructure;
> DbContext propio con esquema separado por [ADR-0030](../../decisiones/0030-multi-dbcontext-por-modulo.md)).
> Hereda las piezas transversales del backend: Outbox ([ADR-0009](../../decisiones/0009-outbox-pattern-eventos-integracion.md)),
> Idempotency-Key ([ADR-0020](../../decisiones/0020-idempotencia-http.md)),
> versionado `/api/v1/` ([ADR-0021](../../decisiones/0021-versionado-api-rest.md)),
> RBAC granular ([ADR-0007](../../decisiones/0007-autorizacion-rbac-granular.md)),
> Problem Details ([ADR-0010](../../decisiones/0010-manejo-errores-problem-details.md)),
> ETag/If-Match ([ADR-0012](../../decisiones/0012-concurrencia-hibrida.md)),
> PLATFORM-TODO ([ADR-0031](../../decisiones/0031-deuda-de-plataforma-y-stubs-noop.md)),
> reportería nativa ([ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md)).
>
> **Dependencia clave:** el timbrado, la cancelación y la consulta de
> estatus de CFDI **NO** se implementan en este módulo. Se consumen como
> puerto `IFiscalApiClient` del módulo
> [`Millet.Integraciones.Fiscal`](../integraciones-fiscal/00-levantamiento.md)
> (fase 2 de ese módulo, ver [ADR-0038](../../decisiones/0038-fiscalapi-pac-unico.md)).
> Facturación es el primer consumidor de emisión de ese puerto.

---

## 0. Cómo leer este documento

- `[Verificado]` — confirmado con el área en el mapa funcional v0.4 o
  leído del código actual.
- `[Inferido]` — deducido por convenciones del proyecto; no confirmado.
- `[Gap]` — agujero que requiere confirmación antes de implementar.

Este documento es el **levantamiento funcional**. El diseño técnico
(dominio, esquema Postgres, puertos, CQRS, eventos, RBAC, endpoints) vive
en [`01-diseno.md`](01-diseno.md).

---

## 1. Propósito y alcance

### Qué hace este módulo

Centraliza la emisión de comprobantes fiscales digitales (**CFDI 4.0**) y
sus complementos para todas las variantes de venta de la empresa: ventas
de mostrador presenciales, ventas con material producido en Planta Conkal
(maquila), ventas de reparto, ventas de obras y proyectos, ventas de
exportación, servicios de Planta de Pintura, ventas de activos fijos,
facturación administrativa, y traslados con Carta Porte.

Administra el ciclo completo de **anticipos** (emisión, vinculación,
amortización), **notas de crédito** (por bonificación y por aplicación de
anticipo), **complementos de pago** (REPP), y la integración con los
sistemas origen (A+W, Planta Pintura, Sistema de Salidas) para la
sincronización de estados y saldos por pedido.

**El pedido a facturar tiene tres orígenes posibles**: la ingesta
automática desde **A+W** y **Planta Pintura**, y la **captura manual** en
la pantalla de Facturación —al estilo de Requisiciones (master + líneas
inline)—. La captura manual no es exclusiva de Obras o Administrativa:
aplica a cualquier comportamiento fiscal cuando no hay (o no se quiere
usar) un pedido del origen externo. Un `PedidoFacturable` capturado
manualmente fluye por el mismo flujo de emisión que uno ingestado.

> **El Sistema de Salidas NO es un origen de pedidos.** Es una integración
> entrante distinta cuyo rol es proveer el **número de pedimento de
> exportación** (cada Hoja de Salida con Contenedor se relaciona con un
> pedimento) y el packing list. Esa información **se asocia a la factura
> después de generada** (post-timbrado), no origina el pedido. Ver §7.5 y
> §12.1.

Es el módulo donde aterriza la operación fiscal del lado **emitido**:
cualquier ingreso reconocido frente al SAT pasa por aquí, y cualquier
acuerdo de descuento, devolución o aplicación de anticipo se materializa
aquí como un CFDI. Las facturas pueden emitirse a un cliente nominal o a
RFC genérico (público en general nacional `XAXX010101000`, o extranjero
`XEXX010101000`).

### Qué NO hace

- **No gestiona el master de Clientes ni el master de Productos.** Esos
  catálogos se consumen, pero su administración vive en el módulo de
  Administración (`DatosMaestros`) o en el seed inicial migrado desde SAP/A+W.
- **No procesa facturas de proveedor.** Eso vive en CxP.
- **No timbra ni cancela contra el PAC directamente.** Construye el XML y
  lo sella con la CSD, pero el timbrado, la cancelación y la consulta de
  estatus se delegan al puerto `IFiscalApiClient` de
  `Millet.Integraciones.Fiscal`.
- **No es punto de venta físico.** El cobro físico (cajón, terminal
  bancaria, conciliación de efectivo) se asume externo o como ampliación
  posterior; el módulo registra los pagos pero no opera el hardware.
- **No factura globalmente al público en general** (Factura Global
  periódica R1) en MVP. Las facturas **individuales** a RFC genérico sí se
  permiten desde el MVP; lo diferido es la consolidación periódica. La
  estructura de datos reserva el modo para no requerir refactor.
- **No realiza el cierre contable mensual.** Genera la información que
  Contabilidad necesita, pero el cierre vive en Contabilidad.
- **No mantiene el reporte unificado de Obras** (cliente + proveedores).
  Vive en un módulo aparte de Obras que jala datos de Facturación y de CxP
  vía el campo `Obra`.
- **No gestiona devolución de IVA a turistas extranjeros.** Descartado.
- **No opera zonas económicas especiales del Istmo** (Polos de
  Desarrollo). Descartado.

### Boundary con los sistemas origen (A+W, Planta Pintura, Sistema de Salidas)

Hoy existen **tres sistemas externos** que originan información que el ERP
necesita para facturar. En el futuro Planta Pintura y Sistema de Salidas
se absorberán dentro del ERP; por ahora son integraciones. **Importante:
solo dos de ellos (A+W y Planta Pintura) originan pedidos a facturar; el
Sistema de Salidas provee pedimento + packing list, no pedidos.**

**Mecanismo de integración entrante — lectura de vistas SQL (pull)
`[Verificado]`:** El ERP **lee directamente vistas de base de datos**
publicadas en el servidor on-prem de Millet, a través de **Azure Hybrid
Connection Manager**. No se reciben JSON empujados por los sistemas
origen; el ERP consulta las vistas. Esto reemplaza el modelo de push de
JSON de versiones previas.

**La integración A+W es bidireccional, vía tabla-puente que el ERP
especifica `[Verificado]`.** A+W puede mandar tres operaciones sobre un
pedido — **Alta** (primera ingesta), **Modificación** (N veces) y
**Cancelación** — escribiéndolas en una **tabla-puente de solicitudes cuyo
esquema define el ERP** (A+W solo la llena con nuestras especificaciones; es
un canal sancionado, no mutación directa de tablas internas de A+W). El ERP
consume esa cola, decide qué procede según su propio estado, y **escribe de
vuelta** en la misma tabla:

- **Correlación (claim):** el `pedido_facturable_id` del ERP. **Persiste
  siempre** — nunca se borra para señalizar (eso evita huérfanos).
- **Estado de facturación:** **binario** `SinFacturar` / `Facturado` (más
  `Cancelado`). No existe "parcial" (§8).
- **Resultado:** UUID/folio del CFDI y estatus (115 facturado, 70 liquidado).

> **Regla rectora — la factura manda:** una Modificación o Cancelación de
> A+W **solo se auto-aplica si el pedido en el ERP NO tiene CFDI**. Si ya
> está `Facturado`, se **rechaza la automática** y va a revisión manual (la
> cancelación del CFDI sigue el flujo SAT 4.0; el ERP no cancela CFDIs
> automáticamente). Matriz completa en §12.1.

| Sistema | Qué provee | ¿Origina pedido? | Cómo |
|---|---|---|---|
| **A+W** | Pedidos de mostrador, reparto, obras, exportación | **Sí** | Vista SQL leída por el ERP |
| **Planta Pintura** (programa interno) | Órdenes de servicio de recubrimiento, ya con salida de material | **Sí** | Vista SQL leída por el ERP |
| **Sistema de Salidas** | Pedimento de exportación (Hoja de Salida con Contenedor ↔ pedimento) + packing list | **No** — enriquece la factura post-timbrado | Vista SQL leída por el ERP |
| **Captura manual (ERP)** | Pedido capturado por el operador con sus líneas | **Sí** | Pantalla del módulo (estilo Requisiciones); no pasa por vistas SQL |

> **Tres orígenes de pedido `[Verificado]`:** **A+W**, **Planta Pintura** y
> **captura manual**. El operador puede crear un `PedidoFacturable` a mano
> (cliente + líneas con producto/servicio, cantidad, precio, descuento,
> retención), igual que se arma una Requisición; usa los mismos masters del
> ERP para validar datos fiscales. Ver §7.10.
>
> **El Sistema de Salidas es una integración entrante distinta** cuyo único
> rol hacia Facturación es el **pedimento** (#17 del mapa funcional): cada
> Hoja de Salida con Contenedor se relaciona con un pedimento, y el ERP
> replica esa información en cada factura de los pedidos incluidos en la
> salida. Se asocia **después de generada la factura** (§7.5, §12.1).

**Write-back confirmado (capacidad), mecanismo por afinar `[Verificado]` /
`[Gap]`:** la capacidad bidireccional está confirmada (claim al ingestar +
resultado al facturar). Lo que falta afinar (§16) es el **mecanismo
concreto** del canal de escritura — qué SP/tabla-puente expone Millet, su
contrato y manejo de errores/reintentos — y el equivalente para Planta
Pintura. (El Sistema de Salidas no requiere write-back: solo aporta el
pedimento.)

**El ERP es el master único de Cliente y Producto para todo el ERP**
(`DatosMaestros`), incluyendo sus datos fiscales (RFC, régimen, clave SAT,
retenciones), anticipos CFDI, series y sucursales. Los sistemas origen son
master de los datos de fabricación, servicio y entrega.

**Nacimiento del master desde A+W `[Verificado]`.** Los **clientes nacen
desde A+W** y los artículos llevan un atributo `origen` (los de
`origen = A+W`). Cuando se ingesta un pedido de **A+W**, el ERP reconoce si
el cliente y cada artículo ya existen en su master; **si no existen, los
inserta automáticamente leyendo las vistas de la integración A+W** (con sus
datos fiscales). **Este es el único caso de auto-provisión** del master
desde una vista. En **Planta Pintura**, el cliente y el artículo **deben
crearse previamente en el ERP**; si no existen al ingestar, el pedido va a
la bandeja de excepciones (no se auto-crean). Ver §12.1.

> **Implicación:** el `[Gap]` previo "los datos fiscales del cliente residen
> en A+W" se resuelve por la auto-provisión: el cliente nace en el ERP desde
> la vista de A+W con su RFC/régimen. Si la vista trae datos fiscales
> incompletos, el cliente se crea pero la **factura no podrá timbrarse**
> hasta completarlos (validación al emitir, no al ingestar).

---

## 2. Actores

| Actor | Rol en este módulo |
|---|---|
| **Vendedor** | No opera el módulo. Origina el pedido en A+W. Hoy carga los datos fiscales del cliente en A+W (ver pendiente de migración, §16). Registra el descuento por bonificación si aplica y el número de Obra cuando corresponde. |
| **Cajero** | Único rol de caja (mostrador y reparto son el mismo cajero). Emite facturas, recibe pagos, registra formas de pago, imprime versiones simplificadas, cierra liquidaciones de caja. En reparto, emite antes del despacho con forma de pago 99 y recibe la rendición del chofer al regreso. |
| **Caja general** | Procesa notas de crédito por bonificación; centraliza documentación concentrada diaria firmada por responsables de sucursal. |
| **Cuentas por Cobrar (CxC)** | Da seguimiento a saldos pendientes, recibe comprobantes de transferencia. **Instruye qué porción del anticipo se aplica** a cada factura; caja ejecuta esa instrucción. |
| **Ingresos** | Confirma depósitos bancarios, aplica cobros, timbra complementos de pago, refleja la liquidación en el sistema origen. |
| **Contador General (Contabilidad)** | Autoriza la facturación de venta de activos fijos antes del timbrado. Mantiene el catálogo de cuentas y el candado de períodos contables cerrados. |
| **Comercio Exterior** | Provee información para catálogos de exportación (fracciones arancelarias, INCOTERMS). No envía datos por pedido individual. |
| **Depto de Salidas / Logística Conkal** | Genera las Hojas de Salida con contenedor (cada una ligada a un pedimento) y el packing list. El ERP toma esta info del Sistema de Salidas. Determina cuándo se emite Carta Porte y con qué datos. |
| **Sistema A+W** | Sistema origen. Publica pedidos en vistas SQL que el ERP lee. |
| **Programa Planta Pintura** | Sistema origen interno. Publica órdenes de servicio de recubrimiento (con salida ya realizada) en vistas SQL. |
| **Sistema de Salidas** | Sistema origen. Publica Hojas de Salida y su relación con pedimentos en vistas SQL. |
| **PAC — FiscalAPI** (vía `Integraciones.Fiscal`) | Actor externo. Recibe los datos del CFDI, genera/sella/timbra el XML contra el SAT en una sola operación, regresa UUID, sello del SAT, fecha de timbrado. Soporta complementos de Pago, Carta Porte 3.1, CCE y otros. Maneja cancelaciones SAT 4.0. **El módulo Facturación no lo invoca directo — pasa por el puerto `IFiscalApiClient`.** |
| **SAT** | Actor externo. Aprueba/rechaza cancelaciones, valida CFDIs, mantiene catálogos. |

---

## 3. Documentos fiscales que emite el módulo

| Tipo CFDI | Cuándo se emite | Complementos típicos |
|---|---|---|
| **Ingreso (I) — Factura de venta** | Venta de bienes o servicios. Documento principal. Cliente nominal o RFC genérico. | CCE (exportación), Carta Porte 3.1 (traslado con venta), Vehículo usado, Certificado de destrucción según corresponda |
| **Ingreso (I) — Factura de anticipo** | Cobro adelantado de pedidos de maquila o de obra. Serie dedicada. Siempre nominal. | Ninguno típico |
| **Egreso (E) — NC por amortización** | Aplicación de anticipo contra factura final. Autogenerada tras timbrar la factura final. Relaciona la factura de anticipo Y la factura final. | Ninguno |
| **Egreso (E) — NC por bonificación** | Descuento posterior a la facturación (estrategia comercial: no se muestra en la factura). | Ninguno |
| **Egreso (E) — NC por devolución** | Devolución de mercancía (afecta inventario). **Fuera de alcance MVP** salvo confirmación. | Ninguno |
| **Traslado (T)** | Movimiento de mercancía propia sin venta. | Carta Porte 3.1 obligatoria |
| **Pago (P) — REPP** | Confirmación de cobro de facturas PPD. | Pago 2.0 |

> **Nota:** Las "facturas de venta relacionadas (No Timbrado)" del
> levantamiento de Obras quedan **diferidas a Fase 2** junto con la cuenta
> puente "Obras Entregadas No Facturadas". Cuando se incorporen, serán un
> tipo de documento interno no fiscal.

**Complementos fiscales soportados** (lista confirmada por el área; no usan
addendas comerciales):

- **Carta Porte** 3.1 (traslados)
- **Comercio Exterior** (CCE, exportación)
- **Recepción de pagos** (REPP / Pago 2.0)
- **Certificado de destrucción** (destrucción de mercancía / activos)
- **Vehículo usado** (venta de vehículos usados)
- **Recibo de pago de nómina** (probablemente operado desde RH; registrado
  aquí para completitud)

El adaptador de FiscalAPI los soporta nativamente (§12.2).

---

## 4. Entidades del dominio

### 4.1 Comprobante (raíz)

Entidad base de todo CFDI emitido. Atributos comunes:

- Tipo (I, E, T, P)
- Serie y folio (asignados internamente, una serie por sucursal-tipo)
- Sucursal emisora, caja emisora, usuario emisor
- Cliente receptor (snapshot de datos fiscales al momento de emisión)
- Régimen fiscal del emisor (puede haber más de uno — multi-RFC)
- Uso CFDI (catálogo SAT)
- Método de pago (PUE / PPD)
- Forma de pago (catálogo SAT)
- Moneda y tipo de cambio (si no es MXN)
- Subtotal, descuentos, impuestos trasladados, retenciones, total
- **Estado de timbrado:** `Borrador` / `Timbrado` / `Cancelado` /
  `Cancelación pendiente`. El sellado (sello del emisor con CSD) y el
  timbrado (validación del PAC + sello del SAT + UUID) ocurren en una sola
  operación contra FiscalAPI, por lo que **no se modela un estado
  `Sellado` intermedio** visible al usuario.
- UUID (folio fiscal del SAT vía FiscalAPI)
- Sello del CFDI, sello del SAT, fecha de timbrado, RFC del PAC
- XML completo y PDF generado
- Período contable de la fecha de emisión. **Candado:** el módulo no
  permite emitir, cancelar ni modificar ningún comprobante con fecha de un
  período (mes) ya cerrado desde Contabilidad. Validación obligatoria.
- Bandera "Enviado por correo al cliente" + bitácora de envío

### 4.2 Factura de venta

Especialización de Comprobante tipo I. Atributos propios:

- Pedido de origen (de A+W, Planta Pintura, o directo del ERP; puede ser
  nulo en facturación administrativa u obras directas)
- **Canal de venta** (eje organizacional): `Tienda Cancún` / `Tienda
  Circuito` / `Tienda Chichí Suárez` / `CC MID` / `CC Q Roo` / `CC Mpios` /
  `Proyectos y Obras` / `Exportación` / `Planta Pintura` / `Administración`
- **Comportamiento fiscal** (eje fiscal, ortogonal al canal): `Mostrador
  inmediato` / `Con anticipo` / `Exportación con CCE` / `Traslado con
  Carta Porte` / `Venta de activo fijo` / `Administrativa`
- Receptor: cliente nominal **o RFC genérico** (`XAXX010101000` nacional /
  `XEXX010101000` extranjero)
- Líneas de venta (productos o servicios), cada una con su definición de
  retención si el artículo la lleva
- Anticipos aplicados (lista de UUIDs)
- NCs relacionadas (lista de UUIDs)
- Número de Obra (snapshot: `obra_id` numérico + `obra_nombre` texto), **no
  aparece en el XML**
- Datos aduaneros a nivel de línea (nacional o exportación): número de
  pedimento, fecha del documento aduanero, identificación individual de
  mercancía. **Van en el XML normal de la factura, no en el complemento
  CCE** (§4.5 y §7.5)
- Datos de Carta Porte: nulos por default (ver entidad CartaPorte, §4.6)
- Datos de CCE: nulos por default; si es exportación con complemento,
  viven en la entidad CCE (§4.5)
- Bandera "Factura agrupada" — si consolida múltiples pedidos del mismo
  cliente
- Autorización previa requerida (bool + FK a autorización): true para
  venta de activo fijo (Contador General)

### 4.3 Factura de anticipo

Especialización de Comprobante tipo I con serie dedicada. Atributos propios:

- Tipo Anticipo: `CLIENTES_MXP` / `CLIENTES_USD`
- Artículo facturado: servicio "anticipo de clientes" (clave SAT 84111506
  o equivalente). El tipo de comprobante sigue siendo Ingreso.
- Pedido ligado (opcional pero típico)
- Cliente (obligatorio **nominal** — los anticipos no se emiten a RFC
  genérico porque no podrían aplicarse vía relación 07)
- Monto cobrado (puede diferir del monto del CFDI si se cobra parcialmente,
  §6)
- Estado del anticipo: `Abierto` / `Amortizado` / `Cancelado`
- Lista de facturas vinculadas
- Lista de NCs de amortización emitidas
- Saldo amortizable derivado: `Total Cobrado − Total Amortizado`

> **Series configurables `[Verificado]`:** las series CFDI (anticipos y el
> resto) son **parte de la configuración** (catálogo `Serie` por
> sucursal-tipo, editable en settings), **no** valores hardcodeados. El
> **seed inicial** arranca con los valores conocidos: anticipos = `FANT`
> (alineado con CxP; este doc usaba `ANT`). Cambiar una serie es editar el
> catálogo, no el código.

### 4.4 Nota de crédito

Especialización de Comprobante tipo E. Atributos propios:

- Motivo: `Amortización de anticipo` / `Bonificación` / `Devolución`
- Clave SAT de tipo de relación (07 anticipo, 01 bonificación/NC del
  original, 03 devolución)
- Factura(s) origen relacionadas (UUIDs). **En amortización relaciona
  tanto la factura de anticipo como la factura final.**
- Anticipo origen (si motivo = Amortización)
- Importe que afecta inventario: 0 para Amortización y Bonificación,
  distinto de 0 para Devolución

### 4.5 Complemento Comercio Exterior (CCE)

Asociado a una Factura de venta cuando el comportamiento fiscal es
Exportación con complemento. Atributos:

- Tipo de operación: `A1` (definitiva), temporal, etc.
- INCOTERM
- Tipo de cambio DOF del día hábil anterior
- Datos del receptor extranjero (TAX-ID, domicilio, residencia fiscal)
- Datos del destinatario (cuando difiere del receptor)
- Por línea: fracción arancelaria, unidad aduanera (típicamente kg), valor
  unitario en USD, cantidad aduanera (con conversión)
- Bandera "Aplica IVA 0%" vs. "No objeto de IVA"

> **Aclaración del área:** El número de pedimento, la fecha del documento
> aduanero y la identificación individual de mercancías **NO son parte del
> CCE**, sino del XML normal de la factura, y aplican tanto a ventas
> **nacionales como de exportación** (productos de importación en su
> primera venta, art. 29-A CFF). Por eso viven a nivel de línea de la
> Factura de venta (§4.2), no aquí.

### 4.6 Carta Porte (entidad propia)

**Decisión clave:** Carta Porte es una entidad **independiente** de la
Factura. Un pedido A+W puede generar N Carta Portes (una por tramo con
vehículo u operador distinto). Una Carta Porte puede ser un CFDI tipo T
(Traslado) o tipo I (Ingreso, cuando se factura la venta en el mismo
tramo).

Atributos:

- Tramo: origen (sucursal o cliente), destino (sucursal o cliente)
- Distancia recorrida
- Vehículo (placa, tipo, peso máximo)
- Operador (nombre, RFC, licencia)
- Mercancías transportadas (peso y cantidad)
- Pedido A+W relacionado (uno o varios si la carga consolida)
- Carta Porte previa relacionada (continuación de tramo)
- Tipo del CFDI generado: T o I
- Fecha/hora de salida, fecha/hora estimada de llegada

> **Caso operativo:** Material sale de Conkal con Carta Porte 1 (camión 1,
> operador Juan, destino Cancún) como CFDI tipo T. En Cancún se transfiere
> al camión 6 con operador Pedro hacia el cliente final en Cozumel. Se
> emite **Carta Porte 2** con datos del nuevo tramo, referenciando la 1
> como antecedente. Comparten pedido A+W y cliente final pero son CFDIs
> distintos. La UI ofrece "Crear siguiente tramo" que prellena mercancía,
> peso, pedido, cliente final, y pide solo los datos del tramo nuevo.

### 4.7 Complemento de Pago (REPP)

Para facturas con método de pago PPD. Atributos:

- Factura(s) que paga (UUIDs, una a varias)
- Importe pagado, moneda, tipo de cambio si aplica
- Forma de pago real (catálogo SAT, no "Por definir")
- Fecha de pago
- Datos bancarios: cuenta ordenante, cuenta beneficiaria, referencia
- Número de parcialidad por factura cubierta

### 4.8 Relación CFDI

Entidad de unión entre dos CFDIs. Atributos:

- UUID origen, UUID destino
- Tipo de relación (c_TipoRelacion): 01 NC del original, 04 sustitución,
  07 aplicación de anticipo, etc.

Permite reconstruir la cadena completa de un pedido: anticipos → factura
final → NC de amortización → NC de descuento → complementos de pago.

### 4.9 Ticket de mostrador (reservado, Fase 2)

Modelo reservado para facturación online. No se implementa en MVP pero se
modela desde el inicio para no requerir refactor.

Atributos previstos:

- Modo de facturación: `Nominal inmediata` / `Pendiente de ticket` /
  `Incluida en global`
- Código de referencia de portal
- Plazo de facturación nominal (fecha límite)
- Asociación al CFDI emitido

En MVP, todas las ventas de mostrador operan en modo `Nominal inmediata` y
el cliente proporciona datos fiscales en caja.

---

## 5. Catálogos

### 5.1 Catálogos internos del ERP

Propiedad del ERP. El módulo de Facturación los consume.

| Catálogo | Master | Notas |
|---|---|---|
| Cliente | ERP (`DatosMaestros`) — master único del ERP | Datos fiscales (RFC, régimen, CP fiscal), domicilios de entrega, días de crédito, uso CFDI/método/forma de pago default. **Multimoneda** (MXN y USD) y **multi-impuesto** (IVA 16%, 0%, retenciones). **Nacen desde A+W**: auto-provisionados desde la vista al ingestar un pedido A+W si no existen (§12.1). |
| Sucursal | ERP | Define series CFDI propias y cajas asociadas. RFC emisor asociado (multi-RFC). |
| Caja | ERP | Asociada a usuario o equipo físico |
| Serie | ERP | Una serie por (sucursal, tipo de comprobante) |
| Producto y servicio | ERP (`DatosMaestros`) — master único del ERP | **Clave SAT (c_ClaveProdServ)**, **clave unidad SAT (c_ClaveUnidad)**, cuenta contable asociada, **retención por artículo**, y atributo **`origen`** (`A+W` / `ERP` / …). Sin clave SAT no se factura. Los de `origen = A+W` se auto-provisionan desde la vista al ingestar (§12.1); los de Planta Pintura/Administrativa se crean en el ERP. |
| Activo Fijo | ERP (o módulo Activos Fijos) | Productos marcados como activo fijo. Para Venta de Activos: valida que el producto esté dado de alta como activo fijo, con valor en libros y depreciación. |
| ConceptoContable | ERP | Capa de indirección entre eventos del módulo y cuentas contables reales (§6.5) |
| Vehículo | ERP | Para Carta Porte |
| Operador (chofer) | ERP | Para Carta Porte (RFC, licencia) |

### 5.2 Catálogos del SAT

`c_FormaPago`, `c_MetodoPago`, `c_UsoCFDI`, `c_RegimenFiscal`,
`c_TipoRelacion`, `c_TipoDeComprobante`, `c_TasaCuota`, `c_ObjetoImp`,
`c_Moneda`, `c_Pais`, `c_ClaveProdServ`, `c_ClaveUnidad`,
`c_FraccionArancelaria`, `c_INCOTERM`, `c_TipoOperacion` (CCE).

Dos fuentes posibles:

- **FiscalAPI** los expone vía su API de catálogos (vigentes contra el SAT).
- **Espejo local** sincronizado periódicamente desde FiscalAPI (job
  nocturno).

**Recomendación:** espejo local para latencia baja y resiliencia ante
caída de FiscalAPI, con sincronización nocturna desde su API. La fuente del
espejo no es el SAT directo, es FiscalAPI. (Reusar los catálogos SAT del
proyecto `Compartido` donde ya existan — ver §12.2.)

### 5.3 Datos provenientes de A+W

A+W provee al ERP la información de pedidos listos para facturar (vía
vistas SQL, §12.1). **No es master de clientes ni de productos en el ERP** —
solo aporta las referencias (`numero_cliente`, `producto_id`) que el ERP
resuelve contra sus masters. A+W mantiene su propio catálogo de estatus; el
ERP recibe pedidos "listos para facturar" y le notifica de regreso
transiciones a 115 (facturado) y 70 (liquidado).

> **Pendiente `[Gap]`:** Catálogo completo de estatus A+W relevantes. El
> área indicó que lo envía; aún no recibido. Sin él no se modelan
> definitivamente las transiciones. Estatus conocidos: **15** (confirmado),
> **69** (listo en APT), **70** (liquidado), **115** (facturado).

---

## 6. Anticipos: ciclo y reglas

Corazón del módulo y la pieza con mayor complejidad fiscal.

### 6.1 Tipos de anticipo

Solo dos valores válidos:

- `ANTICIPO_CLIENTES_MXP` — moneda MXN
- `ANTICIPO_CLIENTES_USD` — moneda USD

La diferencia es exclusivamente la cuenta contable de destino (§6.5). El
comportamiento operativo y fiscal es idéntico.

### 6.2 Tres momentos del ciclo

**Momento 1 — Emisión de la factura de anticipo.** En caja cuando el
cliente paga adelantado un pedido de maquila o un anticipo de obra. Serie
de anticipos (ver nota `FANT`, §4.3), clave SAT 84111506 ("Servicios de
facturación") o equivalente, descripción "Anticipo de clientes
nacionales/extranjeros". Genera automáticamente un registro en el Control
de Anticipos con saldo inicial = monto cobrado.

**Momento 2 — Vinculación con la factura final.** Al emitir la factura
final del pedido, se marca "Relación de anticipo" y se seleccionan uno o
más anticipos del mismo cliente con saldo disponible. La factura final
lleva en su XML la relación CFDI tipo `07 — Aplicación de anticipo`.
**Esta vinculación no afecta el saldo del anticipo aún.**

**Momento 3 — NC de amortización.** Inmediatamente después del timbrado
exitoso de la factura final, el sistema autogenera una NC tipo Egreso con:

- Motivo: Amortización de anticipo
- Relación CFDI tipo 07 referenciando **tanto el UUID de la factura de
  anticipo como el UUID de la factura final**
- Importe: el monto que se amortiza (parcial o total)

La NC reduce el saldo del anticipo. **La autogeneración debe ocurrir en la
misma transacción atómica que el timbrado de la factura final** para evitar
facturas finales sin NC asociada (estado inconsistente).

> **Decisión clave del área (A.1) `[Verificado]`:** La amortización se hace
> siempre vía NC autogenerada, no como descuento directo en la factura
> final. SAP B1 operaba con amortización directa, lo cual causa fricción en
> auditorías. El ERP nuevo adopta el método de NC sin excepción.

### 6.3 Regla crítica: saldo amortizable

El saldo amortizable NO es el monto del CFDI de anticipo. Es:

```
Saldo amortizable = Monto cobrado − Monto amortizado
```

Donde "Monto cobrado" se confirma vía:
- PUE: el cobro registrado en caja al emitir el anticipo.
- PPD: la suma de los complementos de pago timbrados que referencian el
  anticipo.

Un anticipo emitido pero no cobrado totalmente no puede amortizarse por el
total emitido. El sistema valida esta restricción al permitir la
vinculación (M2) y la NC (M3).

### 6.4 Reglas de vinculación y aplicación

- Un anticipo puede vincularse a **múltiples** facturas finales
  (amortización parcial sucesiva).
- Una factura final puede vincular **múltiples** anticipos del mismo
  cliente.
- Si el pedido que originó un anticipo se cancela, el anticipo puede
  reaplicarse al **pedido sustituto** del mismo cliente.
- Los anticipos **siempre** se emiten nominales con datos fiscales
  completos. No se permiten a RFC genérico (no podrían amortizarse vía
  relación 07).
- **CxC instruye qué porción del anticipo se aplica** a cada factura; caja
  ejecuta. La decisión del monto es de CxC, no de caja.
- El crédito de un anticipo cobrado se refleja en el sistema origen en el
  pedido relacionado para que el vendedor vea el saldo.

### 6.5 Mapeo a cuentas contables

Para no acoplar Facturación a códigos de cuenta aún no definidos por
Contabilidad, se introduce una capa de indirección:

- Cada evento contable se etiqueta con un `ConceptoContable` (código lógico
  estable, ej. `ANTICIPO_CLIENTE_MXP`, `BONIFICACION_DESCUENTO`).
- Tabla de mapeo `ConceptoContable → CuentaContable` en el catálogo de
  cuentas (módulo Administración cuando exista).
- En MVP, el mapeo se siembra con placeholders (`TBD-ANT-MXP`,
  `TBD-ANT-USD`) y flag `requiere_codigo_definitivo = true`.
- La integración con Contabilidad bloquea cualquier asiento que apunte a un
  código TBD.

Conceptos contables conocidos hoy:

- `ANTICIPO_CLIENTE_MXP`, `ANTICIPO_CLIENTE_USD`
- `BONIFICACION_DESCUENTO`, `DEVOLUCION_VENTA`
- `INGRESO_VENTA_NACIONAL`, `INGRESO_VENTA_EXPORTACION`
- `IVA_TRASLADADO_16`, `IVA_TRASLADADO_0` (exportación)
- `GANANCIA_CAMBIARIA`, `PERDIDA_CAMBIARIA`
- `UTILIDAD_VENTA_ACTIVO`, `PERDIDA_VENTA_ACTIVO`
- `ACTIVO_FIJO`, `DEPRECIACION_ACUMULADA`

**Diferencia cambiaria automática:** Al aplicar un pago en moneda
extranjera, el sistema calcula la diferencia entre el TC de la factura y el
del cobro, y registra el resultado en `GANANCIA_CAMBIARIA` o
`PERDIDA_CAMBIARIA`. La misma lógica aplica del lado proveedores (CxP).

Cuando Contabilidad entregue los códigos definitivos, es una migración de
pocas filas y cero cambios de código.

### 6.6 Estados del anticipo

| Estado | Significado | Transición |
|---|---|---|
| `Abierto` | Saldo amortizable > 0 | Estado inicial al cobrar |
| `Amortizado` | Saldo amortizable = 0 | Cuando la última NC lleva el saldo a cero |
| `Cancelado` | El CFDI del anticipo fue cancelado | Excepción operativa; bloquea aplicación |

### 6.7 Pantalla "Control de Anticipos"

Dos modos:

**Vista Resumen.** Filtros: cliente, fechas, obra, estado. Columnas: folio,
proyecto/obra, monto total con IVA, estado. Sumatorias. Exportable a PDF y
Excel.

**Vista Detallada (estado de cuenta por cliente).** Para un cliente:
- Cada anticipo con su monto inicial
- Facturas vinculadas (indicador Timbrado / No timbrado)
- NCs aplicadas (indicador Timbrado / No timbrado)
- Total facturado, total amortizado, saldo del anticipo
- Número de pedido A+W relacionado

---

## 7. Canales y comportamientos de facturación: flujos

Modelado en **dos ejes ortogonales**:

- **Canal de venta** (organizacional): de dónde viene la venta. Tienda
  Cancún, Tienda Circuito, Tienda Chichí Suárez, CC MID, CC Q Roo, CC
  Mpios, Proyectos y Obras, Exportación, Planta Pintura, Administración.
- **Comportamiento fiscal**: cómo se comporta frente al SAT. Mostrador
  inmediato, con anticipo, exportación con CCE, traslado con Carta Porte,
  venta de activo fijo, administrativa.

Un mismo canal puede tener distintos comportamientos.

### 7.1 Mostrador (venta presencial)

**Origen.** Cliente presencial. El vendedor levanta el pedido en A+W. Si
hay descuento autorizado, lo registra como descuento por bonificación
(monto IVA incluido). Si hay Obra, la registra.

**Cobro y emisión.** El cliente pasa a caja con la confirmación impresa. El
cajero:

1. Busca el pedido por número y lo carga.
2. El sistema calcula `Importe a cobrar = Total del pedido − Bonificación`.
3. La pantalla muestra los comentarios del pedido en un campo **separado
   del XML** — son indicaciones de transporte/piso, nunca llegan al CFDI.
4. El cajero selecciona una o más formas de pago en una sola pantalla,
   indicando el importe de cada una.
5. Registrado el cobro, se emite la factura: el ERP envía los datos a
   FiscalAPI (vía puerto), que sella y timbra en una operación y devuelve
   el UUID.
6. Si hay bonificación > 0, se emite **inmediatamente** una NC por
   bonificación, justificación documentada en caja general. La bonificación
   va por NC posterior, **no** en la factura (estrategia comercial).
7. Si hubo más de una forma de pago, o forma PPD, se timbra el complemento
   de pago.

**Salidas.** Factura PDF + XML enviada automáticamente al correo del
cliente. Impresa: bilingüe (ES/EN) o versión simplificada en español
(térmica). La NC, si existió, también se envía.

**Casos especiales.**

- **RFC genérico.** Si no requiere nominal, se emite a `XAXX010101000`.
  Permitido desde MVP.
- **Factura agrupada.** Múltiples pedidos del mismo cliente en una factura.
- **Sucursal y serie.** Por el usuario de sesión o la ubicación física del
  equipo. Cada caja tiene su serie.
- **Liquidación de caja.** Reporte que cruza facturado vs. cobrado por forma
  de pago, bajo demanda.
- **No se aceptan monedas extranjeras en efectivo.**

### 7.2 Maquila en mostrador

**Diferencia con Mostrador.** El material se produce en Planta Conkal; el
pedido no es de entrega inmediata. El cobro inicial es vía factura de
**anticipo**.

**Flujo.**

1. Cliente pasa a caja con la confirmación indicando el anticipo a cobrar.
2. El cajero emite **Factura de anticipo** (serie de anticipos) tipo
   `ANTICIPO_CLIENTES_MXP`/`USD`. Cobra. El crédito se refleja en el origen.
3. El sistema crea el registro en Control de Anticipos.
4. Cuando el pedido llega a 69 (listo en APT), CxC contacta al cliente para
   el saldo.
5. El cliente paga el saldo (transferencia). Ingresos confirma el depósito.
6. Se emite la **factura final** con "Relación de anticipo" y los UUIDs
   seleccionados. El XML lleva la relación CFDI 07.
7. Inmediatamente después del timbre, se autogenera la **NC por
   amortización** por el monto aplicado.
8. Complemento de pago si aplica (PPD).
9. El pedido pasa a estatus 70 en A+W para entrega.

### 7.3 Reparto

**Origen.** Pedido en A+W, confirmado, pasa a estatus 15 para que el taller
lo programe.

**Flujo.**

1. Taller arma rutas y asigna vehículos. Pedidos a estatus 69.
2. Almacenista verifica cargas y emite albaranes.
3. Caja genera las facturas. **Sugerencia adoptada:** al pasar a 69, se
   ofrece generación automática con check box comparativa contra albarán.
4. Cada factura con `Forma de pago CFDI = 99 (Por definir)` y método PPD.
5. El chofer recibe versiones simplificadas impresas y la "Hoja de Salida"
   con montos reales a cobrar.
6. Al regreso, el chofer **rinde cuentas en caja** ("liquidación de ruta"):
   entrega lo cobrado y el cajero concilia contra lo que salió a cobrar.
7. El cajero aplica los cobros y emite el **complemento de pago** por cada
   factura, con la forma de pago real.
8. La aplicación es por cliente; la UI resuelve múltiples clientes en una
   pantalla.

> "Liquidación del chofer" = rendición de cuentas de ruta. No es un pago al
> chofer.

### 7.4 Obras y proyectos

**Dos rutas de origen.** Desde pedido A+W con número de Obra capturado; o
como factura directa desde el ERP (servicios o inventariables).

**Particularidades.**

- El campo Obra se arrastra del origen a la pantalla de facturación. **No
  aparece en el XML** — es interno para Obras y reportes.
- **Formato:** A+W lo envía en dos campos — `obra_id` (numérico) y
  `obra_nombre` (texto). En la vista SQL serán dos columnas.
- Una obra puede tener múltiples anticipos del mismo cliente.
- Las facturas pueden ser parciales (estimaciones). Cada estimación es una
  factura nueva referenciada al mismo número de Obra.
- NCs por bonificación en caja general con documento concentrado diario
  firmado por el responsable de sucursal.
- Cobros y créditos se replican al origen para transicionar a 70 al
  liquidar.

**Cuenta puente "Obras Entregadas No Facturadas".** Esquema de "factura de
venta relacionada (No Timbrado)" para obras entregadas pero no estimadas.
**Diferido a Fase 2.** Requiere definición conjunta con Contabilidad y
costeo.

### 7.5 Exportación

**Origen.** Pedido A+W con cliente extranjero y datos de exportación
definitiva (A1) o temporal.

**Campos adicionales y su origen real:**

| Responsabilidad | Datos |
|---|---|
| Vendedor (en A+W) | Domicilio del receptor, residencia fiscal, TAX-ID, INCOTERM |
| Comercio Exterior | Información para **catálogos** (fracciones, INCOTERMS). No por pedido. |
| Depto de Salidas / Sistema de Salidas | Packing list. Cada Hoja de Salida con contenedor ↔ un pedimento. |
| Sistema ERP | TC DOF del día hábil anterior, conversión de unidades (a kg), llenado del CCE, **replicación del pedimento** en cada factura. |

**Número de pedimento — flujo particular `[Gap secuencia]`:** el pedimento
**se incluye después de generada la factura**. El ERP toma la info del
Sistema de Salidas (Hoja de Salida ↔ pedimento) y la replica en cada
factura asociada. El campo de pedimento se completa en un segundo momento;
ver pendiente de secuencia (§16).

**Reglas fiscales.**

- RFC receptor: genérico `XEXX010101000` para extranjeros (permitido).
- IVA 0% en exportaciones definitivas con CCE.
- Pedimento, fecha del documento aduanero e identificación de mercancías en
  el **XML normal** (no en CCE); aplican a nacional y exportación de
  productos de importación en primera venta (art. 29-A CFF).
- PDF **bilingüe (ES/EN)** para aduanas.

**Carta Porte en exportación.** La mayoría de las exportaciones marítimas
usan carga terrestre Conkal → Puerto Progreso. **No se emite Carta Porte en
ese tramo.** Sucursales que entregan fuera de su municipio sí emiten Carta
Porte (§7.6).

### 7.6 Carta Porte (transversal)

No es un "tipo de venta" sino un complemento que puede acompañar CFDIs tipo
I o T.

**Cuándo aplica.** Traslados fuera del municipio de origen o por carreteras
federales; Logística Conkal entregando en territorio nacional fuera de
Conkal.

**Cuándo no aplica.** Traslados locales dentro del mismo municipio o zona
metropolitana sin tramos federales.

**Tipos.**

- **CFDI tipo T con Carta Porte:** Millet traslada mercancía propia entre
  ubicaciones.
- **CFDI tipo I con Carta Porte:** cuando la entrega coincide con la
  facturación de la venta.

**Regla clave.** Cada cambio de vehículo u operador a media ruta requiere
un CFDI con Carta Porte **independiente**. No se reutiliza un CFDI
cambiándole datos (§4.6).

**Versión.** Carta Porte **3.1** (vigente desde noviembre de 2023).

### 7.7 Planta de Pintura

**Origen distinto a A+W.** Los servicios provienen del programa interno
Planta Pintura. Allí se genera el pedido, la orden de trabajo y la salida
del material. Realizada la salida, los datos se transmiten al ERP vía la
vista SQL.

**Flujo.**

1. Planta Pintura realiza la salida; la orden queda en la vista.
2. El ERP la localiza como orden de venta. El operador valida color,
   metros²/piezas, importe unitario y total.
3. Se completan datos fiscales faltantes: forma y método de pago, uso CFDI.
4. Se valida la sucursal de emisión.
5. Se timbra vía FiscalAPI.

**Ejemplo confirmado (FPIN 312162):** recubrimiento con pintura en polvo,
artículo de servicio (clave SAT 73181119), unidad MTK (m²), 94 m², Obra y
color en comentarios, método PPD, forma 99. CFDI tipo Ingreso.

**Catálogo:** cliente y artículo de Planta de Pintura **deben existir
previamente en el master del ERP** — la ingesta de Planta Pintura **no
auto-provisiona** (a diferencia de A+W). En la práctica los clientes de
Planta de Pintura se dan de alta también en A+W Business, así que típicamente
ya nacieron en el ERP por la vía A+W; pero el flujo de Planta Pintura los
exige presentes y manda a excepción si faltan, no los crea desde su vista.

### 7.8 Venta de Activos Fijos

**Aplica cuando** se vende un activo fijo (maquinaria, vehículo, equipo).

**Reglas y flujo.**

1. El producto debe estar **dado de alta como activo fijo**. El sistema lo
   valida antes de permitir la factura.
2. Antes de timbrar, requiere **autorización del Contador General**.
3. Si el activo fue de importación, su **primera venta** debe incluir el
   pedimento y la fecha de importación (XML normal).
4. CFDI tipo **Ingreso**. Con IVA si aplica.
5. Al timbrar, genera los **asientos de baja del activo**:
   - Cancela el activo y su depreciación acumulada.
   - Reconoce utilidad o pérdida:
     - Utilidad: `Clientes/Bancos` (cargo) / `Activo fijo` + `Utilidad en
       venta` (abono).
     - Pérdida: `Clientes/Bancos` + `Pérdida en venta` (cargo) / `Activo
       fijo` (abono).
   - Conceptos `ACTIVO_FIJO`, `DEPRECIACION_ACUMULADA`,
     `UTILIDAD_VENTA_ACTIVO`, `PERDIDA_VENTA_ACTIVO`.

> **Dependencia `[Gap]`:** requiere catálogo/módulo de Activos Fijos con
> valor en libros y depreciación al día. Si no existe en MVP, definir un
> mínimo (catálogo de activos con saldos). Ver §16.

### 7.9 Facturación Administrativa

**Aplica cuando** se factura un producto/servicio que **no pertenece a las
familias de comercialización**.

**Características.**

- Artículos inventariables o servicios, ligados al catálogo de productos y
  a una **cuenta contable** específica.
- Cada artículo define si lleva **retención** y cuál.
- Canal `Administración`. No proviene de A+W ni Planta Pintura; se captura
  directo en el ERP.

**Flujo.** Captura directa: selección del cliente (nominal o genérico),
artículos administrativos (con cuenta contable y retención), datos
fiscales, y timbrado vía FiscalAPI.

### 7.10 Captura manual de pedido (transversal a todos los canales)

**No es un canal distinto** sino una **forma de originar el pedido** que
convive con la ingesta automática. El operador arma un `PedidoFacturable` a
mano, igual que se arma una Requisición: encabezado (cliente, canal de
venta, comportamiento fiscal, moneda, Obra opcional) + **líneas inline**
(producto/servicio del master, cantidad, precio, descuento, retención).
Administrativa (§7.9) y Obras directas (§7.4) son los casos más comunes,
pero la captura manual aplica a **cualquier** comportamiento fiscal cuando
no hay pedido del origen externo (o se decide no usarlo).

**Características.**

- Mismo agregado `PedidoFacturable` que la ingesta, con `origen = Manual`.
  **No** pasa por vistas SQL ni Hybrid Connection.
- Las líneas se capturan **inline** (sin modal), patrón
  `LineaInlineForm` de Requisiciones (memoria
  [feedback_inline_no_modal_para_items]).
- Validación contra los mismos masters del ERP (Cliente con datos fiscales,
  Producto con clave SAT y retención). Si falta clave SAT/RFC, no se
  factura — misma regla que la ingesta.
- No genera entradas en la **bandeja de excepciones** (la captura es
  interactiva; los errores se muestran en el formulario).
- Soporta anticipos, NCs, complementos y todos los comportamientos fiscales
  igual que un pedido ingestado.
- No requiere `write-back` a ningún origen externo (no hay origen que
  actualizar).

**Flujo.** Nuevo pedido manual (Sheet) → captura de encabezado + líneas
inline → guardar como `PedidoFacturable` (estado `Importado`, origen
`Manual`) → emisión por el flujo estándar (§7.1–§7.9 según el comportamiento
fiscal elegido).

---

## 8. Facturación parcial — por unidades facturables discretas

> **Decisión `[Verificado]` (supera la versión previa de esta sección):**
> **el ERP NO modela un estado "parcialmente facturado".** Cada
> `PedidoFacturable` es **binario**: `SinFacturar` → `Facturado`. La
> facturación "parcial" de una obra o embarque se resuelve porque **A+W
> libera unidades facturables discretas** — cada estimación de obra, cada
> contenedor, cada envío es un **`PedidoFacturable` independiente** ligado
> por número de pedido/Obra. El control de "piezas pendientes" (cantidad
> ordenada vs. liberada) **vive en A+W**, que decide qué está listo para
> facturar; el ERP solo factura lo que A+W libera.

**Implicaciones.**

- No hay estado `ParcialmenteFacturado` ni "saldo de piezas" en el ERP.
- Cada unidad facturable se ingesta como su propio `PedidoFacturable` y se
  factura completa. Una obra con 3 estimaciones = 3 pedidos facturables (3
  facturas), ligados por `obra_id`.
- **Anticipos:** CxC instruye qué porción del anticipo se aplica a **cada**
  unidad facturable (proporcional o monto absoluto) y caja ejecuta; el saldo
  del anticipo se mantiene coherente entre todas las unidades del mismo
  cliente/obra (§6).
- **No sobrefacturar:** como A+W controla qué libera, el ERP no recibe la
  misma unidad dos veces (idempotencia por `ingesta_control`, §12.1).

---

## 9. Notas de crédito

### 9.1 NC por amortización de anticipo

Autogenerada tras timbrar la factura final (§6.2).

### 9.2 NC por bonificación

Emitida desde caja general cuando hay un descuento autorizado:

1. La factura final ya está timbrada con el total bruto.
2. El sistema sugiere la NC con el importe de la bonificación (IVA incluido)
   y el UUID de la factura origen.
3. Caja general verifica la justificación (firma del responsable, documento
   concentrado del día).
4. Autorizada, se timbra la NC con relación CFDI 01.
5. Se refleja en el origen como crédito al pedido, reduciendo el saldo.

> **Por qué va por NC posterior y no en la factura:** estrategia comercial
> deliberada — no exhibir descuentos altos para evitar guerra de precios.
> Reemplaza la recomendación previa de mostrar el descuento en la factura.

### 9.3 NC por devolución

Afecta inventario. **Diferida fuera del MVP** salvo confirmación.

### 9.4 Reglas comunes

- Toda NC se relaciona obligatoriamente a una factura origen.
- Toda NC sigue el receptor de la factura origen.
- No puede emitirse si la factura origen está cancelada.
- Se envían automáticamente al correo del cliente igual que las facturas.

---

## 10. Cancelación de CFDI

Conforme al esquema SAT 4.0:

- **Motivo de cancelación** (catálogo SAT): 01 errores con relación, 02
  errores sin relación, 03 no se llevó a cabo la operación, 04 operación
  nominativa relacionada en factura global.
- **Folio fiscal sustituto** (cuando motivo = 01).
- **Aceptación del receptor** para CFDIs > $1,000 MXN. Si no responde en 3
  días hábiles, aceptada tácitamente.
- **Plazo:** dentro del ejercicio fiscal en curso.

El módulo administra:

- Solicitudes de cancelación con motivo y, si aplica, UUID sustituto.
- Estado: `Solicitada` / `En proceso` / `Aceptada` / `Rechazada` /
  `Vencida (sin respuesta)`.
- Bitácora de comunicación con el PAC y el SAT.
- Re-emisión automática del CFDI sustituto cuando el motivo es 01.

**Cancelación de anticipos.** Cancelar un anticipo parcialmente amortizado
requiere primero cancelar las NCs de amortización relacionadas. El sistema
valida la cadena.

**Cancelación de facturas con anticipo aplicado.** Cancelar la factura
final requiere cancelar la NC de amortización autogenerada, lo que
restituye el saldo del anticipo a `Abierto`.

**Re-facturación tras cancelar.** Pedido y factura son entidades separadas:
`Facturado` es un estado del pedido, no la factura. Al cancelar el CFDI
vigente, el **pedido vuelve a `SinFacturar` y se puede re-facturar**. El CFDI
cancelado **nunca se borra** (inmutable); la nueva factura, si sustituye, se
liga al cancelado vía relación 04. El pedido conserva el **historial de todos
sus comprobantes** (cancelados + vigente) — trazabilidad fiscal completa. (Si
A+W además manda Cancelación del pedido, este pasa a `Cancelado` y no se
re-factura.)

---

## 11. Complementos de pago (REPP)

Aplica cuando una factura se emitió con método PPD.

**Generación.** Al confirmar un cobro (depósito, transferencia, voucher,
efectivo), el módulo genera un complemento que referencia la factura
cubierta.

**Reglas.**

- Un complemento puede cubrir múltiples facturas (varios pagos en un
  depósito).
- Una factura PPD puede tener múltiples complementos (parcialidades).
- Lleva la forma de pago real (no "Por definir"), fecha, datos bancarios.
- Se timbra como CFDI tipo P con complemento Pago 2.0.

**Automatización propuesta.** Al confirmar una transferencia, el complemento
se genera automáticamente.

---

## 12. Integraciones

### 12.1 Con los sistemas origen (A+W, Planta Pintura, Sistema de Salidas)

**Dirección y mecanismo — lectura de vistas SQL (pull) `[Verificado]`.** El
ERP **lee directamente vistas de base de datos** publicadas en el servidor
on-prem, vía **Azure Hybrid Connection Manager**. Reemplaza el push de JSON
a REST. Características:

- El ERP **consulta** las vistas (pull); los orígenes no empujan nada.
- Azure → on-premise vía Hybrid Connection Manager (sin VPN dedicada).
- Cada origen expone su propio conjunto de vistas.
- El "contrato de integración" deja de ser un esquema JSON y pasa a ser un
  **contrato de columnas de vista** (nombres, tipos, nulabilidad).

**Tabla-puente de solicitudes (cola de operaciones) — esquema definido por
el ERP.** A+W escribe sus tres operaciones (Alta / Modificación /
Cancelación) en una tabla-puente cuyo esquema **especifica el ERP**; A+W
solo la llena. El ERP la consume como **cola ordenada por versión** y
escribe de vuelta en la misma fila el resultado. Esquema lógico (los datos
del pedido —cabecera y líneas— siguen en la vista de A+W; la solicitud los
referencia):

| Columna | Escribe | Significado |
|---|---|---|
| `solicitud_id`, `numero_pedido` | A+W | Identidad de la solicitud y llave natural del pedido |
| `operacion` | A+W | `Alta` / `Modificacion` / `Cancelacion` |
| `version` | A+W | Incremental por pedido (orden e idempotencia) |
| `creada_at` | A+W | Timestamp de la solicitud |
| `erp_pedido_id` (claim) | **ERP** | Correlación; **persiste siempre**, nunca se borra |
| `estado_facturacion` | **ERP** | `SinFacturar` / `Facturado` / `Cancelado` (binario, sin "parcial") |
| `uuid` | **ERP** | Folio fiscal al facturar |
| `resultado`, `motivo`, `procesada_at` | **ERP** | `Aplicada` / `Rechazada` / `Pospuesta` / `Error` + detalle |

**Doble candado de idempotencia** (defensa en profundidad): la cola de
solicitudes es la **entrada**; `ingesta_control` en el ERP es la **fuente de
verdad** (llave natural + hash de contenido + estado), que sobrevive aunque
A+W pierda o reescriba algo. El ERP aplica cada solicitud solo si su
`version` > la última aplicada (orden e idempotencia).

**Matriz de gestión (operación A+W × estado del pedido en el ERP):**

| Operación | `Importado`/`Excepcion` (sin lock) | `Bloqueado` (facturando) | `Facturado` | `Cancelado` |
|---|---|---|---|---|
| **Alta** | Ingesta → crea `PedidoFacturable`, escribe claim + `SinFacturar` | n/a | n/a | n/a |
| **Modificación** | **Aplica**: refresh + nuevo snapshot, `version++` | **Pospone** (reintenta al liberar) | **Rechaza auto** → revisión manual; refleja `Facturado` | Requiere Alta nueva |
| **Cancelación** | **Cancela** → `Cancelado`, confirma a A+W | **Pospone/rechaza** | **Rechaza auto** → revisión manual (cancelar CFDI por flujo SAT + liberar anticipos) | No-op |

> **La factura manda.** El ERP **no** cancela CFDIs automáticamente: una
> Cancelación de A+W sobre un pedido `Facturado` solo **alerta**; un humano
> ejecuta la cancelación SAT 4.0. Idem para Modificación: un CFDI timbrado es
> **inmutable**, así que un cambio sobre lo ya facturado requiere
> NC/sustitución manual, no overwrite.
>
> **Sin huérfanos:** como el `erp_pedido_id` nunca se borra y las operaciones
> son explícitas (no "ausencia de claim"), no se pierde la correlación aunque
> el pedido ya tenga factura.

`[Gap]`: el **contenido** de la vista de datos del pedido (columnas,
estatus, llave natural) sigue por acordar (§16.1.4); el **esquema de la
tabla-puente de solicitudes lo definimos nosotros** (ya no es gap el
mecanismo de escritura).

**Resolución de Cliente y Producto al importar — depende del origen.**

| Caso | Origen **A+W** | Origen **Planta Pintura** |
|---|---|---|
| Cliente **no existe** en el master del ERP | **Auto-inserta** el cliente leyendo la vista de clientes de A+W (con sus datos fiscales) | `cliente_no_existe` → bandeja de excepciones (debe crearse antes en el ERP) |
| Artículo (`origen = A+W`) **no existe** | **Auto-inserta** el artículo leyendo la vista de artículos de A+W | `articulo_no_existe` → excepción (debe crearse antes en el ERP) |

> **Auto-provisión solo desde A+W.** Es el **único** caso en que el ERP crea
> registros de master desde una vista. Planta Pintura exige que cliente y
> artículo existan previamente en el ERP.

**Otras validaciones al importar** (ambos orígenes), antes de convertir un
registro en `PedidoFacturable`:

| Validación | Acción si falla |
|---|---|
| Artículo (ya existente) tiene clave SAT | `producto_sin_clave_sat` |
| Cliente (ya existente o recién provisionado) tiene datos fiscales completos | (no bloquea importación; bloquea **timbrado**, ver §1 boundary) |
| Almacén físicamente asignado | `almacen_no_asignado` |
| Divisa válida | `divisa_invalida` |
| Totales del encabezado cuadran con posiciones | `totales_no_cuadran` |

Los que fallan quedan en una **bandeja de excepciones**, no se pierden.

**Idempotencia.** La garantiza la `ingesta_control` descrita arriba: llave
`(origen, clave_natural)` + `hash_contenido`/versión. Un pedido nunca se
reimporta dos veces ni se duplica, y los cambios se detectan por diferencia
de versión. La tabla recuerda también los pedidos que cayeron en excepción
(para no reprocesarlos en bucle).

**Persistencia.** El registro leído se guarda como
`PedidoFacturableSnapshot` (copia al momento de la lectura) por
trazabilidad. Los datos normalizados van en `PedidoFacturable` y sus
líneas. Si el master de Cliente cambia después, no afecta lo importado; el
cajero ve el snapshot pero puede refrescar antes de timbrar.

**Write-back hacia los orígenes.** Se escribe en la misma tabla-puente de
solicitudes: claim (`erp_pedido_id`) al ingestar, y `estado_facturacion` +
`uuid` al facturar/cancelar. (El Sistema de Salidas no requiere write-back;
solo aporta pedimento.) Mecanismo de escritura **definido por el ERP**;
queda por afinar el contrato con Planta Pintura (§16).

**Datos que aportan las vistas vs. datos que el ERP resuelve:**

| Concepto | Origen |
|---|---|
| Identidad del pedido (número, sucursal) | Vista del origen |
| Cliente (referencia) | Vista del origen |
| Datos fiscales del cliente (RFC, régimen, CP fiscal, defaults) | ERP — master de Cliente |
| Producto (ID, nombre, dimensiones, peso) | Vista del origen |
| Datos fiscales del producto (clave SAT, clave unidad, objeto imp, tasa IVA, retención) | ERP — master de Producto |
| Líneas con precios y descuentos | Vista del origen |
| Almacén físico | Vista del origen |
| Descuento por bonificación | Vista del origen |
| Obra (`obra_id` + `obra_nombre`, dos columnas) | Vista de A+W |
| Uso CFDI / método / forma de pago | Default del cliente; override en pantalla |
| Anticipos aplicables | ERP — Control de Anticipos; CxC instruye el monto |
| **Indicador "requiere pedimento"** (por línea o cabecera) | Pedido (vista A+W / captura manual). **No** del catálogo de Producto: el mismo artículo puede ir con o sin pedimento |
| Pedimento y datos aduaneros (valor) | Sistema de Salidas, replicado por el ERP |
| INCOTERM, fracciones arancelarias | Catálogos alimentados por Comercio Exterior |
| Datos de Carta Porte (vehículo, operador, distancia) | ERP — captura en pantalla |

Esta tabla es la herramienta principal para detectar gaps de cobertura del
master del ERP. Si un dato del lado izquierdo no llega o no es completable,
el pedido no puede facturarse.

**Componentes (BOM) en las posiciones.** Cuando la vista incluye desglose
de componentes (BOM) por posición, **son informativos y no generan líneas
en el CFDI**. Se persisten en el snapshot para auditoría; el CFDI usa solo
el producto padre con su precio y descuento.

### 12.2 Con FiscalAPI (vía `Millet.Integraciones.Fiscal`)

FiscalAPI es el PAC único del ERP ([ADR-0038](../../decisiones/0038-fiscalapi-pac-unico.md)).
**Facturación no lo invoca directo** — consume el puerto `IFiscalApiClient`
expuesto por `Millet.Integraciones.Fiscal` (fase 2 de ese módulo: timbrado,
cancelación, consulta de estatus, respuesta a solicitudes de cancelación).

**Qué hace FiscalAPI por el ERP:**

| Capacidad | Detalle |
|---|---|
| Timbrado CFDI 4.0 | Recibe datos como JSON, construye/calcula cadena, aplica sello del emisor, timbra contra el SAT, devuelve UUID, sello SAT, fecha, XML completo y PDF estándar |
| Complementos nativos | Pagos 2.0, Carta Porte 3.1, CCE, Certificado de destrucción, Vehículo usado, Nómina — sin construcción manual del XML |
| Validación previa | Verifica RFC contra LRFC del SAT, consistencia forma/método de pago, estructura y catálogos; errores descriptivos antes de consumir timbre |
| Cancelaciones SAT 4.0 | Solicitud con motivo y UUID sustituto, consulta de estado, notificación de aceptación/rechazo |
| Catálogos del SAT | Vía API, siempre vigentes (§5.2) |
| Descarga masiva del SAT | CFDIs emitidos/recibidos — para reportes y conciliación (§12.7) |

> **Sellado vs. timbrado:** ambos ocurren en una sola operación contra
> FiscalAPI. Para el usuario es transparente. Por eso el modelo no expone un
> estado `Sellado` intermedio.

**Qué hace el ERP (Facturación), no FiscalAPI:**

- **Construcción de los datos del CFDI** (mapeo dominio → DTO del PAC) y la
  **CSD** (sello del emisor) — la CSD por empresa la provee
  `Integraciones.Fiscal` vía `ICsdProvider`.
- **Generación del PDF para el cliente y para impresión interna** — layouts
  custom de Millet (bilingüe ES/EN; simplificado en español para térmicas)
  conforme a la reportería nativa ([ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md)).
  La versión PDF estándar de FiscalAPI queda como respaldo de auditoría.
- **Bitácora de envíos al cliente** (reintentos, entregables, trazabilidad).
- **Validación local previa** (anticipos vinculados, cliente con datos
  fiscales completos, totales que cuadran) — previene consumir timbres en
  errores corregibles. La validación de FiscalAPI es segunda capa.
- **Persistencia del CFDI** (XML, UUID, sello, PDF, bitácora) en la BD del
  ERP. FiscalAPI es el procesador, no el almacén.

> **Dependencia crítica `[Gap]`:** la fase 2 de `Integraciones.Fiscal`
> (emisión/cancelación) está en **rediseño asíncrono** y bloquea PRs nuevos
> hasta cerrar sus decisiones D1–D10 y validar sandbox (ver
> [`integraciones-fiscal/02-flujo-asincrono.md`](../integraciones-fiscal/02-flujo-asincrono.md)).
> Facturación debe diseñar su flujo de timbrado **asíncrono-tolerante**
> (encolar + reintentos + estado `Cancelación pendiente`), no asumir
> respuesta síncrona inmediata. Ver §16.

**Modelo de costos.** Suscripción mensual + paquetes de timbres. Cada timbre
exitoso consume crédito; los rechazos por validación previa no. Refuerza la
importancia de la validación local.

**Ambientes.** Sandbox de FiscalAPI en dev/QA/staging; producción solo desde
el ambiente productivo. Configurable por variable de ambiente.

### 12.3 Con Contabilidad

- Asientos contables por cada CFDI emitido (factura, anticipo, NC,
  complemento de pago).
- Usan los `ConceptoContable` que Contabilidad mapea a cuentas reales
  (§6.5).
- Reportes: balanzas, auxiliares, conciliaciones.

### 12.4 Con CxC y Tesorería

- Notificación a CxC cuando un pedido pasa a 69 (saldo por cobrar).
- Registro de cheques recibidos como depósitos en tránsito hasta confirmar
  cobro.
- Tesorería confirma depósitos y dispara la emisión del complemento de pago.

### 12.5 Con correo electrónico del cliente

- Envío automático de todos los documentos fiscales de un pedido al correo
  del cliente: factura de anticipo, factura final, NCs, complementos de
  pago.
- Reintentos en caso de fallo. Bitácora visible en cada documento.
- **Reusar** el módulo `Millet.Integraciones.Mailbox` / servicio de
  notificaciones compartido cuando exista (PLATFORM-TODO, §14).

### 12.6 Con módulo de Obras (Fase posterior)

- Facturación expone los documentos vinculados a un número de Obra para que
  Obras los concentre. Obras consulta; no escribe en Facturación.

### 12.7 Descarga de CFDIs emitidos (vía `Integraciones.Fiscal`)

Además del flujo de emisión, el ERP usa la descarga masiva del SAT para
traer los **CFDIs emitidos por Millet** a un repositorio del ERP, para
conciliación con CxC e Ingresos y para reportería. Es la contraparte, del
lado emitido, de lo que CxP hace con los recibidos. **Conviene que el
repositorio de CFDIs sea común a ambos módulos** (emitidos en Facturación,
recibidos en CxP) con la misma estructura base.

### 12.8 Con módulo / catálogo de Activos Fijos

Para venta de activos (§7.8), Facturación consulta el catálogo de Activos
Fijos para validar el alta y obtener valor en libros y depreciación. Si el
módulo no existe en MVP, se requiere un catálogo mínimo de activos con
saldos.

---

## 13. Reportes

Conforme a la reportería nativa ([ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md)):
componentes React + endpoints JSON + exportación client-side (PDF con
`@react-pdf/renderer`, Excel con `exceljs`). Reusar `<ReporteShell>`.

### 13.1 Liquidación de caja

Por sucursal y caja, cortes bajo demanda. Cruza facturado vs. cobrado por
forma de pago, NCs aplicadas, y diferencia en cuentas por cobrar.

### 13.2 Control de Anticipos

Vistas Resumen y Detallada (§6.7).

### 13.3 Reporte de Estados de Facturas de Anticipo

Filtros (cliente, fechas, estatus). Columnas: Fecha, Número de documento,
Cliente, Proyecto, Obra, Referencia de pedido, Tipo de factura, Estado,
Moneda, Importe original, Saldo. Exportable a PDF y Excel.

### 13.4 Reportes por Obra

Vista financiera por obra. **Vive en el módulo de Obras**, no en
Facturación. Facturación expone los datos.

---

## 14. Dependencias de plataforma pendientes

> Sección obligatoria ([ADR-0031](../../decisiones/0031-deuda-de-plataforma-y-stubs-noop.md)).
> Tabla *Pieza · Ticket · NoOp en uso · Cómo se wirea*. Cada `PLATFORM-TODO`
> en el código del módulo tendrá una fila aquí.

| Pieza | Ticket | NoOp en uso | Cómo se wirea |
|---|---|---|---|
| **Esquema Postgres `facturacion` + `FacturacionDbContext`** | — | n/a (se crea desde inicio) | Agregar a `MigrationsHealthCheckOptions.ContextTypes` y al bucle de migraciones en `deploy-app-dev.yml` (memoria [feedback_dbcontext_nuevo_checklist]). |
| **Puerto `IFiscalApiClient` de `Integraciones.Fiscal` (fase 2: timbrar/cancelar/consultar)** | `<IntegracionesFiscalFase2>` | Stub `IFiscalApiClient` que devuelve UUID fake en sandbox vacío | Implementar fase 2 de `Integraciones.Fiscal` (rediseño asíncrono pendiente). Facturación consume el puerto, no implementa el client. |
| **`ICsdProvider` (CSD del emisor por empresa)** | `<IntegracionesFiscalCsd>` | Stub que firma con CSD de pruebas del SAT | Provisto por `Integraciones.Fiscal`; CSD en Key Vault como `Certificate`. |
| **Ingesta de vistas SQL on-prem (`PedidoFacturable`)** | `<HybridConnectionViews>` | NoOp / fixtures si la cs on-prem está vacía | Azure Hybrid Connection Manager a `SER-DATA`; lector de vistas (A+W, Planta Pintura, Salidas). Ver decisión de ubicación en `01-diseno.md`. |
| **Write-back ERP → A+W (claim al ingestar + UUID/estatus 115/70 al facturar)** | `<WriteBackOrigenes>` | NoOp que loggea claim/resultado; `ingesta_control` es fuente de verdad | Canal bidireccional confirmado; implementar contra SP/tabla-puente sancionado de Millet (no mutación directa de A+W). Acordar contrato con cada origen. |
| **Outbox para eventos de integración de Facturación** | — | `NoOpIntegrationEventBusSender` si la cs de Service Bus está vacía | Reusar `OutboxSaveChangesInterceptor` + `OutboxPublisherWorker<FacturacionDbContext>`. |
| **Contabilidad: motor de asientos por `ConceptoContable`** | — | Stub `IContabilidadAsientoPort` que loggea sin persistir | Cuando exista Contabilidad, adapter que consume eventos del Outbox. Mismo patrón que CxP. |
| **DatosMaestros: master de Cliente (fiscal) y Producto (clave SAT, retención)** | — | Compartido con `DatosMaestros` MVP | Extender entidades con los atributos fiscales de §5.1. Master de Cliente hoy en A+W (§16). |
| **Catálogo/módulo de Activos Fijos** | `<ActivosFijos>` | Catálogo mínimo de activos con saldos | Para venta de activos (§7.8). |
| **ConceptoContable → CuentaContable (mapeo)** | — | Seed con placeholders `TBD-*` + `requiere_codigo_definitivo` | Migración de pocas filas cuando Contabilidad entregue códigos (§6.5). |
| **Notificaciones / Mailbox para envío de CFDI al cliente** | `<EnvioCfdiCliente>` | NoOp si SMTP/Graph no configurado | Reusar `Millet.Integraciones.Mailbox` / `INotificacionService` cuando exista. |
| **Servicio compartido de PDF (plantillas bilingües)** | — | Plantillas React propias del módulo ([ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md)) | Reusar utilitarios compartidos de reportería. |
| **Catálogos SAT (espejo local)** | — | Reusar los de `Compartido` donde existan; sync nocturno desde FiscalAPI | Job de sincronización vía `Integraciones.Fiscal`. |
| **Período contable cerrado (candado)** | `<PeriodoContableCerrado>` | Stub `IPeriodoContablePort` que siempre retorna "abierto" | Cuando exista Contabilidad, consultar períodos cerrados antes de cualquier operación. |
| **RBAC: permisos canónicos `facturacion.*`** | — | Registrar al arrancar el módulo | Permisos por recurso (§ diseño). Asignar a roles operativos. |

---

## 15. Decisiones cerradas

| ID | Decisión | Confirmado por |
|---|---|---|
| D1 | Nombre del módulo: `Millet.Facturacion`. Esquema Postgres: `facturacion`. | Convención del proyecto |
| D2 | Integración entrante por **lectura de vistas SQL (pull)** vía Azure Hybrid Connection Manager. Reemplaza el push de JSON. | Área, mapa funcional v0.4 |
| D3 | Modelo de **dos ejes**: Canal de venta (organizacional) × Comportamiento fiscal. Reemplaza la lista plana de "tipos de venta". | Área, v0.4 |
| D4 | **Facturas individuales a RFC genérico** permitidas desde MVP (`XAXX` nacional, `XEXX` exportación). Factura Global periódica diferida. | Comentarios #8, #23 |
| D5 | **Amortización de anticipo siempre vía NC autogenerada** (no descuento directo), en la misma transacción que el timbre de la factura final, relacionando factura de anticipo Y factura final. | Decisión A.1 |
| D6 | **Saldo amortizable = Cobrado − Amortizado** (no Emitido − Amortizado). | Decisión A.1 |
| D7 | **Bonificación por NC posterior**, no en el XML de la factura (estrategia comercial). | Comentarios #9, #22 |
| D8 | **Carta Porte 3.1 como entidad propia**; una por tramo; cambio de vehículo/operador = CFDI nuevo. | Decisión C.1 |
| D9 | **Pedimento y datos aduaneros en el XML normal** (no en CCE); aplican a nacional y exportación. Vienen del Sistema de Salidas. | Comentarios #17, #18 |
| D10 | **PAC = FiscalAPI** (único) consumido vía puerto `IFiscalApiClient` de `Integraciones.Fiscal`. No se implementa el client en Facturación. | [ADR-0038](../../decisiones/0038-fiscalapi-pac-unico.md) |
| D11 | **No hay estado `Sellado` intermedio**; sellado + timbrado en una sola operación. | Comentario #6 |
| D12 | **No addendas comerciales.** Solo complementos fiscales (Carta Porte, CCE, REPP, Cert. destrucción, Vehículo usado, Nómina). | Decisión A.5 |
| D13 | **Candado de período contable cerrado** en toda operación (emisión, cancelación, modificación). | Comentario #1 |
| D14 | **Ganancia/pérdida cambiaria** automática al aplicar pagos en moneda extranjera. | Comentario #19 |
| D15 | **CxC instruye** la porción de anticipo a aplicar; **caja ejecuta**. | Comentario #21 |
| D16 | **No** devolución de IVA a turistas; **no** Polos del Istmo; **NC por devolución** fuera de MVP. | Decisiones A.3, C.6 |
| D17 | Workers de ingesta/timbrado como **`IHostedService` dentro de `Millet.Api`** (mismo patrón que Outbox/Aw/Fiscal). | Convención (D4 Aw / D5 Fiscal) |
| D18 | Integración A+W **bidireccional** vía **tabla-puente de solicitudes cuyo esquema define el ERP** (A+W la llena). Tres operaciones: Alta / Modificación / Cancelación. Claim (`erp_pedido_id`) **persiste**, nunca se borra. **Doble candado**: cola de solicitudes (entrada) + `ingesta_control` (fuente de verdad). | Owner, 2026-05-30 |
| D19 | **No existe estado "parcialmente facturado".** Pedido binario `SinFacturar`/`Facturado`. La facturación parcial = A+W libera **unidades facturables discretas** (cada estimación/embarque es un `PedidoFacturable` propio). | Owner, 2026-05-30 |
| D20 | **La factura manda:** Modificación/Cancelación de A+W solo se auto-aplican si el pedido **no** tiene CFDI. Con CFDI → revisión manual; el ERP **no** cancela CFDIs automáticamente (flujo SAT 4.0 con humano). | Owner, 2026-05-30 |

---

## 16. Decisiones pendientes

### 16.1 Bloqueantes parciales (esperando al área o a los orígenes)

1. **Factura Global periódica.** Individuales a genérico ya permitidas en
   MVP; la consolidación periódica sigue diferida. Estructura reserva el
   modo `Pendiente de ticket`. Plazo, periodicidad y reglas al activar.
2. **Sample completo Anticipo + Factura final + NC de amortización (A.1).**
   Pendiente del área. Necesario para validar el XML exacto y la estructura
   de la NC (nodo `CfdiRelacionados`, concepto, clave SAT).
3. **Catálogo completo de estatus A+W.** Indicado como enviado, no
   recibido. Sin él, las transiciones (15→69→70→115) se modelan
   provisionalmente con los estatus conocidos.
4. **Contrato de columnas de las vistas SQL** (reemplaza la solicitud al
   JSON). Pendientes con cada origen: datos fiscales como columnas o vía
   default del cliente; Obra en dos columnas (confirmado); columna de
   estatus + timestamp; indicador de pedido sustituido; **indicador
   "requiere pedimento" por línea o cabecera** (el mismo artículo puede ir
   con o sin pedimento, así que no se deriva del catálogo); manejo de nulos
   (sin sentinels tipo `<indf>`).
5. **Write-back ERP → orígenes (capacidad confirmada, mecanismo por afinar).**
   La integración A+W es **bidireccional**: claim al ingestar + UUID/estatus
   al facturar. Falta acordar el **canal sancionado** (SP o tabla-puente que
   Millet exponga; no mutación directa de A+W), su contrato, errores y
   reintentos, y el equivalente para Planta Pintura.

### 16.2 Diferidas a fase posterior

6. **Cuenta puente "Obras Entregadas No Facturadas".** Fase 2. Contabilidad
   y costeo.
7. **NC por devolución de mercancía.** Fuera de MVP. Requiere Inventario.
8. **Reporte unificado de Obras** (cliente + proveedores). Vive en Obras.
9. **Cuentas contables específicas.** Capa `ConceptoContable` ya prevista;
   placeholders en MVP.
10. **Absorción de Planta Pintura y Sistema de Salidas en el ERP.** Hoy son
    integraciones por vista; en el futuro nativas. El diseño no debe
    acoplarse a su forma actual de vista.

### 16.3 Validaciones técnicas antes de implementar

11. **Carga inicial / auto-provisión de Cliente desde A+W.** Resuelto el
    master: **el ERP es master único** y los clientes **nacen desde A+W** por
    auto-provisión al ingestar (§1, §12.1). Pendiente: el **contrato de la
    vista de clientes de A+W** (qué columnas fiscales trae: RFC, régimen, CP,
    defaults) y si se hace una **carga inicial masiva** o solo on-demand al
    ingestar el primer pedido de cada cliente. No se factura sin RFC,
    régimen, CP fiscal y defaults.
12. **Cobertura de migración del master de Producto.** Clave SAT, clave
    unidad, cuenta contable, retención por artículo. Sin clave SAT no se
    factura.
13. **Catálogo/módulo de Activos Fijos.** Definir si hay módulo en MVP o un
    catálogo mínimo con saldos.
14. **Conexión Azure Hybrid Connection Manager a `SER-DATA`.** Permisos de
    lectura sobre las vistas, latencia, disponibilidad. Infra crítica.
15. **Detección de cambios en las vistas.** Estrategia: columna estatus +
    timestamp, tabla de control, frecuencia de polling.
16. **Dependencia de la fase 2 de `Integraciones.Fiscal`** (rediseño
    asíncrono). Timbrado/cancelación de Facturación no pueden cerrarse hasta
    que ese módulo cierre D1–D10 y valide sandbox. Diseñar
    asíncrono-tolerante.
17. **Política de fallas de FiscalAPI.** Encolar con reintentos exponenciales
    (hasta 3) y escalar al cajero.
18. **Webhooks vs. polling para cancelaciones asíncronas** (respuesta del
    receptor en 3 días hábiles).
19. **Credenciales y secrets.** Tokens de FiscalAPI y CSDs por
    sucursal/RFC emisor en Key Vault (provisto por `Integraciones.Fiscal`).
20. **Período contable cerrado.** Integración con Contabilidad que indica
    qué períodos están cerrados (candado).

---

## 17. Reglas críticas y restricciones

Consolidadas para referencia. Cada una está descrita en su sección.

| Regla | Origen |
|---|---|
| El descuento por **bonificación no aparece en el XML**. Va por NC posterior. | #9, #22 |
| Los **comentarios de A+W no llegan al XML**. | §7.1 |
| **No se aceptan monedas extranjeras en efectivo.** | §7.1 |
| **PCI-DSS:** no se almacena PAN completo ni vencimiento de tarjetas. Solo 4 últimos dígitos y autorización del adquirente. | C.5 |
| Cheque: capturar **fecha de emisión Y vencimiento**. Depósito en tránsito hasta confirmar cobro. | C.3 |
| **Saldo amortizable = Cobrado − Amortizado.** | A.1 |
| **Anticipos siempre nominales** (no genérico). Facturas de venta normales SÍ pueden ir a genérico. | #8, #23 |
| NC de amortización en la **misma transacción** que el timbre y **relaciona factura de anticipo + factura final**. | A.1, #14 |
| Una **Carta Porte por tramo**. Cambio de vehículo/operador = CFDI nuevo. | C.1 |
| El **campo Obra no aparece en el XML**. Dos columnas: id + nombre. | §7.4, #16 |
| Forma de pago de reparto: **clave 99** + complemento al liquidar. | C.2 |
| **No** devolución de IVA a turistas. | A.3 |
| **No** aplican Polos del Istmo. | C.6 |
| PDF **bilingüe (ES/EN)** salvo simplificada térmica (solo español). | §7.1 |
| **Facturas individuales a RFC genérico permitidas** desde MVP; Global periódica diferida. | #8, #23 |
| **Candado de período cerrado** en toda operación. | #1 |
| **Pedimento y datos aduaneros en el XML normal** (no CCE); del Sistema de Salidas. | #17, #18 |
| **Ganancia/pérdida cambiaria** automática al aplicar pagos en moneda extranjera. | #19 |
| **Clientes multimoneda y multi-impuesto.** | #10 |
| **Venta de activo fijo** requiere alta como activo fijo + autorización del Contador General antes de timbrar. | Correo |
| **Retención por artículo** definida en el catálogo de productos. | Correo |
| **Sellado y timbrado en una sola operación**; no hay estado `Sellado`. | #6 |
| **CxC instruye** la porción de anticipo; **caja ejecuta**. | #21 |
| Cancelación de anticipo amortizado requiere cancelar primero las NCs de amortización. | Análisis técnico |

---

## 18. Glosario

- **CFDI** — Comprobante Fiscal Digital por Internet (4.0). Documento fiscal
  electrónico válido ante el SAT.
- **PAC** — Proveedor Autorizado de Certificación. FiscalAPI en este ERP.
- **Timbrar** — Que el PAC valide el CFDI, lo registre ante el SAT y le
  agregue el sello del SAT más el UUID.
- **Sellar** — Aplicar el sello digital del emisor (con su CSD) al XML.
  Ocurre en la misma operación que el timbrado (D11).
- **UUID / Folio fiscal** — Identificador único del CFDI asignado por el SAT
  al timbrar. Llave externa ante el fisco; el sistema mantiene su ID interno
  además.
- **CSD** — Certificado de Sello Digital del emisor, por RFC/empresa. Vive
  en Key Vault (provisto por `Integraciones.Fiscal`).
- **PUE / PPD** — Pago en Una Exhibición / Pago en Parcialidades o Diferido
  (c_MetodoPago).
- **REPP** — Recibo Electrónico de Pagos (CFDI tipo P con complemento Pago
  2.0).
- **CCE** — Complemento de Comercio Exterior (exportación).
- **Carta Porte** — Complemento de traslado de mercancías (versión 3.1).
- **Anticipo** — Cobro adelantado registrado como CFDI de Ingreso con serie
  dedicada; se amortiza vía NC con relación 07.
- **Amortización** — Aplicación del saldo de un anticipo contra una factura
  final, materializada como NC de Egreso.
- **Bonificación** — Descuento comercial posterior a la factura,
  materializado como NC de Egreso (no aparece en el XML de la venta).
- **Canal de venta** — Eje organizacional de la facturación (de dónde viene
  la venta).
- **Comportamiento fiscal** — Eje fiscal ortogonal al canal (cómo se comporta
  frente al SAT).
- **PedidoFacturable** — Representación normalizada en el ERP de un pedido
  listo para facturar. Su origen puede ser una vista SQL (A+W, Planta
  Pintura, Sistema de Salidas) o **captura manual** en el ERP (§7.10).
- **RFC genérico** — `XAXX010101000` (público en general nacional) /
  `XEXX010101000` (extranjero).
- **ConceptoContable** — Código lógico estable que indireccióna eventos del
  módulo a cuentas contables reales (mapeo en Administración/Contabilidad).
- **Write-back** — Reflejo del estado del ERP de vuelta en el sistema origen
  (facturado 115, liquidado 70, créditos).

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-05-29 | Levantamiento inicial derivado del mapa funcional v0.4 (mayo 2026, ya revisado por el área). Reframe al formato estándar del proyecto: header con namespace + ADRs, leyenda Verificado/Inferido/Gap, tabla de decisiones cerradas (D1–D17), sección de dependencias de plataforma (ADR-0031), glosario. Dependencia explícita del puerto `IFiscalApiClient` de `Integraciones.Fiscal` (fase 2). |
