using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Departamentos;
using Millet.Administracion.Application.Sucursales;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Empresas;

/// <summary>
/// Detalle de empresa con sucursales + departamentos del sistema
/// (F-Admin-PR2.3). 404 si no existe.
///
/// <para>
/// MVP single-tenant: <c>Sucursales</c> y <c>Departamentos</c> son TODAS
/// las del sistema (no filtradas por empresa, porque la entity Sucursal /
/// Departamento no tiene <c>EmpresaId</c> todavía — ADR-0011 + decisión
/// MVP). Cuando se levante multi-empresa, se filtrará por la empresa
/// del path.
/// </para>
/// </summary>
public sealed record ObtenerEmpresaQuery(Guid Id) : IRequest<EmpresaDetalleResponse>;

public sealed record EmpresaDetalleResponse(
    EmpresaResponse Empresa,
    IReadOnlyList<SucursalResponse> Sucursales,
    IReadOnlyList<DepartamentoResponse> Departamentos);

public sealed class ObtenerEmpresaHandler
    : IRequestHandler<ObtenerEmpresaQuery, EmpresaDetalleResponse>
{
    private readonly CompartidoDbContext _db;

    public ObtenerEmpresaHandler(CompartidoDbContext db) => _db = db;

    public async Task<EmpresaDetalleResponse> Handle(
        ObtenerEmpresaQuery query, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "EMPRESA_NO_ENCONTRADA",
                $"No existe empresa con id '{query.Id}'.");

        var sucursales = await _db.Sucursales.AsNoTracking()
            .OrderBy(s => s.Clave)
            .Select(s => new SucursalResponse(s.Id, s.Clave, s.Nombre, s.Tipo, s.Estatus, s.Version, s.ClaveAw, s.ZonaHoraria))
            .ToListAsync(cancellationToken);

        var departamentos = await _db.Departamentos.AsNoTracking()
            .OrderBy(d => d.Clave)
            .Select(d => new DepartamentoResponse(d.Id, d.Clave, d.Nombre, d.Estatus, d.Version))
            .ToListAsync(cancellationToken);

        var empresaDto = new EmpresaResponse(
            empresa.Id, empresa.Rfc, empresa.RazonSocial,
            empresa.NombreComercial, empresa.RegimenFiscal,
            empresa.TasaIvaDefault, empresa.CodigoPostal, empresa.Activa, empresa.Version);

        return new EmpresaDetalleResponse(empresaDto, sucursales, departamentos);
    }
}
