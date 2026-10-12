# Guía campo por campo — Almacén (preparación + recepción parcial 6 de 10)

**Propósito.** Que Eliam repita en su laptop el recorrido de Almacén de la demo sin adivinar: qué se escribe o elige en cada campo, con qué valor exacto y por qué. Fuente del recorrido real: `.verification/demo-tour/almacen/pasos.md` y sus capturas (misma carpeta). Las razones salen del código (`backend/src/Almacen/**`, `frontend/src/features/almacen/**`) y de `docs/modulos/almacen/00-levantamiento.md`; lo que no se pudo sustentar está marcado **(razón no verificada)**.

Ambiente de la demo: web `http://localhost:5173`, API `http://localhost:5080`, base `millet_demo`, 12 de octubre de 2026, idioma es-MX.

## Quién hace qué

| Paso | Quién | Permiso que lo habilita |
|---|---|---|
| Todos (1 a 7) | **Geovany** (`geovanny@uzieltzaboutlook.onmicrosoft.com`, rol `super-admin`) | Tiene todos los permisos, incluidos los bypass `almacen.entradas.gestionar-todas-sucursales` y `almacen.entradas.leer-todas-sucursales` |
| Alternativa (si Geovany no lo hace) | Fernando Aguilar, rol `almacen-jefe` | `almacen.entradas.registrar`, `almacenes/ubicaciones/asignaciones.administrar` y los bypass de entradas (ver `demo-geovany-usuarios-roles-y-datos.md`) |

Un usuario **sin** los bypass solo puede registrar una recepción si está asociado (`UsuarioSucursal`) a la sucursal de la OC y de la ubicación; si no, el API responde 403 `SUCURSAL_NO_ASOCIADA` o `SUCURSAL_NO_DETERMINADA` (`DocumentoSucursalScope`, endpoint `POST /api/v1/almacen/recepciones`). No existe "asignar usuarios a almacenes".

## Prerrequisitos (deben existir ANTES de abrir "Nueva recepción")

| # | Dato | Valor en la demo | Por qué | Si falta |
|---|---|---|---|---|
| 1 | **OC en estado Autorizada** | `OC-MID2026-000007` (proveedor `DEMO-PROV-ACT`, sucursal MID; ART-CRT-01 x10 PZA a $250.00, ART-EST-01 x20 PZA a $35.00) | El buscador solo lista OC Autorizadas con líneas pendientes (`soloConPendienteRecepcion`, estado por omisión Autorizada); el backend exige estado `Autorizada` | `RECEPCION_OC_NO_AUTORIZADA`; con cancelación solicitada: `OC_CANCELACION_SOLICITADA` |
| 2 | Líneas recibibles con artículo y precio MXN > 0 | Ambas líneas son artículos de catálogo, no servicios | Los servicios no se reciben en Almacén (CA2.10); el costo de entrada se toma del precio de la OC | `RECEPCION_ARTICULO_NO_EN_OC`; `RECEPCION_COSTO_OC_INVALIDO` |
| 3 | **Sucursal destino de la OC = sucursal del almacén** | OC MID, almacén `ALM-MID-G` (Planta México Centro) | Lo pide `demo-geovany-usuarios-roles-y-datos.md` §4. En el código no hay una validación explícita de igualdad en el handler; para usuarios sin bypass el endpoint exige asociación a la sucursal de la OC y del rack. **(razón de la igualdad exacta no verificada en código)** | Con Geovany no falla; con usuarios operativos: 403 de sucursal |
| 4 | **Periodo contable abierto** para la fecha del movimiento | Periodo 2026-10 abierto (lo abrió "DEMO sembrador" el 2026-10-12) | Almacén consulta Contabilidad antes de registrar (`PeriodoCerradoValidator`); Almacén no deja registrar con fecha de mes cerrado (§10.3 del levantamiento) | `PERIODO_CONTABLE_NO_ADMITE` (cerrado o no abierto en Contabilidad) o `PERIODO_CERRADO` (cierre mensual de Almacén). Se abre en Contabilidad con `contabilidad.periodo.administrar` |
| 5 | **Ubicación real (rack)** en el sub-almacén de la sucursal | `ALM-MID-G > INSUMOS · R-A1` "Rack A1 - insumos MID" (ya existía; no se creó) | La ubicación `ÚNICA` es el "default de enrutamiento" y solo se drena por salidas | `ENTRADA_A_UBICACION_UNICA` |
| 6 | **Asignación activa artículo → rack** para cada artículo que se recibe | `ART-CRT-01` y `ART-EST-01` → `R-A1` (Paso 2) | Política de negocio: solo se entra donde el artículo está asignado. El selector de rack de cada línea lista únicamente racks asignados a ese artículo | `ENTRADA_SIN_ASIGNACION` |
| 7 | **Unidad del artículo** activa y de la misma dimensión que la de la OC | PZA en ambos | La conversión de unidades exige unidad activa y misma dimensión (en el seed hay PZA y M2; no hay HOJA) | `UNIDAD_INACTIVA`, `UNIDAD_DIMENSION_INCOMPATIBLE`, `UNIDAD_EQUIVALENCIA_REQUERIDA` |
| 8 | **Emulador de Service Bus** encendido (`ServiceBus:Modo=EmuladorLocal`; `tools/demo/demo.sh eventos` o `tools/servicebus-emulator/levantar.sh`) | Encendido durante el recorrido | El "Recibido / Por surtir" de Compras llega por Outbox → Service Bus → `AlmacenEventListenerWorker` de Compras. Sin Service Bus el backend descarta el evento y Compras no se entera (la recepción en Almacén sí queda registrada) | La OC en Compras sigue en Recibido 0.00 |
| 9 | Login real de Microsoft con MFA hecho una vez por cuenta | Geovany | Sin sesión no hay token | — |

