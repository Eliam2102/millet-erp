# F1-ADM-09 · Plan de implementación y cierre

**Fecha de corte:** 2026-09-29  
**Rama:** `feature/F1-ADM-09`  
**Estado:** plan listo para ejecución; cierre productivo condicionado a insumos seguros de Millet  
**Criterio rector:** reutilizar lo existente y agregar sólo las pruebas, ajustes y evidencia que demuestren el recorrido completo.

## 1. Objetivo y resultado observable

Configurar por empresa el PAC FiscalAPI, el CSD, las series y los parámetros fiscales sin mezclar datos entre empresas ni exponer secretos.

La funcionalidad queda **lista para UAT** cuando una empresa puede:

1. guardar y consultar su configuración FiscalAPI;
2. rotar ApiKey y CSD sin que ningún secreto regrese en respuestas o logs;
3. probar la conexión contra sandbox;
4. usar únicamente sus series y parámetros fiscales vigentes;
5. rechazar acceso cruzado, credenciales/CSD inválidos y operaciones sin permiso;
6. repetir una mutación con la misma `Idempotency-Key` sin duplicar efectos.

La configuración real de producción queda fuera del cierre técnico hasta recibir ApiKey, CSD y parámetros vigentes por un canal seguro.

## 2. Base existente que se integra

No se crea un módulo ni una pantalla nuevos. Se integran las piezas actuales:

| Pieza | Implementación existente | Uso en ADM-09 |
|---|---|---|
| Configuración PAC por empresa | `backend/src/Integraciones.Fiscal` | Alta, rotación, consulta y prueba de conexión |
| Cifrado de secretos | `FiscalSecretCipher` + ASP.NET Data Protection | ApiKey y material CSD cifrados en reposo |
| Validación CSD | `CsdValidador` | Rechazo temprano de certificado, llave o password inválidos |
| Adaptador PAC | SDK oficial FiscalAPI | Conexión y operaciones en sandbox |
| Series fiscales | Administración: API/UI de Series | Selección de serie vigente y segregada por empresa |
| Parámetros | Administración: API/UI de Parámetros | Configuración fiscal no secreta por empresa |
| Autorización | `integraciones.fiscal.leer` y `integraciones.fiscal.administrar` | Lectura y mutación separadas |
| Idempotencia | Middleware compartido (ADR-0020) | Protección de `PUT` de configuración |
| UI | `/admin/integraciones/fiscal`, `/admin/series`, `/admin/parametros` | Recorrido administrativo completo |

Decisiones aplicables: ADR-0007, ADR-0010, ADR-0011, ADR-0012, ADR-0020, ADR-0021, ADR-0037 y ADR-0038.

## 3. Alcance

### Incluido

- Configuración FiscalAPI por empresa en sandbox.
- ApiKey de entrada solamente; respuesta siempre enmascarada.
- Captura y rotación de CSD con validación criptográfica previa.
- Identidades de prueba permitidas únicamente con `test.fiscalapi.com`.
- Activación/desactivación y prueba de conexión.
- Validación de permisos, empresa del JWT e idempotencia en el borde HTTP.
- Series y parámetros ya existentes, verificados como parte del recorrido fiscal.
- Pruebas unitarias puntuales, pruebas API de integración y regresión frontend.
- Checklist manual de interfaz y evidencia sin secretos.

### Fuera de alcance

- Credenciales, certificados o timbrado productivos.
- Un segundo PAC, failover o abstracciones nuevas.
- Rediseñar las pantallas de Series o Parámetros.
- Construcción del XML CFDI, cancelación o descarga masiva.
- Automatización E2E con Playwright/Cypress: el repositorio no tiene ese stack instalado y no hace falta agregarlo para este cierre.
- Guardar secretos en Git, fixtures, capturas, documentación o variables visibles del frontend.

## 4. Plan por fases

### Fase 0 · Gate de entrada y seguridad

**Objetivo:** congelar el contrato y evitar trabajar con secretos reales.

- Confirmar empresa sandbox y usuario con permisos de lectura/administración.
- Usar credenciales y CSD de prueba; no pedir ni copiar datos productivos.
- Verificar que Data Protection tenga almacenamiento persistente configurado en el ambiente objetivo.
- Resolver antes de producción la discrepancia documental sobre custodia del CSD: ADR-0038 describe CSD en Key Vault, mientras el código vigente lo cifra en la base con Data Protection. El cierre sandbox puede continuar con el código existente; producción requiere decisión explícita o ADR de reemplazo.

**Salida:** insumos sandbox identificados, ningún secreto en archivos versionados y decisión productiva registrada como dependencia.

### Fase 1 · Contrato HTTP e integración backend

**Objetivo:** demostrar el flujo real desde endpoints, no sólo handlers aislados.

