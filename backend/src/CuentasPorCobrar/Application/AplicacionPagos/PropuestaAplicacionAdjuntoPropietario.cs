using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Ports.Adjuntos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.AplicacionPagos;

/// <summary>
/// M-240: soporte del ajuste de un cobro corto (estado de cuenta o aviso del banco) en el servicio genérico de adjuntos (G1.2). Quien
/// propone lo sube mientras el ajuste espera autorización; autorizado, el soporte queda de solo lectura (rastro de la póliza).
/// Ver: quien propone o quien autoriza (Ingresos); subir y dar de baja: quien propone (supuesto a validar). Sin alcance por sucursal: la propuesta no la tiene (el alcance se valida al crearla).
/// </summary>
public sealed class PropuestaAplicacionAdjuntoPropietario(CuentasPorCobrarDbContext db) : IAdjuntoPropietario
{
    public string TipoEntidad => SoporteAjusteCobro.TipoEntidad;
    public string CodigoNoEncontrado => "PAP_NO_ENCONTRADA";

    // Literales (CxC no referencia Identidad): PermisosCanonicos.CuentasPorCobrar*.
    public string PermisoVer => "cuentas_por_cobrar.aplicacion-pago.proponer|cuentas_por_cobrar.aplicacion-pago.confirmar";
    public string PermisoSubir => "cuentas_por_cobrar.aplicacion-pago.proponer";
    public string PermisoBaja => "cuentas_por_cobrar.aplicacion-pago.proponer";

    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken) =>
        await db.PropuestasAplicacionPago.AsNoTracking().Where(x => x.Id == entidadId)
            .Select(x => new { x.Id, x.EmpresaId, x.Estado, x.AjusteNoFiscal, x.AjusteAutorizadoEn, x.DepositoRef })
            .FirstOrDefaultAsync(cancellationToken) is { } p
            ? new AdjuntoPropietarioInfo(p.Id, p.EmpresaId, null,
                PuedeSubir: p.Estado == EstadoPropuestaAplicacion.Propuesta && p.AjusteNoFiscal < 0 && p.AjusteAutorizadoEn == null,
                null, $"Cobro {p.DepositoRef}")
            : null;

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, AdjuntoOperacion operacion, CancellationToken cancellationToken)
    {
        if (operacion == AdjuntoOperacion.Baja && !info.PuedeSubir)
            throw new BusinessRuleException("PAP_SOPORTE_NO_EDITABLE",
                "El ajuste ya se autorizó o la propuesta ya no está pendiente: su soporte no se da de baja.");
        return Task.CompletedTask;
    }
}
