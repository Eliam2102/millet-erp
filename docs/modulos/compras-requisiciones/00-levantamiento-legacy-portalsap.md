# Levantamiento — Submódulo Requisiciones (Compras)

> **Origen:** ingeniería inversa del portal legacy "PortalSAP / Millet" entregado
> por el cliente en `C:\Users\UserSP\Desktop\PORTALSAP 2 C_C new imp.excel_20250731`,
> complementado con el script de estructura de la BD `ConfigSapM`
> (archivo `Estructura_ConfigSapM-schema-only.sql` en la misma carpeta:
> 40 tablas, ~1180 líneas, **sin stored procedures incluidos**).
>
> **Fecha del levantamiento:** 2026-05-07 (rev. tras esquema verificado)
> **Objetivo:** capturar el flujo de negocio actual del proceso de requisiciones
> previo a orden de compra, para diseñar el submódulo **Requisiciones** dentro
> del módulo **Compras** del nuevo ERP Millet.
>
> **Estado:** borrador para revisión con el owner. No reemplaza una entrevista
> con los usuarios de negocio.

---

## 0. Cómo leer este documento

- **[Verificado]** = leído directamente en el código fuente.
- **[Inferido]** = deducido por nombres de SPs, columnas o convenciones, pero
  no confirmado en el cuerpo de un procedimiento o en BD.
- **[Gap]** = agujero de conocimiento que requiere confirmación con el cliente
  o lectura de los stored procedures de SQL Server (no incluidos en el
  código fuente entregado).

Toda lógica que vive dentro de stored procedures (T-SQL) **no fue auditada**:
el repositorio entregado contiene solo la app Web Forms y las capas de acceso
a datos (ADO.NET con `SqlCommand`/`StoredProcedure`). Los SPs viven en la BD
y **no están en el dump entregado** (que es solo `Estructura_*`). El cliente
deberá generar un script aparte con `Tasks → Generate Scripts → Stored
Procedures only` para tenerlos.

---

## 0.bis Decisiones del cliente (2026-05-07) — `[Verificado en sesión]`

Tras compartir el levantamiento con el cliente, queda **cerrado el scope
real** del submódulo Requisiciones del nuevo ERP. Esto invalida o reduce
varias secciones del documento; cada una se mantiene como referencia
histórica del legacy pero **NO** se replica en el ERP nuevo.

### 0.bis.1 Lógica real del módulo (clave)

> **El submódulo Requisiciones es un workflow stock-aware con bifurcación.**

Al autorizar una requisición, el sistema valida contra el inventario:

```
              Requisición autorizada (cabecera + líneas)
                              │
                              ▼
                  ¿Hay stock en almacén?
                /            │            \
              SÍ          PARCIAL            NO
              │              │                │
              ▼              ▼                ▼
       Movimiento     Movimiento (parte)   Solicitud de compra
       de almacén  +  Solicitud (saldo)    (OC) → llega → entra a stock
       (consumo)                             → consumo se completa
```

- **Si hay stock**: la línea se cubre con un **movimiento de almacén**
  (consumo). NO genera OC. Esto explica por qué solo el 2% de las 226k
  RQs históricas terminaron como Pedido a SAP.
- **Si no hay stock**: la línea se convierte en **solicitud de compra**.
- **Si hay stock parcial**: parte se cubre con almacén, el saldo se
  compra. Por eso existen los estatus 7 (`ASP` Aut Saldo Parcial) y
  8 (`ASC` Aut Saldo Completo) — re-autorización del **saldo no cubierto
  por stock** antes de generar OC.

Las columnas `UNIDADES_TRANSMITIDAS`, `UNIDADES_MOV_INVENTARIO` y
`UNIDADES_TRASLADO` en `DO_RQ_DET` son contadores de "cómo se cubrió cada
unidad solicitada".

> **Consecuencia arquitectónica importante:** el submódulo Requisiciones
> **acopla** Compras con **Almacén de no-producción** del ERP nuevo. La
> autorización dispara una consulta de disponibilidad y reserva contra
> Almacén. Hay que diseñar el contrato entre ambos módulos.

### 0.bis.2 Features del legacy DEPRECADAS (no se replican)

| Feature legacy | Decisión |
|---|---|
| **Cotizaciones / Solicitudes de cotización** (módulo DERECHOS=4) | DEPRECADA. El cliente confirma que el flujo no se usa en operación. Se elimina del scope del ERP nuevo. |
| **Control presupuestal** (`RQ_PERIODOS`, `RQ_MOVIMIENTOS`, módulo DERECHOS=5) | DEPRECADO. No hay control presupuestal vivo. **Gran simplificación**: no se diseña sub-feature de presupuesto en Compras. |
| **Caja chica 1** (módulo DERECHOS=8 "Gastos caja chica") | DEPRECADO. Reemplazada por Caja chica 2. |
| **Microsip** (`USUARIO_MICROSIP`) | DEPRECADO. Sistema contable previo, ya no en uso. |

### 0.bis.3 Features confirmadas activas

| Feature | Detalle |
|---|---|
| **Caja chica 2** (módulo DERECHOS=10 "Gastos caja chica 2") | Activa como **módulo separado** (no parte de Requisiciones). Vendedores y choferes registran gastos extras a posteriori, se modelan "como una compra" para control. **Vive fuera del submódulo Requisiciones.** |
| **Movimientos de almacén desde RQ** | Es la **lógica core** del módulo (ver 0.bis.1), no un caso especial. |
| **Re-autorización de saldos** | Activa. Estatus 7/8 son operativos. |
| **Autorización por cabecera** | Activa, niveles 1 y 2. |

### 0.bis.4 Datos maestros: cambio de propiedad

| Catálogo | Legacy | ERP nuevo |
|---|---|---|
| **Proveedores** | Vivían en SAP | **El ERP nuevo es dueño**. Se hace exportación inicial desde SAP al ERP. |
| **Artículos** (no-producción) | Vivían en SAP | **El ERP nuevo es dueño**. Misma exportación inicial. |

> Esto define la dirección de sincronización: en el ERP nuevo, **Compras
> es propietario de proveedores y artículos de no-producción**. SAP deja
> de ser fuente de verdad para esto. La sincronización con A+W (sistema
> productivo) se mantiene si A+W tiene su propio catálogo de artículos
> productivos, pero ese subset NO entra al submódulo Requisiciones.

### 0.bis.5 Scope final del submódulo Requisiciones del ERP nuevo

**Dentro:**
- Crear requisición (cabecera + líneas con artículo, cantidad, almacén
  destino, proyecto/equipo opcional, centro de costo).
- Workflow de autorización con niveles 1 y 2 por cabecera.
- Consulta de disponibilidad de stock al autorizar.
- Bifurcación automática:
  - Lo cubierto con stock → movimiento de salida en Almacén.
  - El saldo no cubierto → genera Solicitud/Orden de Compra.
- Re-autorización de saldos (cuando llega material parcial).
- Estados del ciclo: `INICIADO` → `CREADO` → `AUT PARCIAL`/`COMPLETA` →
  bifurcación → `OC CREADA`/`SURTIDO PARCIAL`/`SURTIDO COMPLETO` →
  `FINALIZADO`. (No hay rechazo formal; "rechazo" = `ELIMINADO`.)
- Notificaciones en cambios de estatus (vía módulo Notificaciones del
  ERP, ADR 0026).
- Auditoría completa (ADR 0008).

**Fuera:**
- Cotizaciones formales.
- Control presupuestal (no se diseña sub-feature en Compras).
- Multi-moneda y `CONTENEDOR` (importaciones).
- Multi-fondo.
- Caja chica (es módulo separado del ERP).
- Microsip y cualquier integración legacy contable.
- Multi-empresa SAP (pendiente confirmar si aplica una sociedad o varias).
- Autorización por línea (vestigial).
- DIOT separado por línea (vestigial).

---

## 1. Contexto del legacy

### 1.1 Qué es

Aplicación interna ASP.NET **Web Forms** (.NET Framework, no Core) que sirve
como **portal previo a SAP Business One**. Su razón de ser principal: SAP B1
**no tiene flujo de pre-autorización de compras**, así que el cliente
construyó este portal para que las áreas levanten **requisiciones**, las
**coticen** con proveedores, las **autoricen** en hasta dos niveles, y
**finalmente las inserten en SAP** como Solicitud de Compra (`OPRQ`) o
directamente como Orden de Compra (`OPOR`).

### 1.2 Solución dividida en tres proyectos

| Proyecto | Tipo | Responsabilidad |
|---|---|---|
| `MilletSap\Millet` | ASP.NET Web Forms (UI + code-behind) | Pantallas, navegación, lógica de presentación y mucho negocio mezclado |
| `AccesoMil-sap\AccesoMil` | Class library | Capa de datos hacia la BD propia del portal (SQL Server `ConfigSapM`) |
| `AccesoSap-BD_Millet_NEW\AccesoSap` | Class library | Capa de datos hacia la BD de SAP B1 (SQL Server `Millet_NEW`) |

### 1.3 Bases de datos involucradas (connection strings vistas en `Web.config`)

> **[Verificado]** existencia de las cadenas. **No se reproducen credenciales aquí.**

