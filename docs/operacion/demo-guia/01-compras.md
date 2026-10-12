# Guía campo por campo — Recorrido de Compras (requisición a OC autorizada)

> Propósito: que Eliam repita en su laptop el recorrido de Compras de la demo
> sin adivinar: qué se escribe o elige en cada campo, con qué valor exacto y
> por qué ese valor (regla de negocio, dependencia o validación).
>
> Fuentes: el recorrido real `.verification/demo-tour/compras/pasos.md` (12 de
> octubre de 2026, 1440x900, es-MX) y sus capturas numeradas; el código de
> `frontend/src/features/compras/**` y `backend/src/Compras/**`;
> `docs/modulos/compras-requisiciones/01-diseno.md` y
> `docs/modulos/compras-ordenes-compra/01-diseno.md`;
> `docs/operacion/demo-geovany-usuarios-roles-y-datos.md`.
> Donde una razón no se pudo sustentar en código o docs se escribe
> **(razón no verificada)**.
>
> Las capturas viven en `.verification/demo-tour/compras/` y se citan con el
> mismo nombre que en `pasos.md`.

## Entorno y prerrequisitos

Ambiente: web demo `http://localhost:5173`, API `http://localhost:5080`, base
`millet_demo`. Inicio de sesión real con Microsoft (SSO + MFA; cada persona
debe haber entrado una vez antes de la demo).

### Quién interviene

| Persona | Correo | Rol | Qué hace | Por qué ella/él |
|---|---|---|---|---|
| Geovany | geovanny@uzieltzaboutlook.onmicrosoft.com | `super-admin` | Crea la requisición y la OC, adjunta cotización, transmite | Con `super-admin` no puede salir el aviso "Requiere el permiso X"; además queda como `CompradorTitularId` de la OC (creador) |
| fio | fio@uzieltzaboutlook.onmicrosoft.com | `DEMO-ROL-Jefe Compras` | Firma Nivel 1 de la RQ y de la OC | Tiene `compras.requisiciones.autorizar-nivel1` y `compras.ordenes.autorizar-nivel1` |
| Mariana | mariana.ortega@uzieltzaboutlook.onmicrosoft.com | `DEMO-ROL-Dirección` | Firma Nivel 2 de la RQ y de la OC | Tiene `compras.requisiciones.autorizar-nivel2` y `compras.ordenes.autorizar-nivel2` |

El endpoint de firma valida el permiso según el nivel (`AUTORIZAR_NIVEL_DENEGADO`
si falta) y el alcance de sucursal (`SUCURSAL_NO_ASOCIADA`). fio y Mariana
tienen las tres sucursales asociadas; Geovany no tiene filas en
`identidad.usuario_sucursales` y aun así pudo crear en MID por ser
`super-admin` (bypass `compras.requisiciones.gestionar-todas-sucursales`).

### Datos que deben existir antes de empezar

| Dato | Valor en la demo | Si falta |
|---|---|---|
| Equivalencia de centro de costo sucursal x departamento | Solo ING y CAL (ambos CeCo `20IM00`) en MID, MTY y QRO (`centros_costo.departamento_centros_costo`) | `CECO_DEPARTAMENTO_SIN_EQUIVALENCIA` al agregar línea |
| Departamento ING ligado y activo en la sucursal MID | Sí | `RQ_DEPTO_NO_OPERA_EN_SUCURSAL` al crear la RQ |
| Artículos activos y **sin existencias** en MID | `ART-CRT-01` (naturaleza Crítico) y `ART-EST-01` (Estándar); sin saldo en `almacen.saldos_inventario` | Con existencia, parte o toda la cantidad se cubre de almacén y no hay saldo para OC (`RQ_SIN_SALDO_DE_COMPRA`) |
| Proveedor en estatus Activo | `DEMO-PROV-ACT` (estatus 0), con condiciones de pago y uso principal | `PROVEEDOR_INACTIVO` / `PROVEEDOR_EN_REVISION` |
| Catálogo `compras.tipos_documento_oc` con `cotizacion`, `ficha_tecnica`, `correo_autorizacion` | Completo (7 tipos) | `TIPO_DOCUMENTO_SEED_INCOMPLETO` al transmitir la OC |
| Setting `compras.settings.auto_generar_oc_al_autorizar` | `false` | Con `true` la OC se genera sola con un stub en memoria y el paso 4 (convertir) no aplica |
| Setting `apartar_existencia_al_autorizar` | `true` | Solo afecta cómo se reparte el stock al firmar N2 |
| `compras.umbrales_aprobacion_departamento` | Vacía (no se sembró nada) | Sin fila = una sola firma por monto (fail-open); las dos firmas salen por el artículo Crítico |
| Archivo PDF cualquiera (cotización) | `cotizacion-demo.pdf` (19.2 KB, generado para la demo) | Sin PDF no hay paso 5. Cualquier PDF sirve |
| Service Bus emulado (`tools/demo/demo.sh eventos`) | Encendido | No afecta este recorrido; solo el "Recibido / Por surtir" posterior (Almacén) |

Documentos que produjo el recorrido original (los folios cambian al repetirlo):
requisición `MID2026-010008`, OC `OC-MID2026-000007`.

---

## Paso 1. Inicio y menú de Compras

- Quién: Geovany. Ruta: `/` y luego el icono "Compras" de la barra izquierda
  (abre `/compras/requisiciones`).
- Capturas: `01a-inicio-geovany.png`, `01b-menu-compras-bandeja-requisiciones.png`.

| Campo | Qué poner (valor usado en la demo) | Por qué |
|---|---|---|
| Cuenta Microsoft (SSO) | `geovanny@uzieltzaboutlook.onmicrosoft.com` | Es el `super-admin`; el resto de personas entran en sus pasos |
| Contraseña / MFA | La del tenant; MFA una vez por cuenta y entorno | Login real de Entra, sin mocks (`node scripts/login-entra.mjs <rol> [--env dev]` si se usa el arnés) |
| Icono "Compras" (barra izquierda) | Clic | Abre el módulo y su menú lateral |

Botones: "Compras" abre la bandeja. No se escribe nada más.

