using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;

/// <summary>
/// M-450 (86e3zr7ft): al distribuir el costo de un expediente de importación, si la cuenta del gasto de importación exige área o
/// equipo (regla C-03) y no se capturaron, la distribución no procede; con el dato, el centro viaja en el evento. Datos ficticios.
/// </summary>
public sealed class M450DistribucionAreaEquipoTests
{
    private static readonly Guid Actor = Guid.NewGuid(), Area = Guid.NewGuid(), Equipo = Guid.NewGuid();

    private static Expediente Importacion() => new(Guid.NewGuid(), Guid.NewGuid(), "IMP-M450", TipoExpediente.Importacion, Guid.NewGuid(),
        "Distribución FIX", Guid.NewGuid(), Guid.NewGuid(), "USD", Actor, []);

    private static DimensionesDistribucionServicio Servicio(IDimensionesCapturaPort puerto) => new(null!, puerto, new Reloj());

    [Fact]
    public async Task Sin_el_area_que_exige_la_cuenta_la_distribucion_no_procede()
    {
        var act = () => Servicio(new Puerto(faltante: "Falta el área: la cuenta 501.FIX lo exige (regla de dimensiones C-03). Captúrelo para continuar."))
            .CentroAsync(Importacion(), null, default);

        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        ex.Code.Should().Be("CE_DIMENSION_FALTANTE");
        ex.Message.Should().Contain("Falta el área");
    }

    [Fact]
    public async Task Con_el_equipo_capturado_el_centro_es_el_equipo()
    {
        var puerto = new Puerto();
        var centro = await Servicio(puerto).CentroAsync(Importacion(), new DistribuirDatos(Area, Equipo), default);

        centro.Should().Be(Equipo);
        puerto.Recibido!.Campo.Should().Be(DimensionesDistribucionServicio.Campo);
    }

    [Fact]
    public async Task Sin_exigencia_y_sin_dato_procede_sin_centro()
    {
        (await Servicio(new Puerto()).CentroAsync(Importacion(), null, default)).Should().BeNull();
    }

    [Fact]
    public async Task Un_dato_invalido_rechaza_la_distribucion()
    {
        var act = () => Servicio(new Puerto(error: "El área elegida no es de la sucursal del movimiento.")).CentroAsync(Importacion(),
            new DistribuirDatos(Area, null), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("CE_DIMENSION_INVALIDA");
    }

    private sealed class Reloj : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);
    }

    private sealed class Puerto(string? error = null, string? faltante = null) : IDimensionesCapturaPort
    {
        public MovimientoCaptura? Recibido { get; private set; }

        public Task<ExigenciaDimensiones> ExigenciasAsync(MovimientoCaptura movimiento, CancellationToken ct) =>
            Task.FromResult(ExigenciaDimensiones.Ninguna);

        public Task<IReadOnlyList<CentroOpcionDto>> CentrosAsync(Guid empresaId, Guid sucursalId, bool equipos, Guid? areaId, string? q, int limite,
            CancellationToken ct) => Task.FromResult<IReadOnlyList<CentroOpcionDto>>([]);

        public Task<DimensionesCapturaResultado> ValidarAsync(MovimientoCaptura movimiento, Guid? areaId, Guid? equipoId, CancellationToken ct)
        {
            Recibido = movimiento;
            return Task.FromResult(error is not null
                ? new DimensionesCapturaResultado(error, null, null, ExigenciaDimensiones.Ninguna)
                : new DimensionesCapturaResultado(null, faltante, equipoId ?? areaId, ExigenciaDimensiones.Ninguna));
        }
    }
}
