using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Catalogos;

/// <summary>
/// Adapter <b>real</b> de <see cref="ICatalogosSatReadPort"/> sobre los
/// catálogos SAT ya seedeados en el esquema <c>compartido</c> por
/// <c>Millet.Catalogos</c> (F0-PR4). No recrea catálogos (cuidados-infra §8) —
/// consulta directamente <c>CompartidoDbContext</c>. Los catálogos SAT son
/// cross-empresa (no <c>IPerteneceAEmpresa</c>), así que no aplica el filtro de
/// multi-tenancy.
///
/// <para>
/// Solo cuenta filas <b>activas</b>: una clave existente pero desactivada no
/// debe pasar la validación local previa al timbrado.
/// </para>
/// </summary>
public sealed class CompartidoCatalogosSatReadAdapter : ICatalogosSatReadPort
{
    private readonly CompartidoDbContext _db;

    public CompartidoCatalogosSatReadAdapter(CompartidoDbContext db) => _db = db;

    public Task<bool> ExisteFormaPagoAsync(string claveSat, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(claveSat))
        {
            return Task.FromResult(false);
        }

        return _db.FormasPago
            .AsNoTracking()
            .AnyAsync(f => f.ClaveSat == claveSat && f.Activa, cancellationToken);
    }

    public Task<bool> ExisteUsoCfdiAsync(string claveSat, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(claveSat))
        {
            return Task.FromResult(false);
        }

        return _db.UsosCfdi
            .AsNoTracking()
            .AnyAsync(u => u.ClaveSat == claveSat && u.Activa, cancellationToken);
    }

    public Task<bool> ExisteRegimenFiscalAsync(string codigo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            return Task.FromResult(false);
        }

        return _db.RegimenesFiscales
            .AsNoTracking()
            .AnyAsync(r => r.Codigo == codigo && r.Estatus == EstatusCatalogo.Activo, cancellationToken);
    }

    public Task<bool> ExisteMonedaAsync(string codigo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            return Task.FromResult(false);
        }

        var normalizado = codigo.ToUpperInvariant();
        return _db.Monedas
            .AsNoTracking()
            .AnyAsync(m => m.Codigo == normalizado && m.Activa, cancellationToken);
    }
}
