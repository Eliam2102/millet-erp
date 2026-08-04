# Runbook — Customizing A+W para flow per-EDI

> **Audiencia:** equipo A+W de Millet (operador con permiso de edición de
> customizings) + DevOps Tiglass.
> **Versión:** 5.0.0 (marcador por-EDI — el nombre del archivo es el pedido)
> **Última actualización:** 2026-07-19
> **PRs relacionados:**
> - [drop service — detección de marcador](../../../on-prem/aw-drop-service/Program.cs)

---

## 0. v5 — Correlación por MARCADOR, no por log (leer primero)

**Cambio (2026-07):** con el customizing único procesando todos los EDI en
una pasada, el `last_batch.log` compartido juntaba N documentos y ya no se
podía atribuir de forma confiable qué `[Documento]=N` correspondía a cada
EDI. **Se retira todo el parseo de log.** En su lugar, el customizing escribe
un **marcador vacío por-EDI** cuyo nombre carga el pedido.

### Contrato (esto es lo que el customizing debe hacer)

Después de crear el pedido, escribir un archivo **VACÍO** (0 bytes):

```
Carpeta:  E:\XML\Work\Results\
Nombre:   cot_<REF>.<AWDOCID>
Ejemplo:  E:\XML\Work\Results\cot_Q-2026-00451.10432218
```

- `<REF>` = el mismo del `.edi` (`cot_<REF>.edi`). `<AWDOCID>` = `auftragsnummer`
  (numérico) del pedido recién creado.
- **Sin contenido** — toda la info está en el nombre. El `<REF>` no lleva
  puntos, así que el único `.` separa el REF del doc id.
- **Overwrite** si existe (`udf_FileDelete` + recrear).
- **Solo en éxito.** Si A+W **rechaza** el EDI (no crea pedido), **no escribe
  nada** → el drop service cae en timeout (`stuck`) y el ERP lo marca
  `FailedDrop` reintentable / correlación fallida. (Idea 4: sin señal de
  rechazo; el motivo se ve en A+W, no aquí.)
- Requiere procesar **por archivo** (loop) para conocer `<REF>`↔`<AWDOCID>`
  por documento.

### Cómo lo consume el drop service

