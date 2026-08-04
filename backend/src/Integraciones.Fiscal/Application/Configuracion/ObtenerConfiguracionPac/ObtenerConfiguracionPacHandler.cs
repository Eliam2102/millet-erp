using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Application.Configuracion.GuardarConfiguracionPac;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;

namespace Millet.Integraciones.Fiscal.Application.Configuracion.ObtenerConfiguracionPac;

public sealed class ObtenerConfiguracionPacHandler
    : IRequestHandler<ObtenerConfiguracionPacQuery, ConfiguracionPacResponse?>
{
    private readonly IntegracionesFiscalDbContext _db;

    public ObtenerConfiguracionPacHandler(IntegracionesFiscalDbContext db)
    {
        _db = db;
    }

    public async Task<ConfiguracionPacResponse?> Handle(
        ObtenerConfiguracionPacQuery query, CancellationToken cancellationToken)
    {
        // El query filter del BaseDbContext ya filtra por empresa actual,
        // pero pasamos EmpresaId explícito para claridad y para que el
        // endpoint no sea ambiguo. Si el caller pide otra empresa, el
        // query filter ya retorna 0 filas (vía Bypass se podría leer
        // cualquiera — no aplica aquí).
        var c = await _db.ConfiguracionesPac
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.EmpresaId == query.EmpresaId && x.Proveedor == query.Proveedor,
                cancellationToken);

        return c is null ? null : GuardarConfiguracionPacHandler.MapToResponse(c);
    }
}
