# Demo — 00. Maestros y A+W: guía campo por campo

## Propósito

Repetir, sin adivinar, el recorrido de **Maestros y A+W** de la demo: qué se escribe o elige en cada campo de cada pantalla, con el valor exacto que se usó, y por qué. Sirve para que Eliam reproduzca el recorrido en su laptop.

Base de la guía: el recorrido real del 12-oct-2026 documentado en `.verification/demo-tour/maestros/pasos.md` (copia de demo `millet_demo`, rama `feature/acceso-todas-sucursales-ui`). Las razones de negocio salen del código (`backend/`, `frontend/`) y de `docs/`. Cuando una razón no se sustenta en código o docs, se marca **(razón no verificada)**.

Diapositiva de la demo:
- **CONSTRUIDO:** artículos, clientes y proveedores desde la copia de A+W.
- **VALIDAMOS:** proveedor en revisión y sus cinco documentos.
- **SIGUE:** A+W real (ambiente y contrato; objetivo 16-oct).

## Quién hace el recorrido y permisos

- **Quién:** Geovany, usuario `super-admin` (`geovanny@uzieltzaboutlook.onmicrosoft.com`). Con super-admin no sale ningún aviso "Requiere el permiso X"; los fallos serían de datos, no de permisos.
- **Si lo hace otro rol** (p. ej. Fio, Jefe de Compras): los permisos que intervienen están en `docs/operacion/demo-geovany-usuarios-roles-y-datos.md` §3 (sección Maestros). Resumen:

| Acción | Permiso requerido | Sin él |
|---|---|---|
| Ver y gestionar proveedores | `datos_maestros.proveedores.gestionar` | Redirige en silencio al inicio |
| Alta de proveedor, artículo (alta) | `compartido.catalogos.administrar` (no `gestionar`) | No aparece el botón "Nuevo" |
| Ver expediente documental | `proveedores.adjuntos-ver` | 403 |
| Validar / rechazar | `proveedores.validar` | El botón no aparece |
| Datos bancarios | `bancarios-ver` / `bancarios-editar` | La sección no aparece |
| Artículos | `datos_maestros.articulos.gestionar` | Formularios piden `catalogos.leer` |
| Clientes, sincronizar, fiscales, origen | `datos_maestros.clientes.gestionar` + `sincronizar` + `fiscal-editar` + `origen-ver` | Sin `origen-ver` el backend omite datos sin error |
| Productos A+W | `datos_maestros.productos-aw.gestionar` | — |
| Bitácora `/admin/auditoria` | `admin.auditoria.leer` + `admin.empresas.leer` + `identidad.usuarios.leer` + `identidad.roles.leer` + `compartido.catalogos.leer` | Salen 3 avisos |

## Prerrequisitos (antes de empezar)

| Requisito | Valor | Por qué |
|---|---|---|
| Variables del API de sincronización | `IntegracionesAw__Clientes__LecturaHabilitada=true`, `IntegracionesAw__Clientes__AplicacionHabilitada=true`, `IntegracionesAw__Productos__OrigenHabilitado=true`, `IntegracionesAw__OrigenDemo__Permitido=true`, `ConnectionStrings__AwOrigenPgDb` | Sin ellas el botón de sincronizar falla con `lectura_deshabilitada` / `origen_deshabilitado`. `tools/demo/demo.sh api` ya las exporta. |
| Mapeo de moneda A+W | `PESOSMX=MXN`, `USD=USD`, `Euro=EUR` | Sin mapeo, la moneda del cliente no se normaliza y el cliente no hereda moneda MXN. En `appsettings.json` el mapeo está vacío por defecto (`MapeoMoneda: {}`); lo llena el script de demo. |
| Origen activo | `Demo` (copia de demo) | La copia de A+W es la fuente de todo el recorrido. Nunca `Real` en la demo. |
| Catálogos | Monedas, regímenes, usos CFDI, formas de pago, categorías y unidades ya existen en `millet_demo` | Los formularios los consumen como catálogo; sin ellos no hay opciones que elegir. |
| Proveedores de la demo | `DEMO-PROV-ACT` (activo, 5 documentos) y `DEMO-PROV-REV` (en revisión, 3 de 5 documentos) | Son el contraste del recorrido. **No se tocan.** `DEMO-PROV-REV` se usa en vivo para mostrar la OC bloqueada. |
| Claves creadas en el recorrido | `TOUR-ART-001` (artículo), `TOUR-PROV-OK` (proveedor activo), `TOUR-PROV-NO` (proveedor rechazado) | Son los datos del recorrido. Si ya existen, la alta falla con `ARTICULO_CLAVE_DUPLICADA` o `PROVEEDOR_CLAVE_DUPLICADA`: usar otras claves (ver "Valores alternativos"). |
| Hora de la UI | America/Merida (UTC-6) | La API registra en UTC; la bitácora muestra hora local. |

---

## Paso 1. Tarjetas de Datos maestros en Administración

- **Quién:** Geovany.
- **Ruta/menú:** menú lateral "Admin" (engrane, abajo a la izquierda) > `/admin`. Bajar hasta la sección "Datos maestros".
- **Captura:** `01-admin-tarjetas-datos-maestros.png`.

Campos: ninguno. Es navegación.

| Tarjeta | Descripción en pantalla | Lleva a |
|---|---|---|
| Proveedores | "Proveedores que usan todos los módulos" | `/admin/datos-maestros/proveedores` |
| Artículos | "Artículos y servicios que se compran" | `/admin/datos-maestros/articulos` |
| Clientes | "Clientes a los que se factura" | `/admin/datos-maestros/clientes` |
| Productos A+W | "Productos de vidrio de A+W y su clave del SAT" | `/admin/datos-maestros/productos-aw` |

Debe verse: la sección "Módulos" con "Compras" (generación de OC y umbrales) debajo.

**Si sale error:** si la sección no aparece, falta permiso de datos maestros (ver tabla de permisos). No hay error de API en este paso.

---

## Paso 2. Artículos: lista, detalle y alta de TOUR-ART-001

Ruta: `/admin/datos-maestros/articulos`.

### 2a. Lista

- **Captura:** `02a-articulos-lista.png`.
- Se ven 12 artículos (la base tiene 12). Ejemplos: `ART-CRT-01` Refacciones críticas (Crítico), `ART-EST-01` Papelería (Estándar), `ART-SVC-02` Consultoría (Servicio), `ART-RGO-02` Equipo eléctrico de alto voltaje (Riesgo, **En revisión**).
- Filtros: Código, Descripción, Unidad de medida, estado y naturaleza.

**Si sale error:** sin `catalogos.leer` los filtros piden permiso; sin `articulos.gestionar` la lista no abre.

### 2b. Detalle de un artículo existente (ART-CRT-01)

- **Ruta:** lista > fila `ART-CRT-01`.
- **Captura:** `02b-articulo-ART-CRT-01-detalle.png`.
- Encabezado: `ART-CRT-01` · Activo · "Refacciones críticas para línea de producción". Botones "Ver ubicaciones (1)" y "Desactivar".

