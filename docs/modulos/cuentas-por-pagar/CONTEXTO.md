# CONTEXTO · Cuentas por Pagar (MOD-CXP)

Paquete de contexto para desarrollo. No reemplaza el levantamiento ni el diseño: los resume y apunta a ellos.
Última actualización: 2026-10-22 · Tarea que lo actualizó: 86e3kdw2k (P8.5). Este archivo se creó en esa tarea y por ahora solo cubre a fondo el circuito de caja chica; el resto del módulo está resumido y marcado con lo que no se validó.

**Convención de certeza.** Cada dato lleva una etiqueta:
- `[CONF · fuente]` confirmado por Millet o por la base de conocimiento (cita archivo, pregunta/página o nota SRC y fecha).
- `[MANUAL · archivo]` lo dice un manual del ERP escrito por VILO (p. ej. `manual-*.pdf`); NO es respuesta de Millet y no cuenta como CONF.
- `[DISEÑO · doc]` decidido por nosotros en diseño/ADR, sin respuesta de Millet que lo contradiga.
- `[SUPUESTO]` tomado a falta de respuesta; indica la alternativa y a quién se le pregunta.
- `[ABIERTO]` sin respuesta; no desarrollar sobre esto sin decisión del owner.
- `[CÓDIGO]` verificado en el repo (ruta).
Cita de preguntas: `archivo · P<número de pregunta>` (nunca páginas o párrafos mezclados). Nada se escribe sin etiqueta. Lo que no se pudo validar va a ABIERTO, nunca se inventa.

## 1. Qué es y qué no es
CxP recibe los CFDI de proveedor, los convierte en pasivo, concilia contra orden de compra y recepción, y entrega a Tesorería los pasivos autorizados para pago. También maneja los circuitos sin orden de compra: caja chica, viáticos, tarjetas empresariales y gastos aduanales. `[CÓDIGO · backend/src/CuentasPorPagar]`
No ejecuta pagos (Tesorería) ni administra préstamos a empleados (RH). Se integra por eventos con Compras, Almacén, Tesorería y Contabilidad (triada y contratos en `CLAUDE.md` de la raíz).
Responsable de Millet como referente del alcance: Laura Cerón. `[CONF · vault 02 Procesos y módulos/Planos 1.7.1/05 · Cuentas por pagar.md, 2026-10]`

## 2. Documentos a leer (en orden)
| Orden | Ruta | Para qué |
|---|---|---|
| 1 | `docs/modulos/cuentas-por-pagar/00-levantamiento.md` (§5.5, §7.4, §11.6, §13) | Reglas y variantes sin OC; contratos canónicos de la triada |
| 2 | `docs/modulos/cuentas-por-pagar/12-pasivos-internos-tesoreria.md` | Pasivos internos (caja chica, viáticos): D1 a D4. Su D2 y Q4 (mínimo de reposición) quedaron superadas por el cuestionario 03; ver §9 |
| 3 | `docs/modulos/cuentas-por-pagar/01-diseno.md` | Modelo de dominio |
| 4 | `docs/modulos/cuentas-por-pagar/P8-auditoria-09oct2026.md` | Auditoría de las tareas P8 |
| 5 | Vault `11 Manual y plan de construcción/Módulo 08 · Cuentas por Pagar.md` | Reglas R1 a R19, tareas P8.x y criterios CA |
| 6 | Vault `02 Procesos y módulos/Planos 1.7.1/05 · Cuentas por pagar.md` (§7.3 caja chica) | Recorrido de caja chica con roles y controles |
| 7 | Base de conocimiento `03_Cuestionario_Cuentas_por_Pagar.docx` | Respuestas de Laura Cerón; fuente primaria |

## 3. Fichas y estado
Estado verificado en: `develop` @ b2ff9280 el 2026-10-22 (working tree limpio al inicio de la tarea). Solo se verificó P8.5; el resto del módulo no se revisó en esta tarea.

