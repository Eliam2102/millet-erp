using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using SdkModels = Fiscalapi.Models;
using SdkCommon = Fiscalapi.Common;

namespace Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;

/// <summary>
/// Implementación de <see cref="IFiscalApiSdkClient"/> con el SDK NuGet
/// oficial <c>Fiscalapi</c>. Cada método:
/// <list type="bullet">
///   <item>Obtiene el cliente SDK por empresa via <see cref="IFiscalApiSdkClientFactory"/>.</item>
///   <item>Hace la llamada al servicio del SDK correspondiente.</item>
///   <item>Verifica <c>ApiResponse.Succeeded</c> — si false, lanza
///   <see cref="System.InvalidOperationException"/> con el mensaje del PAC.</item>
///   <item>Mapea el response del SDK a los DTOs del puerto.</item>
/// </list>
///
/// <para>
/// <b>Lo que NO hace</b>: Polly / retry / circuit breaker. El SDK del
/// vendor maneja eso internamente. Si en el futuro necesitamos políticas
/// específicas por empresa, se introduce un decorator alrededor del SDK.
/// </para>
/// </summary>
public sealed class FiscalApiSdkAdapter : IFiscalApiSdkClient
{
    private readonly IFiscalApiSdkClientFactory _factory;
    private readonly ILogger<FiscalApiSdkAdapter> _logger;

    public FiscalApiSdkAdapter(IFiscalApiSdkClientFactory factory, ILogger<FiscalApiSdkAdapter> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    // ───────────────────────── Aprovisionamiento ─────────────────────────

    public async Task<PersonExterno> AsegurarPersonAsync(
        Guid empresaId,
        string rfc,
        string legalName,
        string zipCode,
        string satTaxRegimeCode,
        string? satCfdiUseCode,
        string email,
        CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);

        // Buscar si ya existe por RFC.
        var list = await sdk.Persons.GetListAsync(pageNumber: 1, pageSize: 100);
        EnsureSucceeded(list, "AsegurarPersonAsync.List");
        var existing = list.Data.Items.FirstOrDefault(p =>
            string.Equals(p.Tin, rfc, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _logger.LogInformation("[FiscalApiSdkAdapter] Reusando Person existente {Id} para RFC {Rfc}.",
                existing.Id, rfc);
            return MapPerson(existing);
        }

        var nuevo = new SdkModels.Person
        {
            LegalName      = legalName,
            Email          = email,
            Password       = GenerarPasswordRandom(),
            Tin            = rfc,
            ZipCode        = zipCode,
            SatTaxRegimeId = satTaxRegimeCode,
            SatCfdiUseId   = satCfdiUseCode,
        };
        var created = await sdk.Persons.CreateAsync(nuevo);
        EnsureSucceeded(created, "AsegurarPersonAsync.Create");
        return MapPerson(created.Data);
    }

    public async Task SincronizarPersonAsync(
        Guid empresaId,
        string personIdExterno,
        string legalName,
        string zipCode,
        string? satCfdiUseCode,
        CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);
        var current = await sdk.Persons.GetByIdAsync(personIdExterno);
        EnsureSucceeded(current, "SincronizarPersonAsync.Get");

        var person = current.Data;
        var needsUpdate =
            !string.Equals(person.LegalName, legalName, StringComparison.Ordinal)
            || !string.Equals(person.ZipCode, zipCode, StringComparison.Ordinal)
            || (satCfdiUseCode is not null
                && !string.Equals(person.SatCfdiUseId, satCfdiUseCode, StringComparison.Ordinal));
        if (!needsUpdate) return;

        person.LegalName    = legalName;
        person.ZipCode      = zipCode;
        if (satCfdiUseCode is not null) person.SatCfdiUseId = satCfdiUseCode;

        var updated = await sdk.Persons.UpdateAsync(personIdExterno, person);
        EnsureSucceeded(updated, "SincronizarPersonAsync.Update");
    }

    public async Task<TaxFilesSubidos> SubirTaxFilesAsync(
        Guid empresaId,
        string personIdExterno,
        string rfc,
        byte[] cerBytes,
        byte[] keyBytes,
        string password,
        CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);

        var cerFile = new SdkModels.TaxFile
        {
            PersonId   = personIdExterno,
            Tin        = rfc,
            Base64File = Convert.ToBase64String(cerBytes),
            FileType   = SdkModels.FileType.CertificateCsd,
            Password   = password,
        };
        var cerResp = await sdk.TaxFiles.CreateAsync(cerFile);
        EnsureSucceeded(cerResp, "SubirTaxFilesAsync.Cer");

