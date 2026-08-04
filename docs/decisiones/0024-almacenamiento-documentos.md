# ADR-0024: Almacenamiento de documentos con Azure Blob Storage

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: storage, blobs, fiscal, tier-3, fundación

## Contexto y problema

El ERP genera, recibe y almacena cantidades significativas de documentos
binarios:

- **CFDIs XML** (emitidos y recibidos): obligación fiscal de conservar 5 años, inmutables
- **PDFs generados**: facturas impresas, estados de cuenta, reportes; regenerables pero costosos de generar
- **Adjuntos**: contratos escaneados, comprobantes de transferencia, fotos de comprobantes de gastos, identificaciones; subidos por usuarios
- **Exportes**: archivos Excel/CSV de reportes grandes generados a demanda; descargables temporalmente
- **Imágenes**: logos de empresas para incrustar en CFDIs/PDFs/correos

Sin diseño explícito, el equipo terminaría guardando algunos en BD (BYTEA),
otros en filesystem del App Service (que se borra entre reinicios), y
otros en blobs sin convención. Con consecuencias previsibles: BD enorme y
lenta, archivos perdidos, costos excesivos en tier Hot, problemas de
cumplimiento SAT.

Necesitamos diseño completo: dónde se guardan, cómo se nombran, cómo se
controla acceso, cómo se manejan los tiers, cómo se sube/descarga, qué
metadata se conserva en BD.

## Drivers de la decisión

- Cumplimiento SAT: CFDIs deben conservarse inmutablemente 5 años
- Costo: documentos viejos no deben pagar precio de tier Hot
- Performance: descargas grandes no deben atravesar el App Service
- Seguridad: cada acceso pasa por validación de permisos del backend
- Trazabilidad: cada blob importante tiene fila en BD para queries y auditoría
- Escalabilidad: poder crecer sin rediseño cuando se acumulen TBs de CFDIs históricos

## Opciones consideradas

1. Azure Blob Storage con containers especializados, índice en BD, SAS tokens
2. Filesystem del App Service para todo (descartado rápido)
3. Guardar binarios en PostgreSQL (BYTEA o LargeObject)
4. Servicio externo (S3, Cloudinary, etc.)
5. Híbrido: BD para chicos (<1MB), Blob para grandes

## Decisión

Se adopta la **opción 1**: Azure Blob Storage como único almacenamiento
binario, organizado en containers por categoría, con tabla `core.documentos`
como índice consultable en BD, y patrón de SAS tokens para upload/download
delegados.

### Containers

Una sola cuenta de Storage por ambiente (ya prevista en Bicep desde
Fase 1, ADR-0011), con containers especializados:

| Container               | Acceso  | Tier inicial | Lifecycle                                                 | Contenido                                              |
|-------------------------|---------|--------------|-----------------------------------------------------------|--------------------------------------------------------|
| `cfdis-xml`             | Privado | Hot          | Hot 90d → Cool 2 años → Archive indefinido                | CFDIs (emitidos y recibidos), complementos de pago     |
| `documentos-generados`  | Privado | Hot          | Hot 30d → Cool 1 año → eliminar                           | PDFs de facturas, estados de cuenta, reportes          |
| `adjuntos`              | Privado | Hot          | Hot 5 años, después evaluar                               | Archivos subidos por usuarios                          |
| `exportes`              | Privado | Hot          | Eliminar a los 7 días                                     | Excel/CSV de reportes descargables                     |
| `imagenes-publicas`     | Público (read) | Hot   | Indefinido                                                | Logos de empresas embebidos en CFDIs/PDFs/correos      |

### Inmutabilidad de CFDIs

`cfdis-xml` se configura con **immutability policy time-based de 5 años**:

- Una vez subido, el blob NO se puede modificar ni eliminar hasta que expire la retención
- Cumple regla SAT (5 años) y protege contra corrupción accidental o malintencionada
- El soft delete en `documentos` (tabla en BD) solo oculta el registro lógico; el blob físico sobrevive
- Si por alguna razón hay que sobrescribir un CFDI antes de los 5 años: imposible — y eso es la garantía

### Versioning de blobs

