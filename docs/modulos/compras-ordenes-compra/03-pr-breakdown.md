# PR Breakdown — Submódulo Órdenes de Compra (Compras)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 0.2) y
> [02-plan-implementacion.md](02-plan-implementacion.md) (Rev. 1).
>
> **Estado:** Rev. 1 — propuesta inicial.
> **Fecha:** 2026-05-11.

---

## 0. Cómo leer

- Cada fila es **un PR**. ID `F<fase>-PR<n>` secuencial dentro de la
  fase.
- **Tamaños**: XS (≤ 200 líneas netas), S (200–500), M (500–800).
  Techo absoluto: 800. Por encima, partir.
- **Riesgo de romper main**: bajo / medio / alto. Cada PR debe dejar
  `main` verde y desplegable; el riesgo se refiere al *blast radius*
  si se cuela un bug a `main`.
- **Branch naming**: `compras/oc-<fase>-<slug-corto>` (ej.
  `compras/oc-fase4-creacion-desde-rq`). Prefijo `oc-` para
  diferenciar de PRs de Requisiciones (que usan `compras/<fase>-...`).
- **Dependencias**: PRs previos que deben estar mergeados.
- Al final de cada fase hay una nota de **paralelización**.
- **Reuso primero (regla del proyecto):** cada PR indica explícitamente
  qué pieza hereda. Sin reuso, justificar.

> Convención: PR mergeable = build verde + tests pasando + revisión
> de 1 dev + cobertura del slice (unit donde aplique, integration
> donde haya BD/HTTP). Las migraciones EF Core deben tener un
> `dotnet ef migrations script` revisado a mano y adjunto al PR.

---

## Fase 0 — Foundation OC + permisos canónicos (S)

1 PR consolidado: namespaces + permisos canónicos + smoke endpoint.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F0-PR1 | `compras/oc-fase0-foundation-permisos` | **Estructura de namespaces** dentro de `Millet.Compras` para OC: `Domain/Oc/`, `Application/Oc/`, `Infrastructure/Oc/`. Sin clases todavía (placeholders `.gitkeep`). **Permisos canónicos**: agregar 10 constantes `compras.ordenes.*` a `PermisosCanonicos.Todos` con GUIDs deterministas namespace `00000004-...`. Migración EF Core de seed (auto-detectada por delta `HasData`). **Smoke endpoint** `GET /api/v1/compras/ordenes/smoke` con `[RequirePermission("compras.ordenes.leer")]` que retorna `{ "ok": true }`. Tests 403/200. | `backend/src/Identidad/Domain/PermisosCanonicos.cs`, `backend/src/Identidad/Infrastructure/Migrations/<ts>_PermisosOrdenesCompra.cs`, `backend/src/Compras/Domain/Oc/.gitkeep` (y subcarpetas), `backend/src/Api/Endpoints/Compras/Oc/SmokeEndpoint.cs`, `backend/tests/Api.IntegrationTests/Compras/Oc/PermisosTests.cs` | (cierre de RQ en main) | S | bajo (seed idempotente; endpoint dummy) | Migración aplica; los 10 permisos aparecen en `identidad.permisos`; smoke test verifica 403 sin permiso y 200 con permiso. |

**Paralelización Fase 0**: 1 PR.

---

## Fase 1 — Walking skeleton OC (S)

Replica el patrón F1 de RQ: dominio+tabla en uno, endpoints en otro.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F1-PR1 | `compras/oc-fase1-agregado-y-tabla` | **Dominio**: agregado raíz `OrdenCompra` con campos mínimos de cabecera (§1 §4.1 del diseño). VO `Folio` (parametrizado, prefijo `OC-`) con regex. Enums `EstadoOrdenCompra` (7 valores), `SubEstadoRecepcion`, `SubEstadoFacturacion`, `SubEstadoPago`. Hereda `BaseEntity`, implementa `IPerteneceAEmpresa`, `IAuditable`, `IFiscalmenteRelevante`. **EF Core**: tabla `compras.ordenes_compra` con índices del §10.1 del diseño (incluye `ix_oc_referencia_proveedor`, `ix_oc_partidas_abiertas`, `ix_oc_contenedor`, `ix_oc_ruta`, `ix_oc_semana`). Secuencia `compras.folio_secuencias_oc` (patrón heredado de RQ). CHECK constraints (tipo_cambio, importación). Migración. **Tests**: unitarios del agregado (crear vacía, invariantes de cabecera). | `backend/src/Compras/Domain/Oc/{OrdenCompra,Folio,EstadoOrdenCompra,SubEstadoRecepcion,SubEstadoFacturacion,SubEstadoPago,FolioSecuenciaOc}.cs`, `backend/src/Compras/Infrastructure/Oc/Configurations/OrdenCompraConfiguration.cs`, `backend/src/Compras/Infrastructure/Migrations/<ts>_OrdenesCompraTabla.cs`, `backend/tests/Compras.UnitTests/Oc/Domain/*` | F0-PR1 | M | medio (modelo + migración con 10+ índices) | Migración aplica; `\d compras.ordenes_compra` muestra los CHECK y UNIQUE esperados; tabla vacía. Tests del agregado pasan. |
| F1-PR2 | `compras/oc-fase1-crear-y-obtener` | **Crear**: `CrearOrdenCompraVaciaCommand` + handler MediatR + validator FluentValidation. Folio atómico vía `nextval` de la secuencia. Endpoint `POST /api/v1/compras/ordenes` con `[RequirePermission("compras.ordenes.crear")]`. Mapping con Mapster. **Obtener**: `ObtenerOrdenCompraPorIdQuery` + handler + endpoint `GET .../{id}`. Permiso `compras.ordenes.leer`. DTO con `Version` expuesto en `ETag`. **Tests**: unitarios (handler+validator) + integration (HTTP→BD). | `backend/src/Compras/Application/Oc/CrearOrdenCompraVacia/*`, `backend/src/Compras/Application/Oc/ObtenerOrdenCompraPorId/*`, `backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs` | F1-PR1 | M | medio | `curl POST` → 201 con folio formateado (`OC-MID2026-000001`); `GET` → 200 con `ETag`; 404 ProblemDetails para id inexistente. |

**Paralelización Fase 1**: F1-PR1 → F1-PR2 secuencial.

---

## Fase 2 — Borrador completo (M)

