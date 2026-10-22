using Microsoft.AspNetCore.Http;
using Millet.Api.Endpoints.Administracion;
using Millet.Administracion.Application.Empresas;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.UnitTests;

public sealed class EmpresaLogoTests
{
    private static HttpRequest Request(byte[] bytes)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=demo";
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
            new FormFileCollection { new FormFile(new MemoryStream(bytes), 0, bytes.Length, "archivo", "demo.png") { Headers = new HeaderDictionary(), ContentType = "image/png" } });
        return context.Request;
    }

    [Fact]
    public async Task Subida_RechazaFormatoAunqueExtensionYMimeSeanPng()
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => EmpresasEndpoints.LeerLogoAsync(Request("no es una imagen"u8.ToArray()), CancellationToken.None));
        Assert.Equal("EMPRESA_LOGO_FORMATO", error.Code);
    }

    [Fact]
    public async Task Subida_RechazaMasDeUnMegabyte()
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => EmpresasEndpoints.LeerLogoAsync(Request(new byte[LogoEmpresaFormato.MaxBytes + 1]), CancellationToken.None));
        Assert.Equal("EMPRESA_LOGO_TAMANO", error.Code);
    }

    [Theory]
    [InlineData(199, false)]
    [InlineData(200, true)]
    public void Validador_ExigeAnchoMinimo(int ancho, bool valido)
    {
        var bytes = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), ancho);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), 100);
        if (valido) Assert.Equal("image/png", LogoEmpresaFormato.Validar(bytes));
        else Assert.Equal("EMPRESA_LOGO_TAMANO", Assert.Throws<BusinessRuleException>(() => LogoEmpresaFormato.Validar(bytes)).Code);
    }
}