| Ficha | Resultado | Estado real | PR o rama | Brechas abiertas |
|---|---|---|---|---|
| P8.5 (86e3kdw2k) | Caja chica sin mínimo con aprobación de pasivo | Implementado en la rama (23-oct-2026): estado `PendienteAprobarPasivo`, `aprobar-pasivo` con límite de aprobador, una reposición por comprobación, mínimo eliminado | `feature/caja-chica-aprobacion-pasivo-86e3kdw2k` | Montos de aprobadores; ver §9 |
| Resto de P8.x | — | `[ABIERTO]` no verificado aquí; ver `brechas-abiertas.md` del vault | — | — |

## 4. Reglas de negocio confirmadas
| # | Regla | Etiqueta y fuente |
|---|---|---|
| 1 | Las comprobaciones de caja se hacen semanalmente, no hay mínimo, y quedan en un estatus «pendiente de aprobar pasivo» que se resuelve en CxP | `[CONF · 03_Cuestionario_Cuentas_por_Pagar.docx · P5–6 (numeración de SRC-004), Laura Cerón, modificado 2026-08-31]` |
| 2 | En caja chica las facturas generadas por cada CFDI no son pagables: existen para gasto, IVA y DIOT; el pasivo real es la reposición | `[CONF · 01_Matriz_validacion_funcional…RESPUESTAS_MILLET.docx · P7 y CXP-03, 2026-08-19]`. La misma respuesta añade que la reposición se emite «al alcanzar un mínimo», lo cual el cuestionario corrige |
| 3 | La matriz de aprobadores con límite define quién autoriza y hasta qué monto; es la base de las firmas de comprobaciones, viáticos y notas de cargo | `[CONF · Matriz · CXP-01, 2026-08-19]` |
| 4 | Los importes de umbrales y suplencias son configuración pendiente de confirmar por las áreas | `[CONF · Matriz · F-09, 2026-08-19]` |
| 5 | Caja chica: firma del responsable de la sucursal. Aduanales: doble firma de personas distintas | `[CONF · Matriz · F-09 y CXP-07, 2026-08-19]` |
| 6 | Tarjetas: captura el analista de CxP, aprueba el supervisor o jefe de CxP, tolerancia hasta 1.00 MXN. No aplicar a caja chica | `[CONF · cuestionario 03, 2026-08-31]` |
| 7 | Única tolerancia de factura contra OC: dinero por proveedor, 0.99 MXN por omisión | `[DISEÑO · CLAUDE.md raíz, 19-oct-2026, plano CxP 1.7.1 y junta del 8-oct]` |
| 8 | Una factura de caja chica o viáticos no se puede autorizar (candado anti doble pago) | `[CÓDIGO · backend/src/CuentasPorPagar/Application/FacturaProveedor/AutorizarFactura/AutorizarFacturaCommand.cs]` |

## 5. Datos y entidades
- `ComprobacionGastos` (tipos ReembolsoCajaChica y GastosAduanales) con estados Borrador 1, PorRevisar 2, Autorizada 3, Aplicada 4, Rechazada 5, AutorizadaNivel1 6 (solo aduanales). Campos clave: SucursalId, ResponsableId, DestinoReposicion, ReposicionId, MontoTotal, Moneda, motivo de rechazo. `[CÓDIGO · Domain/ComprobacionGastos]`
- `LineaComprobacionGastos` liga cada línea a una `FacturaProveedor`. En caja chica las facturas nacen Capturada al crear la comprobación y se cancelan al rechazar. `[CÓDIGO · CrearComprobacionCajaChicaCommand.cs y TransicionesComprobacionGastosCommands.cs]`
- `ReposicionCajaChica`: pasivo interno hacia Tesorería; destino CuentaSucursal o Responsable; agrupa N comprobaciones por sucursal, destino y moneda. `[CÓDIGO · Domain/ComprobacionGastos/ReposicionCajaChica.cs]`
- `ConfiguracionReposicionCaja`: monto mínimo por sucursal (0 si no hay fila). Vigente en el código, contradice la regla 1. `[CÓDIGO]`
- `AprobadorLimite`: EmpleadoId, TipoGasto (ReembolsoCajaChica, Viaticos, TarjetaCreditoEmpresarial, OtrosSinOc), MontoMax, moneda, vigencia. Solo catálogo; ningún handler lo aplica. `[CÓDIGO · Domain/Catalogos/AprobadorLimite.cs]`
- Liga usuario a empleado: `Empleado.UsuarioId`, leída con `IEmpleadoReadPort.ObtenerPorUsuarioAsync`. `[CÓDIGO · Domain/Ports/Administracion/IEmpleadoReadPort.cs]`