Activado en:
- **`cfdis-xml`**: protege contra borrado accidental por bug (rollback posible aunque la inmutabilidad ya bloquea)
- **`adjuntos`**: los usuarios pueden subir nueva versión de un documento; queremos historial

NO activado en `documentos-generados`, `exportes`, `imagenes-publicas`:
son regenerables o no críticos; el versioning duplicaría costo sin
beneficio claro.

### Naming convention

Estructura jerárquica por path. Blob no tiene "carpetas" reales, pero el
prefijo permite listados eficientes con `ListBlobs(prefix=...)`:

```
{empresa_id}/{año}/{mes}/{tipo}/{id}.{ext}
```

Ejemplos:

```
cfdis-xml/
└── 7e3a-empresa-a/
    └── 2026/
        └── 05/
            ├── emitido/
            │   └── A1B2C3-uuid-cfdi.xml
            └── recibido/
                └── D4E5F6-uuid-cfdi.xml

documentos-generados/
└── 7e3a-empresa-a/
    └── 2026/
        └── 05/
            └── factura-pdf/
                └── A1B2C3-uuid-cfdi.pdf

adjuntos/
└── 7e3a-empresa-a/
    └── 2026/
        └── 05/
            └── cliente-contrato/
                └── 8B9C0D-uuid-contrato-acme.pdf
```

**Razones**:
- Listar todos los CFDIs de una empresa en un mes: query eficiente
- El path mismo lleva metadata útil para debugging
- Particionado natural por empresa y por mes facilita políticas de retención y análisis de costos
- Aislamiento por `empresa_id` aún en el path (defensa-en-profundidad sobre ADR-0011)

### Tabla `core.documentos` (índice en BD)

Cada blob importante tiene fila correspondiente:

```
documentos
├── id (uuid v7, PK)
├── empresa_id (uuid, FK compartido.empresas, not null)
├── tipo_documento (text, not null)         -- 'cfdi_emitido', 'cfdi_recibido', 'factura_pdf', 'adjunto_cliente', etc.
├── entidad_relacionada (text, not null)    -- 'cfdi', 'cliente', 'orden_compra', etc.
├── entidad_id (uuid, not null)             -- ID del registro relacionado
├── container (text, not null)              -- 'cfdis-xml', 'documentos-generados', etc.
├── blob_path (text, not null)              -- path completo dentro del container
├── content_type (text, not null)           -- 'application/xml', 'application/pdf', 'image/jpeg'
├── tamano_bytes (bigint, not null)
├── hash_sha256 (text, not null)            -- para detectar corrupción y duplicados
├── original_filename (text, nullable)      -- nombre con que el usuario subió
├── metadata (jsonb, not null, default '{}')-- contexto adicional (ej. RFC del PAC, folio fiscal)
├── status (text, not null)                 -- 'pending'|'confirmed'|'archived'
├── created_at (timestamptz, not null)
├── created_by (uuid, FK identidad.usuarios, nullable)  -- null si lo generó el sistema
├── deleted_at (timestamptz, nullable)      -- soft delete

INDEX (empresa_id, tipo_documento, created_at DESC)
INDEX (entidad_relacionada, entidad_id)
INDEX (hash_sha256, empresa_id)             -- para detectar duplicados
```

**Razones para índice en BD**:
- Queries naturales: "todos los CFDIs del cliente X" se hace en SQL, no listando blobs
- FK opcional desde otras tablas (`cfdis.documento_xml_id`, `pagos.documento_comprobante_id`)
- Audit log lo registra naturalmente (ADR-0008)
- Soft delete: borrar lógicamente sin tocar el blob (que puede estar en Archive y costaría rehidratar para borrar)

### Acceso desde frontend — patrón de download

Todo acceso a blobs pasa por validación del backend, pero la transferencia
de bytes NO atraviesa el App Service:

