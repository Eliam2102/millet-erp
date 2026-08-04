# ADR-0025: Generación de PDFs con QuestPDF

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: pdf, fiscal, plantillas, tier-3

## Contexto y problema

El ERP necesita generar PDFs en múltiples categorías:

- **Representación impresa de CFDI** (factura, complemento de pago, nota de crédito): obligatoria, regulada por el SAT (debe contener QR, cadena original, sello, etc.)
- **Estados de cuenta** y **reportes operativos**: cartera, antigüedad de saldos
- **Documentos administrativos**: cotizaciones, órdenes de compra, recepciones, remisiones
- **Reportes contables y financieros**: balanza, mayor, estados financieros

Sin librería ni convenciones definidas:
- Cada módulo termina con su propio approach
- El estilo visual es inconsistente entre PDFs
- No hay forma de cambiar la paleta corporativa de Millet en un solo lugar
- Componentes reutilizables (header con logo, totales, tabla de partidas) se duplican
- Cumplimiento SAT en CFDIs queda al criterio del dev que escribe la plantilla

Necesitamos definir librería, arquitectura de plantillas, componentes
reutilizables, y patrones de generación/almacenamiento.

## Drivers de la decisión

- Calidad visual: PDFs con tipografía, espaciado y branding consistentes
- Cumplimiento SAT en CFDIs: QR, cadena original, datos requeridos en posiciones correctas
- Productividad: API ergonómica que evite cálculo de coordenadas a mano
- Performance: generación rápida (<1s para documentos típicos)
- Testabilidad: verificar que las plantillas no rompen visualmente
- Licencia compatible con uso comercial cerrado
- Integración con almacenamiento (ADR-0024) y dinero/fechas (ADR-0014, ADR-0013)

## Opciones consideradas

1. QuestPDF (licencia comercial)
2. iTextSharp (AGPL o licencia comercial)
3. PdfSharp + MigraDoc (gratis, low-level)
4. HTML → PDF con Puppeteer/Playwright headless
5. DinkToPdf / wkhtmltopdf (deprecated)
6. Aspose.PDF (licencia comercial cara)

## Decisión

Se adopta **QuestPDF con licencia comercial** como librería única para
toda la generación de PDFs.

### Justificación

QuestPDF ofrece:
- API fluent en C# puro, layout tipo CSS/Flexbox, runtime estable
- Performance excelente: documentos típicos en <100ms
- Comunidad activa, soporte oficial pagado disponible
- Embeds de fuentes y SVG nativos

**Costo de licencia**: requerida para empresas con revenue >$1M/año desde
mediados de 2024. Aproximadamente $700 USD/año para equipos hasta 10
desarrolladores. Aplica a Millet.

El costo se justifica por:
- Productividad: API moderna ahorra días de trabajo vs PdfSharp
- Soporte oficial: respuesta a issues y nuevas features
- Evita el problema AGPL de iTextSharp (contaminante para código propietario)
- Mucho más rápido y predecible que generación HTML→PDF con Chromium

### Arquitectura — plantillas como código

Cada tipo de PDF es una clase C# que implementa `IDocument` de QuestPDF.
Vive en el módulo correspondiente:

```
backend/src/
├── Fiscal/
│   └── Application/
│       └── Pdf/
│           ├── CfdiFacturaPdfDocument.cs
│           ├── CfdiPagoPdfDocument.cs
│           └── CfdiNotaCreditoPdfDocument.cs
├── Financiero/
│   └── Application/
│       └── Pdf/
│           ├── EstadoCuentaPdfDocument.cs
│           └── AntiguedadSaldosPdfDocument.cs
├── Comercial/
│   └── Application/
│       └── Pdf/
│           └── CotizacionPdfDocument.cs
└── Compras/
    └── Application/
        └── Pdf/
            └── OrdenCompraPdfDocument.cs
```

Cada documento recibe su modelo y produce el PDF:

