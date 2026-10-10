using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.NotaCreditoProveedor.CapturarNotaCredito;

/// <summary>
/// Captura una NC del proveedor (F6-PR1, §7.1 del 01-diseno). El
/// handler intenta resolver la factura origen por
/// <c>UuidRelacionCfdi</c>:
/// <list type="bullet">
///   <item>Si la factura ya existe en CxP → NC nace
///         <see cref="EstadoNotaCredito.Abierta"/>.</item>
///   <item>Si no existe (caso A19, raro) → NC nace
///         <see cref="EstadoNotaCredito.EnEspera"/>; el worker
///         <c>NotaCreditoEnEsperaMatchWorker</c> intenta el match
///         diariamente.</item>
/// </list>
/// </summary>
public sealed record CapturarNotaCreditoCommand(
    Guid? CfdiRecibidoId,
    string UuidCfdi,
    Guid ProveedorId,
    string? FolioProveedor,
    string? SerieProveedor,
    DateTimeOffset FechaCfdi,
    string Moneda,
    decimal? TipoCambio,
    decimal Subtotal,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    TipoNotaCredito Tipo,
    TipoRelacionCfdi TipoRelacionCfdi,
    string UuidRelacionCfdi) : IRequest<CapturarNotaCreditoResponse>;

public sealed record CapturarNotaCreditoResponse(
    Guid Id,
    EstadoNotaCredito Estado,
    Guid? FacturaOrigenId,
    int Version);

public sealed class CapturarNotaCreditoValidator : AbstractValidator<CapturarNotaCreditoCommand>
{
    public CapturarNotaCreditoValidator()
    {
        RuleFor(c => c.UuidCfdi).NotEmpty().Length(36);
        RuleFor(c => c.ProveedorId).NotEmpty();
        RuleFor(c => c.UuidRelacionCfdi).NotEmpty().Length(36);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.Total).GreaterThan(0);
        RuleFor(c => c.Subtotal).GreaterThanOrEqualTo(0);
        RuleFor(c => c.ImpuestosTrasladados).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Retenciones).GreaterThanOrEqualTo(0);
        RuleFor(c => c.FolioProveedor).MaximumLength(40);
        RuleFor(c => c.SerieProveedor).MaximumLength(25);
    }
}

