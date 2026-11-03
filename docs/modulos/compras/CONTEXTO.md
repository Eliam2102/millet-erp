# CONTEXTO · Compras (MOD-COM)

Paquete de contexto para desarrollo. No reemplaza el levantamiento ni el diseño: los resume y apunta a ellos.
Última actualización: 2026-10-17 · Tarea que lo actualizó: 86e3jvu2a (F1-COM-02, cotizaciones comparables)
Estado del código leído: rama `feature/compras-solicitudes-millet-12oct`, HEAD `51449639`; `origin/main` @ 2026-10-16 11:17 contiene `320dfd33`, `91a202c4`, `146c0bcd`. El working tree tiene 23 archivos de Compras/Almacén sin commit (modo N2 por departamento, 16-oct).

**Convención de certeza.** Cada dato lleva una etiqueta:
- `[CONF · fuente]` confirmado por Millet o por la base de conocimiento (cita archivo, pregunta/página o nota SRC y fecha).
- `[DISEÑO · doc]` decidido por nosotros en diseño/ADR, sin respuesta de Millet que lo contradiga.
- `[SUPUESTO]` tomado a falta de respuesta; indica la alternativa y a quién se le pregunta.
- `[ABIERTO]` sin respuesta; no desarrollar sobre esto sin decisión del owner.
- `[CÓDIGO]` verificado en el repo (ruta).
Nada se escribe sin etiqueta. Lo que no se pudo validar va a ABIERTO, nunca se inventa.
Nota de procedencia: los `manual-*.pdf` (22-jul-2026) describen el ERP construido por VILO, no son respuesta de Millet; se etiquetan DISEÑO. `Matriz` = `01_Matriz_validacion_funcional_..._RESPUESTAS_MILLET.docx`; `Cuest.02` = `02_Cuestionario_Compras_Almacen_Produccion_v1.docx` (SRC-003), pregunta = número del ítem. Ruta base de la base de conocimiento: `~/Descargas/BASE DE CONCIMIENTO DE VIDRIOS-…/BASE DE CONCIMIENTO DE VIDRIOS/` (BC).

## 1. Qué es y qué no es
Convierte una necesidad de material o servicio en una compra autorizada y rastreable: Requisición (RQ) → autorización → reparto stock/compra → Orden de Compra (OC) → autorización → seguimiento de recepción, facturación y pago hasta cierre automático. Usuarios: requisitantes, autorizadores N1/N2, jefe de Compras (Rodrigo Chay), Dirección, almacenista (surte RQ). `[DISEÑO · compras-ordenes-compra/00-levantamiento-mapa-funcional.md]`
Absorbe el portal legacy de Requisiciones: "Se reemplaza. El ERP manejará todas las requisiciones" `[CONF · BC/Cuest.02 pregunta 13; vault D03]`.
Fuera: recepción física (Almacén), factura/pasivo (CxP), pago (Tesorería), cotizaciones formales y presupuesto `[DISEÑO · compras-requisiciones/01-diseno.md §1.2]`. Compras de materia prima desde A+W y envío de OC al proveedor no están construidos (§3). Entrega al siguiente proceso: OC autorizada que habilita recepción y conciliación de factura `[CONF · manual-ordenes-compra.pdf §1 (DISEÑO) + Matriz P1]`.

## 2. Documentos a leer (en orden)
| Orden | Ruta | Para qué |
|---|---|---|
| 1 | `CLAUDE.md` (raíz), sección "Triada Compras ↔ Almacén ↔ CxP" y "Segmentación por sucursal" | Eventos canónicos, reglas de oro, ADR-0051 |
| 2 | `docs/modulos/compras-ordenes-compra/00-levantamiento-mapa-funcional.md` | Alcance validado con R. Chay y E. Paredes (11-may) |
| 3 | `docs/modulos/compras-requisiciones/01-diseno.md` §3, §3.bis, §5 | Matriz de aprobación, estados RQ (parcialmente desactualizado, ver §9) |
| 4 | `docs/modulos/compras-ordenes-compra/01-diseno.md` §3, §5, §7, §8 | Asunciones C1–C12, sub-estados, validaciones, integraciones |
| 5 | `docs/decisiones/0043-entrega-post-recepcion-rq.md`, `0033-setting-auto-generar-oc-al-autorizar.md`, `0050-consumo-centro-costo-maquina-dim3.md`, `0051-segmentacion-de-datos-por-sucursal.md`, `0041`, `0009` | Cierre RQ por entrega, setting de auto-OC, CC-Máquina, alcance sucursal, permisos por operación, outbox |
| 6 | `.local-context/12 Implementación de tareas/86e3pdmej · Implementación.md` y `.local-context/brechas-abiertas.md` | Cambios de la junta Millet 12-oct y brechas vivas (el expediente está parcialmente obsoleto) |
| 7 | `.local-context/13 Demo 12-oct/04 · Minuta 12-oct · lo que sabemos y lo de hoy 15-oct.md` | Pedidos M5–M8 de Millet |
| 8 | Vault `02 Procesos y módulos/MOD-COM.md`, `Recorrido compra a pago.md`, `Reglas de los manuales operativos conciliadas.md`, `Compra a pago · respuestas operativas y diferencias.md` | Reglas contrastadas y casos de aceptación |
| 9 | BC: `manual-requisiciones.pdf`, `manual-autorizacion-requisiciones.pdf`, `manual-ordenes-compra.pdf`, `manual-centros-de-costo.pdf` | Operación descrita (22-jul) |
| 10 | BC: `Cuest.02`, `Matriz` (F-09, F-10, F-11, COM-01..13) | Respuestas de Millet |

