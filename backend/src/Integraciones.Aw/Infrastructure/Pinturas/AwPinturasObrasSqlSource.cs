using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Pinturas.Catalogos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Pinturas;

/// <summary>
/// Obras de A+W (<c>MILMAIN.SYSADM.KA_OBJEKT</c>) para publicarlas a <c>pinturas_obra</c> con origen AW
/// (Millet, 1-nov, SRC-122/123: las obras se leen de A+W; el alta es manual allá). La clave y la referencia
/// son el <c>ID</c> de KA_OBJEKT (el mismo que viaja como <c>ObraId</c> del pedido); <c>KZ_GESPERRT</c> distinto de 0 = inactiva.
/// Solo lectura; el login de integración ya tiene SELECT sobre KA_OBJEKT (script 04).
/// </summary>
public sealed class AwPinturasObrasSqlSource(
    IIntegracionSqlConnectionFactory fabrica, IOptions<AwPedidosOptions> opciones, ILogger<AwPinturasObrasSqlSource> logger)
    : IPinturasObrasSource
{
    internal const string Sql = "SELECT ID, BEZ, KZ_GESPERRT FROM MILMAIN.SYSADM.KA_OBJEKT WHERE BEZ IS NOT NULL";

    public Task<IReadOnlyList<FilaPinturasObra>> ObtenerObrasAsync(CancellationToken cancellationToken) =>
        SqlPlumbing.EjecutarAsync<IReadOnlyList<FilaPinturasObra>>(fabrica, opciones.Value, logger, "ObrasPinturas", async conexion =>
        {
            using var cmd = SqlPlumbing.CrearCommand(conexion, Sql, opciones.Value);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            return await Leer(reader, cancellationToken);
        }, cancellationToken);

    internal static async Task<IReadOnlyList<FilaPinturasObra>> Leer(System.Data.Common.DbDataReader reader, CancellationToken ct)
    {
        var obras = new List<FilaPinturasObra>();
        while (await reader.ReadAsync(ct))
        {
            var id = Convert.ToInt32(reader.GetValue(0)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            obras.Add(new FilaPinturasObra(id, reader.GetString(1).Trim(), FilaPinturasObra.OrigenAw, id,
                Activo: Convert.ToInt32(reader.GetValue(2)) == 0));
        }
        return obras;
    }
}
