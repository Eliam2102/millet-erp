using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.AnticipoProveedor.CapturarAnticipo;

public sealed record CapturarAnticipoCommand(
    Guid? CfdiRecibidoId,
    string UuidCfdi,
    Guid ProveedorId,
    string Serie,
    string? FolioProveedor,
    DateTimeOffset FechaCfdi,
    string Moneda,
    decimal? TipoCambio,
    decimal MontoEntregado,
    Guid? OrdenCompraId) : IRequest<CapturarAnticipoResponse>;

public sealed record CapturarAnticipoResponse(
    Guid Id,
    EstadoAnticipo Estado,
    decimal SaldoAmortizable,
    int Version);

public sealed class CapturarAnticipoValidator : AbstractValidator<CapturarAnticipoCommand>
{
    public CapturarAnticipoValidator()
    {
        RuleFor(c => c.UuidCfdi).NotEmpty().Length(36).When(c => c.CfdiRecibidoId == null);
        RuleFor(c => c.ProveedorId).NotEmpty();
        RuleFor(c => c.Serie).MaximumLength(25);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.MontoEntregado).GreaterThan(0);
    }
}

public sealed class CapturarAnticipoHandler : IRequestHandler<CapturarAnticipoCommand, CapturarAnticipoResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IMediator _mediator;
    private readonly IClock _clock;
    private readonly Domain.Ports.DatosMaestros.IProveedorReadPort _proveedores;

    public CapturarAnticipoHandler(
        CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser, IMediator mediator, IClock clock, Domain.Ports.DatosMaestros.IProveedorReadPort proveedores)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _mediator = mediator; _clock = clock; _proveedores = proveedores;
    }

    public async Task<CapturarAnticipoResponse> Handle(CapturarAnticipoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var documento = command.CfdiRecibidoId is Guid documentoId
            ? await _db.CfdisRecibidos.FirstOrDefaultAsync(c => c.Id == documentoId, cancellationToken)
                ?? throw new EntityNotFoundException("CFDI_NO_ENCONTRADO", "No se encontró el CFDI del anticipo.") : null;
        var proveedor = await _proveedores.ObtenerAsync(command.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO", "No se encontró el proveedor del anticipo.");
        if (documento is not null && documento.RfcEmisor.Valor != proveedor.Rfc)
            throw new BusinessRuleException("ANTICIPO_CFDI_PROVEEDOR_DISTINTO", "El RFC emisor del CFDI no coincide con el proveedor del anticipo.");
        var serieConfigurada = await _db.ConfiguracionesAnticipoProveedor.Where(c => c.ProveedorId == command.ProveedorId)
            .Select(c => c.Serie).FirstOrDefaultAsync(cancellationToken) ?? "FANT";
        var serie = documento?.Serie ?? (string.IsNullOrWhiteSpace(command.Serie) ? serieConfigurada : command.Serie.Trim().ToUpperInvariant());
        if (!string.Equals(serie, serieConfigurada, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("ANTICIPO_SERIE_INVALIDA", $"La serie de anticipos configurada para este proveedor es {serieConfigurada}. Configúrala antes de capturar otra serie.");
        if (documento is not null && (documento.Tipo != Domain.Cfdi.TipoCfdi.Ingreso || !string.Equals(documento.Moneda, command.Moneda, StringComparison.OrdinalIgnoreCase) || documento.Total != command.MontoEntregado))
            throw new BusinessRuleException("ANTICIPO_CFDI_NO_COINCIDE", "El CFDI del anticipo debe ser Ingreso y coincidir en moneda e importe.");
        var uuidNormalizado = (documento?.UuidCfdi.Valor ?? command.UuidCfdi).Trim().ToUpperInvariant();
        var existe = await _db.AnticiposProveedor.AnyAsync(a => a.UuidCfdi == uuidNormalizado, cancellationToken);
        if (existe)
        {
            throw new BusinessRuleException(
                "ANTICIPO_DUPLICADO",
                $"Ya existe un anticipo con UUID '{uuidNormalizado}'.");
        }

        var ahora = _clock.UtcNow;
        var anticipo = global::Millet.CuentasPorPagar.Domain.AnticipoProveedor.AnticipoProveedor.Capturar(
            empresaId: empresaId,
            cfdiRecibidoId: command.CfdiRecibidoId,
            uuidCfdi: uuidNormalizado,
            proveedorId: command.ProveedorId,
            serie: serie,
            folioProveedor: documento?.Folio ?? command.FolioProveedor,
            fechaCfdi: documento?.FechaCfdi ?? command.FechaCfdi,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            montoEntregado: command.MontoEntregado,
            ordenCompraId: command.OrdenCompraId,
            capturadoPor: _currentUser.UserId,
            ahora: ahora);

        _db.AnticiposProveedor.Add(anticipo);
        documento?.MarcarConvertidoEnPasivo(anticipo.Id);

        await _mediator.Publish(new AnticipoProveedorCapturadoDomainEvent(
            EmpresaId: empresaId,
            AnticipoId: anticipo.Id,
            ProveedorId: anticipo.ProveedorId,
            MontoEntregado: anticipo.MontoEntregado,
            OrdenCompraId: anticipo.OrdenCompraId,
            OcurridoEn: ahora,
            // G1.6: el anticipo no tiene cuenta bancaria en el agregado → null.
            Moneda: anticipo.Moneda,
            TipoCambio: anticipo.TipoCambio), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new CapturarAnticipoResponse(anticipo.Id, anticipo.Estado, anticipo.SaldoAmortizable, anticipo.Version);
    }
}
