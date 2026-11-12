using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;

/// <summary>
/// M-293b: SDK fiscal simulado, en memoria y determinista, para probar la descarga masiva sin PAC, sin red y sin credenciales.
/// Solo se registra con <c>IntegracionesFiscal:Sdk:Modo=Simulado</c> y únicamente en Development (ver
/// <see cref="SdkSimuladoSoloDevelopmentValidator"/>). Todos los datos son ficticios: RFC genéricos, sin FIEL ni CSD.
///
/// <para>
/// Es sin estado: el RFC, el tipo de descarga y la fecha viajan dentro de los identificadores sintéticos (<c>SIM-…</c>), de modo
/// que sigue funcionando tras reiniciar la API. Una solicitud aparece «en proceso» durante unos segundos y luego «completada»
/// con 3 CFDI por día: 2 vigentes y 1 cancelado.
/// </para>
/// </summary>
public sealed class FiscalApiSdkSimulado(ILogger<FiscalApiSdkSimulado> logger, TimeProvider? reloj = null) : IFiscalApiSdkClient
{
    /// <summary>RFC genérico de pruebas del SAT usado como contraparte ficticia.</summary>
    public const string RfcContraparte = "AAA010101AAA";
    public const string RfcPublicoGeneral = "XAXX010101000";
    /// <summary>Segundos que la solicitud simulada tarda en pasar de «en proceso» a «completada».</summary>
    public const int SegundosParaCompletar = 5;

    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    private static readonly (decimal Subtotal, string Tipo, bool Cancelado)[] Plantilla =
    [
        (1000.00m, "I", false),
        (2000.00m, "I", false),
        (500.00m, "I", true),
    ];

    public Task<PersonExterno> AsegurarPersonAsync(Guid empresaId, string rfc, string legalName, string zipCode,
        string satTaxRegimeCode, string? satCfdiUseCode, string email, CancellationToken cancellationToken)
        => Task.FromResult(new PersonExterno($"SIM-PERSON-{rfc.ToUpperInvariant()}", rfc.ToUpperInvariant(), legalName, zipCode, satTaxRegimeCode, satCfdiUseCode));