- `ConfigSapM` — BD propia del portal. **[Verificado]** 40 tablas con prefijos
  `CA_*` (catálogos), `DO_*` (documentos transaccionales), `RQ_*` (presupuesto
  y movimientos), más entidades sueltas (`USUARIOS`, `BLOQUEOS`, `DERECHOS`,
  `NOTIFICACIONES`, `PEDIDOS`, etc.). El esquema completo está en
  `Estructura_ConfigSapM-schema-only.sql`.
- `Millet_NEW` — BD de **SAP Business One** (tablas SAP `OPRQ`, `OPOR`,
  `OCRD`, `OITM`, etc., más wrappers/SPs `MILLET_*`). **[Gap]** No tenemos
  esquema de esta BD; lo que sabemos viene del código `AccesoSap`.
- `MILMAIN` (instancia `\AWBUSINESS`) — referenciada en config. La tabla
  `USUARIOS` tiene columna `USUARIO_ALFAK`, lo que confirma que esta cadena
  apunta a **A+W** (sistema comercial/producción del cliente). El portal
  resuelve el `USUARIO` del portal a su contraparte en A+W.
- También hay columna `USUARIO_MICROSIP` en `USUARIOS` — sugiere integración
  histórica con **Microsip** (sistema contable previo). **[Gap]** confirmar
  si sigue activa.

### 1.4 Pantallas relevantes al flujo de requisiciones

`MilletSap\Millet\`:

- `Login.aspx` — autenticación.
- `Principal.aspx` — landing.
- `RQ.aspx` / `RQS.aspx` — alta y edición de requisición (cabecera + detalle).
- `Cotizaciones.aspx` *(si existe en la build, validar)* — cotizaciones por
  línea.
- `AutorizarOrdenCompra.aspx` — pantalla de autorización por nivel.
- `VisorOrdenCompraAutorizado.aspx` — visor de órdenes ya autorizadas.
- `Documentos.aspx`, `ArchivosRSM.aspx` — anexos / cargas de soporte.
- `DerechosUsuarios.aspx` — administración de permisos granulares.
- `Notificaciones` (vía `AccesoMil\Notificaciones.cs`) — envío de avisos.

---

## 2. Modelo de datos (BD del portal `ConfigSapM`)

> **[Verificado]** desde el archivo `Estructura_ConfigSapM-schema-only.sql`.
> 40 tablas en el esquema `dbo`. Convención de prefijos:
> `CA_*` = catálogo, `DO_*` = documento transaccional, `RQ_*` = soporte de
> requisiciones (presupuesto, movimientos), resto = entidades transversales.

### 2.1 Cabecera y detalle de requisición — `[Verificado]`

`DO_RQ` (cabecera, PK `RQ_ID`):
`RQ_ID`, `TIPO_DOCTO char(1)`, `CLASIFICACION_ID`, `FOLIO varchar(10)`,
`FECHA`, `CLAVE_PROV`, `PROVEEDOR_ID`, `SUCURSAL_ID`, `DEPARTAMENTO_ID`,
`REQUISITANTE_ID`, `COND_PAGO_ID`, `ALMACEN_ID`, `DESCRIPCION`,
`ESTATUS_ID` (FK → `CA_ESTATUS`), `FECHA_ENTREGA`, `PRIORIDAD_ID`,
`USUARIO_CREADOR`, `FECHA_HORA_CREACION`, `USUARIO_ULT_MODIF`,
`FECHA_HORA_ULT_MODIF`, `MONEDA_ID`, `TIPO_CAMBIO numeric(18,6)`,
`NOMBRE_PROVEEDOR`, `CLAVE_CENTRO_COSTO`, `FECHA_TRANSMITIO`,
`USUARIO_TRANSMITIO`, **`COMPRA int`**, **`MOVIMIENTO int`**,
`CLAVE_SUCURSAL`, `CREADOR_SAP`, `CONTENEDOR`.

> Notas:
> - `TIPO_DOCTO` (un solo char) es el discriminador del documento. Una RQ
>   "normal" y una **cotización** son ambas filas en `DO_RQ` con distinto
>   `TIPO_DOCTO`. Esto explica que NO existan tablas `DO_RQ_COT*` aparte.
> - `COMPRA` y `MOVIMIENTO` parecen referenciar al `PEDIDO_ID` y al
>   `MOVIMIENTO_ID` que se generan al consumir la requisición. **[Inferido]**
>   sin FK formal a esas tablas — es un puntero "blando".
> - `CONTENEDOR` sugiere soporte para **importaciones** (RQs con contenedor).
>   **[Gap]** Confirmar si Compras de no-producción incluye importaciones o
>   si esto es vestigial.

`DO_RQ_DET` (detalle, PK `RQ_DET_ID`, FK `RQ_ID` → `DO_RQ`):
`RQ_DET_ID`, `RQ_ID`, `POSICION`, `CLAVE_ARTICULO`, `UNIDADES numeric(18,5)`,
`UNIDADES_COT_DEV`, `UNIDADES_A_COT`, `UNIDAD_MEDIDA`,
`PRECIO_UNITARIO numeric(18,6)`, `PCTJE_DSCTO`, `PRECIO_TOTAL_NETO`,
`IMPORTE_IMPUESTO`, `CUENTA_ID`, `NOTAS`, `FECHA_REQUERIDA`,
`NOMBRE_ARTICULO`, `PROYECTO`, `PROYECTO_ID`, `EQUIPO`, `EQUIPO_ID`,
`UNIDADES_TRANSMITIDAS`, `UNIDADES_MOV_INVENTARIO`, `UNIDADES_TRASLADO`,
`SUCURSAL_POS_ID`, `SUCURSAL_POS`, `SUCURSAL_POS_ID_2`, `EQUIPO_ID_2`.

> Las columnas `UNIDADES_*` muestran que cada línea hace seguimiento de
> cuánto se cotizó, transmitió, movió a inventario y trasladó. Es decir,
> **una línea de RQ puede consumirse en múltiples eventos**.

`DO_RQ_AUT` (autorización a nivel cabecera, PK `RQ_AUT_ID`):
`RQ_AUT_ID`, `RQ_ID`, `NIVEL int`, `USUARIO_ID`, `FECHA_HORA_AUT`, `NOTAS`.

`DO_RQ_DET_AUT` (autorización a nivel línea, PK `RQ_DET_AUT_ID`):
`RQ_DET_AUT_ID`, `RQ_ID`, `POSICION`, `NIVEL`, `USUARIO_ID`,
`FECHA_HORA_AUT`, `NOTAS`.

> **Hallazgo:** existe autorización **por línea**, no solo por cabecera.
> Es decir, en una RQ con 5 líneas un autorizador puede aprobar solo 3.
> El nuevo ERP debe modelar esto explícitamente.

### 2.2 Ligas entre documentos (cotizaciones, pedidos, etc.) — `[Verificado]`

`DO_RQ_LIGAS` — auto-relación de **cabeceras** de `DO_RQ`:
`DO_RQ_LIGA_ID`, `DO_RQ_FTE_ID`, `DO_RQ_DEST_ID`, `FOLIO_FTE`, `FOLIO_DEST`.

`DO_RQ_DET_LIGAS` — auto-relación de **detalles**:
`DO_RQ_DET_LIGA_ID`, `DO_RQ_DET_FTE_ID`, `DO_RQ_DET_DEST_ID`, `FOLIO_FTE`,
`POS_FTE`, `FOLIO_DEST`, `POS_DEST`.

> **Modelo real:** todo (RQ, cotización, ¿devolución?) son filas en `DO_RQ`
> diferenciadas por `TIPO_DOCTO`. Las relaciones entre documentos viven en
> `DO_RQ_LIGAS` (cabecera) y `DO_RQ_DET_LIGAS` (detalle, con folio + posición
> origen y destino). **No hay tablas separadas `DO_RQ_COT*`.**

### 2.3 Pedido (Orden de Compra ya formateada para SAP) — `[Verificado]`

`PEDIDOS` (v1) y `PEDIDOS_V2` son las tablas que **acumulan** lo que se
transmite a SAP. Casi todas sus columnas tienen sufijo `_SAP`:
`CLAVE_PROV_SAP`, `NOMBRE_PROVEEDOR_SAP`, `CLAVE_SUCURSAL_SAP`,
`CLAVE_MONEDA_SAP`, `CLAVE_CENTRO_COSTO_SAP`, `CLAVE_CREADOR_SAP`, etc.,
más `FECHA_TRANSMITIO`, `USUARIO_TRANSMITIO`, `TIPO_GASTO`, `FACTURA`.

`PEDIDOS_DET` y `PEDIDOS_DET_V2` (detalle) almacenan también
`SEGMENTO_1_SAP`, `SEGMENTO_2_SAP`, `SEGMENTO_3_SAP` (la **cuenta contable
segmentada** de SAP), `CLAVE_IVA_SAP`, `TASA_IVA_SAP`. La V2 añade DIOT
(`DIOT_CLAVE_PROV_SAP`).

> **Hallazgo crítico:** **`PEDIDOS` y `PEDIDOS_V2` NO tienen FK a `DO_RQ`.**
> No existe trazabilidad estructural entre la requisición y el pedido que
> derivó de ella. La relación se mantiene únicamente vía `DO_RQ_LIGAS` o
> via el campo blando `DO_RQ.COMPRA`. En el ERP nuevo esto debe ser una
> relación explícita y consultable.

### 2.4 Presupuesto y movimientos contables — `[Verificado]` (NO inferido antes)

`RQ_PERIODOS` — **presupuesto anual por mes**, granularidad
(cuenta + centro de costo + fondo + año):
`PERIODO_ID`, `ANO`, `CUENTA_ID`, `CENTRO_COSTO_ID`, `FONDO_ID`,
`ENERO numeric(15,2)` ... `DICIEMBRE numeric(15,2)`, auditoría.

`RQ_MOVIMIENTOS` — **registro de afectaciones** entre cuentas y centros
de costo:
`MOVIMIENTO_ID`, `TIPO_MOVTO char(1)`, `FECHA`, `CUENTA_FTE_ID`,
`CENTRO_COSTO_FTE_ID`, `IMPORTE_CUENTA_FTE`, `CUENTA_DEST_ID`,
`CENTRO_COSTO_DEST_ID`, `IMPORTE_CUENTA_DEST`, `CANCELADO`, auditoría.

> **Implicación importante:** el portal **hace control presupuestal previo
> al envío a SAP**. Cada RQ aprobada (o cada autorización) consume un
> presupuesto definido por (año, mes, cuenta, centro de costo, fondo). Esto
> **no estaba documentado en mi primer levantamiento**: el módulo de
> Compras del nuevo ERP debe coordinarse con un módulo (sub-feature) de
> presupuestos. **[Gap]** Confirmar si los Indicadores S&OP usan esta
> misma información o son independientes.

### 2.5 Seguridad y administración — `[Verificado]`

`USUARIOS` (PK `USUARIO_ID`):
`USUARIO_ID`, `NOMBRE`, `APELLIDO`, `USUARIO`, **`CONTRASENA varchar(50)`**
(¡texto plano!), `DEPARTAMENTO_ID` (FK), `SUCURSAL_ID`, `TELEFONO`, `EMAIL`,
**`IMAGEN image NULL`** (foto del usuario embebida en BD), `ESTATUS`, `TIPO`,
`PUESTO`, `NOTAS`, `USUARIO_ALFAK`, `USUARIO_MICROSIP`, auditoría,
`CLAVE_USUARIO_SAP`, `CLAVES_USUARIO_SAP varchar(200)` (plural — sugiere
multi-empresa SAP), `CLAVES_SUCURSAL_SAP varchar(500)`,
`CLAVE_PROVEEDOR_CAJA_CHICA_SAP`.

`DERECHOS` (PK `DERECHO_ID`):
`DERECHO_ID`, `NOMBRE`, `TIPO varchar(10) DEFAULT 'T'`, `PADRE_ID`.

`DERECHOS_USUARIOS` (sin PK, tabla puente):
`USUARIO_ID`, `DERECHO_ID`.

> **Corrección importante:** `DERECHOS_USUARIOS` solo tiene 2 columnas.
> **No hay flag `PERMITIDO`** como yo había inferido. La regla real es:
> **si la fila existe, el usuario tiene el derecho; si no, no lo tiene.**

`BLOQUEOS`: `BLOQUEO_ID`, `TABLA varchar(50)`, `ELEMENTO_ID`, `USUARIO_ID`,
`FECHA_HORA`. Bloqueo pesimista genérico.

`SESIONES`: `SESION_ID`, `USUARIO_ID`, `NOMBRE_HOST`, `DIRECCION_IP`,
`ACTIVO char(1)`, `PAGINA_ACTUAL`, `FECHA_HORA`. Sesiones simples sin token.

`NOTIFICACIONES`: `NOTIFICACION_ID`, `DOCTO_ID`, `USUARIO_NOTIFICADO_ID`,
`CORREO_ELECTRONICO`, `CC`, `FECHA_HORA_ENVIO`, `MENSAJE varchar(5000)`,
`TIPO_EVENTO`, `SELECCION`, `PROCESO_ID`. Cola de eventos a notificar.

`REGISTRO`: `ELEMENTO_ID`, `NOMBRE`, `TIPO`, `PADRE_ID`, `VALOR`, `VALOR_2`,
`VALOR_3`, `BASE_DATOS`. **[Inferido]** tabla genérica de configuración
clave-valor del portal.

`REPORTES` y `REPORTES_USUARIOS`: catálogo de reportes (path al .rpt en
disco) y permisos por usuario.

`RSM`: archivos asociados a un usuario (`NOMBRE_ARCHIVO`, `RUTA`).
**[Inferido]** módulo de gestión documental ligado a la pantalla
`ArchivosRSM.aspx`.

### 2.6 Otras tablas presentes (fuera del scope de Requisiciones)

- `CHEQUE`, módulo de cheques fechados (pantallas `ChequesFechados.aspx`,
  `VisorCheques.aspx`).
- `ORDEN_PRESUPUESTO`, `ORDEN_PRESUPUESTO_DET`, `PRESUPUESTO`,
  `PRESUPUESTO_DET`, `SALIDA_PRESUPUESTO`, `SALIDA_PRESUPUESTO_DET`,
  `ESTADO_PRODUCTO`, `TIPO_CLIENTE`, `COLOR` — módulo de presupuestos
  comerciales (cotizaciones a clientes finales). **No es parte de Compras.**
- `S&OP_INDICADORES`, `S&OP_PROCESOS` — módulo de Sales & Operations
  Planning. **No es parte de Compras.**
- `CA_ALMACENES` — usado tanto por Compras como por inventario.

### 2.7 Diagrama lógico mínimo de Requisiciones

```
              CA_ESTATUS (catálogo)
                   ▲
                   │ (FK)
   USUARIOS ──FK── DO_RQ ──FK─▶ CA_DEPARTAMENTOS
        ▲          │  ▲
        │          │  │ (FK)
        │ (FK)     │  └──── CA_PRIORIDADES
        │          │  └──── CA_SUCURSALES
        │          │
   DO_RQ_AUT       │
   DO_RQ_DET_AUT   ▼
                DO_RQ_DET ◀── DO_RQ_DET_LIGAS ──▶ DO_RQ_DET (otra)
                   │
                   ▼ (consumo no rastreado por FK)
                PEDIDOS / PEDIDOS_V2 ──▶ PEDIDOS_DET / _V2  (formato SAP)
                   │
                   ▼ (transmisión via AccesoSap)
                Millet_NEW (BD SAP B1) — MILLET_INSERTA_RQS / similares

