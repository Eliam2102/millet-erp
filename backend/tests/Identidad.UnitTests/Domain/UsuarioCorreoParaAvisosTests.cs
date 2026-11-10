using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Identidad.Application.Usuarios;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Identidad.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Xunit;

namespace Millet.Identidad.UnitTests.Domain;

/// <summary>
/// Pruebas para campo opcional correo_para_avisos y su resolución con fallback (86e3zexqm).
/// </summary>
public sealed class UsuarioCorreoParaAvisosTests
{
    private const string OidReal = "3f2b9c1e-6a7d-4e58-9b0a-1c2d3e4f5a6b";

    [Fact]
    public void Usuario_CorreoDestinoAvisos_evalua_prioritariamente_CorreoParaAvisos()
    {
        var usuario = new Usuario(
            Guid.CreateVersion7(),
            OidReal,
            "colaborador@millet.com.mx",
            "Juan Pérez",
            correoParaAvisos: "avisos.operaciones@millet.com.mx");

        usuario.CorreoParaAvisos.Should().Be("avisos.operaciones@millet.com.mx");
        usuario.CorreoDestinoAvisos.Should().Be("avisos.operaciones@millet.com.mx");
    }

    [Fact]
    public void Usuario_CorreoDestinoAvisos_hace_fallback_al_correo_institucional_si_es_nulo_o_vacio()
    {
        var usuarioNulo = new Usuario(
            Guid.CreateVersion7(),
            OidReal,
            "colaborador@millet.com.mx",
            "Juan Pérez",
            correoParaAvisos: null);

        var usuarioVacio = new Usuario(
            Guid.CreateVersion7(),
            OidReal,
            "colaborador2@millet.com.mx",
            "María Gómez",
            correoParaAvisos: "   ");

        usuarioNulo.CorreoDestinoAvisos.Should().Be("colaborador@millet.com.mx");
        usuarioVacio.CorreoDestinoAvisos.Should().Be("colaborador2@millet.com.mx");
    }

    [Fact]
    public void Usuario_ActualizarCorreoParaAvisos_y_ActualizarPerfil_permiten_editar_y_limpiar()
    {
        var usuario = new Usuario(
            Guid.CreateVersion7(),
            OidReal,
            "cuenta@millet.com.mx",
            "Carlos");

        usuario.ActualizarCorreoParaAvisos("departamental@millet.com.mx");
        usuario.CorreoDestinoAvisos.Should().Be("departamental@millet.com.mx");

        // Limpiar mediante ActualizarPerfil
        usuario.ActualizarPerfil(null, null, null, false, limpiarCorreoParaAvisos: true);
        usuario.CorreoParaAvisos.Should().BeNull();
        usuario.CorreoDestinoAvisos.Should().Be("cuenta@millet.com.mx");

        // Asignar mediante ActualizarPerfil
        usuario.ActualizarPerfil(null, null, null, false, correoParaAvisos: "nuevo.buzon@millet.com.mx");
        usuario.CorreoDestinoAvisos.Should().Be("nuevo.buzon@millet.com.mx");
    }

    [Fact]
    public void Empleado_CorreoDestinoAvisos_prioriza_CorreoParaAvisos_y_hace_fallback_a_Email()
    {
        var empConAvisos = new Empleado(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "EMP-001",
            "Pedro",
            email: "pedro@millet.com.mx",
            emailContacto: "pedro.personal@gmail.com",
            correoParaAvisos: "mantenimiento@millet.com.mx");

        empConAvisos.CorreoDestinoAvisos.Should().Be("mantenimiento@millet.com.mx");

        var empFallbackEmail = new Empleado(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "EMP-002",
            "Laura",
            email: "laura@millet.com.mx",
            emailContacto: "laura.personal@gmail.com",
            correoParaAvisos: null);

        empFallbackEmail.CorreoDestinoAvisos.Should().Be("laura@millet.com.mx");

        var empFallbackContacto = new Empleado(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "EMP-003",
            "Sin Entra",
            email: null,
            emailContacto: "contacto@externo.com",
            correoParaAvisos: null);

        empFallbackContacto.CorreoDestinoAvisos.Should().Be("contacto@externo.com");
    }

