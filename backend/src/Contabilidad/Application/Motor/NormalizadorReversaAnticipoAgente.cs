using Millet.Contabilidad.Domain;
namespace Millet.Contabilidad.Application.Motor;
public static partial class NormalizadorEventos
{
    /// <summary>
    /// M-327 · reversa autorizada (Contador General) de la comprobación de un anticipo al agente aduanal. Importes: <c>Total</c> = todo
    /// lo anticipado, que pasa de Anticipo al agente aduanal al saldo a favor del agente; <c>GastosLiberados</c> = las facturas del
    /// agente que la comprobación había saldado y vuelven a Proveedores. Llave por solicitud: la misma reversa no duplica la póliza.
    /// </summary>
    public const string ReversaComprobacionAnticipoAgente = "cuentas_por_pagar.anticipo-agente.comprobacion-revertida.v1";
    public static readonly (string Modulo, string Tipo) DocReversaAnticipoAgente = ("cuentas_por_pagar", "reversa_comprobacion_anticipo_agente");

    private static EventoContable ReversaAnticipoAgente(Guid eventoId, string payload)
    {
        var p = LeerContrato<ReversaAnticipoAgenteContrato>(payload);
        if (p.EmpresaId == Guid.Empty || p.SolicitudId == Guid.Empty || p.SaldoAFavorMxn <= 0 || p.GastosLiberadosMxn < 0)
            throw new EventoIlegibleException("La reversa del anticipo al agente no trae solicitud ni importes válidos.");
        return new(ReversaComprobacionAnticipoAgente, eventoId, p.EmpresaId, p.Fecha,
            new DocumentoOrigenPoliza(DocReversaAnticipoAgente.Modulo, DocReversaAnticipoAgente.Tipo, p.SolicitudId, p.FolioExpediente, null),
            p.SucursalId, p.ProveedorId, null, null, null, "MXN", 1,
            new Dictionary<string, decimal?> { ["Total"] = p.SaldoAFavorMxn, ["GastosLiberados"] = p.GastosLiberadosMxn }, [], payload,
            LlaveOverride: $"{ReversaComprobacionAnticipoAgente}:{p.SolicitudId}");
    }
    private sealed record ReversaAnticipoAgenteContrato(Guid EmpresaId, Guid SolicitudId, Guid ProveedorId, Guid? SucursalId,
        decimal SaldoAFavorMxn, decimal GastosLiberadosMxn, DateOnly Fecha, string? FolioExpediente);
}
