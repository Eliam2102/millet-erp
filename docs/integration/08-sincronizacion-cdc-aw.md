# 08 — Sincronización continua A+W → ERP por CDC (O1A-AW-INT)

> **Estado:** construido y probado contra el sandbox local (SQL Server 2022 Developer). **Apagado por defecto.**
> **No está habilitado en el A+W real**: ver §7 (viabilidad). Complementa — no reemplaza — el barrido completo de [05](05-sincronizacion-clientes-aw.md) (clientes) y [06](06-sincronizacion-productos-aw.md) (productos).
> Sandbox y fixtures: [07](07-sandbox-aw.md). "Sandbox ≠ producción": nada de esto valida la Hybrid Connection ni la edición real de SQL Server.

## 1. Problema que resuelve

Los docs 05 §8 y 06 §12 dejaron la detección de cambios como barrido completo + hash, porque A+W no ofrece una fecha de cambio confiable: `TRANSACTION_TIME` viene NULL en prácticamente todo el real (25/25 en la muestra de clientes, 5 filas en `KU_KUNDEN`), y un barrido completo (~44,7 mil clientes, ~6,8 mil productos) no puede correr cada pocos minutos. Además un **borrado físico** en A+W es indetectable con barrido.

**CDC (Change Data Capture)** de SQL Server registra cada INSERT/UPDATE/DELETE desde el log de transacciones, sin depender de ninguna columna de fecha, y sin que A+W cambie su código.

## 2. Diseño

```
A+W (SQL Server) ──CDC──▶ cdc.fn_cdc_get_all_changes_*  ──▶ AwCambiosCdcOrigen (solo SELECT)
                                                                   │  "qué referencias cambiaron"
                                                                   ▼
          AwCambiosSyncWorker (cada N s) ──▶ AwCambiosAplicador ──▶ sincronizador existente
                                                  │                  (relee la referencia en A+W y aplica;
                                                  ▼                   nunca aplica el contenido de CDC)
                                         aw_cdc_watermark (LSN)
```

| Pieza | Archivo | Responsabilidad |
|---|---|---|
| Puerto | `Application/Cambios/IAwCambiosOrigen.cs` | `ObtenerLsnActualAsync`, `LeerCambiosAsync(entidad, desdeLsn, tamaño)` |
| Lector | `Infrastructure/Cambios/AwCambiosCdcOrigen.cs` | Lee el último cambio por llave en `(desde, hasta]`; lote con `TOP … WITH TIES` (no parte un LSN); detecta LSN expirado |
| Aplicador | `Application/Cambios/AwCambiosAplicador.cs` | Un ciclo por entidad: leer cambios → aplicar → avanzar watermark |
| Worker | `Application/Workers/AwCambiosSyncWorker.cs` | Ciclo cada `IntervaloSegundos`; errores aislados por entidad; health check de liveness |
| Estado | `Domain/AwCdcWatermark.cs`, tabla `integraciones_aw.aw_cdc_watermark` | Último LSN procesado por entidad (migración `AwCdcWatermark`) |
| Wiring | `Infrastructure/Cambios/DependencyInjection.cs` | `AddIntegracionesAwCambios`; no registra nada si `Habilitado=false` |

**Qué tabla mueve qué:** clientes = `KU_KUNDEN` (llave `ID`). Productos = `BA_PRODUKTE` (llave `BA_PRODUKT`) + `BA_STUKL` (composición, llave `PRODUKT`) + `BA_PRODUKTE_BEZ` (descripción por idioma, llave `BA_PRODUKT`): un cambio en la composición o en la descripción cuenta como cambio del producto.

### Reglas (todas probadas, §5)

