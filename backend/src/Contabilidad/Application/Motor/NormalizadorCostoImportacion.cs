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
    /// <summary>
    /// Costo de importación aplicado. H1 paso 4 (86e3z5w25, aditivo): <c>CentroCostoId</c> = área o equipo capturado al distribuir el
    /// expediente en Comercio Exterior; va en el cargo a gasto de importación (plantilla v2, ConCentroCosto). Las líneas de inventario no lo llevan.
    /// </summary>
    public const string CostoImportacionAplicado = "almacen.expediente.costo-aplicado.v1";

    /// <summary>
    /// M-169 (Contabilidad, 10-nov): grupo contable del gasto (lo manda Comercio Exterior vía Almacén) → campo de importe de la
    /// plantilla. Cada campo tiene su línea de cargo y su rol (GASTO_IMPORTACION_HONORARIOS_PF, …). <c>GastoImportacion</c> queda
    /// con lo que no trae grupo (evento anterior a M-169 o grupo desconocido) y carga al rol GASTO_IMPORTACION, como antes.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CamposGastoImportacion = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["HONORARIOS_PF"] = "GastoHonorariosPf",
        ["HONORARIOS_PM"] = "GastoHonorariosPm",
        ["FLETES_MANIOBRAS"] = "GastoFletesManiobras",
        ["ALMACENAJES"] = "GastoAlmacenajes",
        ["OTROS"] = "GastoOtros",
    };

    private static EventoContable CostoImportacion(Guid eventoId, string payload)
    {
        var p = LeerContrato<CostoImportacionContrato>(payload);
        if (p.EmpresaId == Guid.Empty || p.ExpedienteId == Guid.Empty || p.NumeroVersion < 1 || p.Partidas is null ||
            p.Partidas.Any(x => x.DiferenciaMxn != x.InventarioMxn + x.GastoImportacionMxn))
            throw new EventoIlegibleException("Los importes aplicados del expediente no cuadran.");
        var gasto = p.Partidas.Sum(x => x.GastoImportacionMxn);
        var importes = new Dictionary<string, decimal?> { ["Total"] = p.Partidas.Sum(x => x.DiferenciaMxn),
            ["Inventario"] = p.Partidas.Sum(x => x.InventarioMxn) };
        foreach (var campo in CamposGastoImportacion.Values) importes[campo] = 0m;
        // Solo si el desglose cuadra con el gasto aplicado; si no, todo queda en GASTO_IMPORTACION (como un evento sin desglose).
        IReadOnlyList<CostoImportacionGrupo> grupos = p.GastosPorGrupo is { Count: > 0 } g && g.Sum(x => x.ImporteMxn) == gasto ? g : [];
        foreach (var grupo in grupos.Where(x => x.Grupo is not null && CamposGastoImportacion.ContainsKey(x.Grupo)))
            importes[CamposGastoImportacion[grupo.Grupo]] = importes[CamposGastoImportacion[grupo.Grupo]] + grupo.ImporteMxn;
        importes["GastoImportacion"] = gasto - CamposGastoImportacion.Values.Sum(c => importes[c]!.Value);
        return new(CostoImportacionAplicado, eventoId, p.EmpresaId, FechaContable.HoyLocal(p.OcurridoEn),
            new DocumentoOrigenPoliza("almacen", "costo_importacion", p.ExpedienteId, null, null),
            p.SucursalId, null, null, p.CentroCostoId, null, "MXN", 1,
            importes,
            p.Partidas.Select(x => new LineaEventoContable(null, x.InventarioMxn, null, false, ArticuloId: x.ArticuloId)).ToArray(),
            payload, LlaveOverride: $"{CostoImportacionAplicado}:{p.ExpedienteId}:{p.NumeroVersion}");
    }
    private sealed record CostoImportacionContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid ExpedienteId,
        Guid? SucursalId, int NumeroVersion, IReadOnlyList<CostoImportacionLinea>? Partidas, Guid? CentroCostoId = null,
        IReadOnlyList<CostoImportacionGrupo>? GastosPorGrupo = null);
    private sealed record CostoImportacionGrupo(string Grupo, decimal ImporteMxn);
    private sealed record CostoImportacionLinea(Guid ArticuloId, decimal DiferenciaMxn, decimal InventarioMxn, decimal GastoImportacionMxn);
}
