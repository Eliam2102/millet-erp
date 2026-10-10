---
title: "ADM-09 - Plan tecnico de brechas contra especificacion v2"
fecha: 2026-09-30
estado: fases-1-y-gate-local-verdes-fases-2-a-4-parciales
tags: [ADM-09, integraciones-fiscal, series, folios, FiscalAPI, planificacion]
aliases: ["Plan pendiente ADM-09 v2"]
---

# ADM-09 - Plan tecnico de brechas contra especificacion v2

## 1. Objetivo y corte verificado

Plan de lo pendiente de ADM-09, construido al contrastar completamente:

- [[../../referencias/ADM09_Especificacion_funcional_tecnica_v2.pdf|ADM09_Especificacion_funcional_tecnica_v2.pdf]], v2.0 del 30-sep-2026;
- [[03-f1-adm-09-plan-implementacion|plan anterior]];
- [[../../handoff/30-evidencia-f1-adm-09|evidencia de la rama]];
- código real del checkout `feature/F1-ADM-09`, HEAD `ec51761`.

No declara aprobadas P01-P06 ni ejecutados T01-T12 y no contiene secretos. La solución mínima reutiliza `ConfiguracionPac`, `Serie`, `SecuenciaFolio`, Data Protection, permisos, auditoría e idempotencia existentes; sólo añade invariantes o contratos demostrablemente ausentes.

## 2. Resultado ejecutivo

La base existe, pero aún no está lista para una prueba sandbox concluyente. Orden crítico:

1. **Corregir aislamiento multiempresa de Series.** `Serie` tiene `EmpresaId`, pero no implementa `IPerteneceAEmpresa`; sus handlers aceptan el ID del request y no siempre lo contrastan con `ICurrentEmpresaContext`. El filtro global y el interceptor cross-tenant no la protegen.
2. **Aplicar y verificar migraciones/runtime.** Existen migraciones de vigencia CSD e idempotencia; falta gate HTTP reproducible contra PostgreSQL desechable y reinicio con descifrado del key ring.
3. **Cerrar decisiones mínimas P02-P05.** Determinan si una reserva sandbox elige una serie inequívoca, específica, continua e históricamente estable.
4. **Decidir o diferir P01 explícitamente.** Hoy se guarda primero y se prueba sólo lo persistido.
5. **Restringir URL/ambiente.** El servidor admite cualquier URL absoluta HTTP/HTTPS. Debe usar orígenes HTTPS autorizados antes de cargar credenciales.
6. **Recibir insumos externos por canal seguro** y ejecutar T12 con Fiscal/Contabilidad.

No implementar todavía tokens de candidato, otra bóveda ni otra entidad de emisor. Proceden sólo con un contrato aprobado para P01/P06.

## 2.1 Avance técnico al 1-oct-2026

- **DT-01 corregida:** `Serie` adopta `IPerteneceAEmpresa`; el filtro global limita lecturas a la empresa del JWT y el interceptor protege escrituras. Listado, alta y reserva también contrastan explícitamente cualquier `EmpresaId` recibido con `ICurrentEmpresaContext`. Detalle, edición y desactivación de IDs ajenos quedan ocultos por el filtro.
- **DT-07 corregida:** `ConfiguracionPac` acepta únicamente los orígenes HTTPS `test.fiscalapi.com` y `live.fiscalapi.com`, sin credenciales en URL, puertos alternos, paths, query ni fragmentos. La fábrica del SDK revalida la URL persistida antes de crear un cliente, cubriendo datos heredados inválidos.
- **Regresiones añadidas:** ciclo cross-tenant de Series sin efectos sobre la fila ajena; rechazo de HTTP, localhost, loopback, host suplantado, path y puerto no oficial; PUT PAC ajeno se valida en el borde HTTP.
- **Verificación obtenida:** `Integraciones.Fiscal.UnitTests` **195/195**; compilación aislada de `Api.IntegrationTests` **0 warnings, 0 errores**.
- **Gate PostgreSQL concluido:** se aplicaron los 12 contextos de migración a PostgreSQL 17 desechable y pasaron **37/37** pruebas HTTP focalizadas. Incluyen 50 reservas concurrentes, idempotencia, carrera desactivar-reservar, aislamiento multiempresa, allowlist PAC y conflictos de versión. No se utilizó `millet-dev-postgres`.
- **Decisiones respetadas:** no se modificaron P01-P06, `FirstOrDefaultAsync`, fallback global, unicidad activa, continuidad ni custodia productiva.

