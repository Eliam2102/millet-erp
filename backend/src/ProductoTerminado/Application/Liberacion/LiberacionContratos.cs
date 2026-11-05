using FluentValidation;
using MediatR;

namespace Millet.ProductoTerminado.Application.Liberacion;

/// <summary>
/// Decisión de CxC (<c>decision-liberacion.emitida.v1</c>) para el pedido <c>PedidoRef</c> (número de A+W, S10). Solo la aplican
/// <c>Liberado</c> y <c>LiberadoConOverride</c> a un pedido retenido; repetida o fuera de lugar no cambia nada. Devuelve true si liberó.
/// </summary>
public sealed record AplicarDecisionCxcCommand(Guid EmpresaId, string PedidoRef, string Resultado, Guid? DecididoPor = null) : IRequest<bool>;

/// <summary>Barrido de pedidos retenidos (cada 15 min): reevalúa saldo y REPARTO de los que ya tienen el 100 % de piezas. Devuelve cuántos liberó.</summary>
public sealed record BarrerRetenidosCommand(int Lote = 200) : IRequest<int>;

/// <summary>Solo Demo: fija el saldo de prueba del pedido (simula el cobro de caja) y reevalúa. Devuelve true si el pedido pasó a 70.</summary>
public sealed record PonerSaldoDemoCommand(string NumeroPedido, decimal Saldo) : IRequest<bool>;

public sealed class PonerSaldoDemoValidator : AbstractValidator<PonerSaldoDemoCommand>
{
    public PonerSaldoDemoValidator()
    {
        RuleFor(x => x.NumeroPedido).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Saldo).GreaterThanOrEqualTo(0);
    }
}
