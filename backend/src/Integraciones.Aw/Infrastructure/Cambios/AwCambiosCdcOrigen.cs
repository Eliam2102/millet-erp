using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Cambios;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Cambios;

/// <summary>
/// Lector de cambios sobre CDC (<c>cdc.fn_cdc_get_all_changes_*</c>). Requiere CDC habilitado en A+W
/// (instancias <c>SYSADM_KU_KUNDEN</c>, <c>SYSADM_BA_PRODUKTE</c>, <c>SYSADM_BA_STUKL</c>, <c>SYSADM_BA_PRODUKTE_BEZ</c>) y SELECT/EXECUTE del lector sobre <c>cdc</c>.
/// </summary>
public sealed class AwCambiosCdcOrigen(
    IIntegracionSqlConnectionFactory factory, AwPedidosOptions plomeria, ILogger<AwCambiosCdcOrigen> logger) : IAwCambiosOrigen
{
    // Último cambio por llave dentro de (desde, hasta]; WITH TIES para no partir un LSN a la mitad.
    // Instancia y columna son constantes de este archivo, nunca entrada externa.
    private const string Plantilla = """
        SELECT TOP (@n) WITH TIES CAST(k AS nvarchar(20)) AS ref, op, lsn FROM (
          SELECT k = {KEY}, op = __$operation, lsn = __$start_lsn,
                 rn = ROW_NUMBER() OVER (PARTITION BY {KEY} ORDER BY __$start_lsn DESC, __$seqval DESC)
            FROM cdc.fn_cdc_get_all_changes_{INST}(@desde, @hasta, N'all')) x
         WHERE rn = 1 ORDER BY lsn
        """;

    private static string Sql(string inst, string key) => Plantilla.Replace("{INST}", inst).Replace("{KEY}", key);

    private static readonly (string Inst, string Sql, bool Borra)[] Fuentes =
    {
        ("SYSADM_KU_KUNDEN", Sql("SYSADM_KU_KUNDEN", "ID"), true),
    };

    private static readonly (string Inst, string Sql, bool Borra)[] FuentesProducto =
    {
        ("SYSADM_BA_PRODUKTE", Sql("SYSADM_BA_PRODUKTE", "BA_PRODUKT"), true),
        ("SYSADM_BA_STUKL", Sql("SYSADM_BA_STUKL", "PRODUKT"), false), // línea borrada/cambiada = producto modificado
        ("SYSADM_BA_PRODUKTE_BEZ", Sql("SYSADM_BA_PRODUKTE_BEZ", "BA_PRODUKT"), false), // descripción por idioma
    };

    public Task<string> ObtenerLsnActualAsync(CancellationToken ct) =>
        SqlPlumbing.EjecutarAsync(factory, plomeria, logger, "CdcLsnActual", async c =>
        {
            using var cmd = SqlPlumbing.CrearCommand(c, "SELECT sys.fn_cdc_get_max_lsn()", plomeria);
            return Hex((byte[]?)await cmd.ExecuteScalarAsync(ct) ?? throw SinCdc());
        }, ct);

    public Task<AwCambiosLote> LeerCambiosAsync(AwEntidadCambio entidad, string? desdeLsn, int tamano, CancellationToken ct) =>
        SqlPlumbing.EjecutarAsync(factory, plomeria, logger, "CdcLeerCambios", async c =>
        {
            tamano = Math.Clamp(tamano, 1, 5000);
            var hasta = (byte[]?)await Escalar(c, "SELECT sys.fn_cdc_get_max_lsn()", ct) ?? throw SinCdc();
            if (desdeLsn is not null && Comparar(Siguiente(Convert.FromHexString(desdeLsn)), hasta) > 0)
                return new AwCambiosLote([], desdeLsn, Completo: true); // nada nuevo desde entonces
            var fuentes = entidad == AwEntidadCambio.Cliente ? Fuentes : FuentesProducto;

            var porRef = new Dictionary<string, (AwTipoCambio Tipo, string Lsn)>();
            string? tope = null; // LSN máximo leído de la fuente llena más atrasada: más allá no se leyó todo
            foreach (var (inst, sql, borra) in fuentes)
            {
                var min = (byte[]?)await Escalar(c, "SELECT sys.fn_cdc_get_min_lsn(@i)", ct, ("@i", SqlDbType.NVarChar, inst)) ?? throw SinCdc();
                var desde = desdeLsn is null ? min : Siguiente(Convert.FromHexString(desdeLsn)); // desdeLsn = ya procesado (exclusivo)
                if (Comparar(desde, min) < 0)
                    throw new AwReaderException("El LSN guardado es anterior al mínimo de CDC (limpieza); se requiere barrido completo.",
                        kind: "cdc_lsn_expirado", isTransient: false);

                using var cmd = SqlPlumbing.CrearCommand(c, sql, plomeria);
                cmd.Parameters.Add(new SqlParameter("@n", SqlDbType.Int) { Value = tamano });
                cmd.Parameters.Add(new SqlParameter("@desde", SqlDbType.Binary, 10) { Value = desde });
                cmd.Parameters.Add(new SqlParameter("@hasta", SqlDbType.Binary, 10) { Value = hasta });
                var n = 0;
                string? maxFuente = null;
                using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    n++;
                    var referencia = r.GetString(0);
                    var tipo = borra && r.GetInt32(1) == 1 ? AwTipoCambio.Eliminado : AwTipoCambio.Upsert;
                    var lsn = Hex((byte[])r.GetValue(2));
                    maxFuente = lsn;
                    if (!porRef.TryGetValue(referencia, out var previo) || string.CompareOrdinal(lsn, previo.Lsn) > 0)
                        porRef[referencia] = (tipo, lsn);
                }
                if (n >= tamano && (tope is null || string.CompareOrdinal(maxFuente, tope) < 0)) tope = maxFuente;
            }

            // Lote lleno: procesado hasta el tope (WITH TIES garantiza grupos de LSN completos) y se descarta lo posterior. Si no, hasta @hasta.
            var cambios = porRef.Where(x => tope is null || string.CompareOrdinal(x.Value.Lsn, tope) <= 0)
                .OrderBy(x => x.Value.Lsn, StringComparer.Ordinal).ToList();
            return new AwCambiosLote(cambios.Select(x => new AwCambio(x.Key, x.Value.Tipo)).ToList(), tope ?? Hex(hasta), Completo: tope is null);
        }, ct);

    private async Task<object?> Escalar(System.Data.Common.DbConnection c, string sql, CancellationToken ct, (string, SqlDbType, object)? p = null)
    {
        using var cmd = SqlPlumbing.CrearCommand(c, sql, plomeria);
        if (p is var (nombre, tipo, valor)) cmd.Parameters.Add(new SqlParameter(nombre, tipo) { Value = valor });
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }

    private static AwReaderException SinCdc() =>
        new("CDC no está habilitado en la BD de A+W (sin LSN).", kind: "cdc_no_habilitado", isTransient: false);

    /// <summary>LSN + 1 (10 bytes big-endian, con acarreo), como <c>sys.fn_cdc_increment_lsn</c>.</summary>
    private static byte[] Siguiente(byte[] lsn)
    {
        var r = (byte[])lsn.Clone();
        for (var i = r.Length - 1; i >= 0 && ++r[i] == 0; i--) { }
        return r;
    }

    private static string Hex(byte[] lsn) => Convert.ToHexString(lsn);
    private static int Comparar(byte[] a, byte[] b) => a.AsSpan().SequenceCompareTo(b);
}
