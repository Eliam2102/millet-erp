using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Identidad.Application.Colaboradores;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Identidad.Infrastructure.Stubs;
using Millet.SharedKernel.Application;

namespace Millet.Identidad.UnitTests.Infrastructure;

/// <summary>
/// Grupo de acceso del ERP en Entra (86e3x6cpw, B2): la cuenta entra al
/// grupo con acceso activo, sale al desactivarlo, y un fallo de Graph queda
/// «Pendiente» sin romper la operación.
/// </summary>
public sealed class GrupoAccesoErpSincronizadorTests : IDisposable
{
    private const string Grupo = "grupo-erp";
    private const string Oid = "22222222-2222-4222-8222-222222222222";

    [Fact]
    public async Task Sin_Grupo_Configurado_No_Toca_El_Directorio()
    {
        var (sincronizador, directorio, usuario) = await CrearAsync(grupoId: null);

        (await sincronizador.SincronizarAsync(usuario.Id, default)).Should().Be(EstadoGrupoAcceso.NoConfigurado);
        (await sincronizador.ConsultarAsync(usuario.Id, default)).Should().Be(EstadoGrupoAcceso.NoConfigurado);
        (await directorio.EsMiembroDeGrupoAccesoAsync(Grupo, Oid, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Activo_Entra_Al_Grupo_E_Inactivo_Sale()
    {
        var (sincronizador, directorio, usuario) = await CrearAsync(Grupo);

        (await sincronizador.SincronizarAsync(usuario.Id, default)).Should().Be(EstadoGrupoAcceso.Sincronizado);
        (await directorio.EsMiembroDeGrupoAccesoAsync(Grupo, Oid, default)).Should().BeTrue();
        (await sincronizador.ConsultarAsync(usuario.Id, default)).Should().Be(EstadoGrupoAcceso.Sincronizado);

        usuario.Desactivar();
        await GuardarAsync(usuario);
        (await sincronizador.ConsultarAsync(usuario.Id, default)).Should().Be(EstadoGrupoAcceso.Pendiente);
        (await sincronizador.SincronizarAsync(usuario.Id, default)).Should().Be(EstadoGrupoAcceso.Sincronizado);
        (await directorio.EsMiembroDeGrupoAccesoAsync(Grupo, Oid, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Fallo_De_Graph_Queda_Pendiente_Sin_Lanzar()
    {
        var (sincronizador, _, usuario) = await CrearAsync(Grupo, new DirectorioQueFalla());

        (await sincronizador.SincronizarAsync(usuario.Id, default)).Should().Be(EstadoGrupoAcceso.Pendiente);
        (await sincronizador.ConsultarAsync(usuario.Id, default)).Should().Be(EstadoGrupoAcceso.Pendiente);
    }

    [Fact]
    public async Task Cuenta_Nueva_Sin_Oid_Queda_Pendiente_Hasta_Que_El_Worker_La_Cree()
    {
        var (sincronizador, _, usuario) = await CrearAsync(Grupo, oid: "pending:" + Guid.NewGuid());

        (await sincronizador.SincronizarAsync(usuario.Id, default)).Should().Be(EstadoGrupoAcceso.Pendiente);
    }

    private IdentidadDbContext? _db;

    public void Dispose() => _db?.Dispose();

    private async Task<(GrupoAccesoErpSincronizador, IEntraDirectorioPort, Usuario)> CrearAsync(
        string? grupoId, IEntraDirectorioPort? directorio = null, string oid = Oid)
    {
        var options = new OptionsMonitorFijo(new EntraDirectorioOptions
        {
            DominiosPermitidos = ["millet.mx"],
            GrupoAccesoErpId = grupoId,
        });
        directorio ??= new DirectorioEntraSimulado(options);
        _db = new IdentidadDbContext(new DbContextOptionsBuilder<IdentidadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        var usuario = new Usuario(Guid.CreateVersion7(), oid, "persona@millet.mx", "Persona");
        _db.Usuarios.Add(usuario);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (new GrupoAccesoErpSincronizador(_db, directorio, options,
            NullLogger<GrupoAccesoErpSincronizador>.Instance), directorio, usuario);
    }

    private async Task GuardarAsync(Usuario usuario)
    {
        _db!.Usuarios.Update(usuario);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private sealed class DirectorioQueFalla : IEntraDirectorioPort
    {
        private static HttpRequestException Falla() => new HttpRequestException("Graph no respondió.");
        public Task<CuentaEntra?> BuscarPorCorreoAsync(string correo, CancellationToken ct) => throw Falla();
        public Task<CuentaEntraCreada> CrearCuentaAsync(SolicitudCuentaEntra solicitud, CancellationToken ct) => throw Falla();
        public Task<string> RestablecerContrasenaTemporalAsync(string objectId, CancellationToken ct) => throw Falla();
        public Task<IReadOnlyList<CuentaEntra>> ListarCuentasAsync(string? texto, int top, CancellationToken ct) => throw Falla();
        public Task AgregarAGrupoAccesoAsync(string grupoId, string objectId, CancellationToken ct) => throw Falla();
        public Task QuitarDeGrupoAccesoAsync(string grupoId, string objectId, CancellationToken ct) => throw Falla();
        public Task<bool> EsMiembroDeGrupoAccesoAsync(string grupoId, string objectId, CancellationToken ct) => throw Falla();
    }

    private sealed class OptionsMonitorFijo(EntraDirectorioOptions valor) : IOptionsMonitor<EntraDirectorioOptions>
    {
        public EntraDirectorioOptions CurrentValue => valor;
        public EntraDirectorioOptions Get(string? name) => valor;
        public IDisposable? OnChange(Action<EntraDirectorioOptions, string?> listener) => null;
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
