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
/// <see cref="ModoNivel2"/> (S3). Desde la minuta Millet del 12-oct la RQ no
/// lleva monto: el monto contra el umbral se valida en la OC. Estas pruebas
/// cubren que la RQ nunca sube a N2 por monto, que la disponibilidad se activa
/// según el modo y que los artículos de servicio no cuentan como faltante.
/// </summary>
public sealed class EvaluadorMultidimensionalModoTests
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid Departamento = Guid.CreateVersion7();
    private static readonly Guid Articulo = Guid.CreateVersion7();
    private static readonly Guid Servicio = Guid.CreateVersion7();

    [Fact]
    public async Task Sin_Fila_De_Settings_Sin_Existencia_Solo_N1()
    {
        // Sin fila de settings el modo es SoloMonto: en la RQ no hay criterio de disponibilidad.
        var nivel = await EvaluarAsync(modo: null, precio: 100m, cantidad: 2m, disponible: 0m);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task SoloMonto_Monto_Excedido_No_Sube_A_N2_En_La_Requisicion()
    {
        // Total 2 × 1000 = 2000: la RQ ya no sube a N2 por monto (lo valida la OC).
        var nivel = await EvaluarAsync(ModoNivel2.SoloMonto, precio: 1_000m, cantidad: 2m, disponible: 0m);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task SoloDisponibilidad_Ignora_Monto_Excedido_Si_Hay_Existencia()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloDisponibilidad, precio: 1_000m, cantidad: 2m, disponible: 10m);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task SoloDisponibilidad_Sin_Existencia_Suficiente_Exige_N2()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloDisponibilidad, precio: 100m, cantidad: 2m, disponible: 1m);

        Assert.Equal(RequiereNivel.N1YN2, nivel);
    }

    [Fact]
    public async Task SoloDisponibilidad_Articulo_Servicio_No_Cuenta_Como_Faltante()
    {
        var nivel = await EvaluarAsync(ModoNivel2.SoloDisponibilidad, precio: 100m, cantidad: 2m, disponible: 0m, esServicio: true);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task Articulo_Servicio_No_Consulta_Stock_Al_Evaluar_Disponibilidad()
    {
        var consultas = new List<Guid>();

        var nivel = await EvaluarAsync(ModoNivel2.Ambos, precio: 100m, cantidad: 2m, disponible: 0m,
            esServicio: true, consultas: consultas);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
        Assert.DoesNotContain(Servicio, consultas);
    }

    [Fact]
    public async Task Ambos_Exige_N2_Por_Disponibilidad_Aunque_Monto_Sea_Bajo()
    {
        var nivel = await EvaluarAsync(ModoNivel2.Ambos, precio: 100m, cantidad: 2m, disponible: 0m);

        Assert.Equal(RequiereNivel.N1YN2, nivel);
    }

    [Fact]
    public async Task Ambos_Con_Existencia_Total_Y_Monto_Excedido_No_Sube_A_N2()
    {
        var nivel = await EvaluarAsync(ModoNivel2.Ambos, precio: 1_000m, cantidad: 2m, disponible: 10m);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task Linea_Sin_Precio_Con_Faltante_Exige_N2_Por_Disponibilidad()
    {
        // Precio 0 (RQ sin precio estimado): la evaluación depende solo de la existencia.
        var nivel = await EvaluarAsync(ModoNivel2.SoloDisponibilidad, precio: 0m, cantidad: 2m, disponible: 0m);

        Assert.Equal(RequiereNivel.N1YN2, nivel);
    }

    [Fact]
    public void Setter_Rechaza_Valor_Fuera_De_Enum()
    {
        var settings = new ComprasSettings(Guid.CreateVersion7(), Empresa, autoGenerarOcAlAutorizar: false);

        Assert.Equal(ModoNivel2.SoloMonto, settings.ModoNivel2);
        Assert.Throws<BusinessRuleException>(() => settings.EstablecerModoNivel2((ModoNivel2)99));
    }

    private static async Task<RequiereNivel> EvaluarAsync(
        ModoNivel2? modo,
        decimal precio,
        decimal cantidad,
        decimal disponible,
        bool esServicio = false,
        List<Guid>? consultas = null)
    {
        var dbName = Guid.NewGuid().ToString();
        var empresa = new BypassedEmpresaContext();
        await using var db = new ComprasDbContext(
            new DbContextOptionsBuilder<ComprasDbContext>().UseInMemoryDatabase(dbName).Options, empresa);
        await using var compartido = new CompartidoDbContext(
            new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(dbName).Options, empresa);

        if (modo is ModoNivel2 m)
        {
            var settings = new ComprasSettings(Guid.CreateVersion7(), Empresa, autoGenerarOcAlAutorizar: false);
            settings.EstablecerModoNivel2(m);
            db.ComprasSettings.Add(settings);
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
            new ArticulosFalsos(esServicio ? Servicio : Guid.Empty));

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