## 3. Fichas y estado
Estado verificado en código y en `origin/main`, no copiado del plan. Fichas F1-COM-xx en vault `03 Funcionalidades/`; ninguna tiene tarea ClickUp propia localizada, salvo F1-COM-06 (vía 86e3pdmej).
| Ficha | Resultado | Estado real | PR o rama | Brechas abiertas |
|---|---|---|---|---|
| F1-COM-01 | Crear y autorizar RQ | **Construido**: CRUD, transmitir, N1/N2, rechazo con motivo, cancelar, eliminar, cerrar manual, reparto stock/compra, bandeja con aprobación masiva `[CÓDIGO · backend/src/Compras/Application/{CrearRequisicion,Autorizar,Matriz,Disponibilidad}]` | 86e3pdmej (`320dfd33`,`91a202c4` en origin/main); PR #43/#44 (CeCo/sucursal) | Estado explícito "En almacén" previo a autorizar; `PrecioEstimado` oculto pero aún en el modelo; N+1 de stock cerrado solo en working tree |
| F1-COM-02 | Registrar y comparar cotizaciones | **Construido en rama `feature/sprint02-geovany` (86e3jvu2a)**: agregado `Cotizacion`, comparativo normalizado (unidad de la RQ y MXN con TC del día), editar/eliminar antes de elegir, y OC desde RQ con la cotización elegida (precio, moneda y PDF adjunto) `[CÓDIGO · backend/src/Compras/Domain/Cotizaciones, backend/src/Compras/Application/Cotizaciones, frontend/src/features/compras/pages/ComparativoCotizaciones.tsx]` | `feature/sprint02-geovany` (sin PR) | Cotización parcial por partidas y compra directa sin RQ fuera de alcance; pendiente run de evidencia en dev (migraciones y permisos `compras.cotizaciones.*` sin aplicar en dev) |
| F1-COM-03 | OC desde RQ sin exceder saldo | **Construido** `[CÓDIGO · Application/Oc/CrearOrdenCompraDesdeRequisicion, Oc/SaldoCompraRq.cs, ListarRequisicionesDisponibles]`; compromiso exclusivo de RQ (C9) | en `origin/main` (PR no identificado) | Quién crea la OC en el parcial y cómo llega lo comprado al requisitante (86e3pdmej P2) |
| F1-COM-04 | OC sin RQ con autorización | **Construido** (`compras.ordenes.crear-sin-rq`, motivo + adjunto `correo_autorizacion`, CHECK `ck_oc_sin_rq_motivo`) | main | — |
| F1-COM-05 | Autorizar OC y 2ª firma | **Construido** `Domain/Oc/OrdenCompra.cs` `Autorizar`, `EvaluadorNivelOc`, aprobación masiva | `320dfd33` | Corte N2 y rango de umbrales sin confirmar (§9 A1) |
| F1-COM-06 | Recepción, facturación, pago, cierre automático | **Parcial real**: listeners de Almacén/CxP actualizan sub-estados y cierran/reabren; demo e2e por API `CompraAPagoDemoTests` verde; comercio exterior con campos supuesto | `146c0bcd`, `97723438` | BL y lista oficial de campos de importación; tolerancia de cantidad por línea de OC no construida |
| F1-COM-07 | Entregas parciales, incidencias, compromisos proveedor | **Pendiente/parcial débil**: hay parcialidad (saldo por línea) pero no incidencias ni compromisos de proveedor | — | Diseñar |
| F1-COM-08 | Cancelar OC conservando autorización y motivo | **Construido**: cancelar, cancelar con recepciones con doble firma (`CancelacionSolicitada`, `SolicitudCancelacionOc`), duplicar | main | Probar segunda firma de punta a punta (vault contraste) |
| F1-COM-09 | Compras MP en ERP → A+W | **No construido**: sin referencias A+W en `backend/src/Compras` | — | Contrato, idempotencia, campos (Cuest.02 pregunta 10) |
| F1-COM-10 | OC en PDF y envío al proveedor | **Parcial**: PDF QuestPDF sin CC-Máquina (`Infrastructure/Oc/Pdf/QuestPdfOrdenCompraGenerator.cs`, `GET /{id}/pdf`, `OrdenCompraAutorizadaPdfListener`); **envío al proveedor no existe** | — | PDF sin campos de importación; razón social/RFC reales (PLATFORM-TODO) |
| F1-COM-11 | Acuse del proveedor | **Construido (rama `feature/sprint02-geovany`)**: respuesta del proveedor con fechas prometidas por línea/parcialidad, estado comercial y evidencia G1.2 | feature/sprint02-geovany | Envío real (F1-COM-10); incidencias y avisos (F1-COM-07) |
| F1-COM-12 | Trazabilidad RQ→OC→recepción→factura→pago | **Parcial condicionado**: `GET /api/v1/compras/trazabilidad`; proveedores de nodos para recepciones y facturas, sin nodo de pagos | — | Proveedor de pagos |
| F1-COM-13 | Análisis de compras y cumplimiento | **Parcial**: partidas abiertas + KPIs, historial de compras de material, últimas 100 compras; sin cumplimiento de proveedor | — | Métricas por proveedor |