4 PRs: líneas+totales; cabecera completa; logística+importación; adjuntos.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F2-PR1 | `compras/oc-fase2-lineas-y-totales` | Entidad hija `LineaOrdenCompra` (§4.2 del diseño) con `cantidad_recibida` y `cantidad_facturada` en 0 default. VOs `DescuentoLinea`, `IndicadorImpuestos`. Tabla `compras.orden_compra_lineas` con CASCADE + CHECK constraints (cantidad > 0, cantidad_recibida ≤ cantidad, etc.). VO `TotalesOC` (computed). **Motor impuestos v0**: IVA 16% fijo sobre base gravable (motor real con regímenes fiscales en Fase 3). Tests unitarios. | `backend/src/Compras/Domain/Oc/{LineaOrdenCompra,DescuentoLinea,IndicadorImpuestos,TotalesOC,DescuentoGlobal}.cs`, `backend/src/Compras/Infrastructure/Oc/Configurations/LineaOrdenCompraConfiguration.cs`, `backend/src/Compras/Infrastructure/Migrations/<ts>_LineasYTotalesOc.cs` | F1-PR2 | M | medio (modelo + migración) | Migración aplica; CHECK rechazan cantidades inválidas; cálculo de totales con 1, 3 y 10 líneas + descuento global verifica fórmula. |
| F2-PR2 | `compras/oc-fase2-comandos-cabecera-y-linea` | **Cabecera**: `ActualizarCabeceraCommand` + handler + validator + endpoint `PATCH .../{id}`. Permitido en `Borrador` o `Rechazada`. **Líneas**: `AgregarLineaManualCommand`, `ActualizarLineaCommand`, `EliminarLineaCommand` + endpoints REST (`POST/PATCH/DELETE .../lineas`). `AgregarLineaManual` solo si `SinRequisicionPrevia = true` (en Fase 4 se agrega el de RQ). Recalcular totales en cada cambio. **Excepción `Notas`**: campo `texto_adicional` editable en cualquier estado no terminal. | `backend/src/Compras/Application/Oc/ActualizarCabecera/*`, `backend/src/Compras/Application/Oc/Lineas/{AgregarLineaManual,ActualizarLinea,EliminarLinea,ActualizarTextoAdicional}/*`, `backend/src/Api/Endpoints/Compras/Oc/{CabeceraEndpoints,LineasEndpoints}.cs` | F2-PR1 | S | bajo | Tests integration: agregar/editar/eliminar línea en Borrador OK; en Autorizada (vía test seed) → 422; texto_adicional editable siempre que no terminal. |
| F2-PR3 | `compras/oc-fase2-logistica-importacion` | VOs `InformacionLogistica`, `InformacionImportacion`, `ContactoProveedor`, `ReferenciaProveedor`. Columnas inline en `ordenes_compra` (ya creadas en F1-PR1, este PR las cablea al agregado). Comandos `ActualizarInformacionLogisticaCommand` + `ActualizarInformacionImportacionCommand` con endpoints `PATCH .../informacion-logistica` y `.../informacion-importacion`. Logística editable hasta `Autorizada` inclusive (transportista/guía cambian en recepción sin re-auth — §4.6 del diseño). Importación editable solo en `Borrador`/`Rechazada` excepto `NumeroPedimento` que se captura al recibir. | `backend/src/Compras/Domain/Oc/{InformacionLogistica,InformacionImportacion,ContactoProveedor,ReferenciaProveedor}.cs`, `backend/src/Compras/Application/Oc/{ActualizarInformacionLogistica,ActualizarInformacionImportacion}/*`, `backend/src/Api/Endpoints/Compras/Oc/InformacionEndpoints.cs` | F2-PR2 | S | bajo | Tests: capturar logística + importación en Borrador OK; importación sin todos los campos pero con `EsImportacion = true` → validator falla; logística editable post-autorización (vía test seed). |
| F2-PR4 | `compras/oc-fase2-adjuntos-y-blob-stub` | Entidad `AdjuntoOC` + tabla `compras.orden_compra_adjuntos`. Catálogo `compras.tipos_documento_oc` con seed (cotizacion, ficha_tecnica, correo_autorizacion, pedimento, factura_proveedor_extranjero, packing_list, otro). Comandos `AdjuntarDocumentoCommand` + `RemoverAdjuntoCommand`. Endpoints `POST .../adjuntos` (multipart/form-data) y `DELETE .../adjuntos/{id}`. **Stub** `IAlmacenarBlobPort` con `LocalFilesystemBlobStub` (escribe a `/tmp/oc-blobs/` o configurable). **PLATFORM-TODO(`<BlobStorageAzure>`)** apuntando a wireup real en Fase 10. Tests con archivo dummy. | `backend/src/Compras/Domain/Oc/{AdjuntoOC,TipoDocumentoOc}.cs`, `backend/src/Compras/Domain/Ports/Blob/IAlmacenarBlobPort.cs`, `backend/src/Compras/Infrastructure/Oc/Stubs/LocalFilesystemBlobStub.cs`, `backend/src/Compras/Application/Oc/Adjuntos/*`, `backend/src/Compras/Infrastructure/Migrations/<ts>_AdjuntosTiposDocOc.cs`, `backend/src/Api/Endpoints/Compras/Oc/AdjuntosEndpoints.cs` | F2-PR3 | M | medio (multipart + stub blob) | Adjuntar PDF → 201, blob URL devuelto y persistido; quitar → 204; en estado no Borrador, quitar → 422 (adjuntar sigue permitido salvo terminal). |

**Paralelización Fase 2**: secuencial (cada uno toca el agregado).

---

## Fase 3 — Workflow de autorización (M)