Debe verse: Inicio con "Buenos días, Geovany" y acceso rápido "Requisición /
Orden de compra"; luego "Bandeja de requisiciones" con el botón azul "Nueva
requisición". Menú lateral del módulo: Mis requisiciones, Pendientes de
autorización, Órdenes de compra, OCs pendientes de autorización, Partidas
abiertas (reporte) y, en Configuración, Aprobadores.

Si sale error:

| Síntoma | Causa |
|---|---|
| Aviso rojo "Ya no tienes permiso para esta acción – Requiere el permiso X" | La cuenta no es `super-admin` o su rol no tiene el permiso; con `super-admin` no ocurre |
| Pantalla de login en bucle | Sesión Entra sin MFA completado |

---

## Paso 2. Nueva requisición, líneas y transmitir

- Quién: Geovany. Ruta: Compras > Mis requisiciones, `/compras/requisiciones`.
- Permiso: `compras.requisiciones.crear` + (`gestionar-todas-sucursales` o
  asociación a la sucursal).

### 2.1 Panel "Nueva requisición" (cabecera)

Se abre con el botón "Nueva requisición" (`02a-bandeja-requisiciones.png`).
Captura: `02b-nueva-requisicion-cabecera.png`. Las líneas **no** se capturan
aquí: se agregan en el detalle después de crear.

| Campo | Obligatorio | Qué poner (valor usado en la demo) | Por qué / qué pasa si falta |
|---|---|---|---|
| Sucursal | Sí (*) | `MID · Planta México Centro` | Define el folio (`MID2026-...`), el alcance de datos (ADR-0051) y de qué sucursal se cubre el stock. Vacío: "Sucursal requerida". El frontend manda también el código (`MID`, regex `^[A-Z]{2,4}$`); si no lo resuelve sale el aviso "No se pudo resolver el código de sucursal." |
| Departamento | Sí (*) | `ING · Ingeniería` | Solo lista los departamentos ligados a la sucursal elegida (cambiar la sucursal lo vacía). ING tiene equivalencia de CeCo (`20IM00`), que la línea hereda. Vacío: "Departamento requerido". Si no opera en la sucursal: `RQ_DEPTO_NO_OPERA_EN_SUCURSAL` |
| Requisitante (delegación) | No | Dejar "Yo (default)" | Solo aparece con permiso de delegación. Vacío = el usuario del JWT. Elegir a otra persona exige `compras.requisiciones.seleccionar-requisitante` (`SELECCIONAR_REQUISITANTE_DENEGADO`). Importante: si el requisitante tiene ficha de empleado, el CeCo se resuelve con el departamento **del empleado**, no el de la cabecera (`CentroCostoRqResolver`); Geovany no tiene ficha y usa el de la cabecera |
| Clasificación | Sí (*) | `Servicio` (valor por omisión, no se cambió). Opciones: Servicio, Orden de compra, Materia prima, Pinturas | Se guarda como dato de la RQ. El tooltip dice que "determina parte de la matriz de aprobación", pero el evaluador del backend (`EvaluadorMultidimensional`) solo usa naturaleza del artículo y monto: **no la lee** (verificado). Para una demo de artículos físicos conviene "Materia prima" u "Orden de compra" (`pasos.md`, problema 8) |
| Prioridad | Sí (*) | `Normal`. Opciones: Baja, Normal, Alta | Solo orden de atención para el comprador; "NO afecta la matriz de aprobación" (tooltip del formulario) |
| Fecha entrega deseada | No | Vacío | Opcional; el selector no admite fechas anteriores a hoy |
| Proveedor sugerido | No | Vacío | Opcional; si se elige debe estar Activo (`PROVEEDOR_INACTIVO`, `PROVEEDOR_EN_REVISION`, `PROVEEDOR_NO_ENCONTRADO`) |
| Obra (opcional) | No | Vacío | Máx. 120 caracteres (`RQ_OBRA_INVALIDA`). No cambia tras transmitir (`RQ_OBRA_CONGELADA`) y se hereda a la OC; consolidar RQs de obras distintas falla con `OC_OBRAS_DISTINTAS` |
| Descripción | No | `DEMO Compras - papeleria y limpieza para Ingenieria` | Texto libre, máx. 500 (`DESCRIPCION_DEMASIADO_LARGA`). Es lo que se ve en la bandeja; úsalo para reconocer tu RQ |

(*) Los campos obligatorios llevan asterisco rojo en el formulario.

Botones:

| Botón | Qué hace | Estado resultante |
|---|---|---|
| Crear requisición | Crea la RQ y abre su detalle | Borrador, folio `MID2026-0100NN`, versión v1; toast "Requisición MID2026-010008 creada" |
| Cancelar | Cierra el panel (pide confirmar si hay cambios; el borrador queda guardado en el navegador) | Sin cambios |

### 2.2 Detalle: sección "Líneas", formulario "Agregar línea"

Se abre con "Agregar línea" dentro del detalle. Captura:
`02c-nueva-requisicion-linea-formulario.png` (formulario inline con borde
punteado). Se capturan **dos** líneas, una a la vez; el formulario se limpia y
queda abierto para la siguiente.

| Campo | Obligatorio | Línea 1 | Línea 2 | Por qué / qué pasa si falta |
|---|---|---|---|---|
| Artículo | Sí (*) | `ART-CRT-01` Refacciones críticas para línea de producción | `ART-EST-01` Papelería de oficina genérica | El de naturaleza Crítico fuerza dos firmas; el Estándar muestra el contraste. Ambos sin existencias en MID. Vacío: "Artículo requerido". Inactivo: `ARTICULO_INACTIVO`; inexistente: `ARTICULO_NO_ENCONTRADO` |
| Cantidad | Sí (*) | `10` | `20` | Debe ser > 0 ("La cantidad debe ser mayor a 0"). Los decimales los limita la unidad del artículo ("Demasiados decimales para la unidad seleccionada"). Con stock cero toda la cantidad queda "a comprar": 10 y 20 |
| Unidad | Sí (solo lectura) | `PZA` (la trae el artículo) | `PZA` | Se hereda de la unidad por defecto del artículo (campo de solo lectura). No se escribe |
| Precio (monto + moneda) | Sí (*) | `250` y `MXN` | `35` y `MXN` | Precio estimado > 0, máx. 2 decimales ("El precio debe ser mayor a 0", "Máximo 2 decimales en el precio"). Moneda por omisión `MXN`; usa MXN en toda la demo (ver nota de moneda en Paso 4). El monto total es 10x250 + 20x35 = **3,200 MXN** |
| Fecha | No | Vacío | Vacío | Fecha requerida de la línea; no admite fechas pasadas |
| Centro de costo | Sí (aunque no lleva asterisco) | `20IM00 — INGENIERIA DE MANUFACTURA`, lo trae solo | igual | Se hereda de la equivalencia sucursal x departamento (ADM08). Si el usuario no puede elegir, queda en solo lectura. Sin equivalencia: `CECO_DEPARTAMENTO_SIN_EQUIVALENCIA` ("Tu departamento no tiene centro de costo asignado; pídelo a Contabilidad"). Si el botón "Agregar línea" está apagado, la consulta del CeCo está en curso o falló |
| Detalles > Proyecto | No | Vacío | Vacío | Máx. 50 en pantalla (el backend admite 200) |
| Detalles > Cuenta contable | No | Vacío | Vacío | Opcional |
| Detalles > Notas | No | Vacío | Vacío | Máx. 500 caracteres |

