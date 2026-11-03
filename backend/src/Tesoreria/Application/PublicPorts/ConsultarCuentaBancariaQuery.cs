using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.PublicPorts;

/// <summary>
/// Puerto público de lectura (86e3tfdve): datos de una cuenta bancaria propia
/// para que Facturación arme la cuenta beneficiaria del complemento de pago.
/// Devuelve el número y la CLABE completos: el consumidor los usa solo para el
/// CFDI y los enmascara al mostrarlos (ADR-0006). El filtro global limita a la
/// empresa del request.
/// </summary>
public sealed record ConsultarCuentaBancariaQuery(Guid CuentaBancariaId) : IRequest<CuentaBancariaLectura?>;

public sealed record CuentaBancariaLectura(Guid Id, string Banco, string NumeroCuenta, string? Clabe, string Moneda, bool Activa);

public sealed class ConsultarCuentaBancariaHandler(TesoreriaDbContext db)
    : IRequestHandler<ConsultarCuentaBancariaQuery, CuentaBancariaLectura?>
{
    public Task<CuentaBancariaLectura?> Handle(ConsultarCuentaBancariaQuery q, CancellationToken cancellationToken) =>
        db.CuentasBancarias.AsNoTracking()
            .Where(c => c.Id == q.CuentaBancariaId)
            .Select(c => new CuentaBancariaLectura(c.Id, c.Banco, c.NumeroCuenta, c.Clabe, c.Moneda, c.Activa))
            .FirstOrDefaultAsync(cancellationToken);
}