```
1. Frontend llama: GET /api/documentos/{id}/url-descarga
2. Backend:
   a. Carga el documento desde BD
   b. Valida que el usuario tenga acceso (empresa_id correcto, permiso para tipo de documento)
   c. Si el blob está en Archive: retorna 202 con info de rehidratación pendiente
   d. Si está en Hot/Cool: genera SAS token con expiración 15 min
   e. Retorna { url: "https://...?sv=...&sig=...", expira_en: "...", filename: "..." }
3. Frontend: window.location = url  o  <a href={url} download={filename}>
4. Navegador descarga directamente desde Blob Storage
```

**Beneficios**:
- 100% de los accesos pasan por validación del backend
- Transferencia eficiente: no satura ancho de banda del App Service
- SAS tokens cortos limitan exposición si se interceptan

### Acceso al CONTENIDO de un blob subido — GET autenticado por stream

> **Refinamiento (PR adjuntos OC).** Convención transversal para servir el
> **contenido** de blobs **subidos** que el front necesita previsualizar o
> descargar inline.

**Regla:** el contenido de un blob subido se sirve por un **GET autenticado
que hace stream por el backend** (`IAlmacenarBlobPort.ObtenerStreamAsync` →
`Results.File`/`Results.Stream`). **Nunca** se expone el `blobUrl`/`blobRef`
crudo del storage al frontend para que lo navegue directo.

**Por qué:** el `blobUrl` crudo depende del adaptador de storage activo —
en prod es `https://…` (Azure, navegable), pero en **dev con el stub local
es `file://…`**, que el navegador **no puede** abrir ni embeber desde una
página `http://`. El front terminaba con `<embed src="file://…">` /
`<a href="file://…">` rotos. El GET-stream es agnóstico del ambiente
(funciona igual en dev y Azure), queda **auth-gated** (mismo permiso con el
que se ve el recurso) y el front lo consume como **Blob → object URL**
(`blob:…`) para preview inline y `<a download>` (patrón
`usePdfOrdenCompra` / `useValeBlob`).

**Relación con el patrón SAS de arriba:** el SAS directo (browser → Blob,
sin atravesar el App Service) sigue siendo válido y preferible para
**descargas grandes/masivas** (driver de performance). El GET-stream aplica
a **adjuntos pequeños** (≤20 MB, ver whitelist de FOC2) que requieren
**preview inline auth-gated** sin filtrar una URL pública. No se contradicen:
SAS para volumen, stream para adjuntos visibles en la UI.

**Appliers — quién sigue ya esta convención y quién falta:**

| Módulo · recurso | GET-stream de contenido | Estado |
|---|---|---|
| Compras OC — PDF generado | `GET /ordenes/{id}/pdf` | ✅ |
| Almacén — vale | `GET /salidas/{id}/vale` | ✅ |
| **Compras OC — adjuntos subidos** | `GET /ordenes/{id}/adjuntos/{adjuntoId}/contenido` | ✅ (este PR) |
| CxP — evidencias | — | ⏳ pendiente (sigue el patrón viejo de OC adjuntos) |
| Almacén — packing-list | — | ⏳ pendiente (solo POST de subida) |
| Almacén — evidencia devoluciones | — | ⏳ pendiente (solo POST de subida) |

> Los ⏳ son follow-ups: cada uno necesita su GET-stream de contenido y que
> el front consuma el object URL en vez del `blobRef` crudo. No se abordan
> en este PR (alcance: solo Compras OC).

### Acceso desde frontend — patrón de upload

Patrón inverso para subidas de usuario:

```
1. Frontend llama: POST /api/documentos/preparar-upload
   body: { tipo, contentType, tamanoBytes, entidadRelacionada, entidadId }
2. Backend:
   a. Valida (tipo permitido para el usuario, tamaño dentro de límites,
      entidad relacionada accesible)
   b. Crea fila en `documentos` con status='pending', genera blob_path nuevo
   c. Genera SAS token con permiso de write, expiración 15 min
   d. Retorna { documentoId, uploadUrl, blobPath, expira_en }
3. Frontend: PUT directo a uploadUrl con el File object del usuario
4. Frontend llama: POST /api/documentos/confirmar-upload/{documentoId}
   body: { hashSha256? } (opcional; el backend recalcula de todos modos)
5. Backend:
   a. Verifica que el blob existe y tiene contenido
   b. Calcula SHA-256 real del blob
   c. Si el hash coincide con un blob existente de la misma empresa: dedup
      (apunta documentoId al blob_path existente, marca el nuevo para borrar)
   d. Actualiza fila en `documentos`: status='confirmed', tamano_bytes real,
      hash_sha256 real
   e. Retorna metadata final
```

