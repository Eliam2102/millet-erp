using System.Text.Json;
using System.Text.RegularExpressions;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// M-327 · póliza de reversa de un anticipo al agente aduanal ya comprobado: cargo al saldo a favor del agente y abono a Anticipo al
/// agente aduanal por lo anticipado; las facturas del agente que la comprobación había saldado vuelven a Proveedores. Payload como el
/// outbox (PascalCase); la plantilla se lee del SQL de la migración.
/// </summary>
public class M327ReversaAnticipoAgenteTests
{
    private static readonly JsonSerializerOptions Opciones = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static string Payload(Guid solicitud, decimal saldo, decimal gastos) => JsonSerializer.Serialize(new
    {
        EventType = N.ReversaComprobacionAnticipoAgente, EmpresaId = Guid.NewGuid(), OcurridoEn = new DateTimeOffset(2026, 11, 10, 18, 0, 0, TimeSpan.Zero),
        AnticipoProveedorId = Guid.NewGuid(), AnticipoAgenteId = Guid.NewGuid(), SolicitudId = solicitud, ProveedorId = Guid.NewGuid(),
        SucursalId = Guid.NewGuid(), SaldoAFavorMxn = saldo, GastosLiberadosMxn = gastos, Fecha = new DateOnly(2026, 11, 10),
        FolioExpediente = "CE-IMP-2026-0142",
    });

    [Fact]
    public void Normaliza_importes_fecha_y_llave_por_solicitud()
    {
        var solicitud = Guid.NewGuid();
        var e = N.Normalizar(N.ReversaComprobacionAnticipoAgente, Guid.NewGuid(), Payload(solicitud, 50000, 42000));
        Assert.Equal(50000, e.Importes["Total"]); Assert.Equal(42000, e.Importes["GastosLiberados"]);
        Assert.Equal(new DateOnly(2026, 11, 10), e.FechaContable);
        Assert.Equal($"{N.ReversaComprobacionAnticipoAgente}:{solicitud}", e.Llave);
        Assert.Equal(solicitud, e.Documento.Id); Assert.NotNull(e.ProveedorId);
        // Otra entrega del mismo evento (otro id de outbox) cae en la misma llave: no duplica la póliza.
        Assert.Equal(e.Llave, N.Normalizar(N.ReversaComprobacionAnticipoAgente, Guid.NewGuid(), Payload(solicitud, 50000, 42000)).Llave);
    }

    [Theory]
    [InlineData(50000, 42000)]
    [InlineData(50000, 0)]
    public void La_plantilla_sembrada_cuadra_y_mueve_el_anticipo_al_saldo_del_agente(decimal saldo, decimal gastos)
    {
        var e = N.Normalizar(N.ReversaComprobacionAnticipoAgente, Guid.NewGuid(), Payload(Guid.NewGuid(), saldo, gastos));
        var plantilla = Plantilla();
        decimal Suma(LadoPartida lado) => plantilla.Where(l => l.Lado == lado).Sum(l => e.Importes[l.Campo]!.Value);
        Assert.Equal(saldo + gastos, Suma(LadoPartida.Cargo)); Assert.Equal(Suma(LadoPartida.Cargo), Suma(LadoPartida.Abono));
        Assert.Contains(plantilla, l => l is { Rol: "ANTICIPO_AGENTE_ADUANAL", Lado: LadoPartida.Abono, Campo: "Total" });
        Assert.Contains(plantilla, l => l is { Rol: "ANTICIPO_A_PROVEEDORES", Lado: LadoPartida.Cargo, Campo: "Total" });
        Assert.Contains(plantilla, l => l is { Rol: "PROVEEDORES", Lado: LadoPartida.Abono, Campo: "GastosLiberados", Auxiliar: AuxiliarPlantilla.Proveedor });
    }

    private static List<LineaPlantillaContable> Plantilla()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var ruta = Directory.GetFiles(Path.Combine(dir.FullName, "backend", "src", "Contabilidad", "Infrastructure", "Persistence", "Migrations"),
            "*_M327PlantillaReversaAnticipoAgente.cs").Single(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var sql = File.ReadAllText(ruta);
        Assert.Contains($"'{N.ReversaComprobacionAnticipoAgente}'", sql, StringComparison.Ordinal);
        var lineas = Regex.Match(sql, @"'(?<lineas>\[\{[^']*\])'::jsonb").Groups["lineas"].Value;
        return JsonSerializer.Deserialize<List<LineaPlantillaContable>>(lineas, Opciones)!;
    }
}