```csharp
public class CfdiFacturaPdfDocument : IDocument
{
    private readonly CfdiFacturaPdfModel _model;

    public CfdiFacturaPdfDocument(CfdiFacturaPdfModel model) => _model = model;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(PdfDesignTokens.MarginCm, Unit.Centimetre);
            page.DefaultTextStyle(t =>
                t.FontFamily(PdfDesignTokens.FontFamilyPrimary)
                 .FontSize(PdfDesignTokens.FontSizeBody));

            page.Header().Element(c =>
                new EmpresaHeader(_model.Empresa).Compose(c));

            page.Content().Column(col =>
            {
                col.Item().Element(ComposeFolioYFecha);
                col.Item().Element(c =>
                    new ClienteAddressBlock(_model.Receptor).Compose(c));
                col.Item().Element(c =>
                    new ConceptosTable(_model.Conceptos).Compose(c));
                col.Item().Element(c =>
                    new TotalesBlock(_model.Totales).Compose(c));
                col.Item().Element(ComposeSelloYQr);
            });

            page.Footer().Element(c =>
                new DocumentFooter(_model.Leyenda).Compose(c));
        });
    }

    private void ComposeFolioYFecha(IContainer container) { ... }
    private void ComposeSelloYQr(IContainer container) { ... }
}
```

### Servicio `IPdfGenerator`

```csharp
public interface IPdfGenerator
{
    /// <summary>Genera bytes del PDF sin almacenarlo.</summary>
    Task<byte[]> GenerateAsync(IDocument document, CancellationToken ct);

    /// <summary>Genera y almacena en Blob (ADR-0024); retorna referencia.</summary>
    Task<Documento> GenerateAndStoreAsync(
        IDocument document,
        StoragePdfRequest request,
        CancellationToken ct);

    /// <summary>Genera o retorna el PDF cacheado si ya existe.</summary>
    Task<Documento> GenerateOrGetCachedAsync(
        IDocument document,
        CachedPdfRequest request,
        CancellationToken ct);
}

public record StoragePdfRequest(
    string TipoDocumento,         // 'factura_pdf', 'estado_cuenta_pdf', etc.
    string Container,             // típicamente 'documentos-generados'
    string EntidadRelacionada,    // 'cfdi', 'cliente', etc.
    Guid EntidadId,
    Dictionary<string, object>? Metadata = null);

public record CachedPdfRequest(
    string TipoDocumento,
    string EntidadRelacionada,
    Guid EntidadId);  // busca por tipo+entidad+id en core.documentos
```

`GenerateOrGetCachedAsync` es el path estándar:
- Busca en `core.documentos` por `(empresa_id, tipo_documento, entidad_relacionada, entidad_id)` con `status='confirmed'` y `deleted_at IS NULL`
- Si existe: retorna referencia (no regenera)
- Si no existe: genera, sube a Blob, crea fila, retorna

### Design tokens compartidos

Constantes centralizadas en `Shared/Application/Pdf/PdfDesignTokens.cs`:

```csharp
public static class PdfDesignTokens
{
    // Márgenes y espaciado
    public const float MarginCm = 1.5f;
    public const float SpacingSm = 4;
    public const float SpacingMd = 8;
    public const float SpacingLg = 16;

    // Tipografía
    public const string FontFamilyPrimary = "Inter";
    public const string FontFamilyMono = "JetBrains Mono";
    public const float FontSizeBody = 10;
    public const float FontSizeSmall = 8;
    public const float FontSizeTitle = 16;
    public const float FontSizeHeading = 12;

    // Paleta corporativa Millet (placeholder, ajustar al branding real)
    public static readonly Color ColorPrimary = Color.FromHex("#0F4C75");
    public static readonly Color ColorSecondary = Color.FromHex("#3282B8");
    public static readonly Color ColorTextBody = Color.FromHex("#1F2937");
    public static readonly Color ColorTextMuted = Color.FromHex("#6B7280");
    public static readonly Color ColorBorder = Color.FromHex("#E5E7EB");
    public static readonly Color ColorBackground = Color.FromHex("#FFFFFF");
    public static readonly Color ColorBackgroundSubtle = Color.FromHex("#F9FAFB");

    // Tablas
    public const float TableRowHeight = 18;
    public const float TableHeaderHeight = 22;
}
```

**Tipografía**: Inter como fuente principal (libre, excelente legibilidad,
soporta español sin problemas). Embebida en los PDFs (configurada en
`QuestPDF.Settings`). JetBrains Mono para folios, UUIDs, códigos.

### Componentes reutilizables

En `Shared/Application/Pdf/Components/`:

**`<EmpresaHeader>`**: logo + datos fiscales del emisor