DO_RQ_LIGAS: relaciona dos DO_RQ (RQ ↔ cotización, etc.)
RQ_PERIODOS / RQ_MOVIMIENTOS: control presupuestal por (año, cuenta, CC,
                              fondo) y registro de afectaciones contables.
NOTIFICACIONES: cola de eventos a comunicar (no se vio el envío real).
BLOQUEOS: lock pesimista durante edición.
```

---

## 3. Estados y ciclo de vida — `[Verificado]` (catálogo `CA_ESTATUS`)

El catálogo `CA_ESTATUS` tiene 22 estados que cubren **varios módulos**, no
solo Requisiciones. Los relevantes para el ciclo de RQ son del 1 al 11:

| ID | CLAVE | NOMBRE | Pertenece a |
|----|-------|--------|-------------|
| **1** | DOINI | INICIADO | RQ |
| **2** | DOCRD | CREADO | RQ |
| **3** | DOEMD | ELIMINADO | RQ (soft delete) |
| **4** | AUTPAR | AUTORIZACIÓN PARCIAL | RQ |
| **5** | AUTCOM | AUTORIZACIÓN COMPLETA | RQ |
| **6** | DOFIN | FINALIZADO | RQ (terminal) |
| **7** | ASP | AUT SALDO PARCIAL | RQ — parcialmente surtida |
| **8** | ASC | AUT SALDO COMPLETO | RQ — totalmente surtida |
| **9** | OCC | ORDEN DE COMPRA CREADA | OC derivada de RQ |
| **10** | SP | SURTIDO PARCIAL | RQ |
| **11** | SC | SURTIDO COMPLETO | RQ |
| 13–20 | PR/POSI/POA/OSP/OSC/SR/SE/PE | varios | Presupuesto comercial (otro módulo) |
| 21–23 | CHE | DEPOSITADO/CANCELADO/DEVUELTO | Cheques (otro módulo) |
| (sin 12) | — | — | — |

> **Corrección importante:** mi primera versión del documento decía que el
> estatus 10 era "En Proceso" — eso era inferencia. **El estatus 10 es
> "SURTIDO PARCIAL"** y forma parte del ciclo de surtido posterior a la OC.
> No existe un estatus formal "Rechazada"; el rechazo se modela como
> **ELIMINADO (3)** — soft delete.

### Workflow real de estados (deducido)

```
   1 INICIADO ──▶ 2 CREADO ──▶ 4 AUT PARCIAL (N1) ──▶ 5 AUT COMPLETA (N1+N2)
                      │                                       │
                      │                                       ▼
                      ▼                              9 ORDEN DE COMPRA CREADA
                  3 ELIMINADO                                 │
                  (en cualquier                               ▼
                   momento)                          10 SURTIDO PARCIAL ──▶ 11 SURTIDO COMPLETO
                                                              │                      │
                                                              ▼                      ▼
                                                          7 ASP                  8 ASC
                                                                                     │
                                                                                     ▼
                                                                              6 FINALIZADO
