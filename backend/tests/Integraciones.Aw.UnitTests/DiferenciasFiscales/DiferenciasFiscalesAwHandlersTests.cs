using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.DiferenciasFiscales;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.DiferenciasFiscales;

/// <summary>
/// U1.8b: criterios de aceptación a través de los handlers (reporte en vivo y "Marcar revisado") con
/// CompartidoDbContext InMemory y un origen A+W falso. Corridas persistidas: ver Api.IntegrationTests.
/// </summary>
public sealed class DiferenciasFiscalesAwHandlersTests : IAsyncDisposable
{
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class Usuario : ICurrentUserContext
    {
        public Guid? UserId { get; init; } = Guid.Parse("0199e000-0000-7000-8000-0000000000aa");
        public string? UserName => "revisor.demo";
    }

    private sealed class OrigenFalso : IAwClientesOrigen
    {
        public Dictionary<int, AwClienteOrigenFila> Filas { get; } = [];
        public Exception? Error { get; set; }
        public int LecturasPorReferencia { get; private set; }
        public int Paginas { get; private set; }

        public Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct)
        {
            if (Error is not null) throw Error;
            LecturasPorReferencia++;
            return Task.FromResult(int.TryParse(referencia, out var id) ? Filas.GetValueOrDefault(id) : null);
        }

