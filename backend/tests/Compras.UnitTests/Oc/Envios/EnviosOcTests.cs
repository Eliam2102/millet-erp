using Microsoft.EntityFrameworkCore;
using MediatR;
using Millet.Compras.Application.Oc.RegistrarImpresion;
using Microsoft.Extensions.Logging;
using Millet.Compras.Application.Oc.Envios;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Application.Oc.Pdf;
using Millet.Compras.Domain.Ports.Pdf;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Correo;

namespace Millet.Compras.UnitTests.Oc.Envios;

// Todos los destinatarios y documentos de estas pruebas son DEMO ficticios.
public sealed class EnviosOcTests
{
    [Fact]
    public async Task G18a_Autorizada_EnviaPdfExistente_YRegistraSinCambiarEstados()
    {
        using var f = new Fixture();
        var estados = (f.Oc.Estado, f.Oc.SubEstadoRecepcion, f.Oc.SubEstadoFacturacion, f.Oc.SubEstadoPago);
        var envio = await f.Enviar();
        Assert.Equal("simulado", envio.Resultado);
        Assert.Equal("correo", envio.Modo);
        Assert.Equal("proveedor-demo@example.test", envio.Destinatario);
        Assert.Equal(f.Usuario.UserId, envio.UsuarioId);
        Assert.Equal(f.Reloj.UtcNow, envio.Fecha);
        Assert.Equal(f.Pdf.Id, envio.PdfId);
        Assert.Equal(new byte[] { 37, 80, 68, 70 }, f.Correo.Ultimo!.Pdf);
        Assert.Equal("ORIGINAL", f.Impresion.MarcaUsada);
        Assert.Equal(estados, (f.Oc.Estado, f.Oc.SubEstadoRecepcion, f.Oc.SubEstadoFacturacion, f.Oc.SubEstadoPago));
        Assert.Single(await f.Db.EnviosOc.ToListAsync());
        Assert.Equal(new RegistrarImpresionOcCommand(f.Oc.Id, f.Pdf.Id), Assert.Single(f.Sender.Impresiones));
    }