| Campo | Qué poner / qué muestra | Por qué |
|---|---|---|
| Clave | `ART-CRT-01` (no editable) | La clave es identificador inmutable; la UI la bloquea en edición. |
| Unidad de medida | `PZA · Pieza` | Se reasigna desde el catálogo de unidades; el backend valida que exista (`ARTICULO_UNIDAD_MEDIDA_NO_REGISTRADA`). |
| Nombre | `Refacciones críticas para línea de producción` | Obligatorio, máximo 254 caracteres (validador de alta y actualización). |
| Naturaleza | `Crítico` (nota: "Alimenta la matriz de aprobación de Compras") | Define qué aprobaciones exige Compras. Es el único campo del artículo que cambia el flujo de OC. |
| Categoría | `Refacciones` | Opcional. Si la categoría no existe, `ARTICULO_CATEGORIA_NO_REGISTRADA`. |
| Precio referencia | `15000` | Opcional. Mínimo 0. Solo referencia; no sustituye el precio de la OC. |
| Moneda referencia | `MXN · Peso Mexicano` | Código ISO de 3 letras. Si no está registrada: `ARTICULO_MONEDA_NO_REGISTRADA`. |

Botones: "Descartar cambios" y "Guardar cambios" (deshabilitados sin cambios). No se guardó nada en este paso.

**Si sale error:** sin cambios no hay error. Un cambio de unidad o moneda a un valor no registrado devuelve el código indicado.

### 2c. Alta de un artículo nuevo (TOUR-ART-001)

- **Quién:** Geovany.
- **Ruta:** `/admin/datos-maestros/articulos` > botón **"Nuevo" del panel izquierdo** (no el de la barra superior, que abre creación rápida).
- **Captura:** `02c-articulo-alta-formulario-vacio.png` (vacío), `02d-articulo-alta-categoria-abierta.png` (categoría abierta), `02e-articulo-alta-formulario-lleno.png` (lleno), `02f-articulo-TOUR-ART-001-creado.png` (detalle creado, sin toast).

| Campo (etiqueta exacta) | Obligatorio | Qué poner (valor de la demo) | Por qué / validación |
|---|---|---|---|
| Clave | Sí | `TOUR-ART-001` (placeholder `A-001`) | Máximo 20 caracteres. Inmutable después de creado. Si ya existe: `ARTICULO_CLAVE_DUPLICADA`. |
| Nombre | Sí | `Tour artículo de prueba (ficticio)` (placeholder `Caja de cartón 30×30×30`) | Máximo 254 caracteres. Vacío: "Nombre requerido". |
| Unidad de medida | Sí | `PZA · Pieza` | Lista del catálogo (PAR, PZA, G, KG, L, M3, ML, CM, M, MM, HR, M2). Sin unidad: "Selecciona una unidad de medida". |
| Naturaleza | Sí | `Estándar` (valor por omisión) | Opciones: Estándar, Servicio, Crítico, Riesgo. Alimenta la matriz de aprobación de Compras. |
| Categoría | No | `Refacciones` | Opcional. Opciones: Equipo eléctrico, IMPRESIÓN Y DIGITAL, INSUMOS PRODUCCION, MAT DE LIMPIEZA, MAT DE TI Y COMUNIC, MATER. DE OFICINA, MEDICINAS Y FARMACIA, Materiales peligrosos, PRENDAS SEGURIDAD, Químicos, Refacciones, SUMI. LABORATORIO, Servicios profesionales, Servicios técnicos. |

No aparecen en el alta: Descripción larga, Precio referencia y Moneda referencia. El panel dice que son opcionales y "se ajustan después en el detalle".

Botón **"Crear artículo"**: envía `POST /api/v1/catalogos/articulos` y devuelve 201 `{"id":"01a12ffc-7420-7eb4-ad87-5264b21c90a6","clave":"TOUR-ART-001"}`. Abre el detalle del artículo recién creado.

Debe verse: `TOUR-ART-001` · Activo · "Tour artículo de prueba (ficticio)" con "Ver ubicaciones (0)".

**Si sale error:**

| Situación | Código / mensaje | Causa y acción |
|---|---|---|
| Clave repetida | `ARTICULO_CLAVE_DUPLICADA` | Usar otra clave. |
| Unidad no registrada | `ARTICULO_UNIDAD_MEDIDA_NO_REGISTRADA` | Elegir unidad del catálogo. |
| Categoría no registrada | `ARTICULO_CATEGORIA_NO_REGISTRADA` | Dejar vacía o elegir categoría vigente. |
| Campo vacío | "Clave requerida" / "Nombre requerido" | Llenar el campo. |

Nota del paso: en la captura `02f` no hay toast (se recapturó). Sin errores de red.

---

## Paso 3. Clientes: lista, cliente AW-1 y sincronización

Ruta: `/admin/datos-maestros/clientes`.

### 3a. Lista

- **Captura:** `03a-clientes-lista.png`.
- Encabezado: botones "Sincronizar" y "Nuevo".
- Panel izquierdo: "Clientes sin clasificar", filtros RFC y Razón social, dos selectores (Todos / Todos), selector "Sincronización: todos" y chip "Fiscales incompletos".
- Panel derecho: "Clasificación y ligados de clientes" con el catálogo (Cancelero, Constructora, Distribuidor, Gobierno, Particular) y "Ligados pendientes de revisión" (sin pendientes).
- Filas: código (`AW-1`, `AW-2002`…), nombre, marca **A+W**, chip "Fiscales incompletos" y "Pendiente de validación".

**Si sale error:** sin `datos_maestros.clientes.gestionar` la pantalla no abre. Ver problema P5 (texto interno en clasificaciones).

### 3b. Cliente AW-1 (CLIENTE DEMO 001) con "Datos de origen A+W"

- **Ruta:** lista > fila `AW-1`.
- **Captura:** `03b-cliente-AW-1-detalle.png`; sección de origen: `03h-cliente-AW-1-datos-origen-aw.png`.
- Este cliente llega de la copia y **no se edita** en la demo. La tabla describe qué significa cada campo.

| Campo | Valor en AW-1 | Por qué |
|---|---|---|
| Clasificación | "Elige una clasificación" (vacía) | Para **guardar** un cliente hace falta una clasificación activa: si no, `CLIENTE_CLASIFICACION_REQUERIDA` ("Elige una clasificación activa para guardar el cliente."). Por eso la demo no guarda AW-1 sin clasificar. |
| Clave | `AW-1` (no editable) | Clave inmutable del cliente. |
| Referencia externa | `1` (no editable) | Correlación con A+W. |
| Razón social | `CLIENTE DEMO 001` | Nombre que viene de A+W (campo NAME1). Si el origen no trae NAME1, la fila se rechaza en la sincronización (ver 3c). |
| RFC | vacío ("Requerido para timbrar") | Sin RFC no se puede timbrar. La sincronización solo aplica el RFC de origen (UST_ID) si tiene forma válida (12 o 13 caracteres); si no, queda como candidato sin aplicar. |
| Régimen fiscal | "Selecciona régimen fiscal" (vacío) | Dato fiscal del cliente; lo llena quien captura el fiscal. **(razón no verificada: no revisé la regla exacta de obligatoriedad del régimen)** |
| Código postal fiscal | `00001` | Viene de PLZ de A+W; se aplica en sincronización solo si tiene forma válida. |
| Uso CFDI default / Forma de pago default | vacíos | Opcionales en la demo. **(razón no verificada)** |
| Método de pago default | "— sin asignar —" | Opcional. |
| Moneda default | `MXN · Peso Mexicano` | Viene de `PESOSMX` por el mapeo de moneda (ver Prerrequisitos). |
| Cliente genérico | no marcado | Solo para público general. |
| Email | `cliente001@demo.invalid` | Dato de origen. |
| Teléfono | `5500000001` | Dato de origen. |
| Receptor extranjero (Comercio Exterior): Núm. reg. id. trib., País de residencia, Domicilio extranjero (calle), Estado / provincia, Código postal extranjero | vacíos | Opcionales; solo aplican a receptores del extranjero. |