---

## Paso 1. Menú de Almacén

- **Ruta / quién:** Geovany. Icono "Almacén" de la barra izquierda; abre `/almacen/recepciones`.
- **Captura:** `01-menu-almacen.png`.

| Elemento | Qué hacer | Por qué |
|---|---|---|
| Icono "Almacén" (barra izquierda) | Un clic desde Inicio | Abre el módulo; el menú lateral aparece con tres bloques |
| Menú lateral — Operación | Solo mirar: Recepciones, Salidas, Devoluciones, Inventario físico, Saldos, Consulta jerárquica | Recepciones es donde se registra la entrada; Saldos se revisa al final (Paso 7) |
| Menú lateral — Cierre y reportes | Solo mirar: Cierre de mes, Reportes | No se usan en la demo. No ejecutar "Cierre de mes": cerraría el mes y bloquearía recepciones (`PERIODO_CERRADO`) |
| Menú lateral — Configuración | Solo mirar: Almacenes, Sub-almacenes, Reabasto, Ubicación de artículos, Ubicaciones | Se usan en el Paso 2 |
| Selector "Sucursal" (barra superior) | Dejar en "Todas las permitidas" | Geovany tiene los bypass; filtrar por sucursal solo recorta listados |

- **Debe verse:** bandeja de Recepciones con 10 recepciones previas (`M-ENT2026-000001` a `000010`).
- **Si sale error:** ninguno esperado. Si el menú sale vacío o con aviso rojo "Requiere el permiso X", la sesión no tiene el rol; cerrar sesión y volver a entrar (o esperar 5 min tras asignar rol).

---

## Paso 2. Preparación: almacenes, sub-almacenes, ubicaciones y asignaciones

Este paso es de **preparación** (se hace una vez por OC/sucursal). Las pantallas 2a a 2d son de consulta; solo se escribe en 2e (si hace falta rack) y en 3 y 4 (asignaciones).

### 2a. Almacenes (consulta) — `/almacen/almacenes` — `02a-almacenes.png`

| Dato visible | Valor en la demo | Por qué importa |
|---|---|---|
| Almacenes activos | `ALM-MID-G` Almacén General MID (Planta México Centro), `ALM-MID-MP` Materia Prima MID, `ALM-MTY-G`, `ALM-QRO-G` | La OC es de sucursal MID, así que el destino es un almacén MID: se usó `ALM-MID-G` (insumos y refacciones). `ALM-MID-MP` también sirve para MID pero para materia prima |

- Botones: solo lectura en la demo (no crear almacenes).

### 2b. Sub-almacenes (consulta) — `/almacen/sub-almacenes` — `02b-sub-almacenes.png`

| Dato visible | Valor en la demo | Por qué importa |
|---|---|---|
| 12 sub-almacenes (INSUMOS, MAT-DIR, MAT-REV por almacén) | Se usa `ALM-MID-G > INSUMOS` "Insumos y refacciones" | Variante A es para insumos y refacciones (levantamiento §5.2); materiales directos no-vidrio (interlayer, silicones, pinturas) van a MAT-DIR con variante B |
| Regla de unicidad | Una recepción = un solo sub-almacén | Todas las líneas deben caer en racks del mismo sub-almacén; si no: `RECEPCION_MULTI_SUBALMACEN` |

### 2c. Ubicaciones (consulta) — `/almacen/ubicaciones` — `02c-ubicaciones.png`

| Dato visible | Valor en la demo | Por qué importa |
|---|---|---|
| Fila `ÚNICA` con etiqueta "default de enrutamiento" en cada sub-almacén | No usar para recibir | `ENTRADA_A_UBICACION_UNICA`: "Las entradas exigen una ubicación real; la ÚNICA solo se drena por salidas." Tampoco admite asignaciones (`ASIGNACION_A_UNICA`) |
| Racks reales en `ALM-MID-G > INSUMOS` | `R-A1` "Rack A1 - insumos MID" y `R-01` "Rack 1 DEMO"; se **reutilizó `R-A1`** | Ya existían (creados antes de la demo); no hace falta crear nada si ya hay un rack real en el sub-almacén |
| Filtros "Sub-almacén" y "Estatus" | Dejar en "Todos los sub-almacenes" / "Todos" | Solo recortan la lista |
| Iconos por fila (lápiz, baja) | No usar | Editar solo cambia clave y nombre (el sub-almacén no se puede mover); la baja desactiva el rack |

### 2e. Nueva ubicación (solo si NO hay rack real) — botón "Nueva ubicación" — `02e-nueva-ubicacion-formulario-sin-guardar.png`

