using System.Security.Claims;
using Millet.Api.Auth;

namespace Millet.Api.UnitTests;

public sealed class EntraGruposClaimsP6Tests
{
    [Fact]
    public void Grupos_del_token_se_deduplican()
    {
        var (grupos, overage) = EntraGruposClaims.Leer(new ClaimsIdentity([new Claim("groups", "a"), new Claim("groups", "a"), new Claim("groups", "b")]));
        Assert.Equal(2, grupos.Count);
        Assert.Contains("a", grupos);
        Assert.Contains("b", grupos);
        Assert.False(overage);
    }
    [Theory]
    [InlineData("hasgroups", "true")]
    [InlineData("_claim_names", "{\"groups\":\"src1\"}")]
    public void Overage_exige_consulta_de_pertenencia(string tipo, string valor)
        => Assert.True(EntraGruposClaims.Leer(new ClaimsIdentity([new Claim(tipo, valor)])).Overage);
    [Fact]
    public void Marca_malformada_rechaza_sesion()
        => Assert.Throws<UnauthorizedAccessException>(() => EntraGruposClaims.Leer(new ClaimsIdentity([new Claim("_claim_names", "{")])));
}