3 PRs: autorización (transmitir+N1+N2); rechazar+motor impuestos; cancelar+soft lock.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F3-PR1 | `compras/oc-fase3-autorizar-pipeline` | **Entidad**: `AutorizacionOC` + tabla `compras.orden_compra_autorizaciones` con índice único parcial `(orden_compra_id, nivel) WHERE resultado = 1`. **Motivo rechazo bitmask**: ALTER seed de `compras.motivos_rechazo` para agregar bit `OrdenCompra = 8` al campo `aplica_a` de los motivos relevantes. **Enviar**: `EnviarAAutorizacionCommand` + handler + validator. Valida invariantes pre-auth: ≥1 línea, proveedor activo (C10), cotización adjunta **o** `CotizacionExcepcionada` + correo autorizacion (C11), ficha técnica si `EsImportacion`, motivo + correo si `SinRequisicionPrevia`. Transición `Borrador → EnAutorizacionJefeCompras`. **Autorizar**: `AutorizarOrdenCompraCommand` (con `Nivel`) + handler. Transiciones N1 → EnAutorizacionDireccion, N2 → Autorizada. Setea `FechaContabilizacion = now()` al autorizar N2. Emite eventos in-process `OrdenCompraEnviadaAAutorizacionEvent` y `OrdenCompraAutorizadaEvent`. Endpoints `POST .../{id}/transmitir` y `POST .../{id}/autorizaciones`. Tests del state machine completo. | `backend/src/Compras/Domain/Oc/AutorizacionOC.cs`, `backend/src/Compras/Domain/Oc/Events/{OrdenCompraEnviadaAAutorizacionEvent,OrdenCompraAutorizadaEvent}.cs`, `backend/src/Compras/Application/Oc/{EnviarAAutorizacion,Autorizar}/*`, `backend/src/Compras/Infrastructure/Migrations/<ts>_AutorizacionesOcYMotivosBitmask.cs`, `backend/src/Api/Endpoints/Compras/Oc/AutorizacionEndpoints.cs` | F2-PR4 | M | medio (state machine + validaciones pre-auth) | Captura completa → transmitir → N1 → N2 → Autorizada con FechaContabilizacion. Sin cotización ni excepción → 422 al transmitir. Importación sin ficha técnica → 422. Segundo N1 → 409 (UNIQUE). |
| F3-PR2 | `compras/oc-fase3-rechazar-y-motor-impuestos` | **Rechazar**: `RechazarOrdenCompraCommand` con `motivoId` + `motivoTexto?`. Validators verifican que motivo aplica a OC (bitmask) y, si permite texto libre, exigen texto. Endpoint `POST .../{id}/rechazar`. Transición desde N1 o N2 → `Rechazada`. Evento `OrdenCompraRechazadaEvent`. **Motor de impuestos v1 (C3)**: tabla `compras.regimenes_fiscales_articulo` con seed inicial (16% IVA general, exentos, tasa 0%, retención ISR 10% para servicios profesionales). Lookup por `(regimen_proveedor, regimen_articulo)`. Reemplazar el cálculo simplificado v0 de F2-PR1 por el motor real. Cálculo de `IvaImporte` y `RetencionIsr` por línea. | `backend/src/Compras/Application/Oc/Rechazar/*`, `backend/src/Compras/Domain/Oc/Events/OrdenCompraRechazadaEvent.cs`, `backend/src/Compras/Domain/Oc/RegimenFiscalArticulo.cs`, `backend/src/Compras/Infrastructure/Migrations/<ts>_RegimenesFiscalesYRechazoOc.cs`, `backend/src/Compras/Application/Oc/Impuestos/CalculadorImpuestos.cs` | F3-PR1 | S | medio (cambia cálculo de totales) | Rechazo desde N1 o N2 → Rechazada con motivo. Cálculo: IVA general 1000 × 16% = 160; exento = 0; con retención ISR 10% = -100. Tests parametrizados con regímenes. |
| F3-PR3 | `compras/oc-fase3-cancelar-y-soft-lock` | **Cancelar (sin recepciones)**: `CancelarOrdenCompraCommand` con `motivoId` + `motivoTexto?`. Permitido desde cualquier estado no terminal mientras `SubEstadoRecepcion = SinRecepcion`. Transición a `Cancelada`. Evento `OrdenCompraCanceladaEvent`. La cancelación con recepciones parciales entra en F5-PR4 (necesita listeners primero). Endpoint `POST .../{id}/cancelar`. **Soft lock (ADR 0012 Capa 2)**: declarar `OrdenCompra` en la lista de entidades con soft lock del módulo (la infraestructura ya existe — UF8-PR1 mergeado). | `backend/src/Compras/Application/Oc/Cancelar/*`, `backend/src/Compras/Domain/Oc/Events/OrdenCompraCanceladaEvent.cs`, `backend/src/Compras/Infrastructure/Collaboration/EntidadesConSoftLock.cs` (extensión), `backend/src/Api/Endpoints/Compras/Oc/CancelarEndpoint.cs` | F3-PR2 | S | medio (state machine + soft lock wiring) | Cancelar desde Borrador → Cancelada. Cancelar desde Autorizada sin recepciones → Cancelada. Dos compradores abriendo la misma OC → SignalR notifica "X está editando" (funcional contra hub real, no mock). |

**Paralelización Fase 3**: secuencial.

---

## Fase 4 — Creación desde RQ + compromiso exclusivo (M)

3 PRs: ALTER + selector; flujos de creación 1:1 y N:1; liberación.

> **PLATFORM-TODO(`<rq-compromiso>`)**: este bloque modifica el agregado
> `Requisicion` para agregar `comprometida_en_oc_id`. Coordinar con
> el equipo de RQ — si están trabajando en paralelo, mergear F4-PR1
> primero para evitar conflictos.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F4-PR1 | `compras/oc-fase4-rq-compromiso-y-selector` | **Ampliar agregado `Requisicion`**: agregar `ComprometidaEnOcId : OrdenCompraId?` + métodos `ComprometerEnOc(ocId)` y `LiberarDeOc()`. Migración aditiva: `ALTER TABLE compras.requisiciones ADD COLUMN comprometida_en_oc_id uuid REFERENCES compras.ordenes_compra(id)` + índices parciales `ix_requisiciones_comprometida` e `ix_requisiciones_disponibles` (incluye `sucursal_id` para el selector). **Query** `ListarRequisicionesDisponiblesParaConsolidarQuery` con filtro `estado = Autorizada AND comprometida_en_oc_id IS NULL AND sucursal_id = :sucursalOc` (restricción §10.5). Endpoint `GET .../ordenes/requisiciones-disponibles?sucursalId=...`. Tests: 2 sucursales, RQ comprometida no aparece. | `backend/src/Compras/Domain/Requisicion.cs` (métodos nuevos), `backend/src/Compras/Infrastructure/Configurations/RequisicionConfiguration.cs` (extensión), `backend/src/Compras/Infrastructure/Migrations/<ts>_RequisicionComprometidaEnOc.cs`, `backend/src/Compras/Application/Oc/ListarRequisicionesDisponiblesParaConsolidar/*`, `backend/src/Api/Endpoints/Compras/Oc/RequisicionesDisponiblesEndpoint.cs` | F3-PR3 | M | **alto** (ALTER de tabla central de RQ + métodos en agregado existente) | Migración aplica sin downtime. RQ comprometida no aparece en el selector. RQ de otra sucursal no aparece. Tests de RQ existentes siguen pasando. |
| F4-PR2 | `compras/oc-fase4-creacion-desde-rq` | **Flujo 1:1 (§4.1)**: `CrearOrdenCompraDesdeRequisicionCommand` + handler. Pre-llena cabecera de la RQ + líneas con FK a `RequisicionId`/`LineaRequisicionId`. Emite `RqComprometidaEnOcEvent` que el handler `RqComprometidaEnOcListener` (in-proc, mismo BC) consume para llamar `Requisicion.ComprometerEnOc(ocId)`. Endpoint `POST .../ordenes/desde-requisicion` con `{ requisicionId, proveedorId }`. **Flujo N:1 (§4.2)**: `AgregarLineaDesdeRequisicionCommand` agrega líneas adicionales a OC en Borrador desde RQs del selector. Valida `lineaRq.sucursalId == oc.sucursalDestinoId`. Endpoint `POST .../ordenes/{id}/lineas/desde-requisicion`. **Política de preservación**: mismo `articulo_id` de RQs distintas → líneas separadas (no se suman). Tests cubren ambos flujos. | `backend/src/Compras/Application/Oc/CrearOrdenCompraDesdeRequisicion/*`, `backend/src/Compras/Application/Oc/Lineas/AgregarLineaDesdeRequisicion/*`, `backend/src/Compras/Domain/Oc/Events/RqComprometidaEnOcEvent.cs`, `backend/src/Compras/Application/Oc/Eventos/RqComprometidaEnOcListener.cs` | F4-PR1 | M | medio (cross-agregado in-proc) | Crear desde RQ → OC con líneas heredadas + RQ marcada como comprometida. Agregar 3 RQs misma sucursal → 3 grupos de líneas separados. Agregar RQ de otra sucursal → 422. |
| F4-PR3 | `compras/oc-fase4-liberacion` | **Liberación al editar**: ampliar `EliminarLineaCommand` (de F2-PR2): si la línea tiene `requisicion_id`, emite `LineaRqLiberadaEvent` con `requisicion_id`. Listener actualiza la RQ con `LiberarDeOc()` **solo si era la última línea de esa RQ en la OC** (verificar con query). **Liberación al cancelar**: ampliar `CancelarOrdenCompraCommand` (de F3-PR3): tras transicionar a `Cancelada`, emite eventos de liberación para todas las RQs comprometidas (solo si OC sin recepciones — el caso parcial entra en F5-PR4). Tests: cancelar OC con 3 RQs → 3 RQs vuelven al pool; eliminar última línea de RQ → RQ vuelve al pool; eliminar línea pero RQ tiene otra → RQ sigue comprometida. | `backend/src/Compras/Application/Oc/Lineas/EliminarLinea/*` (extensión), `backend/src/Compras/Application/Oc/Cancelar/*` (extensión), `backend/src/Compras/Domain/Oc/Events/LineaRqLiberadaEvent.cs`, `backend/src/Compras/Application/Oc/Eventos/LineaRqLiberadaListener.cs` | F4-PR2 | S | medio (efectos cross-agregado) | Suite de liberación pasa: cancelar libera N RQs; eliminar última línea de RQ libera 1; quedan otras líneas → RQ sigue comprometida. |