En la demo esta captura es solo del formulario; **no se guardó nada**.

| Campo | Qué poner (valor de la demo) | Por qué / validación |
|---|---|---|
| **Sub-almacén** (obligatorio) | `ALM-MID-G > INSUMOS` | El rack nace dentro de un sub-almacén y no se puede mover después. Vacío: "Sub-almacén requerido". Debe ser el sub-almacén de la sucursal de la OC |
| **Clave** (obligatorio) | Ejemplo del placeholder: `HG1-84` (el rack existente se llama `R-A1`) | Máximo 20 caracteres; única dentro del sub-almacén. Vacío: "La clave es requerida". Repetida: `UBICACION_CLAVE_DUPLICADA` ("Ya existe una ubicación con esa clave en el sub-almacén.") |
| **Nombre** (obligatorio) | Texto descriptivo, p. ej. "Rack A1 - insumos MID" | Máximo 254 caracteres. Vacío: "El nombre es requerido" |
| (sin campo de estatus) | — | La ubicación nace **Activa**; no se elige |

| Botón | Qué hace |
|---|---|
| **Crear ubicación** | Crea el rack (Idempotency-Key fresca por intento). Toast verde "Ubicación creada" |
| **Cancelar** / X | Cierra sin guardar |

### 2d. Asignaciones antes — `/almacen/asignaciones` — `02d-asignaciones-antes.png`

| Dato visible | Valor en la demo | Por qué importa |
|---|---|---|
| Asignaciones activas | Ninguna para `ART-CRT-01` ni `ART-EST-01` | Sin asignación, la recepción falla con `ENTRADA_SIN_ASIGNACION`: "El artículo no está asignado a la ubicación elegida. Asígnalo primero." |

### 3. Asignar `ART-CRT-01` — "Ubicación de artículos" > botón "Asignar artículo"

Capturas: `03a-asignar-ART-CRT-01-formulario.png`, `03b-asignar-ART-CRT-01-resultado.png`. Ruta: Almacén > Configuración > Ubicación de artículos (`/almacen/asignaciones`).

| Campo | Qué poner (valor de la demo) | Por qué / validación |
|---|---|---|
| **Artículo** (obligatorio) | Buscar y elegir `ART-CRT-01 · Refacciones críticas para línea de producción` | Es el artículo de la línea #1 de la OC. Debe existir y estar **activo**; si no: `ARTICULO_NO_ENCONTRADO` / `ARTICULO_INACTIVO` ("El artículo está inactivo; no se puede asignar.") |
| **Ubicación** (obligatorio) | `ALM-MID-G › INSUMOS · R-A1` | Debe ser un rack real. La `ÚNICA` se rechaza con `ASIGNACION_A_UNICA`. Si ya está asignado y activo: `ASIGNACION_DUPLICADA` ("El artículo ya está asignado a esta ubicación."). Si la asignación existía inactiva, volver a asignar la **reactiva** |

| Botón | Qué hace |
|---|---|
| **Asignar artículo** | Crea la asignación activa y **crea el saldo del artículo en 0** en esa ubicación (lo dice el panel: "Se creará su existencia en 0 ahí."). Toast verde "Artículo asignado a la ubicación" |
| **Cancelar** / X | Cierra sin guardar |

- **Debe verse:** la fila nueva en la bandeja, estatus Activo. El toast verde tapa unos segundos el selector de sucursal y el botón "Nuevo" (no es error).

### 4. Asignar `ART-EST-01`

Capturas: `04a-asignar-ART-EST-01-formulario.png`, `04b-asignar-ART-EST-01-resultado.png`, `04c-asignaciones-despues.png`.

| Campo | Qué poner (valor de la demo) | Por qué |
|---|---|---|
| **Artículo** | `ART-EST-01 · Papelería de oficina genérica` | Es la línea #2 de la OC. Aunque en la demo solo se reciba la línea #1, se asigna también para poder recibir la #2 después sin volver a preparar |
| **Ubicación** | `ALM-MID-G › INSUMOS · R-A1` (el mismo rack) | Mismo sub-almacén para toda la recepción. Dos artículos en el mismo rack es válido |

- **Estado final** (`04c`): `ART-EST-01` y `ART-CRT-01` ambos en `ALM-MID-G > INSUMOS · R-A1`, Activo.
- **Si sale error:** `ASIGNACION_DUPLICADA` (ya estaba asignado: no hay que repetir), `ASIGNACION_A_UNICA` (elegiste la ÚNICA), `ARTICULO_INACTIVO`, `UBICACION_NO_ENCONTRADA` (rack dado de baja o inexistente).

---

## Paso 3. Nueva recepción (Variante A, UUID manual) con recepción parcial

- **Ruta / quién:** Geovany. `/almacen/recepciones` > botón **Nuevo** (barra superior) o el botón de la bandeja "Nueva recepción" > panel lateral derecho "Nueva recepción".
- **Capturas:** `05a-nueva-recepcion-inicio.png`, `05b-nueva-recepcion-oc-elegida.png`, `05c-recepcion-formulario-cabecera.png`, `05d-recepcion-formulario-linea-6-de-10.png`.
- **Orden recomendado de captura:** tipo → OC → ubicación por defecto → fecha → CFDI/UUID → observaciones → marcar línea → cantidad → registrar. **Elige la OC antes que la ubicación por defecto**: la ubicación por defecto solo se copia a las filas que ya existen; las filas se crean al elegir la OC y nacen sin rack (`construirFilas`, `aplicarUbicacionHelper`). Si eliges la ubicación primero y la OC después, el rack de cada línea queda vacío y habrá que elegirlo línea por línea.

