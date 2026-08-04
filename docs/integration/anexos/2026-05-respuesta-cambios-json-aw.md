> **Nota de archivo (2026-07-05):** copia íntegra del documento entregado por
> el equipo A+W de Millet en mayo 2026, en respuesta a `solicitud_cambios_json_aw.md` v0.1.
> Corresponde al diseño de integración por JSON que **no se construyó**; se
> conserva como procedencia del contrato de vistas de
> [04-ingesta-pedidos-facturacion.md](../04-ingesta-pedidos-facturacion.md) (Anexo A, ADR-0048).

# Respuesta a solicitud de cambios — JSON de integración A+W → ERP

**De:** Equipo A+W — MILLET  
**Para:** Equipo ERP — Módulo de Facturación  
**Referencia:** `solicitud_cambios_json_aw.md` v0.1  
**Fecha:** Mayo 2026  
**Estado:** Implementación completada

---

## Resumen ejecutivo

De los 15 cambios al contrato JSON solicitados, **14 fueron implementados y desplegados**. Solo 1 se mantiene sin cambio por decisión interna. Los 3 flujos adicionales (sección 4) quedan pendientes de coordinación.

| Categoría | Solicitados | Implementados | Sin cambio | Pendientes |
|---|---|---|---|---|
| Críticos (1.x) | 5 | **5** | 0 | 0 |
| Estructura (2.x) | 7 | **6** | 1 | 0 |
| Limpieza (3.x) | 3 | **3** | 0 | 0 |
| **Total** | **15** | **14** | **1** | **0** |

---

## Categoría 1 — CRÍTICOS

### 1.1 Datos fiscales estructurados ✅ IMPLEMENTADO

Se agregaron los 3 campos solicitados como campos estructurados en el encabezado:

```json
"uso_cfdi": "G03",
"metodo_pago": "PUE",
"forma_pago": "03"
```

**Implementación técnica:**

| Campo | Origen en A+W | Lógica |
|---|---|---|
| `uso_cfdi` | `KA_LIEFERBED.FREMD_KEY` (JOIN con `K.OR_LIEFERBED`) | Si el pedido es de exportación (`AH_KOPF='Interfaz EDI'` o `GRUPPE='Ventas Internacionales'`) → `"S01"` (Sin efectos fiscales). Para los demás, se toma de la tabla `KA_LIEFERBED`. Si vacío/`<indf>` → `null`. **NOTA:** Se programará en A+W una validación para que este campo sea obligatorio al capturar el pedido, evitando que llegue vacío o `<indf>`. |
| `metodo_pago` | Derivado de `condicion_pago` | Si `condicion_pago = "CONTADO"` → `"PUE"`. Todo lo demás → `"PPD"` |
| `forma_pago` | `KA_ZAHLWEG.FREMD_KEY` (JOIN con `K.FI_ZAHLWEG`) | Toma la clave del catálogo. Si vacío/`<indf>` → `"99"` (Por definir). **NOTA:** Se programará en A+W una validación para que este campo sea obligatorio al capturar el pedido, evitando que llegue vacío o `<indf>`. |

**Catálogo de forma_pago disponible (tabla KA_ZAHLWEG):**

| Valor A+W | FREMD_KEY | Equivalencia SAT |
|---|---|---|
| CHEQUE | 02 | 02 — Cheque nominativo |
| EFECTIVO | 01 | 01 — Efectivo |
| POR DEFINIR | 99 | 99 — Por definir |
| TARJETA CREDITO | 04 | 04 — Tarjeta de crédito |
| TRANSFERENCIA | 03 | 03 — Transferencia electrónica |
| `<indf>` / vacío | - | 99 — Por definir (default) |

`notas_pedido` queda exclusivamente para texto operativo, tal como solicitaron.

---

### 1.2 Campo `obra` separado en ID y nombre ✅ IMPLEMENTADO

```json
"obra_id": 5000483,
"obra_nombre": "HOTEL CIELO"
```

Cuando `KO_OBJEKT_KUNDE = 0` (sin obra), ambos campos llegan como `null`. Se eliminó el campo concatenado `obra` del payload.

---

### 1.3 Sentinel `<indf>` → `null` ✅ IMPLEMENTADO

Todos los campos string del payload (encabezado, posiciones y componentes) pasan por limpieza. Cualquier valor `"<indf>"` se convierte a `null` JSON sin excepciones.

Caso especial: `divisa` ya no puede llegar como `<indf>` porque ahora se obtiene de `KA_WAEHRUNGEN.FREMD_KEY` con default `"MXN"` (ver punto 2.1).

---

### 1.4 Campo `pedido_sustituido_numero` ✅ IMPLEMENTADO