**Paralelización Fase 4**: secuencial.

---

## Fase 5 — Sub-estados + listeners cross-BC + cierre automático (M)

4 PRs. F5-PR3 (reemplazo del stub `InMemoryGenerarSolicitudCompraPort`) y F5-PR4 (cancelación con recepciones parciales) se aíslan por riesgo.

> **PLATFORM-TODO(`<oc-stub-replace>`)**: F5-PR3 desconecta el
> `OcBorradorStub` que hoy usa el flujo de bifurcación de RQ. Es el
> "punto de no retorno" de OC: hasta este PR, RQ podía operar con
> stubs si OC no existía; después, RQ depende de OC real. Coordinar
> con equipo de RQ y validar en staging con datos reales antes de
> mergear a `main`.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F5-PR1 | `compras/oc-fase5-sub-estados-y-recalculo` | **Sub-estados materializados**: columnas ya existen en `ordenes_compra` (creadas en F1-PR1). Este PR agrega el método `RecalcularSubEstados()` en el agregado: itera líneas y calcula `SubEstadoRecepcion` (basado en `cantidad_recibida`), `SubEstadoFacturacion` (basado en `cantidad_facturada`), `SubEstadoPago` (basado en agregación de facturas y pagos — para v1, usa `monto_pagado` denormalizado). Método público `RegistrarRecepcionLinea(lineaId, cantidad)` que actualiza la línea + llama recálculo. Si las 3 dimensiones cierran, transición a `Cerrada` + evento `OrdenCompraCerradaEvent`. Tests parametrizados: línea por línea, mixto, todo recibido. | `backend/src/Compras/Domain/Oc/OrdenCompra.cs` (métodos `RecalcularSubEstados`, `RegistrarRecepcionLinea`, etc.), `backend/src/Compras/Domain/Oc/Events/OrdenCompraCerradaEvent.cs`, `backend/tests/Compras.UnitTests/Oc/SubEstadosTests.cs` | F4-PR3 | M | medio (lógica central del cierre automático) | Tests del recálculo cubren: SinRecepcion (todos 0), Parcial (mixto), Completa (todos al máximo). Cierre automático dispara cuando los 3 son Completa/Completa/Pagada. |
| F5-PR2 | `compras/oc-fase5-listeners-cross-bc-stub` | **Contratos cross-BC nuevos** en `Compras.Domain.Ports.Cxp/`: `FacturaProveedorRegistradaEvent`, `NotaCreditoProveedorRegistradaEvent`. En `Compras.Domain.Ports.Tesoreria/`: `PagoFacturaProveedorEvent`. En `Compras.Domain.Ports.Almacen/`: `OcDevolucionRegistradaEvent`. **Listeners** en `Application/Oc/Eventos/`: `OcRecepcionRegistradaListener` (consume el contrato existente), `FacturaProveedorRegistradaListener`, `NotaCreditoProveedorRegistradaListener`, `PagoFacturaProveedorListener`, `OcDevolucionRegistradaListener`. Cada listener actualiza la línea correspondiente vía repositorio, llama `RecalcularSubEstados`. Idempotencia: payload contiene "cantidad acumulada" (set), no incremento. Si la OC estaba `Cerrada` y llega devolución, transición de regreso a `Autorizada` con sub-estado `Parcial`. Tests con eventos mock disparados manualmente. | `backend/src/Compras/Domain/Ports/{Cxp,Tesoreria,Almacen}/*.cs` (eventos nuevos), `backend/src/Compras/Application/Oc/Eventos/{OcRecepcionRegistradaListener,FacturaProveedorRegistradaListener,NotaCreditoProveedorRegistradaListener,PagoFacturaProveedorListener,OcDevolucionRegistradaListener}.cs`, `backend/tests/Compras.IntegrationTests/Oc/Listeners/*` | F5-PR1 | M | medio (5 listeners, cada uno con su test) | Cada listener probado: recepción suma cantidad_recibida y recalcula; factura suma cantidad_facturada; pago avanza sub_pago; devolución decrementa y revierte cierre. Idempotencia: aplicar mismo evento 2x = 1x. |
| F5-PR3 | `compras/oc-fase5-reemplazo-stub-oc-borrador` | **Reemplazo del stub** `InMemoryGenerarSolicitudCompraPort` por implementación real: cuando RQ bifurca y genera saldo de compra, llama `IGenerarSolicitudCompraPort.GenerarAsync(...)`, OC ahora crea una OC en `Borrador` real (no escribe a `compras.oc_borrador_stub`). Cabecera mínima: proveedor sugerido si la RQ lo trae, sucursal heredada, comprador = creador de la RQ (default reasignable). Líneas con `requisicion_id` + `linea_requisicion_id`. **Deprecar tabla** `compras.oc_borrador_stub` con migración que la deja vacía + comment SQL deprecating. **NO se borra todavía** — el borrado físico se hace dos releases después con verificación de que ninguna fila quedó. Tests: bifurcación de RQ con saldo → OC real creada, RQ marcada como comprometida. **PR aislado por blast radius cross-BC.** | `backend/src/Compras/Infrastructure/Oc/GenerarSolicitudCompraDesdeRq.cs` (implementación real), `backend/src/Compras/Infrastructure/Stubs/InMemoryGenerarSolicitudCompraPort.cs` (borrar wiring DI), `backend/src/Compras/Infrastructure/Stubs/OcBorradorStub.cs` (deprecar), `backend/src/Compras/Infrastructure/Migrations/<ts>_DeprecarOcBorradorStub.cs`, `backend/src/Api/Program.cs` (cambiar registración DI) | F5-PR2 | M | **alto** (cross-BC, cambia comportamiento de RQ en producción) | Tests existentes de RQ siguen pasando con la OC real en lugar del stub. Spot-check manual: RQ con stock parcial → OC real visible en `/api/v1/compras/ordenes/{id}`. Tabla `oc_borrador_stub` vacía tras run. |
| F5-PR4 | `compras/oc-fase5-cancelar-con-recepciones-parciales` | **Cancelar con recepciones parciales**: ampliar `CancelarOrdenCompraCommand` (de F3-PR3). Requiere permisos `compras.ordenes.cancelar.doble` + `.autorizar.nivel1` + `.autorizar.nivel2` (validador chequea los 3). Las cantidades ya recibidas permanecen en la línea (no se decrementan). Para cada línea con RQ asociada, calcula `cantidad_no_recibida = cantidad - cantidad_recibida` y emite `LineaRqLiberadaEvent` con `cantidad_liberada = cantidad_no_recibida`. Si la cantidad no recibida es 0, la línea queda asociada a la OC cancelada (trazabilidad contable) y la RQ no se libera por esa línea. Tests: cancelar con 50% recibido → 50% del pool de RQ regresa. **PR aislado por riesgo: maneja edge cases de inventario/contable.** | `backend/src/Compras/Application/Oc/Cancelar/*` (extensión), `backend/src/Compras/Application/Oc/Eventos/LineaRqLiberadaListener.cs` (extensión para liberación parcial), `backend/tests/Compras.IntegrationTests/Oc/CancelarConRecepcionesTests.cs` | F5-PR3 | S | **alto** (edge cases de inventario y contable) | Tests cubren: 0% recibido → libera 100%; 50% recibido → libera 50%; 100% recibido → no libera. Doble auth verifica los 3 permisos. |