### 3.1 Cabecera

| Campo | Qué poner (valor de la demo) | Por qué / qué pasa si falta o está mal |
|---|---|---|
| **Tipo de recepción** (obligatorio, por omisión Variante A) | **Variante A — Con factura/CFDI** ("Insumos y refacciones — el proveedor llega con factura") | ART-CRT-01 y ART-EST-01 son insumos/refacciones; el proveedor entrega con factura. Variante B es para materiales directos no-vidrio con packing list (ver sección "Variante A vs B"). Cambiar de variante **reinicia el formulario** |
| **Orden de compra** (obligatorio) | Escribir `OC-MID2026-000007` y elegirla. El buscador lista solo OC Autorizadas con líneas pendientes (salieron la 000007 y la 000005) | La recepción siempre es contra OC (levantamiento §5.6). Al elegirla se cargan sus líneas pendientes y el costo viene de la OC. Vacío: "OC requerida". OC no autorizada: `RECEPCION_OC_NO_AUTORIZADA` ("La OC no está en estado que acepte recepción."). Al cambiar de OC se descarta el CFDI elegido (ya no corresponde al proveedor) |
| **Ubicación por defecto (opcional)** | `ALM-MID-G › INSUMOS · R-A1` | Ayuda de captura: copia ese rack a las líneas que aún no tienen rack y se guarda para reportería. **No sustituye** al rack de cada línea (el de la línea es el que manda). Vacío: "Sin ubicación por defecto", sin problema si cada línea trae su rack |
| **Fecha de movimiento** (obligatorio) | `12/10/2026` (hoy; viene por omisión). Formato interno `2026-10-12` | Determina el periodo contable y el año del folio (`M-ENT2026-…`). Debe caer en un periodo **abierto**: si no, `PERIODO_CONTABLE_NO_ADMITE` o `PERIODO_CERRADO`. Rango válido del año: 2020 a 2099 ("Fecha de movimiento fuera de rango razonable.") |
| **CFDI del proveedor** (obligatorio en variante A) — selector "CFDIs por procesar de {RFC}…" | **No usado en la demo** (se dejó vacío y se pasó al UUID manual) | En la demo el selector no tenía un CFDI utilizable para `DEMO-PROV-ACT` (solo CFDIs por procesar PRB2001, PRB1002 que no corresponden al proveedor). Si hubiera uno del proveedor, se elige aquí y se manda `cfdiRecibidoId`. Aparece solo si el usuario tiene `cuentas_por_pagar.cfdis.leer` |
| **Cargar XML** (botón junto al selector) | No usado | Abre una hoja apilada para cargar el XML del CFDI (canal CargaManual de CxP); requiere `cuentas_por_pagar.cfdis.cargar-manual`. Al cargar, el CFDI queda vinculado |
| Enlace **¿El CFDI no está en el sistema? Captura el folio fiscal del impreso** | Pulsarlo | Cambia al modo de folio fiscal manual (limpia el CFDI elegido). Sin permiso de leer CFDIs, este modo es el único y sale directo |
| **Folio fiscal (UUID del impreso)** (obligatorio si no hay CFDI vinculado) | `6F1D2A9E-4B7C-4E58-9A31-0D5C8B7E1F42` (inventado para la demo) | Vínculo fiscal obligatorio en variante A (decisión 2026-07-14, levantamiento §5.4): sin CFDI vinculado ni UUID no se registra. Formato exigido: UUID 8-4-4-4-12 hex (`^[0-9A-Fa-f]{8}-…-[0-9A-Fa-f]{12}$`), error "Folio fiscal con formato UUID (8-4-4-4-12 hex)" / "El folio fiscal no cumple el formato UUID del SAT". Se guarda en mayúsculas. Sin CFDI y sin UUID: "Vincula el CFDI del proveedor o captura su folio fiscal (UUID del impreso)." CxP enlaza el CFDI real cuando llegue el XML por su canal |
| Enlace **Mejor vincular un CFDI del sistema** | No usar | Regresa al selector y borra el UUID capturado |
| **Observaciones (opcional)** | `Recepcion parcial demo - 6 de 10 Refacciones criticas` | Texto libre que queda en el comentario del movimiento y viaja en el evento. Máximo 500 caracteres. Vacío: no pasa nada |

### 3.2 Líneas de la OC ("Líneas de la OC (2 pendientes)")

Cada línea muestra: **Línea #N**, artículo, UM, y tres datos de solo lectura: **Solicitada**, **Ya recibida**, **Pendiente**. Solo se listan líneas con pendiente > 0 y que no sean servicio.

