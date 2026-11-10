using System.Text.Json;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Ports;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// CAMBIO_ALMACEN de la app de Almacenes (M-446, contrato v8; Jorge 9-nov): <c>almacenes[]</c> dice de qué almacén salen las
/// hojas (división + línea, con sus grupos) y <c>almacenDestino</c> a cuál entran. Cubre cambio de división, de línea o de ambas.
/// Mismo parseo y resolución de grupos que las salidas (sin costo: el traslado va al promedio del origen); el registro lo hace
/// <see cref="TrasladoAppAlmacenes"/>.
/// </summary>
public static class AlmacenesAppCambio
{
    public const string Tipo = "CAMBIO_ALMACEN";

    public sealed record Resuelto(Guid EmpresaId, Guid UsuarioId, IReadOnlyList<SalidaApp> Origen, LlaveAlmacenApp Destino);

    public static async Task<(Resuelto? Cambio, string? Error)> ManejarAsync(
        JsonElement datos, IAppSalidaResolverPort resolver, IUsuarioServicioReadPort usuarioServicio, CancellationToken ct)
    {
        if (datos.ValueKind != JsonValueKind.Object) return (null, "datos debe ser un objeto.");
        if (AlmacenesAppSalida.Llave(datos, "almacenDestino") is not { } destino)
            return (null, "datos.almacenDestino.tipo es requerido.");
        var (salida, error) = await AlmacenesAppSalida.ManejarAsync(Tipo, datos, resolver, usuarioServicio, ct);
        if (salida is null) return (null, error);
        var origen = salida.Salidas.Select(s =>
        {
            var motivo = $"{s.Motivo} ({s.Almacen?.ToString() ?? "sin almacén"} → {destino})";
            return s with { Motivo = motivo.Length > 500 ? motivo[..500] : motivo };
        }).ToList();
        return (new Resuelto(salida.EmpresaId, salida.UsuarioId, origen, destino), null);
    }
}