**Paralelización Fase 5**: secuencial.

---

## Fase 6 — PDF al proveedor + duplicar OC (M)

3 PRs: stub PDF + listener; duplicar OC; librería PDF real + bandejas.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F6-PR1 | `compras/oc-fase6-pdf-stub-y-listener` | Puerto `IGenerarPdfOrdenCompraPort` en `Compras.Domain.Ports.Pdf/`. **Stub** `LocalPdfOrdenCompraStub` que devuelve PDF placeholder (texto plano con folio + total). Tabla `compras.orden_compra_pdf` con FK 1:1 a `ordenes_compra` + `blob_url`. Listener `OrdenCompraAutorizadaListener` (síncrono, dentro del handler de autorizar N2): genera PDF, persiste blob URL. Servicio `GenerarPdfOrdenCompraService` que **agrupa líneas por artículo** y oculta `departamento_solicitante_id`, `requisicion_id` (§3.bis.4 del diseño). Endpoint `GET .../ordenes/{id}/pdf` que devuelve el blob. **PLATFORM-TODO(`<PdfService>`)** apuntando a wireup real en F6-PR3. | `backend/src/Compras/Domain/Ports/Pdf/IGenerarPdfOrdenCompraPort.cs`, `backend/src/Compras/Infrastructure/Oc/Pdf/{LocalPdfOrdenCompraStub,GenerarPdfOrdenCompraService}.cs`, `backend/src/Compras/Application/Oc/Eventos/OrdenCompraAutorizadaListener.cs` (extensión), `backend/src/Compras/Infrastructure/Migrations/<ts>_OrdenCompraPdf.cs`, `backend/src/Api/Endpoints/Compras/Oc/PdfEndpoint.cs` | F5-PR4 | M | medio (listener síncrono dentro del handler crítico de Autorizar) | Autorizar OC → PDF placeholder generado + descargable. Consolidación con 3 líneas mismo artículo → PDF muestra 1 línea con cantidad sumada. |
| F6-PR2 | `compras/oc-fase6-duplicar-oc` | **Comando `DuplicarOrdenCompraCommand(ocOrigenId)`** (C4 cerrado como cancelar + recrear). Valida que `ocOrigen.Estado IN (Cancelada, Rechazada)`. Crea una OC nueva en `Borrador`, copia cabecera (proveedor, sucursal, moneda, condiciones de pago, uso principal, banderas `EsImportacion`/`SinRequisicionPrevia`, observaciones, info logística e info importación si aplica) y **líneas manuales** (sin FK a RQ — el comprador re-selecciona si quiere consolidar; las RQs originales fueron liberadas al cancelar la OC origen). **NO copia**: adjuntos (cotización debe ser nueva), autorizaciones, eventos previos, sub-estados, vínculos a RQs. Setea `oc_origen_id` en la nueva. Emite `OrdenCompraDuplicadaEvent`. Endpoint `POST .../ordenes/{ocOrigenId}/duplicar`. Query `ObtenerOrdenCompraOrigenQuery` para navegar desde una OC duplicada a su origen. Tests: duplicar desde Cancelada → OK; desde Borrador → 422; verificar que adjuntos no se copian. | `backend/src/Compras/Application/Oc/DuplicarOrdenCompra/*`, `backend/src/Compras/Domain/Oc/Events/OrdenCompraDuplicadaEvent.cs`, `backend/src/Compras/Application/Oc/ObtenerOrdenCompraOrigen/*`, `backend/src/Api/Endpoints/Compras/Oc/DuplicarEndpoint.cs` | F6-PR1 | S | bajo (read de OC origen + insert de OC nueva, sin state machine compleja) | Cancelar OC con N líneas → duplicar → OC nueva en Borrador con las N líneas pre-llenadas (manuales, sin RQ), sin adjuntos, sin autorizaciones, con `oc_origen_id` apuntando a la cancelada. Endpoint con permiso `compras.ordenes.crear`. |
| F6-PR3 | `compras/oc-fase6-pdf-real-y-bandejas` | **Librería PDF: QuestPDF** (decisión cerrada en Rev. 3 del 02-plan). Layout institucional con datos fiscales Millet + proveedor, líneas agrupadas, condiciones de pago, fecha, firma. Reemplazar `LocalPdfOrdenCompraStub` por `QuestPdfOrdenCompraImpl`. Borrar `PLATFORM-TODO(<PdfService>)`. **Bandejas básicas**: queries `ListarOrdenesCompraQuery` con filtros (estado, sub-estados, proveedor, comprador, fechas, importe, referencia proveedor) + paginación offset-based + `ListarPendientesAutorizacionOcQuery`. Endpoints `GET .../ordenes` y `.../ordenes/pendientes-autorizacion`. Read models planos vía proyección EF. | `backend/src/Compras/Infrastructure/Oc/Pdf/QuestPdfOrdenCompraImpl.cs` (reemplaza stub), `backend/src/Compras/Application/Oc/{ListarOrdenesCompra,ListarPendientesAutorizacion}/*`, `backend/src/Api/Endpoints/Compras/Oc/BandejasEndpoints.cs`, `Directory.Packages.props` (agregar QuestPDF) | F6-PR2 | M | medio (nueva dependencia + queries con índices) | PDF generado con layout institucional reconocible. Bandejas P95 < 200ms con dataset seed de 1k OCs. Filtros y paginación funcionan. |

**Paralelización Fase 6**: F6-PR1 → F6-PR2 → F6-PR3 secuencial.

---

## Fase 7 — Reportes operativos (M)

