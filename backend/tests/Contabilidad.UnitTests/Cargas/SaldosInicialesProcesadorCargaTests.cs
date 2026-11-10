using MediatR;
using Millet.Contabilidad.Application.SaldosIniciales;
using Millet.Contabilidad.Infrastructure.Cargas;
using Millet.SharedKernel.Application.Cargas;

namespace Millet.Contabilidad.UnitTests.Cargas;

/// <summary>M-047/M-167 ↔ M-166: el adaptador del lote común traduce filas, errores y resultados.</summary>
public sealed class SaldosInicialesProcesadorCargaTests
{
    private sealed class FakeSender : ISender
    {
        public object? Ultimo { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            Ultimo = request;
            object r = request switch
            {
                ValidarBalanzaAperturaQuery => new ValidacionBalanzaApertura(false, 1000m, 700m, 300m,
                    [new ErrorBalanzaApertura(3, "cuenta", "AP_CUENTA_NO_EXISTE", "Cuenta no existe en el catálogo")], 2, 0, null),
                AplicarSaldosInicialesCommand => new AperturaAplicadaResponse(Guid.Parse("11111111-1111-1111-1111-111111111111"), "SI-000001", false),
                _ => throw new NotImplementedException(),
            };
            return Task.FromResult((TResponse)r);
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static readonly Guid Subio = Guid.NewGuid();
    private static readonly Guid Autorizo = Guid.NewGuid();

    private static LoteCargaContexto Contexto(string? sucursal = "101") => new(Guid.NewGuid(), Guid.NewGuid(), new string('A', 64),
        new DateOnly(2026, 10, 31), Subio, Autorizo, "Contadora", "Contralor",
        new Dictionary<string, string?> { ["sucursalPorOmision"] = sucursal });

    private static FilaCarga Fila(int n, string cuenta, string cargo, string abono, string tipoAux = "", string clave = "") =>
        new(n, new Dictionary<string, string>
        {
            ["cuenta"] = cuenta, ["cargo"] = cargo, ["abono"] = abono, ["tipoAuxiliar"] = tipoAux,
            ["claveAuxiliar"] = clave, ["sucursal"] = "", ["concepto"] = "",
        });

    [Fact]
    public void Declara_el_tipo_columnas_permisos_y_fecha_de_corte()
    {
        var p = new SaldosInicialesProcesadorCarga(new FakeSender());
        p.Tipo.Should().Be("contabilidad.saldos-iniciales");
        p.ColumnasObligatorias.Should().BeEquivalentTo("cuenta", "cargo", "abono");
        p.Columnas.Select(c => c.Nombre).Should().Contain(["tipoAuxiliar", "claveAuxiliar", "sucursal", "concepto"]);
        p.RequiereFechaCorte.Should().BeTrue();
        p.PermisoSubir.Should().Be("contabilidad.catalogo.importar");
        p.PermisoAutorizar.Should().Be("contabilidad.polizas.autorizar");
    }

    [Fact]
    public async Task Validar_convierte_las_filas_y_mapea_errores_y_resumen()
    {
        var sender = new FakeSender();
        var p = new SaldosInicialesProcesadorCarga(sender);

        var v = await p.ValidarAsync(Contexto(), [Fila(2, "105.01.00.00", "1,000.00", ""), Fila(3, "105.09.00.00", "", "700")], default);

        var q = sender.Ultimo.Should().BeOfType<ValidarBalanzaAperturaQuery>().Subject;
        q.FechaCorte.Should().Be(new DateOnly(2026, 10, 31));
        q.SucursalPorOmision.Should().Be("101");
        q.Filas.Should().HaveCount(2);
        q.Filas[0].Should().Be(new FilaBalanzaApertura(2, "105.01.00.00", 1000m, 0m));
        q.Filas[1].Abono.Should().Be(700m);
        v.PuedeAplicar.Should().BeFalse();
        v.Errores.Should().ContainSingle(e => e.Fila == 3 && e.Codigo == "AP_CUENTA_NO_EXISTE");
        v.Resumen["Diferencia"].Should().Be("300.00");
    }

    [Fact]
    public async Task Importe_que_no_es_numero_da_error_en_su_renglon_sin_llamar_a_contabilidad()
    {
        var sender = new FakeSender();
        var v = await new SaldosInicialesProcesadorCarga(sender).ValidarAsync(Contexto(), [Fila(4, "105.01.00.00", "abc", "")], default);
        v.PuedeAplicar.Should().BeFalse();
        v.Errores.Should().ContainSingle(e => e.Fila == 4 && e.Campo == "cargo" && e.Codigo == "AP_IMPORTE_INVALIDO");
        sender.Ultimo.Should().BeNull();
    }

    [Fact]
    public async Task Aplicar_manda_quien_subio_y_quien_autorizo_y_regresa_la_poliza()
    {
        var sender = new FakeSender();
        var lote = Contexto(sucursal: null);
        var r = await new SaldosInicialesProcesadorCarga(sender).AplicarAsync(lote, [Fila(2, "105.01.00.00", "1000", "")], default);

        var c = sender.Ultimo.Should().BeOfType<AplicarSaldosInicialesCommand>().Subject;
        c.LoteId.Should().Be(lote.LoteId);
        c.SubidoPorId.Should().Be(Subio); c.SubidoPor.Should().Be("Contadora");
        c.AutorizadoPorId.Should().Be(Autorizo); c.AutorizadoPor.Should().Be("Contralor");
        c.SucursalPorOmision.Should().BeNull();
        r.Referencia.Should().Be("11111111-1111-1111-1111-111111111111");
        r.Folio.Should().Be("SI-000001");
        r.YaAplicada.Should().BeFalse();
    }
}
