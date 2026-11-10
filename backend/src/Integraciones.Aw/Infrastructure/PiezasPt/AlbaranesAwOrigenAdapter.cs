using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;

/// <summary>
/// Lector SOLO SELECT de <c>dbo.vw_erp_albaran_pos</c> (script 20, BD de integración). Trae todos los albaranes de los
/// pedidos que tienen alguno de la ventana, para que PT sume la posición completa (entregas parciales en varios albaranes).
/// Demo en PostgreSQL (tabla con el mismo nombre); Real por la conexión de integración.
/// </summary>
public sealed class AlbaranesAwOrigenAdapter(Func<DbConnection> fabrica, bool postgres) : IAlbaranesAwOrigenReader
{
    internal const string Sql =
        "SELECT a.numero_pedido, a.numero_posicion, a.albaran_folio, a.albaran_fecha, a.piezas " +
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
        {
            var fecha = r.GetValue(3);
            filas.Add(new(Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture)!.Trim(),
                Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture),
                // Npgsql entrega date como DateOnly; SQL Server, como DateTime.
                fecha is DateOnly d ? d : DateOnly.FromDateTime(Convert.ToDateTime(fecha, CultureInfo.InvariantCulture)),
                r.IsDBNull(4) ? 0 : Convert.ToDecimal(r.GetValue(4), CultureInfo.InvariantCulture)));
        }
        return filas;
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