Sección **"Datos de origen A+W"** (solo lectura, de la copia):

| Campo | Valor | Qué significa |
|---|---|---|
| Estado | "Pendiente de validación" | Estado de la lectura de origen. Botón "Reintentar lectura". |
| Condición de pago | `<indf>` | Texto crudo del origen (ver P3). |
| Días nominales | `—` | Sin condición resuelta. |
| Moneda de origen | `PESOSMX` | Código crudo del origen (ver P3). |
| Última lectura | 12/10/2026 6:02:07 a.m. | Hora local. |
| Última aplicación | 11/10/2026 1:05:59 p.m. | Hora local. |
| Contrato / mapeo | `1 / 0-borrador` | Versión del contrato y del mapeo. |

**Si sale error:** no hubo error en AW-1. Si el detalle no muestra la sección de origen, falta `origen-ver`.

### 3c. Sincronización de clientes desde la copia de demo

- **Ruta:** `/admin/datos-maestros/clientes` > botón **"Sincronizar"** del encabezado izquierdo.
- **Captura del diálogo:** `03c-sincronizar-dialogo.png`.

| Control | Qué poner | Por qué |
|---|---|---|
| Selector de origen | **"Copia de demo"** | Es el origen de la demo. Ver P4: al abrir, el origen ya viene preseleccionado en "Copia de demo"; hay que confirmarlo a la vista. Cambiar de origen pide confirmación en un diálogo. |
| Botón "Iniciar sincronización" | — | Envía `POST /api/v1/datos-maestros/clientes/sincronizacion/ejecuciones` con `{"tipo":"Barrido"}` → 202 `{"id":"01a12ffd-c968-744f-9dad-d63553dc8cf2","estado":"Pendiente"}`. Termina en segundos. |

Texto del diálogo: "Lee los clientes de A+W y los aplica al ERP. Los datos fiscales existentes no se sobrescriben."

**Por qué no sobrescribe fiscales:** la aplicación de RFC y CP fiscal de origen solo está autorizada para el adaptador de demo, y solo si el dato tiene forma válida. Los datos fiscales que ya capturó Millet no se pisan.

Resultado (captura `03e-sincronizar-ejecucion-resultado.png`): la fila nueva arriba muestra "Parcial (con errores)", Barrido, 12/10/2026 6:02:03 a.m.

Detalle (captura `03f-sincronizar-detalle-ejecucion.png`):

| Contador | Valor real | Cómo explicarlo |
|---|---|---|
| Estado | Parcial (con errores) | Hubo filas procesadas y al menos una con error. |
| Leídos | 60 | Filas que devolvió el origen. |
| Creados | 0 | Los clientes de la copia ya estaban sincronizados en corridas previas. |
| Actualizados | 0 | Ningún cambio respecto a lo ya aplicado. |
| Sin cambios | 59 | Filas idénticas a lo ya aplicado. |
| Pendientes | 59 | **(significado no verificado en la UI: coincide con "Sin cambios", no lo expliqué)** |
| Conflictos | 0 | — |
| Errores | 1 | Una fila inválida: ref. 58. |

Sección "Errores y conflictos por referencia (1)":
- **Fila inválida en A+W · ref. 58**
- Mensaje: `NAME1 vacío (referencia 58).`
- Acción sugerida: "Falta un dato obligatorio en el origen (p. ej. el nombre). Corrígelo en A+W y reintenta."
- Código: `fila_invalida`. Botón "Reintentar".
- Lectura de API: `GET .../sincronizacion/ejecuciones/01a12ffd-c968-744f-9dad-d63553dc8cf2` → 200, `errores[0] = {"referencia":"58","codigo":"fila_invalida"}`.
- Por qué: el mapeador rechaza una fila sin NAME1 (`AwClienteSnapshotMapper`), porque la razón social es obligatoria para crear el cliente.

**Reintento (ref. 58):** botón "Reintentar" de la fila. Envía `POST /api/v1/datos-maestros/clientes/sincronizacion/reintentos` con `{"referencia":"58"}` → 200. Crea una ejecución tipo "Referencia" (`01a12ffe-50ec-7ef9-8914-a693cc422b2d`) con leídos 1 y errores 1 (mismo `NAME1 vacío`). Captura: `03g-sincronizar-error-tras-reintento.png`. **Resultado: no resuelve nada mientras el origen no se corrija.** No se modificó el ERP.

**Si sale error:**

| Situación | Código | Causa y acción |
|---|---|---|
| Lectura apagada | `lectura_deshabilitada` | Variables del API (`IntegracionesAw__Clientes__LecturaHabilitada`). |
| Origen demo apagado | `origen_deshabilitado` | `IntegracionesAw__OrigenDemo__Permitido=true`. |
| Moneda sin equivalencia | — | Ver P13: no se reprodujo en la demo. Si pasa, falta el mapeo `MapeoMoneda`. |
| Nombre vacío en origen | `fila_invalida` / `NAME1 vacío` | Corregir en la copia; el reintento no sirve antes. |

---

## Paso 4. Productos A+W: lista, variantes, laminado y sincronización

Ruta: `/admin/datos-maestros/productos-aw`.

### 4a. Lista

- **Captura:** `04a-productos-aw-lista.png`.
- Botones: "Sincronizar con A+W" y "Nuevo".
- Filtros: Referencia, Descripción, Todos, Todos, "Todos los tipos" y chip "Fiscales incompletos".
- Cada fila: referencia (`2`, `100006`…), descripción, marca A+W, "UC M2 · 2 medidas" (con variantes) o "UC M2" (sin variantes), y número de piezas en laminados.

### 4b. Producto con variantes por medida: VIDRIO FLOAT CLARO 6MM (referencia 2)

- **Ruta:** lista > fila `VIDRIO FLOAT CLARO 6MM`.
- **Captura:** `04b-producto-VIDRIO-FLOAT-CLARO-6MM-detalle.png`.