3 PRs: partidas abiertas; árbol de documentos (cross-módulo); últimas 100 compras + bandejas adicionales.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F7-PR1 | `compras/oc-fase7-partidas-abiertas` | Query `ListarPartidasAbiertasQuery` con filtros completos (§7 del 02-plan + §8.2 del 01-diseño): estado, sub_recepcion, sub_facturacion, sub_pago, proveedor, comprador, fecha rango, contenedor, ruta, semana, importe, días atrasados (computed contra `fecha_entrega_esperada`). Endpoint `GET .../ordenes/partidas-abiertas` con permiso `compras.ordenes.reportes.partidas_abiertas`. **Benchmark** con dataset seed de 5k OCs activas + 50k históricas (cerradas, no aparecen). Si P95 > 500ms, agregar índices o vista materializada `compras.vw_partidas_abiertas`. Reporte de benchmark adjunto al PR. | `backend/src/Compras/Application/Oc/ListarPartidasAbiertas/*`, `backend/src/Api/Endpoints/Compras/Oc/PartidasAbiertasEndpoint.cs`, `backend/tests/Compras.IntegrationTests/Oc/PartidasAbiertasPerfTests.cs`, `tools/perf/seed-5k-ocs.ps1` | F6-PR3 | M | medio (perf-sensitive con índice complejo `ix_oc_partidas_abiertas`) | Reporte de benchmark con P50/P95 antes/después de cualquier optimización. Filtros combinados funcionan. |
| F7-PR2 | `compras/oc-fase7-arbol-documentos` | Query `ObtenerArbolDocumentosQuery` cross-módulo: recibe `(tipoDocumento, id)` y construye el árbol RQ → OC → Recepción → Factura → Pago bidireccionalmente. Endpoint `GET .../trazabilidad/arbol-documentos?desde={tipo}&id={id}`. **Promover como cross-módulo desde el inicio**: vive en `Compras.Application.Trazabilidad` con interfaz `IObtenerArbolDocumentosService` que en v1 solo conoce RQ y OC; CxP, Recepción y Tesorería se conectan en el futuro implementando providers adicionales. Permiso `compras.ordenes.leer`. Tests con seed de OC con 2 RQs + 2 recepciones + 1 factura + 1 pago (stubeado): árbol completo navegable. | `backend/src/Compras/Application/Trazabilidad/ObtenerArbolDocumentos/*`, `backend/src/Compras/Domain/Trazabilidad/IObtenerArbolDocumentosService.cs`, `backend/src/Compras/Infrastructure/Trazabilidad/ArbolDocumentosImpl.cs`, `backend/src/Api/Endpoints/Compras/Trazabilidad/ArbolDocumentosEndpoint.cs` | F7-PR1 | S | bajo (read-only sobre tablas existentes) | Desde una OC, árbol muestra 2 RQs upstream + 2 recepciones + 1 factura + 1 pago downstream. Desde una RQ, árbol muestra la OC que la consume + descendientes. |
| F7-PR3 | `compras/oc-fase7-historial-bandejas-kpis` | **Sizing: M con nota de monitoreo.** Si el PR cruza 800 líneas netas (techo M), partir en F7-PR3a (3 queries: historial + hermanas + KPIs — los que alimentan UF7 del frontend) y F7-PR3b (Últimas 100 + bandejas presets + duplicaciones). **Últimas 100 compras** (§8.5 del 01-diseño): query `ListarUltimas100ComprasMaterialQuery` con filtros (proveedor, fecha, cantidad mínima, tipo de documento). Endpoint `GET .../articulos/{id}/historial-compras`. **Historial de OC** (brecha §14.1 del 05 — confirmada P0): query `ObtenerHistoricoOrdenCompraQuery` que arma timeline cronológico leyendo `core.audit_log` filtrado por `entity_type='OrdenCompra'` y `entity_id`, agrupando eventos por tipo (creación, transmisión, autorizaciones, recepciones, facturas, pagos, cancelación, duplicación). Endpoint `GET /ordenes/{id}/historico`. **OCs hermanas duplicadas** (brecha §14.2 del 05): query `ListarOcsHermanasDuplicadasQuery` con filtro `WHERE oc_origen_id = :ocOrigenId`. Endpoint `GET /ordenes/{id}/duplicadas`. **KPIs de partidas abiertas** (brecha §14.4 del 05 — FOC10 del 05): query `ObtenerKpisPartidasAbiertasQuery` que devuelve 4 agregados (monto pendiente recibir, pendiente facturar, pendiente pago, count atrasadas) reactivos a los mismos filtros que F7-PR1. Endpoint `GET /ordenes/partidas-abiertas/kpis`. **Bandejas adicionales**: vistas filtradas predefinidas (mis borradores, autorizadas pendientes recepción, etc.) como `ListarOrdenesCompraQuery` con preset. **KPI de duplicaciones**: query `ListarOrdenesCompraDuplicadasQuery` con filtro por comprador + período. | `backend/src/Compras/Application/Oc/{ListarUltimas100ComprasMaterial,ObtenerHistoricoOrdenCompra,ListarOcsHermanasDuplicadas,ObtenerKpisPartidasAbiertas,ListarOrdenesCompraDuplicadas}/*`, `backend/src/Compras/Application/Oc/Bandejas/*Preset.cs`, `backend/src/Api/Endpoints/Compras/Oc/{HistorialEndpoint,HistoricoOcEndpoint,DuplicadasEndpoint,KpisPartidasAbiertasEndpoint,BandejasPresetEndpoints}.cs` | F7-PR2 | M | bajo (read-only sobre tablas existentes + audit_log) | Históricos de material para 3 proveedores y 2 períodos verificados manualmente. Historial de OC con ciclo completo (capturar → autorizar → recibir → facturar → pagar) cronológicamente ordenado. KPIs reactivos a filtros. Cada preset de bandeja devuelve solo OCs en el estado/sub-estado correcto. |

**Paralelización Fase 7**: secuencial.

---

## Fase 8 — Eventos de integración + Notificaciones (S)

2 PRs si la infra Outbox de RQ ya está en main; 4 si OC es el primer consumidor real (en cuyo caso se reusa el patrón F6 del 03-pr-breakdown de RQ).

> **Nota crítica**: revisar estado del PR de Outbox de RQ (F6-PR1 y
> F6-PR2 de su breakdown) antes de iniciar. Si no están en main, se
> mergean primero como parte de la plataforma compartida. Este
> breakdown asume que YA están y OC solo agrega sus eventos.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F8-PR1 | `compras/oc-fase8-eventos-integracion` | Wirear emisión de 6 eventos de integración versionados al Outbox (asume `IIntegrationEventPublisher` real ya wireado por RQ): `compras.orden-compra.enviada-a-autorizacion.v1`, `.autorizada.v1`, `.rechazada.v1`, `.cancelada.v1`, `.cerrada.v1`, `.reabierta.v1`. Mappers desde domain events a integration events. Test E2E con consumer dummy verificando publicación y orden. Si los PRs de Outbox de RQ no están en main, este PR los incluye (consolidado +M) — coordinar con dueño de plataforma. | `backend/src/Compras/Application/Oc/Integration/*`, `backend/src/Compras/Application/Oc/Eventos/*` (decoradores), `docs/integraciones/compras-oc-eventos.md` | F7-PR3 | S (o M si incluye Outbox) | medio | Eventos llegan al Service Bus emulador en dev en el orden correcto; payload v1 verificable. |
| F8-PR2 | `compras/oc-fase8-coordinacion-notificaciones` | Documento de contrato de eventos (`docs/integraciones/compras-oc-eventos.md`) con shape de cada evento v1. Plantillas de email coordinadas con módulo Notificaciones (cuando exista): "OC pendiente de autorización" (a N1, luego a N2), "OC autorizada" (al comprador), "OC rechazada con motivo" (al comprador). Si Notificaciones no existe todavía, este PR queda doc-only y los emails se conectan en una fase futura. Actualizar §12 del 01-diseño retirando `<Outbox>` si quedó esa pieza. | `docs/integraciones/compras-oc-eventos.md`, `docs/integraciones/compras-oc-plantillas-notif.md`, `docs/modulos/compras-ordenes-compra/01-diseno.md` (§12) | F8-PR1 | XS | bajo | Doc revisado por owner; §12 actualizada. |

