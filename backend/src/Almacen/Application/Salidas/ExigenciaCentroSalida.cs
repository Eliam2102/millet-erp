using Millet.Contabilidad.Application.PublicPorts;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Salidas;

/// <summary>
/// M-450 (86e3zr7ft): la salida de consumo (con requisición o por vale) carga a la cuenta de consumo con el centro de cada línea. Si esa
/// cuenta exige área o equipo (regla C-03) y alguna línea no trae centro, la salida no se registra: el dato lo captura el usuario (en la
/// requisición o en el vale), Contabilidad no lo deduce. Sin puerto o sin sucursal no hay exigencia que revisar.
/// </summary>
internal static class ExigenciaCentroSalida
{
    public const string EventoConsumo = "almacen.salida_requisicion.registrada.v1";
    public const string CampoConsumo = "Subtotal";

    public static async Task VerificarAsync(IDimensionesSalidaInventarioPort? dimensiones, Guid empresaId, Guid? sucursalId, DateOnly fecha,
        IReadOnlyList<Guid?> centrosPorLinea, string origen, CancellationToken ct)
    {
        if (dimensiones is null || sucursalId is not { } sucursal) return;
        var sinCentro = centrosPorLinea.Select((c, i) => (c, Linea: i + 1)).Where(x => x.c is null).Select(x => x.Linea).ToList();
        if (sinCentro.Count == 0) return;
        var d = await dimensiones.ResolverAsync(new SolicitudDimensionesSalida(empresaId, sucursal, "consumo", fecha, null, null,
            EventoConsumo, CampoConsumo), ct);
        if (!d.ExigeArea && !d.ExigeEquipo) return;
        var pide = d.ExigeEquipo ? "el área y el equipo" : "el área";
        var lineas = sinCentro.Count == 1 ? $"la línea {sinCentro[0]}" : $"las líneas {string.Join(", ", sinCentro)}";
        throw new BusinessRuleException("SALIDA_SIN_AREA_EQUIPO",
            $"La cuenta {d.CuentaCodigo} del consumo exige {pide} (regla de dimensiones C-03) y {lineas} {origen}.");
    }
}