| Campo (por línea) | Línea #1 `ART-CRT-01` (la que se recibe) | Línea #2 `ART-EST-01` | Por qué / validación |
|---|---|---|---|
| **Casilla "Incluir línea N en la recepción"** | **Marcada** | **Sin marcar** | Solo las líneas marcadas se envían. Debe haber al menos una: si no, "Marca al menos una línea para recibir." La línea #2 queda con Recibido 0.00 / Por surtir 20.00 |
| Solicitada / Ya recibida / Pendiente | 10 / 0 / 10 | 20 / 0 / 20 | Pendiente = Solicitada − Ya recibida (nunca negativo). Es la base de Por surtir |
| **Unidad capturada (opcional)** | Vacía (el placeholder muestra `PZA`) | — | Vacía = se recibe en la unidad de la OC (PZA). Solo se llena si llegó en otra unidad del catálogo (p. ej. `CAJA`); el sistema convierte y exige equivalencia configurada (`UNIDAD_EQUIVALENCIA_REQUERIDA`, `UNIDAD_DIMENSION_INCOMPATIBLE`, `UNIDAD_INACTIVA`). Máximo 20 caracteres. En la demo no se usa |
| **Cantidad a recibir** (obligatorio) | **6** (por omisión trae 10 = el pendiente; se cambió a 6) | — | Es la recepción parcial. Debe ser mayor a cero ("La cantidad debe ser mayor a cero."). Respeta los decimales de la unidad (PZA no admite fracciones: la pantalla marca error de decimales por unidad y el servidor responde `CANTIDAD_DECIMALES_EXCEDE_UNIDAD`). Ver nota de tolerancia abajo |
| Ayuda "Máximo con 5% tolerancia: 10.5" | Solo informativa | — | La pantalla calcula 10 × 1.05. Ver nota de tolerancia |
| **Ubicación (rack)** (obligatorio en filas marcadas) | `ALM-MID-G › INSUMOS · R-A1`, con la etiqueta verde **coincide** | — | "coincide" = es el mismo rack que "Ubicación por defecto". El selector lista **solo racks asignados a ese artículo** (por eso el Paso 3/4). Vacío: "Elige la ubicación (rack) destino." / "La ubicación (rack) es obligatoria en la entrada." Rack ÚNICA: `ENTRADA_A_UBICACION_UNICA`. Sin asignación: `ENTRADA_SIN_ASIGNACION`. Racks de sub-almacenes distintos entre líneas: `RECEPCION_MULTI_SUBALMACEN` |
| **Comentario (opcional)** | Vacío | — | Nota por línea, máximo 500 caracteres. No afecta nada |

> **Nota de tolerancia (verificada en código).** La pantalla permite capturar hasta pendiente × 1.05 (10.5) y muestra "Máximo con 5% tolerancia". Pero el backend valida contra `saldo pendiente + cantidad solicitada × tolerancia del artículo / 100`, y hoy el adaptador de artículos entrega tolerancia `null` (= 0%) (`ArticuloReadAdapter`, `RecepcionOcGuard`). En consecuencia, con los datos actuales **el máximo real es el pendiente exacto (10)**; capturar 10.5 pasa la pantalla pero el API responde `RECEPCION_EXCEDE_TOLERANCIA` ("La cantidad total recibida supera el saldo pendiente con tolerancia (…)"). Para la demo usar siempre ≤ pendiente. Si se llena "Unidad capturada", la pantalla no aplica el tope de 5% (la conversión se valida en el servidor).

### 3.3 Botones

| Botón | Qué hace |
|---|---|
| **Registrar recepción** | Valida en pantalla y envía `POST /api/v1/almacen/recepciones` con Idempotency-Key (un doble clic no duplica). Muestra "Registrando…" mientras responde |
| **Cancelar** / X | Cierra el panel sin registrar nada |
| Botón flotante de la esquina inferior derecha (icono de isla) | No es parte del flujo; tapa parcialmente "Registrar recepción" pero sigue siendo clicable por los lados |

- **Petición real de la demo:** 201 `{"recepcionId":"01a12f9d-e2b4-7439-b8ef-051dd22e3ec6","folio":"M-ENT2026-000011"}`. Cuerpo: `cfdiRecibidoId: null`, `cfdiUuidFiscal` con el UUID, `ubicacionHelperId` = id de `R-A1`, una línea con `cantidad: 6` y `ubicacionId` = `R-A1`.
- **Debe verse:** toast verde "Recepción M-ENT2026-000011 registrada" y navegación automática al detalle (Paso 4). No sale error de periodo contable.
- **Qué hace el sistema al registrar** (una sola transacción): valida OC, periodo, unidad, decimales, rack y asignación; crea el movimiento `EntradaCompra` (Registrado) con costo = precio de la OC; actualiza el saldo del rack; reserva folio `M-ENT{año}-{secuencia}`; guarda el evento `almacen.oc_recepcion.registrada.v1` en el Outbox.

### 3.4 Si sale error