## 2.2 Avance DT-04 e idempotencia

- La reserva toma un bloqueo `FOR UPDATE` sobre la serie activa dentro de la misma transacción que ejecuta el UPSERT del contador.
- Si la reserva bloquea primero, la desactivación confirma después de esa reserva; si la desactivación confirma primero, la consulta bloqueada reevalúa `activa` y la reserva falla con `SERIE_NO_CONFIGURADA`.
- Se añadió una prueba PostgreSQL que ordena la intercalación mediante un bloqueo de fila controlado, y otra que acredita replay con misma clave/body, siguiente folio con clave nueva y `422` al reutilizar clave con body distinto.
- Las pruebas de 50 reservas, idempotencia y carrera se ejecutaron en Docker Desktop `desktop-linux` y pasaron dentro del gate **37/37**. No se intervino WSL2 ni `millet-dev-postgres`.
- P02/P03 siguen bloqueando unicidad/ambigüedad/fallback. El bloqueo transaccional no selecciona una política nueva y conserva el comportamiento heredado.

## 2.3 Control optimista y continuidad acreditada

- `PATCH` y desactivación de Series exigen `X-Expected-Version`; una versión obsoleta devuelve `409 CONCURRENCY_CONFLICT`. La UI envía la versión mostrada.
- El PUT PAC acepta alta sin versión y exige coincidencia al actualizar; una rotación obsoleta no modifica configuración ni secretos.
- El contador mantiene continuidad sin decremento ni reutilización de huecos; el replay idempotente no consume otro folio.
- Inicialización productiva, inmutabilidad tras uso y `SerieId` siguen pendientes de P04/P05 y datos de Fiscal.

## 3. Requisitos confirmados y estado real

| Requisito confirmado | Estado verificado | Pendiente mínimo |
|---|---|---|
| Una entidad emisora Millet | `Empresa` es fuente de RFC, razón social, régimen y CP; Facturación la consume | Validar lugar de expedición por sucursal; no derivarlo del nombre |
| Reutilizar FiscalAPI, configuración, Series y Folios | Cumplido; no hay PAC ni contador paralelo | Mantenerlo sin abstracciones nuevas |
| Secretos sólo de escritura | DTO/UI no devuelven ApiKey/CSD; auditoría protege nombres ApiKey/CSD/certificado/llave | T01/T03/T11 contra HTTP y logs reales |
| ApiKey/CSD cifrados | Data Protection cifra los cuatro valores; Bicep declara Blob + Key Vault | T10 runtime: reinicio, recuperación y fallo seguro |
| Vigencia CSD visible | Correspondencia cer/key/password y tres estados implementados | Aplicar/verificar `20260930043912_CsdVigenciaMetadatos`; T08/T11 |
| Configuración aislada por empresa | PAC y Series usan `IPerteneceAEmpresa`; guardas y regresiones cross-tenant compilan | Ejecutar T03 contra PostgreSQL desechable |
| Series por sucursal/tipo | Modelo, empresa, filtro, `UsuarioSucursal` y cinco tipos existen | Ambigüedad, política global/específica y consumidor dependen de P02/P03 |
| Desactivación conserva historia | No hay borrado; detalle queda; reserva bloquea la fila y reevalúa estado | Ejecutar carrera y decidir relación de reserva |
| Reserva atómica | UPSERT por `(serie_id, periodo_clave)`, bloqueo de serie y test de 50 definidos | Ejecutar en PostgreSQL real; resolver selección según P02/P03 |
| Reintento idempotente | Middleware + migración y prueba específica de reserva definidos | Ejecutar T05 completo |
| Permiso y alcance en servidor | PAC/Series tienen permisos y sucursal se valida | Acreditar empresa; decidir permiso administrativo vs operativo |
| UI conectada | Formularios y smoke tests existen | Conflicto/403/422, recarga y errores contra API real |
| Auditoría sin secretos | `IAuditable` + interceptor con redacción | Reserva SQL directa y conflictos sin evento de negocio propio |

## 4. P01-P06: decisiones y límites

### P01 - probar candidato antes de guardar