**Beneficios**:
- Archivos grandes (PDFs de varios MB, fotos de comprobantes) NO atraviesan el App Service
- Si la subida falla a la mitad, el blob queda huérfano pero `confirmar-upload` nunca se llama
- Job nocturno limpia blobs con `status='pending'` de >1 hora (cleanup de uploads abandonados)
- Validación final (hash, tamaño real, content-type real) la hace el backend en `confirmar-upload`

### Detección de duplicados

Al `confirmar-upload`:
- Calcula SHA-256
- Busca filas en `documentos` con mismo `(empresa_id, hash_sha256)` y status='confirmed'
- Si existe: la nueva fila apunta al `blob_path` existente; el blob recién subido se marca para eliminar
- Útil para CFDIs recibidos (mismos XMLs enviados por error duplicado) y adjuntos repetidos
- El usuario no nota: la operación reporta éxito normal

### Tiers y lifecycle automático

Configurado en Bicep mediante `ManagementPolicies`:

```bicep
resource lifecyclePolicy 'Microsoft.Storage/storageAccounts/managementPolicies@...' = {
  properties: {
    policy: {
      rules: [
        // CFDIs: Hot → Cool a los 90 días → Archive a los 2 años
        {
          name: 'cfdis-cool-after-90d'
          enabled: true
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['cfdis-xml/'] }
            actions: {
              baseBlob: {
                tierToCool: { daysAfterCreationGreaterThan: 90 }
                tierToArchive: { daysAfterCreationGreaterThan: 730 }
              }
            }
          }
        },
        // Documentos generados: Cool a 30d, eliminar a 1 año
        {
          name: 'docs-generados-cool-then-delete'
          enabled: true
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['documentos-generados/'] }
            actions: {
              baseBlob: {
                tierToCool: { daysAfterCreationGreaterThan: 30 }
                delete: { daysAfterCreationGreaterThan: 365 }
              }
            }
          }
        },
        // Exportes: eliminar a 7 días
        {
          name: 'exportes-delete-7d'
          enabled: true
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['exportes/'] }
            actions: {
              baseBlob: { delete: { daysAfterCreationGreaterThan: 7 } }
            }
          }
        }
      ]
    }
  }
}
```

### Implicaciones de Archive

Los CFDIs >2 años viven en Archive. Acceder requiere "rehydrate" que toma
horas. Aceptable porque:
- Consultas tan antiguas son raras (auditorías, requerimientos del SAT)
- Suelen ser planeables (no urgentes en segundos)

**UX en frontend**:
- Si el documento está en Archive, el endpoint retorna 202 con
  `{ rehidratacion_solicitada: true, disponible_aprox: "4-15 horas" }`
- Frontend muestra: "Documento en archivo histórico. Recuperación
  solicitada; disponible en 4-15 horas. Te notificaremos por SignalR
  cuando esté listo."
- Endpoint `POST /api/documentos/{id}/rehidratar` dispara el rehydrate
- Endpoint `GET /api/documentos/{id}/estado` consulta avance
- Job nocturno detecta blobs rehidratados y notifica al usuario que los
  solicitó (vía email, ver ADR de email)

### Imágenes públicas

`imagenes-publicas` es el ÚNICO container con acceso público de lectura.
Razón: los logos de empresas se embeben en CFDIs y correos; necesitan ser
accesibles vía URL pública sin SAS tokens.

**Restricciones**:
- Solo administradores de la empresa pueden subir/eliminar logos (permiso `compartido.imagenes.gestionar`)
- Cada empresa tiene UN logo activo; al subir uno nuevo, el anterior queda histórico (versioning no aplica aquí, se usa fila distinta en `documentos`)
- Tamaño máximo: 2 MB
- Formatos permitidos: PNG, JPG (no SVG por consideraciones de seguridad — SVG puede llevar JS)

### Wrapper `IDocumentStorage`

