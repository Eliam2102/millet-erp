using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Settings;

/// <summary>
/// Handler de <see cref="ActualizarComprasSettingsCommand"/>. Upsert por
/// (empresa_id): si no existe la fila se crea con
/// <see cref="ComprasSettings.CrearDefault"/> y luego se aplica el patch;
/// si existe se actualiza solo los campos no-null.
/// </summary>
public sealed class ActualizarComprasSettingsHandler
    : IRequestHandler<ActualizarComprasSettingsCommand, ComprasSettingsResponse>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public ActualizarComprasSettingsHandler(
        ComprasDbContext db,
        ICurrentEmpresaContext currentEmpresa)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
    }

    public async Task<ComprasSettingsResponse> Handle(
        ActualizarComprasSettingsCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var row = await _db.ComprasSettings
            .FirstOrDefaultAsync(s => s.EmpresaId == empresaId, cancellationToken);

        if (row is null)
        {
            row = ComprasSettings.CrearDefault(empresaId);
            _db.ComprasSettings.Add(row);
        }

        if (command.AutoGenerarOcAlAutorizar is bool flag)
        {
            row.EstablecerAutoGenerarOcAlAutorizar(flag);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new ComprasSettingsResponse(
            EmpresaId: row.EmpresaId,
            AutoGenerarOcAlAutorizar: row.AutoGenerarOcAlAutorizar);
    }
}