- **Actual:** `TestConexionPacCommand` recibe BaseUrl/ApiKey/timeout pero el handler los ignora; el SDK resuelve lo persistido. La UI bloquea probar cuando hay cambios.
- **Decide VILO:** aceptar temporalmente guardar-luego-probar o aprobar prueba transitoria ligada al candidato.
- **Mínimo si se aprueba:** validar candidato en memoria y devolver comprobante opaco de corta vida ligado a usuario, empresa, proveedor y hash; PUT exige ese comprobante. No persistir candidato ni confiar en un booleano del navegador.
- **Evitar:** workflow genérico, caché distribuida o rediseño SDK sin conocer caducidad/topología.

### P02 - una candidata fiscal activa

- **Actual:** sólo hay consulta previa para duplicado exacto; los índices son no únicos. La reserva usa `FirstOrDefaultAsync`.
- **Deciden VILO + Fiscal:** una activa por `(empresa, sucursal, tipo)` para tipos fiscales y si abarca OC/Póliza.
- **Mínimo:** diagnosticar duplicados sin borrarlos; resolución manual; índice único parcial después; rechazo explícito de legado ambiguo.

### P03 - bloquear si falta serie específica

- **Actual:** primero busca sucursal y luego cae silenciosamente a global; una prueba acredita ese fallback.
- **Decide Fiscal:** bloquear para CFDI/Nota de crédito/Factura anticipo o autorizar global. Revisar Carta Porte/REP, que reservan como `Cfdi`.
- **Mínimo:** política por `TipoDocumentoSerie` dentro del handler; no crear motor de reglas ni cambiar OC/Póliza sin revisar consumidores.

### P04 - sin reinicio ni reutilización automática

- **Actual:** `None`, `Anual`, `Mensual`; secuencia nueva inicia en 1; no existe decremento/reinicio manual.
- **Decide Fiscal:** política y continuidad real.
- **Mínimo:** `None` e inicio 1 sólo en fixtures. Producción requiere inicialización controlada y auditable; nunca reutilizar huecos.

### P05 - identidad histórica después de uso

- **Actual:** prefijo/sufijo/reinicio editables aun con secuencias. `Version` se devuelve, pero PATCH/desactivar no la verifican.
- **Deciden Fiscal + VILO:** bloquear campos tras primer uso y reemplazar con nueva serie + desactivación.
- **Mínimo:** existencia de `SecuenciaFolio` como señal; rechazo de edición y control de versión canónico. No crear historial paralelo.

### P06 - custodia productiva

- **Actual:** secretos cifrados en PostgreSQL; key ring Blob y wrapping key Key Vault declarados. ADR-0038 sigue propuesta y menciona otra custodia.
- **Deciden VILO + TI:** aceptar BD/Data Protection o migrar material.
- **Mínimo:** primero T10 runtime y ADR. Si se rechaza lo actual, migración reversible sin exponer plaintext.

## 5. Deuda técnica verificable

| ID | Evidencia | Impacto | Tratamiento |
|---|---|---|---|
| DT-01 | `Serie` no implementa `IPerteneceAEmpresa`; operaciones no contrastan siempre empresa actual | Operación cross-tenant con permiso e ID/empresa ajena | **Bloqueador sandbox:** aplicar convención existente y pruebas |
| DT-02 | La unicidad comentada en `Serie.cs` no existe en BD; hay índices no únicos + `AnyAsync` | Altas concurrentes duplicadas | Corregir comentario; constraint según P02; traducir a 409 |
| DT-03 | `FirstOrDefaultAsync` ante candidatas activas | Reserva no determinista | Rechazo explícito + constraint tras diagnóstico |
| DT-04 | Corregida con bloqueo de fila y transacción; prueba compila | Falta acreditar que no nace reserva tras desactivación confirmada | Ejecutar T06 intercalado en PostgreSQL desechable |
| DT-05 | Reserva no entrega `SerieId`; consumidores sólo guardan folio/número | Trazabilidad depende del catálogo mutable | Decidir contrato; persistir relación si se aprueba |
| DT-06 | Corregida con `X-Expected-Version` en Series y PAC | Versión obsoleta devuelve 409 sin mutación | Acreditado en gate HTTP; falta aceptación UI manual |
| DT-07 | BaseUrl permite HTTP y cualquier host | SSRF/ambiente contradictorio | **Bloqueador sandbox:** allowlist HTTPS |
| DT-08 | Ambiente se infiere de BaseUrl | Dos fuentes si se añade enum sin retirar una | Usar URL canónica o una única representación aprobada |
| DT-09 | Test PAC no actualiza `UltimaTestConexion*`, pero el DTO los conserva | Estado visible vacío/engañoso | Eliminar del recorrido o registrar resultado seguro según decisión |
| DT-10 | Auditoría EF redacta secretos; reserva usa SQL directo y conflictos no generan bitácora propia | Actor/resultado/correlación incompletos | Evento mínimo sólo si T09/criterio lo exige |
| DT-11 | Reserva HTTP usa el mismo permiso administrativo que CRUD | Contrato operativo posiblemente incorrecto | VILO define; reutilizar permisos canónicos |
| DT-12 | Comentarios/textos aún dicen “interno” o “reactivación futura” | Handoff inconsistente | Limpiar con el cambio funcional relacionado |
| DT-13 | No hay inicialización controlada de folio productivo | Colisión/discontinuidad | Esperar dato Fiscal; script/migración revisable, no seed ficticio |
| DT-14 | `ParametroGlobal` no tiene `EmpresaId` | No acredita parámetros fiscales por empresa | Corregir alcance o diseñar sólo tras decisión |

