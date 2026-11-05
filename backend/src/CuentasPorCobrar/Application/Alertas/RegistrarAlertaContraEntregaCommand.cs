using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Alertas;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Application.Alertas;

/// <summary>
/// PT5.7: Producto Terminado avisa que un pedido a contra entrega no se liquidó en caja el día de la salida
/// (<c>producto_terminado.contra-entrega.no-liquidada.v1</c>). Levanta la alerta de cartera; un aviso repetido del mismo pedido no duplica la alerta.
/// Devuelve true si creó una alerta nueva. La notificación push/correo sigue en PLATFORM-TODO(&lt;Notificaciones&gt;), como las demás alertas de cartera.
/// </summary>
public sealed record RegistrarAlertaContraEntregaCommand(Guid EmpresaId, Guid ClienteId, string Moneda, decimal Total, string NumeroPedido, string HojaSalidaFolio, DateTimeOffset SalidaAt) : IRequest<bool>;

public sealed class RegistrarAlertaContraEntregaHandler(CuentasPorCobrarDbContext db, IClock clock) : IRequestHandler<RegistrarAlertaContraEntregaCommand, bool>
{
    public async Task<bool> Handle(RegistrarAlertaContraEntregaCommand c, CancellationToken cancellationToken)
    {
        // Sin cliente no hay a quién cobrar: se ignora en vez de reintentar para siempre.
        if (c.ClienteId == Guid.Empty) return false;
        var detalle = $"Contra entrega sin liquidar: pedido {c.NumeroPedido}, hoja {c.HojaSalidaFolio}, {c.Total.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))} {c.Moneda}. El chofer no liquidó en caja el día de la salida.";
        if (await db.AlertasCartera.AnyAsync(a => a.EmpresaId == c.EmpresaId && a.ClienteId == c.ClienteId && a.Tipo == TipoAlertaCartera.ContraEntregaNoLiquidada && a.Detalle == detalle, cancellationToken))
            return false;
        db.AlertasCartera.Add(new AlertaCartera(c.EmpresaId, c.ClienteId, TipoAlertaCartera.ContraEntregaNoLiquidada, c.Moneda, detalle, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