## 4. Reglas de negocio confirmadas
| # | Regla | Etiqueta y fuente |
|---|---|---|
| R1 | Cadena de compra de insumos: RQ → autorización → OC → recepción → factura → autorización de pago → pago → complemento | `[CONF · Matriz P1]` |
| R2 | OC sin RQ (compra directa de materiales): exige motivo + correo de autorización además de la cotización; recepción por packing list queda `factura_pendiente` | `[CONF · Matriz P2, F-09]` |
| R3 | La RQ nace sin monto; Compras es quien cotiza; el monto y la matriz N1/N2 se evalúan en la OC; RQ va primero a Almacén (surtido total/parcial) y solo el faltante pasa a compra | `[CONF · .local-context/13 Demo 12-oct/04 · Minuta (M5–M7), pedido de Millet 12-oct, transcripción con errores]` |
| R4 | Matriz RQ original (3 capas): naturaleza `Critico`/`Riesgo` ⇒ N1+N2; monto > umbral del departamento ⇒ N1+N2; resto solo N1; la naturaleza más restrictiva gana | `[DISEÑO · compras-requisiciones/01-diseno.md A1, §3.bis.1]`; manual `[DISEÑO · manual-autorizacion-requisiciones.pdf §3]` |
| R5 | N2 de RQ por "monto, disponibilidad o ambos"; límites no definidos | `[CONF · BC/Cuest.02 pregunta 1]` (límites: ABIERTO, §9) |
| R6 | OC: N1 siempre (jefe de Compras); N2 (Dirección) solo si `TotalAPagar` MXN > `UmbralMonto` del departamento de la RQ o de la fila general (`DepartamentoId = Guid.Empty`); sin umbral o sin TC válido ⇒ N2 obligatorio | `[CÓDIGO · Application/Oc/EvaluadorNivelOc.cs]` `[SUPUESTO]` (corte pendiente, §9 A1) |
| R7 | `UmbralMenorCuantia` (N1) < `UmbralMonto` (N2), ambos ≥ 0; N1 solo marca la OC "menor cuantía" en bandeja, no salta N2 | `[CÓDIGO · Application/Umbrales/UpsertUmbralValidator.cs; Domain/UmbralAprobacionDepartamento.cs]` |
| R8 | Modo N2 por disponibilidad (`SoloMonto`=1, `SoloDisponibilidad`=2, `Ambos`=3) vive en el umbral por departamento (default `Ambos`), con `MontoMinimoDisponibilidad` opcional | `[CÓDIGO · Domain/ModoNivel2.cs; Application/Matriz/EvaluadorMultidimensional.cs]` — cambio sin commit del 16-oct (brechas-abiertas.md) |
| R9 | Autorización OC: el comprador titular no puede autorizar (`OC_AUTOAUTORIZACION`); N2 no puede ser la misma persona que N1 (`OC_FIRMA_MISMA_PERSONA`); N2 se rechaza si la OC es de menor cuantía (`OC_AUTORIZAR_NIVEL2_NO_REQUERIDO`) | `[CÓDIGO · Domain/Oc/OrdenCompra.cs ~1073–1180]` |
| R10 | Para enviar OC a autorización: ≥1 línea, todas con precio (`OC_PRECIO_REQUERIDO`), proveedor `Activo` (al enviar y al autorizar), cotización adjunta salvo `CotizacionExcepcionada` + adjunto `correo_autorizacion`, sucursal única por OC, RQ no comprometida en otra OC | `[CÓDIGO · EnviarAAutorizacionOcHandler.cs; Domain/Oc/OrdenCompra.cs]`; `[DISEÑO · OC 01-diseno.md §7.1, C9–C11]` |
| R11 | OC autorizada no se modifica: se cancela y se duplica (`oc_origen_id`); no hay re-autorización | `[DISEÑO · OC 01-diseno.md C4 (cerrada 2026-05-11)]` |
| R12 | Cancelar OC con recepciones registradas exige doble firma | `[CONF · Matriz COM-05, P4]` |
| R13 | Cierre de OC lo calcula Compras: Recepción=`Completa` + Facturación=`Completa` + Pago=`Pagada`; se reabre sola ante devolución a proveedor | `[CONF · Matriz COM-05, P3; manual-ordenes-compra.pdf §8]`; `[CÓDIGO · Domain/Oc/SubEstado*.cs, Application/Oc/Eventos]` |
| R14 | Recepción parcial mantiene la OC abierta con cantidad pendiente; la RQ no cierra porque cierre la OC | `[CONF · BC/Cuest.02 pregunta 16]`; `[CONF · vault Reglas de los manuales conciliadas, "Surtido y cierre RQ"]` |
| R15 | RQ cierra por entrega al solicitante (no por cubrimiento ni por recepción); la recepción solo acumula `CantidadRecibida`; cierre manual excepcional con permiso | `[CÓDIGO · Domain/Requisicion.cs, métodos RegistrarRecepcion y RegistrarEntrega]`; `[DISEÑO · ADR-0043 (estado "Propuesta")]` |
| R16 | RQ `Rechazada` es final: requiere nueva RQ; `Eliminada` solo desde `Borrador`/`EnAutorizacion` | `[DISEÑO · manual-autorizacion-requisiciones.pdf §5; RQ 01-diseno.md §5.2]` |
| R17 | Transmitir RQ exige ≥1 línea; tras transmitir se congela la estructura de líneas | `[CONF · Matriz COM-11]`; `[DISEÑO · manual-requisiciones.pdf §5]` |
| R18 | CC-Máquina (Dim3) obligatorio por línea de RQ y de OC manual; en OC desde RQ se hereda bloqueado; el PDF de OC no lo muestra; alcance de máquinas limita captura, no autorización | `[DISEÑO · manual-requisiciones.pdf §4; manual-ordenes-compra.pdf §4,§7; manual-autorizacion-requisiciones.pdf §6; ADR-0050]` |
| R19 | OC consolidada preserva trazabilidad 1:1 línea-OC ↔ línea-RQ; todas las RQ de una OC comparten `SucursalDestinoId` | `[DISEÑO · OC 01-diseno.md C8, §7.1]` |
| R20 | Tres tolerancias ortogonales: cantidad por línea de OC (la fija Compras y cierra la línea al recibir), cantidad por material (catálogo), monto por proveedor (default global $0.99 MXN, CxP). Fuera de tolerancia se rechaza sin override | `[CONF · Matriz F-10, P4]` — la de línea de OC **no está construida** (§9) |
| R21 | Precio de inventario = precio de OC en variantes A y B; la diferencia con factura se corrige aguas arriba (cancelar/recrear OC o refacturar) | `[CONF · Matriz P2, P4, F-10]` |
| R22 | Alta de proveedor: la solicita Compras y la valida CxP; no migrar proveedores sin movimientos en 24 meses; OC a proveedor en revisión se rechaza | `[CONF · Matriz ADM-22; vault Cruce 14-oct G1.1 P1]`; `[CÓDIGO · Compras/Application/Proveedores]` |
| R23 | Nivel 1 de RQ: jefe del departamento; segundo nivel típicamente jefe de Almacén cuando no hay stock | `[CONF · Matriz F-09 (RQ)]` — titulares y suplentes: ABIERTO |
| R24 | Volumen operativo (diario, normal/pico): 50/60 RQ, 15/35 OC, 15/30 recepciones de insumos | `[CONF · BC/Cuest.02 pregunta 19]` |

