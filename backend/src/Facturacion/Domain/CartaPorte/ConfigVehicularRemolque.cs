using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.CartaPorte;

/// <summary>
/// H-U33: columna «Remolque» del catálogo oficial SAT <c>c_ConfigAutotransporte</c> (Carta Porte 3.1), embebido como recurso
/// (fuente y actualización en <c>Domain/CartaPorte/Catalogos/README.md</c>). «1» = la configuración exige remolque o semirremolque
/// (el PAC rechaza el CFDI sin el nodo Remolques, CP184); «0» o «0, 1» (opcional) = no se exige.
/// La captura de remolques aún no existe: con una configuración que los exige se rechaza antes de timbrar, sin gastar folio.
/// </summary>
public static class ConfigVehicularRemolque
{
    public const string Codigo = "CARTA_PORTE_REMOLQUE_REQUERIDO";
    private const string Recurso = "Millet.Facturacion.Catalogos.c_ConfigAutotransporte.csv";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Catalogo = new(Cargar);

    /// <summary>True si la configuración vehicular exige remolque según el catálogo; una clave desconocida no se bloquea (decide el PAC).</summary>
    public static bool ExigeRemolque(string? configVehicular) =>
        !string.IsNullOrWhiteSpace(configVehicular)
        && Catalogo.Value.TryGetValue(configVehicular.Trim().ToUpperInvariant(), out var remolque)
        && remolque == "1";

    public static void Validar(string? configVehicular, string placa)
    {
        if (ExigeRemolque(configVehicular))
            throw new BusinessRuleException(Codigo,
                $"El vehículo {placa} tiene configuración {configVehicular!.Trim().ToUpperInvariant()}, que exige remolque o semirremolque, " +
                "y la Carta Porte aún no captura remolques. Usa un vehículo cuya configuración no los exija (por ejemplo C2 o C3); " +
                "no se reservó folio.");
    }

    private static Dictionary<string, string> Cargar()
    {
        using var stream = typeof(ConfigVehicularRemolque).Assembly.GetManifestResourceStream(Recurso)
            ?? throw new InvalidOperationException($"No se encontró el recurso {Recurso}.");
        using var reader = new StreamReader(stream);
        var catalogo = new Dictionary<string, string>(StringComparer.Ordinal);
        reader.ReadLine(); // encabezado
        while (reader.ReadLine() is { } linea)
        {
            var partes = linea.Split(',');
            if (partes.Length == 2 && partes[0].Length > 0)
                catalogo[partes[0].Trim()] = partes[1].Trim();
        }
        return catalogo;
    }
}
