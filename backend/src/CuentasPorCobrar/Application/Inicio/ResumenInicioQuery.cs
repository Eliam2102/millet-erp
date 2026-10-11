using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Reportes.AntiguedadSaldos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.CuentasPorCobrar.Application.Inicio;

public sealed record ResumenInicioQuery(string Pieza, Guid? SucursalId = null)
    : IRequest<ResumenInicioResponse>, IDocumentoScopedQuery, ISucursalSeleccionadaQuery
{
    public PeriodoInicio Periodo { get; init; } = PeriodoInicio.Mes;
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public string TipoDocumento => "factura_cartera";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}
public sealed class ResumenInicioHandler(CuentasPorCobrarDbContext db, IMediator mediator, IClock clock)
    : IRequestHandler<ResumenInicioQuery, ResumenInicioResponse>
{
    public async Task<ResumenInicioResponse> Handle(ResumenInicioQuery request, CancellationToken cancellationToken)
    {
        if (request.Pieza == "vencido")
        {
            // Reutiliza los buckets, abonos y notas de crédito del reporte de antigüedad.
            var reporte = await mediator.Send(new AntiguedadSaldosQuery(FechaCorte: FechasInicio.Hoy(clock), Moneda: "MXN") { SucursalId = request.SucursalId }, cancellationToken);
            var clientes = reporte.Filas.Count(f => (decimal?)f["vencido"] > 0);
            var monto = (decimal?)reporte.Totales?["vencido"] ?? 0;
            return new(monto, clientes, $"{clientes} clientes con saldo vencido · solo MXN", clientes);
        }
        var facturas = db.FacturasCartera.AsNoTracking()
            .Where(f => request.DocumentosPermitidos == null || (request.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(f.Id));
        var hoy = FechasInicio.Hoy(clock);
        var desde = FechasInicio.Desde(request.Periodo, hoy);
        // PostgreSQL (timestamptz) solo acepta parámetros en UTC.
        var desdeUtc = new DateTimeOffset(desde.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-6)).ToUniversalTime();
        var hastaUtc = new DateTimeOffset(hoy.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-6)).ToUniversalTime();
        facturas = facturas.Where(f => f.CreatedAt >= desdeUtc &&
            f.CreatedAt < hastaUtc);
        var abiertas = await facturas.CountAsync(f => f.Estado == EstadoFacturaCartera.Abierta || f.Estado == EstadoFacturaCartera.Parcial, cancellationToken);
        var cobradas = await facturas.CountAsync(f => f.Estado == EstadoFacturaCartera.Pagada, cancellationToken);
        return new(abiertas, abiertas, $"Cartera registrada del {desde:dd/MM} al {hoy:dd/MM} · tus sucursales",
            Etapas: [new("Por cobrar", abiertas), new("Cobrados", cobradas)]);
    }
}

public sealed class ResumenInicioValidator : FluentValidation.AbstractValidator<ResumenInicioQuery>
{
    public ResumenInicioValidator()
    {
        RuleFor(request => request.Periodo).IsInEnum();
        RuleFor(request => request.SucursalId).NotEqual(Guid.Empty).When(request => request.SucursalId.HasValue);
        RuleFor(request => request.Pieza).Must(p => new[] { "vencido", "circuito" }.Contains(p));
    }
}
