using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;
using Millet.Tesoreria.Application.Cuentas;
using Millet.Tesoreria.Application.Pasivos;
using Millet.Tesoreria.Application.Reportes;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Inicio;

public sealed record ResumenInicioQuery(string Pieza, Guid? SucursalId = null)
    : IRequest<ResumenInicioResponse>, IDocumentoScopedQuery, ISucursalSeleccionadaQuery
{
    public string PermisoTodasSucursales => "tesoreria.documentos.leer-todas-sucursales";
    public string TipoDocumento => Pieza switch { "pagos" => "pasivo", "depositos" => "deposito", _ => "cuenta_bancaria" };
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class ResumenInicioHandler(TesoreriaDbContext db, IMediator mediator, IClock clock)
    : IRequestHandler<ResumenInicioQuery, ResumenInicioResponse>
{
    public async Task<ResumenInicioResponse> Handle(ResumenInicioQuery request, CancellationToken cancellationToken)
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.UtcNow, "America/Merida").DateTime);
        if (request.Pieza == "bancos")
        {
            var cuentas = await mediator.Send(new SaldosPorCuentaQuery { SucursalId = request.SucursalId }, cancellationToken);
            var mxn = cuentas.Where(c => c.Moneda == "MXN").ToArray();
            return new(mxn.Sum(c => c.Saldo), mxn.Length,
                $"{mxn.Length} cuentas · solo MXN · corte {TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.UtcNow, "America/Merida"):HH:mm}");
        }
        if (request.Pieza == "pagos")
        {
            var pasivos = PasivosPendientesLectura.Consultar(db, request.DocumentosPermitidos)
                .Where(p => p.Moneda == "MXN" && p.FechaVencimiento <= hoy.AddDays(7));
            var total = await pasivos.CountAsync(cancellationToken);
            var vencidos = await pasivos.CountAsync(p => p.FechaVencimiento < hoy, cancellationToken);
            return new(await pasivos.SumAsync(p => p.SaldoPendiente, cancellationToken), total,
                $"{total} pasivos autorizados · {vencidos} vencidos · solo MXN", vencidos);
        }
        if (request.Pieza == "depositos")
        {
            var total = await db.DepositosConfirmacion.AsNoTracking()
                .Where(d => request.DocumentosPermitidos == null || (request.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(d.Id))
                .CountAsync(d => d.Estado == EstadoDepositoConfirmacion.Pendiente && d.PropuestaCxcId != null, cancellationToken);
            return new(total, total, total == 0 ? "Sin propuestas de Cobranza por confirmar" : $"{total} propuestas de Cobranza esperan confirmación");
        }
        // Mismos movimientos del reporte de flujo: importes por cuenta en MXN, sin mezclar monedas.
        var lunes = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
        var cuentasIds = await FlujoLectura.Cuentas(db, request.DocumentosPermitidos, "MXN")
            .Select(c => c.Id).ToListAsync(cancellationToken);
        var movimientos = await FlujoLectura.Movimientos(db, cuentasIds)
            .Where(m => m.FechaValor >= lunes && m.FechaValor <= lunes.AddDays(6))
            .GroupBy(m => new { m.FechaValor, m.Sentido })
            .Select(g => new { g.Key.FechaValor, g.Key.Sentido, Total = g.Sum(m => m.Monto) }).ToListAsync(cancellationToken);
        string[] dias = ["Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom"];
        var serie = Enumerable.Range(0, 7).Select(i => new DiaFlujoInicio(dias[i],
            movimientos.Where(m => m.FechaValor == lunes.AddDays(i) && m.Sentido == SentidoMovimiento.Ingreso).Sum(m => m.Total),
            movimientos.Where(m => m.FechaValor == lunes.AddDays(i) && m.Sentido == SentidoMovimiento.Egreso).Sum(m => m.Total))).ToArray();
        return new(0, movimientos.Count, "Ingresos y egresos registrados · semana actual · solo MXN", Serie: serie);
    }
}

public sealed class ResumenInicioValidator : FluentValidation.AbstractValidator<ResumenInicioQuery>
{
    public ResumenInicioValidator()
    {
        RuleFor(request => request.SucursalId).NotEqual(Guid.Empty).When(request => request.SucursalId.HasValue);
        RuleFor(request => request.Pieza).Must(p => new[] { "pagos", "bancos", "depositos", "flujo" }.Contains(p));
    }
}
