using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Cierre;

/// <summary>
/// F1-ALM-13: foto de la valuación al cierre mensual (tabla <c>almacen.valuaciones_cierre</c>). Una fila por grupo del ALFAK
/// (sub-almacén, artículo, medida, lote) con la existencia y el valor al último día del mes, tomada al ejecutar el cierre. El ALFAK de
/// un mes cerrado lee de aquí: un ajuste de valor posterior (incrementables de CE, restauración de costo) ya no cambia la cifra.
/// </summary>
public sealed class ValuacionCierre : IPerteneceAEmpresa
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public Guid PeriodoCerradoId { get; private set; }
    public int Anio { get; private set; }
    public int Mes { get; private set; }
    public Guid SubAlmacenId { get; private set; }
    public Guid ArticuloId { get; private set; }
    public Guid? ProductoAwVarianteId { get; private set; }
    public string? Lote { get; private set; }
    public decimal Cantidad { get; private set; }
    public decimal ValorMxn { get; private set; }

    private ValuacionCierre() { }

    public ValuacionCierre(PeriodoCerrado periodo, Guid subAlmacenId, Guid articuloId, Guid? varianteId, string? lote, decimal cantidad, decimal valorMxn)
    {
        Id = Guid.CreateVersion7();
        EmpresaId = periodo.EmpresaId;
        PeriodoCerradoId = periodo.Id;
        Anio = periodo.Anio;
        Mes = periodo.Mes;
        SubAlmacenId = subAlmacenId;
        ArticuloId = articuloId;
        ProductoAwVarianteId = varianteId;
        Lote = lote;
        Cantidad = cantidad;
        ValorMxn = valorMxn;
    }
}
