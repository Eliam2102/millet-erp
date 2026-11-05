using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;
namespace Millet.Contabilidad.Application.Motor;
public static partial class NormalizadorEventos
{
    public const string PagoAnticipoAgente = "tesoreria.anticipo-agente.pagado.v1";
    public const string DevolucionAnticipoAgente = "tesoreria.anticipo-agente.devolucion-confirmada.v1";
    private static EventoContable DevolucionAgente(Guid eventoId, string payload)
    {
        var p = LeerContrato<DevolucionAgenteContrato>(payload);
        return new(DevolucionAnticipoAgente, eventoId, p.EmpresaId, p.FechaValor,
            new DocumentoOrigenPoliza("tesoreria", "devolucion_anticipo_agente", p.DevolucionId, p.ReferenciaBancaria, null),
            p.SucursalId, p.ProveedorId, null, null, null, "MXN", 1,
            new Dictionary<string, decimal?> { ["Total"] = p.Monto }, [], payload,
            LlaveOverride: $"{DevolucionAnticipoAgente}:{p.DevolucionId}");
    }
    private sealed record DevolucionAgenteContrato(Guid EmpresaId, Guid DevolucionId, decimal Monto,
        DateOnly FechaValor, string? ReferenciaBancaria, Guid SucursalId, Guid ProveedorId);
    public const string CostoImportacionAplicado = "almacen.expediente.costo-aplicado.v1";
    private static EventoContable CostoImportacion(Guid eventoId, string payload)
    {
        var p = LeerContrato<CostoImportacionContrato>(payload);
        if (p.EmpresaId == Guid.Empty || p.ExpedienteId == Guid.Empty || p.NumeroVersion < 1 || p.Partidas is null ||
            p.Partidas.Any(x => x.DiferenciaMxn != x.InventarioMxn + x.GastoImportacionMxn))
            throw new EventoIlegibleException("Los importes aplicados del expediente no cuadran.");
        return new(CostoImportacionAplicado, eventoId, p.EmpresaId, FechaContable.HoyLocal(p.OcurridoEn),
            new DocumentoOrigenPoliza("almacen", "costo_importacion", p.ExpedienteId, null, null),
            p.SucursalId, null, null, null, null, "MXN", 1,
            new Dictionary<string, decimal?> { ["Total"] = p.Partidas.Sum(x => x.DiferenciaMxn),
                ["Inventario"] = p.Partidas.Sum(x => x.InventarioMxn), ["GastoImportacion"] = p.Partidas.Sum(x => x.GastoImportacionMxn) },
            p.Partidas.Select(x => new LineaEventoContable(null, x.InventarioMxn, null, false, ArticuloId: x.ArticuloId)).ToArray(),
            payload, LlaveOverride: $"{CostoImportacionAplicado}:{p.ExpedienteId}:{p.NumeroVersion}");
    }
    private sealed record CostoImportacionContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid ExpedienteId,
        Guid? SucursalId, int NumeroVersion, IReadOnlyList<CostoImportacionLinea>? Partidas);
    private sealed record CostoImportacionLinea(Guid ArticuloId, decimal DiferenciaMxn, decimal InventarioMxn, decimal GastoImportacionMxn);
}