Botones:

| Botón | Qué hace | Estado resultante |
|---|---|---|
| Agregar línea | Guarda la línea y deja el formulario abierto | Toast "Línea 1 agregada" / "Línea 2 agregada" |
| Cancelar (o Esc) | Cierra el formulario sin guardar | Sin cambios |

Capturas del resultado: `02d-requisicion-borrador-detalle.png`,
`02e-requisicion-borrador-lineas.png`. Nota: la tabla de líneas no tiene
columna de centro de costo; solo se ve dentro del formulario de la línea.
Ambas líneas heredaron `20IM00`.

### 2.3 Transmitir

| Elemento | Qué hacer | Por qué |
|---|---|---|
| Botón "Transmitir" (barra superior del detalle) | Pulsar | Pasa la RQ a firmas. Exige al menos una línea |
| Diálogo "¿Transmitir requisición MID2026-010008?" | "Confirmar" | Texto del diálogo: "La RQ pasará a EnAutorización y los autorizadores podrán firmarla. No podrás editar líneas estructuralmente después." (`02f-transmitir-confirmacion.png`) |

Estado resultante: Borrador pasa a **En autorización**; aparecen "Aprobar Nivel
1", "Aprobar Nivel 2" (apagado hasta que firme N1), "Rechazar" y "Eliminar"
(`02g-requisicion-transmitida.png`).

Si sale error:

| Código / mensaje | Causa |
|---|---|
| `SUCURSAL_NO_ASOCIADA` (403) | El usuario no tiene el bypass ni está asociado a la sucursal |
| `RQ_DEPTO_NO_OPERA_EN_SUCURSAL` (422) | El departamento no está ligado o activo en esa sucursal |
| `SELECCIONAR_REQUISITANTE_DENEGADO` | Se eligió otro requisitante sin el permiso de delegación |
| `PROVEEDOR_INACTIVO`, `PROVEEDOR_EN_REVISION`, `PROVEEDOR_NO_ENCONTRADO` | Proveedor sugerido no Activo o inexistente |
| `CECO_DEPARTAMENTO_SIN_EQUIVALENCIA` (422) | El departamento no tiene CeCo asignado (en la demo solo ING y CAL lo tienen) |
| `CECO_SOLO_LECTURA` (422) | Se intentó elegir un CeCo distinto al heredado sin tener alcance para elegir |
| `CECO_INVALIDO` (422) | CeCo inexistente, inactivo o fuera de alcance; al transmitir: "Todas las líneas deben tener centro de costo antes de transmitir." |
| `ARTICULO_INACTIVO` / `ARTICULO_NO_ENCONTRADO` | Artículo dado de baja o inexistente |
| `LINEAS_SOLO_EN_BORRADOR` (422) | Se intentó agregar o editar líneas con la RQ ya transmitida |
| `TRANSMITIR_SIN_LINEAS` (422) | Se transmitió sin ninguna línea |
| `TRANSMITIR_SOLO_DESDE_BORRADOR` (422) | La RQ ya no está en Borrador |
| Diálogo de conflicto de versión (409) | Otra persona modificó el documento; refrescar |
| `DESCRIPCION_DEMASIADO_LARGA`, `RQ_OBRA_INVALIDA` | Descripción > 500 u Obra > 120 caracteres |

---

## Paso 3. Autorizar la requisición (fio N1, Mariana N2)

La RQ pide **dos firmas** porque la línea 1 usa `ART-CRT-01` (naturaleza
Crítico) y eso fuerza el segundo nivel, aunque la tabla de umbrales esté vacía
(ver sección de reglas al final). No hay campos que llenar: solo botones y un
diálogo por firma. La firma no lleva notas en la interfaz.

### 3.1 fio, Nivel 1

- Quién y ruta: fio en `/compras/pendientes` (Compras > Pendientes de
  autorización). Permiso: `compras.requisiciones.autorizar-nivel1`.

| Paso en pantalla | Acción | Por qué / qué debe verse |
|---|---|---|
| Fila `MID2026-010008` | "Ver" | La bandeja muestra "En autorización" y la insignia "Falta N1" (`03a-fio-pendientes-requisicion.png`) |
| Detalle y líneas | Revisar | Bloque "Disponibilidad estimada": 0 disponibles; 10 y 20 a comprar (`03b-fio-detalle-requisicion-en-autorizacion.png`, `03c-fio-detalle-requisicion-lineas.png`) |
| Botón "Aprobar Nivel 1" | Pulsar | Abre confirmación |
| Diálogo "¿Aprobar Nivel 1 de MID2026-010008?" | "Confirmar" | "Tu firma queda registrada con tu usuario y la fecha actual. No puedes deshacerla; solo se puede rechazar la RQ por un autorizador." (`03d-fio-aprobar-nivel1-confirmacion.png`) |

Resultado: "Nivel 1 autorizado 12/10/2026"; la bandeja pasa a "Falta N2"; el
histórico sube a 5 eventos (`03e-fio-requisicion-nivel1-aprobado.png`).

### 3.2 Mariana, Nivel 2

- Quién y ruta: Mariana en `/compras/pendientes`. Permiso:
  `compras.requisiciones.autorizar-nivel2`.