    public Task SincronizarPersonAsync(Guid empresaId, string personIdExterno, string legalName, string zipCode,
        string? satCfdiUseCode, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>No guarda nada de los archivos: devuelve una vigencia ficticia de un año.</summary>
    public Task<TaxFilesSubidos> SubirTaxFilesAsync(Guid empresaId, string personIdExterno, string rfc, byte[] cerBytes,
        byte[] keyBytes, string password, CancellationToken cancellationToken)
    {
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        return Task.FromResult(new TaxFilesSubidos("SIM-CER", "SIM-KEY", ahora.AddDays(-1), ahora.AddYears(1)));
    }

    public Task<DownloadRuleExternaDto> AsegurarDownloadRuleAsync(Guid empresaId, string personIdExterno, SatQueryType satQueryType,
        DownloadType downloadType, SatInvoiceStatusFilter satInvoiceStatus, string descripcion, CancellationToken cancellationToken)
    {
        var rfc = RfcDe(personIdExterno);
        var id = $"SIM-RULE-{rfc}-{(int)satQueryType}-{(int)downloadType}-{(int)satInvoiceStatus}";
        return Task.FromResult(new DownloadRuleExternaDto(id, rfc, satQueryType, downloadType, satInvoiceStatus, IsTest: true));
    }

    public Task<SolicitudDescargaExternaDto> CrearSolicitudAsync(Guid empresaId, string ruleIdExterno, DateTimeOffset startDate,
        DateTimeOffset endDate, CancellationToken cancellationToken)
    {
        var ahora = _reloj.GetUtcNow();
        var id = $"SIM-REQ-{ruleIdExterno["SIM-RULE-".Length..]}-{startDate:yyyyMMdd}-{ahora.UtcTicks}";
        logger.LogInformation("SDK fiscal SIMULADO: solicitud {Id} creada (sin PAC)", id);
        return Task.FromResult(new SolicitudDescargaExternaDto(id, 1, 1, null, null, ahora.AddSeconds(SegundosParaCompletar), ahora));
    }

    public Task<SolicitudDescargaExternaDto> ConsultarSolicitudAsync(Guid empresaId, string requestIdExterno, CancellationToken cancellationToken)
    {
        var p = Parsear(requestIdExterno);
        var ahora = _reloj.GetUtcNow();
        var completa = (ahora - p.Creada).TotalSeconds >= SegundosParaCompletar;
        return Task.FromResult(completa
            ? new SolicitudDescargaExternaDto(requestIdExterno, 3, 3, Plantilla.Length, ahora, null, p.Creada)
            : new SolicitudDescargaExternaDto(requestIdExterno, 1, 1, null, ahora, p.Creada.AddSeconds(SegundosParaCompletar), p.Creada));
    }

    public async IAsyncEnumerable<MetaItemDto> ListarMetaItemsAsync(Guid empresaId, string requestIdExterno,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var p = Parsear(requestIdExterno);
        for (var i = 0; i < Plantilla.Length; i++)
        {
            var c = Construir(p, i);
            yield return new MetaItemDto(c.Uuid, c.RfcEmisor, c.NombreEmisor, c.RfcReceptor, c.NombreReceptor, c.Fecha,
                c.Fecha.AddMinutes(5), c.Total, c.Tipo, c.Cancelado ? "Cancelado" : "Vigente",
                c.Cancelado ? c.Fecha.AddDays(1) : null);
        }
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<XmlCfdiItemDto> ListarXmlsAsync(Guid empresaId, string requestIdExterno,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var p = Parsear(requestIdExterno);
        if (p.QueryType != (int)SatQueryType.Cfdi) yield break; // Metadata no genera XML, igual que el PAC real.
        for (var i = 0; i < Plantilla.Length; i++)
        {
            var c = Construir(p, i);
            yield return new XmlCfdiItemDto(c.Uuid, Encoding.UTF8.GetBytes(Xml(c)));
        }
        await Task.CompletedTask;
    }

    public Task<EstatusUuidDto> ConsultarEstatusUuidAsync(Guid empresaId, string uuidCfdi, string rfcEmisor, string rfcReceptor,
        decimal total, CancellationToken cancellationToken)
    {
        var cancelado = uuidCfdi.EndsWith("cccccccccccc", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(new EstatusUuidDto(uuidCfdi, cancelado ? "Cancelado" : "Vigente",
            cancelado ? null : "Cancelable sin aceptación", cancelado ? "Cancelado sin aceptación" : null, _reloj.GetUtcNow()));
    }

    public Task<PingResultDto> PingAsync(Guid empresaId, CancellationToken cancellationToken)
        => Task.FromResult(new PingResultDto(true, 200, "Conexión simulada: no hay red ni PAC real.", 1, _reloj.GetUtcNow()));

    // ── Generación determinista ─────────────────────────────────────────

    private sealed record Param(string Rfc, int QueryType, int DownloadType, DateTimeOffset Dia, DateTimeOffset Creada);
    private sealed record Cfdi(string Uuid, string RfcEmisor, string NombreEmisor, string RfcReceptor, string NombreReceptor,
        DateTimeOffset Fecha, decimal Subtotal, decimal Total, string Tipo, bool Cancelado, int Indice);

    private static string RfcDe(string personId) => personId.StartsWith("SIM-PERSON-", StringComparison.Ordinal)
        ? personId["SIM-PERSON-".Length..]
        : personId;

    // SIM-REQ-{rfc}-{query}-{download}-{status}-{yyyyMMdd}-{ticks}
    private static Param Parsear(string requestId)
    {
        var t = requestId.Split('-');
        if (t.Length != 8 || t[0] != "SIM" || t[1] != "REQ")
            throw new InvalidOperationException($"Identificador de solicitud simulada no válido: '{requestId}'.");
        return new Param(t[2], int.Parse(t[3], CultureInfo.InvariantCulture), int.Parse(t[4], CultureInfo.InvariantCulture),
            new DateTimeOffset(DateTime.ParseExact(t[6], "yyyyMMdd", CultureInfo.InvariantCulture), TimeSpan.FromHours(-6)),
            new DateTimeOffset(long.Parse(t[7], CultureInfo.InvariantCulture), TimeSpan.Zero));
    }

    private static Cfdi Construir(Param p, int i)
    {
        var (subtotal, tipo, cancelado) = Plantilla[i];
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"{p.Rfc}|{p.DownloadType}|{p.Dia:yyyyMMdd}|{i}"));
        // Los 6 últimos bytes marcan el estatus: cc = cancelado (lo usa ConsultarEstatusUuid).
        for (var b = 10; b < 16; b++) hash[b] = cancelado ? (byte)0xCC : (byte)0x11;
        var uuid = new Guid(hash).ToString().ToUpperInvariant();
        var recibido = p.DownloadType == (int)DownloadType.Recibidos;
        var (rE, nE, rR, nR) = recibido
            ? (RfcContraparte, "PROVEEDOR SIMULADO SA DE CV", p.Rfc, "RECEPTOR SIMULADO")
            : (p.Rfc, "EMISOR SIMULADO", RfcPublicoGeneral, "PUBLICO EN GENERAL");
        return new Cfdi(uuid, rE, nE, rR, nR, p.Dia.AddHours(10 + i), subtotal, decimal.Round(subtotal * 1.16m, 2), tipo, cancelado, i);
    }

    private static string Xml(Cfdi c)
    {
        var inv = CultureInfo.InvariantCulture;
        var iva = c.Total - c.Subtotal;
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" Version="4.0" Serie="SIM" Folio="{c.Indice + 1}" Fecha="{c.Fecha:yyyy-MM-ddTHH:mm:ss}" FormaPago="99" MetodoPago="PPD" SubTotal="{c.Subtotal.ToString("0.00", inv)}" Moneda="MXN" Total="{c.Total.ToString("0.00", inv)}" TipoDeComprobante="{c.Tipo}" Exportacion="01" LugarExpedicion="64000">
              <cfdi:Emisor Rfc="{c.RfcEmisor}" Nombre="{c.NombreEmisor}" RegimenFiscal="601" />
              <cfdi:Receptor Rfc="{c.RfcReceptor}" Nombre="{c.NombreReceptor}" DomicilioFiscalReceptor="64000" RegimenFiscalReceptor="601" UsoCFDI="G03" />
              <cfdi:Conceptos>
                <cfdi:Concepto ClaveProdServ="01010101" Cantidad="1" ClaveUnidad="ACT" Descripcion="Concepto simulado {c.Indice + 1}" ValorUnitario="{c.Subtotal.ToString("0.00", inv)}" Importe="{c.Subtotal.ToString("0.00", inv)}" ObjetoImp="02">
                  <cfdi:Impuestos>
                    <cfdi:Traslados>
                      <cfdi:Traslado Base="{c.Subtotal.ToString("0.00", inv)}" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="{iva.ToString("0.00", inv)}" />
                    </cfdi:Traslados>
                  </cfdi:Impuestos>
                </cfdi:Concepto>
              </cfdi:Conceptos>
              <cfdi:Impuestos TotalImpuestosTrasladados="{iva.ToString("0.00", inv)}">
                <cfdi:Traslados>
                  <cfdi:Traslado Base="{c.Subtotal.ToString("0.00", inv)}" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="{iva.ToString("0.00", inv)}" />
                </cfdi:Traslados>
              </cfdi:Impuestos>
              <cfdi:Complemento>
                <tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="1.1" UUID="{c.Uuid}" FechaTimbrado="{c.Fecha.AddMinutes(5):yyyy-MM-ddTHH:mm:ss}" RfcProvCertif="SIM010101SIM" SelloCFD="SIMULADO" NoCertificadoSAT="00000000000000000000" SelloSAT="SIMULADO" />
              </cfdi:Complemento>
            </cfdi:Comprobante>
            """;
    }
}
