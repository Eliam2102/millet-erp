using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// M-240: póliza contraria de la reversa de un ajuste de cobro. Mismo camino que el ajuste original (CXC-06): Contabilidad lee la
/// reversa por <see cref="IAjusteCobroContablePort"/> (CxC sirve el dato), toma de la póliza vigente del pago de cliente (#12) la partida
/// del ajuste (rol <c>COMISIONES_BANCARIAS</c> o <c>DIFERENCIAS_EN_COBRO</c>) y la de clientes, y registra sus contrarias por el mismo
/// importe, sucursal, dimensiones y tipo de cambio: abono a la cuenta del ajuste y cargo al cliente. La póliza lleva el mismo documento
/// origen que la del pago (la conciliación de cobros la suma al mismo cobro) y queda ligada a ella como reversa.
/// </summary>
public sealed class ReversaAjusteCobroContableServicio(ContabilidadDbContext db, IAjusteCobroContablePort ajustes, IPolizaContablePort polizas,
    IClock clock) : IReversaAjusteCobroContablePort
{
    public const string RolComision = "COMISIONES_BANCARIAS";
    public const string RolDiferencia = "DIFERENCIAS_EN_COBRO";
    public const string PrefijoLlave = "cxc.reversa-ajuste-cobro";

    public async Task<PolizaRegistrada> RegistrarAsync(Guid empresaId, Guid propuestaId, CancellationToken ct)
    {
        var r = await ajustes.ObtenerReversaAsync(empresaId, propuestaId, ct)
            ?? throw new BusinessRuleException("CONTAB_AJUSTE_COBRO_SIN_REVERSA", "El ajuste del cobro no está revertido en CxC.");

        var (modulo, tipo) = NormalizadorEventos.DocPagoCliente;
        var original = await db.Polizas.IgnoreQueryFilters().AsNoTracking().Include(p => p.Partidas)
            .Where(p => p.EmpresaId == empresaId && p.DeletedAt == null && p.OrigenModulo == modulo && p.OrigenTipo == tipo
                && p.OrigenDocumentoId == r.MovimientoBancarioId && p.Estado == EstadoPoliza.Autorizada
                && p.PolizaAnuladaId == null && p.AnuladaPorPolizaId == null && p.Relacion == null)
            .OrderBy(p => p.Fecha).FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("CONTAB_COBRO_SIN_POLIZA",
                "El cobro no tiene póliza vigente: revisa la bandeja de rechazos del motor contable antes de revertir el ajuste.");

        var rol = r.Tipo == TipoAjusteCobroContable.ComisionBancaria ? RolComision : RolDiferencia;
        var cuentasRol = await db.RolesCuenta.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && x.CuentaId != null && (x.Codigo == rol || x.Codigo.StartsWith(rol + "_")))
            .Select(x => x.CuentaId!.Value).Distinct().ToListAsync(ct);
        var partidas = original.Partidas.OrderBy(d => d.Linea).ToList();
        var ajuste = partidas.FirstOrDefault(d => cuentasRol.Contains(d.CuentaId) && d.Cargo > 0m && d.ClienteId == null)
            ?? throw new BusinessRuleException("CONTAB_COBRO_SIN_PARTIDA_AJUSTE",
                $"La póliza {original.Folio} no tiene la partida del ajuste ({rol}): no hay nada que revertir.");
        var cliente = partidas.FirstOrDefault(d => d.ClienteId == r.ClienteId && d.Abono > 0m)
            ?? partidas.FirstOrDefault(d => d.ClienteId != null && d.Abono > 0m)
            ?? throw new BusinessRuleException("CONTAB_COBRO_SIN_PARTIDA_CLIENTE",
                $"La póliza {original.Folio} no tiene la partida del cliente.");

        var extranjera = ajuste.Moneda != Poliza.MonedaNacional;
        var contrarias = new List<PartidaRequest>
        {
            new(cliente.CuentaId, "Reversa del ajuste: cliente", ajuste.Cargo, 0m, cliente.SucursalId,
                cliente.Dim1Id, cliente.Dim2Id, cliente.Dim3Id, cliente.Proyecto, ClienteId: cliente.ClienteId,
                Moneda: ajuste.Moneda, TipoCambio: ajuste.TipoCambio, ImporteOriginal: extranjera ? ajuste.ImporteOriginal : null,
                Origen: OrigenMovimiento.AuxiliarCxC),
            new(ajuste.CuentaId, Recortar($"Reversa: {ajuste.Concepto}".Trim(), PolizaPartida.ConceptoMaximo), 0m, ajuste.Cargo, ajuste.SucursalId,
                ajuste.Dim1Id, ajuste.Dim2Id, ajuste.Dim3Id, ajuste.Proyecto,
                Moneda: ajuste.Moneda, TipoCambio: ajuste.TipoCambio, ImporteOriginal: extranjera ? ajuste.ImporteOriginal : null,
                Origen: OrigenMovimiento.Automatica),
        };

        var concepto = $"Reversa del ajuste de cobro de {original.Folio}: {r.Motivo}";
        return await polizas.RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica(original.TipoClave, FechaContable.HoyLocal(clock.UtcNow),
            Recortar(concepto, Poliza.ConceptoMaximo),
            new DocumentoOrigenPoliza(original.OrigenModulo!, original.OrigenTipo!, original.OrigenDocumentoId!.Value, original.OrigenFolio, original.OrigenUuid),
            $"{PrefijoLlave}:{propuestaId}", contrarias, OrigenMovimiento.Automatica, empresaId, ReversaDePolizaId: original.Id), ct);
    }

    private static string Recortar(string texto, int maximo) => texto.Length > maximo ? texto[..maximo] : texto;
}
