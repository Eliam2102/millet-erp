using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Adjuntos;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>Implementa <see cref="IExpedienteProveedorReadPort"/> con el mismo cálculo del endpoint de expediente.</summary>
public sealed class ExpedienteProveedorReadAdapter : IExpedienteProveedorReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly IClock _clock;

    public ExpedienteProveedorReadAdapter(CompartidoDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ExpedienteProveedorResumen?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken)
    {
        var tipoPersona = await _db.Proveedores.AsNoTracking()
            .Where(p => p.Id == proveedorId)
            .Select(p => (TipoPersonaProveedor?)p.TipoPersona)
            .FirstOrDefaultAsync(cancellationToken);
        if (tipoPersona is null) return null;

        var e = await ExpedienteAdjuntos.CalcularAsync(
            _db, ProveedorAdjuntoPropietario.Tipo, proveedorId,
            esPersonaMoral: tipoPersona == TipoPersonaProveedor.Moral,
            AdjuntoSoporte.Hoy(_clock), cancellationToken);
        return new ExpedienteProveedorResumen(proveedorId, e.Completo, e.Faltantes, e.Vencidos, e.PorVencer);
    }
}
