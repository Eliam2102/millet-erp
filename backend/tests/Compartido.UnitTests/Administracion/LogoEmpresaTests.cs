using Microsoft.EntityFrameworkCore;
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
        await using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresa());
        var empresa = new Empresa(Guid.NewGuid(), "DEMO", "DEM010101AAA", "Empresa ficticia", "601", "Calle", "1", "Demo", "Demo", "Demo", "Demo", "MEX");
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        var blobs = new Blobs();
        var handler = new LogoEmpresaHandler(db, blobs, new Usuario(), new Reloj());
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
    }

    [Fact]
    public void Comando_Valida_Formato_Tamano_E_Id()
    {
        var validator = new SubirLogoEmpresaValidator();
        Assert.True(validator.Validate(new(Guid.NewGuid(), Png())).IsValid);
        Assert.False(validator.Validate(new(Guid.Empty, Png())).IsValid);
        Assert.Contains(validator.Validate(new(Guid.NewGuid(), [1, 2])).Errors, x => x.ErrorCode == "EMPRESA_LOGO_FORMATO");
        Assert.Contains(validator.Validate(new(Guid.NewGuid(), new byte[LogoEmpresaFormato.MaxBytes + 1])).Errors, x => x.ErrorCode == "EMPRESA_LOGO_TAMANO");
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