```json
"pedido_sustituido_numero": 10425678
```

Origen: `BW_AUFTR_KOPF.AH_HAUPT_AUFTR`. Si el valor es `0` → se envía como `null` (no es sustitución).

---

### 1.5 Identificador de versión del pedido ✅ IMPLEMENTADO

```json
"version_pedido": 3
```

**Implementación técnica:**

Se implementó como **contador incremental** con detección de cambios reales:

| Escenario | Comportamiento | Exit code |
|---|---|---|
| Pedido nuevo (sin historial) | Se envía con `version_pedido: 1` | **200** |
| Pedido sin cambios respecto al último envío | **No se envía**. No se crea registro en historial | **204** |
| Pedido con cambios respecto al último envío | Se envía con `version_pedido: N+1` | **200** |

La comparación se realiza contra el **último JSON exitoso** del mismo pedido almacenado en `HISTORIAL_INTEGRACION` (columna `Version` tipo INT agregada a la tabla). Se excluye el campo `version_pedido` de la comparación para evitar falsos positivos.

El exit code **204** permite a la aplicación llamadora (Gupta/A+W) informar al usuario que no hubo cambios y no fue necesario reenviar.

---

## Categoría 2 — De estructura

### 2.1 `divisa` con código ISO 4217 ✅ IMPLEMENTADO

```json
"divisa": "MXN"
```

Origen: `KA_WAEHRUNGEN.FREMD_KEY` mediante `JOIN ON W.WAEHRUNG = K.FI_WAEHRUNG`. Si no hay match o está vacío → default `"MXN"`.

---

### 2.2 Importes en una sola moneda ✅ IMPLEMENTADO

**Posiciones — antes (6 campos):**
```json
"importe_pieza_pesos": 1743.68,
"importe_pieza_dlls": 0.00,
"importe_posicion_pesos": 1743.68,
"importe_posicion_dlls": 0.00,
"descuento_pesos": 87.18,
"descuento_dlls": 0.00
```

**Posiciones — ahora (3 campos):**
```json
"importe_pieza": 1743.68,
"importe_posicion": 1743.68,
"descuento": 87.18
```

**Componentes — antes (2 campos):**
```json
"importe_pesos": 500.00,
"importe_dlls": 0.00
```

**Componentes — ahora (1 campo):**
```json
"importe": 500.00
```

La función `SYSADM.DEVUELVE_IMPORTE_Y_DESCTO_N` devuelve el importe en la moneda del pedido según `FI_WAEHRUNG`. En componentes se hace JOIN con `BW_AUFTR_KOPF` y `KA_WAEHRUNGEN` para determinar la moneda y seleccionar `PR_BETR_NETTO` (pesos) o `PR_BETR_NETTO_FW` (extranjera).

Se mantiene `descuento_porcentaje` como campo independiente.

---

### 2.3 `unidad_medida` normalizada ✅ IMPLEMENTADO

```json
"unidad_medida": "M2"
```

Se aplica `UPPER(REPLACE(PR_EINHEIT, '²', '2'))` en SQL. Valores normalizados: `M2`, `PZA`, `ML`, `KG`.

---

### 2.4 Eliminar campo `departamento` constante ⬜ SIN CAMBIO

**Decisión:** Se mantiene `"departamento": "COMERCIAL"` en el JSON por decisión interna. Se conserva para futura flexibilidad si se integran otros departamentos.

---

### 2.5 Eliminar `total_pie2` ✅ IMPLEMENTADO

Campo eliminado del payload. El ERP puede calcularlo si lo necesita: `total_m2 * 10.7639`.

Los campos `alto_pulgadas_fraccion` y `ancho_pulgadas_fraccion` se mantienen porque A+W los usa para documentación interna del pedido.

---

### 2.6 Datos básicos del cliente ✅ IMPLEMENTADO

```json
"numero_cliente": 56380,
"nombre_cliente": "GLOBAL CONSTRUCCIONES SA DE CV",
"rfc_cliente": "GCO123456789"
```

| Campo | Origen | Limpieza |
|---|---|---|
| `nombre_cliente` | `KU_KUNDEN.NAME1 + ' ' + KU_KUNDEN.NAME2` | LTRIM/RTRIM + ISNULL |
| `rfc_cliente` | `KU_KUNDEN.UST_ID` | Se eliminan todos los caracteres que no sean letras (A-Z, a-z) o números (0-9). Quita espacios, guiones, puntos, comas y cualquier símbolo especial |

---

### 2.7 `condicion_pago` con catálogo cerrado ✅ IMPLEMENTADO

El campo `condicion_pago` se alimenta de `FI_ZAHLBED` que está respaldado por la tabla catálogo `KA_ZAHLBED`. Los valores posibles son:

| Valor | Mapeo a `metodo_pago` |
|---|---|
| CONTADO | PUE |
| 7 DIAS | PPD |
| 15 DIAS | PPD |
| 21 DIAS | PPD |
| 30 DIAS | PPD |
| 45 DIAS | PPD |
| 60 DIAS | PPD |
| 75 DIAS | PPD |
| 90 DIAS | PPD |
| REPARTO | PPD |

Si el valor en la BD es `<indf>` o vacío → se envía como `"CONTADO"` (default). **NOTA:** Se programará en A+W una validación para que este campo sea obligatorio al capturar el pedido, evitando que llegue vacío o `<indf>`.

---

## Categoría 3 — Limpieza

### 3.1 Convención uniforme para campos opcionales ✅ IMPLEMENTADO

**Todos los campos definidos en el catálogo aparecen siempre en el JSON**, con valor `null` si no aplican. Nunca se omite la propiedad.

---

### 3.2 Teléfono normalizado ✅ IMPLEMENTADO

Se eliminan todos los caracteres que no sean números (0-9): espacios, guiones, paréntesis, barras, signos + y cualquier otro símbolo. Ejemplo: `"998 166 4177 / 998 845 1991"` → `"99816641779988451991"`. El campo `telefono_contacto_entrega` solo contiene dígitos en el JSON de salida.

---

### 3.3 Saltos de línea en `notas_pedido` ✅ IMPLEMENTADO

Se estandarizan saltos de línea de `\r\n` (Windows) a `\n` (Unix). La función `RtfToText()` se aplica en la query SQL y no deja artefactos RTF en el resultado.

---

## Eventos y operaciones adicionales (Sección 4)

### 4.1 Cancelación de pedido desde A+W

**Estado: RECIBIDO — Pendiente de coordinación**

Entendemos el requerimiento. Evaluaremos el endpoint propuesto y les compartiremos nuestra propuesta técnica.

### 4.2 Notificación inversa ERP → A+W al facturar

**Estado: RECIBIDO — Pendiente de coordinación**

Este flujo requiere que A+W exponga un endpoint receptor. Coordinaremos para definir el contrato de entrada.

### 4.3 Sincronización del master de Clientes

**Estado: RECIBIDO — Decisión confirmada**

Confirmamos: **el ERP es master de Clientes**. Coordinaremos el mecanismo de sincronización cuando sea necesario.

---

## Resumen final

| # | Prioridad | Cambio | Estado |
|---|---|---|---|
| 1.1 | Crítica | Datos fiscales (`uso_cfdi`, `metodo_pago`, `forma_pago`) | ✅ Implementado |
| 1.2 | Crítica | `obra` separada en `obra_id` + `obra_nombre` | ✅ Implementado |
| 1.3 | Crítica | `<indf>` → `null` | ✅ Implementado |
| 1.4 | Crítica | `pedido_sustituido_numero` | ✅ Implementado |
| 1.5 | Crítica | `version_pedido` con detección de cambios | ✅ Implementado |
| 2.1 | Alta | Divisa ISO 4217 | ✅ Implementado |
| 2.2 | Alta | Importes unificados (posiciones y componentes) | ✅ Implementado |
| 2.3 | Alta | Unidad normalizada | ✅ Implementado |
| 2.4 | Alta | Eliminar `departamento` | ⬜ Sin cambio |
| 2.5 | Alta | Eliminar `total_pie2` | ✅ Implementado |
| 2.6 | Alta | `nombre_cliente` + `rfc_cliente` | ✅ Implementado |
| 2.7 | Alta | `condicion_pago` catálogo cerrado | ✅ Implementado |
| 3.1 | Media | Opcionales siempre presentes con `null` | ✅ Implementado |
| 3.2 | Media | Teléfono solo dígitos | ✅ Implementado |
| 3.3 | Media | Notas: `\r\n` → `\n` | ✅ Implementado |
| 4.1 | Crítica | Cancelación de pedido | ⏳ Pendiente coordinación |
| 4.2 | Crítica | Callback de facturación | ⏳ Pendiente coordinación |
| 4.3 | A definir | Master de Clientes | ⏳ Pendiente coordinación |

**Totales JSON:** 14 implementados · 1 sin cambio · 0 pendientes  
**Flujos adicionales:** 3 pendientes de coordinación

---

## Lo que reconocemos como bien diseñado (recíproco)

Apreciamos que el documento reconozca los aspectos positivos del contrato actual. Por nuestra parte, valoramos la claridad, estructura y nivel de detalle de esta solicitud — facilitó significativamente la toma de decisiones y la implementación.

---

*Documento generado por el equipo A+W — MILLET. Mayo 2026.*
