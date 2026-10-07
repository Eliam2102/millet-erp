using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Adjuntos;

namespace Millet.Compartido.UnitTests.Adjuntos;

public sealed class AdjuntoEnlaceTokenServiceTests
{
    private static AdjuntoEnlaceTokenService Crear(IDataProtectionProvider? p = null, int ttl = 60)
        => new(p ?? new EphemeralDataProtectionProvider(), Options.Create(new AdjuntoEnlaceOptions { TtlSegundos = ttl }));

    [Fact]
    public void Emite_y_valida_atado_a_adjunto_y_usuario()
    {
        var s = Crear();
        var (adjunto, usuario) = (Guid.NewGuid(), Guid.NewGuid());

        var e = s.Emitir(adjunto, usuario);

        s.Validar(e.Token).Should().Be(new Millet.SharedKernel.Application.Adjuntos.AdjuntoEnlaceClaims(adjunto, usuario));
        e.ExpiraEn.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(60), TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("basura")]
    public void Token_invalido_devuelve_null(string token) => Crear().Validar(token).Should().BeNull();

    [Fact]
    public void Token_manipulado_o_de_otra_clave_devuelve_null()
    {
        var token = Crear().Emitir(Guid.NewGuid(), Guid.NewGuid()).Token;

        Crear().Validar(token).Should().BeNull("otra clave de protección");
        var s = Crear(new EphemeralDataProtectionProvider());
        var propio = s.Emitir(Guid.NewGuid(), Guid.NewGuid()).Token;
        s.Validar(propio[..^2] + "AA").Should().BeNull();
    }

    [Fact]
    public async Task Token_expira_tras_el_ttl()
    {
        var s = Crear(ttl: 1);
        var token = s.Emitir(Guid.NewGuid(), Guid.NewGuid()).Token;

        await Task.Delay(TimeSpan.FromSeconds(2.2));

        s.Validar(token).Should().BeNull();
    }
}
