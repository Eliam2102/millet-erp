using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure;
using Millet.SharedKernel.Infrastructure.Persistence.Interceptors;
using Millet.Administracion.Application.Empresas;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Blob;

namespace Millet.Compartido.UnitTests.Administracion;

public sealed class LogoEmpresaTests
{
    [Fact]
    public async Task Reemplazar_Y_Quitar_Conserva_Blobs_Historicos()
    {
        var usuario = new Usuario();
        var reloj = new Reloj();
        var contextoEmpresa = new FakeEmpresa();
        var auditoria = new AuditSaveChangesInterceptor(reloj, usuario, contextoEmpresa, new AuditOriginContext(), new AuditCorrelationContext());
        await using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(auditoria).Options, contextoEmpresa);
        var empresa = new Empresa(Guid.NewGuid(), "DEMO", "DEM010101AAA", "Empresa ficticia", "601", "Calle", "1", "Demo", "Demo", "Demo", "Demo", "MEX");
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        var blobs = new Blobs();
        var handler = new LogoEmpresaHandler(db, blobs, usuario, reloj);
        var bytes = Png();
        await handler.Handle(new SubirLogoEmpresaCommand(empresa.Id, bytes), CancellationToken.None);
        var primera = empresa.LogoBlobRef;
        await handler.Handle(new SubirLogoEmpresaCommand(empresa.Id, bytes), CancellationToken.None);
        Assert.NotEqual(primera, empresa.LogoBlobRef);
        Assert.StartsWith($"empresas/{empresa.Id}/logo/", empresa.LogoBlobRef);
        var archivo = await handler.Handle(new ObtenerLogoEmpresaQuery(empresa.Id), CancellationToken.None);
        Assert.NotNull(archivo);
        await archivo.Contenido.DisposeAsync();
        await handler.Handle(new QuitarLogoEmpresaCommand(empresa.Id), CancellationToken.None);
        Assert.Null(await handler.Handle(new ObtenerLogoEmpresaQuery(empresa.Id), CancellationToken.None));
        Assert.Equal(2, blobs.Claves.Count);
        Assert.Equal(0, blobs.Eliminados);
        var registros = await db.Set<AuditLogEntry>()
            .Where(x => x.EntidadId == empresa.Id && x.Operacion == "actualizar").ToListAsync();
        Assert.Equal(3, registros.Count);
        Assert.All(registros, registro =>
        {
            Assert.Equal(usuario.UserId, registro.UsuarioId);
            using var cambios = JsonDocument.Parse(registro.Cambios);
            Assert.True(cambios.RootElement.GetProperty("diff").TryGetProperty("LogoBlobRef", out _));
        });
    }

    [Fact]
    public void Validador_SoloExigeContenidoEId()
    {
        var validator = new SubirLogoEmpresaValidator();
        Assert.True(validator.Validate(new SubirLogoEmpresaCommand(Guid.NewGuid(), Png())).IsValid);
        Assert.False(validator.Validate(new SubirLogoEmpresaCommand(Guid.Empty, Png())).IsValid);
        Assert.True(validator.Validate(new SubirLogoEmpresaCommand(Guid.NewGuid(), [1, 2])).IsValid);
        Assert.Contains(validator.Validate(new SubirLogoEmpresaCommand(Guid.NewGuid(), null!)).Errors, x => x.ErrorCode == "EMPRESA_LOGO_FORMATO");
    }

    [Theory]
    [InlineData(false, "EMPRESA_LOGO_FORMATO")]
    [InlineData(true, "EMPRESA_LOGO_TAMANO")]
    public async Task Handler_RechazaLogoConCodigoDeNegocio(bool grande, string codigo)
    {
        await using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresa());
        var blobs = new Blobs();
        var handler = new LogoEmpresaHandler(db, blobs, new Usuario(), new Reloj());
        var bytes = grande ? new byte[LogoEmpresaFormato.MaxBytes + 1] : new byte[] { 1, 2 };
        var error = await Assert.ThrowsAsync<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() =>
            handler.Handle(new SubirLogoEmpresaCommand(Guid.NewGuid(), bytes), default));
        Assert.Equal(codigo, error.Code);
        Assert.Empty(blobs.Claves);
    }

    private static byte[] Png()
    {
        var bytes = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), 200);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), 100);
        return bytes;
    }
    private sealed class Usuario : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "Usuario ficticio";
    }
    private sealed class Reloj : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class Blobs : IBlobStoragePort
    {
        public List<string> Claves { get; } = [];
        public int Eliminados { get; private set; }
        public Task SubirAsync(string clave, Stream contenido, string contentType, CancellationToken cancellationToken)
        {
            Claves.Add(clave);
            return Task.CompletedTask;
        }
        public Task<Stream> ObtenerStreamAsync(string clave, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream(Png()));
        public Task EliminarAsync(string clave, CancellationToken cancellationToken) { Eliminados++; return Task.CompletedTask; }
    }
}