1. **CDC dice QUÉ cambió, no CUÁL es el valor.** Cada referencia se relee con `LeerPorReferenciaAsync` y pasa por el mismo mapeo/validación/hash que el barrido. Idempotente.
2. **El watermark solo avanza tras aplicar el lote completo.** Si algo falla, el siguiente ciclo relee desde el mismo LSN.
3. **`desdeLsn` es "ya procesado" (exclusivo).** Evita releer la última transacción.
4. **Sin watermark → barrido completo inicial.** El LSN se toma **antes** del barrido; lo que cambie durante él llega por CDC después.
5. **LSN expirado** (limpieza de CDC, por defecto 3 días de retención) → `AwReaderException(kind: "cdc_lsn_expirado")` → barrido completo de recuperación y watermark nuevo.
6. **Borrado físico = informativo.** Se registra en el log (`Eliminado`); **no** da de baja (ausencia ≠ baja, doc 05 §8). La política de baja la define Millet.
7. **Fallo del origen no pierde cambios.** Un fallo transitorio propaga la excepción, el watermark no avanza y el worker reintenta. Un cliente que no se pudo leer se convierte en excepción (el sincronizador de clientes lo registra como ejecución `Fallida` sin lanzar).
8. **El barrido completo sigue siendo la conciliación** (nocturna/manual): cubre lo que CDC no ve (p. ej. cambios en `KA_ZAHLBED`, que afectan la condición de pago del cliente).

## 3. Configuración

Sección `IntegracionesAw:Cambios` (todo apagado por defecto) y `ConnectionStrings:AwCambiosDb`:

| Clave | Default | Nota |
|---|---|---|
| `Habilitado` | `false` | Sin esto no se registra el origen ni el worker |
| `Clientes` / `Productos` | `true` | Qué entidades procesa el worker |
| `IntervaloSegundos` | `30` | Mínimo efectivo 5 |
| `TamanoLote` | `500` | Cambios por lectura (se acota a 1–5000) |
| `SqlQueryTimeoutSeconds` / `SqlConnectTimeoutSeconds` | `15` / `15` | |
| `ConnectionStrings:AwCambiosDb` | — | **TLS verificado obligatorio** (`Encrypt=True`, sin `TrustServerCertificate`); en Azure, desde Key Vault. Usuario de **solo lectura** con `SELECT`/`EXECUTE` sobre el esquema `cdc` |

Además, la lectura de cada sincronizador debe estar habilitada (`IntegracionesAw:Clientes:LecturaHabilitada/AplicacionHabilitada`, `IntegracionesAw:Productos:OrigenHabilitado`) con su origen en `Sql`.

## 4. Sandbox con CDC

- `tools/aw-sandbox/docker-compose.yml`: `MSSQL_AGENT_ENABLED=true` (CDC necesita SQL Server Agent para los jobs de captura/limpieza).
- `tools/aw-sandbox/schema/05-cdc.sql`: habilita CDC en `AW_SANDBOX` sobre las 4 tablas (idempotente, **después** del seed) y da a `aw_ro` `SELECT/EXECUTE/VIEW DEFINITION` sobre `cdc`. Lo aplica `aw-sandbox.sh up`.
- `tools/aw-sandbox/cdc/01-habilitar.sql`, `02-prueba.sql`: prueba manual sobre la copia completa `AW_FULL` (UPDATE y DELETE capturados con valor antes/después).
- **Aviso:** el sandbox es SQL Server 2022 **Developer**, que trae todas las funciones de Enterprise. Que funcione ahí **no prueba** que funcione en el Standard RTM de producción (§7).

## 5. Pruebas y resultados (2026-10-05)

Todas opt-in (`Category=AwSandbox`, variable `AW_SANDBOX_CONN`), con PostgreSQL desechable (`tools/validate-integration-isolated.sh`), nunca `millet_dev`. Resultado (sandbox, 2026-10-05):

| Prueba | Resultado |
|---|---|
| `AwCambiosCdcSandboxTests` (lector: cambios, borrados, paginación, LSN) | Pasa |
| `AwCambiosAplicadorSandboxTests` (aplicador de punta a punta) | Pasa |
| Escenarios de fallo: LSN expirado y caída del servidor a mitad de ciclo | Pasan |

### 5.1 Casos de uso de la tarea O1A-AW-INT cubiertos por CDC