| Mensaje / código | Dónde sale | Causa y solución |
|---|---|---|
| "Vincula el CFDI del proveedor o captura su folio fiscal (UUID del impreso)." | Bajo el campo CFDI | No hay CFDI vinculado ni UUID. Captura el UUID |
| "Folio fiscal con formato UUID (8-4-4-4-12 hex)" | Bajo el UUID | Faltan o sobran caracteres, o hay letras fuera de A–F. Copia el valor de la demo |
| "Marca al menos una línea para recibir." | Sobre la lista de líneas | Ninguna casilla marcada |
| "Elige la ubicación (rack) destino." | Bajo "Ubicación (rack)" | Línea marcada sin rack |
| "Excede el pendiente (10) + 5% de tolerancia (10.5000)." | Bajo "Cantidad a recibir" | Cantidad mayor al pendiente + 5% |
| Toast "Cantidad excede tolerancia de la línea de OC." (`RECEPCION_EXCEDE_TOLERANCIA`) | Toast | Cantidad mayor al pendiente (con tolerancia real del artículo, hoy 0%). Baja a ≤ 10 |
| Toast "Las líneas van a sub-almacenes distintos." (`RECEPCION_MULTI_SUBALMACEN`) | Toast | Racks de sub-almacenes distintos. Usa racks del mismo sub-almacén |
| `ENTRADA_A_UBICACION_UNICA` — "Las entradas exigen una ubicación real; la ÚNICA solo se drena por salidas." | Toast / título del error | Se eligió la ÚNICA. Elige `R-A1` |
| `ENTRADA_SIN_ASIGNACION` — "El artículo no está asignado a la ubicación elegida. Asígnalo primero." | Toast | Falta la asignación (Paso 3/4) |
| `PERIODO_CONTABLE_NO_ADMITE` — "El periodo 2026-10 está cerrado o no está abierto en Contabilidad…" | Toast | Abrir el periodo en Contabilidad (`contabilidad.periodo.administrar`) o usar una fecha de un periodo abierto |
| `PERIODO_CERRADO` — "El periodo 2026/10 está cerrado. No se aceptan movimientos con fecha del mes cerrado." | Toast | El cierre mensual de Almacén ya se ejecutó para ese mes. Usar otra fecha |
| `RECEPCION_OC_NO_AUTORIZADA` / `OC_CANCELACION_SOLICITADA` | Bajo "Orden de compra" / toast | La OC no está Autorizada o tiene cancelación en curso |
| `RECEPCION_LINEA_OC_ARTICULO_INCONGRUENTE`, `RECEPCION_ARTICULO_NO_EN_OC`, `RECEPCION_COSTO_OC_INVALIDO` | Toast | Línea/artículo no corresponde a la OC, es un servicio, o el precio de la OC no es > 0 |
| `UNIDAD_*`, `CANTIDAD_DECIMALES_EXCEDE_UNIDAD` | Toast / bajo cantidad | Problema de unidad capturada, equivalencia o decimales |
| 403 `SUCURSAL_NO_ASOCIADA` / `SUCURSAL_NO_DETERMINADA` | Toast | Usuario sin bypass y sin asociación a la sucursal de la OC/rack. Con Geovany no ocurre |
| 409 por Idempotency-Key | Toast | Se repitió la petición con la misma llave; cerrar el panel y reabrirlo genera una nueva |

---

## Paso 4. Detalle de la recepción registrada

- **Ruta / quién:** Geovany. `/almacen/recepciones/01a12f9d-e2b4-7439-b8ef-051dd22e3ec6`. Se llega solo al registrar, o desde la bandeja pulsando el folio. **Capturas:** `06a-bandeja-recepciones-con-nueva.png` (bandeja), `06b-detalle-recepcion-M-ENT2026-000011.png` (detalle).
- **No hay campos que escribir.** Solo se revisa:

| Dato en pantalla | Valor esperado | Qué comprueba |
|---|---|---|
| Folio y estado | `M-ENT2026-000011` · **Registrado** | La recepción quedó firme (inmutable) |
| Fecha de movimiento / Registrado | 2026-10-12 / 12/10/2026 | La fecha capturada y la marca de tiempo del registro |
| Sub-almacén | `INSUMOS · Insumos y refacciones` | Se derivó del rack elegido |
| Orden de compra | `OC-MID2026-000007` | Vínculo a la OC |
| CFDI recibido (folio fiscal) | `6F1D2A9E-4B7C-4E58-9A31-0D5C8B7E1F42` | El UUID manual quedó guardado |
| Factura proveedor | `—` | Almacén **no** crea la factura: la captura CxP por su flujo (regla de oro de la triada) |
| Observaciones | `Recepcion parcial demo - 6 de 10 Refacciones criticas` | El texto capturado |
| Línea 1 | `ART-CRT-01`, cantidad 6.00, UM PZA ("Captura: 6 PZA"), centro de costo `20IM00 — INGENIERIA DE MANUFACTURA`, costo unitario $250.00, monto $1,500.00 | Costo = precio de la OC (6 × 250 = 1,500). El centro de costo se hereda de la línea de la OC |
| Bandeja (`06a`) | Nueva fila arriba: 2026-10-12, INSUMOS, OC-MID2026-000007, $1,500.00, Registrado | Aparece en la lista |

| Botón | Qué hace |
|---|---|
| **Ver trazabilidad** (arriba) | Abre la cadena documental de la recepción |
| Flecha "Recepciones" | Regresa a la bandeja |

- **Si sale error:** 403 si el usuario no tiene `almacen.entradas.leer` ni el bypass de lectura por sucursal.

---