        var keyFile = new SdkModels.TaxFile
        {
            PersonId   = personIdExterno,
            Tin        = rfc,
            Base64File = Convert.ToBase64String(keyBytes),
            FileType   = SdkModels.FileType.PrivateKeyCsd,
            Password   = password,
        };
        var keyResp = await sdk.TaxFiles.CreateAsync(keyFile);
        EnsureSucceeded(keyResp, "SubirTaxFilesAsync.Key");

        return new TaxFilesSubidos(
            CerIdExterno: cerResp.Data.Id,
            KeyIdExterno: keyResp.Data.Id,
            ValidFrom:    cerResp.Data.ValidFrom,
            ValidTo:      cerResp.Data.ValidTo);
    }

    public async Task<DownloadRuleExternaDto> AsegurarDownloadRuleAsync(
        Guid empresaId,
        string personIdExterno,
        SatQueryType satQueryType,
        DownloadType downloadType,
        SatInvoiceStatusFilter satInvoiceStatus,
        string descripcion,
        CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);

        var qtCode  = MapSatQueryTypeToApi(satQueryType);
        var dtCode  = MapDownloadTypeToApi(downloadType);
        var stCode  = MapSatInvoiceStatusToApi(satInvoiceStatus);

        // Buscar rule existente con la misma combinación (idempotencia).
        var list = await sdk.DownloadRules.GetListAsync(pageNumber: 1, pageSize: 100);
        EnsureSucceeded(list, "AsegurarDownloadRuleAsync.List");
        var existing = list.Data.Items.FirstOrDefault(r =>
            r.PersonId           == personIdExterno
            && r.SatQueryTypeId  == qtCode
            && r.DownloadTypeId  == dtCode
            && r.SatInvoiceStatusId == stCode);
        if (existing is not null)
        {
            _logger.LogInformation("[FiscalApiSdkAdapter] Reusando DownloadRule existente {Id}.", existing.Id);
            return MapRule(existing);
        }

        var nueva = new SdkModels.DownloadRule
        {
            PersonId           = personIdExterno,
            Description        = descripcion,
            SatQueryTypeId     = qtCode,
            DownloadTypeId     = dtCode,
            SatInvoiceStatusId = stCode,
        };
        var created = await sdk.DownloadRules.CreateAsync(nueva);
        EnsureSucceeded(created, "AsegurarDownloadRuleAsync.Create");
        return MapRule(created.Data);
    }

    // ────────────────────────────── Operación ───────────────────────────

    public async Task<SolicitudDescargaExternaDto> CrearSolicitudAsync(
        Guid empresaId,
        string ruleIdExterno,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);

        var nueva = new SdkModels.DownloadRequest
        {
            DownloadRuleId        = ruleIdExterno,
            DownloadRequestTypeId = "Manual",
            StartDate             = startDate.UtcDateTime,
            EndDate               = endDate.UtcDateTime,
        };
        var created = await sdk.DownloadRequests.CreateAsync(nueva);
        EnsureSucceeded(created, "CrearSolicitudAsync");
        return MapRequest(created.Data);
    }

    public async Task<SolicitudDescargaExternaDto> ConsultarSolicitudAsync(
        Guid empresaId,
        string requestIdExterno,
        CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);
        var resp = await sdk.DownloadRequests.GetByIdAsync(requestIdExterno);
        EnsureSucceeded(resp, "ConsultarSolicitudAsync");
        return MapRequest(resp.Data);
    }

    public async IAsyncEnumerable<MetaItemDto> ListarMetaItemsAsync(
        Guid empresaId,
        string requestIdExterno,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);
        // Por ahora SDK solo expone GetMetadataItemsAsync sin paginación
        // explícita (ver SDK source). Si en el futuro se exponen más
        // páginas, se itera aquí.
        var resp = await sdk.DownloadRequests.GetMetadataItemsAsync(requestIdExterno);
        EnsureSucceeded(resp, "ListarMetaItemsAsync");
        foreach (var item in resp.Data.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return MapMetaItem(item);
        }
    }

    public async IAsyncEnumerable<XmlCfdiItemDto> ListarXmlsAsync(
        Guid empresaId,
        string requestIdExterno,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);
        var resp = await sdk.DownloadRequests.GetXmlsAsync(requestIdExterno);
        EnsureSucceeded(resp, "ListarXmlsAsync");

        foreach (var item in resp.Data.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.Base64Content)) continue;

            byte[] xmlBytes;
            try { xmlBytes = Convert.FromBase64String(item.Base64Content); }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex,
                    "[FiscalApiSdkAdapter] request={Request}: item con base64 inválido — skip.",
                    requestIdExterno);
                continue;
            }

            var uuid = ExtractUuidFromXml(xmlBytes);
            if (uuid is null)
            {
                _logger.LogWarning(
                    "[FiscalApiSdkAdapter] request={Request}: XML sin TimbreFiscalDigital — skip.",
                    requestIdExterno);
                continue;
            }

            yield return new XmlCfdiItemDto(Uuid: uuid, XmlBytes: xmlBytes);
        }
    }

    /// <summary>
    /// Extrae el atributo UUID del nodo <c>tfd:TimbreFiscalDigital</c>
    /// con un regex contra el texto del XML. Evitamos parsear con
    /// <c>XmlReader</c> en este módulo (el parser real vive en CxP) — un
    /// regex sobre el atributo basta porque CFDI 4.0 garantiza que el
    /// timbre aparece una sola vez por documento.
    /// </summary>
    private static readonly Regex UuidPattern = new(
        @"<(?:[\w]+:)?TimbreFiscalDigital\b[^>]*\bUUID\s*=\s*[""']([0-9a-fA-F-]{36})[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string? ExtractUuidFromXml(byte[] xmlBytes)
    {
        // Detección rápida del BOM UTF-8 (CFDIs vienen indistintamente).
        var offset = xmlBytes.Length >= 3
                     && xmlBytes[0] == 0xEF && xmlBytes[1] == 0xBB && xmlBytes[2] == 0xBF ? 3 : 0;
        var text = Encoding.UTF8.GetString(xmlBytes, offset, xmlBytes.Length - offset);
        var match = UuidPattern.Match(text);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    // ────────────────────────── Refresh por UUID ────────────────────────

    public async Task<EstatusUuidDto> ConsultarEstatusUuidAsync(
        Guid empresaId,
        string uuidCfdi,
        string rfcEmisor,
        string rfcReceptor,
        decimal total,
        CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);
        var req = new SdkModels.InvoiceStatusRequest
        {
            InvoiceUuid  = uuidCfdi,
            IssuerTin    = rfcEmisor,
            RecipientTin = rfcReceptor,
            InvoiceTotal = total,
        };
        var resp = await sdk.Invoices.GetStatusAsync(req);
        EnsureSucceeded(resp, "ConsultarEstatusUuidAsync");
        var data = resp.Data;
        return new EstatusUuidDto(
            Uuid:                uuidCfdi,
            EstatusSat:          data?.Status            ?? "Desconocido",
            EsCancelable:        data?.CancelableStatus,
            EstatusCancelacion:  data?.CancellationStatus,
            ConsultadoEn:        DateTimeOffset.UtcNow);
    }

    // ─────────────────────────────── Health ─────────────────────────────

    public async Task<PingResultDto> PingAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);
            // La sobrecarga paginada GetListAsync(page, size) del SDK es un
            // NotImplementedException ("Utiliza GetListAsync y ListCatalogAsync
            // en su lugar") — el ping usa la versión sin parámetros, que sí
            // pega GET /api/v4/download-catalogs (bug FAC-DET-PR8; explotó
            // en el primer Probar conexión con credenciales reales).
            var resp = await sdk.DownloadCatalogs.GetListAsync();
            stopwatch.Stop();
            return new PingResultDto(
                Exitosa:      resp.Succeeded,
                StatusCode:   resp.HttpStatusCode,
                Mensaje:      resp.Succeeded ? "OK" : (resp.Message ?? "(sin mensaje)"),
                TiempoMs:     stopwatch.ElapsedMilliseconds,
                ConsultadoEn: DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new PingResultDto(
                Exitosa:      false,
                StatusCode:   0,
                Mensaje:      ex.Message,
                TiempoMs:     stopwatch.ElapsedMilliseconds,
                ConsultadoEn: DateTimeOffset.UtcNow);
        }
    }

    // ─────────────────────────────── Helpers ────────────────────────────

    private static void EnsureSucceeded<T>(SdkCommon.ApiResponse<T> resp, string operacion)
    {
        if (resp.Succeeded) return;
        var detalles = resp.Details ?? string.Empty;
        throw new InvalidOperationException(
            $"FiscalAPI {operacion} falló (HTTP {resp.HttpStatusCode}): " +
            $"{resp.Message ?? "(sin mensaje)"} {detalles}".Trim());
    }

    private static PersonExterno MapPerson(SdkModels.Person p) =>
        new(IdExterno:      p.Id,
            Rfc:            p.Tin ?? string.Empty,
            LegalName:      p.LegalName ?? string.Empty,
            ZipCode:        p.ZipCode,
            SatTaxRegimeId: p.SatTaxRegimeId,
            SatCfdiUseId:   p.SatCfdiUseId);

    private static DownloadRuleExternaDto MapRule(SdkModels.DownloadRule r) =>
        new(IdExterno:        r.Id,
            Tin:              r.Tin ?? string.Empty,
            SatQueryType:     MapSatQueryTypeFromApi(r.SatQueryTypeId),
            DownloadType:     MapDownloadTypeFromApi(r.DownloadTypeId),
            SatInvoiceStatus: MapSatInvoiceStatusFromApi(r.SatInvoiceStatusId),
            // El SDK expone `IsTest` solo en el response; campo no en el shape genérico.
            // PLATFORM-TODO(<FiscalApiSdkIsTestField>): exponer cuando se agregue.
            IsTest:           false);

    private static SolicitudDescargaExternaDto MapRequest(SdkModels.DownloadRequest r) =>
        new(IdExterno:                  r.Id,
            SatRequestStatusId:         ParseIntOrNull(r.SatRequestStatusId),
            DownloadRequestStatusId:    ParseIntOrNull(r.DownloadRequestStatusId),
            InvoiceCount:               r.InvoiceCount,
            LastAttemptDate:            r.LastAttemptDate is DateTime la ? new DateTimeOffset(la, TimeSpan.Zero) : null,
            NextAttemptDate:            r.NextAttemptDate is DateTime na ? new DateTimeOffset(na, TimeSpan.Zero) : null,
            CreatedAt:                  new DateTimeOffset(r.CreatedAt, TimeSpan.Zero));

    private static MetaItemDto MapMetaItem(SdkModels.MetadataItem m) =>
        new(Uuid:                  m.InvoiceUuid ?? string.Empty,
            RfcEmisor:             m.IssuerTin ?? string.Empty,
            NombreEmisor:          m.IssuerName ?? string.Empty,
            RfcReceptor:           m.RecipientTin,
            NombreReceptor:        m.RecipientName,
            FechaCfdi:             new DateTimeOffset(m.InvoiceDate, TimeSpan.Zero),
            FechaCertificacionSat: m.SatCertificationDate == default ? null : new DateTimeOffset(m.SatCertificationDate, TimeSpan.Zero),
            Total:                 m.Amount,
            TipoComprobante:       m.InvoiceType ?? string.Empty,
            EstatusSat:            m.Status == 1 ? "Vigente" : (m.Status == 0 ? "Cancelado" : "Desconocido"),
            FechaCancelacion:      m.CancellationDate == default ? null : new DateTimeOffset(m.CancellationDate, TimeSpan.Zero));

    private static int? ParseIntOrNull(string? value)
        => int.TryParse(value, out var n) ? n : null;

    private static string MapSatQueryTypeToApi(SatQueryType qt) => qt switch
    {
        SatQueryType.Metadata    => "Metadata",
        SatQueryType.Cfdi        => "CFDI",
        SatQueryType.Retenciones => "Retenciones",
        _ => throw new ArgumentOutOfRangeException(nameof(qt))
    };

    private static SatQueryType MapSatQueryTypeFromApi(string? id) => id switch
    {
        "Metadata"    => SatQueryType.Metadata,
        "CFDI"        => SatQueryType.Cfdi,
        "Retenciones" => SatQueryType.Retenciones,
        _             => SatQueryType.Metadata,
    };

    private static string MapDownloadTypeToApi(DownloadType dt) => dt switch
    {
        DownloadType.Emitidos  => "Emitidos",
        DownloadType.Recibidos => "Recibidos",
        DownloadType.Uuid      => "Uuid",
        _ => throw new ArgumentOutOfRangeException(nameof(dt))
    };

    private static DownloadType MapDownloadTypeFromApi(string? id) => id switch
    {
        "Emitidos"  => DownloadType.Emitidos,
        "Recibidos" => DownloadType.Recibidos,
        "Uuid"      => DownloadType.Uuid,
        _           => DownloadType.Recibidos,
    };

    private static string MapSatInvoiceStatusToApi(SatInvoiceStatusFilter st) => st switch
    {
        SatInvoiceStatusFilter.Todos     => string.Empty,
        SatInvoiceStatusFilter.Vigente   => "Vigente",
        SatInvoiceStatusFilter.Cancelado => "Cancelado",
        _ => throw new ArgumentOutOfRangeException(nameof(st))
    };

    private static SatInvoiceStatusFilter MapSatInvoiceStatusFromApi(string? id) => id switch
    {
        ""          => SatInvoiceStatusFilter.Todos,
        null        => SatInvoiceStatusFilter.Todos,
        "Vigente"   => SatInvoiceStatusFilter.Vigente,
        "Cancelado" => SatInvoiceStatusFilter.Cancelado,
        _           => SatInvoiceStatusFilter.Vigente,
    };

    private static string GenerarPasswordRandom()
        => $"Mil!{Guid.NewGuid():N}".Substring(0, 24);
}