```csharp
public class EmpresaHeader
{
    private readonly EmpresaPdfData _empresa;

    public void Compose(IContainer container)
    {
        container.Row(row =>
        {
            row.ConstantItem(80).Image(_empresa.LogoBytes);  // resuelto vía IDocumentStorage
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(_empresa.RazonSocial)
                    .FontSize(PdfDesignTokens.FontSizeHeading).Bold();
                col.Item().Text($"RFC: {_empresa.Rfc}");
                col.Item().Text(_empresa.RegimenFiscal);
                col.Item().Text(_empresa.DomicilioFiscalFormateado)
                    .FontSize(PdfDesignTokens.FontSizeSmall)
                    .FontColor(PdfDesignTokens.ColorTextMuted);
            });
        });
    }
}
```

**`<ClienteAddressBlock>`**: dirección formateada del receptor

**`<ConceptosTable>`**: tabla de partidas con cantidad, descripción, unitario, total. Usa formateo de `Money` (ADR-0014) consistentemente.

**`<TotalesBlock>`**: subtotal, descuentos, IVA, retenciones, total final. Visualmente jerárquico (totales con peso visual mayor).

**`<DocumentFooter>`**: leyenda legal + paginación + fecha de generación + correlation ID (para soporte). Aparece en todas las páginas.

**`<QrCodeImage>`**: genera QR a partir de string usando `QRCoder`. Usado en CFDIs.

**`<SelloDigitalBlock>`**: bloque con cadena original y sello SAT en formato monoespaciado, salto de línea cada 80 caracteres.

### Plantilla CFDI: requisitos SAT 4.0

Elementos obligatorios:

- Datos del **emisor**: RFC, nombre/razón social, régimen fiscal, lugar de expedición (CP)
- Datos del **receptor**: RFC, nombre, domicilio fiscal (CP), régimen fiscal, uso CFDI
- **Folio fiscal** (UUID asignado por el PAC)
- **Fecha y hora de timbrado**
- **Folio interno** y **serie**
- **Conceptos**: clave SAT producto/servicio, cantidad, clave unidad SAT, descripción, valor unitario, importe, descuentos, impuestos por concepto
- **Subtotal, descuentos, total**
- **Impuestos** trasladados/retenidos (IVA, ISR si aplica)
- **Forma de pago**, **método de pago**, **moneda**, **tipo de cambio** (si aplica, ADR-0014)
- **Código QR** con URL al servicio de validación SAT (`https://verificacfdi.facturaelectronica.sat.gob.mx/...`)
- **Cadena original** del complemento de certificación
- **Sello digital** del CFDI (emisor)
- **Sello digital** del SAT (PAC)
- Información del PAC certificador: nombre, RFC, número de certificado
- Leyenda: **"Este documento es una representación impresa de un CFDI"**

QR generado con paquete `QRCoder` (MIT, gratuito, sin dependencias).

### Plantilla complemento de pago

Similar al CFDI pero con secciones específicas:
- Información del pago: monto, moneda, tipo de cambio, forma de pago
- Cuenta ordenante / beneficiaria (RFC, banco, número)
- Documentos relacionados (facturas que se están pagando) con sus folios fiscales

### Plantillas administrativas (cotización, OC, etc.)

Más libres, sin requerimientos SAT:
- Header con datos de empresa
- Datos del cliente/proveedor
- Folio interno (no fiscal)
- Tabla de conceptos
- Totales
- Términos y condiciones (texto opcional configurable por empresa)
- Espacios para firmas

### Estados de cuenta y reportes

- Header con empresa, fecha de corte, periodo
- Body con tabla(s) de datos
- Resumen al inicio o final
- Footer con totales globales, paginación

Patrón de "tabla larga" en QuestPDF: usa `Table` con `ExtendLastCellsToTableBottom()` para que el header se repita en cada página automáticamente.

### Generación on-demand vs batch

**On-demand** (mayoría):
- Usuario hace clic "Imprimir factura" en el frontend
- Endpoint `POST /api/fiscal/cfdis/{id}/pdf` invoca `GenerateOrGetCachedAsync`
- Retorna `{ documentoId, urlDescarga }` (SAS URL del Blob, ADR-0024)
- Frontend redirige al usuario al SAS URL

**Batch** (cuando aplique):
- "Generar PDFs de todos los CFDIs del mes" → user job (ADR-0022 categoría C)
- Worker procesa en background, sube a `documentos-generados`, actualiza progreso
- Al completar, notifica al usuario vía SignalR (ADR-0001) y/o email

### Caching de PDFs

PDFs son **regenerables** pero costosos en CPU. Política:

- Primera vez que se solicita: se genera, se sube a Blob, se crea fila en `core.documentos`
- Solicitudes posteriores: se retorna SAS URL al blob existente (sin regenerar)
- Si el documento subyacente cambia: invalidar (descartado: CFDIs son inmutables; cotizaciones podrían cambiar — en ese caso el handler que las modifique debe `SoftDelete` el PDF asociado para forzar regeneración)
- Si el lifecycle elimina el PDF (1 año post-creación, ADR-0024): la siguiente solicitud regenera

### Configuración de fuentes embebidas

QuestPDF requiere registrar fuentes al inicio:

```csharp
// Program.cs
QuestPDF.Settings.License = LicenseType.Professional;

FontManager.RegisterFontFromEmbeddedResource(
    "MilletErp.Shared.Application.Pdf.Fonts.Inter-Regular.ttf");
FontManager.RegisterFontFromEmbeddedResource(
    "MilletErp.Shared.Application.Pdf.Fonts.Inter-Bold.ttf");
FontManager.RegisterFontFromEmbeddedResource(
    "MilletErp.Shared.Application.Pdf.Fonts.JetBrainsMono-Regular.ttf");
```

Las fuentes viven en `Shared/Application/Pdf/Fonts/` como Embedded
Resources del proyecto. Esto garantiza consistencia visual sin importar el
SO donde corre el App Service.

### Localización

PDFs siempre en español (es-MX). Formatos:
- Fechas: `dd/MM/yyyy` o `2 de mayo de 2026` (ADR-0013)
- Hora: `HH:mm:ss` cuando es relevante (timbrado)
- Montos: `$1,234.56 MXN` o `$1,234.56` si la empresa factura primariamente en MXN (ADR-0014)
- Decimales: punto (`.`)
- Miles: coma (`,`)

Si en el futuro se requiere generar PDFs en inglés (clientes
extranjeros), se evaluará — ADR aparte.

### Performance

QuestPDF en benchmarks típicos:
- CFDI 1 página: ~50-80ms
- Estado de cuenta 5 páginas: ~150-200ms
- Reporte 100 páginas: ~2-5 segundos

Para reportes muy grandes (>100 páginas):
- Generación en background como user job
- Streaming en lugar de buffering completo en memoria si se justifica

### Tests

**Unit tests** de cada plantilla:
- Crear modelo de prueba con `Bogus` (ADR-0016)
- Invocar `Generate(...)` y verificar que retorna bytes y no crashea
- Verificar tamaño razonable (entre N KB y M KB para detectar cambios groseros)

**Snapshot testing con Verify** (ADR-0016):
- Para plantillas críticas (CFDI), guardar PDF de referencia
- Cualquier cambio visual obliga a confirmar regeneración manual
- Útil para detectar regresiones accidentales en layout

**Tests específicos de cumplimiento**:
- Validar que el QR de CFDI contiene la URL correcta del SAT con UUID, RFC emisor, RFC receptor, total, sello correctos
- Validar que la cadena original está presente y se imprime completa
- Validar que el sello digital se renderiza con tipografía monoespaciada
- Validar que la leyenda "Este documento es una representación impresa..." aparece

**Tests de integración**:
- Generar y subir a Blob (Azurite) y verificar que el `Documento` resultante apunta correctamente

### Visualización en frontend

Componente `<PdfViewer documentoId={id} />` que:
- Llama al endpoint para obtener URL de descarga
- Embeds el PDF inline con `<iframe>` o `<embed>` para vista previa
- Botones "Descargar" y "Imprimir"
- Fallback si el navegador no soporta inline PDF (raro en navegadores modernos)

Para CFDIs específicamente, también embebe el XML como descarga
adicional vía el mismo patrón.

### Lo que NO incluye

- **Editor visual de plantillas para usuarios finales**: descartado, las plantillas son código. Cambiarlas requiere PR
- **Plantillas personalizadas por cliente**: una sola plantilla por tipo de documento (al menos inicialmente). Si surge necesidad por requerimiento de un cliente importante, ADR aparte
- **PDFs editables (forms)**: no es objetivo
- **Firma digital de PDFs (PAdES)**: si surge requerimiento legal específico, ADR puntual
- **Marcas de agua dinámicas** (ej. "BORRADOR" en CFDIs no timbrados, "DUPLICADO" en reimpresiones): si surge necesidad operativa
- **Generación de PDFs desde HTML**: descartado por las razones de la decisión

## Consecuencias

