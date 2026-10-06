# Plan — F1-CON-03 Abrir, cerrar y reabrir periodos con autorización (`Millet.Contabilidad`)

> **Versión:** 0.1 (borrador para aprobación) · **Fecha:** 2026-10-06
> **Tarea:** F1-CON-03 · **Responsable (ClickUp):** Uziel · **Rama:** `feature/F1-CON-03-periodos-contables` (desde `main` `4933872`)
> **Estado:** PLAN, sin código.
> **Fuentes funcionales:** texto de ClickUp (planeación 28-sep, ajustada; 8 h; 5 al 6-oct) y fichas de la base de conocimiento
> `C1.1 · Periodos contables` y `C1.2 · Adaptadores reales de periodo y tipo de cambio` (verificadas contra `main` `9ecbcd2`,
> 04-oct), `Plano C1 · Motor contable` §2 y §9, `Módulo 10 · Contabilidad` (R18, R19, R28), decisión `D18`.
> **Dependencias:** F1-CON-01 (PR #30) y F1-CON-02 (PR #32), ambas en `main`.

Los periodos, el ejercicio y los calendarios de este plan son **de prueba** (`FIX-`/DEMO). **No son el calendario oficial**
de Contabilidad (dependencia externa por confirmar).

---

## 0. Diferencias entre ClickUp y las fichas (resolver antes de construir)

| # | Punto | ClickUp (texto pegado) | Ficha de la base de conocimiento | Propuesta |
|---|---|---|---|---|
| X1 | Responsable | Uziel | C1.1 y C1.2: Eliam, del 5 al 16-oct | Construye Uziel; avisar a Eliam porque C1.3–C1.5 (pólizas) consumen este contrato |
| X2 | Sustituir los NoOp «siempre abierto» | Dentro de la tarea | Fuera: es C1.2 (Facturación, Tesorería, Almacén) | **Esta tarea publica el contrato real y lo conecta en Contabilidad**; el cambio de los NoOp de los otros módulos queda en C1.2 y se registra como «pendiente» en la evidencia. Opción B en §11 si se decide incluirlo |
| X3 | Apertura | «apertura, cierre y reapertura» | Crear el año completo; estados Abierto/Cerrado | Tres estados (§3, D2): crear el ejercicio deja los periodos sin abrir y la apertura es explícita |
| X4 | Permisos | cerrar ≠ reabrir | `contabilidad.periodo.cerrar` y `contabilidad.periodo.reabrir` | Se usan los de la ficha y se agregan `leer` y `administrar` (§7) |
| X5 | Arrastre de saldos (CA10.10) | No se menciona | Sí | No hay pólizas ni saldos todavía: se documenta y se difiere (ficha §7: saldos calculados desde pólizas, sin tablas acumuladas) |
| X6 | Estimación | 8 h | — | 8 h no alcanza para backend + UI + pruebas + evidencia. Estimación propia: **≈16 h** (§12) |

---

## 1. Resultado esperado y anti-alcance

**Resultado:** ejercicio contable con 12 periodos ordinarios y el periodo 13 de ajuste, con apertura, cierre y reapertura
persistidos (estado + versión + bitácora). Un contrato público de consulta del estado del periodo reemplaza la suposición
«siempre abierto». El movimiento de Contabilidad que hoy existe (movimiento de prueba de CON-02) verifica el periodo antes de
confirmarse y se rechaza con un mensaje claro si está cerrado.

**Se construye:** entidades, migración, comandos/consultas (MediatR), endpoints `/api/v1/contabilidad/periodos/*`, permisos,
bitácora y auditoría, puerto público de consulta, conexión con los consumidores de Ola 1A, pantalla de periodos, pruebas y
evidencia.

**No se construye:**
- Checklist de cierre, conciliaciones previas (CA10.9) ni cierre anual → CON-12.
- Adaptadores en Facturación, Tesorería y Almacén → C1.2 (ver X2).
- Pólizas, saldos y arrastre → C1.3 en adelante.
- Calendario oficial, responsables reales y condiciones de cierre → por confirmar con Contabilidad.
- Cualquier efecto sobre el inventario: reabrir contabilidad **no** reabre `almacen.periodos_cerrados` (D18/R28).

---

## 2. Hallazgos en el código (verificado contra `main` `4933872`, 06-oct)

| Pieza | Dónde | Qué hace hoy | Qué pasa en esta tarea |
|---|---|---|---|
| `Facturacion.Domain.Ports.IPeriodoContablePort` | `Facturacion/Infrastructure/DependencyInjection.cs:71` → `NoOpPeriodoContablePort` | Siempre `true`. 8 handlers lo consultan (emitir factura, anticipo, NC, REP, carta porte, tramo, pedimento, reintento de timbrado). Algunos usan la fecha de **hoy**, no la del documento | No se toca (C1.2). Se registra como consumidor pendiente |
| `Tesoreria.Domain.Ports.IPeriodoContablePort` | `Tesoreria/Infrastructure/DependencyInjection.cs:30` → NoOp | Siempre `true`. 4 usos (ingreso, pagos x2, pago a cuenta) con la fecha valor | Igual: C1.2. Tesorería **ya referencia** `Millet.Contabilidad`, así que su adaptador será trivial |
| `Almacen.Domain.Ports.IPeriodoContableReadPort` | `Almacen/Infrastructure/DependencyInjection.cs:48` → NoOp | Registrado, **sin ningún handler que lo use**. Almacén bloquea con su propia tabla `almacen.periodos_cerrados` (F8-PR2) | No se toca. Se documenta que el bloqueo de inventario es independiente (D18) |
| `ConfirmarMovimientoPruebaHandler` | `Contabilidad/Application/Dimensiones/MovimientosCommands.cs` | Confirma un movimiento de prueba con `FechaContable` sin consultar periodo | **Consumidor 1:** rechaza si el periodo no está abierto |
| `ValidarMovimientoDimensionesHandler` | mismo archivo | Panel «Probar movimiento» | **Consumidor 2:** muestra el estado del periodo como error de validación |
| `RegistrarUsoCuentaCommand` (cuentas) | `Contabilidad/Application/Catalogo/CuentasCommands.cs:297` | Marca la cuenta como usada; no tiene fecha y solo lo invoca un movimiento ya validado | No aplica: no es un movimiento. Se documenta como «cubierto por el movimiento que lo invoca» |
| `BaseEntity.Version` + `If-Match`/ETag | `SharedKernel/Domain/BaseEntity.cs`, `Api/Endpoints/Contabilidad/*` (`Helpers.TryParseVersion`) | Concurrencia optimista; `DbUpdateConcurrencyException` → 409 en `GlobalExceptionHandler` | Se reutiliza para el cierre concurrente |
| `IAuditable` + `AuditSaveChangesInterceptor` | `SharedKernel` → `core.audit_log` | Auditoría automática | Se aplica a ejercicio y periodo; además bitácora propia con motivo (§4) |
| Outbox en Contabilidad | `ContabilidadDbContext.cs:12` | `PLATFORM-TODO(<OutboxContabilidad>)`: aún no existe | No se emiten eventos de integración (no hay suscriptores). La bitácora es el registro de eventos (D6) |

---

## 3. Decisiones de diseño

- **D1 · Un ejercicio por año natural y empresa.** `EjercicioContable(EmpresaId, Anio)` único. Una sola empresa (Millet); el
  periodo **no** es por sucursal (§7).
- **D2 · Estados del periodo:** `NoAbierto → Abierto → Cerrado`, y `Cerrado → Abierto` solo por reapertura.
  Crear el ejercicio genera los 13 periodos en `NoAbierto`. «Abrir» es explícito (puede abrirse en lote: «abrir enero a
  diciembre»). Esto evita que se registre por error en un mes futuro y cubre la «apertura» que pide ClickUp.
- **D3 · Periodo 13:** fechas = 31-dic del ejercicio; **nunca se resuelve por fecha** (una fecha de diciembre cae en el 12).
  Solo se abre cuando el 12 está cerrado (R19: «después del cierre ordinario»). Solo admite movimientos `Manual`
  (CA10.12); la condición «póliza autorizada» se verifica en C1.3.
- **D4 · Reglas de transición:**
  - Cerrar exige estado `Abierto` y que **los periodos anteriores del mismo ejercicio estén cerrados** (cierre secuencial;
    supuesto a validar con Contabilidad, configurable si lo rechazan).
  - Reabrir exige estado `Cerrado` y que **el periodo siguiente no esté cerrado**, o se reabren en cascada (supuesto: se rechaza
    con mensaje «reabra primero febrero»; más simple y auditable).
  - Abrir no exige orden.
- **D5 · Concurrencia e idempotencia (casos de prueba 3 y 5):**
  - Las mutaciones exigen `If-Match` con la versión. Dos cierres con la misma versión → el segundo recibe **409**
    (`DbUpdateConcurrencyException`).
  - Cerrar un periodo ya cerrado con versión vigente → **409 `CONTAB_PERIODO_YA_CERRADO`** (explícito, nunca éxito silencioso).
  - Repetir la misma petición con la misma `Idempotency-Key` → se devuelve la respuesta original (ADR-0020), sin segundo
    registro en bitácora.
  - Índice único en bitácora `(periodo_id, version_resultante)`: imposible duplicar el evento aunque fallen las capas anteriores.
- **D6 · Bitácora como registro de eventos.** Tabla `periodos_contables_bitacora` (acción, estado anterior/nuevo, usuario,
  fecha, motivo, versión). Se suma a `core.audit_log`. Los eventos de integración (`PeriodoContableCerradoEvent`,
  `PeriodoContableReabiertoEvent`) quedan con `PLATFORM-TODO(<OutboxContabilidad>)` hasta que haya suscriptores (C1.2/C1.5).
- **D7 · Motivo obligatorio** en cerrar y reabrir (10–500 caracteres). Abrir: opcional.
- **D8 · Contrato público de consulta** (en `Contabilidad/Application/PublicPorts`):
  ```csharp
  public interface IPeriodoContableConsultaPort
  {
      Task<EstadoPeriodoContable> ConsultarPorFechaAsync(DateOnly fecha, CancellationToken ct);       // 1–12
      Task<EstadoPeriodoContable> ConsultarAsync(int anio, int numero, CancellationToken ct);        // 1–13
  }
  public sealed record EstadoPeriodoContable(int Anio, int Numero, EstadoPeriodo Estado, bool Existe)
  {
      public bool AdmiteMovimientos => Existe && Estado == EstadoPeriodo.Abierto;
  }
  ```
  Más un `VerificadorPeriodoContable.LanzarSiNoAdmiteAsync(fecha, origen)` que lanza `BusinessRuleException`
  `CONTAB_PERIODO_CERRADO` / `CONTAB_PERIODO_NO_ABIERTO` / `CONTAB_PERIODO_INEXISTENTE` (→ 422 con mensaje: «El periodo
  2026-09 está cerrado; no se pueden registrar movimientos con esa fecha»).
- **D9 · Periodo inexistente = no admite movimientos (falla cerrada)** en Contabilidad. Para C1.2 queda anotado el riesgo:
  conectar Facturación/Tesorería obliga a crear el ejercicio antes de operar (la demo y la carga de arranque deben sembrarlo).
- **D10 · Sin efecto en inventario.** No se publica nada hacia Almacén; su tabla de periodos sigue siendo su fuente (D18).

---

## 4. Modelo de datos (esquema `contabilidad`, una migración `ContabilidadPeriodos`)

| Tabla | Columnas clave | Restricciones |
|---|---|---|
| `ejercicios_contables` | `id`, `empresa_id`, `anio`, `version`, auditoría base | `UNIQUE (empresa_id, anio)`, `CHECK anio BETWEEN 2000 AND 2999` |
| `periodos_contables` | `id`, `ejercicio_id`, `empresa_id`, `anio`, `numero` (1–13), `fecha_inicio`, `fecha_fin`, `estado`, `abierto_por/en`, `cerrado_por/en`, `reabierto_por/en`, `version` | `UNIQUE (empresa_id, anio, numero)`, `CHECK numero BETWEEN 1 AND 13`, índice `(empresa_id, fecha_inicio, fecha_fin)` |
| `periodos_contables_bitacora` | `id`, `periodo_id`, `accion` (Abrir/Cerrar/Reabrir), `estado_anterior`, `estado_nuevo`, `motivo`, `usuario_id`, `usuario_nombre`, `ocurrido_en`, `version_resultante` | `UNIQUE (periodo_id, version_resultante)`; append-only (`INotAudited` no; sin UPDATE/DELETE en el handler) |

`EjercicioContable` y `PeriodoContable` heredan de `BaseEntity` + `IAuditable`; `EmpresaId` con el filtro global (ADR-0011).

---

## 5. Comandos y consultas (MediatR + FluentValidation)

| Caso de uso | Tipo | Permiso | Respuesta / errores |
|---|---|---|---|
| `CrearEjercicioContableCommand(anio)` | Command | `contabilidad.periodo.administrar` | 201 + 13 periodos `NoAbierto`; 409 `CONTAB_EJERCICIO_EXISTE` |
| `AbrirPeriodosCommand(ejercicioId, numeros[], motivo?)` | Command | `contabilidad.periodo.administrar` | 200; 422 si alguno no está `NoAbierto`, o 13 con 12 sin cerrar |
| `CerrarPeriodoCommand(periodoId, version, motivo)` | Command | `contabilidad.periodo.cerrar` | 200; 409 versión / ya cerrado; 422 anterior abierto |
| `ReabrirPeriodoCommand(periodoId, version, motivo)` | Command | `contabilidad.periodo.reabrir` | 200; 403 sin permiso; 409 versión; 422 siguiente cerrado |
| `ListarEjerciciosQuery` / `ObtenerEjercicioQuery(id)` | Query | `contabilidad.periodo.leer` | Ejercicio + 13 periodos con estado y versión |
| `ObtenerBitacoraPeriodoQuery(periodoId)` | Query | `contabilidad.periodo.leer` | Historial (quién, cuándo, por qué) |
| `ConsultarEstadoPeriodoQuery(fecha \| anio+numero)` | Query | `contabilidad.periodo.leer` | `EstadoPeriodoContable` (mismo contrato que el puerto, expuesto por HTTP para la UI) |

---

## 6. API (`/api/v1/contabilidad/periodos`, ADR-0021)

```
GET    /ejercicios                                   → lista
POST   /ejercicios                  Idempotency-Key  → crear ejercicio
GET    /ejercicios/{id}                              → ETag
POST   /ejercicios/{id}/abrir       Idempotency-Key, If-Match (versión del ejercicio)
POST   /{periodoId}/cerrar          Idempotency-Key, If-Match
POST   /{periodoId}/reabrir         Idempotency-Key, If-Match
GET    /{periodoId}/bitacora
GET    /estado?fecha=2026-09-15  |  ?anio=2026&numero=13
```

Problem Details (ADR-0010); contrato completo en `10-contrato-api-periodos.md` (se escribe con el código).

---

## 7. Permisos y alcance por sucursal

- Nuevos en `PermisosCanonicos` y en `frontend/.../permission-codes.test.ts`:
  `contabilidad.periodo.leer`, `contabilidad.periodo.administrar`, `contabilidad.periodo.cerrar`,
  `contabilidad.periodo.reabrir`.
- Asignación DEMO: «Contador General» (o el rol equivalente sembrado hoy) recibe `reabrir`; un rol contable operativo recibe
  `leer` + `cerrar`. **Por confirmar con Contabilidad** quién cierra y quién reabre (R18 dice Contador General para ambas).
- **Sucursal:** el periodo es de la empresa, no de una sucursal; no aplica `SucursalScopeGuard` (ADR-0051) en los endpoints de
  periodos. Los consumidores siguen aplicando su propio alcance (el movimiento de prueba ya verifica la sucursal).

---

## 8. Conexión de consumidores

| Consumidor | Módulo / ola | Acción en CON-03 | Estado al cerrar |
|---|---|---|---|
| Confirmar movimiento de prueba (dimensiones) | Contabilidad · Ola 1A | `VerificadorPeriodoContable` antes de guardar → 422 | **Conectado** |
| Validar movimiento («Probar movimiento») | Contabilidad · Ola 1A | Error de validación `PERIODO` en `ValidacionDimensiones` | **Conectado** |
| Uso de cuenta (`RegistrarUsoCuentaCommand`) | Contabilidad · Ola 1A | Sin fecha; lo invoca un movimiento ya verificado | No aplica (documentado) |
| Facturación `IPeriodoContablePort` (8 handlers) | Facturación | Adaptador → `IPeriodoContableConsultaPort` | **Pendiente C1.2** |
| Tesorería `IPeriodoContablePort` (4 usos) | Tesorería | Ídem | **Pendiente C1.2** |
| Almacén `IPeriodoContableReadPort` (sin usos) | Almacén | Ninguna; bloqueo propio (D18) | **Pendiente C1.2 / no aplica** |
| Pólizas manuales y automáticas | C1.3–C1.5 | Usan el verificador; periodo 13 solo manual | **Pendiente (olas futuras)** |

---

## 9. UI (`/contabilidad/periodos`)

Antes de tocar UI: leer `design-system/DESIGN.md`, `FIGMA.md`, `README.md` y `frontend/AGENTS.md`; patrón P3 de
`frontend/docs/patrones-compras.md`.

- Selector de ejercicio + botón «Nuevo ejercicio» (solo con `administrar`).
- Tabla de 13 periodos: número, nombre (Enero… / «13 · Ajustes de auditoría»), fechas, estado (badge del design system),
  quién/cuándo cerró o reabrió, acciones según permiso y estado.
- Diálogo de confirmación para cerrar y reabrir con **motivo obligatorio**; reabrir con aviso «No reabre el inventario».
- Panel de bitácora por periodo.
- Manejo de 409 («Otro usuario modificó este periodo; recargue») y 422 con el mensaje del servidor.
- Panel «Probar movimiento» (CON-02) muestra el error de periodo.
- Entrada en navegación y buscador de accesos protegida por `contabilidad.periodo.leer`.

---

## 10. Pruebas

**Unitarias (`Contabilidad.UnitTests`):** transiciones válidas e inválidas, periodo 13 (solo tras cerrar el 12; solo `Manual`),
resolución de fecha → periodo, motivo obligatorio, cierre secuencial, reapertura con siguiente cerrado.

**Integración (`Api.IntegrationTests`, Postgres real):**

| # | Caso (ClickUp / ficha) | Esperado |
|---|---|---|
| 1 | Periodo abierto: confirmar movimiento | 200, se guarda |
| 2 | Contabilizar en periodo cerrado | 422 `CONTAB_PERIODO_CERRADO`, nada escrito |
| 3 | Cierre concurrente (dos `If-Match` con la misma versión, en paralelo) | uno 200, el otro 409; una sola fila de bitácora |
| 4 | Reapertura sin permiso | 403; estado sin cambio |
| 5 | Repetir cierre (ya cerrado) / misma `Idempotency-Key` | 409 explícito / respuesta original; bitácora sin duplicado |
| 6 | Consumidores de Ola 1A respetan el contrato | movimiento de prueba y panel de validación rechazan periodo cerrado, inexistente y no abierto |
| 7 | Reapertura autorizada (C1.1-a inverso) | 200 + bitácora con quién/cuándo/motivo + `core.audit_log` |
| 8 | Reabrir contabilidad no reabre inventario (C1.1-b) | `almacen.periodos_cerrados` sin cambio; movimiento de Almacén sigue rechazado |
| 9 | Periodo 13 (CA10.12, parte disponible) | No abre con el 12 abierto; rechaza origen no manual |

**Frontend:** pruebas del diálogo (motivo obligatorio, botones según permiso) y `permission-codes.test.ts`.

---

## 11. Fases y orden de trabajo

1. **Dominio + migración** — entidades, configuraciones EF, migración `ContabilidadPeriodos`, pruebas unitarias.
2. **Aplicación + API + permisos** — comandos/consultas, endpoints, `PermisosCanonicos`, siembra DEMO del ejercicio 2026.
3. **Contrato y consumidores** — `IPeriodoContableConsultaPort`, verificador, conexión en movimiento de prueba y panel.
4. **Pruebas de integración** — casos 1–9.
5. **UI** — pantalla, diálogo, bitácora, navegación.
6. **Documentación y evidencia** — `10-contrato-api-periodos.md`, ADR-0058 (periodos contables: estados, periodo 13, contrato
   de consulta y falla cerrada), `11-evidencia-f1-con-03.md` (consumidores conectados/pendientes, capturas UI/API), nota en
   la bóveda de Obsidian. PR con «F1-CON-03» en el título.

**Opción B (si se decide incluir X2):** fase 3b con adaptadores en Facturación y Tesorería hacia el contrato y quitar sus NoOp
(+4 h, y obliga a sembrar ejercicios en todas las pruebas de esos módulos). Recomendación: **no**, dejarlo en C1.2.

---

## 12. Esfuerzo

| Fase | Horas |
|---|---|
| 1 Dominio + migración | 2.5 |
| 2 Aplicación + API + permisos | 3 |
| 3 Contrato + consumidores | 1.5 |
| 4 Integración | 3 |
| 5 UI | 4 |
| 6 Docs + evidencia | 2 |
| **Total** | **≈16 h** (contra 8 h de ClickUp; la tarea vence hoy 6-oct) |

---

## 13. Lo que se necesita de Millet (no bloquea; se construye con supuestos)

- Calendario contable oficial y si el ejercicio es siempre año natural (supuesto: sí).
- Quién cierra y quién reabre (supuesto: Contador General reabre; rol contable cierra).
- Si el cierre debe ser secuencial y si reabrir enero obliga a reabrir los meses siguientes (supuestos D4).
- Una reapertura real de ejemplo con operaciones permitidas/rechazadas (complemento propuesto en F1-CON-03).

## 14. Dependencias de plataforma (ADR-0031)

| Pieza | Ticket | NoOp en uso | Cómo se conecta |
|---|---|---|---|
| Eventos de periodo | `<OutboxContabilidad>` | Ninguno (solo bitácora) | Outbox en `ContabilidadDbContext` cuando haya suscriptores |
| Candado en Facturación/Tesorería/Almacén | C1.2 | `NoOpPeriodoContable*Port` | Adaptador hacia `IPeriodoContableConsultaPort` en el DI de cada módulo |
| Arrastre de saldos | C1.3/C1.7 | — | Saldos calculados desde pólizas |

## 15. Riesgos

- **Falla cerrada (D9)** al conectar C1.2: sin ejercicio creado, Facturación y Tesorería dejarían de operar. Mitigación: siembra
  del ejercicio en demo/arranque y alerta en la UI cuando falte el del año siguiente.
- Facturación consulta el periodo con la fecha de **hoy** en varios handlers; al conectarlo en C1.2 hay que revisar qué fecha
  manda la regla D13.
- Supuestos D4 pueden cambiar con la respuesta de Contabilidad (cambio acotado al dominio).
