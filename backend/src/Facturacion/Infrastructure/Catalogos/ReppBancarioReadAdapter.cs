using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Application.EventListeners;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Infrastructure.Catalogos;

public sealed class ReppBancarioReadAdapter(CompartidoDbContext db, IOptions<ReppAutomaticoOptions> options)
    : IReppBancarioReadPort
{
    public Task<decimal?> TipoCambioAsync(string moneda, DateOnly fecha, CancellationToken cancellationToken) =>
        (from tc in db.TiposCambio.AsNoTracking()
         join m in db.Monedas.AsNoTracking() on tc.MonedaId equals m.Id
         where m.Codigo == moneda && tc.Fecha == fecha
         select (decimal?)tc.ValorEnMxn).SingleOrDefaultAsync(cancellationToken);

    public async Task<Guid> SucursalEmisoraAsync(CancellationToken cancellationToken)
    {
        var sucursales = db.Sucursales.AsNoTracking().Where(s => s.Estatus == EstatusCatalogo.Activo);
        var clave = options.Value.SucursalClave?.Trim();
        if (!string.IsNullOrEmpty(clave)) sucursales = sucursales.Where(s => s.Clave == clave);
        var ids = await sucursales.Select(s => s.Id).Take(2).ToListAsync(cancellationToken);
        if (ids.Count != 1)
            throw new BusinessRuleException("REPP_SUCURSAL_FALTANTE",
                "Configura una sucursal emisora activa en Facturacion:ReppAutomatico:SucursalClave.");
        return ids[0];
    }
}
