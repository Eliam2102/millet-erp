# F1-ADM-09 · Evidencia de seguridad y gate de cierre

**Fecha de corte:** 2026-09-30
**Rama:** `feature/F1-ADM-09`  
**Alcance de este documento:** evidencia parcial del primer corte; no contiene credenciales, CSD, payloads ni valores de configuración sensibles. El plan contractual actualizado está en `docs/modulos/integraciones-fiscal/03-f1-adm-09-plan-implementacion.md`.

## Corrección de alcance del 29-sep-2026

El commit `4103239` no cierra por sí solo ADM-09. Reforzó PAC/CSD, autorización por empresa, no exposición de secretos y regresiones del formulario. La ficha original exige además alcance autorizado por sucursal, separación demostrada por tipo, concurrencia, serie desactivada, emisor fiscal, tres estados visibles del CSD, mensajes integrados, evidencia UI/API y PR en `main`.

Hasta completar esas fases, el estado oficial es **implementación parcial**. Timbrado, cancelación y complementos existentes son contexto consumidor, no entregables nuevos de ADM-09.

## Resultado

- **Cifrado y CSD:** evidencia estática y pruebas focalizadas en verde.
- **Persistencia de Data Protection en QA/producción:** preparada en código e infraestructura, pero no se verificó un despliegue real desde este entorno.
- **Segregación PAC/series:** el modelo incluye `EmpresaId`; la validación HTTP/cross-tenant corresponde a las pruebas de integración del cierre.
- **Parámetros:** la implementación actual es global o por módulo, no por empresa. Por tanto, no se puede acreditar el requisito del plan de "parámetros fiscales por empresa" con el modelo vigente.
- **Prueba real FiscalAPI sandbox:** **bloqueada**; no se encontraron credenciales ni CSD de prueba disponibles de forma segura en el entorno. No se intentó una llamada con datos inventados.
- **Producción:** **bloqueada** hasta recibir insumos por canal seguro y resolver formalmente la custodia del CSD.
- **Alcance por sucursal:** implementado con asignaciones reales `UsuarioSucursal` en listado, detalle, mutaciones y reserva HTTP; las series globales sólo se administran con permiso corporativo.
- **Estado visible del CSD:** `NotBefore`/`NotAfter` se persisten como metadatos y la UI proyecta `Vigente`, `ProximoAVencer` y `Vencido` con umbral inclusivo de 30 días.
- **Reserva concurrente/tipo:** regresiones de tipo, desactivación e histórico añadidas; la prueba HTTP de 50 reservas se conserva, pero su ejecución sigue condicionada al runner con BD desechable.
- **PR/main:** rama remota disponible; integración final pendiente.

## Evidencia verificable

### Data Protection y secretos

- `backend/src/Api/Program.cs` registra Data Protection con application name `Millet.ERP` y rotación de 90 días. Si existen conjuntamente `DataProtection:BlobConnString` y `DataProtection:KeyIdentifier`, persiste el key ring en `dataprotection-keys/millet-erp.xml` y lo protege con Azure Key Vault; sin ambos valores usa el fallback local del framework.
- `infra/modules/keyvault.bicep` declara `dataprotection-master-key` RSA 2048 limitada a `wrapKey`/`unwrapKey`.
- `infra/modules/storage.bicep` declara el contenedor `dataprotection-keys` con `publicAccess: None`.
- `infra/modules/appservice.bicep` inyecta ambos settings y asigna a la identidad administrada los roles de criptografía de Key Vault y acceso a Blob.
- `FiscalSecretCipher` usa ASP.NET Data Protection con purpose versionado; la persistencia de configuración usa columnas `bytea` para ApiKey, certificado, llave y password. Los DTO/endpoints deben seguir cubiertos por las pruebas HTTP de no exposición.

Esta evidencia prueba el *wiring* versionado, no que el key ring, la key y los roles existan o sean accesibles en un ambiente desplegado. Antes de UAT se debe validar arranque, escritura/lectura del ring y descifrado tras reinicio de instancia.

### Segregación de datos administrativos

- `ConfiguracionPac` se persiste con `EmpresaId` y un índice único `(EmpresaId, Proveedor)`.
- `Serie` contiene `EmpresaId`; reserva y búsqueda filtran explícitamente por empresa.
- `ParametroGlobal` no contiene `EmpresaId`: su alcance documentado en código es global o por módulo. La pantalla/API existente de parámetros no demuestra aislamiento por empresa.

Consecuencia: el cierre puede reutilizar parámetros verdaderamente globales, pero debe corregir el plan/contrato o implementar otro almacenamiento antes de afirmar que los parámetros fiscales son distintos para empresas A y B.

## Comandos ejecutados