## 5. Datos y entidades
- **`Requisicion`** (agregado, `Domain/Requisicion.cs`) con `LineaRequisicion` (artículo, cantidad, unidad, `PrecioEstimado` Money hoy opcional/0, CC-Máquina, `Cubrimiento` con `CantidadDeAlmacen`/`CantidadDeCompra`/`CantidadRecibida`/`CantidadEntregada`), `Autorizacion`. `Clasificacion`: Servicio=0, OrdenCompra=1, MateriaPrima=2, Pinturas=3; `Prioridad`: Baja/Normal/Alta; `OrigenRequisicion`: Manual/Sistema (la RQ por reorden la genera el Sistema, no un usuario) `[CÓDIGO]` `[DISEÑO · manual-requisiciones.pdf §1]`.
- **`EstadoRequisicion`** (smallint): Borrador 0, EnAutorizacion 1, Autorizada 2, EnSurtido 3, Cerrada 4, Cancelada 5, Rechazada 6, Eliminada 7, CerradaSinSurtir 8, CerradaSurtidaParcial 9 `[CÓDIGO · Domain/EstadoRequisicion.cs]`. `SituacionSurtido` derivada: EsperandoCompra, ListoParaSurtir, SurtidoParcial; "En almacén" es solo insignia derivada en el detalle.
- **`OrdenCompra`** (`Domain/Oc/OrdenCompra.cs`): `EstadoOrdenCompra` Borrador 0, EnAutorizacionJefeCompras 1, EnAutorizacionDireccion 2, Autorizada 3, Cerrada 4, Cancelada 5, Rechazada 6, CancelacionSolicitada 7 (CHECK `ck_oc_estado` 0..7). Sub-estados: Recepción {SinRecepcion, Parcial, Completa}, Facturación {SinFactura, Parcial, Completa}, Pago {SinPago, Parcial, Pagada}, columnas materializadas (C7) `[CÓDIGO]`. Campos clave: `RequiereNivel2` (default true), `TotalAPagar`, `TipoCambio` (requerido si moneda ≠ MXN), `SinRequisicionPrevia`, `CotizacionExcepcionada`, `InformacionLogistica`, `InformacionImportacion`, `NumeroPedimento` (8 o 10 dígitos). Impuestos por régimen proveedor+artículo (C3) `[DISEÑO]`.
- **Umbrales**: `UmbralAprobacionDepartamento` (`UmbralMenorCuantia` N1 opcional, `UmbralMonto` N2, `ModoNivel2`, `MontoMinimoDisponibilidad`, vigencia desde/hasta); fila general = `DepartamentoId Guid.Empty` `[CÓDIGO]`. `AprobadorDepartamento` con `RolAprobador` JefeDpto/JefeAlmacen/AutorizadorN2 `[CÓDIGO]`.
- **Folios** `[CÓDIGO · Domain/Folio.cs, Domain/Oc/Folio.cs]`: RQ `^[A-Z]{2,4}\d{4}-\d{6}$` (ej. `MID2026-000001`); OC `OC-{prefijo}{año}-{secuencial:6}` (ej. `OC-MID2026-000001`); secuencia atómica por sucursal y año. Formato RQ `[DISEÑO · compras-requisiciones/01-diseno.md A7, marcada cerrada con el cliente]`.
- **Settings por empresa** `ComprasSettings`: `AutoGenerarOcAlAutorizar` (default false, ADR-0033), `ApartarExistenciaAlAutorizar` (default true). Reserva en Almacén con TTL default 14 días `[DISEÑO · RQ A2]`.
- **Catálogos**: motivos de rechazo (`compras.motivos_rechazo`, opción OTRO con texto libre), tipos de documento OC (`cotizacion`, `ficha_tecnica`, `correo_autorizacion`, `pedimento`, `factura_proveedor_extranjero`, `packing_list`, `otro`) `[DISEÑO]`.
- **Centros de costo**: jerarquía Sucursal → Área → Máquina; hoy 5 sucursales, 57 áreas, 361 máquinas; clave "SGG0102 — LINEA DE PINTADO" `[DISEÑO · manual-centros-de-costo.pdf §1]`. Departamentos con titulares por definir: Mantenimiento, Producción, Comercial, Compras, Rec. Humanos, T.I. `[CONF · Cuest.02 pregunta 2]`. Sucursales/almacenes: Conkal (Insumos, Materias primas con hegla1/hegla2/Bystronic/Interlayer/Silicones, Herrajes, Accesorios), Circuito/Cottolengo/Cancún (Insumos, MP, Herrajes), Planta Pintura (Insumos, MP) `[CONF · Cuest.02 pregunta 3]`.
- Moneda: MXN en v1; otras con TC manual (C1) `[DISEÑO · OC 01-diseno.md]`.