| Campo | Valor | Por qué |
|---|---|---|
| Revisión fiscal del producto | "Por regla, sin revisar" (origen "Regla de familia"); botones "Confirmar ficha fiscal" y "Volver a la regla" | La ficha fiscal la pone una regla por familia de A+W. Confirmarla es una decisión de Millet, no del sistema. |
| Referencia externa | `2` (no editable) | Clave de correlación con A+W. |
| Unidad de medida | `M2 · Metro cuadrado` | Unidad de venta del producto. |
| Descripción | `VIDRIO FLOAT CLARO 6MM` | Viene de A+W. Si viene vacía, la sincronización la rechaza (`descripcion vacía`). |
| Categoría | "Selecciona categoría" | Opcional. |
| Objeto de impuesto | `02` | Regla fiscal: objeto de impuesto (SAT). |
| Clave prod/serv SAT | `30171708` | Clave del SAT para el tipo. **Ver P15** (el valor de la captura no coincide con la regla de demo). |
| Clave unidad SAT | `H87` | Pieza. |
| Tasa IVA traslado | `0.16` (16 %) | IVA de vidrio. Se muestra como fracción. |
| Tasa retención IVA / ISR | vacías | Opcionales; no aplican a este producto. **(razón no verificada)** |
| Datos de aduana | fracción arancelaria, unidad aduanera, peso unitario | Solo para exportación; vacíos en la demo. **(razón no verificada)** |

Bloque **Medidas (2)**, unidad M2:

| Medida | Ancho | Alto | Espesor | Composición |
|---|---|---|---|---|
| `1830x2440` | 1830 mm | 2440 mm | 6 mm | sin dato |
| `2440x3660` | 2440 mm | 3660 mm | 6 mm | sin dato |

Bloque "Datos de origen A+W": "Sincronizado", tipo Vidrio plano, grupo Float, mercancía `310 · VIDRIO FLOAT`, modelo `FLOAT CLARO`, última lectura y aplicación 11/10/2026 1:07:13 p.m., contrato/mapeo `1 / 1-componentes`, unidad de origen M2.

Nota: la guía original pedía la medida `1800x2600`. **No existe en la copia de demo** (ver P6). Usar `1830x2440` y `2440x3660`.

### 4c. Producto laminado: VIDRIO LAMINADO SATINADO+CLARO

- **Ruta:** lista > Descripción `SATINADO` > fila `VIDRIO LAMINADO SATINADO+CLARO`.
- **Captura:** `04c-productos-aw-filtro-laminado.png` (filtro "LAMINADO", cuatro resultados) y `04d-producto-VIDRIO-LAMINADO-SATINADO-CLARO-composicion.png`.

| Elemento | Valor | Por qué |
|---|---|---|
| Medidas (1) | fila `BASE`, sin ancho ni alto ("sin dato"), espesor 12.89 mm, **composición `6+0.89+6`** | Un laminado se define por su composición (capas), no por una medida. |
| Bloque "Composición (9)" | `340006` 6MM CLARO SATINOVO TEMPLADO 1/4" (VTE · 6 mm), `419043` SENTRY GLASS PLUS 0.89MM (Relleno · 0.89 mm), `600001` CANTO PULIDO PLANO (Proceso) | Lista de componentes de origen. Son de solo lectura. |

### 4d. Sincronización de productos desde la copia de demo

1. Botón **"Sincronizar con A+W"** del encabezado izquierdo. Diálogo "Sincronización de productos A+W" (captura `04e-sincronizar-productos-dialogo.png`). Texto: "Lee los productos de A+W y los aplica al ERP. Los datos fiscales que ya completaste no se sobrescriben."
2. Selector de origen: **"Copia de demo"** (captura `04f-sincronizar-productos-origen-copia-de-demo.png`). Mismo riesgo de preselección que en clientes (P4).
3. Botón **"Iniciar sincronización"**. `POST /api/v1/datos-maestros/productos-aw/sincronizacion` → 200. **Es síncrona:** no crea ejecución en lista; el resultado aparece en el mismo panel (captura `04g-sincronizar-productos-ejecucion.png`).

Por qué no pisa datos fiscales: el servicio nunca toca claves SAT, tasas ni datos de aduana de un producto existente (`AplicarProductoAwService`). Solo actualiza descripción, unidad, variantes y baja.

Resultado real:

| Contador | Valor real | Cómo explicarlo |
|---|---|---|
| Leídos | 47 | Productos del origen. |
| Creados | 0 | Ya estaban sincronizados de corridas previas. |
| Actualizados | 0 | Sin cambios de descripción, unidad o variantes. |
| Sin cambios | 45 | Idénticos a lo aplicado. |
| Pendientes | 1 | **(significado no verificado en la UI)** |
| Conflictos | 0 | — |
| Errores | **1** (la lista muestra **2**; ver P7) | Inconsistencia de la UI. |

Errores por referencia (2):

| Referencia | Tipo | Mensaje real | Acción sugerida (UI) | Código |
|---|---|---|---|---|
| 46 | Fila inválida en A+W | `descripcion vacía (referencia 46).` | "Falta un dato obligatorio en el origen (p. ej. la descripción). Corrígelo en A+W y reintenta." | `fila_invalida` |
| 47 | Unidad sin equivalencia | `Producto no aplicado (UNIDAD_SIN_EQUIVALENCIA).` | "El producto no se creó ni se actualizó. Da de alta la unidad y su equivalencia SAT, y reintenta." | `UNIDAD_SIN_EQUIVALENCIA` |

No se pulsó "Reintentar" en ninguno: el 46 depende de corregir la copia y el 47 de dar de alta la unidad (fuera del alcance de la demo).

Por qué no hubo creados: ya estaban sincronizados. Para mostrar altas en vivo hace falta una copia con productos nuevos.

**Cómo explicar los dos errores en la demo:**
- `descripcion vacía`: el origen trae un producto sin descripción; no se puede dar de alta sin nombre. Se corrige en A+W, no en el ERP.
- `UNIDAD_SIN_EQUIVALENCIA`: el producto usa una unidad de A+W que no tiene equivalencia SAT en Millet; el sistema no inventa la equivalencia. Se da de alta la unidad y su equivalencia, y se reintenta.

---

## Paso 5. Proveedores: lista, DEMO-PROV-REV (solo lectura), alta, expediente y validación

Ruta: `/admin/datos-maestros/proveedores`.

### 5a. Lista

- **Captura:** `05a-proveedores-lista.png`.
- Encabezado "Proveedores" con botón "Nuevo". Pestañas: **Todos** (por omisión), Por validar, Activos. Filtros: RFC, Razón social y dos selectores.
- Filas relevantes: `DEMO-PROV-ACT` (Activo) y `DEMO-PROV-REV` con insignia ámbar "En revisión".
- También aparecen proveedores de la copia (MOD-PROV-001, PD0001 a PD0010, PROV-EST, PROV-INA, PROV-INT, PROV-MAY, PROV-SVC).

### 5b. DEMO-PROV-REV en revisión (solo lectura)

- **Ruta:** lista > fila `DEMO-PROV-REV`.
- **Captura:** `05c-proveedor-DEMO-PROV-REV-detalle.png`.
- **No se editó, no se subió documento, no se validó, no se rechazó.**