## 6. Plan por fases

### Fase 0 - decisiones y datos

- **Alcance:** P01-P06, permisos, tipos fiscales, lugar de expedición y continuidad.
- **Esperado:** matriz aprobada; lo no aprobado queda diferido.
- **Archivos:** esta nota; ADR-0038 sólo tras decisión; matriz sin datos sensibles.
- **Migración/compatibilidad:** ninguna.
- **Pruebas:** revisar contratos/consumidores; no T12.
- **Riesgos:** convertir fixtures en política; mezclar propuestas con seguridad obligatoria.
- **Dependencias:** VILO P01/P02/P05/P06/permisos; Fiscal P02-P05/lugar/continuidad; TI P06/canal/sandbox.
- **Entrada/salida:** especificación + contraste / decisión, responsable y datos autorizados.
- **Commit:** `docs(fiscal): registrar decisiones pendientes de ADM-09`.

### Fase 1 - frontera de seguridad y ambiente

- **Alcance:** DT-01, DT-07 y negativos empresa/sucursal.
- **Esperado:** JWT prevalece; IDs ajenos fallan antes de consulta/escritura/PAC; sólo hosts HTTPS canónicos.
- **Archivos:** `Serie.cs`; handlers de `Compartido/Application/Administracion/Series/`; ambos endpoints; `ConfiguracionPac.cs` o validator; tests HTTP de Series/PAC.
- **Migración/compatibilidad:** no columna nueva al adoptar interfaz; validar bypass. Inventariar URLs personalizadas antes de allowlist.
- **Pruebas:** T03; empresa A no lista/obtiene/crea/edita/desactiva/reserva B; HTTP, localhost/IP/host alterno rechazados; UI segura.
- **Riesgos:** romper tooling con empresa arbitraria o proxy no inventariado.
- **Dependencia:** VILO confirma hosts/proxy.
- **Entrada/salida:** sin credenciales / frontera cerrada y cero llamadas externas en negativos.
- **Commits:** `fix(admin): aplicar contexto de empresa a series`; `fix(fiscal): restringir endpoints de proveedor autorizados`; `test(admin): cubrir aislamiento de series por empresa`.

### Fase 2 - selección y concurrencia de series

- **Alcance:** DT-02 a DT-04; P02/P03 sólo aprobadas.
- **Esperado:** exactamente una serie; cero/varias fallan sin incremento; alta concurrente no duplica; desactivación confirmada bloquea.
- **Archivos:** crear/reservar/desactivar serie, `CompartidoDbContext.cs`, migración Compartido, `SeriesEndpointsTests.cs`.
- **Migración/compatibilidad:** diagnóstico previo, resolución manual, índice parcial después; conservar historia; mantener OC/Póliza fuera si corresponde.
- **Pruebas:** T04/T06; ausente/inactiva/ambigua; dos altas/activaciones; carrera desactivar/reservar; tipos separados; global según política.
- **Riesgos:** locks amplios/deadlocks/constraint sobre datos incompatibles.
- **Dependencia:** P02/P03 VILO/Fiscal.
- **Entrada/salida:** F1 verde + decisiones / PostgreSQL reproducible y selección determinista.
- **Commits:** `fix(admin): rechazar seleccion ambigua de series`; `feat(admin): aplicar unicidad de serie activa`; `test(admin): cubrir concurrencia y desactivacion de series`.