- Crear pruebas de integración para `GET`, `PUT` y `POST .../test` de configuración fiscal.
- Cubrir permiso de lectura, permiso de administración y ausencia de permiso.
- Cubrir empresa correcta y rechazo cross-tenant.
- Cubrir `Idempotency-Key` ausente, válida y repetida.
- Confirmar que GET/PUT nunca devuelven ApiKey, CSD, password, hashes o ciphertext.
- Mantener las pruebas de conexión deterministas con un doble del cliente FiscalAPI; la llamada real queda para el gate sandbox.

**Salida:** contrato HTTP protegido y reproducible en CI.

### Fase 2 · Recorrido fiscal por empresa

**Objetivo:** unir PAC, CSD, Series y Parámetros usando las capacidades existentes.

- Configurar FiscalAPI sandbox para empresa A.
- Capturar/rotar CSD de prueba y validar que una carga inválida no persista cambios.
- Crear o seleccionar una serie fiscal vigente de empresa A.
- Crear o seleccionar los parámetros fiscales requeridos de empresa A.
- Cambiar a empresa B y comprobar que no puede leer o mutar la configuración de A.
- Verificar que series y parámetros de A no aparecen como datos utilizables de B.

**Salida:** recorrido nominal y negativo por empresa, sin duplicar lógica entre módulos.

### Fase 3 · Frontend y seguridad visible

**Objetivo:** asegurar que la interfaz conserva el contrato seguro.

- Extender las pruebas del formulario sólo donde falte cobertura observable: enmascarado, CSD, error de guardado y control por permiso.
- Confirmar que cambiar a modo live limpia identidades sandbox.
- Confirmar que `Probar conexión` usa la configuración persistida y queda deshabilitado con cambios sin guardar.
- Verificar que archivos y passwords no reaparecen después de guardar o recargar.
- Ejecutar typecheck, lint y tests focalizados.

**Salida:** UI sin regresiones y sin secretos rehidratados.

### Fase 4 · Sandbox y evidencia de cierre

**Objetivo:** realizar una única prueba controlada contra FiscalAPI sandbox.

- Guardar configuración con credenciales de prueba por canal seguro.
- Ejecutar `Probar conexión` y registrar sólo estado, código, duración y correlation/request id no sensible.
- Revisar logs y Network para confirmar ausencia de ApiKey, CSD y password.
- Adjuntar evidencia redactada del caso nominal, negativo, segregación y regresión.
- Documentar recuperación/rotación y dependencia pendiente para producción.

**Salida:** ADM-09 lista para UAT en sandbox; producción permanece bloqueada nominalmente por insumos y decisión de custodia del CSD.

## 5. Commits propuestos

Los commits se ejecutan sólo después de cada fase verde. No se incluye commit automático en este plan.

| Orden | Commit | Propósito |
|---:|---|---|
| 1 | `test(fiscal): cubrir configuracion PAC por HTTP` | Permisos, tenant, idempotencia y no exposición de secretos |
| 2 | `test(fiscal): cerrar regresion del formulario PAC` | Casos visibles faltantes sin introducir E2E nuevo |
| 3 | `fix(fiscal): cerrar brechas detectadas por integracion` | Sólo si las pruebas descubren una falla real; omitir si no hay cambio productivo |
| 4 | `docs(fiscal): registrar evidencia sandbox ADM-09` | Resultado, comandos, evidencia redactada y bloqueo productivo |

## 6. Archivos previstos y por qué

### Cambio mínimo esperado

| Archivo | Motivo |
|---|---|
| `backend/tests/Api.IntegrationTests/IntegracionesFiscal/ConfiguracionPacEndpointsTests.cs` | Nueva cobertura del borde HTTP completo |
| `frontend/src/features/integraciones-fiscal/components/ConfiguracionPacForm.test.tsx` | Completar regresiones visibles que hoy no están cubiertas |
| `docs/modulos/integraciones-fiscal/03-f1-adm-09-plan-implementacion.md` | Fuente versionada del plan y alcance |
| `docs/handoff/30-evidencia-f1-adm-09.md` | Evidencia final, comandos y resultado sandbox |

### Sólo si una prueba demuestra una brecha

| Archivo | Motivo posible |
|---|---|
| `backend/src/Api/Endpoints/IntegracionesFiscal/IntegracionesFiscalEndpoints.cs` | Corregir contrato, autorización o metadata HTTP |
| `backend/src/Integraciones.Fiscal/Application/Configuracion/**` | Corregir validación/orquestación en el punto compartido |
| `backend/src/Integraciones.Fiscal/Domain/ConfiguracionPac.cs` | Corregir una invariante de dominio, no una particularidad de UI |
| `backend/src/Integraciones.Fiscal/Infrastructure/Cifrado/**` | Corregir cifrado/validación demostrablemente defectuosos |
| `frontend/src/features/integraciones-fiscal/components/ConfiguracionPacForm.tsx` | Corregir comportamiento observable fallido |
| `frontend/src/features/integraciones-fiscal/api/**` | Corregir contrato cliente-servidor |