**Paralelización Fase 8**: F8-PR1 → F8-PR2 secuencial. Pueden ejecutarse en paralelo con F9 si Notificaciones todavía no existe.

---

## Fase 9 — Catálogos seed completos para OC (M)

Completar los catálogos seed que OC necesita y que no existen o vienen
incompletos del trabajo previo de Requisiciones. **No incluye
importación desde SAP ni pantallas de CRUD** — ambos son bloque
separado post-MVP (mapa funcional §10 "Decisiones resueltas").

1 PR consolidado: las tablas son aditivas, el riesgo es bajo, los
seeds y endpoints `GET` siguen un patrón ya validado en RQ.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F9-PR1 | `compras/oc-fase9-catalogos-seed` | **Tablas nuevas** en schema `compartido`: `compartido.incoterms`, `compartido.transportistas`, `compartido.regimenes_fiscales`, `compartido.condiciones_pago` (las que no existan ya del trabajo de RQ). Entidades, configuración EF inline en `CompartidoDbContext`, migración aditiva. **Seeds versionados** con `HasData` (o `IHostedService` que se autoexcluye en `Production`) — datos validados con cliente / CxP / Contabilidad antes del PR. **Endpoints GET read-only** en `/api/v1/catalogos/...` con permiso `compartido.catalogos.leer`. **Sin POST/PATCH/DELETE** — pantallas de CRUD son bloque post-MVP. **Cleanup**: verificar que los validators de OC encuentran las refs y dejan de caer en valores hardcoded de Fases 2–3. | `backend/src/SharedKernel/Domain/{Incoterm,Transportista,RegimenFiscal,CondicionesPago}.cs`, `backend/src/SharedKernel/Infrastructure/Persistence/CompartidoDbContext.cs` (extensión), `backend/src/SharedKernel/Infrastructure/Persistence/Migrations/Compartido/<ts>_CatalogosOcSeed.cs`, `backend/src/Api/Endpoints/Catalogos/{Incoterms,Transportistas,RegimenesFiscales,CondicionesPago}Endpoints.cs`, `backend/tests/Api.IntegrationTests/Catalogos/*` | F8-PR2 | M | bajo | Migración aplica; seeds cargan en dev/QA; cada endpoint GET responde 200 con la lista; 403 sin permiso. Flujos end-to-end de OC (Fases 2–7) operan contra catálogos seed sin valores hardcoded. |

**Paralelización Fase 9**: 1 PR. F9-PR1 paralelizable con F8-PR1
(distintas capas).

> **Diferido post-MVP (bloque separado, no parte de OC v1):**
> pantallas de CRUD para administración de catálogos + importación
> inicial desde SAP B1 (proveedores, artículos, condiciones de pago,
> regímenes fiscales) + migración de OCs históricas si el cliente la
> requiere. El modelo soporta migración aditiva cuando se reabra
> (ver §11 del 01-diseño).

---

## Fase 10 — Hardening + UAT (M)

3 PRs: idempotency en endpoints + observabilidad; perf + OpenAPI; runbook + UAT + blob real.

> **Nota**: el middleware de idempotency-key (ADR-0020) vive en
> `SharedKernel`. RQ ya lo introdujo (F8-PR1 de su breakdown); OC
> solo decora sus endpoints.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F10-PR1 | `compras/oc-fase10-idempotency-audit-y-observabilidad` | **Idempotency audit** (decoradores `[RequireIdempotencyKey]` ya aplicados desde el inicio de cada PR — decisión cerrada en Rev. 2 del 04): este PR audita la cobertura. Test parametrizado que escanea endpoints de OC y verifica que cada POST de mutación está decorado. Documentar en OpenAPI. **Observabilidad**: custom dimensions en Serilog para `OrdenCompraId`, `Folio`, `Estado`, `EmpresaId`. `Activity` spans en handlers críticos (`Autorizar`, `Cancelar`, `RegistrarRecepcion`, `DuplicarOrdenCompra`). Dashboards en Application Insights (definidos como JSON o link). | `backend/src/Compras/Application/Oc/**` (decoradores MediatR), `docs/operacion/dashboards-compras-oc.md`, `backend/tests/Api.IntegrationTests/Compras/Oc/IdempotencyCoverageTests.cs` | F9-PR1 | S | bajo | Audit de cobertura pasa. Traces aparecen en App Insights con `OrdenCompraId` searchable. |
| F10-PR2 | `compras/oc-fase10-perf-y-openapi` | **Perf bandeja + partidas abiertas**: seed de 50k OCs (mezcla de estados). Benchmark con BenchmarkDotNet de las queries pesadas (`ListarOrdenesCompraQuery`, `ListarPartidasAbiertasQuery`, `ObtenerArbolDocumentosQuery`). Si P95 > 300ms, evaluar índices adicionales o vista materializada. **OpenAPI**: XML doc comments en cada endpoint de OC, request, response. `[ProducesResponseType]` para todos los códigos esperados. Verificación de drift del JSON generado. | `tools/perf/seed-50k-ocs.ps1`, `backend/src/Compras/Infrastructure/Migrations/<ts>_IndicesOcOptimizacion.cs` (condicional), `backend/src/Api/Endpoints/Compras/Oc/**` (XML docs) | F10-PR1 | M | medio (índices afectan escritura) | Reporte de benchmark adjunto. `/scalar/v1` muestra el submódulo OC completo con descripciones. |
| F10-PR3 | `compras/oc-fase10-runbook-uat-blob-real` | **Blob real**: reemplazar `LocalFilesystemBlobStub` por implementación Azure Blob Storage (`AzureBlobOrdenCompraImpl`) usando `BlobServiceClient`. Migrar blobs existentes en `/tmp/oc-blobs/` a Azure (script). Borrar `PLATFORM-TODO(<BlobStorageAzure>)`. **Runbook** (`docs/operacion/runbook-compras-oc.md`): cómo cancelar OC con factura asociada (proceso manual CxP primero), cómo duplicar una OC cancelada, cómo investigar conflictos 409, cómo manejar PDFs corruptos. **Plan UAT** (`docs/operacion/plan-uat-compras-oc.md`) con grupo piloto (Rodrigo + 1 comprador + 1 Director) cubriendo los 12 indicadores de éxito del §12 del 01-diseño. | `backend/src/Compras/Infrastructure/Oc/Pdf/AzureBlobOrdenCompraImpl.cs`, `backend/src/Compras/Infrastructure/Oc/Blob/AzureBlobAdjuntoImpl.cs`, `tools/migracion-blob/migrate-local-to-azure.csx`, `docs/operacion/runbook-compras-oc.md`, `docs/operacion/plan-uat-compras-oc.md` | F10-PR2 | M | medio (cambia storage backend) | UAT firmado por el cliente. Blobs ya viven en Azure; los stubs filesystem desaparecen del wiring. |

**Paralelización Fase 10**: secuencial.

---

## Resumen de PRs por fase

