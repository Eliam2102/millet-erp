using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Administracion.Application.Empleados;

/// <summary>
/// Generador autoincremental de claves de empleado con prefijo "EMP-".
/// Busca las claves existentes en la empresa que sigan el patrón EMP-{número},
/// toma el número máximo y genera el consecutivo formateado con padding de 3 dígitos (EMP-001, EMP-002, etc.).
/// </summary>
public static partial class GeneradorClaveEmpleado
{
    private static readonly Regex EmpClaveRegex = new(
        @"^EMP-(\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public const string Prefijo = "EMP-";

    public static async Task<string> GenerarSiguienteClaveAsync(
        CompartidoDbContext db,
        Guid empresaId,
        CancellationToken ct = default)
    {
        var claves = await db.Empleados.AsNoTracking()
            .Where(e => e.EmpresaId == empresaId)
            .Select(e => e.Clave)
            .ToListAsync(ct);

        var max = 0;
        foreach (var clave in claves)
        {
            var match = EmpClaveRegex.Match(clave);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var num) && num > max)
            {
                max = num;
            }
        }

        var siguiente = max + 1;
        var candidata = FormatearClave(siguiente);
        while (claves.Contains(candidata, StringComparer.OrdinalIgnoreCase))
        {
            siguiente++;
            candidata = FormatearClave(siguiente);
        }

        return candidata;
    }

    public static string FormatearClave(int numero) => $"{Prefijo}{numero:D3}";
}
