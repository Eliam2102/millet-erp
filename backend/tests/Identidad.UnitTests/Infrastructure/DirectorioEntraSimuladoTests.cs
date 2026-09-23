using Microsoft.Extensions.Options;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure.Stubs;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.UnitTests.Infrastructure;

/// <summary>
/// Directorio Entra simulado (plan 15, F2): reglas que debe reproducir el
/// adaptador de Graph (UPN único, dominio permitido, idempotencia).
/// </summary>
public class DirectorioEntraSimuladoTests
{
    private static DirectorioEntraSimulado Crear(params CuentaSimuladaOptions[] semillas)
        => new(new OptionsMonitorFijo(new EntraDirectorioOptions
        {
            DominiosPermitidos = ["millet.mx"],
            Simulacion = new EntraSimulacionOptions { CuentasExistentes = [.. semillas] },
        }));

    private static SolicitudCuentaEntra Solicitud(string upn, string clave = "emp-1")
        => new(upn, "Persona Nueva", "personal@gmail.com", clave);

    [Fact]
    public async Task Buscar_Cuenta_Semilla_Ignora_Mayusculas_Y_Da_Oid_Estable_No_Pendiente()
    {
        var directorio = Crear(new CuentaSimuladaOptions { Correo = "ana.lopez@millet.mx", Nombre = "Ana López" });

        var cuenta = await directorio.BuscarPorCorreoAsync("  ANA.LOPEZ@millet.mx ", default);

        cuenta.Should().NotBeNull();
        cuenta!.NombreMostrado.Should().Be("Ana López");
        cuenta.Habilitada.Should().BeTrue();
        cuenta.ObjectId.Should().Be(DirectorioEntraSimulado.OidEstable("ana.lopez@millet.mx"));
        Guid.TryParse(cuenta.ObjectId, out _).Should().BeTrue();
        Usuario.EsOidPendiente(cuenta.ObjectId).Should().BeFalse();
    }

    [Fact]
    public async Task Buscar_Correo_Inexistente_Retorna_Null()
    {
        var directorio = Crear();

        (await directorio.BuscarPorCorreoAsync("nadie@millet.mx", default)).Should().BeNull();
    }

    [Fact]
    public async Task Crear_Cuenta_Queda_Buscable_Y_Devuelve_Contrasena_Temporal()
    {
        var directorio = Crear();

        var creada = await directorio.CrearCuentaAsync(Solicitud("nuevo@millet.mx"), default);

        creada.Cuenta.Upn.Should().Be("nuevo@millet.mx");
        creada.Cuenta.Habilitada.Should().BeTrue();
        Usuario.EsOidPendiente(creada.Cuenta.ObjectId).Should().BeFalse();
        creada.ContrasenaTemporal.Should().HaveLength(16);
        creada.ToString().Should().NotContain(creada.ContrasenaTemporal);

        var buscada = await directorio.BuscarPorCorreoAsync("nuevo@millet.mx", default);
        buscada.Should().Be(creada.Cuenta);
    }

    [Fact]
    public async Task Crear_Con_Upn_Existente_Lanza_Conflicto()
    {
        var directorio = Crear(new CuentaSimuladaOptions { Correo = "ana.lopez@millet.mx" });

        var act = () => directorio.CrearCuentaAsync(Solicitud("ana.lopez@millet.mx"), default);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("ENTRA_UPN_EN_USO");
    }

    [Fact]
    public async Task Crear_Con_Dominio_No_Permitido_Lanza_Regla_De_Negocio()
    {
        var directorio = Crear();

        var act = () => directorio.CrearCuentaAsync(Solicitud("alguien@gmail.com"), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ENTRA_DOMINIO_NO_PERMITIDO");
    }

    [Fact]
    public async Task Reintento_Con_Misma_Clave_Devuelve_Misma_Cuenta_Con_Contrasena_Nueva()
    {
        var directorio = Crear();

        var primera = await directorio.CrearCuentaAsync(Solicitud("nuevo@millet.mx", "emp-42"), default);
        var reintento = await directorio.CrearCuentaAsync(Solicitud("nuevo@millet.mx", "emp-42"), default);

        reintento.Cuenta.Should().Be(primera.Cuenta);
        reintento.ContrasenaTemporal.Should().NotBe(primera.ContrasenaTemporal);
    }

    [Fact]
    public async Task Otra_Clave_Con_Mismo_Upn_Lanza_Conflicto()
    {
        var directorio = Crear();
        await directorio.CrearCuentaAsync(Solicitud("nuevo@millet.mx", "emp-1"), default);

        var act = () => directorio.CrearCuentaAsync(Solicitud("nuevo@millet.mx", "emp-2"), default);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public void Contrasena_Temporal_Cumple_Complejidad()
    {
        for (var i = 0; i < 50; i++)
        {
            var contrasena = DirectorioEntraSimulado.GenerarContrasenaTemporal();

            contrasena.Should().HaveLength(16);
            contrasena.Should().Match(c => c.Any(char.IsUpper) && c.Any(char.IsLower)
                && c.Any(char.IsDigit) && c.Any(ch => !char.IsLetterOrDigit(ch)));
        }
    }

    [Theory]
    [InlineData("persona@millet.mx", true)]
    [InlineData("persona@MILLET.MX", true)]
    [InlineData("persona@sub.millet.mx", false)]
    [InlineData("persona@gmail.com", false)]
    [InlineData("sin-arroba", false)]
    [InlineData("persona@", false)]
    public void Politica_De_Dominios(string correo, bool esperado)
    {
        var options = new EntraDirectorioOptions { DominiosPermitidos = ["millet.mx"] };

        options.PermiteCorreo(correo).Should().Be(esperado);
    }

    [Fact]
    public void Sin_Dominios_Configurados_No_Permite_Ninguno()
    {
        new EntraDirectorioOptions().PermiteCorreo("persona@millet.mx").Should().BeFalse();
    }

    private sealed class OptionsMonitorFijo(EntraDirectorioOptions valor) : IOptionsMonitor<EntraDirectorioOptions>
    {
        public EntraDirectorioOptions CurrentValue => valor;
        public EntraDirectorioOptions Get(string? name) => valor;
        public IDisposable? OnChange(Action<EntraDirectorioOptions, string?> listener) => null;
    }
}
