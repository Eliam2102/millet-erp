using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Proformas.Queries;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Proformas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Proformas.RegistrarConformidadProforma;

/// <summary>
/// M-234: registra la conformidad del cliente con una versión concreta de la proforma de un pedido
/// (quién, cuándo y medio). <see cref="Fecha"/> vacía = hoy (día fiscal en México). No aprueba otras versiones.
/// </summary>
public sealed record RegistrarConformidadProformaCommand(
    Guid PedidoFacturableId,
    Guid ProformaId,
    string Quien,
    MedioConformidadProforma Medio,
    DateOnly? Fecha = null,
    string? Notas = null) : IRequest<ProformaConformidadResponse>;

public sealed class RegistrarConformidadProformaValidator : AbstractValidator<RegistrarConformidadProformaCommand>
{
    public RegistrarConformidadProformaValidator()
    {
        RuleFor(c => c.PedidoFacturableId).NotEmpty();
        RuleFor(c => c.ProformaId).NotEmpty();
        RuleFor(c => c.Quien).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Medio).IsInEnum();
        RuleFor(c => c.Notas).MaximumLength(500);
    }
}

public sealed class RegistrarConformidadProformaHandler
    : IRequestHandler<RegistrarConformidadProformaCommand, ProformaConformidadResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;

    public RegistrarConformidadProformaHandler(
        FacturacionDbContext db, IAlcanceCajaEvaluator alcance, ICurrentUserContext user, IClock clock)
    {
        _db = db;
        _alcance = alcance;
        _user = user;
        _clock = clock;
    }

    public async Task<ProformaConformidadResponse> Handle(
        RegistrarConformidadProformaCommand command, CancellationToken cancellationToken)
    {
        await ProformaAlcance.AsegurarPedidoVisibleAsync(_db, _alcance, command.PedidoFacturableId, cancellationToken);

        var proforma = await _db.ProformasPedido
            .FirstOrDefaultAsync(p => p.Id == command.ProformaId && p.PedidoFacturableId == command.PedidoFacturableId, cancellationToken)
            ?? throw new EntityNotFoundException("PROFORMA_NO_ENCONTRADA", $"No existe la proforma '{command.ProformaId}' en el pedido.");

        if (await _db.ProformasConformidad.AnyAsync(c => c.ProformaPedidoId == proforma.Id, cancellationToken))
            throw new BusinessRuleException(
                "FAC_PROFORMA_YA_CONFORME", $"La proforma {proforma.Folio} ya tiene conformidad registrada.");

        var ahora = _clock.UtcNow;
        var hoy = CfdiEmisionBuilder.DiaFiscal(ahora);
        var conformidad = ProformaConformidad.Registrar(
            proforma, command.Quien, command.Fecha ?? hoy, command.Medio, command.Notas,
            _user.UserId, _user.UserName, ahora, hoy, CfdiEmisionBuilder.DiaFiscal(proforma.GeneradaEn));

        _db.ProformasConformidad.Add(conformidad);
        await _db.SaveChangesAsync(cancellationToken);
        return ProformaConformidadResponse.Desde(conformidad);
    }
}