| Caso | Dónde | Resultado |
|---|---|---|
| Consulta / recepción (paginada) | `AwCambiosCdcSandboxTests.Pagina_sin_perder_cambios…` | Paginar de 1 en 1 = de 100 en 100, sin perder ni repetir |
| Actualización | `…Detecta_actualizaciones_y_borrados_fisicos…`, `…Un_cambio_en_A_W_llega_al_ERP…` | Cliente y producto actualizados llegan al ERP sin barrido |
| Borrado físico | ídem | `Eliminado` informado; el ERP **no** da de baja |
| Duplicado / repetición | `…Un_cambio_en_A_W_llega_al_ERP…` | El ciclo siguiente no ve nada (idempotente) |
| Dato inválido | Heredado del sincronizador (suites 05/06) | El mapeo/validación es el mismo; CDC solo entrega la referencia |
| Fallo | `…Fallo_de_A_W_a_mitad…`, `…El_worker_en_el_host…` | Servidor caído: excepción transitoria, watermark intacto; el worker sigue vivo |
| Reintento | ídem | Al volver el servidor cada cambio se aplica exactamente una vez |
| LSN expirado | `…LSN_expirado_dispara_barrido…`, `…Pagina_sin_perder…` | Barrido de recuperación y watermark nuevo |
| Conciliación origen/destino | Suites 05/06 (`AwConciliacion`) | Sin cambios (el barrido sigue siendo la conciliación) |

### 5.2 Defectos que las pruebas encontraron y se corrigieron

1. Cursor **inclusivo**: releía la última transacción (un `INSERT` masivo comparte LSN) → 53 productos "cambiados" en vez de 2. Ahora `desdeLsn` es exclusivo.
2. Paginación con `WITH TIES`: un grupo de LSN mayor que el lote no avanzaba el cursor (bucle). Resuelto con el mismo cambio.
3. Paginación con varias fuentes (producto = 3 tablas): un lote lleno de una podía hacer saltar cambios de otra. Ahora se corta en el LSN de la fuente llena más atrasada.
4. `BA_PRODUKTE_BEZ` no estaba en CDC: un cambio solo de descripción pasaba inadvertido.

### 5.3 Lo que NO está cubierto

- **Expiración por retención real** (se simuló con un LSN anterior al mínimo; no se esperó la limpieza de CDC).
- **El barrido de clientes que el aplicador encola** lo ejecuta el dispatcher existente; en los tests no corre (el de productos sí es directo).
- **Volumen y tasa real de cambios**: el sandbox tiene 150 clientes / 160 productos (la copia `AW_FULL` tiene 44 695 / 6 802 pero no se midió CDC bajo carga). La tasa real en producción no se pudo medir (§7).
- **Hybrid Connection y TLS verificado**, latencia real.
- **Otros mapeos**: clientes con estado/moneda/condición sin mapeo aprobado quedan `Pendiente` (doc 07, hallazgo 1); CDC no lo cambia.

## 6. Cómo activarlo (cuando proceda)

1. Habilitar CDC en A+W (§7) y dar al usuario de lectura acceso a `cdc`.
2. `IntegracionesAw:Cambios:Habilitado=true` y `ConnectionStrings:AwCambiosDb` (Key Vault, TLS verificado).
3. Verificar la lectura de clientes/productos (`Origen=Sql`) y observar los logs `CDC {Entidad}: N cambios aplicados…` y el health check `aw-cambios-sync-worker`.
4. Mantener el barrido completo nocturno como conciliación.

## 7. Viabilidad de CDC en la base de producción

Mediciones de solo lectura contra el A+W real el 2026-10-05 (evidencia: `.local-context/evidencias/o1a-aw-sync/viabilidad-cdc-prod-2026-10-05.txt`).

| Dato | Real | Efecto en CDC |
|---|---|---|
| Servidor | `SER-DATA\AWBUSINESS`, **SQL Server 2016 RTM (13.0.1601.5), Standard** | 🔴 **Bloqueante.** CDC en edición Standard solo existe desde **2016 SP1**. En RTM Standard no se puede habilitar. Hay que parchear a SP3 (13.0.6300) o superior |
| BD | `MILMAIN`, compat 120, recovery FULL, sin replicación, sin CDC ni Change Tracking en el servidor | ✅ CDC funciona en compat 120 y en FULL. Nada que choque |
| Triggers en las tablas | 0 | ✅ |
| PK en las 4 tablas | sí | ✅ (también permitiría `net_changes`) |
| Tamaño | 44 701 / 6 802 / 9 071 / 10 548 filas; log de 100 MB | ✅ Tablas chicas. ⚠️ El log (100 MB, crecimiento 10 %) retiene lo no capturado: con el job de captura caído el log crece |
| Ancho de tabla | `KU_KUNDEN` 203 columnas (3 LOB), `BA_PRODUKTE` 228 (1 LOB) | ⚠️ Capturar **todas** las columnas duplica cada fila modificada. Conviene `@captured_column_list` con solo las columnas que consume el lector (o solo las llaves, que es lo único que usa este diseño) |
| SQL Server Agent | **No verificable** con `aw_ro` (sin acceso a `msdb`/DMV) | 🟡 CDC lo exige para captura y limpieza. Confirmar con el DBA |
| Permisos para habilitar | `aw_ro` no es sysadmin ni db_owner | Habilitar CDC exige **sysadmin** (a nivel BD) y db_owner (a nivel tabla); lo debe hacer el DBA/vendor, no el ERP |
| Tasa real de cambios | **No medible** sin VIEW SERVER STATE | 🟡 Estimar sobre el A+W real tras habilitar en una réplica |