## Paso 5. Reflejo en Compras (Recibido / Por surtir)

- **Ruta / quién:** Geovany. `/compras/ordenes/01a12ebb-73a4-738f-96a4-f0220183c9a1` (Compras > Órdenes de compra > `OC-MID2026-000007`) > pestaña **Líneas**.
- **Capturas:** `07a-oc-detalle-tras-recepcion.png`, `07b-oc-lineas-recibido-por-surtir.png`.
- **No hay campos que escribir.** Solo abrir y comparar.

| Qué mirar | Valor esperado | Por qué |
|---|---|---|
| Estado de la OC | **Autorizada**, versión **v5** (antes v4) | La recepción actualiza la OC y sube la versión |
| Botón de cancelar | Cambió de "Cancelar OC" a **"Solicitar cancelación"** | Ya hay recepción: la cancelación pasa a doble firma |
| Línea 1 `ART-CRT-01` | Cantidad 10.00, **Recibido 6.00, Por surtir 4.00** | Recibido llega del evento; Por surtir = Cantidad − Recibido |
| Línea 2 `ART-EST-01` | Cantidad 20.00, **Recibido 0.00, Por surtir 20.00** | No se marcó en la recepción |
| Fila "Recepción" de la cabecera | Puede quedar tapada por la barra sticky de acciones; dice "Recepción parcial (~50 %)" | Cosmético conocido; el "~50 %" no coincide con el 20 % por piezas (6 de 30) ni con el 47 % por monto. No es un error de datos; lo que vale son las columnas Recibido / Por surtir |

**Cuánto tarda.** Flujo: Outbox de Almacén → emulador de Service Bus → `AlmacenEventListenerWorker` de Compras. El publicador del Outbox consulta cada **5 segundos** (`PollIntervalSeconds = 5`); en el recorrido real, al abrir la OC (menos de un minuto después) ya estaba actualizado. Si no cambia: recargar la página tras 10–30 segundos; si pasa más de un minuto, revisar que el emulador esté encendido y los workers corriendo.

**Cómo comprobarlo en BD (solo lectura):** `almacen.integration_events_outbox` con `almacen.oc_recepcion.registrada.v1` y `published_at` no nulo; `compras.orden_compra_lineas.cantidad_recibida` = 6.0000 y 0.0000; `sub_estado_recepcion` = 1 (parcial).

- **Si sale error / no se actualiza:** causa casi siempre es Service Bus apagado (el backend descarta el evento). La recepción en Almacén ya está registrada y no se duplica; hay que encender el emulador y, si el evento ya se descartó, no se reenvía solo.

---

## Paso 6. Trazabilidad de la OC con la recepción

- **Ruta / quién:** Geovany. En el detalle de la OC, botón **Trazabilidad** (arriba a la derecha) abre `/compras/trazabilidad/oc/01a12ebb-73a4-738f-96a4-f0220183c9a1`. **Captura:** `08-trazabilidad-oc-con-recepcion.png`.
- **No hay campos.**

| Qué mirar | Valor esperado |
|---|---|
| Origen (1) | Requisición `MID2026-010008` (el estado puede salir en crudo como "EnSurtido"; cosmético) |
| Actual | Orden de compra `OC-MID2026-000007` (Autorizada) |
| Derivados (1) | Recepción `M-ENT2026-000011` (Registrado, 12/10/2026) |

- Geovany no recibe 403 porque tiene los bypass de almacén. Un usuario operativo sin asociación a la sucursal sí.

---

## Paso 7. Saldos del artículo recibido

- **Ruta / quién:** Geovany. Almacén > Operación > Saldos (`/almacen/saldos`). **Captura:** `09a-saldos.png`.
- **No hay campos que escribir** (solo filtros opcionales).

| Fila | Valor esperado | Por qué |
|---|---|---|
| `INSUMOS · ART-CRT-01 · Refacciones críticas para línea de producción` | Cantidad **6.00**, Disponible **6.00**, CPP **$250.00**, Valor inventario **$1,500.00** | La entrada suma 6; el costo promedio ponderado nace del precio de la OC ($250.00) en variante A y B (levantamiento §5.6: "precio de inventario = OC"); valor = 6 × 250 |
| `ART-EST-01` | 0.00 | Existe porque la asignación crea el saldo en 0; no se recibió nada |
| Totales de la tabla | Cantidad 33.00, valor $5,548.00 | Incluyen saldos de otros artículos de la demo; no deben coincidir con 6.00 |

- **Aviso:** la columna "Sub-almacén" solo dice `INSUMOS`; no distingue almacén MID de MTY (`DEMO-ART-PIEZA` sale 3 veces). Para identificar el rack correcto, fijarse en el artículo y la cantidad.

---

## Cómo se calcula "Por surtir" y por qué una recepción parcial 6 de 10 deja 4

| Concepto | Fórmula | Dónde |
|---|---|---|
| Pendiente en el panel "Nueva recepción" | `max(0, Cantidad de la OC − Cantidad ya recibida)` | `construirFilas` (frontend) |
| Recibido de la línea en Compras | `Recibido anterior + Cantidad del evento` (acumulado que Compras aplica a la línea de OC) | `OcRecepcionEnAlmacenCommand` (Compras): `acumulado = lineaOc.CantidadRecibida + linea.Cantidad` |
| Por surtir (pestaña Líneas de la OC) | `max(Cantidad − Recibido, 0)` (nunca negativo) | `EditorLineas.tsx` |