## 6. Contratos
**Publica** (Outbox → topic `compras-events`; `[CÓDIGO · Application/Integration, Application/Integration/Oc]`): `compras.requisicion.{autorizada,cancelada,cerrada,cerrada-manualmente,eliminada,rechazada}.v1`, `compras.orden-compra.{enviada-a-autorizacion,autorizada,rechazada,cancelada,cerrada,reabierta}.v1`. La triada exige además saldo no surtido informativo (`RequisicionSaldoNoSurtidoIntegrationEvent`). Nombre canónico `{Agregado}{Verbo}Event` `[DISEÑO · CLAUDE.md]`.
**Consume** `[CÓDIGO · Infrastructure/Workers/{CxpEventListenerWorker,AlmacenEventListenerWorker}.cs]`: de CxP `cuentas_por_pagar.factura.registrada.v1` (sub-estado Facturación) y `factura.pago-aplicado.v1` (Pago, acumulado por OC calculado por CxP); informativos sin efecto: factura autorizada/cancelada/rechazada-por-tolerancia/diferencia-precio-detectada, nota-credito.registrada, nota-cargo.autorizada, anticipo.capturado, pasivo.autorizado-para-pago, estado-cuenta-tc.cerrado. De Almacén `almacen.oc_recepcion.registrada.v1`, `almacen.oc_devolucion.registrada.v1` (decrementa recibido y reabre OC), `almacen.salida_requisicion.registrada.v1` (entrega de RQ, ADR-0043). Idempotencia por `EventoProcesado` (`Domain/Idempotencia`). `NotaCreditoProveedorRegistradaListener` sigue informativo: PLATFORM-TODO `<NcGranularidadLineaOc>` `[CÓDIGO · CLAUDE.md]`.
**Puertos** (`Domain/Ports/*`): usa `IConsultarStockPort`, `IAlmacenReadPort`, `IAlmacenEntregasReadPort`, `IArticuloReadPort`, `IProveedorReadPort`, `IUsuarioReadPort`, `IDepartamentoReadPort`, `ISucursalDepartamentoReadPort`, `IResolverAutorizadorPort`, `IGenerarSolicitudCompraPort`, `IGenerarPdfOrdenCompraPort`, `IAlmacenarBlobPort`; adaptadores públicos en `Infrastructure/PublicAdapters` (`ComprasOcReadAdapter`, `ComprasRequisicionReadAdapter`, `ComprasCrearRqSistemaAdapter`, `ComprasPedidoVivoReadAdapter`) que consumen Almacén/CxP.
**Permisos** (`backend/src/Identidad/Domain/PermisosCanonicos.cs`): RQ `compras.requisiciones.{leer,crear,editar,eliminar,cancelar,cerrar-manual,autorizar-nivel1,autorizar-nivel2,rechazar,editar-de-otros-usuarios,seleccionar-requisitante,ver-todos-departamentos,leer-todas-sucursales,gestionar-todas-sucursales,adjuntos-ver|subir|baja}`; OC `compras.ordenes.{leer,crear,crear-sin-rq,adjuntar,logistica,autorizar-nivel1,autorizar-nivel2,cancelar,cancelar-doble,cerrar-manual,reportes-partidas-abiertas,leer-todas-sucursales,gestionar-todas-sucursales}`; `compras.aprobadores.administrar`; `compras.configuracion.{leer,editar}` (umbrales y settings reusan estos). Alcance por sucursal: ADR-0051 con bypass `*-todas-sucursales` por recurso; guards `RqSucursalScope.cs`, `OcSucursalScope.cs`.
**Endpoints** `[CÓDIGO · backend/src/Api/Endpoints/Compras]`: `/api/v1/compras/requisiciones` (+`/{id}/lineas`, `transmitir`, `autorizaciones`, `autorizaciones-masivas`, `rechazar`, `eliminar`, `cancelar`, `cerrar-manual`, `cubrimiento-estimado`, `historico`), `/api/v1/compras/pendientes-autorizacion`, `/api/v1/compras/ordenes` (+`desde-requisicion`, `duplicar`, `lineas`, `transmitir`, `autorizaciones`, `autorizaciones-masivas`, `rechazar`, `cancelar`, `cancelar-con-recepciones`, `resolver-cancelacion`, `informacion-logistica`, `informacion-importacion`, `numero-pedimento`, `adjuntos`, `pdf`, `partidas-abiertas`, `partidas-abiertas/kpis`, `requisiciones-disponibles`, `pendientes-autorizacion`), `/api/v1/compras/umbrales-aprobacion` (GET/PUT, `Idempotency-Key`), `/api/v1/compras/aprobadores`, `/api/v1/compras/configuracion`, `/api/v1/compras/trazabilidad`, `/api/v1/compras/articulos`, `/api/v1/compras/motivos-rechazo`.

