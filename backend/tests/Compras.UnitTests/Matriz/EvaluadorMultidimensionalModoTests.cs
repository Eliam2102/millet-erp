using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Matriz;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Infrastructure;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.UnitTests.Matriz;

/// <summary>
/// Pruebas unitarias de <see cref="EvaluadorMultidimensional"/> por
/// <see cref="ModoNivel2"/> por departamento: monto contra umbral, disponibilidad
/// con monto mínimo, fila general de respaldo, fail-safe sin umbral y servicios
/// fuera del faltante.
/// </summary>
public sealed class EvaluadorMultidimensionalModoTests
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid Departamento = Guid.CreateVersion7();
    private static readonly Guid Articulo = Guid.CreateVersion7();
    private static readonly Guid Servicio = Guid.CreateVersion7();

    [Fact]
    public async Task CA3_1_Ambos_Umbral20k_Rq15k_Con_Stock_Basta_N1()
    {
        var nivel = await EvaluarAsync(ModoNivel2.Ambos, 20_000m, null, 15_000m, 1m, 10m);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task CA3_1_SoloMonto_Umbral20k_Rq15k_Con_Stock_Basta_N1()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloMonto, 20_000m, null, 15_000m, 1m, 10m);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task CA3_2_Ambos_Rq25k_Pide_N2()
    {
        var nivel = await EvaluarAsync(ModoNivel2.Ambos, 20_000m, null, 25_000m, 1m, 10m);

        Assert.Equal(RequiereNivel.N1YN2, nivel);
    }

    [Fact]
    public async Task CA3_2_SoloMonto_Rq25k_Pide_N2()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloMonto, 20_000m, null, 25_000m, 1m, 10m);

        Assert.Equal(RequiereNivel.N1YN2, nivel);
    }

    [Fact]
    public async Task CA3_3_SoloDisponibilidad_Rq5k_Sin_Existencia_Pide_N2()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloDisponibilidad, 20_000m, null, 5_000m, 1m, 0m);

        Assert.Equal(RequiereNivel.N1YN2, nivel);
    }

    [Fact]
    public async Task C3_1a_SoloMonto_Rq5k_Sin_Existencia_Basta_N1()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloMonto, 20_000m, null, 5_000m, 1m, 0m);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task SoloDisponibilidad_Rq25k_Con_Stock_Basta_N1()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloDisponibilidad, 20_000m, null, 25_000m, 1m, 10m);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task Monto_Igual_Al_Umbral_Se_Queda_En_N1()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloMonto, 20_000m, null, 20_000m, 1m, 10m);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task Minimo_No_Superado_No_Dispara_Disponibilidad()
    {
        var nivel = await EvaluarAsync(ModoNivel2.Ambos, 20_000m, 10_000m, 5_000m, 1m, 0m);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task Minimo_Superado_Dispara_Disponibilidad()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloDisponibilidad, 20_000m, 3_000m, 5_000m, 1m, 0m);

        Assert.Equal(RequiereNivel.N1YN2, nivel);
    }

    [Fact]
    public async Task Minimo_Usa_Importe_A_Comprar_No_El_Total()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloDisponibilidad, 20_000m, 3_000m, 1_000m, 10m, 8m);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task Sin_Umbral_Vigente_Exige_N2()
    {
        var nivel = await EvaluarAsync(null, 20_000m, null, 100m, 1m, 10m);

        Assert.Equal(RequiereNivel.N1YN2, nivel);
    }

    [Fact]
    public async Task Depto_Sin_Fila_Usa_Fila_General()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloMonto, 20_000m, null, 15_000m, 1m, 0m, departamentoFila: Guid.Empty);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task SoloDisponibilidad_Articulo_Servicio_No_Cuenta_Como_Faltante()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloDisponibilidad, 20_000m, null, 100m, 2m, 0m, esServicio: true);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task Articulo_Servicio_No_Consulta_Stock_Al_Evaluar_Disponibilidad()
    {
        var consultas = new List<Guid>();

        var nivel = await EvaluarAsync(ModoNivel2.Ambos, 20_000m, null, 100m, 2m, 0m, esServicio: true, consultas: consultas);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
        Assert.DoesNotContain(Servicio, consultas);
    }

    [Fact]
    public async Task SoloMonto_No_Consulta_Stock()
    {
        var consultas = new List<Guid>();

        var nivel = await EvaluarAsync(ModoNivel2.SoloMonto, 20_000m, null, 5_000m, 1m, 0m, consultas: consultas);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
        Assert.Empty(consultas);
    }

    private static async Task<RequiereNivel> EvaluarAsync(
        ModoNivel2? modo,
        decimal umbral,
        decimal? minimo,
        decimal precio,
        decimal cantidad,
        decimal disponible,
        bool esServicio = false,
        Guid? departamentoFila = null,
        List<Guid>? consultas = null)
    {
        var dbName = Guid.NewGuid().ToString();
        var empresa = new BypassedEmpresaContext();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var db = new ComprasDbContext(
            new DbContextOptionsBuilder<ComprasDbContext>().UseInMemoryDatabase(dbName).Options, empresa);
        await using var compartido = new CompartidoDbContext(
            new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(dbName).Options, empresa);

        if (modo is ModoNivel2 m)
        {
            db.UmbralesAprobacionDepartamento.Add(new UmbralAprobacionDepartamento(
                Empresa, departamentoFila ?? Departamento, hoy.AddDays(-1), umbral,
                modoNivel2: m, montoMinimoDisponibilidad: minimo));
            await db.SaveChangesAsync();
        }

        var requisicion = new Requisicion(
            id: Guid.CreateVersion7(),
            empresaId: Empresa,
            folio: Folio.Parse("MID2026-000001"),
            folioAnio: 2026,
            clasificacion: Clasificacion.MateriaPrima,
            sucursalId: Guid.CreateVersion7(),
            departamentoId: Departamento,
            almacenDestinoId: Guid.CreateVersion7(),
            requisitanteId: Guid.CreateVersion7(),
            creadorId: Guid.CreateVersion7(),
            prioridad: Prioridad.Normal,
            fechaSolicitud: DateTimeOffset.UtcNow,
            descripcion: "Test modo N2");
        var articuloId = esServicio ? Servicio : Articulo;
        requisicion.AgregarLinea(Guid.CreateVersion7(), articuloId, cantidad, "PZA", new Money(precio, "MXN"));

        var evaluador = new EvaluadorMultidimensional(
            db,
            new NaturalezaResolverService(compartido),
            new StockFalso(articuloId, disponible, consultas),
            new ConversionIdentidad(),
            new ArticulosFalsos(esServicio ? Servicio : Guid.Empty),
            new TestClock(DateTimeOffset.UtcNow));

        return await evaluador.EvaluarAsync(requisicion);
    }

    private sealed class StockFalso(Guid articuloId, decimal disponible, List<Guid>? consultas = null) : IConsultarStockPort
    {
        public Task<DisponibilidadStock> ConsultarPorSucursalAsync(
            Guid sucursalId, Guid articuloIdConsultado, CancellationToken cancellationToken)
        {
            consultas?.Add(articuloIdConsultado);
            return Task.FromResult(new DisponibilidadStock(
                OnHand: articuloIdConsultado == articuloId ? disponible : 0m,
                Disponible: articuloIdConsultado == articuloId ? disponible : 0m));
        }
    }

    private sealed class ConversionIdentidad : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(
            Guid articuloId, decimal cantidad, string? unidadCapturada, string unidadDocumento, CancellationToken ct) =>
            Task.FromResult(new ConversionUnidad(cantidad, unidadDocumento, cantidad, 1m, unidadDocumento, cantidad));
    }

    private sealed class ArticulosFalsos(Guid servicioId) : IArticuloReadPort
    {
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> articuloIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                articuloIds.ToDictionary(id => id, id => new ArticuloLectura(id, "CLAVE", "Nombre", id == servicioId)));
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Ambito();

        private sealed class Ambito : IDisposable
        {
            public void Dispose() { }
        }
    }
}