No se debe editar manualmente `frontend/src/routeTree.gen.ts`; sólo se regenera si cambia una ruta fuente.

## 7. Pruebas automatizadas

### Backend unitarias

- Creación exige ApiKey.
- Rotación idempotente no cambia timestamps con el mismo secreto.
- CSD completo válido se acepta; password, par o vigencia inválidos se rechazan.
- Identidades sandbox se rechazan fuera de `test.fiscalapi.com`.
- Cambio a live limpia identidades sandbox.
- Cifrado round-trip, separación por purpose y hash de detección.

Comando:

```powershell
dotnet test backend/tests/Integraciones.Fiscal.UnitTests/Millet.Integraciones.Fiscal.UnitTests.csproj --no-restore
```

### Backend integración HTTP

- `GET` sin autenticar → 401.
- Usuario sin `integraciones.fiscal.leer` → 403.
- Usuario lector obtiene configuración enmascarada.
- Usuario lector no puede guardar/probar → 403.
- Administrador sin `Idempotency-Key` en `PUT` → 400.
- Administrador guarda configuración sandbox → 200.
- Repetición con la misma key idempotente → misma respuesta/efecto.
- Empresa A intentando operar sobre B → 403 y sin cambio persistido.
- Respuesta serializada no contiene ApiKey, CSD, password, hash ni ciphertext.
- Prueba de conexión con doble: éxito y error normalizado sin filtrar credenciales.

Comando focalizado:

```powershell
dotnet test backend/tests/Api.IntegrationTests/Millet.Api.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~IntegracionesFiscal
```

### Frontend

- Toggle sandbox/live.
- No renderizar secretos existentes; placeholder enmascarado.
- CSD se envía sólo al capturar/rotar y se limpia del estado tras éxito.
- Botón de prueba bloqueado con cambios sin guardar.
- Campos de identidad sólo en sandbox.
- Mutaciones ocultas o deshabilitadas sin permiso administrar.
- Error de API conserva un mensaje útil sin mostrar el payload sensible.

Comandos:

```powershell
npm --prefix frontend run test -- ConfiguracionPacForm
npm --prefix frontend run typecheck
npm --prefix frontend run lint
```

### Regresión de solución

```powershell
dotnet build backend/Millet.sln --no-restore
dotnet test backend/Millet.sln --no-build --no-restore
npm --prefix frontend run test
npm --prefix frontend run build
```

## 8. Verificación manual en interfaz

Ejecutar con DevTools abiertos y datos exclusivamente sandbox:

1. Ingresar como lector: la ruta abre, los datos aparecen enmascarados y no hay acciones de mutación disponibles.
2. Ingresar como administrador y seleccionar empresa A.
3. Activar modo sandbox; comprobar Base URL `https://test.fiscalapi.com` e identidades de prueba.
4. Capturar ApiKey y CSD de prueba; guardar y recargar.
5. Confirmar que ApiKey, `.cer`, `.key` y password no reaparecen en UI, respuesta, consola, Network ni logs.
6. Ejecutar `Probar conexión`; comprobar resultado, duración y timestamp.
7. Ir a Series y verificar una serie fiscal vigente para A.
8. Ir a Parámetros y verificar los parámetros fiscales de A.
9. Cambiar a empresa B; comprobar ausencia de configuración, series y parámetros de A.
10. Intentar URL/API de A con sesión en B; comprobar 403.
11. Probar CSD/password inválido; comprobar mensaje controlado y que la configuración anterior permanece intacta.
12. Volver de sandbox a live; comprobar que desaparecen identidades de prueba y que la UI exige una rotación consciente antes de operación real.

## 9. Definition of Done

- Build, lint, unitarias, integración HTTP y regresión frontend en verde.
- Caso nominal, negativo, cross-tenant e idempotente documentados.
- Ningún secreto en Git, salida de pruebas, capturas, respuestas o logs.
- Series y parámetros fiscales verificados por empresa.
- Prueba sandbox controlada exitosa o bloqueo externo con dueño y evidencia.
- Revisión cruzada del PR.
- Decisión de custodia productiva del CSD registrada antes de habilitar live.

## 10. Estado inicial de esta rama

Antes de ejecutar el plan existe una modificación ajena en `frontend/src/routeTree.gen.ts`. Debe preservarse y no incluirse en los commits de ADM-09 salvo que se demuestre que proviene de una regeneración requerida por una ruta fuente cambiada.