## 7. Código: dónde vive y trampas
| Qué | Ruta |
|---|---|
| Dominio / Aplicación / Infraestructura (esquema `compras`, `ComprasDbContext`, 89 migraciones) | `backend/src/Compras/{Domain,Application,Infrastructure}`; OC en subcarpeta `Oc/` |
| Workers y listeners | `backend/src/Compras/Infrastructure/Workers`, `Application/Eventos`, `Application/Oc/Eventos` |
| Stubs temporales (`Compras:UseStubs`, solo Development/Test) | `Infrastructure/Stubs/*` (`<StubsTeardown>`, plan en `docs/modulos/compras-ordenes-compra/_teardown-stubs-almacen-cxp.md`) |
| Frontend (exemplar de patrones UI: `frontend/docs/patrones-compras.md`) | `frontend/src/features/compras/{pages,components,api,ordenes}`; rutas `frontend/src/routes/_app/compras/*` |
| Tests | `backend/tests/Compras.UnitTests`, `backend/tests/Compras.IntegrationTests`, `backend/tests/Api.IntegrationTests/Compras/`; Vitest junto a cada componente |
Trampas:
- `PUT /umbrales-aprobacion` concurrente el mismo día devolvía 500; el cambio del 16-oct (sin commit) lo pasa a 409 `UMBRAL_GUARDADO_CONCURRENTE` `[CÓDIGO + brechas-abiertas.md]`. Verificar antes de dar por cerrado.
- `RepartoDisponibilidadService.RepartirAsync` llamaba stock por línea (N+1); cerrado con lote en el cambio sin commit (toca `AlmacenSaldoQueryAdapter`, módulo Almacén).
- `RegistrarRecepcion` en `Requisicion` devuelve siempre `null` (retorno muerto por ADR-0043); no esperar `RequisicionCerradaEvent` desde recepción.
- Con `PrecioEstimado` = 0 el monto de RQ nunca supera el umbral: el N2 de RQ solo sale por naturaleza, disponibilidad o falta de umbral.
- Umbral no recalcula OC ya enviada: `RequiereNivel2` se fija al enviar a autorización.
- Estado OC `CancelacionSolicitada` existe en código pero no en `01-diseno.md` (§5.1 dice 7 estados).
- `e2e/verify/DEMO-COMPRA-PAGO/_demo.ts` lista el permiso `compras.ordenes.transmitir`, que no existe en `PermisosCanonicos` (transmitir exige `compras.ordenes.crear`).
- Reglas de repo: no commitear sin permiso; DbContext propio, nunca tablas de otro módulo (usar puertos/eventos); `PLATFORM-TODO(F1-COM-06)` marca supuestos de comercio exterior (`rg "PLATFORM-TODO" backend/src/Compras`).

