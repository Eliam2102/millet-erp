using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Integraciones.Aw.Application.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;

/// <summary>
/// Lector SOLO SELECT de <c>dbo.vw_erp_albaran_pos</c> (script 20, BD de integración). Trae todos los albaranes de los
/// pedidos que tienen alguno de la ventana, para que PT sume la posición completa (entregas parciales en varios albaranes).
/// Demo en PostgreSQL (tabla con el mismo nombre); Real por la conexión de integración.
/// </summary>
public sealed class AlbaranesAwOrigenAdapter(Func<DbConnection> fabrica, bool postgres, ILogger? logger = null) : IAlbaranesAwOrigenReader
{
    internal const string Sql =
        "SELECT a.numero_pedido, a.numero_posicion, a.albaran_folio, a.albaran_fecha, a.piezas, a.pedido_albaran " +
        "FROM dbo.vw_erp_albaran_pos a " +
        "WHERE a.numero_pedido IN (SELECT n.numero_pedido FROM dbo.vw_erp_albaran_pos n WHERE n.albaran_fecha >= @desde) " +
        "ORDER BY a.numero_pedido, a.numero_posicion, a.albaran_fecha, a.albaran_folio";

    public Task<string> OrigenActualAsync(CancellationToken ct) => Task.FromResult(postgres ? "Demo" : "Real");

    public async Task<IReadOnlyList<AlbaranPosicionOrigen>> LeerAsync(DateOnly desde, CancellationToken ct)
    {
        await using var c = fabrica();
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandTimeout = 60;
        cmd.CommandText = Sql;
        var p = cmd.CreateParameter(); p.ParameterName = "desde"; p.DbType = DbType.Date; p.Value = desde.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(p);

        var filas = new List<AlbaranPosicionOrigen>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            if (Mapear(r) is { } fila) filas.Add(fila);
            else (logger ?? NullLogger.Instance).LogWarning(
                "Fila de vw_erp_albaran_pos omitida: pedido, posición, folio, fecha o pedido del albarán nulos o inválidos (pedido {Pedido}).",
                r.IsDBNull(0) ? null : Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture));
        return filas;
    }

    /// <summary>
    /// Una fila de la vista, o null si le falta pedido, posición, folio, fecha o pedido del albarán: una fila mala se
    /// omite (con aviso) y no tumba el ciclo. Piezas nulas cuentan como 0.
    /// </summary>
    internal static AlbaranPosicionOrigen? Mapear(IDataRecord r)
    {
        try
        {
            if (r.IsDBNull(0) || r.IsDBNull(1) || r.IsDBNull(2) || r.IsDBNull(3) || r.IsDBNull(5)) return null;
            var pedido = Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture)!.Trim();
            var pedidoAlbaran = Convert.ToString(r.GetValue(5), CultureInfo.InvariantCulture)!.Trim();
            if (pedido.Length == 0 || pedidoAlbaran.Length == 0) return null;
            var fecha = r.GetValue(3);
            return new(pedido,
                Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture),
                // Npgsql entrega date como DateOnly; SQL Server, como DateTime.
                fecha is DateOnly d ? d : DateOnly.FromDateTime(Convert.ToDateTime(fecha, CultureInfo.InvariantCulture)),
                r.IsDBNull(4) ? 0 : Convert.ToDecimal(r.GetValue(4), CultureInfo.InvariantCulture),
                pedidoAlbaran);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException) { return null; }
    }
}

/// <summary>Elige el lector Demo o Real según el origen activo de A+W (mismo criterio que PT y Serie 9000).</summary>
public sealed class AlbaranesAwOrigenSelector(IServiceProvider services, AwOrigenSesion sesion) : IAlbaranesAwOrigenReader
{
    public async Task<string> OrigenActualAsync(CancellationToken ct) => await sesion.EsDemoAsync(ct) ? "Demo" : "Real";

    public async Task<IReadOnlyList<AlbaranPosicionOrigen>> LeerAsync(DateOnly desde, CancellationToken ct) =>
        await (services.GetKeyedService<IAlbaranesAwOrigenReader>(await OrigenActualAsync(ct))
            ?? throw new BusinessRuleException("PT_ALBARAN_AW_ORIGEN_NO_HABILITADO",
                "Use Demo mientras no esté firmado I13.4; revise la conexión de integración y la bandera de albaranes de A+W."))
            .LeerAsync(desde, ct);
}
