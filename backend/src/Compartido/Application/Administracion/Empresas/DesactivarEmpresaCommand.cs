using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Empresas;

/// <summary>
/// Desactiva una empresa (F-Admin-PR2.3). Setea
/// <see cref="Empresa.Activa"/> = <c>false</c>. Idempotente: si ya está
/// inactiva, no-op.
///
/// <para>
/// Validación cross-entity (ADR-0011): si existen sucursales activas, no
/// se permite desactivar. Lanza 422
/// <c>EMPRESA_TIENE_SUCURSALES_ACTIVAS</c>.
/// </para>
/// <para>
/// F1-ADM-01: <see cref="Sucursal"/> ya tiene <c>EmpresaId</c>, así que
/// "sucursales activas" se filtra por la empresa que se intenta
/// desactivar (antes del multi-tenant real aplicaba a todas las
/// sucursales del sistema).
/// </para>
/// </summary>
public sealed record DesactivarEmpresaCommand(Guid Id) : IRequest<EmpresaResponse>;

public sealed class DesactivarEmpresaHandler
    : IRequestHandler<DesactivarEmpresaCommand, EmpresaResponse>
{
    private readonly CompartidoDbContext _db;

    public DesactivarEmpresaHandler(CompartidoDbContext db) => _db = db;

    public async Task<EmpresaResponse> Handle(
        DesactivarEmpresaCommand command, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "EMPRESA_NO_ENCONTRADA",
                $"No existe empresa con id '{command.Id}'.");

        if (empresa.Activa)
        {
            var tieneSucursalesActivas = await _db.Sucursales.AsNoTracking()
                .AnyAsync(s => s.EmpresaId == command.Id && s.Estatus == EstatusCatalogo.Activo, cancellationToken);
            if (tieneSucursalesActivas)
            {
                throw new BusinessRuleException(
                    "EMPRESA_TIENE_SUCURSALES_ACTIVAS",
                    "No se puede desactivar la empresa: tiene sucursales activas. " +
                    "Desactiva primero las sucursales asociadas.");
            }

            empresa.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new EmpresaResponse(
            empresa.Id,
            empresa.Rfc,
            empresa.RazonSocial,
            empresa.NombreComercial,
            empresa.RegimenFiscal,
            empresa.TasaIvaDefault,
            empresa.CodigoPostal,
            empresa.Activa,
            empresa.Version);
    }
}
