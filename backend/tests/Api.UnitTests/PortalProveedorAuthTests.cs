using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Millet.Api.Auth;
using Millet.Api.Auth.Options;
using Millet.SharedKernel.Application;

namespace Millet.Api.UnitTests;

/// <summary>Criterio de seguridad P8.3: el token del portal no lo acepta el esquema del ERP y viceversa.</summary>
public sealed class PortalProveedorAuthTests
{
    private static readonly JwtIssuanceOptions Jwt = new()
    {
        SigningKey = new string('k', 48), Issuer = "millet-erp-api", Audience = "millet-erp-api",
    };

    private sealed class RelojFijo(DateTimeOffset ahora) : IClock { public DateTimeOffset UtcNow => ahora; }

    private static TokenValidationParameters Parametros(string audiencia) => new()
    {
        ValidateIssuer = true, ValidIssuer = Jwt.Issuer,
        ValidateAudience = true, ValidAudience = audiencia,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.SigningKey)),
        ValidateLifetime = true,
    };

    private static (string Token, Guid Prov, Guid Emp) Emitir(DateTimeOffset ahora)
    {
        var prov = Guid.NewGuid();
        var emp = Guid.NewGuid();
        var t = new PortalProveedorTokenService(Options.Create(Jwt), new RelojFijo(ahora)).Emitir(prov, emp, "v@prov.mx");
        return (t.Token, prov, emp);
    }

    [Fact]
    public async Task Token_del_portal_lo_acepta_el_esquema_del_portal_y_lo_rechaza_el_del_ERP()
    {
        var (token, prov, emp) = Emitir(DateTimeOffset.UtcNow);
        var handler = new JsonWebTokenHandler();

        var portal = await handler.ValidateTokenAsync(token, Parametros(PortalProveedorAuth.Audience));
        var erp = await handler.ValidateTokenAsync(token, Parametros(Jwt.Audience));

        Assert.True(portal.IsValid);
        Assert.Equal(prov.ToString(), portal.Claims[PortalProveedorAuth.ProveedorIdClaim]);
        Assert.Equal(emp.ToString(), portal.Claims[PortalProveedorAuth.EmpresaIdClaim]);
        Assert.False(erp.IsValid, "el esquema del ERP exige su propia audiencia");
    }

    [Fact]
    public async Task Token_del_ERP_no_lo_acepta_el_esquema_del_portal()
    {
        var erpToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Jwt.Issuer, Audience = Jwt.Audience, Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.SigningKey)), SecurityAlgorithms.HmacSha256),
        });

        var r = await new JsonWebTokenHandler().ValidateTokenAsync(erpToken, Parametros(PortalProveedorAuth.Audience));

        Assert.False(r.IsValid);
    }

    [Fact]
    public void Token_dura_8_horas_y_no_trae_permisos_del_ERP()
    {
        var ahora = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(Emitir(ahora).Token);

        Assert.Equal(ahora.AddHours(8).UtcDateTime, jwt.ValidTo);
        Assert.Equal("millet-portal-proveedores", Assert.Single(jwt.Audiences));
        Assert.DoesNotContain(jwt.Claims.Select(c => c.Type), t => t.Contains("perm") || t == "role" || t == "roles");
    }
}