```

> El par **ASP/ASC** ("aut saldo parcial/completo") es un segundo ciclo de
> autorización **sobre el saldo no surtido** de una RQ ya en proceso. Es
> decir: si una RQ se surte parcialmente y queda un saldo, ese saldo
> requiere una nueva autorización antes de generar otra OC.

Métodos C# que disparan transiciones (`AccesoMil\Requisiciones.cs`):

- `ActualizarEstatusRequisicion(RQ_ID, USUARIO, ESTATUS_ACTUAL, ESTATUS_NUEVO)`
- `MODIF_EST_RQ(RQ_ID, USUARIO, ESTATUS_ID)`
- `ObtenerEstatusDocto(RQ_ID)` para validación previa.

Métodos para órdenes de compra (`AccesoMil\AutorizarOrdenCompra.cs`):

- `Autorizar_Orden_C(ORDEN_COMPRA, TIPO_AUT, USUARIO)` →
  SP `MILLET_AUTORIZAR_PEDIDOS_COMPRA`.
- `DesAutorizar_Orden_C(...)` → SP `MILLET_DESAUTORIZAR_PEDIDOS_COMPRA`.
- `ValidaAut2(ORDEN_COMPRA, TIPO_AUT)` → SP `MILLET_EXISTE_PO_AUT_1_2`.

> **[Gap residual]** Las transiciones exactas (qué disparador permite saltar
> de qué estatus a cuál) viven en los SPs y en el code-behind. La tabla
> arriba es deducida del catálogo + comportamiento UI, no de la lógica de
> SPs.

---

## 4. Roles, permisos y niveles de autorización

### 4.1 Modelo de autorización — `[Verificado]` (91 derechos reales)

- **No hay roles monolíticos**. Hay **derechos granulares por usuario** vía
  tabla puente `DERECHOS_USUARIOS (USUARIO_ID, DERECHO_ID)`. **Si existe la
  fila, el usuario tiene el derecho.** No hay flag `PERMITIDO`.
- El catálogo `DERECHOS` tiene **91 entradas** organizadas en árbol de 3
  niveles vía `(TIPO, PADRE_ID)`:
  - `TIPO='P'` → módulo raíz (`PADRE_ID=0`). 13 módulos.
  - `TIPO='H'` → acción dentro de un módulo (`PADRE_ID` = id del módulo).
  - `TIPO='HH'` → sub-acción (`PADRE_ID` = id del derecho hijo).
- Hay **2,193 asignaciones** en `DERECHOS_USUARIOS` (verificado desde el
  dump) — el sistema se usa intensivamente con permisos personalizados.

### 4.2 Módulos raíz (DERECHO_ID, NOMBRE)

Todos relevantes para entender el alcance del legacy. Negrita = relevante
para el submódulo Requisiciones del nuevo ERP:

- 1 = Configuración
- 2 = Archivos RSM
- **3 = Requisiciones** ← nuestro foco
- **4 = Solicitudes de cotización** ← módulo separado en el legacy
- **5 = Control presupuestal** ← módulo separado en el legacy
- 6 = Reportes
- 7 = ERP-S&OP
- 8 = Gastos caja chica
- 9 = Salidas de mercancía
- 10 = Gastos caja chica 2
- 13 = Cheques
- **14 = Autorizar Orden Compra** ← módulo separado, derecho 10301 "Guardar"

### 4.3 Acciones dentro del módulo Requisiciones (PADRE_ID = 3)

26 derechos granulares — esto refina mucho la lista de permisos a modelar
en el ERP nuevo:

| ID | Acción |
|----|--------|
| 301 | Nuevo |
| 302 | Abrir |
| 303 | Modificar |
| 304 | Eliminar |
| 305 | **Autorizar nivel 1** |
| 306 | **Autorizar nivel 2** |
| 307 | Seleccionar fecha |
| 308 | **Seleccionar requisitante** (delegación: el creador ≠ requisitante) |
| 309 | **Generar cotización** (de RQ a Solicitud de cotización) |
| 310 | **Modificar requisiciones de otros usuarios** (override) |
| 311 | Generar movimiento de inventario sin autorización |
| 312 | Generar movto. de inventario con aut. nivel 1 |
| 313 | Generar movto. de inventario con aut. nivel 2 |
| 314 | Eliminar con autorización nivel 1 |
| 315 | Eliminar con autorización nivel 2 |
| 316 | Eliminar con solicitud de compra generada |
| 317 | **Transmitir a SAP** |
| 318–320 | Generar traslado de inventario sin / con N1 / con N2 |

> Tres hallazgos importantes:
> - **Una requisición puede generar movimientos o traslados de inventario**,
>   no solo OCs. Es decir, "Requisición" en el legacy es más amplia que
>   "petición de compra"; abarca también movimientos de stock interno.
> - El **creador puede ser distinto al requisitante** (308). Implica
>   delegación formal.
> - La acción **"Generar cotización" (309)** confirma que de una RQ puede
>   nacer una solicitud de cotización (módulo 4). Confirmamos que
>   cotizaciones es un módulo separado en la UI, aunque comparte tablas.

### 4.4 Acciones de Solicitudes de cotización (PADRE_ID = 4)

23 acciones, en su mayoría espejo de Requisiciones, más:

- 409 = Generar orden de compra (de cotización a OC)
- 414 = Modificar con orden de compra generada
- 415 = Modificar con orden de compra cancelada
- 418 = Seleccionar serie para orden de compra (folio)
- 422–423 = Eliminar con autorización de saldo parcial / completo

### 4.5 Acciones de Control presupuestal (PADRE_ID = 5)

8 acciones que confirman el módulo:

- 501 Visualizar control presupuestal
- 502 Inicializar periodos
- 503 Modificar periodos
- 504 Agregar periodo cuenta-CC
- 505 Eliminar periodo cuenta-CC
- 506 Registrar movimientos
- 507 Modificar movimientos
- 508 Eliminar movimientos

> El árbol completo de 91 derechos está en `Datos_Muestreados.sql` líneas
> 137–227 (BD `ConfigSapM`, tabla `DERECHOS`).

> Nota técnica: `REPORTES_USUARIOS (REPORTE_ID, USUARIO_ID, DERECHO)` tiene
> un campo `DERECHO int DEFAULT 1` — único caso con flag. Es excepción.

### 4.2 Niveles 1 y 2 — `[Inferido]`

- **Nivel 1** suele ser jefatura del departamento solicitante.
- **Nivel 2** suele ser gerencia / dirección.
- Las autorizaciones se persisten una por nivel en `DO_RQ_AUT` con
  `NIVEL ∈ {1, 2}` y `NOTAS` opcional.
- **[Gap]** No se vio en C# regla "monto > X exige nivel 2". Probablemente
  está en SP, o no existe formalmente y depende del criterio del
  autorizador.

### 4.3 Hardcoding inquietante

En `VisorOrdenCompraAutorizado.aspx.cs` se vio un `switch` por
`USUARIO_ID` que mapea **manualmente** ciertos IDs a departamentos:

```csharp
switch (USUARIO_ID) {
  case "218": DEPARTAMENTO = 1; // VENTAS_EXP
  case "11":  DEPARTAMENTO = 2; // COMPRAS_MP
  default:    DEPARTAMENTO = 3; // OTRO
}
```

Esto debe migrarse a una asignación basada en datos (rol o pertenencia a
departamento), no en IDs literales.

---

## 5. Flujo de negocio end-to-end

### Diagrama lineal del happy path

```
[Solicitante]                            [Aut. N1]            [Aut. N2]            [Comprador]            [SAP B1]
     │                                       │                    │                     │                     │
     │  1. Crea RQ (DO_RQ + DO_RQ_DET)       │                    │                     │                     │
     │  estatus=1                            │                    │                     │                     │
     │                                       │                    │                     │                     │
     │  2. "Transmitir" → estatus=2          │                    │                     │                     │
     │  notifica a Aut. N1 ─────────────────▶│                    │                     │                     │
     │                                       │                    │                     │                     │
     │  3. (Opcional) Cargar cotizaciones    │                    │                     │                     │
     │     DO_RQ_COT + DO_RQ_LIGAS           │                    │                     │                     │
     │                                       │                    │                     │                     │
     │                              4. Aut. N1 aprueba/rechaza    │                     │                     │
     │                              estatus=4 (ok) o 3 (rechazo)  │                     │                     │
     │                              persiste DO_RQ_AUT N=1 ──────▶│                     │                     │
     │                                       │                    │                     │                     │
     │                                       │  5. Aut. N2 aprueba/rechaza              │                     │
     │                                       │  estatus=Autorizada                      │                     │
     │                                       │  persiste DO_RQ_AUT N=2 ────────────────▶│                     │
     │                                       │                    │                     │                     │
     │                                       │                    │   6. Transmite a SAP (MILLET_INSERTA_RQS)  │
     │                                       │                    │   inserta en OPRQ + ORQD ─────────────────▶│
     │                                       │                    │                     │   crea SC en SAP    │
     │                                       │                    │                     │                     │
     │  7. Notificación de cierre / log de auditoría              │                     │                     │
