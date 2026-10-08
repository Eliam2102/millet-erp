# Uziel · contraste de asignaciones al 06-oct-2026

Fuente vigente: paquete local `Para_Uziel`, mensaje y arranque del 06-oct; fichas completas semanales.

**Estructura de ramas (06-oct, noche).** `ficha/U1.2-adm09-pac-series` parte de los seis commits de `origin/feature/F1-ADM-09` (`c93205e4`) con merge de `origin/main` (`49dd6df2`, incluye ADM-06/07, design system, CON-01/02 y G1.2). Conflictos resueltos: `SucursalScopeGuard` conserva ambas constantes y `SeriesEndpointsTests` conserva las dos pruebas de 403 en reserva. El trabajo U1.2 vive en el worktree `millet-erp-u1`, hermano del checkout principal; CON-03 sigue en su propia rama y no se mezcla con U1.2 (sin dependencia funcional; solo comparten `Program.cs`).

## Matriz inicial (lectura de código; no aceptación)

| Ficha | Implementado al corte remoto | Faltante | CA a validar | Dependencias/evidencia |
|---|---|---|---|---|
| U1.2 | PAC cifrado, metadatos CSD, permisos por empresa/sucursal, control de versión y reserva transaccional en rama ADM-09 | Candidato antes de guardar; unicidad activa fiscal, continuidad inicial e inmutabilidad; revisión P01–P06; sandbox real | CA1.7 serie de Cancún configurable; CA1.8 credenciales inválidas sin escritura y secretos no recuperables | `32-adm09-continuacion-sandbox.md`; 196 unitarias/37 HTTP son resultados históricos; E06 Eliam, V06 datos Fiscal/TI |
| U1.0 | Asignación/revocación y desactivación ya invalidan la caché local; roles publican eventos | Validar asignación/revocación existentes; completar cambios de permisos del rol y service principals; frontend actualizado; coherencia multinstancia | CA1.5 y U1.0-a: siguiente petición rechazada | `AsignarRolAUsuarioCommand`, `RevocarRolDeUsuarioCommand`, `ActualizarRolCommand`; cachés TTL actuales |
| U1.9 | Snapshot del receptor y catálogos SAT existentes | Validación completa del maestro antes de reservar o llamar PAC en venta, anticipo, NC y REP; mensaje/enlace al cliente | CA2.6/CA6.4, genérico nacional y compatibilidad 605/G03 | `CfdiEmisionBuilder.ReceptorDe`, `EmitirFacturaVentaHandler`; datos DEMO, sin consulta SAT en línea |
| U1.6 | Eventos v1 y consumidores CxC/A+W | Campos contables opcionales y mappers; contrato común con G1.6 | U1.6-a desglose venta/Ranura/NC; U1.6-b evento antiguo compatible | `FacturacionIntegrationEvents.cs`; coordinar formato con Geovany y consumidor C1.5 Eliam |
| U1.1 | Emisión manual y `facturacion.repp.emitir`; disparo automático existente | Bandeja idempotente solo PPD, PUE solo log; edición de relación sin cambiar monto; emisión individual/lote y alerta | CA6.9, U1.1-a sin emisión automática, U1.1-b alerta, dependencia CA7.14/C7.8 | `EmitirReppDesdePagoConfirmadoCommand`; regla 06-oct sobre crédito PUE prevalece |
| I13.4 | Lectores/puente/write-back, scripts 01–06, ADR-0048, runbooks; `integration/08` §7 disponible | Adaptar borrador suministrado, resolver marcas con trazabilidad y límites implementados/propuestos | I13.4-a revisión de Jorge; CA13.1/13.5 dependen de conexión y retornos posteriores | Borrador 8-oct, envío solo por Eliam; ERP detecta 15/42 desde historial; CDC no habilitable en RTM Standard |
| K10.2 | Integrada en main por PR #32 | Comentario de cierre y CA en demo por Eliam | Los CA de la ficha K10.2 | No reimplementar; borrador abajo |

## Discrepancias que se conservan visibles