public sealed class CapturarNotaCreditoHandler
    : IRequestHandler<CapturarNotaCreditoCommand, CapturarNotaCreditoResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IMediator _mediator;
    private readonly IClock _clock;
    private readonly Domain.Ports.DatosMaestros.IProveedorReadPort _proveedores;
    private readonly Domain.Cfdi.ICfdiBlobStorage _blob;
    private readonly Domain.Cfdi.IXmlCfdiParser _parser;

    public CapturarNotaCreditoHandler(
        CuentasPorPagarDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IMediator mediator,
        IClock clock, Domain.Ports.DatosMaestros.IProveedorReadPort proveedores, Domain.Cfdi.ICfdiBlobStorage blob, Domain.Cfdi.IXmlCfdiParser parser)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _mediator = mediator; _clock = clock; _proveedores = proveedores; _blob = blob; _parser = parser;
    }

    public async Task<CapturarNotaCreditoResponse> Handle(
        CapturarNotaCreditoCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        if (command.CfdiRecibidoId is Guid cfdiId)
        {
            var documento = await _db.CfdisRecibidos.FirstOrDefaultAsync(c => c.Id == cfdiId, cancellationToken)
                ?? throw new EntityNotFoundException("CFDI_NO_ENCONTRADO", "No se encontró el CFDI de la NC.");
            var proveedor = await _proveedores.ObtenerAsync(command.ProveedorId, cancellationToken)
                ?? throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO", "No se encontró el proveedor de la NC.");
            if (documento.XmlBlobRef is null) throw new BusinessRuleException("NC_XML_REQUERIDO", "El CFDI ligado requiere su XML original.");
            await using var stream = await _blob.LeerXmlAsync(documento.XmlBlobRef, cancellationToken)
                ?? throw new BusinessRuleException("NC_XML_REQUERIDO", "No se pudo leer el XML original de la NC.");
            var datos = _parser.Parsear(stream);
            if (datos.RfcEmisor != proveedor.Rfc || datos.Tipo != Domain.Cfdi.TipoCfdi.Egreso || !string.Equals(datos.UuidCfdi, command.UuidCfdi.Trim(), StringComparison.OrdinalIgnoreCase) ||
                datos.Total != command.Total || !string.Equals(datos.Moneda, command.Moneda, StringComparison.OrdinalIgnoreCase) ||
                datos.CfdiRelacionados is not { Count: 1 } || datos.CfdiRelacionados[0].TipoRelacion != ((int)command.TipoRelacionCfdi).ToString("00") ||
                datos.CfdiRelacionados[0].Uuids.Count != 1 || !string.Equals(datos.CfdiRelacionados[0].Uuids[0], command.UuidRelacionCfdi, StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("NC_XML_NO_COINCIDE", "El XML de la NC debe coincidir en proveedor, UUID, tipo, moneda, importe y relación fiscal.");
        }
        // Dedup por UUID del CFDI — el SAT garantiza unicidad global.
        var uuidNormalizado = command.UuidCfdi.Trim().ToUpperInvariant();
        var existe = await _db.NotasCreditoProveedor
            .AsNoTracking()
            .AnyAsync(n => n.UuidCfdi == uuidNormalizado, cancellationToken);
        if (existe)
        {
            throw new BusinessRuleException(
                "NC_DUPLICADA",
                $"Ya existe una NC capturada con UUID '{uuidNormalizado}'.");
        }

        // Resolución de la factura origen por UUID del CFDI relacionado.
        // El UUID de relación apunta al CFDI de la factura origen → buscamos
        // en facturas_proveedor por UuidCfdi.
        var uuidRelacionNormalizado = command.UuidRelacionCfdi.Trim().ToUpperInvariant();
        var facturaOrigen = await _db.FacturasProveedor
            .AsNoTracking()
            .Where(f => f.UuidCfdi == uuidRelacionNormalizado && f.ProveedorId == command.ProveedorId)
            .Select(f => new { f.Id })
            .FirstOrDefaultAsync(cancellationToken);

        var ahora = _clock.UtcNow;
        var nc = global::Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor.Capturar(
            empresaId: empresaId,
            cfdiRecibidoId: command.CfdiRecibidoId,
            uuidCfdi: command.UuidCfdi,
            proveedorId: command.ProveedorId,
            folioProveedor: command.FolioProveedor,
            serieProveedor: command.SerieProveedor,
            fechaCfdi: command.FechaCfdi,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            subtotal: command.Subtotal,
            impuestosTrasladados: command.ImpuestosTrasladados,
            retenciones: command.Retenciones,
            total: command.Total,
            tipo: command.Tipo,
            tipoRelacionCfdi: command.TipoRelacionCfdi,
            uuidRelacionCfdi: command.UuidRelacionCfdi,
            facturaOrigenId: command.TipoRelacionCfdi == TipoRelacionCfdi.AmortizacionAnticipo ? null : facturaOrigen?.Id,
            capturadoPor: _currentUser.UserId,
            ahora: ahora);

        _db.NotasCreditoProveedor.Add(nc);

        await _mediator.Publish(new NotaCreditoProveedorRegistradaDomainEvent(
            EmpresaId: empresaId,
            NotaCreditoId: nc.Id,
            ProveedorId: nc.ProveedorId,
            FacturaOrigenId: nc.FacturaOrigenId,
            TipoRelacionCfdi: (int)nc.TipoRelacionCfdi,
            Total: nc.Total,
            OcurridoEn: ahora,
            // G1.6: la NC no tiene desglose de retenciones ni sucursal en el agregado → null.
            Uuid: nc.UuidCfdi,
            Subtotal: nc.Subtotal,
            Iva: nc.ImpuestosTrasladados,
            RetencionesTotal: nc.Retenciones,
            Moneda: nc.Moneda,
            TipoCambio: nc.TipoCambio), cancellationToken);

        await new NotaCargo.FormalizacionNotaCargoService(_db, _mediator).IntentarAsync(nc, ahora, cancellationToken);
        if (nc.TipoRelacionCfdi == TipoRelacionCfdi.AmortizacionAnticipo)
        {
            var anticipo = await _db.AnticiposProveedor.FirstOrDefaultAsync(a => a.UuidCfdi == uuidRelacionNormalizado &&
                a.ProveedorId == nc.ProveedorId && a.Moneda == nc.Moneda, cancellationToken);
            if (anticipo is not null) nc.VincularAnticipoOrigen(anticipo.Id, ahora);
        }
        if (command.CfdiRecibidoId is Guid documentoId)
        {
            var documento = await _db.CfdisRecibidos.FirstOrDefaultAsync(c => c.Id == documentoId, cancellationToken)
                ?? throw new EntityNotFoundException("CFDI_NO_ENCONTRADO", "No se encontró el CFDI de la NC.");
            if (documento.UuidCfdi.Valor != nc.UuidCfdi || documento.Tipo != Domain.Cfdi.TipoCfdi.Egreso || documento.Moneda != nc.Moneda || documento.Total != nc.Total)
                throw new BusinessRuleException("NC_CFDI_NO_COINCIDE", "El CFDI ligado debe coincidir con el UUID, moneda e importe de la NC y ser de tipo Egreso.");
            documento.MarcarConvertidoEnPasivo(nc.Id);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new CapturarNotaCreditoResponse(nc.Id, nc.Estado, nc.FacturaOrigenId, nc.Version);
    }
}