## 6. Contratos
- **Evento que publica para pasivos internos:** `pasivo.autorizado-para-pago.v1` (`PasivoAutorizadoParaPagoIntegrationEvent`) con `TipoBeneficiario` CajaSucursal o Empleado, `OrigenTipo` ReposicionCajaChica y `OrigenId` = id de la reposición (idempotencia). `FacturaProveedorId` va vacío. `[CÓDIGO · Application/Integration]`
- **Consumidor:** Tesorería, `CuentasPorPagarEventListenerWorker` y `ProyectarPasivoAutorizadoHandler` (crea `PasivoPendientePago.CrearInterno`). `[CÓDIGO · backend/src/Tesoreria/Application/EventListeners]`
- **Triada y demás eventos:** ver tabla de `CLAUDE.md` de la raíz.
- **Puertos de lectura que usa:** `IEmpleadoReadPort`, `ISucursalReadPort`.
- **Permisos:** `cuentas_por_pagar.comprobaciones.{leer,capturar}`, `comprobaciones.aprobar-nivel1` y `aprobar-nivel2` (Autorizar, Aplicar y Rechazar de caja chica usan nivel 1), `reposiciones.{leer,administrar}`, `documentos.{leer,gestionar}-todas-sucursales` (bypass por sucursal, ADR-0051). `[CÓDIGO · Identidad/Domain/PermisosCanonicos.cs]`
- **Endpoints:** `/api/v1/cuentas-por-pagar/comprobaciones` (`/caja-chica`, `/{id}/enviar-revision`, `/autorizar`, `/aplicar`, `/rechazar`, aduanales); `/api/v1/cuentas-por-pagar/reposiciones-caja` (`/`, `/saldos`, `/emitir`, `/configuracion`); catálogo de aprobadores en `CatalogosCxpAdminEndpoints.cs`.

## 7. Código: dónde vive y trampas
| Qué | Ruta |
|---|---|
| Comprobaciones (comandos y transiciones) | `backend/src/CuentasPorPagar/Application/ComprobacionGastos/` |
| Reposición y mínimo | `.../ComprobacionGastos/Reposiciones/ReposicionesCajaChica.cs`, `Domain/ComprobacionGastos/ConfiguracionReposicionCaja.cs` |
| Aprobadores con límite | `.../Application/Catalogos/AprobadoresLimites/`, `Domain/Catalogos/AprobadorLimite.cs` |
| Endpoints | `backend/src/Api/Endpoints/CuentasPorPagar/ComprobacionesEndpoints.cs`, `ReposicionesCajaEndpoints.cs` |
| Frontend | `frontend/src/features/cxp/` (`pages/ComprobacionesPage.tsx`, `ComprobacionDetallePage.tsx`, `ReposicionesCajaPage.tsx`, `components/AccionesComprobacion.tsx`, `api/useReposiciones.ts`, `lib/estado-variante.ts`) |
| Tests | `backend/tests/CuentasPorPagar.UnitTests/ComprobacionGastos/`, `Catalogos/AprobadorLimiteTests.cs`, `backend/tests/Tesoreria.UnitTests/Pasivos/ProyectarPasivoAutorizadoHandlerTests.cs` |

