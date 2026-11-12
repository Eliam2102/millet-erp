using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Alertas;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Application.Alertas;

/// <summary>Espejo de <c>tesoreria.cheque.rebotado.v1</c> (M-372).</summary>
public sealed record ChequeRebotadoPayload(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid ChequeId, Guid ClienteId,
    string BancoEmisor, string Numero, decimal Importe, string Moneda, string Motivo, bool Contramovimiento);

/// <summary>
/// M-372: Tesorería avisa que el banco devolvió un cheque del cliente. Levanta la alerta de cartera en <c>/cxc/alertas</c>
/// para que CxC lo cobre por otra vía. Idempotente por evento.
/// </summary>
public sealed record RegistrarAlertaChequeRebotadoCommand(Guid EventId, ChequeRebotadoPayload Cheque) : IRequest;

public sealed class RegistrarAlertaChequeRebotadoHandler(CuentasPorCobrarDbContext db, IClock clock)
    : IRequestHandler<RegistrarAlertaChequeRebotadoCommand>
{
    public const string EventType = "tesoreria.cheque.rebotado.v1";

    public async Task Handle(RegistrarAlertaChequeRebotadoCommand command, CancellationToken cancellationToken)
    {
        if (await db.EventosProcesados.AnyAsync(e => e.EventoId == command.EventId && e.EventoTipo == EventType, cancellationToken)) return;
        var c = command.Cheque;
        var importe = c.Importe.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
        var detalle = $"Cheque {c.Numero} de {c.BancoEmisor} por {importe} {c.Moneda} rebotado: {c.Motivo}." +
            (c.Contramovimiento ? " Tesorería ya revirtió el ingreso." : " No se había acreditado.");
        db.AlertasCartera.Add(new AlertaCartera(c.EmpresaId, c.ClienteId, TipoAlertaCartera.ChequeRebotado, c.Moneda,
            detalle.Length > 500 ? detalle[..500] : detalle, clock.UtcNow));
        db.EventosProcesados.Add(new EventoProcesado(command.EventId, EventType, clock.UtcNow, $"Cheque={c.ChequeId}"));
        await db.SaveChangesAsync(cancellationToken);
    }
}