### Emisor fiscal y prueba de conexión PAC

- El snapshot de emisión reutiliza `Empresa` como única fuente de RFC, razón social, régimen fiscal y código postal; no se creó otra entidad de emisor.
- `Probar conexión` quedó como operación de sólo lectura: no actualiza `UltimaTestConexionAt` ni `UltimaTestConexionExitosa`.
- Los dobles deterministas cubren éxito, configuración ausente/inactiva (422), timeout, 503 y rechazo HTTP conocido con mensajes accionables que no propagan el detalle interno del proveedor.

```powershell
dotnet test backend/tests/Integraciones.Fiscal.UnitTests/Millet.Integraciones.Fiscal.UnitTests.csproj --no-restore --filter "FullyQualifiedName~TestConexionPacHandlerTests"
```

Resultado: **6 pasaron, 0 fallaron, 0 omitidas**.

```powershell
dotnet test backend/tests/Facturacion.UnitTests/Millet.Facturacion.UnitTests.csproj --no-restore --filter "FullyQualifiedName~EmisorSnapshotTests"
```

Resultado: **2 pasaron, 0 fallaron, 0 omitidas**.

```powershell
dotnet test backend/tests/Facturacion.UnitTests/Millet.Facturacion.UnitTests.csproj --no-build --no-restore --filter "FullyQualifiedName~EmitirFacturaVentaHandlerTests|FullyQualifiedName~AnticiposHandlerTests|FullyQualifiedName~CartaPorte"
```

Resultado: **41 pasaron, 0 fallaron, 0 omitidas**.

La compilación aislada posterior de `Api.IntegrationTests` terminó correctamente con **0 warnings y 0 errores**; valida la composición del código HTTP, pero no sustituye la ejecución contra PostgreSQL pendiente.

### Implementación y regresión ADM-09

- Se añadieron 8 casos HTTP para autenticación, RBAC, idempotencia, enmascarado, cross-tenant y doble determinista del PAC.
- Se corrigió el borde HTTP para validar la empresa actual antes de `GET` y `POST .../test`; antes, el `GET` ajeno terminaba en 404 y la prueba podía alcanzar el SDK con otra empresa.
- Se añadieron regresiones del formulario para no rehidratar secretos, limpiar el CSD tras guardar, no reflejar detalles sensibles de error y respetar el modo de sólo lectura.

```powershell
dotnet test backend/tests/Integraciones.Fiscal.UnitTests/Millet.Integraciones.Fiscal.UnitTests.csproj --no-restore
```

Resultado integrado del 30-sep: **183 pasaron, 0 fallaron, 0 omitidas**.

```powershell
dotnet build backend/tests/Api.IntegrationTests/Millet.Api.IntegrationTests.csproj --no-restore -p:BaseOutputPath=<directorio-temporal>
```

Resultado integrado del 30-sep: **compilación exitosa, 0 warnings, 0 errores**, incluyendo las regresiones finales de Series y PAC. La ejecución HTTP quedó bloqueada: el runner oficial requiere una BD desechable y el script Bash no pudo crearla en este host (`E_ACCESSDENIED`).

`npm --prefix frontend run typecheck:test` terminó verde. Limitando Vitest a un worker para evitar el bloqueo del pool en este host, `ConfiguracionPacForm` terminó con **10/10** pruebas verdes y `SeriesPage.smoke` con **3/3**. ESLint focalizado terminó sin errores; el único warning detectado fue corregido después. El build normal de la solución encontró una DLL del API bloqueada por una instancia previa; la compilación aislada posterior terminó con **0 warnings y 0 errores** y valida el código afectado.

Se intentó ejecutar el gate HTTP focalizado dos veces. El runner oficial `tools/validate-integration-isolated.sh` no pudo iniciar Bash/WSL (`Bash/Service/CreateInstance/E_ACCESSDENIED`). En el segundo intento se creó un PostgreSQL 17 temporal en Docker y en un puerto distinto al de desarrollo, pero la API de Docker dejó de responder incluso a `inspect`, `stop` y `rm`; el proceso se interrumpió sin obtener resultados de pruebas. El gate HTTP se registra como **no concluyente por infraestructura del host**, no como verde ni como fallo funcional. El contenedor temporal se creó con `--rm`; no se tocó `millet-dev-postgres`, aunque Docker debe recuperar disponibilidad para confirmar que la limpieza automática terminó.

### Series, sucursales y emisor

- El endpoint público de reserva exige `admin.series.gestionar`; las invocaciones internas por MediatR permanecen sin acoplarse a HTTP.
- La UI muestra y filtra por sucursal reutilizando el catálogo existente; `SucursalId = null` aparece como `Global`.
- Empresa es la fuente única de RFC, razón social, régimen y CP para el snapshot del emisor.