```

### Pasos en detalle

1. **Captura.** El solicitante abre `RQ.aspx`, llena cabecera y agrega líneas.
   Persistencia vía `INSERTAR_ACTUALIZAR_DO_RQ` y
   `INSERTAR_ACTUALIZAR_DO_RQ_DET`. Estatus inicial = `1`.
2. **Transmisión interna (no a SAP todavía).** El solicitante presiona
   "Transmitir": `ACTUALIZA_DO_RQ` marca `USUARIO_TRANSMITIO`, estatus pasa
   a `2`, se llama a `InsertarNotificacciones(...)`.
3. **Cotización (opcional).** Comprador o solicitante carga cotizaciones por
   proveedor; cada línea de cotización se liga a una o varias líneas de la
   RQ via `DO_RQ_LIGAS`. Selección de "ganadora" no se vio explícita en
   código C# — **[Gap]**, probable SP.
4. **Autorización N1.** El autorizador entra a `RQ.aspx?ID_RQ=...` o a
   `AutorizarOrdenCompra.aspx`. Marca checkbox de autorización y
   opcionalmente pone notas. Persiste `DO_RQ_AUT` con `NIVEL=1`.
   Estatus → `4` o `3`.
5. **Autorización N2.** Mismo patrón con `NIVEL=2`. Estatus → "Autorizada".
6. **Envío a SAP.** Comprador (con derecho "Transmitir") dispara
   `AccesoSap.SolicitudCompra.AgregarRqs(...)`, que invoca el SP
   `MILLET_INSERTA_RQS` en la BD `Millet_NEW`. Ese SP es el que escribe en
   las tablas SAP. **[Gap]** Confirmar exactamente a qué tablas SAP escribe
   (¿`OPRQ`+`ORQD` o directamente `OPOR`+`POR1`?).
7. **Notificación / auditoría.** `InsertarNotificacciones` registra el
   evento. **[Gap]** No se vio envío SMTP real en el C# leído — puede que
   haya un job externo que lee `NOTIFICACIONES` y manda los correos.

---

## 6. Integración con SAP Business One

### 6.1 Patrón de integración

- **Único punto de cruce:** SPs prefijados `MILLET_*` en la BD `Millet_NEW`.
  El portal **no llama el DI/Service Layer de SAP**: escribe directo en la
  BD de SAP a través de SPs custom que el cliente creó.
- **Direccionalidad:** principalmente **portal → SAP**. No se vio
  realimentación automática SAP → portal (p.ej. el portal no se entera si la
  Solicitud de Compra fue luego convertida en OC en SAP).

### 6.2 Operaciones detectadas

`AccesoSap-BD_Millet_NEW\AccesoSap`:

- `SolicitudCompra.cs` → SPs `MILLET_OBTENER_RQS`, `MILLET_INSERTA_RQS`.
- `Pedidos.cs` → operaciones de Orden de Compra.
- `Proveedores.cs` → `MILLET_OBTENER_PROVEEDORES`.
- `Articulos.cs` → catálogo de items.
- `CuentaContable.cs`, `Ivas.cs`, `NormasReparto.cs` — catálogos.

### 6.3 Tablas SAP B1 esperables (referencia estándar de SAP)

- `OPRQ` / `ORQD` — Solicitud de Compra (cabecera/detalle).
- `OPOR` / `POR1` — Orden de Compra.
- `OCRD` — Business Partners (proveedores).
- `OITM` — Items.
- `OCST` — Cost Centers / centros de costo.
- `OASR` — Naturaleza de gasto.

> **[Gap crítico]** La auditoría debe pedir al cliente el código fuente de
> los SPs `MILLET_INSERTA_RQS`, `MILLET_AUTORIZAR_PEDIDOS_COMPRA` y similares
> antes de modelar la integración del nuevo ERP.

---

## 7. Reglas de negocio no obvias detectadas

1. **Bloqueo pesimista** sobre la RQ mientras un usuario la edita
   (`Bloqueos.AgregarActualizarBloqueo("Requisicion", RQ_ID, USUARIO_ID)`).
   Otro usuario no puede abrir la misma RQ hasta liberación. **[Verificado]**
   tabla `BLOQUEOS` con 9 filas activas en el dump.
2. **Folio autogenerado** dentro del SP `INSERTAR_ACTUALIZAR_DO_RQ`.
   **[Verificado]** formato observado: `C########` (letra "C" + 8 dígitos
   con padding cero, ej. `C00000001`, `C00000226173`). Folio único en toda
   `DO_RQ` independientemente de `TIPO_DOCTO`.
3. **TIPO_DOCTO**: en las 50 muestras observadas todas son `'R'`
   (Requisición). **[Gap parcial]** Probable que existan otros valores
   ('C' para cotización, etc.) pero no aparecen en la muestra. Las
   tablas `DO_RQ_LIGAS` están vacías, por lo que **el flujo de cotizar
   desde una RQ no parece estar materializado en datos** — aunque la UI
   (módulo 4 "Solicitudes de cotización") sí existe.
4. **Multi-moneda no se usa en producción**: `MONEDA_ID` siempre NULL y
   `TIPO_CAMBIO` siempre 0.000000 en las 50 muestras. La columna existe
   pero el sistema opera de facto en una sola moneda (MXN implícita).
5. **Importaciones no se usan en producción**: `CONTENEDOR` siempre NULL
   en las muestras. La columna existe pero no se usa.
6. **Transmisión parcial a SAP**: la columna `UNIDADES_TRANSMITIDAS` en
   `DO_RQ_DET` permite transmitir una línea en partes. Existe
   `ACTUALIZA_UNIDADES_TRANSMITIDAS(RQ_ID, RQ_DET_ID, UNIDADES)`.
7. **Triple "consumo" de una línea de RQ**: `UNIDADES_TRANSMITIDAS` (a SAP),
   `UNIDADES_MOV_INVENTARIO` (entrada a almacén) y `UNIDADES_TRASLADO`
   (entre almacenes). Una línea no se "cierra" hasta que esos consumos
   completan la cantidad solicitada.
8. **Autorización a nivel línea** además de cabecera: `DO_RQ_DET_AUT` permite
   autorizar líneas individualmente. Implicación: una RQ puede quedar
   "parcialmente autorizada" — el comprador transmite a SAP solo lo
   aprobado.
9. **Centro de costo y proyecto a nivel línea**: cada `DO_RQ_DET` puede
   apuntar a `PROYECTO_ID`, `EQUIPO_ID` y `SUCURSAL_POS_ID` distintos al de
   la cabecera. Hay incluso un segundo equipo y sucursal POS
   (`*_ID_2`) — **[Gap]** confirmar para qué.
10. **Control presupuestal previo** vía `RQ_PERIODOS` (presupuesto anual
    mensualizado por cuenta + centro de costo + fondo) y `RQ_MOVIMIENTOS`
    (afectaciones contables). **[Verificado]** modelo confirmado:
    - 1,034 periodos definidos en `RQ_PERIODOS` (varios años).
    - **Distribución típica**: el monto anual se reparte uniforme en los 12
      meses (todas las muestras del 2015 muestran el mismo valor 12 veces:
      ej. cuenta 4971061 = 174,883/mes = 2.1M anual).
    - Hay excepciones: algunos periodos tienen meses en cero y solo ciertos
      meses con valor (estacionales).
    - 8,158 movimientos en `RQ_MOVIMIENTOS` con dos `TIPO_MOVTO`:
      - `'A'` = ALTA (agregar presupuesto a una cuenta+CC+fondo).
      - `'T'` = TRASPASO (entre cuentas/CC: FTE con importe negativo y
        DEST con importe positivo, simétricos).
    - **Solo 1 fondo activo** (`CA_FONDOS`) — el sistema soporta múltiples
      pero no se usa.
    **[Gap]** Las reglas exactas de cómo una RQ aprobada **consume** del
    presupuesto siguen viviendo en SPs.
11. **Mapeo usuario portal ↔ SAP** vía `CLAVE_USUARIO_SAP` y la columna
    plural `CLAVES_USUARIO_SAP` (varchar 200) — sugiere multi-empresa SAP
    o múltiples claves por usuario. Igual con `CLAVES_SUCURSAL_SAP` (500).
12. **Caja chica**: el usuario tiene
    `CLAVE_PROVEEDOR_CAJA_CHICA_SAP` — implica que las compras de caja chica
    se modelan como pedidos a un "proveedor" especial por usuario.
13. **Notificaciones desacopladas**: el portal inserta filas en
    `NOTIFICACIONES` (con `MENSAJE`, `CC`, `TIPO_EVENTO`, `PROCESO_ID`) pero
    **no envía mail directamente** desde el C#. Probable job externo
    (Windows Service / SQL Agent). **[Gap]** confirmar.
14. **No se observó** en C# validación de "monto máximo por nivel de
    autorización"; presumiblemente delegada a SP o ausente.
15. **Cuenta contable segmentada en SAP**: `PEDIDOS_DET` guarda
    `SEGMENTO_1_SAP`, `SEGMENTO_2_SAP`, `SEGMENTO_3_SAP`. SAP B1 de Millet
    tiene plan de cuentas con 3 segmentos.
