using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Cfdi.IngresarCfdi;

/// <summary>
/// Handler de <see cref="IngresarCfdiCommand"/>. Flujo (F1-PR1):
/// <list type="number">
///   <item>Lee el XML completo a memoria para parsear + hashear + persistir
///         en blob. Tamaños esperados: 5-50 KB típicos.</item>
///   <item>Calcula el SHA-256 del XML (dedupe secundario por hash en
///         caso de UUID corrupto).</item>
///   <item>Parsea con <see cref="IXmlCfdiParser"/>. Falla → 422 con
///         código <c>CFDI_*</c> del parser.</item>
///   <item>Busca duplicado por UUID en la BD. Si existe, persiste el
///         nuevo registro en estado <see cref="EstadoCfdiRecibido.Duplicado"/>
///         apuntando al original (auditoría completa de qué canal trajo
///         qué CFDI).</item>
///   <item>Guarda XML (y PDF si vino) en blob storage.</item>
///   <item>Persiste el agregado <see cref="CfdiRecibido"/> via EF.</item>
/// </list>
///
/// <para>
/// **Sin EmpresaId del cliente**: lo resuelve del JWT actual. Si no hay
/// JWT (worker de fondo), el caller debe usar un bypass scope con la
/// empresa correcta inyectada en <see cref="ICurrentEmpresaContext"/>.
/// </para>
/// </summary>
public sealed class IngresarCfdiHandler : IRequestHandler<IngresarCfdiCommand, IngresarCfdiResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IXmlCfdiParser _parser;
    private readonly ICfdiBlobStorage _blob;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;
    private readonly IIntegrationEventPublisher _events;

    public IngresarCfdiHandler(
        CuentasPorPagarDbContext db,
        IXmlCfdiParser parser,
        ICfdiBlobStorage blob,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock,
        IIntegrationEventPublisher events)
    {
        _db = db;
        _parser = parser;
        _blob = blob;
        _currentEmpresa = currentEmpresa;
        _clock = clock;
        _events = events;
    }

    public async Task<IngresarCfdiResponse> Handle(
        IngresarCfdiCommand command,
        CancellationToken cancellationToken)
    {
        // EmpresaId: el override solo lo deben enviar workers internos
        // (F2-PR1: descarga masiva SAT, F2-PR2: mailbox). Los endpoints
        // HTTP nunca lo setean — el handler exige Current del JWT.
        Guid empresaId;
        if (command.EmpresaIdOverride is Guid overrideId)
        {
            empresaId = overrideId;
        }
        else if (_currentEmpresa.Current is Guid jwtEmpresaId)
        {
            empresaId = jwtEmpresaId;
        }
        else
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        // 1) Buffer del XML en memoria — el parser, el hash y el blob
        //    son 3 lecturas del stream; copiamos una sola vez.
        using var xmlBuffer = new MemoryStream();
        await command.Xml.CopyToAsync(xmlBuffer, cancellationToken);
        var xmlBytes = xmlBuffer.ToArray();

        // 2) Hash SHA-256 del XML (hex lowercase).
        var xmlHashSha256 = Convert.ToHexString(SHA256.HashData(xmlBytes)).ToLowerInvariant();

        // 3) Parsear (sin retener el stream original — usamos el buffer).
        DatosCfdiParseados datos;
        using (var parseStream = new MemoryStream(xmlBytes, writable: false))
        {
            datos = _parser.Parsear(parseStream);
        }

        var uuid = UuidCfdi.Parse(datos.UuidCfdi);
        var rfcEmisor = RfcMexicano.Parse(datos.RfcEmisor);
        var rfcReceptor = RfcMexicano.Parse(datos.RfcReceptor);

        // 4) Dedupe por UUID.
        var existente = await _db.CfdisRecibidos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UuidCfdi == uuid, cancellationToken);

        // 5) Guardar XML (siempre) + PDF (si vino) en blob.
        using var xmlStorageStream = new MemoryStream(xmlBytes, writable: false);
        var xmlBlobRef = await _blob.GuardarXmlAsync(
            uuid.Valor, datos.FechaCfdi, xmlStorageStream, cancellationToken);
        var pdfBlobRef = await _blob.GuardarPdfAsync(
            uuid.Valor, datos.FechaCfdi, command.Pdf, cancellationToken);

        var ahora = _clock.UtcNow;

        // 6) Construir el agregado. Si hay duplicado, crear nueva fila
        //    en estado Duplicado apuntando al original (ambos canales
        //    quedan trazables; el primer registro mantiene el canal
        //    original y queda intacto).
        var cfdi = CfdiRecibido.Ingresar(
            empresaId: empresaId,
            uuid: uuid,
            rfcEmisor: rfcEmisor,
            rfcReceptor: rfcReceptor,
            tipo: datos.Tipo,
            folio: datos.Folio,
            serie: datos.Serie,
            fechaCfdi: datos.FechaCfdi,
            total: datos.Total,
            subtotal: datos.Subtotal,
            impuestosTrasladados: datos.ImpuestosTrasladados,
            retenciones: datos.Retenciones,
            moneda: datos.Moneda,
            tipoCambio: datos.TipoCambio,
            canalOrigen: command.Canal,
            fechaRecepcion: ahora,
            xmlBlobRef: xmlBlobRef,
            pdfBlobRef: pdfBlobRef,
            xmlHashSha256: xmlHashSha256,
            metodoPago: datos.MetodoPago);

        if (existente is not null)
        {
            // El constraint único sobre UUID rechazaría el insert
            // directo. Para preservar la auditoría de qué canal trajo
            // qué duplicado en F2 (mailbox + SAT), se difiere a una
            // tabla separada. En F1-PR1 simplificamos: rechazamos la
            // ingestión con código del problema y dejamos el original
            // intacto.
            throw new BusinessRuleException(
                "CFDI_DUPLICADO",
                $"Ya existe un CFDI con UUID '{uuid.Valor}' (canal original: {existente.CanalOrigen}, fecha recepción: {existente.FechaRecepcion:O}).");
        }

        _db.CfdisRecibidos.Add(cfdi);

        // Evento de integración: Almacén lo consume para el enlace diferido
        // de recepciones variante A registradas con folio fiscal a mano.
        // El interceptor del outbox lo persiste en la misma TX.
        await _events.PublishAsync(new CfdiRecibidoIngresadoIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: ahora,
            CfdiRecibidoId: cfdi.Id,
            UuidCfdi: cfdi.UuidCfdi.Valor,
            RfcEmisor: cfdi.RfcEmisor.Valor), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new IngresarCfdiResponse(
            Id: cfdi.Id,
            UuidCfdi: cfdi.UuidCfdi.Valor,
            Estado: cfdi.Estado,
            CfdiOriginalId: cfdi.CfdiOriginalId);
    }
}