```powershell
dotnet test backend/tests/Facturacion.UnitTests/Millet.Facturacion.UnitTests.csproj --no-restore --filter "FullyQualifiedName~EmisorSnapshot|FullyQualifiedName~EmitirFacturaVentaHandler|FullyQualifiedName~EmitirFacturaAnticipoHandler|FullyQualifiedName~EmitirCartaPorteHandler"
```

Resultado integrado: **7 pasaron, 0 fallaron, 0 omitidas**.

### Handoff para migración, reinicio y UAT

El código queda **listo para aplicar la migración en un ambiente controlado, reiniciar el API y comenzar UAT de interfaz**, sujeto a las siguientes verificaciones operativas:

1. confirmar que Docker/runner vuelve a responder y ejecutar el gate HTTP focalizado contra una BD desechable;
2. aplicar `20260930043912_CsdVigenciaMetadatos` y verificar las columnas `csd_not_before` y `csd_not_after`;
3. reiniciar el API y comprobar `/health/live`, lectura/descifrado de la configuración PAC existente y persistencia del key ring;
4. recorrer UAT con usuario autorizado/no autorizado, dos sucursales, serie global y tipos Factura/Nota de crédito;
5. mantener la prueba real de FiscalAPI bloqueada hasta recibir ApiKey y CSD sandbox por canal seguro.

La actualización local de `docs/handoff/31-configuracion-local-entra-id.md` se conserva: documenta el arranque estable con PostgreSQL en `127.0.0.1` mediante User Secrets y no debe descartarse durante el handoff.

### Seguridad focalizada

```powershell
dotnet test backend/tests/Integraciones.Fiscal.UnitTests/Millet.Integraciones.Fiscal.UnitTests.csproj --no-restore --filter "FullyQualifiedName~FiscalSecretCipherTests|FullyQualifiedName~CsdValidadorTests|FullyQualifiedName~ConfiguracionPacTests"
```

Resultado: **46 pasaron, 0 fallaron, 0 omitidas** (`net10.0`, duración reportada por el runner: 2 s).

También se inspeccionaron, sin imprimir valores, los nombres de variables de entorno relacionados con Fiscal/PAC/CSD/Data Protection/Key Vault. No se encontró una variable identificable como credencial sandbox o CSD de prueba. El único nombre coincidente fue una variable interna del sandbox de Codex, ajena a FiscalAPI.

## Gate controlado para sandbox

Cuando un operador autorizado entregue por canal seguro una ApiKey y CSD **de prueba**:

1. confirmar empresa y usuario sandbox con permisos de lectura/administración;
2. verificar que la URL sea `https://test.fiscalapi.com`;
3. capturar los secretos sólo en la UI/secret store autorizado, nunca en Git, comandos, tickets o este documento;
4. guardar, recargar y confirmar que ApiKey, certificado, llave y password no regresan en UI, JSON, consola, Network ni logs;
5. ejecutar una sola vez `Probar conexión` y registrar únicamente resultado, duración, timestamp y correlation/request id no sensible;
6. repetir el caso negativo de CSD/password inválido y comprobar que la configuración anterior permanece;
7. ejecutar la prueba cross-tenant de PAC y series. No declarar segregación de parámetros mientras sigan siendo globales.

## Bloqueos productivos

1. **Custodia CSD:** ADR-0038 indica CSD como certificado en Key Vault; el código vigente almacena certificado, llave y password cifrados en PostgreSQL mediante Data Protection. Sandbox puede continuar con el código vigente, pero producción requiere una decisión explícita (actualizar/reemplazar el ADR o cambiar la implementación).
2. **Insumos:** faltan ApiKey, CSD y datos fiscales productivos entregados mediante un canal seguro. No deben copiarse a archivos versionados, fixtures, logs ni evidencia.
3. **Ambiente:** falta evidencia runtime de persistencia/recuperación del key ring y permisos efectivos de la identidad administrada.
4. **Parámetros por empresa:** el modelo actual no soporta esa afirmación; requiere una decisión de alcance antes del cierre.

## Recuperación y rotación

- Conservar soft-delete y purge protection de Key Vault, y protección/recuperación del blob del key ring.
- Rotar ApiKey/CSD desde el flujo administrativo y verificar una prueba de conexión antes de retirar el material anterior.
- No eliminar versiones de key necesarias para descifrar datos existentes. Una pérdida del key ring o de la key vuelve irrecuperables los ciphertexts hasta restaurarlos.
- Si se compromete material criptográfico, rotar la key maestra y también cada secreto operativo; registrar sólo identificadores y timestamps no sensibles.
