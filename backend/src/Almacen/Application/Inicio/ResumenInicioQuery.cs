using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.Almacen.Application.Inicio;

public sealed record ResumenInicioQuery(string Pieza, Guid? SucursalId = null)
    : IRequest<ResumenInicioResponse>, IDocumentoScopedQuery, ISucursalSeleccionadaQuery
{
    public string PermisoTodasSucursales => "almacen.documentos.leer-todas-sucursales";
    public string TipoDocumento => Pieza == "conteos" ? "conteo" : "salida_almacen";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}
public sealed class ResumenInicioHandler(AlmacenDbContext db, IComprasRecepcionesReadPort compras, IClock clock)
    : IRequestHandler<ResumenInicioQuery, ResumenInicioResponse>
{
    public async Task<ResumenInicioResponse> Handle(ResumenInicioQuery request, CancellationToken cancellationToken)
    {
        if (request.Pieza == "recepciones")
        {
            var resumen = await compras.ResumirRecepcionesAsync(request.SucursalesPermitidas, cancellationToken);
            return new(resumen.Total, resumen.Total, $"{resumen.Total} órdenes autorizadas por recibir · {resumen.Parciales} parciales");
        }
        if (request.Pieza == "conteos")
        {
            var conteos = db.Conteos.AsNoTracking()
                .Where(c => request.DocumentosPermitidos == null || (request.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(c.Id))
                .Where(c => c.Estado != Millet.Almacen.Domain.Conteos.EstadoConteo.Aplicado && c.Estado != Millet.Almacen.Domain.Conteos.EstadoConteo.Rechazado);
            var abiertos = await conteos.CountAsync(cancellationToken);
            return new(abiertos, abiertos, abiertos == 0 ? "Sin conteos abiertos en tus sucursales" : $"{abiertos} conteos abiertos en tus sucursales");
        }
        var vales = db.Movimientos.AsNoTracking().Where(m => m.PendienteRegularizacion)
            .Where(m => request.DocumentosPermitidos == null || (request.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(m.Id));
        var total = await vales.CountAsync(cancellationToken);
        var hoy = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.UtcNow, "America/Merida");
        var inicio = new DateTimeOffset(hoy.Date, hoy.Offset);
        var fin = inicio.AddDays(1);
        var vencenHoy = await vales.CountAsync(m => m.FechaLimiteRegularizacion >= inicio && m.FechaLimiteRegularizacion < fin, cancellationToken);
        var vencidos = await vales.CountAsync(m => m.FechaLimiteRegularizacion < clock.UtcNow, cancellationToken);
        var urgentes = await vales.CountAsync(m => m.FechaLimiteRegularizacion < fin, cancellationToken);
        return new(total, total, $"{total} vales abiertos · {vencenHoy} vencen hoy · {vencidos} vencidos", urgentes);
    }
}

public sealed class ResumenInicioValidator : FluentValidation.AbstractValidator<ResumenInicioQuery>
{
    public ResumenInicioValidator()
    {
        RuleFor(request => request.SucursalId).NotEqual(Guid.Empty).When(request => request.SucursalId.HasValue);
        RuleFor(request => request.Pieza).Must(p => new[] { "recepciones", "vales", "conteos" }.Contains(p));
    }
}