16. **DIOT** (Declaración Informativa de Operaciones con Terceros, fiscal
    MX): la versión V2 (`PEDIDOS_DET_V2`) añade `DIOT_CLAVE_PROV_SAP` y
    `DIOT_NOMBRE_PROVEEDOR_SAP` — la V2 separa el proveedor pagado del
    proveedor declarado en DIOT.

---

## 7.bis Volúmenes reales y features vestigiales — `[Verificado]`

Volúmenes en producción (al momento del dump):

| Tabla | Filas |
|---|---:|
| `DO_RQ` | 226,173 |
| `DO_RQ_DET` | 649,056 (≈2.9 líneas/RQ) |
| `DO_RQ_AUT` | 84,547 |
| `PEDIDOS` | 4,572 |
| `PEDIDOS_DET` | 8,114 |
| `RQ_PERIODOS` | 1,034 |
| `RQ_MOVIMIENTOS` | 8,158 |
| `DERECHOS_USUARIOS` | 2,193 |
| `NOTIFICACIONES` | 22,265 |

**Observación de ratios:**
- 226k RQs → 84k autorizaciones → 4.5k pedidos a SAP. **Solo el ~2% de
  las RQs llegaron a transmitirse a SAP como Pedido.** La mayoría se cubre
  con stock interno (movimiento o traslado de inventario), se cancela, o
  queda sin terminar.
- 226k RQs → 84k filas en `DO_RQ_AUT`. Ratio 1:0.37, pero como cada RQ
  puede tener 1 o 2 autorizaciones, el dato real es que muchas RQs nunca
  se autorizan formalmente (probablemente las que se vuelven movimiento
  de inventario directo no requieren autorización N1/N2).

**Tablas con esquema pero CERO datos en producción** (vestigiales):

| Tabla | Implicación |
|---|---|
| `DO_RQ_DET_AUT` | **Autorización por línea nunca se usó.** Solo se autoriza por cabecera. **Simplifica el diseño nuevo.** |
| `DO_RQ_LIGAS`, `DO_RQ_DET_LIGAS` | **Ligas entre documentos no se materializan.** Aunque la UI de cotización existe, el "puente" RQ↔Cotización vive en otro lado o no se persiste. |
| `PEDIDOS_V2`, `PEDIDOS_DET_V2` | **La V2 se diseñó pero nunca se implementó/usó.** El sistema sigue operando con `PEDIDOS` v1, sin DIOT separado. |

> Estas observaciones son **verificadas por la ausencia de filas en el
> dump de 1.5 GB**. Si el cliente sostiene que esas features sí se usan,
> habría que pedirle un dump más reciente o revisar si la BD activa es otra.

### 7.bis.1 Hallazgos del análisis de stored procedures (`Procedimientos_ConfigSapM.sql`, 263 SPs)

**Mayoría son CRUD trivial.** La lógica de negocio NO vive en los SPs;
vive en el code-behind C#. Los SPs son `INSERT/UPDATE` simples con poca
validación.

Hallazgos puntuales útiles:

1. **El schema dump original fue INCOMPLETO.** Los SPs `INSERTAR_DO_INVENTARIO`
   e `INSERTAR_DO_INVENTARIO_DET` referencian tablas `DO_INVENTARIO` y
   `DO_INVENTARIO_DET` que **no aparecen en `Estructura_ConfigSapM-schema-only.sql`**.
   Su descripción dice "INSERTA RESPALDO DEL ENCABEZADO/DETALLE DE
   MOVIMIENTO DE INVENTARIO" — son tablas-respaldo del movimiento de
   inventario que el portal generó. **[Acción]** Pedir al cliente un
   dump del esquema completo o aceptar que estas tablas también
   desaparecen al rediseñar.
2. **Las transiciones de estatus NO se validan en BD.**
   `ACTUALIZA_ESTATUS_DO_RQ` es un `UPDATE` puro sin guard de transición
   válida. Toda regla "no puedes pasar de 5 a 1" vive en el C#. En el
   ERP nuevo conviene modelar la state machine como invariante del
   agregado (no delegarla a la capa de aplicación).
3. **`RQ_ID` se asigna con `MAX(RQ_ID) + 1`**, no con IDENTITY ni
   SEQUENCE. Vulnerable a race condition bajo concurrencia. En el ERP
   nuevo: usar PostgreSQL `IDENTITY` o secuencias.
4. **El folio NO se autogenera en `INSERTAR_ACTUALIZAR_DO_RQ`** — llega
   como parámetro `@FOLIO`. La generación vive en SPs separados
   (`OBTENER_FOLIO_P_ORDEN`, etc.) o en C#. Esto contradice mi
   suposición previa.
5. **Convención `FOLIO_DEST = 'MOV'`**: en `DO_RQ_DET_LIGAS`, el valor
   literal `'MOV'` en `FOLIO_DEST` indica que la liga corresponde a un
   **movimiento de inventario**. Otro folio indica liga a cotización.
   Sin embargo, **estas tablas tienen 0 filas en producción**, así que
   es una convención que dejó de usarse. La lógica vigente probablemente
   se simplificó.
6. **El cálculo actual de "unidades faltantes" en `OBTENER_DO_RQ_DET_DISP`
   y `OBTENER_UNIDADES_FALTANTES` IGNORA `DO_INVENTARIO_DET`** — el
   código que lo consideraba está comentado. La versión vigente solo
   resta lo "ligado a cotización". Otra señal de que el flujo se
   simplificó respecto al diseño original.
7. **Existen `OBTENER_DO_RQ_COT`, `OBTENER_DO_RQ_COT_DET`,
   `INSERTAR_ACTUALIZAR_DO_RQ_COT_DET`** — la infraestructura de
   cotizaciones SÍ está en SPs aunque el cliente la confirmó como
   deprecada. Coherente: el código existe pero no se invoca, las tablas
   no tienen datos.
8. **SPs `SINC_*` confirman dirección de sync legacy**:
   `SINC_ARTICULOS`, `SINC_CIUDADES`, `SINC_DOCTOS_CM`, `SINC_DOCTOS_CP`,
   `SINC_ESTADOS`, `SINC_GRUPOS_LINEAS`, `SINC_LINEAS_ARTICULOS`,
   `SINC_PAISES`, `SINC_PROVEEDORES`. Confirma que en el legacy el
   portal **leía** estos catálogos desde otro sistema (SAP). En el ERP
   nuevo eso se invierte: Compras es dueño de proveedores y artículos.

> Conclusión: los SPs **no aportan reglas de negocio nuevas** — confirman
> que la lógica vivía en C# y validan que las features deprecadas (cotización,
> ligas) sí estuvieron implementadas pero ya no se usan.

---

## 8. Riesgos y gaps para la migración al nuevo ERP

