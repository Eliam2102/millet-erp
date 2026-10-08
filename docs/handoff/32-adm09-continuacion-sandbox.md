# ADM-09 — continuación sandbox y pendientes

Fecha: 1-oct-2026. Chat implementador: `01a0ef6b-a9b6-78e2-a062-b018d7244567`.

## Punto exacto de pausa

El usuario prueba `/admin/integraciones/fiscal`. La configuración guardada muestra CSD configurado y vigente; los campos de API key, certificado, llave y contraseña vacíos tras recargar son intencionales. No acreditan pérdida de secretos.

Al pulsar Probar conexión, la UI muestra timeout y HTTP 0. El log del backend identifica la causa concreta: `FiscalApiSdkAdapter está deshabilitado (FiscalApiSdkAdapterOptions.Disabled=true)`, lanzada en `FiscalApiSdkClientFactory.GetClientAsync`. La solicitud termina en milisegundos; no constituye un timeout real del proveedor ni acredita una conexión externa.

El adaptador YA existe: no falta implementarlo. Falta habilitar su configuración local y corregir la clasificación del error.

## Próximos pasos

1. Revisar la precedencia efectiva de configuración y habilitar localmente `IntegracionesFiscal:Sdk:Disabled=false` en el almacén local apropiado. Confirmar presencia de `IntegracionesFiscal:Sdk:TenantKey` sin imprimir su valor. Es el TID de FiscalAPI, distinto del tenant EntraID.
2. Reiniciar el backend conservando EntraID y comprobar `/health/ready`.
3. Probar la configuración persistida contra sandbox y registrar resultado real. No volver a capturar secretos salvo rotación intencional.
4. Si falla, distinguir configuración local, descifrado, DNS/TLS/conectividad, autenticación y rechazo del proveedor. Una consulta curl desde el sandbox restringido falló: no acredita que el host ni FiscalAPI estén caídos; repetir en el contexto de red adecuado si es necesario.
5. Corregir el manejo del adaptador deshabilitado: actualmente el catch genérico devuelve StatusCode 0 y `TestConexionPacHandler` traduce todo 0 como timeout. Mostrar un error accionable de integración deshabilitada, sin detalle interno ni secretos. Añadir regresión focalizada.
6. Verificar el formulario después de guardar: `onSuccess` limpia drafts pero no llama a `form.reset`; `isDirty` puede mantener Probar conexión deshabilitado. Corregir y probar si se reproduce. Escribir API key sí debe deshabilitar la prueba hasta guardar; los campos vacíos deben conservar secretos persistidos.
7. Completar UAT conectada y luego emisión sandbox controlada con resultado UUID/CFDI almacenado. Probar conexión no demuestra timbrado.

## Estado acreditado

- Gate HTTP PostgreSQL desechable: 37/37; incluye 50 reservas concurrentes, idempotencia, carrera desactivar/reservar y conflictos de versiones Series/PAC.
- Fiscal unit tests: 195/195; compilación integración sin errores/warnings; typecheck frontend aprobado.
- Docker usado: Docker Desktop, contexto `desktop-linux`. No usar el daemon propio de WSL2.
- Base local: CsdVigenciaMetadatos aplicada. El coordinador aplicó ProveedorRfcUnico y AlignComprasSnapshotForNet10; backend Healthy y frontend activo.
- Cambios recientes pendientes de commit/push/revisión. No declarar cierre ni integración main.

## Decisiones funcionales todavía abiertas

- P01: candidato probado antes de guardar; hoy guardar → probar.
- P02: unicidad de serie fiscal activa y selección ambigua.
- P03: fallback global frente a serie específica.
- P04: folio inicial y continuidad fiscal.
- P05: inmutabilidad posterior al uso y persistencia de SerieId.
- P06: custodia productiva y coherencia con ADR; recuperación runtime del key ring pendiente.

Ver `docs/modulos/integraciones-fiscal/04-adm09-especificacion-v2-brecha.md` y `docs/handoff/30-evidencia-f1-adm-09.md`. La actualización del 1-oct prevalece sobre los bloqueos históricos Docker del documento.

No copiar API keys, TID, CSD, contraseñas ni payloads sensibles a notas, Git o evidencia. Este handoff no modifica código ni configuración.

## Actualización 1-oct-2026 — pasos 1, 5 y 6 implementados

- **Config local (paso 1):** `IntegracionesFiscal:Sdk:Disabled=false` en user-secrets de `Millet.Api` (`TenantKey` ya existía; no se imprimió). Requiere reiniciar el backend y revisar `/health/ready`.
- **Clasificación del error (paso 5):** `FiscalApiSdkClientFactory` lanza `IntegracionFiscalNoHabilitadaException` (Domain, 422) cuando `Disabled=true` o falta `TenantKey`. `FiscalApiSdkAdapter.PingAsync` la atrapa y devuelve `StatusCode 501`; `TestConexionPacHandler` muestra «La integración fiscal no está habilitada en este ambiente. Contacta a TI.» sin detalle interno. Antes caía en el catch genérico (`StatusCode 0`) y se presentaba como timeout.
- **Formulario (paso 6):** `ConfiguracionPacForm` llama `form.reset(values)` en `onSuccess` del guardado, para limpiar `isDirty` y habilitar «Probar conexión».
- **Pruebas:** `Integraciones.Fiscal.UnitTests` 196/196 (nuevo caso 501 en `TestConexionPacHandlerTests`).
- **Pendiente:** reiniciar backend y probar contra sandbox (paso 3-4), UAT conectada, emisión sandbox. P01–P06 siguen abiertas. Sin commit.