Trampas: `AplicarComprobacionGastosHandler` incluye por Id la comprobación recién aplicada porque aún no está persistida (parámetro `incluirComprobacionId`). Rechazar solo admite Borrador, PorRevisar y AutorizadaNivel1, así que una comprobación Autorizada o Aplicada no se puede rechazar. Un estado nuevo exige tocar `EstadoComprobacionGastos`, el enum del frontend (`api/types.ts`), `estado-variante.ts`, `EstadoChips.tsx` y `AccionesComprobacion.tsx`.

## 8. Pruebas
- Las pruebas de caja chica se limitan al agregado (`ReposicionCajaChicaTests.cs`, `ComprobacionGastosAggregateTests.cs`). No hay pruebas del emisor de reposición, de la configuración del mínimo ni de saldos pendientes. `[CÓDIGO]`
- Seeds e2e con permisos de reposiciones: `e2e/seed/identity.local.json` y `identity.dev.local.json`. El rol `cxp` existe en la semilla de identidad.
- Escenario manual típico: crear comprobación de caja chica, enviar a revisión, autorizar, aplicar y ver el pasivo en la bandeja de Tesorería como «Reposición de caja chica».

## 9. Abiertos, supuestos y contradicciones
### Contradicciones
| # | Fuente A | Fuente B | Manda (o pendiente de decisión del owner) |
|---|---|---|---|
| 1 | Matriz de Millet (2026-08-19, P7 y CXP-03): reposición al alcanzar un mínimo configurable | Cuestionario 03 (2026-08-31): no hay mínimo | El cuestionario, por ser posterior y del área dueña |
| 2 | Doc 12 §D2/Q4 del repo, código actual y «Guía visual de pipelines» (SRC-023 P7): mínimo configurable | Cuestionario 03 | El cuestionario; el código y el doc 12 se actualizan en P8.5 |
| 3 | Ficha P8.5: una reposición por comprobación | Plano 1.7.1 §7.3: una reposición por corte semanal; código: agrupa por sucursal, destino y moneda | Pendiente de decisión del owner |
| 4 | Matriz F-09 y CXP-07: la firma de caja chica es del responsable de la sucursal | Cuestionario 03: el pasivo lo aprueba CxP | Probablemente dos pasos distintos; sin confirmar |
| 5 | Vault D21: «analista captura y supervisor aprueba» junto a caja chica | Cuestionario 03: esas líneas responden a tarjetas | Aplicarlo a tarjetas, no a caja chica |

### Abiertos y supuestos
| # | Pregunta o supuesto | A quién | Mientras tanto | Etiqueta |
|---|---|---|---|---|
| 1 | Montos de aprobadores con límite, titular y suplente por tipo de gasto | Laura Cerón (plano 1.7.1, Tema 32) | Datos DEMO identificados; no inventar montos | `[ABIERTO]` |
| 2 | Rol nominal que aprueba el pasivo de caja chica en CxP | Laura Cerón | Usar el permiso `comprobaciones.aprobar-nivel1` ya existente | `[SUPUESTO]` |
| 3 | Comprobaciones posteriores al corte, uso de «Emitir ahora» y documento de cierre | Laura Cerón (preguntado en el cuestionario, sin respuesta) | Conservar el corte manual sin mínimo, sin tocar su permiso | `[ABIERTO]` |
| 4 | Una reposición por comprobación o por corte | Owner del proyecto | Seguir la ficha: una por comprobación aprobada | `[SUPUESTO]` |
| 5 | Si las facturas Capturada de caja chica pueden pagarse por otros caminos (NC o anticipo aplicados) | Revisión de código pendiente | Solo se confirmó el candado en Autorizar | `[ABIERTO]` |

## 10. Aprendizajes por tarea
- 2026-10-22 · 86e3kdw2k · El mínimo de reposición vive en backend, frontend (`ReposicionesCajaPage.tsx`, `useReposiciones.ts`, `types.ts`), docs (doc 12 y levantamiento §7.4.1) y permisos; no tiene pruebas. `AprobadorLimite` nunca se ha aplicado en ningún handler.
