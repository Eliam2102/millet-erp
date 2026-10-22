using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public sealed record BasePartidaCosto(Guid PartidaId, Guid? ContenedorId, string Descripcion, decimal ValorAduanaMxn,
    decimal PesoKg, decimal Cantidad, Guid? ClienteId = null, Guid? OcId = null, Guid? RecepcionId = null,
    Guid? ArticuloId = null, Guid? UbicacionId = null, decimal Existencia = 0, decimal CostoPromedioMxn = 0, decimal ExistenciaTotal = 0);
public sealed record RenglonDistribucion(Guid GastoId, Guid ConceptoId, BaseDistribucion Base, Guid PartidaId,
    Guid? ClienteId, decimal BaseUsada, decimal ImporteMxn);
public sealed record CostoPartidaDistribuido(Guid PartidaId, string Descripcion, Guid? ClienteId, Guid? OcId,
    Guid? RecepcionId, Guid? ArticuloId, Guid? UbicacionId, decimal CantidadRecibida, decimal ImporteMxn,
    decimal DiferenciaMxn, decimal InventarioMxn, decimal CostoVentasMxn, decimal CostoUnitarioAntes, decimal CostoUnitarioDespues);

public sealed class DistribucionGasto : BaseEntity, IPerteneceAEmpresa, IAuditable, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public int NumeroVersion { get; private set; }
    public int RevisionFinanciera { get; private set; }
    public Guid UsuarioId { get; private set; }
    public DateTimeOffset Fecha { get; private set; }
    public List<RenglonDistribucion> Renglones { get; private set; } = [];
    public List<CostoPartidaDistribuido> Partidas { get; private set; } = [];
    private DistribucionGasto() { }
    internal DistribucionGasto(Guid empresa, Guid expediente, int version, int revision, Guid usuario,
        DateTimeOffset fecha, IEnumerable<RenglonDistribucion> renglones, IEnumerable<CostoPartidaDistribuido> partidas) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa; ExpedienteId = expediente; NumeroVersion = version; RevisionFinanciera = revision;
        UsuarioId = usuario; Fecha = fecha; Renglones = renglones.ToList(); Partidas = partidas.ToList();
    }
}

public static class ProrrateoGastos
{
    public static decimal[] Repartir(decimal importe, IReadOnlyList<decimal> bases)
    {
        if (importe < 0 || decimal.Round(importe, 2) != importe || bases.Count == 0 || bases.Any(b => b < 0) || bases.Sum() <= 0)
            throw new BusinessRuleException("CE_BASE_SIN_DATOS", "La distribución requiere bases no negativas y al menos una positiva.");
        var total = bases.Sum();
        var resultado = bases.Select(b => decimal.Round(importe * b / total, 2, MidpointRounding.AwayFromZero)).ToArray();
        var mayor = Enumerable.Range(0, bases.Count).OrderByDescending(i => bases[i]).First();
        resultado[mayor] += importe - resultado.Sum();
        if (resultado[mayor] < 0)
        {
            // Importes muy pequeños con muchas partidas: distribuir centavos por restos sin negativos.
            resultado = bases.Select(b => decimal.Floor(importe * b / total * 100) / 100).ToArray();
            resultado[mayor] += importe - resultado.Sum();
        }
        return resultado;
    }

    public static IReadOnlyList<RenglonDistribucion> Calcular(TipoExpediente tipo, IEnumerable<GastoExpediente> gastos,
        IReadOnlyList<ConceptoGasto> conceptos, IReadOnlyList<BasePartidaCosto> partidas)
    {
        List<RenglonDistribucion> resultado = [];
        foreach (var gasto in gastos.Where(g => !g.ExcluidoDelCosto))
        {
            var concepto = conceptos.Single(c => c.Id == gasto.ConceptoId);
            var modo = tipo == TipoExpediente.Importacion ? concepto.BaseImportacion : concepto.BaseExportacion;
            var destinos = partidas.Where(p => gasto.ContenedorId is null || p.ContenedorId == gasto.ContenedorId).OrderBy(p => p.PartidaId).ToArray();
            if (destinos.Length == 0) throw new BusinessRuleException("CE_PARTIDAS_FALTANTES", "Faltan partidas para distribuir " + gasto.Concepto + ".");
            decimal[] bases = destinos.Select(p => modo switch
            {
                BaseDistribucion.ValorAduana => p.ValorAduanaMxn,
                BaseDistribucion.Peso => p.PesoKg,
                BaseDistribucion.Cantidad => p.Cantidad,
                BaseDistribucion.Directo => gasto.ImportesDirectos.GetValueOrDefault(p.PartidaId),
                _ => throw new BusinessRuleException("CE_BASE_INVALIDA", "Base desconocida.")
            }).ToArray();
            decimal[] importes;
            if (modo == BaseDistribucion.Directo)
            {
                if (gasto.ImportesDirectos.Any(x => x.Value != 0 && !destinos.Any(p => p.PartidaId == x.Key)) || bases.Sum() != gasto.ImporteMxn)
                    throw new BusinessRuleException("CE_DIRECTO_NO_CUADRA", "Los importes directos por partida deben sumar el gasto en MXN.");
                importes = bases;
            }
            else
            {
                if (bases.Any(b => b <= 0)) throw new BusinessRuleException("CE_BASE_SIN_DATOS", "Capture una base positiva en cada partida de " + gasto.Concepto + ".");
                importes = Repartir(gasto.ImporteMxn, bases);
            }
            resultado.AddRange(destinos.Select((p, i) => new RenglonDistribucion(gasto.Id, concepto.Id, modo, p.PartidaId, p.ClienteId, bases[i], importes[i])));
        }
        return resultado;
    }
}