- `POST /drop-edi`: escribe `cot_<REF>.edi` en `Work\`, luego poll de
  `Results\` buscando `cot_<REF>.*`. Aparece → `outcome=success` +
  `aw_doc_id` del nombre. Timeout → `stuck`.
- El ERP, tras grabar el `aw_doc_id`, llama `POST /results/{name}/archive`
  que **mueve** el marcador a `Results\archive\`. Estado "procesado" =
  ubicación: `Results\` = pendiente, `Results\archive\` = procesado.
- `GET /completions` enumera los marcadores de `Results\` por cursor (mtime)
  para la reconciliación tardía (EDIs que A+W procesó tras el timeout sync o
  cuya respuesta síncrona se perdió).

### Config del drop service

`DropService:AwImportFolder` (WorkDir) + `DropService:ResultsFolder` (default
`{AwImportFolder}\Results`) + `WaitTimeoutSeconds` + `PollIntervalMs`. Se fue
`LogsFolder`. Ver `appsettings.example.json`.

### Pre-requisito en SER-DATA

```powershell
New-Item -ItemType Directory -Path "E:\XML\Work\Results" -Force
```
Permisos: escritura del usuario A+W (escribe el marcador) + lectura/creación
de `archive\` del usuario `MilletAwDropService`.

> ⚠️ **Sigue pendiente confirmar con A+W** que el EDI sobrescribe el depto y
> quién lo pobla (el Glass Agent al generar el EDI; el ERP no genera EDI).

**Las secciones §1–§8 de abajo describen el modelo por LOG (v2–v4), que este
cambio deja atrás.** Se conservan por referencia histórica y rollback.

---

## 1. Propósito

A partir de PR #199, el **drop service** (`MilletAwDropService` en SER-DATA)
opera en modo **per-EDI bloqueante**: cuando el ERP envía un EDI, el
servicio escribe el archivo en `E:\XML\Work\` y **espera** a que A+W
termine de procesarlo antes de responder al ERP con el outcome final
(`success` / `failed` / `stuck`).

Para que el drop service sepa cuándo A+W terminó y cuál fue el outcome,
el customizing existente de import debe escribir un **archivo de log**
en `E:\XML\Work\Logs\last_batch.log` al finalizar cada ciclo. El drop
service vigila ese archivo, lo parsea buscando el filename del EDI que
acaba de escribir, y extrae el `[Documento]=N` del código `(4615)`.

Sin este cambio, **todos los drops terminarán en `outcome=stuck`** tras
el timeout de 120s — el ERP los marcará como `FailedDrop` con
`kind=aw_processing_timeout`. La integración no funcionará en producción.

### Por qué un único `last_batch.log` que se sobrescribe (vs filename con timestamp)

La versión 1.0 del runbook proponía `last_batch_{success|fail}_{timestamp}.log`.
Validación E2E en mayo 2026 detectó dos problemas:

1. **Acumulación de logs**: A+W escribe un log nuevo cada ciclo (~60s)
   independientemente de si procesó archivos. Resultado: 1440 logs/día
   de basura llenando `Logs\`.
2. **Sufijo `success`/`fail` engañoso**: `udf_EDIImportFromDirectory`
   retorna `TRUE` aunque no haya archivos que procesar (= "nada que
   hacer = OK"). Todos los logs terminan llamándose `_success_*` aunque
   no contengan nada relevante.

La versión 2.0 (este runbook) usa **`last_batch.log` estático que se
sobrescribe cada ciclo**. El drop service:

- Identifica nuestro outcome **por el contenido** del log (busca el
  filename del EDI que escribimos en líneas `(4602)` y extrae el doc id
  del `(4615)` correspondiente). El filename del log no aporta info.
- **Copia** el log a `Logs\Processed\{ediFilename}_{utcTimestamp}.log`
  cuando detecta el outcome de nuestra cotización → preserva audit
  trail por cotización (operador busca con `ls Processed | grep Q-2026-XXX`).
- Logs de ciclos sin nuestro file (vacíos o retries de otros) son
  ignorados por el parser y sobrescritos limpio por A+W en el siguiente
  ciclo.

---

## 1.bis Topología v3 — customizings independientes por sucursal (lanes)

### Motivación

La v2 operaba con **un solo customizing** que encadenaba las importaciones
de todas las sucursales una tras otra y generaba **un único log**. En
operación (jun–jul 2026) esto mostró dos fallas:

1. **Encadenamiento frágil**: el primer ciclo de import funcionaba pero
   los ciclos 2, 3 y 4 a veces no se ejecutaban. El workaround (borrado
   forzado de tablas de importación) chocaba con los procesos de import
   **manuales** de la empresa cuando coincidían en el tiempo.
2. **Log único compartido**: al separar en customizings independientes,
   varios escritores sobre el mismo `last_batch.log` podrían sobrescribirse
   con segundos de diferencia y el drop service perdería outcomes.

### Modelo v3

- **Un customizing independiente por sucursal** (CIR, CHI, CAN, CON).
  Todos leen del **mismo folder** `E:\XML\Work\` — cada uno filtra SOLO
  sus archivos por patrón de filename: `g_sUdv[4] = 'E:\\XML\\Work\\cot_CIR_*.edi'`
  (el ERP embebe la sucursal en el filename: `cot_<SUC>_<REF>.edi`).
- **Cada customizing escribe SU log en SU carpeta**:
  `g_sUdv[10] = 'E:\\XML\\Work\\Logs\\CIR\\last_batch.log'` (etc.). La
  mecánica interna del log NO cambia respecto a la §3.2 — mismo
  `udf_FileDelete` + `udf_FileOpen` + dump de `g_lsUdv[0]`; solo cambia
  la ruta.
- **Ejecución en thread único forzado (secuenciales entre sí)** en el
  scheduler de A+W. La concurrencia real de imports queda como
  optimización futura — primero validar con el equipo A+W que el number
  manager y las tablas del pool de importación toleran imports
  concurrentes. Con volumen actual de cotizaciones, la serialización no
  penaliza de forma perceptible.
- **Drop service**: el `LaneRouter` extrae la sucursal del filename y
  vigila el `LogsDir` de ESA lane (config `DropService:AwImportLanes`,
  ver `appsettings.example.json`). Un filename cuya sucursal no tenga
  lane configurada se rechaza con **422** (error de configuración — el
  ERP no debe reintentar hasta alinear config; distinto de `stuck`).
- **Timeout**: subir `WaitTimeoutSeconds` a **240** — con customizings
  secuenciales, la última sucursal de la cola hereda la espera de las
  anteriores. Calibrar después con las métricas de `/completions`.

### Pregunta abierta con el equipo A+W (bloquea el go-live de v3)

- ¿Los customizings independientes eliminan la necesidad del **borrado de
  tablas de importación**? Si el pool de importación sigue compartido y el
  workaround sigue siendo necesario, la colisión con los imports manuales
  persiste aunque los logs estén separados. Resolver ANTES de clonar.

### Pre-requisitos adicionales v3 (por sucursal)

```powershell
# En SER-DATA, como Administrador — una vez por sucursal:
New-Item -ItemType Directory -Path "E:\XML\Work\Logs\CIR" -Force
New-Item -ItemType Directory -Path "E:\XML\Work\Logs\CHI" -Force
New-Item -ItemType Directory -Path "E:\XML\Work\Logs\CAN" -Force
New-Item -ItemType Directory -Path "E:\XML\Work\Logs\CON" -Force
```

Permisos: los mismos de §2 (escritura del usuario A+W; lectura + creación
de `Processed\` del usuario `MilletAwDropService`) aplicados a **cada**
subcarpeta de sucursal.

### Rollout sugerido

1. QA: clonar SOLO una sucursal (p. ej. CIR) + lane correspondiente en el
   drop service; correr smoke A/B de §4.1.
2. QA: agregar segunda sucursal y validar que un drop de cada una NO se
   cruza (outcome correcto, `lane` correcto en la respuesta, copia en el
   `Processed\` de su lane).
3. PROD: replicar config completa; monitorear §4.2 + verificar que
   `Logs\<SUC>\Processed\` se llena por sucursal.

Rollback v3 → v2: restaurar el customizing único desde backup y dejar la
config del drop service SIN sección `AwImportLanes` (cae a la lane
`default` legacy con `AwImportFolder`).

---

## 2. Pre-requisitos en SER-DATA

Antes de aplicar el cambio al customizing, asegurar:

1. **Crear el directorio de logs** (el customizing falla al abrir un log
   en un directorio que no existe — `udf_FileOpen` con `OF_Create` no
   crea árboles):

   ```powershell
   # En SER-DATA, como Administrador:
   New-Item -ItemType Directory -Path "E:\XML\Work\Logs" -Force
   ```

   El subdirectorio `Processed\` lo crea automáticamente el drop service
   al copiar el primer log. No hace falta crearlo manualmente.

2. **Validar permisos**: el usuario bajo el que corre A+W debe tener
   permiso de escritura en `E:\XML\Work\Logs\`. Si A+W corre como
   servicio bajo `LocalSystem` no hay problema; si corre como otro
   usuario, dar permisos explícitos:

   ```powershell
   icacls "E:\XML\Work\Logs" /grant "${AwUser}:(M)"
   ```

3. **Cleanup de logs viejos** (one-shot, si vienes de la versión 1.0 del
   runbook):

   ```powershell
   Remove-Item E:\XML\Work\Logs\last_batch_*.log -Force -ErrorAction SilentlyContinue
   Remove-Item E:\XML\Work\Logs\Processed\last_batch_*.log -Force -ErrorAction SilentlyContinue
   ```

4. **Confirmar versión del customizing actual** que vive hoy en A+W.
   Este runbook asume el script de la sección 3 (basado en el sample
   compartido por el cliente en mayo 2026 — ver §6 para el original).

---

## 3. Cambio en el customizing

### 3.1 Variables nuevas

Agregar al bloque de declaración/uso de variables al inicio del script:

```
! g_sUdv[10] = path estático del log de salida (siempre el mismo)
! g_hfUdv[1] = file handle del log
```

(Ya no hace falta `g_sUdv[11]` para timestamp — el filename es estático.)

### 3.2 Diff sobre el script existente

**ANTES** (final del script actual):

```
Set g_bUdv[0] = udf_EDIImportFromDirectory ( g_sUdv[4], g_sUdv[5], g_sUdv[6], g_nUdv[0], g_nUdv[1], g_sUdv[0], g_sUdv[1], g_sUdv[2], g_lsUdv[0] );
If g_bUdv[0] = TRUE;
Set g_sUdv[3] = 'EDI import succeeded';
EndIf;
If g_bUdv[0] = FALSE;
Set g_sUdv[3] = 'EDI import failed';
EndIf;

