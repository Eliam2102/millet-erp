using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;

/// <summary>Adaptador de <see cref="ICfdiRecibidoPublicReadPort"/> sobre el buzón de CxP (filtro de empresa ADR-0011).</summary>
public sealed class CfdiRecibidoPublicReadAdapter(CuentasPorPagarDbContext db) : ICfdiRecibidoPublicReadPort
{
    public async Task<IReadOnlyList<CfdiRecibidoPublico>> ListarIngresosPorProcesarAsync(
        DateTimeOffset desde, DateTimeOffset hasta, string? rfcEmisor, int limite, CancellationToken ct)
    {
        var q = db.CfdisRecibidos.AsNoTracking()
            .Where(c => c.Tipo == TipoCfdi.Ingreso && c.Estado == EstadoCfdiRecibido.PorProcesar
                && c.FechaCfdi >= desde && c.FechaCfdi < hasta);
        if (!string.IsNullOrWhiteSpace(rfcEmisor))
        {
            var rfc = RfcMexicano.Parse(rfcEmisor);
            q = q.Where(c => c.RfcEmisor == rfc);
        }
        var filas = await q.OrderByDescending(c => c.FechaCfdi).Take(Math.Clamp(limite, 1, 200)).ToListAsync(ct);
        return filas.Select(Mapear).ToList();
    }

    public async Task<CfdiRecibidoPublico?> ObtenerAsync(Guid id, CancellationToken ct) =>
        await db.CfdisRecibidos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) is { } c ? Mapear(c) : null;

    private static CfdiRecibidoPublico Mapear(CfdiRecibido c) => new(
        c.Id, c.UuidCfdi.Valor, c.RfcEmisor.Valor, c.Serie, c.Folio, c.FechaCfdi, c.Subtotal, c.ImpuestosTrasladados,
        c.Total, c.Moneda, c.Tipo.ToString(), c.Estado.ToString());
}