## 8. Pruebas
- Verificación reportada 15-oct (no re-ejecutada aquí): Compras integración 162/162, Compras.UnitTests 633, Api integración 1288/1289 (fallo intermitente ajeno), Vitest ~666 `[vault · 86e3pdmej · Implementación §6]`.
- Roles y usuarios: `e2e/seed/identity.local.json` (9 usuarios, entre ellos `super-admin`, `almacen-operativo`, `almacen-corporativo`, `admin-identidad`, `auditor`) y `identity.example.json`; la demo compra-a-pago usa dos cuentas con roles separados (TR-01, `9b03bd20`): solicitante/comprador (`compras.requisiciones.crear`, `compras.ordenes.crear`, `crear-sin-rq`, `adjuntar`) y autorizador (`autorizar-nivel1/2`, `leer-todas-sucursales`). Umbrales de la demo: N1 hasta 1,000 y N2 sobre 10,000, para fila general y departamento Compras `[CÓDIGO · e2e/verify/DEMO-COMPRA-PAGO/_demo.ts]`.
- Escenarios típicos: (1) RQ de 10 unidades, OC por saldo no surtido, recibir 6 y luego 4, factura duplicada rechazada, pago parcial `[DISEÑO · vault Recorrido compra a pago]`; (2) línea crítica entre estándar y montos a ambos lados del umbral; (3) RQ de otra sucursal por URL → 403; (4) OC a proveedor en revisión rechazada; (5) cancelar OC con recepciones con doble firma.
- Specs: `e2e/verify/DEMO-COMPRA-PAGO/{compra-a-pago,huecos-demo,niveles-autorizacion}.spec.ts`, `e2e/verify/EVIDENCIA-COMPRAS-86e3pdmej/{recorrido,recorrido-v2,recorrido-v3,oc-sin-precio}.spec.ts`, `e2e/verify/86e3pc50v/`, `e2e/verify/86e3jvu1p/`; integración `backend/tests/Api.IntegrationTests/Compras/{CompraAPagoDemoTests,NivelOcMenorCuantiaTests,UmbralesAprobacionEndpointsTests,ComercioExteriorOcTests,AutorizacionesMasivas*}`.
- Brecha de evidencia visual: sin capturas de CeCo inválido en línea de RQ ni de RQ de otra sucursal (`.local-context/13 Demo 12-oct/01 · Estado real y brechas.md`, del 8-oct, puede estar vencido).