TRACE ( g_sUdv[3], EVENT_INFORMATION, TRACE_CUSTOMIZING );
TRACE ( g_lsUdv[0], EVENT_INFORMATION, TRACE_CUSTOMIZING );
```

**DESPUÉS**:

```
Set g_bUdv[0] = udf_EDIImportFromDirectory ( g_sUdv[4], g_sUdv[5], g_sUdv[6], g_nUdv[0], g_nUdv[1], g_sUdv[0], g_sUdv[1], g_sUdv[2], g_lsUdv[0] );

If g_bUdv[0] = TRUE;
    Set g_sUdv[3] = 'EDI import succeeded';
EndIf;
If g_bUdv[0] = FALSE;
    Set g_sUdv[3] = 'EDI import failed';
EndIf;

! ────────────────────────────────────────────────────────────────────
! Dump del log estructurado a un archivo ÚNICO que se sobreescribe
! cada ciclo. El drop service vigila este archivo y determina el
! outcome buscando el filename del EDI que acaba de escribir + el
! código (4615) "Número doc. determinado" dentro del contenido.
!
! IMPORTANTE: udf_FileWriteString APPENDEA por default según la doc
! oficial de A+W. Para garantizar overwrite limpio cada ciclo, borramos
! el archivo previo ANTES de abrirlo. Si no existe, udf_FileDelete
! retorna FALSE silenciosamente — no es problema.
! ────────────────────────────────────────────────────────────────────

