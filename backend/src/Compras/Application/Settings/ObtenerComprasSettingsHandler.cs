using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Settings;

/// <summary>
/// Handler de <see cref="ObtenerComprasSettingsQuery"/>. Lee la fila de
/// <c>compras.settings</c> filtrando por la empresa del JWT (el query
/// filter global de <c>ComprasDbContext</c> ya aplica). Si no existe,
/// retorna la versión default sin persistirla.
/// </summary>
public sealed class ObtenerComprasSettingsHandler
    : IRequestHandler<ObtenerComprasSettingsQuery, ComprasSettingsResponse>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public ObtenerComprasSettingsHandler(
        ComprasDbContext db,
        ICurrentEmpresaContext currentEmpresa)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
    }

    public async Task<ComprasSettingsResponse> Handle(
        ObtenerComprasSettingsQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var row = await _db.ComprasSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.EmpresaId == empresaId, cancellationToken);

        if (row is null)
        {
            return new ComprasSettingsResponse(
                EmpresaId: empresaId,
                AutoGenerarOcAlAutorizar: false);
        }

        return new ComprasSettingsResponse(
            EmpresaId: row.EmpresaId,
            AutoGenerarOcAlAutorizar: row.AutoGenerarOcAlAutorizar);
    }
}