### Fase 3 - continuidad, identidad y versión

- **Alcance:** P04/P05, DT-05/06/13.
- **Esperado:** identidad inmutable tras uso; versión obsoleta da 409; continuidad sólo con dato aprobado; relación estable.
- **Archivos:** dominio/commands/responses/endpoints/UI de Series; consumidores de Facturación; migraciones sólo si se persiste `SerieId`.
- **Migración/compatibilidad:** `serie_id` nullable y backfill sólo inequívoco; no recalcular folios; inicialización idempotente/auditable.
- **Pruebas:** T05/T07/T09; edición tras uso; versión; mismo/diferente payload; fallo no vuelve a reservar; histórico.
- **Riesgos:** cambio transversal/backfill ambiguo.
- **Dependencias:** P04/P05, continuidad Fiscal, decisión `SerieId` VILO.
- **Entrada/salida:** F2 estable / identidad y concurrencia optimista demostradas.
- **Commits:** `fix(admin): proteger identidad de series utilizadas`; `fix(admin): validar version al modificar series`; `feat(fiscal): relacionar reservas con su serie`; `test(fiscal): cubrir continuidad y reintentos de folio`.

### Fase 4 - configuración y rotación

- **Alcance:** P01 si se aprueba; DT-08/09; versión PAC; conservar anterior ante fallo.
- **Esperado:** con P01, sólo guardar candidato exacto probado; editar invalida. Sin P01, contrato/UI dicen guardar-luego-probar.
- **Archivos:** comandos/handlers/validator de configuración/test; endpoints; factory/adapter SDK; formulario, hooks, schemas/types y tests.
- **Migración/compatibilidad:** comprobante opaco sin persistencia sólo si una instancia; multinstancia requiere decisión. Reusar `Version`.
- **Pruebas:** T07/T08/T11; candidato exitoso/fallido/expirado/modificado; rotación fallida; CSD incompatible; sandbox en live; 409 UI.
- **Riesgos:** secretos en logs/cache; doble fuente ambiente; confundir ping con emisión.
- **Dependencia:** P01 y topología VILO/TI.
- **Entrada/salida:** allowlist + decisión / contrato coherente y sin secretos.
- **Commits:** `feat(fiscal): validar configuracion candidata`; `fix(fiscal): validar version al guardar configuracion`; `test(fiscal): cubrir rotacion y conflictos de configuracion`.

### Fase 5 - gate local integrado y custodia

- **Alcance:** T01-T11 aplicables en PostgreSQL desechable, migraciones, API/UI y T10 controlado.
- **Esperado:** persistencia tras reinicio, descifrado, idempotencia/carreras; ausencia de claves falla segura/recuperable.
- **Archivos:** `tools/validate-integration-isolated.*`, tests HTTP, runbook/evidencia; infraestructura sólo ante defecto real.
- **Migración/compatibilidad:** base vacía y copia sanitizada; restore/rollback cuando aplique.
- **Pruebas:** T01-T11; Network/consola/logs; dos empresas, dos sucursales, global y tipos fiscales.
- **Riesgos:** BD compartida o confundir build con ejecución; Docker/WSL inestable.
- **Dependencias:** runner sano; TI para Blob/Key Vault/identidad.
- **Entrada/salida:** fases previas / matriz esperado-obtenido y migraciones verificadas.
- **Commits:** `test(fiscal): ampliar gate integrado de ADM-09`; `docs(fiscal): registrar evidencia tecnica de ADM-09`.

### Fase 6 - sandbox real y aceptación

- **Alcance:** T12 y una emisión controlada desde Facturación.
- **Esperado:** conexión/emisión en `https://test.fiscalapi.com`, serie/folio/UUID/relación correctos, sin nueva reserva al reintentar; evidencia anonimizada.
- **Archivos:** sólo evidencia/handoff salvo defecto demostrado.
- **Migración/compatibilidad:** ninguna durante prueba.
- **Pruebas:** guardar/recargar, conexión, variante acordada, material inválido, logs y revisión Fiscal.
- **Riesgos:** secretos expuestos, fixture como real, variante incorrecta, aceptar sólo por HTTP 200.
- **Dependencias:** TI entrega canal/credenciales/CSD; Fiscal datos y validación; VILO autoriza.
- **Entrada/salida:** F5 verde + políticas/insumos / T12 con timestamp, versión, resultado, correlación no sensible y conformidad/pending.
- **Commit:** `docs(fiscal): registrar resultado sandbox de ADM-09`.