| Paso en pantalla | Acción | Por qué / qué debe verse |
|---|---|---|
| Fila `MID2026-010008` | "Ver" | Insignia "Falta N2" (`03f-mariana-pendientes-requisicion.png`, `03g-mariana-detalle-falta-nivel2.png`) |
| Botón "Aprobar Nivel 2" | Pulsar | Solo funciona si ya existe N1 (`AUTORIZACION_NIVEL2_SIN_NIVEL1`) |
| Diálogo de confirmación | "Confirmar" | `03h-mariana-aprobar-nivel2-confirmacion.png` |

Qué ocurre al firmar N2 (misma transacción): se cumple la matriz, se consulta
el stock de MID por línea, se aparta lo cubierto por almacén (setting
`apartar_existencia_al_autorizar = true`), y como el stock es cero todo queda
como saldo de compra. Como `auto_generar_oc_al_autorizar = false`, **no** se
crea OC: queda para el paso 4.

Resultado: la bandeja de pendientes queda vacía ("No hay requisiciones
pendientes."); el detalle muestra **Esperando compra** (versión v3, estado
interno EnSurtido) con botones "Cancelar", "Cerrar" y "Convertir a OC"
(`03i-mariana-requisicion-autorizada.png`).

Botones que se ven y no se pulsan: "Rechazar" (pide motivo del catálogo de
rechazo) y "Eliminar".

Si sale error:

| Código / mensaje | Causa |
|---|---|
| `AUTORIZAR_NIVEL_DENEGADO` (403) | El rol no tiene el permiso del nivel; fio también ve "Aprobar Nivel 2" habilitado porque su rol tiene ese permiso, pero no se probó |
| `SUCURSAL_NO_ASOCIADA` (403) | Sin bypass ni asociación a MID |
| `AUTORIZAR_SOLO_EN_AUTORIZACION` | La RQ no está en "En autorización" |
| `AUTORIZACION_NIVEL_DUPLICADO` | Ese nivel ya está firmado |
| `AUTORIZACION_NIVEL2_SIN_NIVEL1` | Se intentó N2 sin N1 previo |
| `BIFURCACION_FALLO` (422) | Falló un puerto al repartir stock (Almacén, apartados); la transacción se revierte y la RQ sigue en En autorización |

Nota: en la RQ **no** hay regla de autoautorización; el creador podría firmar
(el backend solo bloquea al creador de la OC). En la demo firman fio y
Mariana para mostrar roles distintos.

---

## Paso 4. Convertir la requisición en OC

- Quién: Geovany. Permiso: `compras.ordenes.crear` (más el bypass o la
  asociación a la sucursal de la RQ).
- Ruta: Compras > Mis requisiciones, detalle de la RQ
  (`/compras/requisiciones/<id>`). El botón "Convertir a OC" vive en el
  **detalle de la RQ**, no en la bandeja de órdenes.

Pulsar "Convertir a OC" (`04a-requisicion-esperando-compra-convertir.png`) abre
el panel "Convertir RQ MID2026-010008 a OC" (modo 1:1; consolida 1 RQ, 2
líneas). Captura: `04b-convertir-a-oc-formulario.png`.

| Campo | Obligatorio | Qué poner (valor usado en la demo) | Por qué / qué pasa si falta |
|---|---|---|---|
| Esta OC va sin requisición previa | No | Sin marcar | Marcarlo exige permiso `compras.ordenes.crear-sin-rq`, motivo (máx. 1000 en pantalla) y luego el adjunto `correo_autorizacion` al transmitir. Aquí hay RQ, así que va sin marcar |
| Consolidando 1 RQ | Sí (lo trae el panel) | `MID2026-010008 · 2L` | Sin RQ y sin marcar "sin requisición" sale el aviso "Selecciona una requisición o marca «Esta orden va sin requisición previa»..." |
| Proveedor | Sí | `DEMO-PROV-ACT · DEMO-PROV-ACT Suministros ficticios` | Debe estar Activo y se revalida al crear, al transmitir y en cada firma. Vacío: "Selecciona un proveedor." En revisión o inactivo: `PROVEEDOR_EN_REVISION` / `PROVEEDOR_INACTIVO` |
| Sucursal destino | Sí | `MID · Planta México Centro` (viene de la RQ) | La OC y la RQ deben ser de la misma sucursal (`RQ_SUCURSAL_INCOMPATIBLE`); define también el prefijo del folio `OC-MID2026-` |
| Condiciones de pago | Sí | `30D · 30 días` | Dato de cabecera obligatorio de la OC (FK a `compartido.condiciones_pago`); el valor concreto lo elige el comprador (razón de elegir 30D no verificada). Vacío: "Selecciona condiciones de pago." |
| Uso principal | Sí | `MTTO_GEN · Mantenimiento general` | "Clasificación contable" de la OC (diseño OC §4.1). Por qué MTTO_GEN: (razón no verificada). Vacío: "Selecciona un uso principal." |
| Moneda | Sí | `Peso Mexicano (MXN)` | Por omisión MXN. Si fuera otra moneda se exige tipo de cambio > 0 ("Tipo de cambio requerido para monedas distintas a MXN"; `OC_TIPO_CAMBIO_REQUERIDO`) |
| Fecha del documento | Sí | `12 oct 2026` (hoy, por omisión) | Fecha de la OC. Es distinta de la fecha de contabilización, que se fija al firmar N2 |
| Bandera: Es importación | No | Sin marcar | Marcarlo exige adjuntar `ficha_tecnica` antes de transmitir (`OC_FICHA_TECNICA_REQUERIDA`) |
| Fecha entrega esperada (opcional) | No | Vacío | Opcional |
| Observaciones (opcional) | No | `OC de demostracion - flujo de Compras` | Texto libre (máx. 2000 en pantalla, 1000 en backend: no pases de 1000) |

Botones:

| Botón | Qué hace | Estado resultante |
|---|---|---|
| Crear OC | Crea la OC en Borrador y luego le agrega las líneas de la RQ (dos llamadas) | Toast "OC creada: OC-MID2026-000007. Consolidada de 1 RQ(s) — 2 líneas agregadas."; se abre `/compras/ordenes/<id>`; la RQ queda comprometida en la OC |
| Cancelar | Cierra el panel | Sin cambios |