| Elemento | Valor | Por qué |
|---|---|---|
| Encabezado | `DEMO-PROV-REV` · En revisión | Estado de origen de la alta. |
| Botón "Rechazar" | activo | Solo para proveedores en revisión. |
| Botón "Validar proveedor" | deshabilitado | Falta expediente (2 de 5 documentos faltantes). |
| Banner ámbar | "Proveedor en revisión por Cuentas por Pagar" / "Expediente incompleto: 2 documentos faltantes" / "Las órdenes de compra, captura de facturas y programación de pagos están bloqueadas hasta que CxP valide el expediente." | Regla de negocio: el proveedor no es usable en OC hasta validar. |
| Clave | `DEMO-PROV-REV` (no editable) | Inmutable. |
| RFC | `XAXX010101000` | RFC genérico SAT (público en general). Ver P9. |
| Razón social | `DEMO-PROV-REV Suministros ficticios` | — |
| Tipo persona | `Moral` | — |
| Condiciones de pago / Nombre comercial / Email / Teléfono | vacíos | Opcionales. |
| Datos bancarios | Banco `DEMO Banco ficticio`, CLABE enmascarada `000000000000000000`, Beneficiario `DEMO-PROV-REV Suministros ficticios` | Datos de demo. Los edita Tesorería. |
| Expediente | 3 vigentes (Constancia de situación fiscal hasta 12/01/2027, Contrato, Acta constitutiva); faltan Identificación del representante legal y Comprobante de domicilio | Ver "Los 5 documentos" más abajo. |

**Si sale error:** no hay error en este paso. Si la sección de expediente no aparece, falta `proveedores.adjuntos-ver`.

### 5c. Alta de proveedor nuevo: TOUR-PROV-OK

- **Quién:** Geovany. La guía sugiere a Fio (Jefe de Compras) para esta acción; en la corrida la hizo Geovany con super-admin.
- **Ruta:** lista > botón **"Nuevo" del panel izquierdo**.
- **Capturas:** `05d-proveedor-alta-formulario-vacio.png` (vacío), `05e-proveedor-alta-formulario-lleno.png` (lleno), `05f-proveedor-TOUR-PROV-OK-creado.png` (creado, con toast naranja: ver P1).

| Campo (etiqueta exacta) | Obligatorio | Qué poner (valor de la demo) | Por qué / validación |
|---|---|---|---|
| Clave | Sí | `TOUR-PROV-OK` (placeholder `P-001`) | Máximo 20 caracteres. Inmutable después de creado. Duplicada: "Ya existe un proveedor con esa clave." (`PROVEEDOR_CLAVE_DUPLICADA`). |
| Razón social | Sí | `Tour Proveedor OK S.A. de C.V. (ficticio)` (placeholder `Acme S.A. de C.V.`) | Máximo 254 caracteres. Vacío: "Razón social requerida". |
| RFC | Sí | `TPO101010AB1` (placeholder `ACM010101ABC`) | Solo mayúsculas, dígitos, `&` y `Ñ`. Exactamente 12 caracteres para moral o 13 para física (el backend valida `Length(12, 13)`). RFC inventado con formato válido. Si no es genérico y ya existe: `PROVEEDOR_RFC_DUPLICADO` (409). |
| Tipo persona | Sí | `Moral` (por omisión) | Define la longitud del RFC y qué documentos aplican: el acta constitutiva solo aplica a personas morales. |
| Email | No | `contacto@tour-prov-ok.com` | Opcional. Si se escribe, debe ser un email válido. |
| Teléfono | No | `+52 55 0000 0001` | Opcional. Máximo 50 caracteres. |

No aparecen en el alta (se ajustan en el detalle): Condiciones de pago (días, 0 a 365), Nombre comercial, tolerancia de factura, datos bancarios. El panel dice que se "ajustan después en el detalle".

Botón **"Crear proveedor"**: `POST /api/v1/catalogos/proveedores` → 201 `{"id":"01a12ff8-a3fa-7352-9a94-33cd5b49928c","clave":"TOUR-PROV-OK","estatus":2}` (2 = En revisión). La respuesta trae `posibleDuplicadoDeId: null`.

Regla: **todo proveedor nuevo nace En revisión** (F1-ADM-05 G1.1: Compras solicita, CxP valida). No es usable en OC hasta validar.

**Si sale error:**

| Situación | Código / mensaje | Acción |
|---|---|---|
| Clave repetida | `PROVEEDOR_CLAVE_DUPLICADA` | Otra clave. |
| RFC repetido (no genérico) | `PROVEEDOR_RFC_DUPLICADO` | Revisar el proveedor existente con ese RFC. |
| RFC de longitud inválida | "El RFC debe tener 12 o 13 caracteres" | Corregir. |
| Email inválido | "Email inválido" | Corregir o dejar vacío. |

### 5d. Carga de los 5 documentos obligatorios

- **Ruta:** detalle de `TOUR-PROV-OK` > bloque "Expediente documental".
- **PDF de prueba:** 5 archivos `TOUR-PROV-OK-<documento>.pdf`, cada uno con una línea "Demo ...", generados fuera del repositorio (carpeta temporal). Ver "Datos que tuve que preparar".
- Por cada documento: pulsar **"Adjuntar"** > panel inline > "Archivo" (PDF, XML, JPG o PNG; **10 MB**) > "Vigente hasta (opcional)" vacío > **"Subir documento"** → `POST /api/v1/datos-maestros/proveedores/01a12ff8-a3fa-7352-9a94-33cd5b49928c/adjuntos` → 201 (cinco veces).

| Orden | Documento (etiqueta) | Archivo subido | Vigencia resultante | Por qué |
|---|---|---|---|---|
| 1 | Constancia de situación fiscal | `TOUR-PROV-OK-constancia_situacion_fiscal.pdf` | 12/01/2027 (3 meses, por omisión) | Acredita RFC. Vence cada 3 meses. |
| 2 | Contrato | `TOUR-PROV-OK-contrato.pdf` | Sin vigencia | Relación comercial. |
| 3 | Acta constitutiva | `TOUR-PROV-OK-acta_constitutiva.pdf` | Sin vigencia | Solo persona moral. |
| 4 | Identificación del representante legal | `TOUR-PROV-OK-identificacion_representante.pdf` | Sin vigencia | Quién firma por la empresa. |
| 5 | Comprobante de domicilio | `TOUR-PROV-OK-comprobante_domicilio.pdf` | 12/01/2027 (3 meses, por omisión) | Domicilio fiscal; vigencia de 3 meses. |

Captura del diálogo: `05g-adjuntar-documento-dialogo.png`. Capturas por documento: `05h-01-subido-constancia_situacion_fiscal.png`, `05h-02-subido-contrato.png`, `05h-03-subido-acta_constitutiva.png`, `05h-04-subido-identificacion_representante.png` (las cuatro con toast verde), `05h-05-expediente-completo-tras-subir-5.png` (sin toast).

Antes de cargar: banner "Expediente incompleto: 5 documentos faltantes" con los 5 nombres.
Después del quinto: encabezado "Expediente completo" y banner "✓ Expediente documental completo". Los 5 quedan "Vigente" con nombre de archivo y botón "Reemplazar".

**Si sale error:**