## 7. Matriz T01-T12

| Caso | Estado verificable | Fase/evidencia |
|---|---|---|
| T01 Persistencia | Código/migraciones existen; reinicio integrado no acreditado | F5: guardar/recargar/reiniciar/logs |
| T02 Emisor/contexto | Empresa canónica; falta lugar validado y empresa de Series | F1/F5 |
| T03 Permiso negativo | PAC/sucursal/empresa tienen casos compilados | F5 HTTP |
| T04 N concurrentes | Test de 50 existe; ejecución BD desechable no concluyente | F2/F5, conservar números |
| T05 Reintento/conflicto | Replay/body distinto de reserva definidos; ejecución pendiente | F5 |
| T06 Serie/desactivación | Ausente/inactiva y carrera definidas; ambigüedad depende de P02 | F2/F5 |
| T07 Guardado/edición | CSD inválido parcial; candidato/versiones no | F3/F4 |
| T08 Certificados/ambiente | CSD/sandbox parcial; allowlist/combinaciones pendientes | F1/F4/F5 |
| T09 Consumidor | Facturación usa reserva y conserva folio en reintentos; falta conectado | F3/F6 |
| T10 Custodia | Wiring presente; runtime/recuperación no | F5 + P06 |
| T11 UI conectada | Component/smoke no sustituyen API real/conflictos | F4/F5 |
| T12 Sandbox real | No ejecutado | F6 + insumos/revisión Fiscal |

## 8. Dependencias externas

### VILO

- P01-P06, permisos, hosts autorizados, topología multinstancia, persistencia de `SerieId` y ADR de custodia.

### Fiscal/Contabilidad

- Lugar de expedición, catálogo de series, global/específica, continuidad/reinicio y validación de T12.

### TI

- Canal seguro para ApiKey/CSD, conectividad, Blob/Key Vault/identidad, backup/restore del key ring y runner PostgreSQL estable.

### No bloquean construcción local

- Credenciales productivas, catálogo definitivo mientras se usen fixtures marcados, ni emisión/cancelación/REP/conciliación/retorno A+W fuera del cierre individual.

## 9. Gate mínimo para sandbox

- [x] DT-01 multiempresa corregida y cubierta en código; ejecución HTTP pendiente del runner.
- [x] DT-07 allowlist HTTPS aplicada; ejecución HTTP pendiente del runner.
- [ ] Migraciones aplicadas en BD desechable y API reiniciada.
- [ ] T01, T03, T04, T05, T06, T08 y T11 verdes en alcance aplicable.
- [ ] P02-P05 aprobadas o excepción temporal explícita para T12.
- [ ] P01 implementada o aceptación explícita de guardar-luego-probar.
- [ ] P06/T10 suficientes para el ambiente.
- [ ] Lugar, serie y continuidad de prueba entregados por Fiscal.
- [ ] ApiKey/CSD cargados por operador mediante canal seguro.

## 10. Resumen ejecutable para el chat implementador

1. Empezar por Fase 1: empresa de Series y allowlist HTTPS.
2. Probar cross-tenant con cero consulta/escritura/reserva y cero llamada PAC.
3. Pedir P02/P03 antes de tocar selección; después diagnosticar duplicados, rechazar ambigüedad y aplicar constraint sin borrar historia.
4. Probar carrera desactivar/reservar en PostgreSQL; el UPSERT del contador no basta.
5. Implementar P04/P05 y `SerieId` sólo con aprobación; reutilizar `Version`, auditoría y entidades.
6. P01 sólo con comprobante ligado al candidato; si se difiere, conservar honestamente guardar-luego-probar.
7. Ejecutar T01-T11 en runner desechable, reinicio y UI conectada; infraestructura fallida = no concluyente.
8. T12 sólo después del gate, secretos fuera de Git y validación Fiscal.

Los commits propuestos son neutrales y pequeños; ninguno declara cierre, aceptación ni producción lista.
