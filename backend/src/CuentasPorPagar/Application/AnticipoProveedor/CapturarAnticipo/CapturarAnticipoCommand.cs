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
        RuleFor(c => c.UuidCfdi).NotEmpty().Length(36);
        RuleFor(c => c.ProveedorId).NotEmpty();
        RuleFor(c => c.Serie).NotEmpty();
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

    public CapturarAnticipoHandler(
        CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser, IMediator mediator, IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _mediator = mediator; _clock = clock;
    }

    public async Task<CapturarAnticipoResponse> Handle(CapturarAnticipoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var uuidNormalizado = command.UuidCfdi.Trim().ToUpperInvariant();
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
            uuidCfdi: command.UuidCfdi,
            proveedorId: command.ProveedorId,
            serie: command.Serie,
            folioProveedor: command.FolioProveedor,
            fechaCfdi: command.FechaCfdi,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            montoEntregado: command.MontoEntregado,
            ordenCompraId: command.OrdenCompraId,
            capturadoPor: _currentUser.UserId,
            ahora: ahora);

        _db.AnticiposProveedor.Add(anticipo);

        await _mediator.Publish(new AnticipoProveedorCapturadoDomainEvent(
            EmpresaId: empresaId,
            AnticipoId: anticipo.Id,
            ProveedorId: anticipo.ProveedorId,
            MontoEntregado: anticipo.MontoEntregado,
            OrdenCompraId: anticipo.OrdenCompraId,
            OcurridoEn: ahora), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new CapturarAnticipoResponse(anticipo.Id, anticipo.Estado, anticipo.SaldoAmortizable, anticipo.Version);
    }
}
