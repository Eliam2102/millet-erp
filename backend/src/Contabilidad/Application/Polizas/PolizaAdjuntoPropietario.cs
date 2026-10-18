using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Polizas;

/// <summary>
/// Documento soporte de la póliza dentro del ERP (C1.3, R7; expediente G1.2). Se adjunta y se da de baja mientras la póliza es
/// borrador; autorizada es inmutable (C1.3-b) y sus adjuntos quedan de solo lectura. La póliza es de la empresa: sin alcance por sucursal.
/// </summary>
public sealed class PolizaAdjuntoPropietario(ContabilidadDbContext db) : IAdjuntoPropietario
{
    public const string Tipo = "poliza";

    public string TipoEntidad => Tipo;
    public string CodigoNoEncontrado => "CONTAB_POLIZA_NO_ENCONTRADA";

    // Literales (Contabilidad no referencia Identidad): PermisosCanonicos.ContabilidadPolizas*.
    public string PermisoVer => "contabilidad.polizas.leer";
    public string PermisoSubir => "contabilidad.polizas.capturar";
    public string PermisoBaja => "contabilidad.polizas.capturar";

    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken) =>
        await db.Polizas.AsNoTracking().Where(x => x.Id == entidadId)
            .Select(x => new { x.Id, x.EmpresaId, x.Estado, x.TipoClave, x.Numero, x.Concepto })
            .FirstOrDefaultAsync(cancellationToken) is { } p
            ? new AdjuntoPropietarioInfo(p.Id, p.EmpresaId, null, p.Estado == EstadoPoliza.Borrador, null,
                p.Numero is { } n ? Poliza.FormatearFolio(p.TipoClave, n) : $"Borrador · {p.Concepto}")
            : null;

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, AdjuntoOperacion operacion, CancellationToken cancellationToken)
    {
        if (operacion == AdjuntoOperacion.Baja && !info.PuedeSubir)
            throw new BusinessRuleException("CONTAB_POLIZA_NO_EDITABLE", "La póliza ya está autorizada: sus documentos soporte no se dan de baja.");
        return Task.CompletedTask;
    }
}
