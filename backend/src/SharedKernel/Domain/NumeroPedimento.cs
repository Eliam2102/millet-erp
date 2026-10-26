using Millet.SharedKernel.Application.Exceptions;
namespace Millet.SharedKernel.Domain;

public static class NumeroPedimento
{
    /// <summary>
    /// Quita espacios sin validar. Para reconstruir datos ya guardados: un
    /// pedimento anterior a F1-COM-06 con otro formato no debe impedir leer
    /// la OC. La validación de 15 dígitos va en <see cref="Normalizar"/>, al capturar.
    /// </summary>
    public static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Replace(" ", "");

    public static string? Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var normalizado = valor.Replace(" ", "");
        if (normalizado.Length != 15 || normalizado.Any(c => c < '0' || c > '9'))
            throw new BusinessRuleException("IMPORT_PEDIMENTO_FORMATO_INVALIDO",
                "El número de pedimento debe tener 15 dígitos; puedes separarlos con espacios.");
        return normalizado;
    }
}
