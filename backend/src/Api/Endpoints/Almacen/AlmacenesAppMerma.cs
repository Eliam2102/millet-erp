using System.Globalization;
using System.Text.Json;
using Millet.Almacen.Application.Mermas;
using Millet.Almacen.Domain.Mermas;
using Millet.Almacen.Domain.Ports;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// DESPERDICIO_MESA y MERMA_PROCESO de la app de Almacenes (A4.4). Nombres y campos puestos por el ERP, por validar con Jorge
/// (el contrato v6 no trae estos mensajes). <c>DESPERDICIO_MESA</c>: sucursal, fecha (yyyy-MM-dd) y <c>mesas[]</c> con
/// mesa, articuloAw, m2 y kg opcional (ajuste diario por mesa de corte). <c>MERMA_PROCESO</c>: sucursal, fecha, etapa
/// (corte, templado, laminado, insulado o manejo), motivo, articuloAw, kg y m2. Artículo o sucursal inexistente → ERROR
/// sin guardar nada. El valor lo calcula el ERP (m² × costo promedio); de la app viajan solo cantidades.
/// </summary>
public static class AlmacenesAppMerma
{
    public static readonly string[] Tipos = [MermaProceso.DesperdicioMesa, "MERMA_PROCESO"];

    public sealed record Resuelta(Guid EmpresaId, MermaApp Merma);

    public static async Task<(Resuelta? Merma, string? Error)> ManejarAsync(
        string tipo, JsonElement datos, IAppSalidaResolverPort resolver, IUsuarioServicioReadPort usuarioServicio, CancellationToken ct)
    {
        var sucursal = Texto(datos, "sucursal");
        if (string.IsNullOrWhiteSpace(sucursal)) return (null, "datos.sucursal es requerida.");
        if (!DateOnly.TryParseExact(Texto(datos, "fecha"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            return (null, "datos.fecha es requerida (yyyy-MM-dd).");
        if (fecha > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)) return (null, "datos.fecha no puede ser futura.");

        var filas = new List<(string? Mesa, string Articulo, decimal M2, decimal? Kg)>();
        if (tipo == MermaProceso.DesperdicioMesa)
        {
            if (!datos.TryGetProperty("mesas", out var mesas) || mesas.ValueKind != JsonValueKind.Array || mesas.GetArrayLength() == 0)
                return (null, "mesas debe ser un arreglo con al menos un elemento.");
            var i = 0;
            foreach (var x in mesas.EnumerateArray())
            {
                var (fila, error) = Fila(x, $"mesas[{i++}]", exigeMesa: true, exigeKg: false);
                if (error is not null) return (null, error);
                filas.Add(fila);
            }
            if (filas.GroupBy(f => (f.Mesa!.Trim().ToUpperInvariant(), f.Articulo.Trim())).Any(g => g.Count() > 1))
                return (null, "Hay una mesa repetida con el mismo artículo en el mismo mensaje.");
        }
        else
        {
            var (fila, error) = Fila(datos, "datos", exigeMesa: false, exigeKg: true);
            if (error is not null) return (null, error);
            filas.Add(fila);
            if (!MermaProceso.Etapas.Contains((Texto(datos, "etapa") ?? "").Trim().ToLowerInvariant()))
                return (null, $"datos.etapa debe ser una de: {string.Join(", ", MermaProceso.Etapas)}.");
            if (string.IsNullOrWhiteSpace(Texto(datos, "motivo"))) return (null, "datos.motivo es requerido.");
        }

        var sp = await usuarioServicio.ObtenerReordenAsync(ct);
        if (sp is null || !sp.Activo) return (null, "El usuario de servicio del ERP no está disponible.");
        var r = await resolver.ResolverAsync(sucursal, filas.Select(f => new GrupoSalidaApp(f.Articulo, null, null)).ToList(), ct);
        if (r.Error is not null) return (null, r.Error);

        var lineas = filas.Select((f, k) => new MermaLineaApp(f.Mesa, r.Grupos[k].ArticuloId, f.M2, f.Kg)).ToList();
        return (new Resuelta(sp.EmpresaId, new MermaApp(tipo == MermaProceso.DesperdicioMesa ? tipo : MermaProceso.Merma,
            r.SucursalId, fecha, Texto(datos, "etapa"), Texto(datos, "motivo"), lineas)), null);
    }

    private static ((string? Mesa, string Articulo, decimal M2, decimal? Kg) Fila, string? Error) Fila(
        JsonElement x, string ruta, bool exigeMesa, bool exigeKg)
    {
        if (x.ValueKind != JsonValueKind.Object) return (default, $"{ruta} debe ser un objeto.");
        var mesa = Texto(x, "mesa");
        if (exigeMesa && string.IsNullOrWhiteSpace(mesa)) return (default, $"{ruta}.mesa es requerida.");
        string? articulo = null;
        if (x.TryGetProperty("articuloAw", out var a) && a.ValueKind is JsonValueKind.Number or JsonValueKind.String)
            articulo = a.ValueKind == JsonValueKind.Number ? a.GetRawText() : a.GetString();
        if (string.IsNullOrWhiteSpace(articulo)) return (default, $"{ruta}.articuloAw es requerido.");
        if (!TryNumero(x, "m2", out var m2) || m2 is null or <= 0) return (default, $"{ruta}.m2 debe ser un número mayor que cero.");
        if (!TryNumero(x, "kg", out var kg) || kg is <= 0 || (exigeKg && kg is null))
            return (default, $"{ruta}.kg debe ser un número mayor que cero{(exigeKg ? " y es requerido" : "")}.");
        return ((mesa, articulo, m2.Value, kg), null);
    }

    private static string? Texto(JsonElement o, string n) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryNumero(JsonElement o, string n, out decimal? valor)
    {
        valor = null;
        if (!o.TryGetProperty(n, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out var d)) return false;
        valor = d;
        return true;
    }
}