Abstracción que ocultan los detalles de Blob Storage del código de negocio:

```csharp
public interface IDocumentStorage
{
    Task<DocumentoCreado> UploadAsync(
        UploadDocumentoRequest request,
        CancellationToken ct);

    Task<Stream> DownloadStreamAsync(
        Guid documentoId,
        CancellationToken ct);

    Task<DocumentoSasUrl> GetReadSasUrlAsync(
        Guid documentoId,
        TimeSpan validity,
        CancellationToken ct);

    Task<UploadSasInfo> CreateUploadSasAsync(
        CreateUploadSasRequest request,
        CancellationToken ct);

    Task<DocumentoConfirmado> ConfirmUploadAsync(
        Guid pendingDocumentoId,
        CancellationToken ct);

    Task SoftDeleteAsync(
        Guid documentoId,
        CancellationToken ct);

    Task<RehidratationStatus> RequestRehydrationAsync(
        Guid documentoId,
        CancellationToken ct);

    Task<RehidratationStatus> GetRehydrationStatusAsync(
        Guid documentoId,
        CancellationToken ct);
}
```

**Implementaciones**:
- `BlobDocumentStorage` (producción): usa `Azure.Storage.Blobs`
- `InMemoryDocumentStorage` (tests unit): mantiene blobs en memoria
- En desarrollo local: apunta a Azurite (emulador local de Blob Storage), corriendo en docker-compose

### Lo que NO incluye este ADR

- **CDN delante de imágenes públicas**: si el volumen lo justifica (ej. cuando se acumulen muchos logos de clientes), ADR aparte
- **Encriptación con customer-managed keys**: por default Azure encripta con keys de Microsoft. Si surge requerimiento regulatorio, ADR puntual
- **Sincronización con OneDrive/Dropbox del cliente**: no es objetivo
- **Acceso programático para integradores externos**: cuando aplique
- **Antivirus scanning** en uploads: descartado en Tier-3 inicial (Azure Defender for Storage es opción futura si surge necesidad)
- **Watermarking** automático de PDFs sensibles: si surge requerimiento

## Consecuencias

**Positivas**
- Cumplimiento fiscal: CFDIs inmutables por 5 años garantizado por Azure
- Costos optimizados: lifecycle automático mueve documentos viejos a tiers baratos
- Performance: archivos grandes no atraviesan App Service
- Seguridad: SAS tokens cortos, validación de permisos por documento, dedup natural por hash
- Trazabilidad: cada blob importante en `core.documentos`, auditable (ADR-0008)
- Escalable: Azure Blob Storage maneja TBs sin problema
- Backup natural: Azure ya replica geo-redundantemente; podemos elegir LRS/ZRS/GRS según ambiente

**Negativas**
- Complejidad inicial: dos roundtrips para upload (preparar + confirmar). Aceptable: el patrón resuelve archivos grandes elegantemente
- Acceder a Archive es lento (horas). Aceptable porque es raro y predecible
- Job de cleanup de blobs huérfanos (uploads abandonados) requiere mantenimiento. Mitigable con hosted service alineado a ADR-0022
- Costo de listar blobs: si una operación lista miles, paga por cada operación. Mitigado: las queries de listado se hacen en BD (`core.documentos`), no en Blob

## Descartadas

**Filesystem del App Service**. App Service Linux no garantiza
persistencia entre reinicios; Windows tiene persistencia limitada. Y
multi-instancia rompe completamente: cada instancia tiene su propio FS.
Inaceptable.

**PostgreSQL BYTEA o LargeObject**. Funciona técnicamente pero:
- BD se infla rápido (TB de XMLs y PDFs)
- Backups se vuelven lentos
- Costo de storage en PostgreSQL flexible server es alto vs Blob
- No hay tiers automáticos
- Performance de queries se degrada cuando hay binarios grandes en filas

**S3 / Cloudinary / otro proveedor externo**. Funcionaría pero agrega
proveedor adicional con su propia auth, network egress entre Azure y AWS,
duplicación de monitoring. Sin razón cuando Azure Blob ya está disponible.

