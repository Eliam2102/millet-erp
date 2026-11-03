using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Infrastructure.Adapters;

/// <summary>
/// CXC-06 (86e3qddef): CxC sirve a Contabilidad el ajuste autorizado de una propuesta de aplicación. Corre desde el motor contable
/// (sin usuario ni empresa en contexto): ignora los filtros globales y filtra por la empresa del evento.
/// </summary>
public sealed class AjusteCobroContableAdapter(CuentasPorCobrarDbContext db) : IAjusteCobroContablePort
{
    public async Task<AjusteCobroContable?> ObtenerAsync(Guid empresaId, Guid propuestaId, CancellationToken ct)
    {
        var p = await db.PropuestasAplicacionPago.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && x.Id == propuestaId)
            .Select(x => new { x.AjusteNoFiscal, x.TipoAjuste, x.MotivoAjuste, x.AjusteAutorizadoPor, x.AjusteAutorizadoEn })
            .FirstOrDefaultAsync(ct);
        if (p is null || p.AjusteNoFiscal >= 0 || p.TipoAjuste is null
            || p.AjusteAutorizadoEn is not DateTimeOffset en)
            return null;
        var tipo = p.TipoAjuste == TipoAjusteCobro.ComisionBancaria
            ? TipoAjusteCobroContable.ComisionBancaria : TipoAjusteCobroContable.DiferenciaMenor;
        return new AjusteCobroContable(tipo, Math.Abs(p.AjusteNoFiscal), p.MotivoAjuste ?? string.Empty, p.AjusteAutorizadoPor, en);
    }
}