Debe verse (`04c-oc-borrador-detalle-cabecera.png`,
`04d-oc-borrador-lineas.png`): OC en **Borrador**, Proveedor DEMO-PROV-ACT,
Comprador titular Geovany, condiciones 30 días, uso Mantenimiento general; en
la pestaña Líneas las dos líneas con la insignia "Desde RQ MID2026-010008",
CC-Máquina `20IM00 — INGENIERIA DE MANUFACTURA`, Recibido 0.00 y Por surtir
10.00 y 20.00.

Qué hereda la OC de la RQ (verificado en el handler): la cantidad de cada
línea es la **cantidad de compra** (saldo no cubierto por almacén), el precio
unitario es el **precio estimado de la RQ** (solo el monto, sin convertir
moneda) y el centro de costo se copia 1 a 1. Por eso la RQ y la OC deben ir
ambas en MXN.

Si sale error:

| Código / mensaje | Causa |
|---|---|
| `RQ_NO_EN_SURTIDO` (422) | La RQ no está en "Esperando compra" (EnSurtido); por ejemplo, falta una firma |
| `RQ_YA_COMPROMETIDA` (422) | La RQ ya está en otra OC |
| `RQ_SUCURSAL_INCOMPATIBLE` (422) | Sucursal destino distinta a la de la RQ |
| `RQ_SIN_LINEAS` / `RQ_SIN_SALDO_DE_COMPRA` (422) | La RQ no tiene líneas con saldo de compra: el stock cubrió todo. Por eso los artículos deben estar sin existencias |
| `CECO_INVALIDO` (422) | Alguna línea tiene CeCo inexistente o inactivo |
| `PROVEEDOR_NO_ENCONTRADO`, `PROVEEDOR_INACTIVO`, `PROVEEDOR_EN_REVISION` | Proveedor no utilizable |
| `OC_TIPO_CAMBIO_REQUERIDO`, `MONEDA_FORMATO_INVALIDO`, `TIPO_CAMBIO_INVALIDO` | Moneda distinta de MXN sin T/C, o código de moneda inválido |
| `CREAR_SIN_RQ_DENEGADO` | Se marcó "sin requisición previa" sin `crear-sin-rq` |
| Toast "OC creada parcial: ..." | La OC se creó pero falló al agregar las líneas de alguna RQ: la OC queda en Borrador **sin esas líneas** y hay que agregarlas desde el detalle de la OC |
| `SUCURSAL_NO_ASOCIADA` (403) | Sin bypass ni asociación |

---

## Paso 5. Adjuntar la cotización

- Quién: Geovany. Permiso: `compras.ordenes.adjuntar` (se pide aun solo para
  ver la pestaña: sin él, 403 al abrirla).
- Ruta: `/compras/ordenes/<id>`, pestaña "Adjuntos". Solo se puede mientras la
  OC no esté en estado terminal.
- Capturas: `05a-oc-adjuntar-tipo-documento.png`,
  `05b-oc-adjuntar-cotizacion-lista.png`, `05c-oc-cotizacion-adjunta.png`.

| Campo | Obligatorio | Qué poner (valor usado en la demo) | Por qué / qué pasa si falta |
|---|---|---|---|
| Seleccionar archivo | Sí | `cotizacion-demo.pdf` (19.2 KB) | Máx. 20 MB. Tipos: PDF, imágenes (jpg, jpeg, png, webp, gif) y Office (xlsx, xls, docx, doc) y txt. Fuera de eso: `ADJUNTO_FORMATO_NO_PERMITIDO`; demasiado grande: `ADJUNTO_TAMANO_EXCEDIDO`; vacío: `ARCHIVO_REQUERIDO` |
| Tipo de documento (requerido) | Sí | `Cotización del proveedor (cotizacion)` | La clave `cotizacion` es la que busca el sistema para dejar transmitir (Paso 6). Otro tipo (por ejemplo `otro`) sube bien pero **no** sirve de cotización. Sin tipo, "Subir" no se habilita |

Lista de tipos que aparece (7): cotización, ficha técnica, correo de
autorización, pedimento, factura de proveedor extranjero, packing list y otro
(`05a-...png`; catálogo `compras.tipos_documento_oc`, diseño OC C6).

Botones:

| Botón | Qué hace | Estado resultante |
|---|---|---|
| Subir | Sube el archivo y guarda su ficha | Fila "cotizacion-demo.pdf", etiqueta "Cotización del proveedor", 19.2 KB, fecha y "Geovany", con icono de basura para darlo de baja |
| X de la fila | Descarta el archivo antes de subir | Sin cambios |

Aviso conocido: la miniatura muestra "Couldn't load plugin" en el Chromium
headless (no hay visor de PDF); es del navegador, no confirmado como defecto
del producto. Ábrelo en un navegador normal antes de presentar.

Si sale error:

| Código / mensaje | Causa |
|---|---|
| 403 al abrir la pestaña | Falta `compras.ordenes.adjuntar` |
| `ADJUNTO_FORMATO_NO_PERMITIDO` / `ADJUNTO_TAMANO_EXCEDIDO` / `ARCHIVO_REQUERIDO` | Archivo con extensión no permitida, > 20 MB o vacío |
| `TIPO_DOCUMENTO_REQUERIDO` / `TIPO_DOCUMENTO_NO_ENCONTRADO` / `TIPO_DOCUMENTO_INACTIVO` | Tipo ausente, inexistente o inactivo en el catálogo |
| `OC_ADJUNTO_NO_EN_TERMINAL` | La OC ya está en estado terminal (cancelada, cerrada, etc.) |
| `SUCURSAL_NO_ASOCIADA` (403) | Sin bypass ni asociación |

---

## Paso 6. Enviar a autorización

- Quién: Geovany. Ruta: detalle de la OC.
- Capturas: `06a-oc-transmitir-confirmacion.png`, `06b-oc-en-autorizacion.png`.

No hay campos. Se pulsa el botón "Transmitir a autorización" y luego
"Transmitir" en el diálogo "¿Transmitir OC OC-MID2026-000007 a autorización?".

Qué valida el sistema al transmitir (verificado en
`EnviarAAutorizacionOcHandler`):

