using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter de CxP que implementa el puerto inverso
/// <see cref="IFiscalCfdiReceiver"/> declarado en
/// <c>Millet.Integraciones.Fiscal.Domain.Ports</c>.
///
/// <para>
/// Lo invoca <c>DescargaPollerWorker</c> (módulo Integraciones.Fiscal,
/// PR-11) por cada CFDI cosechado de un <c>DownloadRequest</c> Terminado
/// creado contra una rule <c>SatQueryType=CFDI</c> (D1 revisada en PR-14
/// — doc 02 §13.10). Cada llamada incluye la metadata SAT del meta-item
/// más el XML completo emparejado por UUID.
/// </para>
///
/// <para>
/// Flujo del adapter:
/// </para>
/// <list type="number">
///   <item>Dedupe por <c>uuid_cfdi_valor</c>. Si ya existe, skip
///   (idempotencia frente a reentregas del worker o solicitudes
///   traslapadas).</item>
///   <item>Parsea el XML con <see cref="IXmlCfdiParser"/>. Si el parse
///   falla, loggea warning y descarta (el meta-item solo no nos sirve
///   sin XML — esa era la deuda del flujo Metadata-only).</item>
///   <item>Calcula SHA-256 del XML para deduplicación secundaria.</item>
///   <item>Guarda el XML en <see cref="ICfdiBlobStorage"/>.</item>
///   <item>Crea <see cref="CfdiRecibido"/> via <see cref="CfdiRecibido.Ingresar"/>
///   en estado <see cref="EstadoCfdiRecibido.PorProcesar"/> (con XML, hash y
///   blob ref reales — no <c>MetadataOnly</c>).</item>
/// </list>
///
/// <para>
/// <b>Sin transacciones explícitas</b>: una llamada = una fila → un
/// SaveChanges. El worker llama N veces para N CFDIs; si crashea a mitad,
/// los anteriores ya están persistidos y la próxima corrida reentrega los
/// siguientes (idempotencia natural por el dedupe UUID).
/// </para>
/// </summary>
public sealed class FiscalCfdiReceiverAdapter : IFiscalCfdiReceiver
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IXmlCfdiParser _parser;
    private readonly ICfdiBlobStorage _blob;
    private readonly IClock _clock;
    private readonly ILogger<FiscalCfdiReceiverAdapter> _logger;

    public FiscalCfdiReceiverAdapter(
        CuentasPorPagarDbContext db,
        IXmlCfdiParser parser,
        ICfdiBlobStorage blob,
        IClock clock,
        ILogger<FiscalCfdiReceiverAdapter> logger)
    {
        _db = db;
        _parser = parser;
        _blob = blob;
        _clock = clock;
        _logger = logger;
    }

    public async Task IngresarCfdiAsync(
        CfdiCosechadoPayload payload, CancellationToken cancellationToken)
    {
        var uuidNormalizado = payload.Uuid.Trim().ToUpperInvariant();

        // 1) Dedupe por UUID. Si ya existe, no hacer nada (idempotente).
        var existente = await _db.CfdisRecibidos
            .AsNoTracking()
            .Where(c => c.UuidCfdi.Valor == uuidNormalizado)
            .Select(c => new { c.Id, c.Estado })
            .FirstOrDefaultAsync(cancellationToken);

        if (existente is not null)
        {
            _logger.LogDebug(
                "[FiscalCfdiReceiverAdapter] UUID {Uuid} ya existe en estado {Estado} — skip.",
                uuidNormalizado, existente.Estado);
            return;
        }

        // 2) Parsear XML. Si está corrupto o sin TFD, descartamos (el
        //    meta-item solo no nos sirve — necesitamos los conceptos +
        //    impuestos para captura en pasivos).
        DatosCfdiParseados datos;
        try
        {
            datos = _parser.Parsear(System.Text.Encoding.UTF8.GetString(payload.XmlBytes));
        }
        catch (CfdiParseException ex)
        {
            _logger.LogWarning(ex,
                "[FiscalCfdiReceiverAdapter] UUID {Uuid} con XML inválido ({Codigo}) — descartando.",
                payload.Uuid, ex.Codigo);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[FiscalCfdiReceiverAdapter] UUID {Uuid} con XML no parseable — descartando.",
                payload.Uuid);
            return;
        }

        UuidCfdi uuid;
        RfcMexicano rfcEmisor, rfcReceptor;
        try
        {
            uuid = UuidCfdi.Parse(datos.UuidCfdi);
            rfcEmisor = RfcMexicano.Parse(datos.RfcEmisor);
            rfcReceptor = RfcMexicano.Parse(datos.RfcReceptor);
        }
        catch (BusinessRuleException ex)
        {
            _logger.LogWarning(ex,
                "[FiscalCfdiReceiverAdapter] UUID {Uuid} con datos parseados inválidos — descartando.",
                payload.Uuid);
            return;
        }

        // 3) Hash + blob.
        var xmlHashSha256 = Convert.ToHexString(SHA256.HashData(payload.XmlBytes)).ToLowerInvariant();
        using var xmlStream = new MemoryStream(payload.XmlBytes, writable: false);
        var xmlBlobRef = await _blob.GuardarXmlAsync(
            uuid.Valor, datos.FechaCfdi, xmlStream, cancellationToken);

        // 4) Crear agregado con datos del XML parseado (la metadata SAT
        //    del payload se usa solo para auditoría — los importes y
        //    moneda canónicos vienen del XML, que es la fuente de verdad
        //    SAT). El canal queda fijo en DescargaSat.
        var cfdi = CfdiRecibido.Ingresar(
            empresaId:                 payload.EmpresaId,
            uuid:                      uuid,
            rfcEmisor:                 rfcEmisor,
            rfcReceptor:               rfcReceptor,
            tipo:                      datos.Tipo,
            folio:                     datos.Folio,
            serie:                     datos.Serie,
            fechaCfdi:                 datos.FechaCfdi,
            total:                     datos.Total,
            subtotal:                  datos.Subtotal,
            impuestosTrasladados:      datos.ImpuestosTrasladados,
            retenciones:               datos.Retenciones,
            moneda:                    datos.Moneda,
            tipoCambio:                datos.TipoCambio,
            canalOrigen:               CanalOrigenCfdi.DescargaSat,
            fechaRecepcion:            _clock.UtcNow,
            xmlBlobRef:                xmlBlobRef,
            pdfBlobRef:                null,
            xmlHashSha256:             xmlHashSha256,
            solicitudDescargaId:       payload.SolicitudDescargaId,
            requestIdExternoFiscalApi: payload.RequestIdExterno);

        _db.CfdisRecibidos.Add(cfdi);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[FiscalCfdiReceiverAdapter] UUID {Uuid} ingresado PorProcesar para empresa {Empresa}.",
            uuidNormalizado, payload.EmpresaId);
    }
}