Ejemplo de la demo: línea 1 = 10 − 6 = **4**; línea 2 = 20 − 0 = **20**. Si ahora se recibieran otras 4 piezas de la línea 1, el Pendiente del panel sería 4, el Recibido pasaría a 10 y Por surtir a 0; cuando todas las líneas llegan a su cantidad la OC se cierra sola (el cierre lo calcula Compras junto con facturación y pago; no lo emite Almacén). Una línea ya completa deja de aparecer en el panel de recepción.

---

## Variante A vs B: cuándo usar cada una

| | **Variante A — Con factura/CFDI** | **Variante B — Con packing list** |
|---|---|---|
| Materiales | Insumos y refacciones | Materiales directos no-vidrio (interlayer, silicones, sellantes, pinturas) |
| Documento que llega con el material | Factura impresa del proveedor | Packing list con la OC referenciada |
| Campo fiscal / documento | **CFDI del proveedor obligatorio**: elegir CFDI del sistema, cargar XML o capturar UUID (8-4-4-4-12) | **Packing list obligatorio**: archivo PDF/JPEG/PNG/WebP u Office, máx. 20 MB. Sin archivo: "Sube el archivo del packing list antes de registrar." No lleva CFDI ni UUID |
| Endpoint | `POST /api/v1/almacen/recepciones` | `POST /api/v1/almacen/recepciones/packing-list` |
| Factura | La captura CxP por su flujo normal; Almacén solo referencia el CFDI | La factura llega **después** por CxP; el evento sale con `factura_pendiente=true` y CxP concilia la recepción al registrar la factura |
| Todo lo demás (OC, fecha, rack, asignación, periodo, tolerancia, costo = OC, observaciones, líneas) | Igual | Igual |
| Para la demo | **Usar esta** (más corta de explicar: un UUID) | Solo si se quiere mostrar la conciliación posterior en CxP |

## Valores alternativos

| Se puede cambiar sin romper nada | Condición |
|---|---|
| Cantidad a recibir en la línea #1 | Cualquier entero entre 1 y 10 (PZA no admite decimales). Por surtir se recalcula solo (10 − N) |
| Marcar también la línea #2 (con cantidad ≤ 20) | Su artículo ya está asignado al mismo rack (Paso 4) |
| Observaciones y comentario de línea | Texto libre (500 máx.) |
| UUID fiscal | Cualquier UUID con formato 8-4-4-4-12 hexadecimal; es solo referencia hasta que CxP enlace el XML real |
| Fecha de movimiento | Cualquier fecha de un periodo contable **abierto** y de un mes **no cerrado** en Almacén; cambia el año/folio si cambia el año |
| Rack | Cualquier rack real activo del mismo sub-almacén (`R-A1` o `R-01` en `ALM-MID-G > INSUMOS`) **con asignación del artículo a ese rack** |
| Otra OC | Cualquier OC Autorizada con pendiente (p. ej. `OC-MID2026-000005`), repitiendo asignaciones y revisando periodo y sucursal |
| Variante B en lugar de A | Si el material es de MAT-DIR y se tiene un archivo de packing list |

| **NO** cambiar | Por qué |
|---|---|
| Elegir la ubicación `ÚNICA` | `ENTRADA_A_UBICACION_UNICA` |
| Recibir sin asignar el artículo al rack | `ENTRADA_SIN_ASIGNACION` |
| Mezclar racks de sub-almacenes distintos en una recepción | `RECEPCION_MULTI_SUBALMACEN` |
| Recibir más que el pendiente (aunque la pantalla deje hasta 10.5) | `RECEPCION_EXCEDE_TOLERANCIA` con la tolerancia actual (0%) |
| Fecha en un mes cerrado o sin periodo abierto | `PERIODO_CONTABLE_NO_ADMITE` / `PERIODO_CERRADO` |
| Ejecutar "Cierre de mes" de Almacén durante la demo | Bloquea nuevas recepciones de ese mes |
| Usar una unidad capturada sin equivalencia (p. ej. `HOJA`, que no existe en el seed) | `UNIDAD_*` |
| Apagar el emulador de Service Bus antes de registrar | Compras no se actualiza |
| Cambiar de variante a medias | Reinicia el formulario completo |

## Razones no verificadas (resumen)

1. Igualdad exacta "sucursal destino de la OC = sucursal del almacén": viene de `demo-geovany-usuarios-roles-y-datos.md`; en el handler de recepción no hay chequeo explícito (solo el alcance por sucursal del endpoint para usuarios sin bypass).
2. Si el selector de CFDI en la demo realmente filtra por el RFC del proveedor: la captura `05b` muestra "CFDIs por procesar de XAXX010101000…" y `pasos.md` reporta que los CFDIs listados no eran del proveedor; no se verificó el contenido del filtro en código.
3. El código `RECEPCION_SIN_CFDI` que maneja la pantalla no existe en el backend actual; la falta de CFDI/UUID se rechaza con el mensaje del validador (400).
