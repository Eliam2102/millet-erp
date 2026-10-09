using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Domain.Conteos;

/// <summary>Foto inmutable de la política global vigente al iniciar el conteo.</summary>
public sealed record ConteoUmbrales
{
    public decimal VariacionPctParaRecuento { get; }
    public decimal VariacionValorParaRecuento { get; }
    public decimal UmbralNivel1Maximo { get; }
    public decimal UmbralNivel2Maximo { get; }

    public ConteoUmbrales(decimal variacionPctParaRecuento, decimal variacionValorParaRecuento,
        decimal umbralNivel1Maximo, decimal umbralNivel2Maximo)
    {
        if (variacionPctParaRecuento < 0 || variacionValorParaRecuento < 0 ||
            umbralNivel1Maximo < 0 || umbralNivel2Maximo < 0)
            throw new BusinessRuleException("CONTEO_UMBRALES_NEGATIVOS",
                "Los cuatro umbrales de inventario deben ser mayores o iguales a cero.");
        if (umbralNivel1Maximo >= umbralNivel2Maximo)
            throw new BusinessRuleException("CONTEO_UMBRALES_ORDEN_INVALIDO",
                "El máximo del Nivel 1 debe ser menor que el máximo del Nivel 2.");
        VariacionPctParaRecuento = variacionPctParaRecuento;
        VariacionValorParaRecuento = variacionValorParaRecuento;
        UmbralNivel1Maximo = umbralNivel1Maximo;
        UmbralNivel2Maximo = umbralNivel2Maximo;
    }

    // El neto conserva el signo; la autorización exige el mismo nivel para faltantes y sobrantes.
    public int NivelRequerido(decimal montoNeto) => Math.Abs(montoNeto) <= UmbralNivel1Maximo
        ? 1 : Math.Abs(montoNeto) <= UmbralNivel2Maximo ? 2 : 3;
}