## 9. Abiertos y supuestos
| # | Pregunta o supuesto | A quién | Mientras tanto | Etiqueta |
|---|---|---|---|---|
| A1 | Corte de N2 en OC y rango válido de umbrales (la minuta habla de 1,000 a 10,000); qué significa exactamente N1 como umbral | Millet, sesión ~19-oct (Fernando Alejos / Dirección) | N2 si monto MXN > `UmbralMonto`; sin umbral ⇒ N2 | ABIERTO |
| A2 | Titulares y suplentes de RQ/OC por departamento | Fernando Alejos / áreas | Aprobadores configurables (`AprobadorDepartamento`) | ABIERTO |
| A3 | Quién crea la OC en la compra parcial y cómo llega lo comprado al requisitante; apartar lo recibido para la RQ de origen | Millet (minuta M5/M6) | Compras crea OC desde el faltante; retorno no construido; otra salida puede consumir lo recibido | ABIERTO |
| A4 | ¿`PrecioEstimado` se elimina del modelo o solo se oculta (M6)? El evaluador lo usa | Eliam | Oculto en UI, se guarda 0 | SUPUESTO |
| A5 | Lista oficial de campos de comercio exterior de la OC (BL incluido); ¿se exigen al crear o al autorizar?; ¿exportación necesita indicador propio? | Millet (M8) / Eliam | Campos supuesto Vilo `PLATFORM-TODO(F1-COM-06)`; CHECK relajado en `97723438` (incoterm y país solo fuera de Borrador/Rechazada) | SUPUESTO |
| A6 | Regla "recurrente" en bandeja (≥3 RQ en 60 días, misma clasificación) | Millet | `VentanaRecurrenciaDias = 60`, `MinimoRecurrencia = 3`, solo marca informativa | SUPUESTO |
| A7 | Tolerancia de cantidad por línea de OC: validada por Millet, no construida | Eliam (brecha, no pregunta) | Solo existen la de material y la de monto de proveedor | ABIERTO |
| A8 | Tolerancia de proveedor en % (ficha la pedía monto o %) | Eliam | Solo monto MXN | ABIERTO |
| A9 | Recepción sin factura (variante B packing list) vs Contabilidad "no hay recepción sin factura; factura de reserva" (Q34) | Contabilidad / Eliam | Variante B construida, cuestionada | ABIERTO |
| A10 | Estado explícito "En almacén" antes de N1/N2 (M5) | Eliam / owner | Insignia derivada | ABIERTO |
| A11 | Envío de OC al proveedor y acuse (F1-COM-10/11); compras MP → A+W (F1-COM-09); cotizaciones comparables (F1-COM-02); incidencias de proveedor (F1-COM-07) | Millet (Compras) | No construido | ABIERTO |
| A12 | Orden de Compra de otra sucursal / compra centralizada y traspaso Conkal→sucursal para herrajes | Fernando Alejos | Restricción de sucursal única por OC | ABIERTO |
| A13 | Naturaleza `Riesgo`: ¿tercer nivel o N2 especializado?; ¿exhaustivas las 4 naturalezas?; auto-aprobación si el solicitante es jefe | Millet | Riesgo = N1+N2 | ABIERTO |
| A14 | Quién es "Gerente de Almacén/Calidad" en dictamen y rol de la cuarentena (afecta devoluciones a proveedor) | Almacén/Calidad | — | ABIERTO |
| C1 | **Contradicción**: sin umbral configurado, `manual-autorizacion-requisiciones.pdf §3` dice que el monto no fuerza N2; el código (`EvaluadorMultidimensional`, `EvaluadorNivelOc`) exige N2 | Eliam | Manda el código (fail-safe); corregir el manual | ABIERTO |
| C2 | **Contradicción**: monto/matriz en la RQ (`compras-requisiciones/01-diseno.md` A1, manuales, Matriz P1/F-09) vs en la OC (minuta 12-oct M5–M7, código `EvaluadorNivelOc`); el evaluador de RQ conserva capa de monto con `PrecioEstimado` | Eliam / Millet | Ambos viven en código | ABIERTO |
| C3 | **Contradicción**: Matriz F-09 "OC: Jefe de Compras y Dirección" (dos niveles) vs código y minuta (N2 solo sobre umbral) | Millet | Código | ABIERTO |
| C4 | **Contradicción**: N2 de RQ = "jefe de Almacén sin stock" (Matriz F-09; vault Cruce G1.11) vs `autorizador_n2_de_sucursal` (`RQ 01-diseno.md` §3.bis.2) vs `RolAprobador.AutorizadorN2` | Eliam | Rol configurable | ABIERTO |
| C5 | **Contradicción de cierre de RQ**: `RQ 01-diseno.md` §5 (cierra por cubrimiento) y guía SRC-023 (cierre al pagar la OC) vs manual/ADR-0043 y código (cierra por entrega). ADR-0043 sigue en estado "Propuesta" | Eliam | Manda el código | ABIERTO |
| C6 | **Contradicción de estado documental**: el expediente `86e3pdmej` dice "sin push" y `ModoNivel2` en `ComprasSettings` y CHECK de importación estricto; `origin/main` ya contiene `320dfd33`/`91a202c4`/`146c0bcd`, el modo vive hoy en el umbral por departamento (sin commit) y el CHECK fue relajado | Geovany | Manda el código | ABIERTO |
| C7 | **Contradicción menor de volumen**: `OC 01-diseno.md` §1.3 (15–45 OC/día) y RQ §1.3 (~70/día legacy) vs Cuest.02 pregunta 19 (RQ 50/60, OC 15/35 normal/pico) | — | Usar Cuest.02 | ABIERTO |
| C8 | Diseño OC dice 7 estados; código tiene 8 (`CancelacionSolicitada`) y CHECK 0..7 | Eliam | Actualizar doc | ABIERTO |
| C9 | **Contradicción documental**: `compras-ordenes-compra/01-diseno.md` (§1.2 y glosario, "Cotización… adjunto, no entidad"; "Cotizaciones formales como entidad separada" fuera de alcance) y `compras-requisiciones/01-diseno.md` §1.2 dejan las cotizaciones fuera de v1; la ficha 86e3jvu2a (16-oct), el plano 1.7.1 `02 · Compras` §7.3 y el Tema 10 de la Lista única las mantienen en Fase 1 | Eliam | Manda la ficha: construido como entidad (C13 del diseño de OC) | ABIERTO |

## 10. Aprendizajes por tarea
(vacío: se llena en el cierre de cada `/develop-task`)
