using Millet.SharedKernel.Application.Exceptions;
namespace Millet.SharedKernel.Domain;

public static class NumeroPedimento
{
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
