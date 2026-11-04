using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Consumo;

/// <summary>
/// Norma de consumo de un artículo componente (A4.8, D3): cuánto se consume por unidad de medida de la pieza
/// terminada (m² de área o m lineal de perímetro, según la unidad del artículo) y su % de merma. Para un artículo en
/// M³ (argón, D4) <see cref="FactorM3PorM2"/> convierte el área de la pieza a m³. Tabla
/// <c>almacen.normas_consumo_componente</c>, una por (empresa, artículo). El catálogo arranca vacío: sin norma el
/// consumo queda en la bandeja de revisión.
/// </summary>
public sealed class NormaConsumoComponente : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ArticuloId { get; private set; }
    public decimal ConsumoPorUnidad { get; private set; }
    public decimal PorcentajeMerma { get; private set; }
    public decimal? FactorM3PorM2 { get; private set; }

    private NormaConsumoComponente() { }

    public NormaConsumoComponente(Guid articuloId, decimal consumoPorUnidad, decimal porcentajeMerma, decimal? factorM3PorM2)
        : base(Guid.CreateVersion7())
    {
        if (articuloId == Guid.Empty)
            throw new BusinessRuleException("NORMA_CONSUMO_ARTICULO_INVALIDO", "El artículo es obligatorio.");
        ArticuloId = articuloId;
        Actualizar(consumoPorUnidad, porcentajeMerma, factorM3PorM2);
    }

    public void Actualizar(decimal consumoPorUnidad, decimal porcentajeMerma, decimal? factorM3PorM2)
    {
        if (consumoPorUnidad <= 0)
            throw new BusinessRuleException("NORMA_CONSUMO_INVALIDA", "El consumo por unidad debe ser mayor que cero.");
        if (porcentajeMerma is < 0 or >= 100)
            throw new BusinessRuleException("NORMA_MERMA_INVALIDA", "La merma debe estar entre 0 y menos de 100 %.");
        if (factorM3PorM2 is <= 0)
            throw new BusinessRuleException("NORMA_FACTOR_INVALIDO", "El factor m² → m³ debe ser mayor que cero.");
        ConsumoPorUnidad = consumoPorUnidad;
        PorcentajeMerma = porcentajeMerma;
        FactorM3PorM2 = factorM3PorM2;
    }
}
