using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Cuentas;

/// <summary>Catálogo de la empresa para registrar ingresos. No expone saldos ni movimientos de otras sucursales.</summary>
public sealed record CuentaParaIngresoResponse(Guid Id, string Banco, string NumeroCuenta, string Moneda);
public sealed record ListarCuentasParaIngresosQuery : IRequest<IReadOnlyList<CuentaParaIngresoResponse>>;

public sealed class ListarCuentasParaIngresosHandler(TesoreriaDbContext db)
    : IRequestHandler<ListarCuentasParaIngresosQuery, IReadOnlyList<CuentaParaIngresoResponse>>
{
    public async Task<IReadOnlyList<CuentaParaIngresoResponse>> Handle(ListarCuentasParaIngresosQuery request, CancellationToken cancellationToken)
    {
        // El filtro global restringe a la empresa vigente; el alta valida la sucursal del ingreso.
        var cuentas = await db.CuentasBancarias.AsNoTracking().Where(c => c.Activa)
            .OrderBy(c => c.Banco).ThenBy(c => c.Moneda).ToListAsync(cancellationToken);
        return cuentas.Select(c => new CuentaParaIngresoResponse(c.Id, c.Banco, Clabe.Enmascarar(c.NumeroCuenta), c.Moneda)).ToArray();
    }
}