**Positivas**
- Plantillas como código: type-safe, refactorizables, testeables
- Estilo consistente vía design tokens
- Componentes reutilizables eliminan duplicación
- Performance excelente para 99% de los casos
- Caching natural en Blob: no regeneramos lo mismo dos veces
- Cumplimiento SAT verificable con tests automatizados
- Integración limpia con `IDocumentStorage` (ADR-0024)

**Negativas**
- Costo de licencia comercial (~$700 USD/año). Aceptable: justificado por productividad y soporte
- Cambiar layouts de PDFs requiere despliegue (no es runtime configurable). Aceptable: los PDFs no cambian frecuentemente; cuando lo hacen, va con PR
- Una plantilla por tipo de documento puede ser limitante si un cliente importante pide personalización. Mitigable a futuro con ADR específico
- Tests de snapshot pueden ser ruidosos si se ajusta cualquier cosa visual. Aceptable: la confirmación manual es barata

## Descartadas

**iTextSharp**. Maduro pero AGPL contamina código propietario; la licencia
comercial es cara (varios miles USD/año por servidor). El costo y la
fricción legal no se justifican.

**PdfSharp + MigraDoc**. Gratis y funcional pero API low-level: hay que
calcular coordenadas, manejar saltos de página manualmente, layout de
tablas verboso. Productividad significativamente inferior a QuestPDF.

**HTML → PDF con Puppeteer/Playwright**. Flexible (cualquier dev frontend
puede iterar), pero:
- Requiere Chromium en el contenedor del App Service (~150 MB)
- Cold start lento (segundos)
- Performance peor que generación nativa
- Layout puede variar entre versiones de Chromium

**DinkToPdf / wkhtmltopdf**. Deprecated, problemas con .NET moderno,
mantenimiento errático.

**Aspose.PDF**. Excelente pero licencia muy cara ($2,000+/año por
desarrollador). No se justifica vs QuestPDF para nuestros casos de uso.

## Notas de implementación

**Backend**

- Agregar `QuestPDF` y `QRCoder` a `Directory.Packages.props`
- Configurar licencia en `Program.cs`: `QuestPDF.Settings.License = LicenseType.Professional;`
- Comprar licencia anual; almacenar key de licencia en Key Vault si aplica
- Embeber fuentes Inter y JetBrains Mono en `Shared/Application/Pdf/Fonts/`
- Crear `IPdfGenerator` y `BlobPdfGenerator` (default impl) en `Shared/Infrastructure/Pdf/`
- Crear `PdfDesignTokens` en `Shared/Application/Pdf/`
- Componentes reutilizables en `Shared/Application/Pdf/Components/`
- Plantillas específicas en cada módulo: `{Modulo}/Application/Pdf/`

**Caché de logos**

- Logos de empresas viven en `imagenes-publicas` (ADR-0024)
- El componente `<EmpresaHeader>` los obtiene vía `IDocumentStorage.DownloadStreamAsync(...)`
- Para evitar fetch en cada generación: cache in-memory (con `IMemoryCache`) por empresa, TTL 1 hora
- Invalidación: cuando el admin sube un logo nuevo, se invalida la entrada del cache

**Endpoints**

- `POST /api/{modulo}/{recurso}/{id}/pdf` por cada tipo de PDF
- Patrón estándar:
  ```csharp
  [HttpPost("{id}/pdf")]
  [RequirePermission("...")]
  public async Task<IActionResult> GenerarPdf(Guid id, ...)
  {
      var model = await _service.BuildPdfModelAsync(id, ct);
      var doc = new CfdiFacturaPdfDocument(model);
      var documento = await _pdfGenerator.GenerateOrGetCachedAsync(doc, request, ct);
      var sasUrl = await _storage.GetReadSasUrlAsync(
          documento.Id, TimeSpan.FromMinutes(15), ct);
      return Ok(new { documentoId = documento.Id, urlDescarga = sasUrl });
  }
  ```

**Documentación en `CLAUDE.md`**

- Cómo crear una plantilla nueva (heredar de `IDocument`, modelo en input)
- Convenciones de naming
- Cómo usar componentes reutilizables
- Cómo testear una plantilla (snapshot, contenido, performance)
- Cómo agregar un nuevo design token
- Reglas para CFDIs: campos obligatorios, layout SAT 4.0

**ADRs hijo posibles**

- Plantillas personalizadas por cliente (cuando surja la necesidad)
- Marcas de agua dinámicas
- Firma digital de PDFs (PAdES)
- Generación de PDFs en otros idiomas (i18n)
- Política de optimización para reportes muy grandes (streaming, partitioned generation)