    [Theory]
    [InlineData("avisos@millet.com.mx", "principal@millet.com.mx", "avisos@millet.com.mx")]
    [InlineData("  avisos@millet.com.mx  ", "principal@millet.com.mx", "avisos@millet.com.mx")]
    [InlineData(null, "principal@millet.com.mx", "principal@millet.com.mx")]
    [InlineData("", "principal@millet.com.mx", "principal@millet.com.mx")]
    [InlineData("   ", "  principal@millet.com.mx  ", "principal@millet.com.mx")]
    [InlineData(null, null, null)]
    [InlineData("", "   ", null)]
    public void UsuarioAvisosResolver_resuelve_correctamente(string? avisos, string? principal, string? esperado)
    {
        var resultado = UsuarioAvisosResolver.ResolverCorreoDestino(avisos, principal);
        resultado.Should().Be(esperado);
    }

    [Fact]
    public async Task UsuarioCorreoAvisosAdapter_resuelve_por_id_y_en_lote()
    {
        var options = new DbContextOptionsBuilder<IdentidadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var empresaContext = new FakeEmpresaContext();
        await using var db = new IdentidadDbContext(options, empresaContext);

        var id1 = Guid.CreateVersion7();
        var id2 = Guid.CreateVersion7();

        var u1 = new Usuario(id1, OidReal, "u1@millet.com.mx", "Usuario 1", correoParaAvisos: "u1.avisos@millet.com.mx");
        var u2 = new Usuario(id2, Guid.NewGuid().ToString(), "u2@millet.com.mx", "Usuario 2", correoParaAvisos: null);

        db.Usuarios.AddRange(u1, u2);
        await db.SaveChangesAsync();

        var adapter = new UsuarioCorreoAvisosAdapter(db, empresaContext);

        var correo1 = await adapter.ObtenerCorreoAvisosAsync(id1, CancellationToken.None);
        var correo2 = await adapter.ObtenerCorreoAvisosAsync(id2, CancellationToken.None);

        correo1.Should().Be("u1.avisos@millet.com.mx");
        correo2.Should().Be("u2@millet.com.mx");

        var mapa = await adapter.ObtenerCorreosAvisosAsync([id1, id2], CancellationToken.None);
        mapa.Should().HaveCount(2);
        mapa[id1].Should().Be("u1.avisos@millet.com.mx");
        mapa[id2].Should().Be("u2@millet.com.mx");
    }

    [Fact]
    public async Task ActualizarUsuarioHandler_actualiza_y_limpia_CorreoParaAvisos()
    {
        var options = new DbContextOptionsBuilder<IdentidadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var empresaContext = new FakeEmpresaContext();
        await using var db = new IdentidadDbContext(options, empresaContext);

        var id = Guid.CreateVersion7();
        var usuario = new Usuario(id, OidReal, "usr@millet.com.mx", "Usuario Prueba");
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        var handler = new ActualizarUsuarioHandler(db);

        // Asignar correo para avisos
        var cmd1 = new ActualizarUsuarioCommand(id, null, null, null, false, CorreoParaAvisos: "avisos@millet.com.mx");
        var resp1 = await handler.Handle(cmd1, CancellationToken.None);

        resp1.CorreoParaAvisos.Should().Be("avisos@millet.com.mx");
        var enDb1 = await db.Usuarios.FindAsync(id);
        enDb1!.CorreoParaAvisos.Should().Be("avisos@millet.com.mx");

        // Limpiar correo para avisos
        var cmd2 = new ActualizarUsuarioCommand(id, null, null, null, false, LimpiarCorreoParaAvisos: true);
        var resp2 = await handler.Handle(cmd2, CancellationToken.None);

        resp2.CorreoParaAvisos.Should().BeNull();
        var enDb2 = await db.Usuarios.FindAsync(id);
        enDb2!.CorreoParaAvisos.Should().BeNull();
    }

    [Fact]
    public void ActualizarUsuarioValidator_rechaza_formato_invalido_en_CorreoParaAvisos()
    {
        var validator = new ActualizarUsuarioValidator();
        var cmdInvalido = new ActualizarUsuarioCommand(
            Guid.NewGuid(), null, null, null, false, CorreoParaAvisos: "no-es-correo");

        var resultado = validator.Validate(cmdInvalido);
        resultado.IsValid.Should().BeFalse();
        resultado.Errors.Should().Contain(e => e.PropertyName == "CorreoParaAvisos");
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new DummyDisposable();
        private sealed class DummyDisposable : IDisposable { public void Dispose() { } }
    }
}
