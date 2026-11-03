using System.Globalization;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;

/// <summary>Lista de estaciones de PT desde configuración ("a-b" o "n").</summary>
public sealed class EstacionesPtFiltro
{
    private EstacionesPtFiltro(List<(int Desde, int Hasta)> rangos) => Rangos = rangos;
    public IReadOnlyList<(int Desde, int Hasta)> Rangos { get; }
    public bool Vacio => Rangos.Count == 0;
    public bool Contiene(int estacion) => Rangos.Any(r => estacion >= r.Desde && estacion <= r.Hasta);

    public static EstacionesPtFiltro Parse(IEnumerable<string> valores)
    {
        var rangos = new List<(int, int)>();
        foreach (var crudo in valores)
        {
            var texto = (crudo ?? "").Trim();
            var partes = texto.Split('-');
            if (partes.Length is < 1 or > 2 || !TryInt(partes[0], out var desde) || !TryInt(partes.Length == 2 ? partes[1] : partes[0], out var hasta) || hasta < desde)
                throw new ArgumentException($"Estación o rango de PT inválido: '{texto}'.", nameof(valores));
            rangos.Add((desde, hasta));
        }
        return new(rangos);
    }

    private static bool TryInt(string s, out int n) => int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n);
}
