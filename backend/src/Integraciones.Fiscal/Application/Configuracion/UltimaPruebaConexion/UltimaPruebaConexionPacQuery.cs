using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;

namespace Millet.Integraciones.Fiscal.Application.Configuracion.UltimaPruebaConexion;

/// <summary>M-232: última prueba de conexión registrada de la configuración del PAC (null si no hay).</summary>
public sealed record UltimaPruebaConexionPacQuery(Guid EmpresaId, ProveedorPac Proveedor) : IRequest<UltimaPruebaConexionPacResponse?>;

public sealed record UltimaPruebaConexionPacResponse(
    DateTimeOffset RealizadaEn,
    bool Exitosa,
    int StatusCode,
    string Mensaje,
    string Origen,
    string? RealizadaPor);

public sealed class UltimaPruebaConexionPacHandler(IntegracionesFiscalDbContext db)
    : IRequestHandler<UltimaPruebaConexionPacQuery, UltimaPruebaConexionPacResponse?>
{
    public async Task<UltimaPruebaConexionPacResponse?> Handle(UltimaPruebaConexionPacQuery query, CancellationToken cancellationToken) =>
        await db.PruebasConexionPac.AsNoTracking()
            .Where(p => p.EmpresaId == query.EmpresaId && p.Proveedor == query.Proveedor)
            .OrderByDescending(p => p.RealizadaEn)
            .Select(p => new UltimaPruebaConexionPacResponse(p.RealizadaEn, p.Exitosa, p.StatusCode, p.Mensaje, p.Origen.ToString(), p.RealizadaPor))
            .FirstOrDefaultAsync(cancellationToken);
}