**Híbrido BD para chicos / Blob para grandes**. Tentador pero introduce
complejidad sin ganar mucho:
- ¿Cuál es "chico"? Cualquier umbral es arbitrario
- Las queries deben saber dónde buscar cada documento
- Migrar de un lado a otro cuando crece es trabajo extra
- Mejor uniformidad: todo a Blob, BD solo metadata

## Notas de implementación

**Backend**

- Agregar `Azure.Storage.Blobs` a `Directory.Packages.props`
- Implementar `BlobDocumentStorage` en `Shared/Infrastructure/Storage/`
- Tabla `core.documentos` en migración inicial del esquema `core` (Fase 1) o cuando se necesite (probablemente Fase 2)
- Endpoints en `Api/Controllers/DocumentosController`:
  - `POST /api/documentos/preparar-upload` (request SAS de write)
  - `POST /api/documentos/confirmar-upload/{id}` (validar y completar)
  - `GET /api/documentos/{id}/url-descarga` (request SAS de read)
  - `GET /api/documentos/{id}/estado` (status incluyendo rehidratación)
  - `POST /api/documentos/{id}/rehidratar`
  - `DELETE /api/documentos/{id}` (soft delete)
- Hosted service `DocumentosOrphanCleanupJob` (alineado con ADR-0022): cada hora, busca documentos con `status='pending'` de >1 hora y los marca para limpieza; el blob físico se elimina en otro pase

**Bicep**

- Storage Account ya prevista en infra base; agregar:
  - `containers`: cfdis-xml, documentos-generados, adjuntos, exportes, imagenes-publicas
  - `imagenes-publicas` con `publicAccess = 'blob'`; los demás `publicAccess = 'none'`
  - `immutabilityPolicies` en `cfdis-xml` con retención 5 años
  - `versioning enabled` en `cfdis-xml` y `adjuntos`
  - `managementPolicies` con las reglas de lifecycle de la tabla
- Diagnostic settings: enviar logs de Blob Storage a App Insights

**Permisos** (alineado con ADR-0007)

- `documentos.subir` (general)
- `documentos.descargar` (general)
- `documentos.eliminar` (general)
- `compartido.imagenes.gestionar` (admin de empresa)

Validación adicional por tipo de documento: ej. para subir un CFDI XML
también se requiere permiso `fiscal.cfdi.timbrar` o equivalente.

**Frontend**

- Componente `<DocumentUpload />` en `components/erp/upload/`:
  - Wrapper sobre input file que ejecuta el patrón preparar-upload + PUT + confirmar-upload
  - Progreso de upload con `XMLHttpRequest.upload.onprogress` o fetch streams
  - Manejo de errores: si confirmar-upload falla, intenta cleanup
- Componente `<DocumentDownload />`:
  - Botón que llama url-descarga, redirige al SAS URL
  - Si está en Archive: modal informativo con opción de solicitar rehidratación
- Hook `useDocumentos(entidadRelacionada, entidadId)`: lista documentos asociados
- Hook `useRehidratacionStatus(documentoId)`: polling del estado

**Tests**

- Unit tests con `InMemoryDocumentStorage`
- Integration tests con Azurite (docker-compose):
  - Upload flow completo
  - Download via SAS
  - Dedup por hash
  - Cleanup de orphans
  - Rehidratación (mock; Azure Archive real es lento para tests)
- Tests específicos de inmutabilidad: intentar borrar/modificar un CFDI debe fallar

**Documentación en `CLAUDE.md`**

- Cuándo usar `IDocumentStorage` vs guardar en BD
- Patrón estándar de upload (3 pasos) y de download (1 paso)
- Cómo asociar un documento a una entidad de dominio
- Convenciones de naming de blobs
- Cómo manejar documentos en Archive desde la UI
- Cómo configurar Azurite para desarrollo local

**ADRs hijo posibles**

- CDN delante de imágenes públicas si volumen lo justifica
- Customer-managed keys si surge requerimiento de cumplimiento
- Antivirus scanning con Azure Defender for Storage
- Estrategia de backup adicional (snapshots, replicación cross-region)
- Política específica para documentos con datos personales sensibles (LFPDPPP)