| Condición | Por qué |
|---|---|
| Existe al menos un adjunto de tipo `cotizacion` | Sustenta el precio de la OC (diseño OC C11). El código aplica la versión estricta: el `correo_autorizacion` **no** sustituye la cotización (comentario "D4 (P7)" en el handler) |
| Proveedor Activo | Se revalida en cada transición (C10) |
| Al menos una línea y cabecera completa (proveedor, condiciones, uso) | `OC_TRANSMITIR_SIN_LINEAS`, `OC_TRANSMITIR_BORRADOR_MINIMO` |
| Si es importación: adjunto `ficha_tecnica` | `OC_FICHA_TECNICA_REQUERIDA` |
| Si es sin RQ: motivo y adjunto `correo_autorizacion` | `OC_MOTIVO_SIN_RQ_REQUERIDO`, `OC_SIN_RQ_CORREO_REQUERIDO` |

Estado resultante: **En autorización Jefe de Compras**; barra de pasos
Borrador (verde) > Jefe Compras (N1, activo) > Dirección (N2) > Autorizada;
botones "Aprobar Nivel 1", "Rechazar", "Cancelar OC"; versión v2.

Nota: a Geovany (creador de la OC) el botón "Aprobar Nivel 1" le sale
**deshabilitado** con el motivo "Quien crea la OC no puede firmarla." (arreglo de
la rama `fix/demo-12oct-correcciones`; en `main` salía habilitado y el
backend lo rechazaba con `OC_AUTOAUTORIZACION`). Y tras firmar N1, esa misma
persona ve "Aprobar Nivel 2" deshabilitado ("Ya firmaste el nivel 1…").

Si sale error:

| Código / mensaje | Causa |
|---|---|
| `OC_COTIZACION_REQUERIDA` | "Adjunta la cotización antes de enviar la orden de compra a autorización." No hay adjunto con tipo `cotizacion` |
| `TIPO_DOCUMENTO_SEED_INCOMPLETO` | Faltan `cotizacion`, `ficha_tecnica` o `correo_autorizacion` en `compras.tipos_documento_oc` |
| `PROVEEDOR_INACTIVO` / `PROVEEDOR_EN_REVISION` | El proveedor dejó de estar Activo |
| `OC_TRANSMITIR_SIN_LINEAS` / `OC_TRANSMITIR_BORRADOR_MINIMO` | OC sin líneas o con cabecera incompleta |
| `OC_FICHA_TECNICA_REQUERIDA` | Marcada como importación sin ficha técnica |
| `OC_TRANSMITIR_SOLO_DESDE_BORRADOR_O_RECHAZADA` | La OC ya fue transmitida |

---

## Paso 7. fio firma Nivel 1 de la OC

- Quién: fio. Permiso: `compras.ordenes.autorizar-nivel1`.
- Ruta: `/compras/ordenes/pendientes-autorizacion` (OCs pendientes de
  autorización). Sin campos: botones y un diálogo.
- Capturas: `07a-fio-ocs-pendientes-autorizacion.png`,
  `07b-fio-oc-detalle-aprobar-nivel1.png`,
  `07c-fio-aprobar-nivel1-confirmacion.png`,
  `07d-fio-oc-nivel1-aprobado.png`.

| Paso en pantalla | Acción | Qué debe verse |
|---|---|---|
| Bandeja "Órdenes esperando firma Nivel 1 (Jefe Compras)" | "Ver detalle" en `OC-MID2026-000007` | La OC con "En autorización Jefe de Compras" |
| Botón "Aprobar Nivel 1" | Pulsar | Abre confirmación |
| Diálogo "¿Aprobar OC OC-MID2026-000007 (Nivel 1)?" | "Aprobar Nivel 1" | Cierra y firma |

Resultado: estado **En autorización Dirección** (v3), N1 en verde y N2
activo; fio ya no ve botón de aprobar.

Por qué fio puede: no es el creador (Geovany) y tiene el permiso del nivel 1.

Si sale error:

| Código | Causa |
|---|---|
| `OC_AUTOAUTORIZACION` (422) | El usuario es el creador de la OC (`CompradorTitularId`) |
| `AUTORIZAR_NIVEL_DENEGADO` (403) | Falta `compras.ordenes.autorizar-nivel1` |
| `OC_AUTORIZAR_ESTADO_INCORRECTO` | La OC no está en "En autorización Jefe de Compras" |
| `OC_AUTORIZACION_NIVEL_DUPLICADO` | N1 ya firmado en este ciclo |
| `PROVEEDOR_*` | El proveedor dejó de estar Activo (se revalida en cada firma, según `demo-geovany...md` §4) |

---

## Paso 8. Mariana firma Nivel 2 de la OC

- Quién: Mariana. Permiso: `compras.ordenes.autorizar-nivel2`.
- Ruta: `/compras/ordenes/pendientes-autorizacion`. Sin campos.
- Capturas: `08a-mariana-ocs-pendientes-autorizacion.png`,
  `08b-mariana-oc-detalle-aprobar-nivel2.png`,
  `08c-mariana-aprobar-nivel2-confirmacion.png`,
  `08d-mariana-oc-autorizada.png`.

| Paso en pantalla | Acción | Qué debe verse |
|---|---|---|
| Bandeja "Órdenes esperando firma Nivel 2 (Dirección)" | "Ver detalle" | La OC en "En autorización Dirección" |
| Botón "Aprobar Nivel 2" | Pulsar | Abre confirmación |
| Diálogo "¿Aprobar OC OC-MID2026-000007 (Nivel 2)?" | "Aprobar Nivel 2" | El diálogo avisa: se genera el PDF, se fija la fecha de contabilización y se notifica al proveedor; no se revierte sin cancelar (1 firma o doble firma) |

Resultado: estado **Autorizada** (v4), barra con los cuatro pasos en verde,
"Fecha contabilización 12/10/2026" y botón "Cancelar OC" disponible. Se emite
el evento `OrdenCompraAutorizadaEvent` (habilita recepción y conciliación en
Almacén y CxP).

Si sale error:

| Código | Causa |
|---|---|
| `OC_AUTOAUTORIZACION` (422) | La persona es la creadora de la OC |
| `OC_FIRMA_MISMA_PERSONA` (422) | "La segunda firma debe ser de una persona distinta a la que firmó el primer nivel" (si fio también intentara N2) |
| `AUTORIZAR_NIVEL_DENEGADO` (403) | Falta `compras.ordenes.autorizar-nivel2` |
| `OC_AUTORIZAR_ESTADO_INCORRECTO` | La OC no está en "En autorización Dirección" (falta N1) |
| `OC_AUTORIZACION_NIVEL2_SIN_NIVEL1` | N2 sin N1 autorizado en el ciclo vigente |
| `OC_AUTORIZACION_NIVEL_DUPLICADO` | N2 ya firmado |