Set g_sUdv[10] = 'E:\\XML\\Work\\Logs\\last_batch.log';

! Borrar log del ciclo previo (silencioso si no existe).
Call udf_FileDelete ( g_sUdv[10] );

! Crear nuevo + escribir + cerrar.
Set g_bUdv[1] = udf_FileOpen ( g_hfUdv[1], g_sUdv[10], OF_Create );
If g_bUdv[1] = TRUE;
    Set g_bUdv[12] = udf_FileWriteString ( g_hfUdv[1], g_lsUdv[0] );
    Set g_bUdv[2] = udf_FileClose ( g_hfUdv[1] );
EndIf;

TRACE ( g_sUdv[3], EVENT_INFORMATION, TRACE_CUSTOMIZING );
TRACE ( g_lsUdv[0], EVENT_INFORMATION, TRACE_CUSTOMIZING );
```

### 3.3 Notas sobre la sintaxis

- **`udf_FileDelete` antes de `udf_FileOpen`** garantiza overwrite limpio
  independientemente de la semántica exacta de `OF_Create`. La doc de
  `udf_FileWriteString` dice "Adds a line" sugiriendo append — el delete
  previo elimina la ambigüedad. Si el archivo no existe, `udf_FileDelete`
  retorna `FALSE` sin lanzar error.
- **El `If g_bUdv[0] = TRUE/FALSE`** del bloque del trace se mantiene
  porque el `TRACE` interno de A+W sigue siendo útil para diagnóstico.
  El drop service no usa `g_bUdv[0]` — el outcome viene del contenido
  del log.
- **El `If g_bUdv[1] = TRUE`** alrededor del write evita escribir si
  falla la apertura del archivo (p. ej. directorio no existe o sin
  permisos). En ese caso el drop service detecta timeout y reporta
  `stuck` — alerta operacional inmediata.
- **NO retirar los `TRACE`** existentes. Son el canal interno de A+W
  y siguen siendo útiles.
- **NO se requiere** variable de timestamp ni sufijo success/fail.
  Si tu script tenía esa lógica de la versión 1.0 del runbook,
  reemplazarla por esta versión simplificada.

---

## 4. Procedimiento de despliegue

### 4.1 En QA (validación)

1. **Backup del customizing actual** desde A+W (export a archivo de texto).
2. **Aplicar el cambio** de la §3.2 en el customizing de QA.
3. **Crear** `E:\XML\Work\Logs\` en SER-DATA-QA (§2 ítem 1).
4. **Coordinar con DevOps**: desplegar drop service de PR #203 a
   SER-DATA-QA antes de continuar (debe contener el watcher refactoreado
   que polea `last_batch.log` content-based — ver instrucciones de
   build/publish en el README del drop service).
5. **Test smoke A — EDI válido**:
   - Disparar drop desde el ERP (curl POST `/api/v1/integraciones/aw/cotizaciones`
     con quoteRef único; ver script de smoke en `docs/integration/runbooks/`).
   - Esperar al próximo ciclo del scheduler de A+W (~1 min).
   - Confirmar en `E:\XML\Work\`:
     - El EDI desapareció de `Work\` (A+W lo movió).
     - Apareció en `Save\` con prefijo de timestamp.
   - Confirmar en `E:\XML\Work\Logs\last_batch.log`:
     - Contiene una línea `(4602) ... [archivo]=...{nuestro-filename}.edi`.
     - Contiene `(4615) Número doc. determinado. [Documento]=N [Tipo]=Pedido`.
     - Contiene `@@<N>@@` al final.
   - Confirmar en `E:\XML\Work\Logs\Processed\`:
     - Apareció `{nuestro-filename}_{utcTimestamp}.log` con el contenido
       del log (copia hecha por el drop service tras detectar el outcome).
   - Confirmar respuesta HTTP del ERP: `outcome=success`, `aw_doc_id=N`.
6. **Test smoke B — EDI con artículo inválido (fuerza fail)**:
   - Disparar drop con un EDI que A+W rechazaría (cliente inexistente,
     artículo no en banco de datos, etc.).
   - Esperar ciclo de A+W.
   - Confirmar respuesta HTTP del ERP: `outcome=failed`, `aw_error_codes` no
     vacío o `aw_doc_id=null`, `aw_diagnostic_log` con el contenido del log.

### 4.2 En PROD

Sólo proceder después de:
- Cambio validado en QA por al menos 24h sin issues.
- DevOps confirmó que el ERP de prod corre PR #201 + #202 (callback
  flow + `DropTimeoutSeconds=180`).
- Drop service en SER-DATA-PROD actualizado con build de PR #203.

Pasos:
1. Backup del customizing actual de prod.
2. Crear `E:\XML\Work\Logs\` en SER-DATA-PROD (si no existe).
3. Cleanup de logs viejos (§2 ítem 3) — one-shot.
4. Aplicar cambio de §3.2.
5. **Monitorear 10 minutos**:
   - Application Insights del ERP: contadores `aw.cotizacion.drop_success`
     y `aw.cotizacion.correlated` deben subir conforme se procesan
     cotizaciones; `aw.cotizacion.drop_failed` con `outcome=stuck`
     debe quedar plano. Si sube → rollback (§5).
   - SER-DATA: confirmar que `Logs\Processed\` se llena con un archivo
     por cotización procesada.

---

## 5. Rollback

Si algo va mal tras el cambio:

1. **Restaurar el customizing anterior** desde el backup de §4.1 paso 1.
2. **NO** borrar `E:\XML\Work\Logs\` — no estorba al customizing viejo.
3. Sin el log esperado, el drop service responde `outcome=stuck` para
   todos los drops y el ERP marca `FailedDrop` con
   `kind=aw_processing_timeout`. Operacionalmente: las cotizaciones
   quedan en estado fallido reintentable hasta que se restaure el flow.

---

## 6. Apéndice: script ORIGINAL de referencia

Script tal como vivía antes de este runbook (compartido por el cliente
en mayo 2026):

```
! ********************************************************************;
! ** Read all available EDI files and create orders for them        **;
! ********************************************************************;