| Situación | Código | Acción |
|---|---|---|
| Archivo vacío | `ADJUNTO_ARCHIVO_VACIO` | Elegir un PDF con contenido. |
| Extensión o tipo no permitido | **(código no verificado; revisar `SubirAdjuntoCommand`)** | Solo PDF, XML, JPG o PNG. |
| Mayor a 10 MB | rechazo de tamaño | Reducir el archivo. |
| Vigencia en el pasado | `ADJUNTO_VIGENCIA_PASADA` | Poner una fecha futura o dejar vacío. |
| Sin `proveedores.adjuntos-ver` | 403 | Permiso (ver tabla). |

### 5e. Validar y activar

1. Pulsar **"Validar proveedor"** (ya habilitado). Captura `05i-antes-de-validar.png`.
2. Diálogo "Validar y activar proveedor": "¿Confirmas que el expediente documental de Tour Proveedor OK S.A. de C.V. (ficticio) (TOUR-PROV-OK) ha sido revisado y cumple todos los requisitos de CxP? El proveedor pasará a estatus Activo y podrá operar con órdenes de compra y facturas." Botones "Cancelar" y **"Validar y activar"**. Captura `05j-validar-confirmacion.png`.
3. Pulsar **"Validar y activar"** → `POST /api/v1/datos-maestros/proveedores/01a12ff8-a3fa-7352-9a94-33cd5b49928c/validar` → 200 `{"estatus":0,"validadoEn":"2026-10-12T11:57:47Z"}` (0 = Activo).
4. Resultado: `TOUR-PROV-OK` · **Activo**, con "Desactivar" y "Poner en revisión". Desaparece el banner. Captura `05k-proveedor-TOUR-PROV-OK-activo.png`.

Reglas del backend (`ValidarProveedorCommand`):
- Solo valida proveedores **En revisión**: si no, `PROVEEDOR_NO_EN_REVISION`.
- Solo si el expediente está **completo**: si no, `PROVEEDOR_EXPEDIENTE_INCOMPLETO` con la lista de faltantes y vencidos.
- Registra quién validó (`ValidadoPorId`) y deja bitácora.

---

## Paso 6. Proveedor de rechazo: TOUR-PROV-NO (alta y rechazo con motivo)

Ruta: igual que el paso 5c.

1. Pulsar **"Nuevo"** (panel izquierdo) y llenar:

| Campo (etiqueta exacta) | Obligatorio | Qué poner | Por qué |
|---|---|---|---|
| Clave | Sí | `TOUR-PROV-NO` | Igual que en 5c. |
| Razón social | Sí | `Tour Proveedor NO S.A. de C.V. (ficticio)` | — |
| RFC | Sí | `TPN101010CD2` | 12 caracteres, moral. |
| Tipo persona | Sí | `Moral` (por omisión) | — |
| Email | No | `contacto@tour-prov-no.com` | — |
| Teléfono | No | `+52 55 0000 0002` | — |

Captura del formulario lleno: `06a-proveedor-alta-TOUR-PROV-NO-lleno.png`.

2. Pulsar **"Crear proveedor"** → 201 `{"id":"01a12ffa-19a0-7cbd-8c46-3a700763f085","clave":"TOUR-PROV-NO","estatus":2}`. Captura `06b-proveedor-TOUR-PROV-NO-creado.png` (en revisión, con toast naranja: P1).

3. Pulsar **"Rechazar"** (encabezado). Diálogo "Rechazar proveedor": "Indica el motivo del rechazo del expediente para Tour Proveedor NO S.A. de C.V. (ficticio) (TOUR-PROV-NO). El proveedor pasará a estatus Inactivo." Campo "Motivo de rechazo (obligatorio, 5 a 500 caracteres):" con contador "0 / 500". Botón "Confirmar rechazo" deshabilitado hasta escribir. Captura `06c-rechazar-dialogo.png`.

| Campo | Qué poner | Por qué |
|---|---|---|
| Motivo de rechazo | `Expediente incompleto: falta constancia de situacion fiscal y contrato (prueba de demo).` (captura `06d-rechazar-motivo-lleno.png`) | Obligatorio, de 5 a 500 caracteres (`RechazarProveedorCommand`). Queda en bitácora y en el banner del proveedor. |

4. Pulsar **"Confirmar rechazo"** → `POST /api/v1/datos-maestros/proveedores/01a12ffa-19a0-7cbd-8c46-3a700763f085/rechazar` con `{"motivo":"..."}` → 200 (estatus 1 = Inactivo).

Resultado: `TOUR-PROV-NO` · **Inactivo**; banner rojo "Motivo de rechazo de CxP" con el texto; toast verde "Proveedor TOUR-PROV-NO rechazado (estatus Inactivo)". Captura `06e-proveedor-TOUR-PROV-NO-inactivo.png`.

Regla: solo se rechaza un proveedor **En revisión** (si no, `PROVEEDOR_NO_EN_REVISION`).

**Si sale error:** motivo de menos de 5 o más de 500 caracteres: "El motivo de rechazo debe contener al menos 5 caracteres." / "no puede exceder 500 caracteres". El botón queda deshabilitado mientras esté vacío.

---

## Paso 7. Comprobación en el selector de proveedor de una Nueva OC (sin guardar)

- **Quién:** Geovany.
- **Ruta:** `/compras/ordenes` > botón "Nueva" > panel "Nueva orden de compra" (captura `07a-nueva-oc-formulario.png`).
- **No se llenó nada ni se guardó.** Ningún POST en esta pantalla.

Campos del panel (solo referencia):

| Campo | Valor de la demo | Por qué |
|---|---|---|
| Esta OC va sin requisición previa | no tocar | Opción para OC sin RQ. |
| Agregar requisiciones (consolidación) | no tocar | Consolidar RQ en una OC. |
| Proveedor | buscador "Buscar proveedor…" (ver pasos siguientes) | Solo ofrece proveedores **activos**. |
| Sucursal destino | — | — |
| Condiciones de pago | — | — |
| Uso principal | — | — |
| Moneda | Peso Mexicano (MXN), por omisión | — |
| Fecha del doc. | — | — |

Búsquedas en el buscador de proveedor:

| Texto escrito | Resultado | Captura | Por qué |
|---|---|---|---|
| `TOUR` | Solo `TOUR-PROV-OK` (Activo) | `07b-nueva-oc-buscar-proveedor-TOUR.png` | Activo, se puede usar. |
| `DEMO-PROV-REV` | "Ningún proveedor activo coincide con \"DEMO-PROV-REV\"." | `07b-nueva-oc-buscar-proveedor-DEMO-PROV-REV.png` | En revisión: bloqueado hasta validar. |
| `TOUR-PROV-NO` | "Ningún proveedor activo coincide con \"TOUR-PROV-NO\"." | `07c-nueva-oc-buscar-proveedor-TOUR-PROV-NO.png` | Inactivo: no aparece. |
| `DEMO-PROV` | `DEMO-PROV-ACT` (Activo); no `DEMO-PROV-REV` | `07c-nueva-oc-buscar-proveedor-DEMO.png` | Mismo criterio. |

Conclusión: el selector solo ofrece proveedores **activos**. El texto "Ningún proveedor activo coincide" lo dice. La regla de negocio: la OC, la captura de facturas y la programación de pagos solo se hacen con proveedores validados.