    [Fact]
    public async Task G18b_SinCorreo_PideCorreo_YCapturadoNoModificaProveedor()
    {
        using var f = new Fixture();
        f.Proveedores.Email = null;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => f.Enviar());
        Assert.Contains("correo", error.Message);
        Assert.Empty(f.Db.EnviosOc);
        var envio = await f.Enviar("capturado-demo@example.test");
        Assert.Equal("capturado-demo@example.test", envio.Destinatario);
        Assert.Null(f.Proveedores.Email);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task G18c_SinDosFirmas_NoEnviaNiRegistra(int firmas)
    {
        using var f = new Fixture(firmas);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => f.Enviar());
        Assert.Contains("dos firmas distintas", error.Message);
        Assert.Null(f.Correo.Ultimo);
        Assert.Empty(f.Db.EnviosOc);
    }

    [Fact]
    public async Task G18d_Reenvio_CreaSegundoRegistro_NoDuplicaOc()
    {
        using var f = new Fixture();
        var primero = await f.Enviar();
        var segundo = await f.Enviar();
        Assert.NotEqual(primero.Id, segundo.Id);
        Assert.Equal(2, await f.Db.EnviosOc.CountAsync());
        Assert.Equal(2, f.Sender.Impresiones.Count);
        Assert.Equal(1, await f.Db.OrdenesCompra.CountAsync());
    }

    [Fact]
    public async Task G18e_Manual_RegistraUsuarioFechaModo_SinInvocarCorreo()
    {
        using var f = new Fixture();
        var envio = await f.Enviar(modo: "manual");
        Assert.Equal("manual", envio.Modo);
        Assert.Equal("registrado", envio.Resultado);
        Assert.Empty(f.Sender.Impresiones);
        Assert.Equal(f.Usuario.UserId, envio.UsuarioId);
        Assert.Equal(f.Reloj.UtcNow, envio.Fecha);
        Assert.Null(f.Correo.Ultimo);
    }

    [Theory]
    [InlineData("invalido", "correo")]
    [InlineData("a@example.test\r\nBcc:otro@example.test", "correo")]
    [InlineData("a@example.test", "otro")]
    public async Task EntradaInvalida_NoProduceEnvio(string email, string modo)
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<BusinessRuleException>(() => f.Enviar(email, modo));
        Assert.Empty(f.Db.EnviosOc);
        Assert.Null(f.Correo.Ultimo);
    }

    [Fact]
    public async Task FalloCorreo_QuedaRegistrado_SinExponerExcepcion()
    {
        using var f = new Fixture();
        f.Correo.Falla = true;
        var envio = await f.Enviar();
        Assert.Equal("fallido", envio.Resultado);
        Assert.Empty(f.Sender.Impresiones);
        Assert.Single(f.Db.EnviosOc);
        var aviso = Assert.Single(f.Logger.Mensajes);
        Assert.Contains(f.Oc.Id.ToString(), aviso);
        Assert.DoesNotContain("example.test", aviso);
    }

    [Theory]
    [InlineData(false, "aceptado")]
    [InlineData(true, "simulado")]
    public async Task FalloImpresion_ConservaEnvioGuardado_YRegistraAdvertencia(bool simulado, string resultado)
    {
        using var f = new Fixture();
        f.Correo.EsSimulado = simulado;
        f.Sender.Falla = true;

        var envio = await f.Enviar();

        Assert.Equal(resultado, envio.Resultado);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(resultado, (await f.Db.EnviosOc.SingleAsync()).Resultado);
        Assert.NotNull(f.Correo.Ultimo);
        Assert.Single(f.Sender.Impresiones);
        var advertencia = Assert.Single(f.Logger.Entradas);
        Assert.Equal(LogLevel.Warning, advertencia.Nivel);
        Assert.IsType<InvalidOperationException>(advertencia.Error);
        Assert.Contains(envio.Id.ToString(), advertencia.Mensaje);
    }

    [Fact]
    public async Task SinPdf_NoEnviaNiRegistra()
    {
        using var f = new Fixture();
        f.Db.OrdenCompraPdfs.Remove(f.Pdf);
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => f.Enviar());
        Assert.Empty(f.Db.EnviosOc);
    }

    [Fact]
    public async Task AutorizadaConUnaFirma_NoEnvia()
    {
        using var f = new Fixture(1, requiereNivel2: false);
        Assert.Equal(EstadoOrdenCompra.Autorizada, f.Oc.Estado);
        await Assert.ThrowsAsync<BusinessRuleException>(() => f.Enviar());
        Assert.Null(f.Correo.Ultimo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FirmasMismoUsuario_ODeCicloAnterior_NoEnvia(bool mismoUsuario)
    {
        using var f = new Fixture();
        var firmas = f.Oc.Autorizaciones.ToArray();
        // Simula datos históricos inconsistentes: el envío debe verificar las firmas, no sólo el estado.
        if (mismoUsuario)
            typeof(AutorizacionOC).GetProperty(nameof(AutorizacionOC.UsuarioId))!.SetValue(firmas[1], firmas[0].UsuarioId);
        else
            typeof(AutorizacionOC).GetProperty(nameof(AutorizacionOC.Ciclo))!.SetValue(firmas[1], 0);
        await Assert.ThrowsAsync<BusinessRuleException>(() => f.Enviar());
        Assert.Null(f.Correo.Ultimo);
    }

    [Fact]
    public async Task Historial_DevuelveCorreoYRegistros_ConResultadoExplicito()
    {
        using var f = new Fixture();
        await f.Enviar();
        await f.Enviar(modo: "manual");
        var resultado = await new ObtenerEnviosOcHandler(f.Db, f.Proveedores, f.Correo)
            .Handle(new ObtenerEnviosOcQuery(f.Oc.Id), default);
        Assert.True(resultado.EsSimulado);
        Assert.True(resultado.PuedeEnviar);
        Assert.Equal(f.Proveedores.Email, resultado.CorreoProveedor);
        Assert.Equal(2, resultado.Envios.Count);
        Assert.All(resultado.Envios, e => Assert.Equal(f.Usuario.UserName, e.UsuarioNombre));
    }

    [Fact]
    public async Task Historial_NoExponeOcDeOtraEmpresa()
    {
        using var f = new Fixture();
        await f.Enviar();
        f.Oc.EmpresaId = Guid.NewGuid();
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<EntityNotFoundException>(() => new ObtenerEnviosOcHandler(f.Db, f.Proveedores, f.Correo)
            .Handle(new ObtenerEnviosOcQuery(f.Oc.Id), default));
    }

    private sealed class Fixture : IDisposable
    {
        public ComprasDbContext Db { get; }
        public OrdenCompra Oc { get; }
        public OrdenCompraPdf Pdf { get; }
        public Usuario Usuario { get; } = new();
        public Reloj Reloj { get; } = new();
        public Proveedores Proveedores { get; } = new();
        public Correo Correo { get; } = new();
        public Impresion Impresion { get; } = new();
        public LoggerCaptura Logger { get; } = new();
        public Sender Sender { get; } = new();
        public Fixture(int firmas = 2, bool requiereNivel2 = true)
        {
            var empresa = new Empresa();
            Db = new(new DbContextOptionsBuilder<ComprasDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
            Oc = new OrdenCompra(Guid.NewGuid(), empresa.Current!.Value,
                Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000001"), 2026,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                new DateOnly(2026, 10, 22), sinRequisicionPrevia: true, motivoSinRequisicion: "DEMO ficticio");
            Oc.ActualizarDatosFactura("G03", "PPD", "99");
            Oc.AgregarLineaManual(Guid.NewGuid(), Guid.NewGuid(), 1, "PZA", 100, Guid.NewGuid());
            if (firmas > 0)
            {
                Oc.EnviarAAutorizacion(Reloj.UtcNow, requiereNivel2);
                Oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), Reloj.UtcNow);
            }
            if (firmas == 2) Oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, Guid.NewGuid(), Reloj.UtcNow);
            Pdf = new(Guid.NewGuid(), Oc.Id, "demo.pdf", "application/pdf", 4, Reloj.UtcNow, "DEMO");
            Db.OrdenesCompra.Add(Oc);
            Db.OrdenCompraPdfs.Add(Pdf);
            Db.SaveChanges();
        }
        public Task<EnvioOc> Enviar(string? email = null, string modo = "correo") =>
            new RegistrarEnvioOcHandler(Db, Usuario, Reloj, Proveedores, Impresion, Correo, Sender, Logger)
                .Handle(new RegistrarEnvioOcCommand(Oc.Id, email, modo), default);
        public void Dispose() => Db.Dispose();
    }
    private sealed class Sender : ISender
    {
        public bool Falla { get; set; }
        public List<RegistrarImpresionOcCommand> Impresiones { get; } = [];
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Impresiones.Add(Assert.IsType<RegistrarImpresionOcCommand>(request));
            if (Falla) throw new InvalidOperationException("Fallo de impresión DEMO");
            return Task.FromResult((TResponse)(object)Impresiones.Count);
        }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
    private sealed class Usuario : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "Usuario DEMO ficticio";
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-10-22T18:00:00Z"); }
    private sealed class Proveedores : IProveedorReadPort
    {
        public string? Email { get; set; } = "proveedor-demo@example.test";
        public Task<IReadOnlyDictionary<Guid, ProveedorLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorLectura>>(ids.ToDictionary(id => id, id => new ProveedorLectura(id, "DEMO", "Proveedor ficticio", Email)));
    }
    private sealed class Correo : ICorreoDocumentoService
    {
        public bool EsSimulado { get; set; } = true;
        public bool Falla { get; set; }
        public CorreoDocumento? Ultimo { get; private set; }
        public Task EnviarAsync(CorreoDocumento correo, CancellationToken ct)
        {
            Ultimo = correo;
            if (Falla) throw new InvalidOperationException("Detalle privado del transporte");
            return Task.CompletedTask;
        }
    }
    private sealed class LoggerCaptura : ILogger<RegistrarEnvioOcHandler>
    {
        public List<string> Mensajes { get; } = [];
        public List<(LogLevel Nivel, Exception? Error, string Mensaje)> Entradas { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entradas.Add((logLevel, exception, formatter(state, exception)));
            if (logLevel == LogLevel.Warning) Mensajes.Add(formatter(state, exception));
        }
    }
    private sealed class Impresion : ImpresionOrdenCompraService
    {
        public Impresion() : base(null!, null!, null!, null!, null!, null!, null!, null!) { }
        public string? MarcaUsada { get; private set; }
        public override Task<PdfOrdenCompraGenerado> RenderizarAsync(Guid ordenCompraId, string? marca, CancellationToken ct)
        {
            MarcaUsada = marca;
            return Task.FromResult(new PdfOrdenCompraGenerado([37, 80, 68, 70], "application/pdf", "OC-MID2026-000001.pdf"));
        }
    }
}
