using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Timbrado.Contingencias;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Timbrado.Adopcion;

/// <summary>
/// M-284 (86e3zr751): adopta en el ERP el CFDI que el PAC SÍ timbró cuando la respuesta no llegó
/// (comprobante «en proceso» o «Timbrado fallido» por falta de respuesta). Facturación descarga el XML
/// del portal del PAC y lo carga; no se vuelve a timbrar ni se duplica el CFDI ante el SAT.
/// Con <see cref="SoloRevisar"/> devuelve el cotejo ERP contra XML sin cambiar nada.
/// </summary>
public sealed record AdoptarCfdiTimbradoCommand(Guid ComprobanteId, string Xml, bool SoloRevisar = false)
    : IRequest<AdoptarCfdiTimbradoResponse>;

/// <summary>Un dato cotejado: lo que tiene el ERP, lo que trae el XML y si coinciden.</summary>
public sealed record CampoCotejado(string Campo, string Erp, string Xml, bool Coincide);

public sealed record AdoptarCfdiTimbradoResponse(
    Guid Id, string Folio, string Estado, string? Uuid, bool Adoptado, IReadOnlyList<CampoCotejado> Cotejo);

public sealed class AdoptarCfdiTimbradoValidator : AbstractValidator<AdoptarCfdiTimbradoCommand>
{
    public AdoptarCfdiTimbradoValidator()
    {
        RuleFor(c => c.ComprobanteId).NotEmpty();
        RuleFor(c => c.Xml).NotEmpty().WithMessage("Carga el XML del CFDI timbrado.")
            .MaximumLength(LectorCfdiTimbrado.TamanoMaximo).WithMessage("El XML excede 2 MB; no parece un CFDI.");
    }
}

