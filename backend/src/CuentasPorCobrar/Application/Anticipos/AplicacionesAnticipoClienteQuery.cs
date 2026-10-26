using FluentValidation;
using MediatR;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;

namespace Millet.CuentasPorCobrar.Application.Anticipos;

/// <summary>
/// F1-CXC-05: saldo de anticipos por cliente y su aplicación a la factura final,
/// vía <see cref="IFacturacionAplicacionesAnticipoReadPort"/>. Solo consulta
/// (R11): la emisión, vinculación y amortización viven en Facturación.
/// <c>ClienteId</c> <c>null</c> = todos los clientes con anticipos. Alcance por
/// sucursal del CFDI de anticipo (ADR-0051), mismo bypass que
/// <see cref="AnticiposClienteQuery"/>.
/// </summary>
public sealed record AplicacionesAnticipoClienteQuery(Guid? ClienteId, string? Estado = null)
    : IRequest<AplicacionesAnticipoResponse>, Millet.SharedKernel.Application.ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

/// <summary>Totales de una moneda (nunca se suman divisas distintas). Excluye anticipos cancelados.</summary>
public sealed record TotalesAnticiposMonedaDto(
    string Moneda, decimal Cobrado, decimal Aplicado, decimal Comprometido, decimal Saldo);

public sealed record AplicacionesAnticipoResponse(
    IReadOnlyList<AnticipoConAplicacionesDto> Anticipos,
    IReadOnlyList<TotalesAnticiposMonedaDto> TotalesPorMoneda);

public sealed class AplicacionesAnticipoClienteHandler
    : IRequestHandler<AplicacionesAnticipoClienteQuery, AplicacionesAnticipoResponse>
{
    private readonly IFacturacionAplicacionesAnticipoReadPort _anticipos;

    public AplicacionesAnticipoClienteHandler(IFacturacionAplicacionesAnticipoReadPort anticipos)
    {
        _anticipos = anticipos;
    }

    public async Task<AplicacionesAnticipoResponse> Handle(
        AplicacionesAnticipoClienteQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<AnticipoConAplicacionesDto> anticipos =
            await _anticipos.ListarAsync(query.ClienteId, cancellationToken);

        if (query.SucursalesPermitidas is { } permitidas)
            anticipos = anticipos.Where(a => a.SucursalId is Guid id && permitidas.Contains(id));
        if (!string.IsNullOrWhiteSpace(query.Estado))
            anticipos = anticipos.Where(a => string.Equals(a.Estado, query.Estado, StringComparison.OrdinalIgnoreCase));

        var lista = anticipos.ToList();
        var totales = lista
            .Where(a => a.Estado != EstadoAnticipoAplicaciones.Cancelado)
            .GroupBy(a => a.Moneda)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new TotalesAnticiposMonedaDto(
                g.Key,
                g.Sum(a => a.Cobrado),
                g.Sum(a => a.Aplicado),
                g.Sum(a => a.Comprometido),
                g.Sum(a => a.Saldo)))
            .ToList();

        return new AplicacionesAnticipoResponse(lista, totales);
    }
}

public sealed class AplicacionesAnticipoClienteValidator : AbstractValidator<AplicacionesAnticipoClienteQuery>
{
    public AplicacionesAnticipoClienteValidator()
    {
        RuleFor(q => q.ClienteId).NotEqual(Guid.Empty).When(q => q.ClienteId.HasValue)
            .WithMessage("El cliente no es válido.");
        RuleFor(q => q.Estado)
            .Must(e => EstadoAnticipoAplicaciones.Todos.Contains(e!, StringComparer.OrdinalIgnoreCase))
            .When(q => !string.IsNullOrWhiteSpace(q.Estado))
            .WithMessage("El estado no es válido.");
    }
}