        public Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
        {
            if (Error is not null) throw Error;
            Paginas++;
            var desde = cursor is null ? 0 : int.Parse(cursor);
            var pag = Filas.Values.Where(f => f.Id > desde).OrderBy(f => f.Id).Take(tamano).ToList();
            return Task.FromResult(new AwClientesPagina(pag, pag.Count == tamano ? pag[^1].Id.ToString() : null));
        }
    }

    private readonly CompartidoDbContext _db = new(new DbContextOptionsBuilder<CompartidoDbContext>()
        .UseInMemoryDatabase("u18b-" + Guid.NewGuid()).Options, new Empresa());
    private readonly OrigenFalso _origen = new();
    private readonly AwClientesOptions _clientes = new() { LecturaHabilitada = true };
    private readonly DiferenciasFiscalesAwOptions _options = new();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private DiferenciasFiscalesAwService Servicio() => new(_db, _origen, Options.Create(_clientes),
        Options.Create(new AwProductosOptions()), Options.Create(new AwPedidosOptions()), Options.Create(_options),
        NullLogger<DiferenciasFiscalesAwService>.Instance);

    private Task<ReporteResponse<FilaDiferenciaFiscalAw>> ReporteAsync(string? tipo = null, bool incluirRevisadas = false) =>
        new ObtenerDiferenciasFiscalesAwHandler(Servicio(), TimeProvider.System)
            .Handle(new(tipo, null, null, incluirRevisadas), default);

    private Task<RevisionDiferenciaFiscalAwDto> MarcarAsync(FilaDiferenciaFiscalAw f, ICurrentUserContext? user = null) =>
        new MarcarDiferenciaFiscalAwRevisadaHandler(Servicio(), _db, user ?? new Usuario(), TimeProvider.System)
            .Handle(new(f.Tipo, f.EntidadId, f.Campo, f.Huella, "Validado con Fiscal"), default);

    private async Task<Cliente> ClienteAsync(int referencia, string regimen)
    {
        var c = new Cliente(Guid.CreateVersion7(), $"AW-{referencia}", "CLIENTE DEMO", OrigenMaster.Aw,
            referenciaExterna: referencia.ToString(), rfc: "DEMO010101AB1", regimenFiscal: regimen, codigoPostalFiscal: "97000");
        _db.Clientes.Add(c);
        await _db.SaveChangesAsync();
        _origen.Filas[referencia] = ComparadorDiferenciasFiscalesTests.Aw(referencia, regimen: "601");
        return c;
    }

    [Fact]
    public async Task CA_a_cliente_con_601_en_A_W_y_612_en_ERP_aparece_con_ambos_valores()
    {
        await ClienteAsync(1001, "612");

        var r = await ReporteAsync();

        var fila = r.Filas.Should().ContainSingle().Subject;
        (fila.TipoEtiqueta, fila.Referencia, fila.CampoEtiqueta, fila.ValorOrigen, fila.ValorErp, fila.EstadoEtiqueta, fila.Fuente)
            .Should().Be(("Cliente", "1001", "Régimen fiscal", "601", "612", "Diferente", "A+W"));
        r.Resumen!.ClientesComparados.Should().Be(1);
        r.Columnas.Select(c => c.Clave).Should().Contain(["valorOrigen", "valorErp", "fuente"]);
    }

    [Fact]
    public async Task CA_b_revisado_desaparece_hasta_que_cambia_un_valor()
    {
        var cliente = await ClienteAsync(1001, "612");
        var fila = (await ReporteAsync()).Filas.Single();

        var dto = await MarcarAsync(fila);

        dto.Huella.Should().Be(fila.Huella);
        (await ReporteAsync()).Filas.Should().BeEmpty();
        var conRevisadas = await ReporteAsync(incluirRevisadas: true);
        conRevisadas.Filas.Should().ContainSingle().Which.Should().Match<FilaDiferenciaFiscalAw>(f => f.Revisada && f.RevisadaPor == "revisor.demo");
        conRevisadas.Resumen!.Revisadas.Should().Be(1);

        // Cambia el valor en A+W: la diferencia reaparece.
        _origen.Filas[1001] = ComparadorDiferenciasFiscalesTests.Aw(1001, regimen: "603");
        (await ReporteAsync()).Filas.Should().ContainSingle().Which.ValorOrigen.Should().Be("603");

        // Se vuelve a revisar; luego cambia el valor en el ERP: reaparece otra vez.
        await MarcarAsync((await ReporteAsync()).Filas.Single());
        (await ReporteAsync()).Filas.Should().BeEmpty();
        cliente.ActualizarDatos(regimenFiscal: "626");
        await _db.SaveChangesAsync();
        (await ReporteAsync()).Filas.Should().ContainSingle().Which.ValorErp.Should().Be("626");
        (await _db.DiferenciasFiscalesAwRevisiones.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Marcar_con_huella_vieja_o_sin_diferencia_da_conflicto_y_sin_usuario_prohibido()
    {
        var cliente = await ClienteAsync(1001, "612");
        var fila = (await ReporteAsync()).Filas.Single();
        _origen.Filas[1001] = ComparadorDiferenciasFiscalesTests.Aw(1001, regimen: "612");

        await FluentActions.Awaiting(() => MarcarAsync(fila)).Should().ThrowAsync<ConflictException>();
        await FluentActions.Awaiting(() => MarcarAsync(fila, new Usuario { UserId = null })).Should().ThrowAsync<ForbiddenException>();
        (await _db.DiferenciasFiscalesAwRevisiones.AnyAsync(r => r.EntidadId == cliente.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task CA_c_articulo_sin_clave_SAT_aparece_como_Faltante()
    {
        var p = new ProductoAw(Guid.CreateVersion7(), "13", "VIDRIO TEMPLADO DEMO", "M2", claveUnidadSat: "MTK", objetoImp: "02");
        p.AplicarClasificacion(null, "Templado", "VTE");
        _db.ProductosAw.Add(p);
        _db.Add(new ReglaFiscalAw(Guid.CreateVersion7(), "Tipo", "VTE", "Demo", new("30171706", "MTK", "02", 0.16m)));
        await _db.SaveChangesAsync();

        var r = await ReporteAsync("Producto");

        var fila = r.Filas.Should().ContainSingle().Subject;
        (fila.TipoEtiqueta, fila.CampoEtiqueta, fila.EstadoEtiqueta, fila.ValorOrigen, fila.ValorErp, fila.Fuente)
            .Should().Be(("Producto A+W", "Clave de producto SAT", "Faltante", "30171706", null, "Regla Tipo = VTE"));
        _origen.LecturasPorReferencia.Should().Be(0, "los productos no leen A+W: A+W no tiene datos SAT");
    }

    [Fact]
    public async Task Sin_lectura_A_W_o_con_A_W_caido_avisa_y_sigue_con_productos()
    {
        await ClienteAsync(1001, "612");
        _clientes.LecturaHabilitada = false;
        var r = await ReporteAsync();
        r.Filas.Should().BeEmpty();
        r.Avisos.Should().ContainSingle().Which.Should().Contain("apagada").And.NotContain("LecturaHabilitada");

        _clientes.LecturaHabilitada = true;
        _origen.Error = new AwReaderException("host secreto", "timeout", isTransient: true);
        r = await ReporteAsync();
        r.Avisos.Should().ContainSingle().Which.Should().Contain("timeout").And.NotContain("host secreto");
    }

    [Fact]
    public async Task Con_muchos_clientes_recorre_A_W_por_paginas_y_avisa_los_que_no_encuentra()
    {
        _options.MaxLecturasPorReferencia = 1;
        await ClienteAsync(1001, "612");
        await ClienteAsync(1002, "601");
        _origen.Filas.Remove(1002);

        var r = await ReporteAsync("Cliente");

        _origen.LecturasPorReferencia.Should().Be(0);
        _origen.Paginas.Should().BeGreaterThan(0);
        r.Filas.Should().ContainSingle().Which.Referencia.Should().Be("1001");
        r.Avisos.Should().ContainSingle().Which.Should().StartWith("1 cliente(s)");
    }
}