public sealed class AdoptarCfdiTimbradoHandler(
    FacturacionDbContext db,
    ISender sender,
    ICfdiTimbradoPort fiscal,
    ICfdiRepositorioPort cfdiRepo,
    IIntegrationEventPublisher eventos,
    ValidadorReceptorFiscal receptorFiscal,
    ICurrentUserContext user,
    IClock clock,
    IProductosReadPort? productos = null,
    IConfiguracionPacResolver? configuracionPac = null,
    ICertificadoSatProvider? certificadosSat = null)
    : IRequestHandler<AdoptarCfdiTimbradoCommand, AdoptarCfdiTimbradoResponse>
{
    /// <summary>Fallos en los que el PAC pudo haber timbrado (los mismos que exigen confirmar el reintento).</summary>
    internal static readonly string[] CodigosAmbiguos = ["PAC_TIMEOUT", "PAC_SIN_RESPUESTA", "PAC_RESPUESTA_INCOMPLETA"];

    private static readonly TimeZoneInfo ZonaFiscal = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    public async Task<AdoptarCfdiTimbradoResponse> Handle(AdoptarCfdiTimbradoCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var comprobante = await db.Comprobantes.FirstOrDefaultAsync(c => c.Id == command.ComprobanteId, ct)
            ?? throw new EntityNotFoundException("COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante {command.ComprobanteId}.");
        ExigirAdoptable(comprobante);

        var xml = LectorCfdiTimbrado.Leer(command.Xml);
        var cotejo = await CotejarAsync(comprobante, xml, command.Xml, ct);
        if (command.SoloRevisar)
            return Respuesta(comprobante, adoptado: false, cotejo);

        var diferencias = cotejo.Where(c => !c.Coincide).Select(Mensaje).ToList();
        if (diferencias.Count > 0)
            throw new BusinessRuleException("CFDI_NO_CORRESPONDE", string.Join(" ", diferencias));
        // El UUID se guarda como lo entrega el PAC; se compara sin importar mayúsculas.
        string[] variantes = [xml.Uuid, xml.Uuid.ToUpperInvariant(), xml.Uuid.ToLowerInvariant()];
        if (await db.Comprobantes.AnyAsync(c => c.Id != comprobante.Id && c.Uuid != null
                && variantes.Contains(c.Uuid), ct))
            throw new BusinessRuleException("CFDI_UUID_EN_USO",
                $"El UUID {xml.Uuid} ya pertenece a otro comprobante del ERP; este XML no es de este comprobante.");

        await CargarNavegacionesAsync(comprobante, ct);
        var ahora = clock.UtcNow;
        var fecha = new DateTimeOffset(DateTime.SpecifyKind(xml.FechaTimbradoLocal, DateTimeKind.Unspecified),
            ZonaFiscal.GetUtcOffset(xml.FechaTimbradoLocal)).ToUniversalTime();
        var archivoId = await cfdiRepo.GuardarAsync(new CfdiArchivoNuevo(
            Uuid: xml.Uuid, XmlContenido: command.Xml.TrimStart('﻿'), PdfContenido: null,
            SelloCfdi: xml.SelloCfd, SelloSat: xml.SelloSat, NoCertificadoSat: xml.NoCertificadoSat,
            RfcProveedorCertificacion: xml.RfcProvCertif, FechaTimbrado: fecha), ct);

        if (comprobante.Estado == EstadoTimbrado.TimbradoFallido)
        {
            comprobante.ReabrirParaReintentoTimbrado();
            comprobante.MarcarTimbradoEnProceso();
        }
        comprobante.MarcarTimbrado(xml.Uuid, xml.SelloCfd, xml.SelloSat, xml.NoCertificadoSat, fecha,
            xml.RfcProvCertif, archivoId);
        comprobante.RecalcularDatosTimbre(command.Xml.TrimStart('﻿'));

        var previos = await db.BitacorasIntentoTimbrado.CountAsync(b => b.ComprobanteId == comprobante.Id, ct);
        db.BitacorasIntentoTimbrado.Add(BitacoraIntentoTimbrado.Registrar(
            comprobante.EmpresaId, comprobante.Id, previos + 1, ResultadoIntentoTimbrado.Timbrado,
            "CFDI_ADOPTADO", $"CFDI adoptado del XML del PAC por {user.UserName ?? "un usuario"}; no se volvió a timbrar.", ahora));
        await ContingenciasTimbrado.AplicarIntentoAsync(db, comprobante, null, null, timbrado: true, ahora, ct);

        // Los efectos posteriores al timbrado (evento contable, pedido, A+W, NC de ranura, REP) una sola vez.
        // M-285: en la factura de venta se registran con el timbrado y se aplican uno por uno.
        var efectos = new EfectosPostTimbrado(db, sender, fiscal, cfdiRepo, eventos, receptorFiscal, productos);
        if (comprobante is FacturaVenta venta)
            await efectos.RegistrarAsync(venta, await Integration.ClienteContableFacturacion.ResolverAsync(db, venta, ct), ct);
        else
            await efectos.AplicarAsync(comprobante, ahora, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException("COMPROBANTE_CAMBIO",
                "El comprobante cambió mientras se adoptaba el CFDI (otro reintento o adopción). Recarga y revisa su estado.", ex);
        }
        if (comprobante is FacturaVenta timbrada)
            await efectos.EjecutarPendientesAsync(timbrada, ahora, ct);
        return Respuesta(comprobante, adoptado: true, cotejo);
    }

    private static void ExigirAdoptable(Comprobante c)
    {
        if (c.Estado is EstadoTimbrado.Timbrado or EstadoTimbrado.CancelacionPendiente or EstadoTimbrado.Cancelado)
            throw new BusinessRuleException("COMPROBANTE_YA_TIMBRADO", "El comprobante ya está timbrado.");
        var ambiguo = c.Estado == EstadoTimbrado.TimbradoEnProceso
            || (c.Estado == EstadoTimbrado.TimbradoFallido
                && CodigosAmbiguos.Contains(c.TimbradoErrorCodigo, StringComparer.OrdinalIgnoreCase));
        if (!ambiguo)
            throw new BusinessRuleException("COMPROBANTE_NO_ADOPTABLE",
                "Solo se adopta el CFDI de un comprobante cuyo timbrado no se confirmó (en proceso o sin respuesta del PAC). " +
                "Un rechazo del PAC no generó CFDI: corrige y reintenta.");
    }

    private async Task<IReadOnlyList<CampoCotejado>> CotejarAsync(Comprobante c, CfdiTimbradoLeido xml, string xmlTexto, CancellationToken ct)
    {
        // Con el PAC en modo sandbox, el adaptador timbra con identidades de prueba: también se aceptan.
        var config = configuracionPac is null ? null
            : await configuracionPac.ResolverAsync(c.EmpresaId, ProveedorPac.FiscalApi, ct);
        // Identidades de prueba activas = ambiente de pruebas del PAC (sandbox).
        var enPruebas = config?.EmisorSandbox is not null || config?.ReceptorSandbox is not null;
        var emisor = c.SnapshotEmisor().Rfc;
        var tipo = c.Tipo switch
        {
            TipoComprobante.Ingreso => "I",
            TipoComprobante.Egreso => "E",
            TipoComprobante.Traslado => "T",
            TipoComprobante.Pago => "P",
            _ => c.Tipo.ToString(),
        };
        var campos = new List<CampoCotejado>
        {
            Campo("Serie", CfdiEmisionBuilder.SerieDe(c), xml.Serie ?? "—"),
            // QA O3: el PAC de pruebas reescribe el folio; con las identidades de prueba activas no se exige
            // (el sello del emisor, verificado abajo, sigue detectando cualquier edición del XML).
            enPruebas && !Igual(CfdiEmisionBuilder.FolioDe(c), xml.Folio)
                ? new("Folio", CfdiEmisionBuilder.FolioDe(c), $"{xml.Folio ?? "—"} (el PAC de pruebas reescribe el folio)", true)
                : Campo("Folio", CfdiEmisionBuilder.FolioDe(c), xml.Folio ?? "—"),
            Campo("Tipo de comprobante", tipo, xml.TipoDeComprobante),
            new("RFC emisor", emisor, xml.RfcEmisor,
                Igual(emisor, xml.RfcEmisor) || Igual(config?.EmisorSandbox?.Rfc, xml.RfcEmisor)),
            new("RFC receptor", c.ReceptorRfc, xml.RfcReceptor,
                Igual(c.ReceptorRfc, xml.RfcReceptor) || Igual(config?.ReceptorSandbox?.Rfc, xml.RfcReceptor)),
        };
        // REP y traslado llevan Total 0 en el CFDI; el importe se coteja en ingresos y egresos.
        if (c.Tipo is TipoComprobante.Ingreso or TipoComprobante.Egreso)
            campos.Add(new("Total", Importe(c.Total), Importe(xml.Total), c.Total == xml.Total));

        // QA O1: el Timbre Fiscal Digital debe ser el de este CFDI (su SelloCFD es el Sello del comprobante).
        var selloTimbre = !string.IsNullOrWhiteSpace(xml.SelloComprobante)
            && string.Equals(xml.SelloComprobante.Trim(), xml.SelloCfd?.Trim(), StringComparison.Ordinal);
        campos.Add(new(CampoTimbre, "Sello del comprobante", selloTimbre ? "Coincide" : "No coincide", selloTimbre));

        // QA O1: el CFDI existe y está vigente ante el SAT (UUID, RFC emisor y receptor, total y sello).
        campos.Add(await EstatusSatAsync(c, xml, ct));

        // QA O1 (re-QA): verificación criptográfica. El sello del emisor sobre la cadena original del XML
        // recibido (detecta folio, serie, conceptos o importes editados) y el sello del SAT sobre el timbre.
        var emisorSello = VerificadorSelloCfdi.VerificarSelloEmisor(xmlTexto);
        campos.Add(new(CampoSelloEmisor, "Válido", emisorSello.Detalle, emisorSello.Valido));
        var satSello = await VerificadorSelloCfdi.VerificarSelloSatAsync(xmlTexto, certificadosSat, ct);
        campos.Add(!satSello.Verificable && enPruebas
            ? new(CampoSelloSat, "Válido", "No verificable en pruebas (el certificado del SAT de pruebas no está publicado)", true)
            : new(CampoSelloSat, "Válido", satSello.Detalle, satSello.Valido));
        return campos;
    }

    private const string CampoTimbre = "Timbre fiscal";
    private const string CampoSelloEmisor = "Sello del emisor";
    private const string CampoSelloSat = "Sello del SAT";
    private const string CampoSat = "Estatus en el SAT";

    private async Task<CampoCotejado> EstatusSatAsync(Comprobante c, CfdiTimbradoLeido xml, CancellationToken ct)
    {
        try
        {
            var sello = xml.SelloComprobante?.Trim();
            var estatus = await fiscal.ConsultarEstatusAsync(new EstatusCfdiSolicitud(
                c.EmpresaId, xml.Uuid, xml.RfcEmisor, xml.RfcReceptor, xml.Total,
                sello is { Length: >= 8 } ? sello[^8..] : null), ct);
            return new(CampoSat, "Vigente", estatus.EstatusSat, estatus.EsVigente);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(CampoSat, "Vigente", $"No se pudo consultar: {ex.Message}", false);
        }
    }

    private static CampoCotejado Campo(string campo, string erp, string xml) => new(campo, erp, xml, Igual(erp, xml));

    private static bool Igual(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Importe(decimal valor) => valor.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string Mensaje(CampoCotejado c)
    {
        if (c.Campo == CampoSelloEmisor)
            return c.Xml.EndsWith('.') ? c.Xml : c.Xml + ".";
        if (c.Campo == CampoSelloSat)
            return c.Xml.EndsWith('.') ? c.Xml : c.Xml + ".";
        if (c.Campo == CampoTimbre)
            return "El Timbre Fiscal Digital del XML no corresponde al sello del comprobante; el archivo pudo modificarse.";
        if (c.Campo == CampoSat)
            return c.Xml.StartsWith("No se pudo consultar", StringComparison.Ordinal)
                ? $"No se pudo verificar el CFDI ante el SAT ({c.Xml}). Intenta más tarde."
                : $"El SAT no reporta este CFDI como vigente (estatus: {c.Xml}); no se adopta.";
        var sujeto = c.Campo switch
        {
            "Serie" => "La serie",
            "Folio" => "El folio",
            "Total" => "El total",
            "Tipo de comprobante" => "El tipo de comprobante",
            _ => $"El {c.Campo}",
        };
        return $"{sujeto} del XML es {c.Xml}; el comprobante es {c.Erp}.";
    }

    private static AdoptarCfdiTimbradoResponse Respuesta(Comprobante c, bool adoptado, IReadOnlyList<CampoCotejado> cotejo) =>
        new(c.Id, c.Folio, c.Estado.ToString(), c.Uuid, adoptado, cotejo);

    /// <summary>Navegaciones que usan los efectos (las mismas que carga el reintento por tipo).</summary>
    private async Task CargarNavegacionesAsync(Comprobante c, CancellationToken ct)
    {
        switch (c)
        {
            case FacturaVenta f:
                await db.FacturasVenta.Include(x => x.Lineas).Include(x => x.Relaciones)
                    .Include(x => x.ComplementoCce!).ThenInclude(x => x.Lineas)
                    .Where(x => x.Id == f.Id).LoadAsync(ct);
                break;
            case Domain.Anticipos.FacturaAnticipo a:
                await db.FacturasAnticipo.Include(x => x.Relaciones).Where(x => x.Id == a.Id).LoadAsync(ct);
                break;
            case Domain.NotasCredito.NotaCredito n:
                await db.NotasCredito.Include(x => x.Relaciones).Where(x => x.Id == n.Id).LoadAsync(ct);
                break;
            case Domain.Repp.ReciboPago r:
                await db.RecibosPago.Include(x => x.FacturasPagadas).ThenInclude(x => x.Impuestos)
                    .Include(x => x.Relaciones).Where(x => x.Id == r.Id).LoadAsync(ct);
                break;
        }
    }
}