Set g_nUdv[0] = 0;
Set g_nUdv[1] = 1;
Set g_sUdv[0] = 'PERIFERICO';
Set g_sUdv[1] = 'VENTAS EDI';
Set g_sUdv[2] = 'EDI Import Workflow';
Set g_sUdv[4] = 'E:\\XML\\Work\\**************_EDI.ASC';
Set g_sUdv[5] = 'E:\\XML\\Work\\Fail\\';
Set g_sUdv[6] = 'E:\\XML\\Work\\Save\\';

SalLoadAppAndProcessMsgs ('wscript.exe E:\\XML\\edi_millet.vbs', Window_NotVisible, g_nUdv[10]);

Set g_bUdv[0] = udf_EDIImportFromDirectory ( g_sUdv[4], g_sUdv[5], g_sUdv[6], g_nUdv[0], g_nUdv[1], g_sUdv[0], g_sUdv[1], g_sUdv[2], g_lsUdv[0] );

If g_bUdv[0] = TRUE;
Set g_sUdv[3] = 'EDI import succeeded';
EndIf;
If g_bUdv[0] = FALSE;
Set g_sUdv[3] = 'EDI import failed';
EndIf;

TRACE ( g_sUdv[3], EVENT_INFORMATION, TRACE_CUSTOMIZING );
TRACE ( g_lsUdv[0], EVENT_INFORMATION, TRACE_CUSTOMIZING );
```

---

## 7. Apéndice: formato esperado del log A+W

Sample REAL capturado por Millet en mayo 2026 — el log que el customizing
de la §3.2 produce tras procesar exitosamente `cot_Q-2026-99020.edi`:

```
( 0 ) :    -   (4600) Ejecutando scripts de comando...
( 0 ) :    -   (4601) Buscando fichero import. [Cliente]=0 [Fichero]=E:\AgentEDI\Work\COT_**************.EDI
( 0 ) :    -   (4602) Encontr. archivo import. [archivo]=E:\AgentEDI\Work\cot_Q-2026-99020.edi
( 0 ) :    -   (4604) Fichero import. abierto. [Fichero]=E:\AgentEDI\Work\cot_Q-2026-99020.edi
( 0 ) :    -   (4605) Documento ext. guardado. [Número=1 [Cliente]=100024 [Posiciones]=1
( 0 ) :    -   (4606) Fichero import. cerrado
( 0 ) :    -   (4609) Fichero importado trasladado a carpeta backup. [fichero]=E:\AgentEDI\Work\Save\20260517220724120000_cot_Q-2026-99020.edi
( 0 ) :    -   (4601) Buscando fichero import. [Cliente]=0 [Fichero]=E:\AgentEDI\Work\COT_**************.EDI
( 0 ) :    -   (4603) El fichero no existe.
( 0 ) :    -   (4611) Import. documentos iniciada. [Número]=1
( 0 ) :    -   (4613) Documento externo leído. [Número]=1 [Posiciones]=1
( 0 ) :    -   (4615) Número doc. determinado. [Documento]=10432218 [Tipo]=Pedido
( 0 ) :    -   1/0 (Pedido:10432218)
( 0 ) :    -   (4632) Ejecución fórmula. [Fórmula]=230206
( 0 ) :    -   (4623) Encabezamiento doc. guardado. [Documento]=10432218
( 0 ) :    -   (4630) Guardando posición. [Documento]=10432218 [Posición]=1
( 2409 ) :    -   (2409) Exist. mín. para el producto 100012 sobrepasadas.
( 0 ) :    -   (4631) Terminar documento. [Documento]=10432218
( 0 ) :    -   (4616) Sumas calculadas. [Documento]=10432218 [Suma]=5451.92
( 0 ) :    -   (4617) Estatus activado. [Documento]=10432218 [Estatus]=3
( 0 ) :    -   (4618) Insertado en gestor de números. [Documento]=10432218 [GN]=Agent-EDI Import Workflow
( 0 ) :    -   (4619) Entrada historial. [Documento]=10432218 [Punto estatus]=610
( 0 ) :    -   @@<10432218>@@
```

Códigos que el drop service reconoce:

| Código | Significado | Uso del drop service |
|---|---|---|
| `(4602)` | `Encontr. archivo import. [archivo]=...` | **Marker de file detected** — el parser busca aquí nuestro filename. |
| `(4604)` | `Fichero import. abierto.` | Informativo. |
| `(4605)` | `Documento ext. guardado.` | Informativo. |
| `(4609)` | `Fichero importado trasladado a carpeta backup.` | Confirma move a `Save\` (no leído por parser; solo para auditoría). |
| `(4611)` | `Import. documentos iniciada.` | Informativo. |
| `(4615)` | `Número doc. determinado. [Documento]=N` | **Marker de SUCCESS doc id** — el parser extrae N. |
| `(4631)` | `Terminar documento. [Documento]=N` | Confirmación de doc finalizado. |
| `@@<N>@@` | (Sin código, al final) | **Backup del doc id** (añadido por nuestro customizing — no es código nativo de A+W). |

---

## 8. Contacto y troubleshooting

- **DevOps Tiglass:** Eduardo Paredes — `eduardo.paredes@tiglass.net`.
- **Equipo A+W Millet:** [operador con permisos de edición de customizing].

**Si el log se escribe pero el drop service no lo detecta:**
- Verificar que el log tiene **exactamente** el nombre `last_batch.log`
  (no `last_batch_success_*.log` ni con timestamp).
- Confirmar que el contenido tiene una línea `(4602) ... [archivo]=...{filename}.edi`
  con el filename exacto que el drop service escribió (el ERP usa
  `cot_{sucursal}_{quoteReference}.edi`).
- (v4) Confirmar que el log se escribió en el `LogsFolder` configurado
  (`DropService:LogsFolder`, por defecto `{AwImportFolder}\Logs`) — si el
  customizing escribe en otra carpeta, el drop service vigila la correcta
  pero el log cae en la equivocada y el outcome termina `stuck`.
- Confirmar que tiene `(4615) [Documento]=N` (no `(4614)` del A+W viejo).
- Verificar permisos de lectura del usuario del servicio
  `MilletAwDropService` sobre `E:\XML\Work\Logs\`.
- Revisar el Event Log de SER-DATA (source `MilletAwDropService`) para
  warnings de `AwLogWatcher`.

**Si el log crece sin parar (no se sobrescribe):**
- Verificar que `udf_FileDelete` se ejecuta antes de `udf_FileOpen`. Si
  no, `udf_FileWriteString` appendea al log existente y crece indefinido.
- Verificar permisos de DELETE del usuario A+W sobre `last_batch.log`.

**Si los logs en `Processed\` no aparecen:**
- El drop service hace `File.Copy` (no move). Verificar permisos de
  escritura del usuario `MilletAwDropService` sobre `Logs\Processed\`.
- El subdir se crea automáticamente; si falla, crear manualmente
  (§2 ítem 1).

**Si el customizing tira error al hacer `udf_FileDelete`:**
- La función retorna `FALSE` silenciosamente si el archivo no existe —
  no debe lanzar excepción. Si la lanza, revisar la versión de A+W
  (compatibilidad con esa función).
