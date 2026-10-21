using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public enum BaseDistribucion { ValorAduana = 1, Peso = 2, Cantidad = 3, Directo = 4 }
public sealed class ConceptoGasto : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public string Codigo { get; private set; } = "";
    public string Nombre { get; private set; } = "";
    public BaseDistribucion BaseImportacion { get; private set; }
    public BaseDistribucion BaseExportacion { get; private set; }
    public bool EsIvaImportacion => Codigo == "IVA";
    private ConceptoGasto() { }
    public ConceptoGasto(Guid empresa, string codigo, string nombre, BaseDistribucion baseImportacion = BaseDistribucion.ValorAduana) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa; Codigo = codigo; Nombre = nombre;
        Configurar(baseImportacion, BaseDistribucion.Peso);
    }
    public void Configurar(BaseDistribucion importacion, BaseDistribucion exportacion)
    {
        if (!Enum.IsDefined(importacion) || exportacion is not (BaseDistribucion.Peso or BaseDistribucion.ValorAduana))
            throw new BusinessRuleException("CE_BASE_INVALIDA", "Seleccione una base válida; exportación admite peso o valor.");
        BaseImportacion = importacion; BaseExportacion = exportacion;
    }
    public static IReadOnlyList<ConceptoGasto> CrearCatalogo(Guid empresa) => new[]
    {
        ("FLETE_INT", "Flete internacional"), ("FLETE_NAC", "Flete nacional"), ("SEGURO", "Seguro"),
        ("MANIOBRAS", "Maniobras"), ("HONORARIOS", "Honorarios del agente aduanal"),
        ("TERCEROS", "Gastos del agente por cuenta de terceros"), ("ALMACENAJE", "Almacenaje"),
        ("IGI", "Arancel (IGI)"), ("DTA", "DTA"), ("PRV", "PRV"), ("IVA", "IVA de importación"), ("OTROS", "Otros")
    }.Select(x => new ConceptoGasto(empresa, x.Item1, x.Item2, x.Item1 == "IGI" ? BaseDistribucion.Directo : BaseDistribucion.ValorAduana)).ToArray();
}

public sealed record DatosGasto(Guid ConceptoId, Guid ProveedorId, string Factura, string Moneda, decimal Importe,
    DateOnly FechaFactura, string? Pedimento = null, decimal? Cotizado = null, Guid? ContenedorId = null,
    Guid? FacturaProveedorId = null, IReadOnlyDictionary<Guid, decimal>? ImportesDirectos = null);

public sealed class GastoExpediente : BaseEntity, IPerteneceAEmpresa, IAuditable, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public Guid ConceptoId { get; private set; }
    public string Concepto { get; private set; } = "";
    public bool ExcluidoDelCosto { get; private set; }
    public Guid ProveedorId { get; private set; }
    public string Factura { get; private set; } = "";
    public Guid? FacturaProveedorId { get; private set; }
    public string Moneda { get; private set; } = "";
    public decimal Importe { get; private set; }
    public decimal TipoCambio { get; private set; }
    public string FuenteTipoCambio { get; private set; } = "";
    public decimal ImporteMxn { get; private set; }
    public DateOnly FechaFactura { get; private set; }
    public string? Pedimento { get; private set; }
    public decimal? Cotizado { get; private set; }
    public decimal? Diferencia => Cotizado is decimal c ? Importe - c : null;
    public Guid? ContenedorId { get; private set; }
    public Dictionary<Guid, decimal> ImportesDirectos { get; private set; } = [];
    private GastoExpediente() { }
    internal GastoExpediente(Guid id, Guid empresa, Guid expediente) : base(id) { EmpresaId = empresa; ExpedienteId = expediente; }
    internal void Editar(DatosGasto d, ConceptoGasto concepto, decimal tc, string fuente)
    {
        if (d.ProveedorId == Guid.Empty || string.IsNullOrWhiteSpace(d.Factura) || d.Factura.Length > 100 ||
            d.Moneda is not ("MXN" or "USD") || d.Importe <= 0 || d.Importe > 999_999_999_999m || decimal.Round(d.Importe, 2) != d.Importe ||
            tc <= 0 || tc > 1_000_000 || (d.Moneda == "MXN" && tc != 1) || string.IsNullOrWhiteSpace(fuente) ||
            d.FechaFactura == default || d.Pedimento?.Length > 100 || d.Cotizado < 0 || d.FacturaProveedorId == Guid.Empty)
            throw new BusinessRuleException("CE_GASTO_INVALIDO", "Revise proveedor, factura, moneda, fecha e importe positivo a centavos.");
        if (d.ImportesDirectos?.Any(x => x.Key == Guid.Empty || x.Value < 0 || decimal.Round(x.Value, 2) != x.Value) == true)
            throw new BusinessRuleException("CE_DIRECTO_INVALIDO", "Los importes directos son MXN a centavos y no negativos.");
        ConceptoId = concepto.Id; Concepto = concepto.Nombre; ExcluidoDelCosto = concepto.EsIvaImportacion;
        ProveedorId = d.ProveedorId; Factura = d.Factura.Trim(); FacturaProveedorId = d.FacturaProveedorId;
        Moneda = d.Moneda; Importe = d.Importe; TipoCambio = tc; FuenteTipoCambio = fuente;
        ImporteMxn = decimal.Round(d.Importe * tc, 2, MidpointRounding.AwayFromZero);
        FechaFactura = d.FechaFactura; Pedimento = d.Pedimento; Cotizado = d.Cotizado; ContenedorId = d.ContenedorId;
        ImportesDirectos = d.ImportesDirectos?.ToDictionary() ?? [];
    }
}

/// <summary>Base documental de la partida; cantidades y saldo de importación se leen de Almacén.</summary>
public sealed class PartidaCosto : BaseEntity, IPerteneceAEmpresa, IAuditable, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public Guid? ContenedorId { get; private set; }
    public string Descripcion { get; private set; } = "";
    public decimal ValorAduanaMxn { get; private set; }
    public decimal PesoKg { get; private set; }
    public string Pedimento { get; private set; } = "";
    private PartidaCosto() { }
    internal PartidaCosto(Guid id, Guid empresa, Guid expediente) : base(id) { EmpresaId = empresa; ExpedienteId = expediente; }
    internal void Editar(Guid? contenedor, string descripcion, decimal valor, decimal peso, string pedimento)
    {
        if (string.IsNullOrWhiteSpace(descripcion) || descripcion.Length > 200 || valor < 0 || peso < 0 ||
            valor > 999_999_999_999m || peso > 999_999_999m || string.IsNullOrWhiteSpace(pedimento) || pedimento.Length > 100)
            throw new BusinessRuleException("CE_PARTIDA_COSTO_INVALIDA", "Capture descripción, pedimento, valor en MXN y peso no negativos.");
        ContenedorId = contenedor; Descripcion = descripcion.Trim(); ValorAduanaMxn = valor; PesoKg = peso; Pedimento = pedimento.Trim();
    }
}