**Si sale error:** no aplica. No se guardó nada.

---

## Paso 8. Bitácora de auditoría de Maestros

- **Quién:** Geovany.
- **Ruta:** `/admin/auditoria` (tarjeta "Auditoría" en Admin > Accesos).
- **Capturas:** `08a-auditoria-inicial.png` (inicial), `08b-auditoria-filtro-modulo-abierto.png` (filtro de módulo abierto), `08c-auditoria-datosmaestros-todas-sucursales.png` (resultado).

| Filtro | Qué poner | Por qué |
|---|---|---|
| Desde | 05/10/2026 (por omisión) | Obligatorio. Rango de la demo. |
| Hasta | 12/10/2026 | Obligatorio. Día del recorrido. |
| Buscar | vacío | Texto libre. |
| Tipo de actor | Todos los tipos | — |
| Módulo | `DatosMaestros` | Filtra solo maestros (opciones: Todos, Compras, Identidad, Administracion, Catalogos, DatosMaestros, Compartido, Contabilidad). |
| Entidad | vacía | — |
| Acción | Todas | — |
| Usuario | Todos | — |
| Empresa | Todas | — |
| Sucursal | "Todas las sucursales de la empresa" (por omisión en esta copia) | Trampa de la guía: si queda en la sucursal activa, la bitácora sale **vacía** porque las filas de maestros no tienen sucursal. Aquí no se presentó. |

Acción: **"Aplicar"** → `GET /api/v1/admin/auditoria?desde=2026-10-05&hasta=2026-10-12&zonaHoraria=America%2FMerida&modulo=DatosMaestros&offset=0&limit=50` → 200.

Filas (de la más reciente a la más antigua; hora local):

| Hora | Registro | Actor |
|---|---|---|
| 12 oct 06:00:36 | "Creó Articulo TOUR-ART-001 · Tour artículo de prueba (ficticio)" | Geovany |
| 12 oct 05:58:19 | "Proveedor TOUR-PROV-NO rechazado por CxP. Motivo: Expediente incompleto: …" | Geovany |
| 12 oct 05:58:19 | "Modificó Proveedor TOUR-PROV-NO — Estatus: EnRevision → Inactivo, MotivoRechazo: null → …" | Geovany |
| 12 oct 05:58:02 | "Creó Proveedor TOUR-PROV-NO" | Geovany |
| 12 oct 05:57:47 | "Proveedor TOUR-PROV-OK validado y activado por CxP con expediente documental completo." | Geovany |
| 12 oct 05:57:47 | "Modificó Proveedor TOUR-PROV-OK — Estatus: EnRevision → Activo, ValidadoEn: null → …, ValidadoPorId: null → Geovany" | Geovany |
| 12 oct 05:56:26 | "Creó Proveedor TOUR-PROV-OK" | Geovany |
| 11 oct 01:07:13 p.m. | "Creó ProductoAw ProductoAw 01a12c5c" y "Creó CambioFiscalAw CambioFiscalAw 01a12c5c" | usuario `fio` (sincronización de productos) |

Nota: las horas de la lista son locales (America/Merida). La API y el texto del cambio muestran UTC (+6 h).

Pendiente: los 5 documentos de TOUR-PROV-OK no aparecen como filas separadas en esta página de 50 filas; no se revisó la paginación completa.

---

## Los 5 documentos del expediente y por qué

Los 5 son obligatorios para un proveedor. El acta solo aplica a persona moral (`SoloPersonaMoral`). Fuente: seed de `CompartidoDbContext` y `ExpedienteAdjuntos`.

| # | Código | Documento | Vigencia | Aplica | Por qué (negocio) |
|---|---|---|---|---|---|
| 1 | `constancia_situacion_fiscal` | Constancia de situación fiscal | 3 meses | Moral y física | Confirma el RFC ante el SAT. Vence y hay que renovarla. |
| 2 | `contrato` | Contrato | Sin vigencia | Moral y física | Acredita la relación comercial. |
| 3 | `acta_constitutiva` | Acta constitutiva | Sin vigencia | **Solo moral** | Acredita la existencia de la sociedad. No aplica a una persona física. |
| 4 | `identificacion_representante_legal` | Identificación del representante legal | Sin vigencia | Moral y física | Quién firma por la empresa. |
| 5 | `comprobante_domicilio` | Comprobante de domicilio | 3 meses | Moral y física | Domicilio fiscal. Vence y hay que renovarlo. |

Reglas (código):
- Expediente **completo** = los 5 obligatorios aplicables vigentes o por vencer. Un vencido bloquea la validación.
- Subir un documento nuevo del mismo tipo reemplaza el vigente (el más reciente cuenta).
- Validar con expediente incompleto: `PROVEEDOR_EXPEDIENTE_INCOMPLETO`.

---

## Cómo se explica en la demo (guion corto de 5 frases)

1. "Los artículos, clientes y productos llegan desde la copia de A+W; el ERP los lee y los aplica sin pisar lo que Millet ya capturó en fiscal."
2. "Cuando un registro de origen está incompleto, el sistema no lo inventa: lo marca con una causa y una acción, por ejemplo un nombre vacío o una unidad sin equivalencia SAT."
3. "Todo proveedor nuevo nace en revisión: no se puede usar en una orden de compra ni pagar hasta que CxP valide su expediente de cinco documentos."
4. "Aquí tenemos el contraste: DEMO-PROV-ACT ya está activo; DEMO-PROV-REV está bloqueado porque le faltan documentos."
5. "Lo que sigue es conectar A+W real, con ambiente y contrato, para el 16 de octubre; hoy todo esto corre sobre la copia."

---

## Valores alternativos

| Campo | Puede cambiar | No debe cambiar | Por qué |
|---|---|---|---|
| Clave de artículo o proveedor | Cualquier clave única de hasta 20 caracteres (p. ej. `TOUR-ART-002`, `TOUR-PROV-OK2`) | Reusar una clave existente | Clave duplicada rechaza el alta. La clave es inmutable: elegirla bien. |
| Nombre / razón social | Cualquier texto ficticio, de hasta 254 caracteres | Dejarlo vacío | Obligatorio. Sin nombre, el origen rechaza la fila. |
| RFC de proveedor | Otro RFC con formato válido (12 caracteres moral, 13 física, mayúsculas) | Formato inválido o RFC real de una persona | Formato inválido rechaza el alta. No usar RFC de personas reales en la demo. |
| Tipo persona | `Física` (si se cambia, el acta no aplica y el expediente pide 4 documentos) | — | Cambia la longitud del RFC y los documentos obligatorios. |
| Naturaleza del artículo | Estándar, Servicio, Crítico o Riesgo | — | Cambia la matriz de aprobación de Compras; es el valor que vale la pena mostrar. |
| Motivo de rechazo | Cualquier texto de 5 a 500 caracteres | Menos de 5 caracteres | Validación del backend. |
| Email / teléfono | Cualquier email válido o vacío / teléfono hasta 50 caracteres | Email sin formato | Validación de email. |
| Vigencia al subir documento | Fecha futura o vacío | Fecha pasada | `ADJUNTO_VIGENCIA_PASADA`. |
| Origen de sincronización | Copia de demo (en la demo) | A+W real | A+W real aún no está lista (objetivo 16-oct). |
| Proveedores `DEMO-PROV-ACT` y `DEMO-PROV-REV` | Nada | Cambiar su estado, expediente, RFC o datos bancarios | `DEMO-PROV-REV` se usa en vivo para mostrar la OC bloqueada. Si se valida o se sube un documento, el guion en vivo pierde su contraste. |
| Monedas | `PESOSMX` → `MXN`, `USD` → `USD`, `Euro` → `EUR` | Quitar el mapeo | Sin mapeo la moneda del cliente no se crea. |