- Handoff ADM-09 dice «sin commit»: fixes de error deshabilitado y `form.reset` sí están en `8d854d12` y en la rama remota `c93205e4`.
- Plan ADM-09 antiguo exige esperar aprobación para P01–P05. Paquete vigente autoriza construir con propuestas; aprobación sigue siendo previa a integración.
- P03 antigua proponía bloquear si faltaba serie específica. Ficha vigente propone prioridad específica sobre global; se construye con ese fallback.
- Custodia actual = ciphertext PostgreSQL + Data Protection, wrapping key en Key Vault y key ring en Blob. Eso no equivale a guardar el CSD como certificado Key Vault según ADR-0038. P06 exige decisión productiva; no declararla resuelta con wiring.
- Layout fiscal: 28NOV/28NOV (2) y Hoja2 usan nomenclaturas distintas; serie vigente/último folio/REP siguen por confirmar. No sembrar valores de producción ni asumir equivalencias.
- `docs/arquitectura.md` conserva referencias históricas .NET 9, varias razones sociales y FakeForLocalDev habitual; README/CLAUDE/ADR-0051 vigentes definen .NET 10, una empresa y login Entra. No cambiar auth real.
- Horas 14:00/17:00 del viernes son propuestas por confirmar.

## Comentario preparado para PR #32 (publica Uziel)

Cierra: K10.2

Implementación integrada en main mediante PR #32 (`49338723`). Pendiente que Eliam valide los criterios de aceptación de K10.2 en la demo y registre su evidencia. Este comentario identifica la ficha; no sustituye aceptación UAT.

## U1.2 · propuestas para Eliam (E06)

| Propuesta | Construcción en esta entrega | Validación requerida antes de integrar |
|---|---|---|
| P01 · probar antes de guardar | La prueba transitoria no escribe ni cachea. PUT vuelve a probar las credenciales efectivas exactas en servidor después de validar CSD, y solo entonces guarda; un fallo no cambia ni siquiera el agregado rastreado. **Excepción:** guardar la configuración como inactiva no exige conexión (desactivar con PAC caído o llave comprometida); reactivarla sí la exige | Confirmar prueba automática al guardar (además del botón), timeout de 10 s y que desactivar sin prueba sea aceptable |
| P02 · una fiscal activa por sucursal/tipo | Índices únicos parciales para CFDI, NC y anticipo; global y específica pueden coexistir. Duplicados legados detienen migración y se concilian manualmente, sin borrar historial. OC/Póliza conservan catálogo existente; toda reserva ambigua se rechaza | Confirmar alcance fiscal; no extender automáticamente a OC/Póliza |
| P03 · específica antes de global | Reserva bloquea candidatas y prioriza sucursal. Solo utiliza global si no existe específica activa | Ratificar fallback del paquete 06-oct frente a la alternativa histórica de bloquear |
| P04 · folio inicial y continuidad | Captura positiva en alta, preview con ese inicio; nuevas series fiscales sin reinicio. Reserva atómica avanza; replay no reutiliza huecos. Campo no editable posteriormente | Fiscal entrega siguiente folio vigente; layout no demuestra últimos folios. Las series fiscales heredadas con reinicio se rechazan antes de reservar hasta conciliar su reemplazo |
| P05 · serie fiscal usada inmutable | Edición y reserva comparten bloqueo de fila. Tras primera secuencia, cambios de prefijo/sufijo/reinicio rechazan; desactivación conserva historial. Reserva añade SerieId sin romper respuesta existente | Confirmar reemplazo por nueva serie. Persistencia de SerieId en cada CFDI consumidor no forma parte del CA actual y sigue por definir |
| P06 · llaves en Key Vault | Se conserva la implementación recuperada: secretos cifrados en BD, wrapping key Key Vault + key ring Blob; no se modifican infraestructura ni configuración real | Decidir custodia de material CSD como Certificate Key Vault según ADR-0038 o revisar ADR para Data Protection. Wiring no demuestra recuperación runtime/productiva |

Datos fiscales: se inspeccionaron los nombres de hojas de ambos XLSX del paquete y las respuestas de serie, REP, TC y certificados del cuestionario 04 DOCX. No se importaron datos privados ni se copiaron certificados. Fuente fiscal del 06-oct informa nomenclaturas diferentes entre hojas; siguen pendientes equivalencias y continuidad vigente de Fiscal.

Sandbox real: no se encontró TenantKey en el entorno del proceso ni un user-secrets en las ubicaciones locales estándar inspeccionadas. Se pidió a Uziel solo ubicación de un almacén seguro, sin valores sensibles. Esto no prueba que no exista otro almacén autorizado. No ejecutar probes de producción ni descarga masiva para suplir una prueba de conexión sandbox.
