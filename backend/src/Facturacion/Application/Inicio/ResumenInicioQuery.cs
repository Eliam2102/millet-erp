using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Inicio;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Inicio;

public sealed record ResumenInicioQuery(string Pieza, Guid? SucursalId = null)
    : IRequest<ResumenInicioResponse>, ISucursalSeleccionadaQuery
{
    public PeriodoInicio Periodo { get; init; } = PeriodoInicio.Mes;
    public string PermisoTodasSucursales => "facturacion.documentos.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}
public sealed class ResumenInicioHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance, IClock clock)
    : IRequestHandler<ResumenInicioQuery, ResumenInicioResponse>
{
    public async Task<ResumenInicioResponse> Handle(ResumenInicioQuery request, CancellationToken cancellationToken)
    {
        if (request.Pieza == "timbrado")
        {
            var comprobantes = db.Comprobantes.AsNoTracking()
                .Where(c => request.SucursalesPermitidas == null || (request.SucursalesPermitidas ?? Array.Empty<Guid>()).Contains(c.SucursalId));
            (comprobantes, _) = await AlcanceBandejaHelper.AplicarAsync(alcance, comprobantes, false, cancellationToken);
            var total = await comprobantes.CountAsync(c => c.Estado == EstadoTimbrado.TimbradoFallido, cancellationToken);
            return new(total, total, total == 0 ? "Sin comprobantes con timbrado fallido" : $"{total} comprobantes requieren reintento", total);
        }
        var pedidos = db.PedidosFacturables.AsNoTracking()
            .Where(p => request.SucursalesPermitidas == null || (request.SucursalesPermitidas ?? Array.Empty<Guid>()).Contains(p.SucursalId));
        (pedidos, _) = await AlcanceBandejaHelper.AplicarAsync(alcance, pedidos, false, cancellationToken);
        var hoy = FechasInicio.Hoy(clock);
        var desde = FechasInicio.Desde(request.Periodo, hoy);
        // PostgreSQL (timestamptz) solo acepta parámetros en UTC.
        var desdeUtc = new DateTimeOffset(desde.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-6)).ToUniversalTime();
        var hastaUtc = new DateTimeOffset(hoy.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-6)).ToUniversalTime();
        if (request.Pieza == "circuito") pedidos = pedidos.Where(p => p.CreatedAt >= desdeUtc &&
            p.CreatedAt < hastaUtc);
        var abiertos = await pedidos.CountAsync(p => p.Origen == OrigenPedido.Aw && p.Estado == EstadoPedidoFacturable.Importado, cancellationToken);
        var excepciones = await pedidos.CountAsync(p => p.Origen == OrigenPedido.Aw && p.Estado == EstadoPedidoFacturable.Excepcion, cancellationToken);
        if (request.Pieza == "pedidos") return new(abiertos, abiertos, $"{abiertos} pedidos A+W sin facturar · {excepciones} con excepción", excepciones);
        var facturados = await pedidos.CountAsync(p => p.Estado == EstadoPedidoFacturable.Facturado, cancellationToken);
        return new(abiertos, abiertos, $"Pedidos del {desde:dd/MM} al {hoy:dd/MM} · tu caja y sucursal", Etapas: [new("Pedidos", abiertos), new("Facturados", facturados)]);
    }
}

public sealed class ResumenInicioValidator : FluentValidation.AbstractValidator<ResumenInicioQuery>
{
    public ResumenInicioValidator()
    {
        RuleFor(request => request.Periodo).IsInEnum();
        RuleFor(request => request.SucursalId).NotEqual(Guid.Empty).When(request => request.SucursalId.HasValue);
        RuleFor(request => request.Pieza).Must(p => new[] { "pedidos", "timbrado", "circuito" }.Contains(p));
    }
}