| Tema | Observación | Acción sugerida en el nuevo ERP |
|---|---|---|
| **Contraseñas en texto plano** | `USUARIOS.CONTRASENA varchar(50)` — sin hash | Migrar a Entra ID (ADR 0003); jamás importar la columna |
| **Imágenes en BD** | `USUARIOS.IMAGEN image NULL` (foto del user en BD) | Pasar a almacenamiento de documentos (ADR 0024) |
| **Sin trazabilidad RQ → Pedido** | `PEDIDOS`/`PEDIDOS_V2` no tienen FK a `DO_RQ` | Modelar la relación explícita en el dominio Compras del ERP |
| **Stored procedures no entregados** | Mucho del flujo vive en T-SQL no incluido en el dump | Pedir al cliente script de SPs `MILLET_*`, `INSERTAR_ACTUALIZAR_DO_RQ*`, `OBTENER_DERECHOS` antes de cerrar diseño |
| **Sin realimentación SAP** | Portal no sabe el destino final de la RQ en SAP | Diseñar el flujo con eventos bidireccionales (Service Bus, ADR 0009) |
| **Hardcoded `USUARIO_ID` → departamento** | En `VisorOrdenCompraAutorizado.aspx.cs` | Reemplazar por RBAC granular (ADR 0007) basado en pertenencia |
| **Sin reglas de monto explícitas** | Autorización por nivel sin criterio formal en el código C# | Definir con el cliente la matriz "monto + naturaleza + sucursal → nivel(es)" |
| **Control presupuestal "casero"** | `RQ_PERIODOS` (12 columnas mes-a-mes) y `RQ_MOVIMIENTOS` | Decidir si el ERP nuevo absorbe esto en Compras o si vive en Contabilidad y Compras lo consulta |
| **Notificaciones por job externo** | Black box (no SMTP visible en C#) | Reemplazar por el módulo de notificaciones del ERP nuevo (ADR 0026) |
| **Multi-BD y SPs custom en SAP** | Acoplamiento fuerte a la BD interna de SAP | Decidir si integramos contra A+W (que ya media SAP) o si mantenemos wrappers temporales mientras dura el strangler |
| **Concurrencia con bloqueo pesimista** | Mala UX si alguien deja la sesión abierta | Sustituir por concurrencia optimista (ADR 0012) |
| **Web Forms + ADO.NET + SP** | No portable | Reescritura completa en arquitectura hexagonal + CQRS (no portar; rediseñar) |
| **Auditoría débil** | No hay tabla de auditoría dedicada; solo columnas `*_CREADOR`/`*_MODIF` por tabla | Aplicar ADR 0008 (auditoría) desde día uno |
| **Multi-empresa SAP latente** | `CLAVES_USUARIO_SAP` y `CLAVES_SUCURSAL_SAP` en plural sugieren múltiples sociedades | Confirmar con el cliente; ADR 0011 (multi-empresa) ya cubre el patrón |
| **Autorización por línea no documentada en flujo legacy** | Existe `DO_RQ_DET_AUT` pero no es obvio cómo se usa en UI | Levantar requerimiento explícito: ¿se autoriza por cabecera, por línea o ambos? |

---

## 9. Mapeo legacy → ERP nuevo (sugerencia preliminar)

| Concepto legacy | Equivalente sugerido en el ERP nuevo |
|---|---|
| BD `ConfigSapM` y tablas `DO_RQ*` | Esquema `compras` (PostgreSQL) en el monolito modular |
| `MILLET_INSERTA_RQS` y SPs `MILLET_*` que escriben en SAP | Adaptador de salida en Compras + Outbox + integración asíncrona vía Service Bus (ADR 0009) |
| Autorización por checkboxes en `.aspx` | Endpoint REST `POST /requisiciones/{id}/autorizaciones` con políticas RBAC (ADR 0007) |
| `DERECHOS_USUARIOS` (presencia = derecho) | Tabla `permisos_usuario` con misma semántica, alimentada desde Entra Groups |
| Bloqueo pesimista en `BLOQUEOS` | Concurrencia optimista por `RowVersion` (ADR 0012) |
| Notificaciones T-SQL → tabla + job externo | Outbox + módulo de Notificaciones (ADR 0026) |
| Cotizaciones como `DO_RQ` con `TIPO_DOCTO` distinto + `DO_RQ_LIGAS` | Agregado "Cotización" separado dentro del bounded context Compras, con FK explícita a la requisición origen |
| Hardcoding `USUARIO_ID → departamento` en `.aspx.cs` | Resolución de claims desde Entra ID + tabla de pertenencia |
| `RQ_PERIODOS` (presupuesto anual mensualizado) | Sub-feature de "Presupuesto" en Contabilidad o en Compras; expone API que Compras consulta antes de aprobar |
| `RQ_MOVIMIENTOS` (afectaciones contables) | Eventos de dominio "ConsumoPresupuestal" emitidos por Compras y consumidos por Contabilidad |
| `PEDIDOS`/`PEDIDOS_V2` (formato SAP intermedio) | Adaptador de salida (anti-corruption layer) entre dominio Compras y SAP/A+W; **no se replica como agregado** |
| `USUARIOS.CONTRASENA` plano | Eliminado: autenticación 100% Entra ID (ADR 0003) |
| `USUARIOS.IMAGEN` (image en BD) | Migrar a Blob Storage (ADR 0024) |
| Auditoría por columnas `*_CREADOR/*_MODIF` repetidas en cada tabla | Auditoría centralizada (ADR 0008) |

---

## 10. Preguntas abiertas para el cliente (antes de diseñar)

> Tras revisar el dump, varias preguntas iniciales ya tienen respuesta o se
> volvieron irrelevantes. Las marco como **(resuelta)** y dejo solo las
> realmente abiertas. Las nuevas que surgieron del dump las agrego al final.

### Aún abiertas (necesitan al cliente)

1. ¿Cuál es el **criterio formal para nivel 2** además de nivel 1? (monto,
   departamento, naturaleza del gasto, todas las RQs). Sigue abierta —
   crítica para arrancar diseño.
2. ¿Hay **política/manual contable** con la matriz monto → nivel(es)
   requerido(s)? Si existe, evita re-inventar reglas.
3. ¿Se acepta abandonar **bloqueo pesimista** a favor de concurrencia
   optimista en el ERP nuevo?
4. ¿El flujo permitirá **rechazo con motivo estructurado** (catálogo de
   motivos) o solo notas libres como hoy?
5. **RQs en vuelo durante el corte** legacy → nuevo: ¿migrar o cerrar y
   rehacer?
6. **Multi-empresa SAP / multi-sociedad**: `CLAVES_USUARIO_SAP` plural
   sugiere más de una sociedad. ¿Cuántas y cuáles? Importa para el modelo
   de datos.
7. **Saldo parcial / completo** (estatus 7 ASP / 8 ASC): el cliente
   confirmó que existen porque el material puede llegar parcial — ¿qué
   evento concreto dispara la re-autorización? ¿se dispara automático al
   recibir parte del material o lo dispara un usuario?
8. **9 sucursales** del legacy: ¿cuáles son físicas y cuáles
   agrupaciones lógicas/organizacionales (ej. "Consejo de administración",
   "Pintura")? Para diseñar el catálogo de sucursales del ERP.
9. **Catálogos de cuentas y centros de costo**: dado que el control
   presupuestal queda fuera del scope, ¿siguen siendo necesarios a nivel
   de línea de RQ (`CUENTA_ID`, `CLAVE_CENTRO_COSTO`)? ¿O solo informativos
   para Contabilidad?
10. **Almacenes**: 21 almacenes en el legacy (`CA_ALMACENES`). ¿Todos
    activos? ¿Hay reglas de qué almacén consume cada departamento?

### Nuevas (surgidas tras conocer la lógica real)

11. **Reserva de stock**: cuando una RQ se autoriza y hay stock disponible,
    ¿el sistema **reserva** el stock al momento de autorizar (lo aparta) o
    espera hasta el surtido físico? Tiene impacto en la concurrencia.
12. **Generación automática de OC**: cuando una RQ tiene saldo a comprar,
    ¿la OC se genera automáticamente al autorizar o requiere una acción
    manual del comprador?
13. **¿Qué disparador convierte una RQ en movimiento de almacén
    inmediato?** ¿La autorización N1 basta? ¿Requiere N2? ¿Depende del
    monto/almacén?

### (resuelta — sesión cliente 2026-05-07)

- **#1 destino SAP/ERP** → ERP propio. SAP no recibe nada.
- **#3/#4 dueño de catálogos** → ERP nuevo, exportación inicial desde SAP.
- **#6 cotizaciones** → DEPRECADA. Fuera de scope.
- **#9 control presupuestal** → DEPRECADO. Fuera de scope.
- **#11 caja chica** → caja 1 deprecada, caja 2 activa como módulo
  separado del ERP, no parte de Requisiciones.
- **#12 Microsip** → DEPRECADO.
- **#13 movimientos de inventario desde RQ** → Aclarado: **es la lógica
  core**, no un caso especial. Si hay stock se cubre con almacén; si no,
  se compra.

### (resuelta — análisis del dump)

- **Estatus 10** = SURTIDO PARCIAL (no "En Proceso").
- **Lista de `TIPO_DOCTO`** → en práctica solo `'R'`.
- **Autorización por línea** (`DO_RQ_DET_AUT`) → vestigial, no se diseña.
- **Importaciones / `CONTENEDOR`** → no se usa.
- **DIOT separado** (`PEDIDOS_V2`) → vestigial.

### (resuelta) Estatus `10`

Era inferido como "En Proceso". **Real:** "SURTIDO PARCIAL". El catálogo
completo está en `Datos_Muestreados.sql` y reflejado en sección 3.

### (resuelta) Lista de valores de `TIPO_DOCTO`

En las muestras solo se observa `'R'`. Necesitamos confirmar con el cliente
si existen otros valores y para qué — pero **dado que `DO_RQ_LIGAS` está
vacío, parece que en la práctica se usa solo `'R'`** y todo lo demás se
maneja en otra tabla o módulo.

### (resuelta) Autorización por línea (`DO_RQ_DET_AUT`)

**Vestigial. La tabla está vacía en producción.** Solo se usa autorización
por cabecera. Decisión clara para el ERP nuevo: **no implementar autorización
por línea**.

### (resuelta) Importaciones (`CONTENEDOR`)

En las muestras `CONTENEDOR` siempre es NULL. **No se usa.** No incluir en
el agregado del ERP nuevo, salvo que el cliente lo confirme.

### (resuelta) DIOT (`PEDIDOS_DET_V2.DIOT_*`)

**Vestigial. `PEDIDOS_V2` tiene 0 filas.** El sistema en producción usa
`PEDIDOS` v1 sin distinción DIOT. Si el ERP nuevo necesita DIOT (probable
por requisito fiscal MX), se diseña fresco; no hay precedente activo en
el legacy.

---

## 11. Inventario rápido de archivos fuente más relevantes

Para la siguiente Claude que reciba este documento — si necesita verificar
algún detalle, los archivos de mayor densidad de información son:

```
Estructura_ConfigSapM-schema-only.sql           ← esquema completo BD portal (verificado)
                                                  40 tablas, FKs, defaults; SIN stored procs
Datos_Muestreados.sql                           ← datos verificados (catalogos completos +
                                                  muestras de 30-300 filas de tablas grandes,
                                                  sin USUARIOS por privacidad)

AccesoMil-sap\AccesoMil\Requisiciones.cs        ← núcleo de RQ
AccesoMil-sap\AccesoMil\AutorizarOrdenCompra.cs ← niveles de autorización
AccesoMil-sap\AccesoMil\Cotizaciones.cs         ← cotizaciones y ligas
AccesoMil-sap\AccesoMil\Notificaciones.cs       ← cola de avisos
AccesoMil-sap\AccesoMil\Bloqueos.cs             ← concurrencia pesimista
AccesoMil-sap\AccesoMil\Pedidos.cs              ← OC tradicional
AccesoMil-sap\AccesoMil\PedidosV2.cs            ← OC versión nueva
AccesoMil-sap\AccesoMil\Presupuestos.cs         ← (verificar) presupuesto/RQ_PERIODOS
AccesoSap-BD_Millet_NEW\AccesoSap\SolicitudCompra.cs ← punto de cruce a SAP
AccesoSap-BD_Millet_NEW\AccesoSap\Pedidos.cs    ← OC en SAP
MilletSap\Millet\RQ.aspx.cs                     ← UI de RQ
MilletSap\Millet\RQS.aspx.cs                    ← UI variante
MilletSap\Millet\AutorizarOrdenCompra.aspx.cs   ← UI de autorización
MilletSap\Millet\VisorOrdenCompraAutorizado.aspx.cs ← visor (ojo: hardcoding)
MilletSap\Millet\DerechosUsuarios.aspx.cs       ← admin de permisos
MilletSap\Millet\Web.config                     ← connection strings y settings
```

Path raíz: `C:\Users\UserSP\Desktop\PORTALSAP 2 C_C new imp.excel_20250731`.

## 12. Cambios respecto a versiones previas del documento

### Rev. 4 — tras revisar `Procedimientos_ConfigSapM.sql` (263 SPs)

- **Confirmado**: la lógica de negocio vive en el code-behind C#, no en
  SPs. Los SPs son CRUD trivial — folio recibido como parámetro,
  estatus actualizado sin guardas, RQ_ID con `MAX+1`.
- **Hallazgo nuevo**: el schema dump original estaba **incompleto** —
  faltan al menos `DO_INVENTARIO` y `DO_INVENTARIO_DET` (referenciadas
  en SPs). Si el cliente quiere completar el levantamiento, pedir un
  schema fresh.
- **Convención `FOLIO_DEST='MOV'`** en ligas indica movimiento de
  inventario; cualquier otro folio = cotización. Pero como las ligas
  tienen 0 filas, esta convención dejó de usarse.
- **El cálculo de "faltantes" ignora `DO_INVENTARIO_DET`** en la versión
  vigente del SP — el código que lo consideraba está comentado. El
  sistema actual es más simple que el diseñado.
- **SPs `SINC_*`** confirman que en el legacy el portal lee proveedores,
  artículos, ciudades, países y otros catálogos desde un sistema externo
  (probablemente SAP). En el ERP nuevo esa dirección se invierte.
- **Conclusión**: los SPs validan el levantamiento; no añaden reglas de
  negocio nuevas. **No bloquean el avance al diseño.**

### Rev. 3 — tras sesión con el cliente (2026-05-07)

- **Lógica core aclarada**: el módulo es **stock-aware con bifurcación**.
  Si hay stock → movimiento de almacén; si no → solicitud de compra; si
  parcial → mixto. Documentado en sección **0.bis.1**.
- **Features deprecadas confirmadas por el cliente** (no se replican):
  Cotizaciones, Caja chica 1, Microsip, Control presupuestal completo
  (incluye `RQ_PERIODOS` y `RQ_MOVIMIENTOS`).
- **Caja chica 2** queda como **módulo separado** del ERP nuevo, no como
  parte del submódulo Requisiciones. Su flujo: vendedores/choferes
  registran gastos a posteriori, modelados "como compra" para control.
- **Catálogo de proveedores y artículos**: cambia de propietario. Antes
  vivían en SAP; ahora **el ERP nuevo es dueño**, con exportación inicial
  desde SAP.
- **Ratio 226k RQs → 4.5k OCs (50:1) explicado**: la mayoría de RQs se
  cubre con stock interno y nunca llega a SAP. No es síntoma de
  abandono, es la lógica de negocio normal.
- **Acoplamiento Compras ↔ Almacén**: el submódulo Requisiciones
  necesita un contrato claro con el módulo Almacén de no-producción para
  consultar disponibilidad y reservar/consumir stock. Hay que diseñarlo.
- **Ya no hay sub-feature de presupuesto en Compras**. Esto simplifica
  significativamente el diseño.
- Sección 10 (Preguntas) reducida de 15 a **10 abiertas + 3 nuevas**
  surgidas de la lógica stock-aware (reserva de stock, generación
  automática de OC, disparador de movimiento de almacén).

### Rev. 2 — tras revisar `Datos_Muestreados.sql` (catálogos + muestras)

- **Estatus**: el catálogo `CA_ESTATUS` real tiene 22 entradas. Estatus 10
  es "SURTIDO PARCIAL", no "En Proceso". No existe estatus formal de
  "Rechazado" — se usa `ELIMINADO (3)` como soft delete.
- **Folio verificado**: formato `C########` (8 dígitos), no depende de
  `TIPO_DOCTO`.
- **Permisos verificados**: 91 derechos en árbol `P/H/HH`. 13 módulos raíz.
  El módulo Requisiciones tiene 26 acciones granulares.
- **Existen 3 módulos separados** (DERECHO_ID 3, 4, 5): Requisiciones,
  Solicitudes de cotización, Control presupuestal.
- **Una RQ puede convertirse en movimiento o traslado de inventario**, no
  solo en OC (derechos 311–313, 318–320). Hallazgo nuevo importante.
- **El creador puede ser distinto al requisitante** (derecho 308
  "Seleccionar requisitante" = delegación).
- **Saldo parcial/completo (ASP/ASC)**: estatus 7/8 modelan re-autorización
  de saldos no surtidos.
- **Tablas vestigiales en producción**:
  - `DO_RQ_DET_AUT` (0 filas) — autorización por línea no se usa.
  - `DO_RQ_LIGAS` y `DO_RQ_DET_LIGAS` (0 filas) — ligas no se materializan.
  - `PEDIDOS_V2`/`PEDIDOS_DET_V2` (0 filas) — V2 nunca se implementó.
- **Multi-moneda y CONTENEDOR**: columnas existen pero **no se usan en
  producción** (siempre NULL en las 50 muestras).
- **Solo 1 fondo activo** (`CA_FONDOS` tiene 1 sola fila: "Ingresos a la
  operación"). El soporte multi-fondo está en el esquema pero no se usa.
- **Modelo presupuestal verificado**: `RQ_PERIODOS` distribuye montos
  anuales en 12 columnas mensuales (típicamente uniforme = anual/12).
  `RQ_MOVIMIENTOS` con tipos `'A'` (alta) y `'T'` (traspaso simétrico).
- **Volumen real**: 226k RQs históricas → solo 4.5k pedidos a SAP (ratio
  50:1). La mayoría de RQs no llegan a SAP — se cubren con stock,
  movimientos internos o se cancelan.
- **9 sucursales heterogéneas**: mezclan ubicación física con áreas de
  negocio y niveles organizacionales. Necesita normalización en el
  ERP nuevo.

### Rev. 1 — tras revisar `Estructura_ConfigSapM-schema-only.sql` (esquema)

Tras revisar el esquema verificado de `ConfigSapM`, estos puntos cambiaron
de "inferido" a "verificado" — o se corrigieron porque la inferencia era
incorrecta:

- **Corregido**: la tabla puente se llama `DERECHOS_USUARIOS` (plural) y
  **no tiene flag `PERMITIDO`**: presencia = derecho.
- **Corregido**: **no existen** tablas `DO_RQ_COT*` para cotizaciones; las
  cotizaciones son `DO_RQ` con `TIPO_DOCTO` distinto, ligadas vía
  `DO_RQ_LIGAS` y `DO_RQ_DET_LIGAS`.
- **Nuevo (no estaba)**: control presupuestal en `RQ_PERIODOS` y
  `RQ_MOVIMIENTOS`. Tiene implicación de diseño grande.
- **Nuevo (no estaba)**: existe **autorización a nivel línea**
  (`DO_RQ_DET_AUT`).
- **Nuevo (no estaba)**: **`PEDIDOS` y `PEDIDOS_V2` no tienen FK a `DO_RQ`**.
  Sin trazabilidad estructural.
- **Nuevo (no estaba)**: contraseñas en texto plano y fotos de usuario
  embebidas en BD — son blockers para la migración tal cual.
- **Nuevo (no estaba)**: integración con **A+W** confirmada vía
  `USUARIO_ALFAK` y conexión `MILMAIN`; integración con **Microsip** vía
  `USUARIO_MICROSIP` (pendiente confirmar si activa).
- **Nuevo (no estaba)**: **DIOT** modelado en `PEDIDOS_DET_V2` con
  proveedor declarado distinto al pagado.
- **Confirmado** todo lo dicho originalmente sobre `BLOQUEOS`, `DO_RQ_AUT`
  con `NIVEL`, multi-moneda en cabecera, `CONTENEDOR`, `CLAVE_*_SAP`.

Lo que sigue **siendo gap** y necesita un script de SPs del cliente o
entrevista:

- Significado real del estatus `10`.
- Reglas de monto para nivel 1 vs nivel 2.
- Reglas exactas de control presupuestal.
- Lista de valores válidos para `TIPO_DOCTO`.
- Si el envío de email se hace desde un job externo (y cuál).
- Si la autorización por línea (`DO_RQ_DET_AUT`) se usa en producción o es
  vestigial.