### 7.1 Qué cambia CDC en la BD de A+W (para que A+W/Millet decidan)

- Crea el esquema `cdc` y tablas `cdc.*_CT` (una por tabla capturada), funciones `fn_cdc_*`, el usuario `cdc` y **dos jobs de SQL Server Agent** (captura y limpieza, 3 días de retención por defecto).
- **No modifica las tablas de A+W ni agrega triggers**, pero sí **objetos nuevos en `MILMAIN`** (la BD del proveedor) y **retención de log** hasta que la captura lo lee.
- Efectos en operación: respaldos/restauraciones de `MILMAIN` incluyen los objetos `cdc` (un *restore* a otro servidor requiere `KEEP_CDC`); el tamaño de la BD y del log crece con la tasa de cambios.
- **Soporte del proveedor:** modificar `MILMAIN` puede estar fuera de lo que A+W (el vendor) soporta. Confirmar con ellos antes de habilitar.

### 7.2 Veredicto

| Escenario | Viable | Condición |
|---|---|---|
| CDC en `MILMAIN` tal como está (2016 RTM Standard) | ❌ **No** | Requiere parchear a 2016 **SP1 o superior** (recomendado SP3) — cambio del servidor completo, no solo del ERP |
| CDC en `MILMAIN` tras parchear a SP1+ | ✅ Sí, con condiciones | Agent corriendo, aprobación de Jorge/vendor, DBA con sysadmin, captura solo de columnas necesarias, monitoreo del log, usuario de lectura con acceso a `cdc` |
| CDC en una **réplica/restore** de `MILMAIN` (otro servidor con edición compatible) | ✅ Sí | Evita tocar la BD de producción; añade latencia del refresco de la réplica y un servidor que mantener |
| **Change Tracking** en lugar de CDC | ✅ Alternativa recomendada para evaluar | Disponible en **todas** las ediciones y en 2016 RTM, sin Agent ni retención de log. Entrega justo lo que este diseño usa (llave cambiada + operación INSERT/UPDATE/DELETE) vía `CHANGETABLE(CHANGES …)`. También modifica `MILMAIN` (habilitar a nivel BD y tabla) y requiere permiso `VIEW CHANGE TRACKING` para el lector. **No implementado ni probado aquí**: solo cambiaría `AwCambiosCdcOrigen` por un lector equivalente; aplicador, worker y watermark se reutilizan |
| Sin tocar A+W | ✅ | Barrido completo periódico (lo que existe hoy) y/o ejecución durable de productos |

**Recomendación:** no habilitar CDC en producción mientras el servidor sea 2016 RTM. Decidir entre (a) parchear a SP3 y habilitar CDC acotado a las columnas necesarias, o (b) evaluar Change Tracking, que no exige parche, Agent ni retención de log. En ambos casos requiere visto bueno de Jorge/vendor y a un DBA con sysadmin. Mientras tanto el barrido completo sigue siendo la vía válida.

### 7.3 Pendientes para cerrar la decisión

| # | Qué | Quién |
|---|---|---|
| 1 | ¿Acepta A+W/vendor objetos nuevos (`cdc` o Change Tracking) en `MILMAIN`? | Jorge / vendor |
| 2 | ¿Se puede parchear 2016 RTM → SP3? Fecha y ventana | Infra / Millet |
| 3 | ¿SQL Server Agent corre en `SER-DATA\AWBUSINESS`? | DBA |
| 4 | Tasa real de cambios por tabla (para dimensionar log y retención) | DBA, en réplica |
| 5 | Probar el lector sobre una réplica real (edición/versión de producción) | DBA + ERP |
| 6 | Decidir CDC vs Change Tracking (y acotar columnas) | Eliam / Jorge |
