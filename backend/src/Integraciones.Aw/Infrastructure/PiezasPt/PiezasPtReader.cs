using System.Data.Common;
using System.Globalization;
using Millet.ProductoTerminado.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;

public sealed record PtStation(int Estacion, string SucursalClaveAw);
public sealed class PiezasPtOptions
{
    public const string SectionName = "IntegracionesAw:PiezasPt";
    public bool EntradaHabilitada { get; set; }
    public Guid? EmpresaId { get; set; }
    public int IntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 200;
    public int MaxIntentos { get; set; } = 5;
    public int PospuestoHoras { get; set; } = 24;
    public bool ContratoI134Firmado { get; set; }
    // Zona horaria en la que A+W Production guarda bearb_zeit (sin zona). Millet opera en Yucatán.
    public string ZonaHorariaOrigen { get; set; } = "America/Merida";
    // PLATFORM-TODO(<PiezasPtEstacionesJorge>): lista real pendiente de Jorge (A+W-QA-7).
    public List<PtStation> EstacionesPt { get; set; } = [];
}
public sealed record PtSourceScan(string Barcode, string OrderNumber, int Position, int? PieceNumber, DateTime Time, int Station)
{
    public string SourceEventId => $"{Barcode}|{Station.ToString(CultureInfo.InvariantCulture)}|{Time.ToString("O", CultureInfo.InvariantCulture)}";
}
public interface IPiezasPtReader
{
    Task<IReadOnlyList<PtSourceScan>> ReadAsync(PtWatermark cursor, int batchSize, IReadOnlyList<PtStation> stations, CancellationToken ct);
}
public sealed class PiezasPtReader(Func<DbConnection> connectionFactory, bool postgres) : IPiezasPtReader
{
    public async Task<IReadOnlyList<PtSourceScan>> ReadAsync(PtWatermark cursor, int batchSize, IReadOnlyList<PtStation> stations, CancellationToken ct)
    {
        if (stations.Count == 0) return [];
        await using var conn = connectionFactory(); await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand(); cmd.CommandTimeout = 30;
        void Parameter(string name, object value) { var p = cmd.CreateParameter(); p.ParameterName = name; p.Value = value; cmd.Parameters.Add(p); }
        var labels = stations.Select((_, i) => "@station" + i).ToArray();
        for (var i = 0; i < stations.Count; i++) Parameter(labels[i], stations[i].Estacion);
        var barcode = postgres ? "CAST(etikettnr AS text) COLLATE \"C\"" : "CAST(etikettnr AS varchar(100)) COLLATE Latin1_General_100_BIN2";
        cmd.CommandText = $"SELECT {(postgres ? "" : "TOP (@batch)")} etikettnr, auftnr, pos, teile_nr, bearb_zeit, esnr_ist " +
            $"FROM {(postgres ? "aw_origen.awbar_bearbeit" : "alcimdb.dbo.awbar_bearbeit WITH (NOLOCK)")} " +
            $"WHERE fertig = 1 AND esnr_ist IN ({string.Join(',', labels)}) AND " +
            $"(bearb_zeit > @time OR (bearb_zeit = @time AND ({barcode} > @barcode OR ({barcode} = @barcode AND esnr_ist > @station)))) " +
            $"ORDER BY bearb_zeit, {barcode}, esnr_ist {(postgres ? "LIMIT @batch" : "")}";
        Parameter("@batch", batchSize); Parameter("@time", cursor.Timestamp); Parameter("@barcode", cursor.Barcode); Parameter("@station", cursor.Station);
        var result = new List<PtSourceScan>(); await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(new(Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture) ?? "", Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture) ?? "",
                r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture),
                r.IsDBNull(3) ? null : Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture),
                DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Unspecified), Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture)));
        return result;
    }
}