---

## Paso 9. Detalle de la OC autorizada

- Quién: Geovany. Ruta: `/compras/ordenes/<id>` y sus pestañas;
  "Trazabilidad" abre `/compras/trazabilidad/oc/<id>`. Solo lectura: no se
  captura nada.

| Pestaña / captura | Qué debe verse | Para qué sirve en la demo |
|---|---|---|
| Información (`09a-oc-autorizada-cabecera.png`) | Estado Autorizada; Recepción, Facturación y Pago en "Sin ... (0 %)"; fecha contabilización 12/10/2026 | Muestra los tres sub-estados que Almacén, CxP y Tesorería irán moviendo |
| Líneas (`09b-oc-autorizada-lineas-recibido-por-surtir.png`) | Columnas Recibido (0.00 y 0.00) y Por surtir (10.00 y 20.00) | Cambian cuando Almacén registra una recepción (no incluida aquí) |
| Adjuntos (`09c-oc-autorizada-adjuntos.png`) | La cotización sigue adjunta | Evidencia documental |
| Autorización (`09e-oc-autorizada-autorizacion.png`) | "Firmas de autorización · ciclo 1": Nivel 1 fio, Nivel 2 Mariana Ortega Valdés, ambas Autorizado con fecha y hora | Muestra el reparto de firmas |
| Historial (`09f-oc-autorizada-historial.png`) | Cronología: creación de OC y líneas por Geovany, adjunto, cambios de estado Borrador > EnAutorizacionJefeCompras > EnAutorizacionDireccion > Autorizada | Auditoría |
| Trazabilidad (`09g-oc-autorizada-trazabilidad.png`) | Requisición MID2026-010008 (EnSurtido) -> Orden de compra OC-MID2026-000007 (Autorizada); sin recepciones, facturas ni pagos | Cadena documental |
| PDF | "Couldn't load plugin." en el Chromium headless; existe el botón "Descargar PDF" | Abrir una vez en un navegador normal antes de presentar |

Si sale error: la trazabilidad **después** de recibir, facturar o pagar da 403
si faltan `almacen.entradas.leer-todas-sucursales`,
`cuentas_por_pagar.facturas.leer-todas-sucursales` y
`tesoreria.documentos.leer-todas-sucursales`. Todas las pantallas de OC cargan
`identidad.usuarios.leer`; sin él, aviso rojo (`demo-geovany...md` §3). En este
recorrido, con las tres cuentas, no salió ningún aviso.

---

## Paso 10 (opcional). Línea manual y cancelación

No se ejecutó en el recorrido: cancelar rompería la OC que queda como
evidencia. Lo que se sabe sin haberlo probado en pantalla:

| Elemento | Dato (fuente) |
|---|---|
| Línea manual de OC | Pide `compras.ordenes.crear-sin-rq` (el picker de centro de costo lo exige); en `demo-geovany...md` §3 |
| Cancelar OC | Exige motivo (`OC_CANCELAR_MOTIVO_REQUERIDO`); la cancelación con doble firma solo aplica si ya hay recepción (`OC_CANCELAR_CON_RECEPCION`, `demo-geovany...md` §4) |

Verificar ambos en un ensayo aparte, con otra OC, antes de enseñarlos.

---

## Reglas que explican el reparto de firmas y los umbrales

| Pregunta | Regla | Fuente |
|---|---|---|
| Por qué `ART-CRT-01` pide dos firmas en la RQ | El motor evalúa tres capas en orden: (3) naturaleza Crítico o Riesgo en **cualquier** línea fuerza N1+N2 sin importar el monto; (2) monto contra el umbral del departamento; (1) si ninguna dispara, solo N1. Con varias líneas manda la naturaleza más restrictiva (Riesgo > Crítico > Servicio > Estándar), así que una línea crítica basta aunque `ART-EST-01` sea Estándar | `EvaluadorMultidimensional`, `NaturalezaResolverService`, diseño RQ §3.bis.1 y §3.bis.2 |
| Qué haría un umbral | Es por **departamento** (no por sucursal), por RQ individual (no acumulado). Si `monto_total` (suma de cantidad x precio de todas las líneas) es **mayor que** (estricto) `umbral_monto` de la fila vigente, pide N1+N2; sin fila vigente no aplica (fail-open, solo N1). El evaluador compara números sin convertir moneda: la columna `moneda` no interviene. En la demo el total es 3,200 MXN, así que cualquier umbral menor a 3,200 dispararía dos firmas aun con solo `ART-EST-01` | Mismo evaluador, `UmbralAprobacionDepartamento` |
| Cómo se sembraría un umbral (no se hizo en la demo) | Tabla `compras.umbrales_aprobacion_departamento`, columnas `empresa_id`, `departamento_id`, `vigente_desde`, `vigente_hasta` (NULL = vigente), `umbral_monto` (>= 0), `moneda` (3 letras mayúsculas, por omisión MXN). No tiene pantalla. Usa el id del departamento **de la RQ** (ING) | Entidad y configuración EF; `demo-geovany...md` §4 |
| Por qué MID + ING y el CeCo `20IM00` | La línea hereda el centro de costo de la equivalencia sucursal x departamento (ADM08). En la base de la demo solo ING y CAL tienen equivalencia (ambos `20IM00`) en MID, MTY y QRO. COMPRAS, ADMFIN, ALMACEN, MTTO y DIR no la tienen y fallarían con `CECO_DEPARTAMENTO_SIN_EQUIVALENCIA`. Por qué el seed solo trae esos dos: (razón no verificada) | `CentroCostoCapturaRegla`, `CentroCostoRqResolver`, `pasos.md` |
| Por qué el artículo debe estar sin existencias | Al firmar N2 el sistema cubre cada línea con el stock disponible de la sucursal y aparta esa parte para almacén; **solo el resto** (`CantidadDeCompra`) puede ir a OC. Si todo está cubierto la RQ se cierra sola y no es convertible (`RQ_SIN_SALDO_DE_COMPRA`); si está cubierto en parte, la OC sale con menos cantidad. Con stock cero, 10 y 20 pasan completos a la OC | `AutorizarRequisicionHandler`, `Requisicion.RegistrarCubrimiento`, `SaldoCompraRq` |
| Por qué la cotización es obligatoria para transmitir la OC | Sustenta el precio de la OC. El código exige al menos un adjunto de tipo `cotizacion`; ni siquiera el `correo_autorizacion` la sustituye (el diseño original C11 preveía una excepción con bandera, que el handler actual no aplica) | `EnviarAAutorizacionOcHandler`, diseño OC C11 |
| Por qué el creador no firma la OC | El código rechaza que quien capturó la OC (`CompradorTitularId`) la firme en cualquier nivel (`OC_AUTOAUTORIZACION`) y exige que N2 sea una persona distinta de la de N1 (`OC_FIRMA_MISMA_PERSONA`). La razón de negocio específica no está en los docs de OC; el ADR-0007 menciona la segregación de funciones en general **(razón específica no verificada)**. Por eso: Geovany crea, fio N1, Mariana N2 |
| Y en la RQ | No existe regla equivalente: solo se exige permiso del nivel, que N2 vaya después de N1 y que no se repita un nivel. Las dos firmas de la RQ también se reparten fio/Mariana, pero por elección de la demo | `Requisicion.RegistrarAutorizacion` |
| Por qué la OC siempre lleva dos firmas | El flujo de estados de la OC pasa siempre por Jefe de Compras (N1) y Dirección (N2); no hay matriz por monto en la OC | `OrdenCompra.Autorizar` |

