# ADR-0058: Servicio genérico de adjuntos con metadatos, vigencia y enlace temporal

- **Estado**: Propuesta (formatos, tamaño, vigencias y retención pendientes de validar con Millet)
- **Fecha**: 2026-10-06
- **Decisores**: Eduardo Paredes (owner)
- **Etiquetas**: adjuntos, blob, seguridad, expediente, proveedor, F1-ADM-11

## Contexto y problema

F1-ADM-11 (contrato `docs/modulos/administracion/adm-11-contrato-adjuntos.md`) estableció el estándar de adjuntos con control de acceso a partir del piloto de Órdenes de Compra, que tiene su propia entidad (`AdjuntoOC`), endpoints y puerto de blob. La subtarea G1.2 pide un expediente documental del proveedor (5 documentos, vigencia, baja con motivo, descarga segura), y vienen más consumidores (pólizas C1.3, comercio exterior CE12.1, recepciones). Replicar el patrón de OC en cada módulo multiplica tablas, endpoints, validaciones y puertos de blob; además el piloto expone la URL del blob en sus DTO (`file:///` en dev), que la ficha prohíbe. Hay tres `IAlmacenarBlobPort` duplicados (Compras, Almacén, Integraciones.Aw).

## Drivers de la decisión

- Un solo lugar para autorización, política de archivo, auditoría, vigencia y baja.
- Nunca exponer URL permanente del blob; descarga solo por endpoint autenticado o enlace temporal.
- Heredar permisos y alcance del documento padre (ADR-0051), con permisos por operación (ADR-0041).
- No romper lo que ya funciona (OC, Almacén, Aw) dentro de la ventana de G1.2.
- Formatos, tamaño, vigencias y retención aún no están validados por Millet: deben ser configurables.

## Opciones consideradas

1. Enlace temporal propio (token firmado con DataProtection) sobre un endpoint de la API, frente a URL SAS de Azure.
2. Entidad genérica `Adjunto` + catálogo de tipos, frente a una entidad de adjunto por módulo.
3. Unificar los tres puertos de blob ahora, frente a un puerto nuevo y migrar después.

## Decisión

- **Modelo genérico**: `compartido.adjuntos` y `compartido.adjunto_tipos_documento`, con `TipoEntidad` + `EntidadId`. Baja lógica con motivo (5–500 caracteres), el blob se conserva. Estado derivado (`Vigente | PorVencer | Vencido | SinVigencia | Baja`) calculado por fecha en `America/Mexico_City`.
- **Autorización heredada**: `IAdjuntoPropietario` por módulo (permisos por operación, existencia y alcance del padre). Orden fijo: permiso → existencia → alcance → operación.
- **Almacenamiento**: puerto nuevo `IBlobStoragePort` (SharedKernel) con adaptadores Azure y filesystem local; los tres puertos legados no se tocan. `BlobRef` jamás sale en un DTO.
- **Descarga**: enlace temporal propio (`ITimeLimitedDataProtector`, TTL 60 s, atado a adjunto y usuario, propósito dedicado) servido por `GET /api/v1/adjuntos/descargas/{token}`; re-verifica la baja y audita. Además, `…/contenido` autenticado para previews.
- **Política por entidad**: `AdjuntosPoliticaOptions.ParaEntidad` con overrides en configuración (proveedor: PDF, XML, JPG, PNG, 10 MB, valores de PRUEBA). El XML se valida sin parsear.
- **Expediente**: `GET …/expediente` y `IExpedienteProveedorReadPort` para que CxP valide el expediente sin tocar tablas de Compartido (G1.1).
- OC queda como **consumidor piloto legado**; no se migra aquí.

## Consecuencias

**Positivas**
- Un módulo nuevo agrega un `IAdjuntoPropietario`, permisos, tipos sembrados y una llamada a `MapAdjuntos`; no escribe tablas ni validaciones propias.
- Se elimina la exposición de URL de blob en el servicio nuevo (y se quitó de los DTO de OC en el mismo PR).
- Valores configurables: validar con Millet no requiere cambios de código.

**Negativas**
- Conviven dos mecanismos (genérico y legado) hasta migrar OC/Almacén; existen cuatro puertos de blob.
- El enlace temporal es un endpoint anónimo cuya credencial es el token: requiere revisión de seguridad y TTL corto.
- Sin FK de `adjuntos` al dueño (genérico): la integridad depende de `IAdjuntoPropietario`; las entidades cross-empresa (Proveedor) dependen solo del permiso.
- Los blobs dados de baja nunca se borran hasta definir retención.

## Descartadas

- **URL SAS de Azure**: expone cuenta y ruta del blob, no sirve con el stub local de desarrollo, no se puede re-verificar la baja al momento de descargar ni auditar la descarga por usuario, y ata el contrato a Azure. El token propio se valida en la API y funciona igual en dev.
- **Entidad de adjunto por módulo**: es el patrón de OC; duplica tabla, validaciones, endpoints, política y pruebas por cada módulo y dificulta reglas transversales (vigencia, baja, auditoría).
- **Unificar los puertos de blob ahora**: obliga a tocar Compras, Almacén e Integraciones.Aw (con sus URL ya persistidas como referencia) en una subtarea de otro alcance; riesgo de regresión sin beneficio para G1.2. Se crea un puerto nuevo y se migra después.

## Notas de implementación

Dependencias de plataforma pendientes (ADR-0031):

| Pieza | Ticket | NoOp en uso | Cómo se wirea |
|---|---|---|---|
| Unificar puertos de blob | `<UnificarBlobPorts>` | Conviven `IBlobStoragePort` y tres `IAlmacenarBlobPort` | Migrar los tres a `IBlobStoragePort` y retirar duplicados (`Api/Program.cs`, wiring de blobs) |
| Migrar adjuntos de OC y Almacén | `<MigrarAdjuntosOcAlmacen>` | `AdjuntoOC`, vale, packing list y evidencias con patrón propio | Un `IAdjuntoPropietario` por módulo + migración de datos (`Api/Program.cs`, registro de propietarios) |
| Antivirus | `<AntivirusAdjuntos>` | Sin escaneo; solo formato, firma y tamaño | Escaneo previo a `SubirAsync` (Defender for Storage u otro) en `SubirAdjuntoCommand` |
| Retención y purga | `<RetencionAdjuntos>` | Baja lógica; el blob se conserva siempre | Política de Millet; job de purga o tier Archive (`DarDeBajaAdjuntoCommand`) |

Detalle en `docs/modulos/administracion/adm-11-contrato-adjuntos.md` §11. Pendientes de validar con Millet: ver "Propuestas a validar con Eliam".
