# ADR-0037: Cifrar secretos operativos con ASP.NET DataProtection y DEK en Key Vault

- **Estado**: Aceptada
- **Fecha**: 2026-05-24 (propuesta); aceptada 2026-05-25 (PR-3 de Integraciones.Fiscal)
- **Decisores**: Eduardo Paredes
- **Etiquetas**: seguridad, integraciones, key-vault, postgres

## Contexto y problema

Los módulos `Millet.Integraciones.Fiscal` y `Millet.Integraciones.Mailbox`
necesitan persistir credenciales operativas (`ApiKey` de FiscalAPI,
`ClientSecret` del App Registration de Entra ID, etc.) que el admin
puede capturar/rotar desde la UI **sin redeploy**.

Hasta hoy estos secretos viven en `appsettings.json` y son inyectados
por Bicep como Key Vault references al App Service. Eso funciona para
**deploy-time config** (operador con acceso a Bicep + KV), pero no para
**runtime config** (admin del ERP en navegador).

Si pasamos a admin-configurable, los secretos van a vivir en Postgres.
La pregunta es **cómo cifrarlos** para que:

- DBA y backups no los vean en cleartext.
- Logs y queries de EF no los expongan por accidente.
- La rotación de la llave maestra sea operable sin re-cifrar manualmente
  cada fila.
- El stack siga siendo portable (no acoplarse a una extensión Postgres
  exótica).

## Drivers de la decisión

- **Compliance básico**: cero plaintext de credenciales en BD.
- **Operabilidad**: rotación de la llave maestra debe poder hacerse desde
  Azure sin tocar el código.
- **Standardización**: usar un mecanismo que .NET reconozca como
  canónico, no inventar primitivos criptográficos custom.
- **DX local**: dev sin Azure debe poder correr el módulo (cifrado local
  de fallback).
- **Multi-purpose**: el mismo mecanismo aplica a `Integraciones.Fiscal`,
  `Integraciones.Mailbox`, y a futuros módulos que persistan credenciales
  (Bancos, SAT directo, ERPs de clientes).

## Opciones consideradas

1. **ASP.NET DataProtection con DEK (Data Encryption Key) en Azure Key
   Vault**, ring de keys persistido en Azure Blob Storage. La app conoce
   una sola key (URI del KV) y descifra el ring en arranque. Los
   secretos individuales se cifran con `IDataProtector` y se persisten
   como `bytea` en columnas Postgres normales.
2. **`pgcrypto` con `pgp_sym_encrypt`** — extensión Postgres que cifra a
   nivel columna. La key viaja como parámetro de cada query/función.
3. **KV-per-secret** — cada credencial es un secret separado en KV. La
   tabla solo guarda referencias (`vault_name`, `secret_name`). La
   managed identity de la app necesita `Get` y `Set`.
4. **Plaintext con RBAC estricto + audit** — almacenar en claro y
   confiar en controles de acceso de Postgres + RLS + auditoría de
   accesos.

## Decisión

**Opción 1 — ASP.NET DataProtection con DEK en Key Vault.**

Detalles:

- DEK vive en `kv-millet-{env}-mxc-01` como key tipo RSA 2048 (no como
  secret — usa `wrapKey`/`unwrapKey`).
- Ring de keys de DataProtection vive en Azure Blob Storage,
  container `dataprotection-keys`, blob `millet-erp.xml`.
- App Service tiene managed identity con permisos `wrapKey` + `unwrapKey`
  sobre la key y `Read`/`Write` sobre el container.
- Cada módulo declara su propio `IDataProtector` con purpose string
  versionado (ej. `Integraciones.Fiscal.ApiKey.v1`,
  `Integraciones.Mailbox.ClientSecret.v1`).
- Columnas en Postgres son `bytea`, sin extensiones especiales.
- Dev local sin KV: DataProtection cae automáticamente a
  `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys` (filesystem) —
  cero fricción.
- Rotación de DEK: rotar versión en KV; ring re-encripta on-demand. Las
  columnas Postgres no requieren re-cifrado masivo porque el ring soporta
  varias keys activas durante la transición.

## Consecuencias

**Positivas**

- Un solo secret en KV gestiona el cifrado de N credenciales. No se
  ensucia el listado de KV con cada credencial individual.
- Patrón canónico de .NET — bien documentado en MSDN y en libros de
  referencia.
- Local dev no requiere KV ni Azure — DataProtection cae a FS.
- Postgres queda agnóstico: solo `bytea` columns, sin pgcrypto.
- Rotación del DEK es operación de KV (1 click). Los datos cifrados
  con la key vieja siguen siendo descifrables mientras la versión
  vieja exista (purge protection).
- Logs y queries EF nunca ven plaintext — los `bytea` son opacos.

**Negativas**

- Si el KV del ambiente se pierde completamente (storage account +
  vault), **todos los ciphertext son irrecuperables**. Mitigación:
  KV soft-delete + purge protection (ya configurado por Bicep);
  storage account con redundancia GRS para el container del ring.
- Acoplamiento a Azure. Si Millet migra de Azure a otro cloud, el
  cifrado debe reconfigurarse. Mitigación: el patrón es `IDataProtector`
  abstracto — cambiar provider (AWS KMS, GCP KMS) es modificación
  contenida en `Program.cs`.
