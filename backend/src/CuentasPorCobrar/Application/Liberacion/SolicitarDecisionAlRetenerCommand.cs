using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Application.Liberacion;

/// <summary>
/// PT5.2: Producto Terminado retuvo un pedido (<c>producto_terminado.pedido.estado-cambiado.v1</c> con <c>Retenido</c>) y pide la decisión
/// por serie/crédito. Se decide una sola vez por pedido, como usuario de sistema y sin override (S12); las autorizaciones
/// manuales siguen por su endpoint. Devuelve true si emitió la decisión.
/// </summary>
public sealed record SolicitarDecisionAlRetenerCommand(Guid EmpresaId, string PedidoRef, Guid ClienteId, string Moneda, decimal MontoPedido) : IRequest<bool>;

public sealed class SolicitarDecisionAlRetenerHandler(CuentasPorCobrarDbContext db, IIntegrationEventPublisher eventos, IClock clock)
    : IRequestHandler<SolicitarDecisionAlRetenerCommand, bool>
{
    /// <summary>Quien firma las decisiones automáticas (DecididoPor no admite vacío).</summary>
    public static readonly Guid UsuarioSistemaId = Guid.Parse("00000000-0000-0000-0000-00000000c0c0");

    public async Task<bool> Handle(SolicitarDecisionAlRetenerCommand c, CancellationToken cancellationToken)
    {
        // Sin cliente o sin monto no hay cascada posible: se ignora en vez de reintentar para siempre.
        if (c.ClienteId == Guid.Empty || c.MontoPedido <= 0 || c.Moneda?.Length != 3) return false;
        var pedidoRef = c.PedidoRef.Trim();
        if (await db.DecisionesLiberacion.AnyAsync(d => d.EmpresaId == c.EmpresaId && d.PedidoRef == pedidoRef, cancellationToken)) return false;

        var sistema = new ContextoSistema(c.EmpresaId);
        await new DecidirLiberacionHandler(db, eventos, sistema, sistema, clock)
            .Handle(new DecidirLiberacionCommand(pedidoRef, c.ClienteId, c.Moneda, c.MontoPedido, null), cancellationToken);
        return true;
    }

    private sealed class ContextoSistema(Guid empresaId) : ICurrentEmpresaContext, ICurrentUserContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
        public Guid? UserId => UsuarioSistemaId;
        public string? UserName => "sistema";
    }
}