| Fase | PRs Rev. 1 | Sizing fase | Notas |
|---|---|---|---|
| 0 — Foundation + permisos | 1 | S | Consolidado en 1 PR; XS pero conjunto. |
| 1 — Walking skeleton OC | 2 | S | Dominio+tabla; endpoints. |
| 2 — Borrador completo | 4 | M | Líneas; comandos; logística+importación; adjuntos. |
| 3 — Workflow autorización | 3 | M | Autorizar pipeline; rechazar+impuestos; cancelar+soft lock. |
| 4 — Creación desde RQ + compromiso | 3 | M | ALTER+selector; flujos 1:1 y N:1; liberación. |
| 5 — Sub-estados + listeners + cierre | 4 | M | F5-PR3 (reemplazo stub) y F5-PR4 (cancelar con recepciones) aislados por riesgo. |
| 6 — PDF + duplicar OC | 3 | M | Stub PDF+listener; duplicar OC (C4); librería real+bandejas. |
| 7 — Reportes operativos | 3 | M | Partidas abiertas; árbol docs; historial+bandejas. |
| 8 — Eventos integración | 2 | S | Eventos OC; coordinación Notif. |
| 9 — Catálogos seed completos para OC | 1 | M | Sin migración SAP en MVP — bloque separado post-MVP. |
| 10 — Hardening + UAT | 3 | M | Audit idempotency+observabilidad; perf+OpenAPI; blob real+runbook+UAT. |
| **Total** | **29** | | Camino crítico 4–5 meses con 2 backend devs. |

---

## Notas de proceso

### Convenciones de PR

- **Branch**: `compras/oc-<fase>-<slug-corto>`. Ejemplos:
  `compras/oc-fase5-listeners-cross-bc-stub`,
  `compras/oc-fase9-catalogos-seed`.
- **Título**: imperativo, conciso, ≤ 72 caracteres.
- **Body**: referencia al PR ID (`F1-PR1`), fase, deps en main. Si
  hay nuevos `PLATFORM-TODO`, listarlos.

### Reglas duras

- Cada PR deja `main` verde y desplegable.
- Migraciones EF Core: `dotnet ef migrations script` adjunto al PR;
  revisión manual obligatoria.
- Si un PR cruza 800 líneas netas, se parte. Excepción justificada
  en el body.
- Sin `PLATFORM-TODO` huérfanos: cada uno debe estar en §12 del 01-diseño.

### Cuándo escalar

- **F4-PR1** (ALTER de `compras.requisiciones`): coordinar con dueño
  de RQ; mergear cuando no haya PRs de RQ abiertos que toquen la
  misma tabla.
- **F5-PR3** (reemplazo del stub `OcBorradorStub`): coordinación
  cross-BC + validación en staging con datos reales obligatoria.
  Code review por dos personas.
- **F5-PR4** (cancelar con recepciones parciales): edge cases
  contables; validar con CxP antes de mergear.
- **F8-PR1** (eventos integración): si Outbox de RQ no está en main,
  consolidar la infraestructura aquí; coordinación con dueño de
  plataforma.
- **F10-PR3** (Azure Blob real): coordinación con TI para Storage
  Account y credenciales en Key Vault.

---

## Cambios respecto a versiones previas

### Rev. 5 — F7-PR3 con nota de monitoreo de sizing (2026-05-11)

Alineado con 02-plan Rev. 5 (sincronización backend↔frontend
explícita):

- F7-PR3 mantiene sizing M pero agrega nota explícita: si cruza
  800 líneas netas, partir en F7-PR3a (3 queries que alimentan UF7
  frontend: historial + hermanas + KPIs) y F7-PR3b (Últimas 100 +
  bandejas presets + duplicaciones). Decisión a tomar al implementar.

### Rev. 4 — endpoints para frontend incluidos en F7-PR3 (2026-05-11)

Tras cierre del 05-frontend Rev. 2, las 3 brechas backend confirmadas
se incluyen en F7-PR3 (no se difieren a tickets posteriores):

- **F0-PR1**: lista de constantes ya dice "10 constantes
  `compras.ordenes.*`" — coincide con FOC11 que agrega `crear.sin_rq`.
  Sin cambio en el conteo pero sí en el contenido.
- **F7-PR3 renombrado** de `compras/oc-fase7-historial-y-bandejas` a
  `compras/oc-fase7-historial-bandejas-kpis` y extendido con:
  - `ObtenerHistoricoOrdenCompraQuery` + endpoint `/historico`
    (brecha §14.1).
  - `ListarOcsHermanasDuplicadasQuery` + endpoint `/duplicadas`
    (brecha §14.2).
  - `ObtenerKpisPartidasAbiertasQuery` + endpoint
    `/partidas-abiertas/kpis` (brecha §14.4 / FOC10).
- Sizing F7-PR3 sigue M (queries triviales sobre tablas existentes
  + `core.audit_log`).

### Rev. 3 — cierre de decisiones backend (2026-05-11)

Tras 3 rondas de decisiones con owner alineadas al 02-plan Rev. 3:

- **Fase 6 reescrita**: F6-PR2 cambia de "reapertura + versiones" a
  **"duplicar OC"** (C4 cerrado como cancelar + recrear). El sizing
  de F6-PR2 baja de M a S (sin state machine compleja ni JSONB
  snapshots). Total de Fase 6 sigue siendo 3 PRs.
- **F7-PR3 ajustado**: reemplaza el reporte de reaperturas
  (`ListarVersionesOrdenCompraQuery`) por KPI de duplicaciones
  (`ListarOrdenesCompraDuplicadasQuery`).
- Permisos: eliminado `compras.ordenes.reabrir` (de 10 a 9).

### Rev. 2 — catálogos sin migración SAP en MVP (2026-05-11)

Correcciones tras feedback del owner alineadas al 02-plan Rev. 2:

- **Fase 9 reescrita**: de "Catálogos reales + migración SAP (L)" con
  3 PRs (F9-PR1 catálogos + F9-PR2 extracción + F9-PR3 carga) a
  "Catálogos seed completos para OC (M)" con **1 PR consolidado**
  (`F9-PR1 compras/oc-fase9-catalogos-seed`). Eliminados F9-PR2 y
  F9-PR3 (scripts de migración SAP).
- **Resumen de PRs**: total baja de 31 (o 32) a **29 PRs**.
- **"Cuándo escalar"**: eliminada la entrada de F9-PR2/PR3.
- **Filosofía de consolidación** sin cambios: los PRs aislados por
  riesgo siguen siendo F4-PR1, F5-PR3 y F5-PR4.

### Rev. 1 — versión inicial (2026-05-11)

Primer corte del breakdown. Calibrado contra el plan Rev. 1 y el
audit del repo. 31 PRs totales (32 con opciones de histórico SAP),
camino crítico 4–5 meses con 2 backend devs.

**Filosofía de consolidación** (regla del proyecto: PRs S–M, no
microscópicos):

- PRs XS afines se agrupan dentro de cada fase (F0, F8-PR2, F2-PR2).
- PRs de alto riesgo se aíslan: F4-PR1 (ALTER tabla central), F5-PR3
  (reemplazo de stub cross-BC), F5-PR4 (cancelar con recepciones).
- Versus 03-pr-breakdown de RQ (34 PRs tras consolidación), OC tiene
  31 PRs porque hereda toda la foundation. Las fases nuevas que no
  tiene RQ (sub-estados, PDF, árbol de documentos) compensan el
  ahorro de F0.