- Cada módulo debe usar el purpose string correcto. Un cambio
  accidental rompe descifrado de las columnas ya cifradas. Mitigación:
  constantes en código, no strings literales sueltos, y test que
  valida round-trip al iniciar.

## Descartadas

**Opción 2 — pgcrypto.** Acopla al backend a una extensión Postgres
específica. Las queries cambian (`SELECT pgp_sym_decrypt(api_key, $key)`
en cada lectura), las migrations cambian, EF Core necesita value
converters custom. El key management acaba siendo igual de complejo
que con DataProtection (¿dónde vive la key? ¿KV? ¿env var?). Cero
ventaja sobre la Opción 1, con más fricción.

**Opción 3 — KV-per-secret.** En el papel parece más seguro porque KV
es el source-of-truth de cada credential individual. En la práctica:

- La managed identity necesita permisos `Set` en KV, ampliando blast
  radius de un compromiso de la app.
- Cada credencial pollutes el listado de KV (con N empresas × M módulos,
  se llega a cientos de entries rápidamente).
- Latencia: cada lectura es una llamada HTTPS a KV (≈50ms). Con caché
  custom se mitiga, pero entonces tienes lo peor de los dos mundos.
- Test connection captura una credencial transitoria — con
  KV-per-secret tendrías que crearla, probarla, y borrarla si falla.
- El admin UI se vuelve un "wrapper de KV management" en lugar de un
  CRUD limpio sobre Postgres.

Para escenarios con compliance MUY estricto (PCI, HIPAA) esta opción
volvería a la mesa. No es el caso de Millet hoy.

**Opción 4 — Plaintext.** Descartada por compliance básico. DBA y
backups en cleartext violan principio de mínimo privilegio.

## Notas de implementación

### Setup en `Program.cs`

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(
        connectionString: builder.Configuration["DataProtection:BlobConnString"],
        containerName: "dataprotection-keys",
        blobName: "millet-erp.xml")
    .ProtectKeysWithAzureKeyVault(
        new Uri(builder.Configuration["DataProtection:KeyIdentifier"]!),
        new DefaultAzureCredential())
    .SetApplicationName("Millet.ERP")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));
```

Si `DataProtection:KeyIdentifier` no está configurado, ASP.NET cae a
FS automáticamente.

### Packages nuevos

- `Azure.Extensions.AspNetCore.DataProtection.Keys` (KV provider)
- `Azure.Extensions.AspNetCore.DataProtection.Blobs` (Blob storage para
  el ring)
- `Azure.Identity` (`DefaultAzureCredential` — ya en uso por
  `Integraciones.Aw`)

### Bicep — cambios necesarios

- `infra/modules/keyvault.bicep` — agregar resource
  `dataprotection-master-key` (tipo RSA 2048, ops `wrapKey`/`unwrapKey`).
  Soft-delete + purge protection ya están on.
- `infra/modules/storage.bicep` — agregar container
  `dataprotection-keys` con `publicAccess: 'None'`.
- App settings nuevos en `appservice.bicep`:
  - `DataProtection__BlobConnString` → KV ref a `storage-connection-string`.
  - `DataProtection__KeyIdentifier` → URI versioned de la key.
- Role assignments:
  - Managed identity del App Service: `Key Vault Crypto User` sobre la
    key + `Storage Blob Data Contributor` sobre el container.

### `SecretCipher` helper

Cada módulo expone su propio `SecretCipher` con purpose string distinto:

```csharp
// En Integraciones.Fiscal:
internal sealed class FiscalSecretCipher
{
    private readonly IDataProtector _protector;
    public FiscalSecretCipher(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Integraciones.Fiscal.ApiKey.v1");
    // ... Encrypt / Decrypt / Hash
}

// En Integraciones.Mailbox:
internal sealed class MailboxSecretCipher
{
    private readonly IDataProtector _protector;
    public MailboxSecretCipher(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Integraciones.Mailbox.ClientSecret.v1");
    // ...
}
```

Aislamiento total — rotación de `v1` → `v2` en un módulo no impacta al
otro.

### Tests

- Round-trip: cifrar / descifrar string conocido, verificar igualdad.
- Cambio de purpose string rompe descifrado (test que confirma este
  fail-fast).
- Hash for change detection retorna mismo valor para mismo plaintext y
  distinto para plaintexts distintos.

### Runbook recovery (apuntado en operación-y-runbook de cada módulo)

- Si se borra accidentalmente el blob `millet-erp.xml`:
  - Si soft-delete del container está on (DEBE estar) — `az storage blob
    restore`. Mientras tanto, columnas cifradas son ilegibles → workers
    en idle, admin UI sin lectura de secretos.
- Si se borra la key del KV:
  - Soft-delete + purge protection garantizan 90 días de recovery.
  - `az keyvault key recover` con `--name dataprotection-master-key`.
- Si se compromete la key:
  - Rotar versión nueva. Marcar la vieja `Disabled`. Los ciphertext
    cifrados con la vieja se vuelven ilegibles → admin debe rotar todas
    las credenciales individuales también.

### Cross-ADR

- Reemplaza implícitamente el patrón actual de "todo secret va a KV
  individualmente" **solo para secretos operativos admin-configurables**.
  Secretos de infra (DB connection string, signing key JWT) **siguen
  yendo a KV directamente** vía Bicep app settings.
- No conflicto con ADR-0030 (multi-DbContext): cada módulo de
  integraciones tiene su propio schema y sus propias columnas
  cifradas, independientes.