Nota para la demo: la guía de operación pide cargar en vivo los 2 documentos que faltan a `DEMO-PROV-REV`. Con el contraste en vivo decidido (no tocarlo), esa parte queda fuera. Confirmar antes de la demo.

---

## Datos que se crearon en el recorrido

Todo se creó con la UI en `millet_demo`. Ningún dato preexistente se modificó.

| Qué | Identificador | Paso |
|---|---|---|
| Artículo `TOUR-ART-001` | id `01a12ffc-7420-7eb4-ad87-5264b21c90a6` | 2c |
| Proveedor `TOUR-PROV-OK` | id `01a12ff8-a3fa-7352-9a94-33cd5b49928c` (alta 201, 5 adjuntos 201, validación 200 → Activo) | 5c–5e |
| Proveedor `TOUR-PROV-NO` | id `01a12ffa-19a0-7cbd-8c46-3a700763f085` (alta 201, rechazo 200 → Inactivo) | 6 |
| Ejecución de sincronización de clientes | `01a12ffd-c968-744f-9dad-d63553dc8cf2` (Barrido, Parcial) | 3c |
| Reintento de cliente (ref. 58) | `01a12ffe-50ec-7ef9-8914-a693cc422b2d` (Referencia, Parcial) | 3c |
| Sincronización de productos A+W | respuesta síncrona, sin id | 4d |
| PDF de prueba | 5 archivos `TOUR-PROV-OK-<documento>.pdf` en una carpeta temporal fuera del repo | 5d |

Sincronizaciones previas (no creadas en el recorrido): 5 ejecuciones de clientes (2 de 11/10 Parciales, 3 del 9/10 Completas).

---

## Problemas conocidos de esta parte

Clasificación: **cosmético** = no cambia datos ni flujo; se puede explicar o corregir sin riesgo. **dato** = la copia o la configuración no coincide con lo esperado; cambia lo que se muestra o se sincroniza.

| ID | Problema | Dónde | Clasificación | Estado / qué hacer |
|---|---|---|---|---|
| P1 | Toast "posible duplicado" en toda alta de proveedor, aunque la respuesta trae `posibleDuplicadoDeId: null`. | Alta de proveedor (`SheetNuevoProveedor.tsx`) | cosmético | En corrección en esta rama: el archivo en el árbol de trabajo ya comprueba `posibleDuplicadoDeId`. Sin commit. Confirmar que la captura `05f` es anterior al cambio. |
| P2 | Aviso de RFC duplicado no reproducido. | Alta de proveedor | — | No se probó. El backend devuelve `PROVEEDOR_RFC_DUPLICADO` (409) para RFC no genéricos. |
| P3 | Valores crudos de A+W en AW-1: condición de pago `<indf>` y moneda de origen `PESOSMX`. | Detalle de cliente AW-1 | dato | Explicar en la demo ("sin traducir del origen"). Traducir antes de presentar a clientes reales. |
| P4 | El origen de sincronización aparece preseleccionado en "Copia de demo" en ambos diálogos. | Sincronizar clientes y productos | dato (riesgo) | En producción podría sincronizarse contra el origen equivocado. Confirmar el origen en cada corrida. |
| P5 | Texto interno "Por validar con Millet (V37, Eliam 10-oct-2026)" en las 5 clasificaciones de cliente. | Clasificación de clientes | cosmético | Es nota de trabajo visible en pantalla. Cambiar la descripción antes de presentar. Está en `CompartidoDbContext.cs` y en migraciones. |
| P6 | Medida `1800x2600` de la guía no existe en la copia; existen `1830x2440` (28 variantes) y `2440x3660` (28). | Producto VIDRIO FLOAT CLARO 6MM | dato | Usar `1830x2440` y `2440x3660`. |
| P7 | Contador "Errores 1" en productos, pero la lista muestra 2 errores (ref. 46 y 47). "Pendientes 1" no se explica. | Sincronización de productos | cosmético | Explicar: el error 47 aparece en la lista; el contador no lo cuenta como error. **Revisar la UI.** |
| P8 | Bitácora con enums y IDs crudos: `EnRevision → Activo`, "ProductoAw 01a12c5c", "+00:00" en fechas del texto. | `/admin/auditoria` | cosmético | Presentar la bitácora como evidencia de quién hizo qué; no leer los enums en voz alta. |
| P9 | `DEMO-PROV-ACT` y `DEMO-PROV-REV` son personas morales con RFC genérico `XAXX010101000`. | Datos de demo | dato | La validación lo acepta para moral (RFC genérico no es único). Puede preguntarlo el público; no tocar. |
| P10 | "Reintentar" de la fila `fila_invalida` (ref. 58) crea una ejecución que falla igual. | Sincronizar clientes | cosmético | No pulsarlo como si fuera a resolver el error. |
| P11 | Toasts tapan el selector de sucursal y el botón "Nuevo" en varias capturas. | Capturas `05f`, `05h-01..04`, `06b` | cosmético | Solo afecta las capturas. Se recapturaron sin toast `05h-05`, `05k`, `06e` y `02f`. |
| P12 | Sin "creados" en clientes ni productos: la copia ya está sincronizada. | Sincronización | dato | Para mostrar altas en vivo, hace falta una copia con registros nuevos. |
| P13 | Errores pedidos por la guía no presentes: "moneda sin equivalencia" en clientes. | Sincronizar clientes | dato | No se forzó ningún error. Si hace falta, requiere otra copia. |
| P14 | Ningún aviso de permisos ni error HTTP con super-admin. | Todo el recorrido | — | Sin acción. |
| P15 | La clave SAT mostrada para VIDRIO FLOAT (`30171708` / `H87`) corresponde a la regla de `appsettings.json`; en `appsettings.Development.json` la regla de "Vidrio plano" es `01010101` / `MTK`. **(causa no verificada: no sé qué configuración cargó el API del recorrido)** | Producto VIDRIO FLOAT CLARO 6MM | dato | Confirmar qué configuración corre en la demo antes de presentar la ficha fiscal. |
| P16 | Expediente de proveedor: el límite de 10 MB y los formatos PDF/XML/JPG/PNG vienen de una configuración marcada como "CONFIGURACION DE PRUEBA … pendiente de validar con Eliam/Millet". Los valores por defecto del código son 20 MB. | Adjuntos de proveedor | dato | Explicar como límite de prueba. |
| P17 | Los 5 documentos de TOUR-PROV-OK no aparecen separados en la bitácora de la primera página. | `/admin/auditoria` | cosmético | Buscar en la paginación completa. |
