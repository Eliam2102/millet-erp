using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Api.Web;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Idempotency;
using Millet.Api.Endpoints.Administracion;
using Millet.Administracion.Application.Empresas;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.UnitTests;

public sealed class EmpresaLogoTests
{
    [Fact]
    public async Task Endpoints_DeclaranLimiteYGetEnviaNosniff()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddMediatR(config => config.RegisterServicesFromAssemblyContaining<EmpresaLogoTests>());
        services.AddSingleton<MediatR.IRequestHandler<ObtenerLogoEmpresaQuery, LogoEmpresaArchivo?>, LogoConsulta>();
        await using var provider = services.BuildServiceProvider();
        var rutas = new Rutas(provider);
        rutas.MapEmpresasEndpoints();
        var endpoints = rutas.DataSources.SelectMany(x => x.Endpoints).ToList();
        var put = endpoints.Single(x => x.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "SubirLogoEmpresa");
        Assert.Equal(EmpresasEndpoints.MaxLogoRequestBytes, put.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
        var get = endpoints.Single(x => x.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "ObtenerLogoEmpresa");
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.RouteValues["id"] = Guid.NewGuid().ToString();
        context.Response.Body = new MemoryStream();
        await get.RequestDelegate!(context);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal("image/png", context.Response.ContentType);
    }

    private sealed class Rutas(IServiceProvider servicios) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider => servicios;
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(servicios);
    }

    public sealed class LogoConsulta : MediatR.IRequestHandler<ObtenerLogoEmpresaQuery, LogoEmpresaArchivo?>
    {
        public Task<LogoEmpresaArchivo?> Handle(ObtenerLogoEmpresaQuery request, CancellationToken cancellationToken)
            => Task.FromResult<LogoEmpresaArchivo?>(new(new MemoryStream([1]), "image/png"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Idempotencia_RespetaLimiteAntesDelHashYRespondeCodigoLogo(bool conLongitud)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(
            new RequestSizeLimitAttribute(EmpresasEndpoints.MaxLogoRequestBytes), new EndpointNameMetadata("SubirLogoEmpresa")), "Logo"));
        context.Request.Method = "PUT";
        context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();
        context.Request.Body = new MemoryStream(new byte[2 * LogoEmpresaFormato.MaxBytes]);
        if (conLongitud) context.Request.ContentLength = context.Request.Body.Length;
        var middleware = new IdempotencyMiddleware(_ => throw new InvalidOperationException("No debe ejecutar el endpoint"),
            Options.Create(new IdempotencyOptions()), NullLogger<IdempotencyMiddleware>.Instance);
        var usuario = new Contexto();
        var error = await Assert.ThrowsAsync<BadHttpRequestException>(() => middleware.InvokeAsync(context, null!, usuario, usuario, usuario));
        Assert.True(context.Request.Body.Position < context.Request.Body.Length);
        if (conLongitud) Assert.Equal(0, context.Request.Body.Position);
        context.Response.Body = new MemoryStream();
        await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance).TryHandleAsync(context, error, default);
        Assert.Equal(413, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var problem = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("EMPRESA_LOGO_TAMANO", problem.RootElement.GetProperty("code").GetString());
    }

    private sealed class Contexto : ICurrentUserContext, ICurrentEmpresaContext, IClock
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "Usuario ficticio";
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
        public DateTimeOffset UtcNow => new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
    }

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
        var contenido = await EmpresasEndpoints.LeerLogoAsync(Request("no es una imagen"u8.ToArray()), CancellationToken.None);
        var error = Assert.Throws<BusinessRuleException>(() => LogoEmpresaFormato.Validar(contenido));
        Assert.Equal("EMPRESA_LOGO_FORMATO", error.Code);
    }

    [Fact]
    public async Task Subida_RechazaMasDeUnMegabyte()
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => EmpresasEndpoints.LeerLogoAsync(Request(new byte[LogoEmpresaFormato.MaxBytes + 1]), CancellationToken.None));
        Assert.Equal("EMPRESA_LOGO_TAMANO", error.Code);
    }

    [Theory]
    [InlineData(0xc0)]
    [InlineData(0xc1)]
    [InlineData(0xc2)]
    [InlineData(0xc3)]
    [InlineData(0xc5)]
    [InlineData(0xc6)]
    [InlineData(0xc7)]
    [InlineData(0xc9)]
    [InlineData(0xca)]
    [InlineData(0xcb)]
    [InlineData(0xcd)]
    [InlineData(0xce)]
    [InlineData(0xcf)]
    public async Task Subida_DetectaJpegPorBytesAunqueMimeYExtensionSeanPng(int marcador)
    {
        byte[] bytes = [0xff, 0xd8, 0xff, (byte)marcador, 0, 11, 8, 0, 100, 0, 200, 1, 1, 0x11, 0];
        var contenido = await EmpresasEndpoints.LeerLogoAsync(Request(bytes), CancellationToken.None);
        Assert.Equal("image/jpeg", LogoEmpresaFormato.Validar(contenido));
    }

    [Theory]
    [InlineData(0xc4)]
    [InlineData(0xc8)]
    [InlineData(0xcc)]
    public void Jpeg_RechazaMarcadoresQueNoSonSof(int marcador)
    {
        byte[] bytes = [0xff, 0xd8, 0xff, (byte)marcador, 0, 11, 8, 0, 100, 0, 200, 1, 1, 0x11, 0];
        Assert.Equal("EMPRESA_LOGO_FORMATO", Assert.Throws<BusinessRuleException>(() => LogoEmpresaFormato.Validar(bytes)).Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MultipartGrande_SeRechazaSinLeerElCuerpoCompleto(bool conLongitud)
    {
        using var multipart = new MultipartFormDataContent("demo");
        multipart.Add(new ByteArrayContent(new byte[2 * LogoEmpresaFormato.MaxBytes]), "archivo", "demo.png");
        var bytes = await multipart.ReadAsByteArrayAsync();
        var request = new DefaultHttpContext().Request;
        request.ContentType = multipart.Headers.ContentType!.ToString();
        request.Body = new MemoryStream(bytes);
        if (conLongitud) request.ContentLength = bytes.Length;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => EmpresasEndpoints.LeerLogoAsync(request, default));
        Assert.Equal("EMPRESA_LOGO_TAMANO", error.Code);
        Assert.True(request.Body.Position < bytes.Length);
        if (conLongitud) Assert.Equal(0, request.Body.Position);
    }

    [Theory]
    [InlineData(new byte[] { 0xff, 0xd8, 0xff })]
    [InlineData(new byte[] { 0xff, 0xd8, 0xff, 0xc0, 0, 11, 8 })]
    public void Validador_RechazaCabeceraJpegTruncada(byte[] bytes)
    {
        var error = Assert.Throws<BusinessRuleException>(() => LogoEmpresaFormato.Validar(bytes));
        Assert.Equal("EMPRESA_LOGO_FORMATO", error.Code);
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