---

## Valores alternativos

### Qué puede cambiar Eliam sin romper nada

| Campo | Alternativa segura | Efecto |
|---|---|---|
| Descripción, Obra, Observaciones de la OC | Cualquier texto dentro de los límites (500, 120, 1000) | Solo cambia lo que se ve en bandeja y detalle |
| Prioridad | Baja, Normal o Alta | Solo orden de atención |
| Clasificación | Materia prima u Orden de compra (más coherente con artículos físicos) | Solo etiqueta; no cambia las firmas |
| Fecha entrega deseada / Fecha de la línea / Fecha entrega esperada de la OC | Hoy o futura | Informativa |
| Cantidades y precios | Otros valores > 0 con 2 decimales en precio y en MXN | Con la tabla de umbrales vacía no cambia el número de firmas; cambia solo el total de la OC |
| Artículos | Otros activos y **sin existencias** en MID | Si los dos son Estándar, la RQ pide una sola firma (N1) y no aparece N2 en la RQ; la OC sigue pidiendo dos |
| Proveedor | Cualquier proveedor Activo | `DEMO-PROV-ACT` es solo el de la demo |
| Condiciones de pago y Uso principal | Cualquier valor del catálogo | Son datos de cabecera; no alteran firmas |
| Nombre y contenido del PDF | Cualquier PDF (< 20 MB) | Solo debe adjuntarse con tipo `cotizacion` |
| Sucursal y departamento | MTY o QRO con ING o CAL (también tienen CeCo `20IM00`) | Funciona si el usuario está asociado o tiene bypass y el departamento está ligado a esa sucursal; los artículos deben estar sin existencias en **esa** sucursal (razón: el stock se consulta por sucursal) |
| Personas | Otros usuarios con los mismos permisos de nivel | La OC exige creador, N1 y N2 distintos entre sí |

### Qué NO cambiar

| No hacer | Qué se rompe |
|---|---|
| Elegir un departamento sin equivalencia de CeCo (COMPRAS, ADMFIN, ALMACEN, MTTO, DIR) | `CECO_DEPARTAMENTO_SIN_EQUIVALENCIA` al agregar la línea |
| Elegir un departamento no ligado a la sucursal | `RQ_DEPTO_NO_OPERA_EN_SUCURSAL` |
| Usar artículos con existencias en la sucursal | La RQ se cubre de almacén y queda sin saldo (`RQ_SIN_SALDO_DE_COMPRA`) |
| Mezclar monedas entre RQ y OC (por ejemplo la línea en USD y la OC en MXN) | La OC copia el monto de la línea sin convertirlo; saldría un precio equivocado. Si la OC no es MXN falta tipo de cambio (`OC_TIPO_CAMBIO_REQUERIDO`) |
| Transmitir la OC sin adjunto `cotizacion` o adjuntar la cotización con otro tipo | `OC_COTIZACION_REQUERIDA` |
| Que Geovany firme la OC, o que fio firme N1 y N2 | `OC_AUTOAUTORIZACION` / `OC_FIRMA_MISMA_PERSONA` |
| Usar un proveedor en revisión o inactivo (por ejemplo `DEMO-PROV-REV`) | `PROVEEDOR_EN_REVISION` / `PROVEEDOR_INACTIVO` |
| Marcar "Es importación" sin ficha técnica | `OC_FICHA_TECNICA_REQUERIDA` |
| Marcar "sin requisición previa" sin `crear-sin-rq`, motivo y correo de autorización | `CREAR_SIN_RQ_DENEGADO`, `OC_MOTIVO_SIN_RQ_REQUERIDO`, `OC_SIN_RQ_CORREO_REQUERIDO` |
| Subir archivos > 20 MB o con extensión no permitida (por ejemplo `.exe`) | `ADJUNTO_TAMANO_EXCEDIDO` / `ADJUNTO_FORMATO_NO_PERMITIDO` |
| Poner `auto_generar_oc_al_autorizar = true` | La OC se genera con un stub en memoria y el paso 4 deja de ser el camino |
| Quitar `compartido.catalogos.leer` o `identidad.usuarios.leer` a fio o Mariana | Avisos rojos en sus pantallas (los selectores y las pantallas de OC los piden) |
| Cambiar el rol de una persona con la sesión abierta | Los permisos se refrescan en unos 5 minutos; pide cerrar sesión |
| Insertar un umbral sin avisar | Cambia el número de firmas de las RQ de ese departamento; si lo haces, cuenta el monto total contra `umbral_monto` sin convertir moneda |

Preparación recomendada antes de repetir: las verificaciones de las secciones 4
(datos) y 7 (lista) de `docs/operacion/demo-geovany-usuarios-roles-y-datos.md`.
