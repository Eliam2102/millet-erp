using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.UnitTests.Domain;

/// <summary>
/// Ciclo de vida del acceso de <see cref="Usuario"/> respecto de Entra ID
/// (alta unificada de colaboradores, F1-ADM-01 plan 15): OID pendiente,
/// vinculación, provisión vía Graph, primer acceso y cuenta técnica.
/// </summary>
public sealed class UsuarioEstadoAccesoTests
{
    private const string OidReal = "3f2b9c1e-6a7d-4e58-9b0a-1c2d3e4f5a6b";

    private static Usuario Crear(
        string entraOid = OidReal,
        EstadoAcceso estado = EstadoAcceso.Activo,
        bool esCuentaTecnica = false) =>
        new(Guid.CreateVersion7(), entraOid, "ana.lopez@millet.mx", "Ana López", estado, esCuentaTecnica);

    [Fact]
    public void Should_Default_Activo_NoTecnica_ParaCompatibilidadConAutoProvision()
    {
        var usuario = new Usuario(Guid.CreateVersion7(), OidReal, "a@millet.mx", "A");

        usuario.EstadoAcceso.Should().Be(EstadoAcceso.Activo);
        usuario.EsCuentaTecnica.Should().BeFalse();
        usuario.PrimerAccesoEn.Should().BeNull();
    }

    [Theory]
    [InlineData("pending:ana.lopez@millet.mx", true)]
    [InlineData("dev-ana.lopez@millet.mx", true)]
    [InlineData("dev-superadmin", false)]
    [InlineData(OidReal, false)]
    public void EsOidPendiente_Distingue_Placeholders_De_Oids_Sinteticos(string oid, bool esperado)
    {
        Usuario.EsOidPendiente(oid).Should().Be(esperado);
    }

    [Fact]
    public void Should_Rechazar_Provision_Con_Oid_Definitivo()
    {
        var act = () => Crear(OidReal, EstadoAcceso.ProvisionandoCuenta);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("USUARIO_OID_PROVISION_INVALIDO");
    }

    [Fact]
    public void Should_Rechazar_Nacer_En_ErrorProvision()
    {
        var act = () => Crear("pending:x@millet.mx", EstadoAcceso.ErrorProvision);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("USUARIO_ESTADO_INICIAL_INVALIDO");
    }

    [Fact]
    public void VincularEntraOid_Desde_Provision_Pasa_A_PendientePrimerAcceso()
    {
        var usuario = Crear("pending:ana.lopez@millet.mx", EstadoAcceso.ProvisionandoCuenta);

        usuario.VincularEntraOid(OidReal);

        usuario.EntraOid.Should().Be(OidReal);
        usuario.TieneOidPendiente.Should().BeFalse();
        usuario.EstadoAcceso.Should().Be(EstadoAcceso.PendientePrimerAcceso);
    }

    [Fact]
    public void VincularEntraOid_Desde_Error_Limpia_Motivo()
    {
        var usuario = Crear("pending:ana.lopez@millet.mx", EstadoAcceso.ProvisionandoCuenta);
        usuario.MarcarErrorProvision("Licencia no disponible");

        usuario.VincularEntraOid(OidReal);

        usuario.EstadoAcceso.Should().Be(EstadoAcceso.PendientePrimerAcceso);
        usuario.MotivoErrorProvision.Should().BeNull();
    }

    [Fact]
    public void VincularEntraOid_Mismo_Oid_Es_Idempotente()
    {
        var usuario = Crear(OidReal, EstadoAcceso.PendientePrimerAcceso);

        usuario.VincularEntraOid(OidReal);

        usuario.EntraOid.Should().Be(OidReal);
        usuario.EstadoAcceso.Should().Be(EstadoAcceso.PendientePrimerAcceso);
    }

    [Fact]
    public void VincularEntraOid_Rechaza_Si_Ya_Tiene_Otro_Oid_Definitivo()
    {
        var usuario = Crear(OidReal);

        var act = () => usuario.VincularEntraOid("otro-oid-definitivo");

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("USUARIO_OID_YA_VINCULADO");
        usuario.EntraOid.Should().Be(OidReal);
    }

    [Theory]
    [InlineData("pending:otro@millet.mx")]
    [InlineData("dev-otro@millet.mx")]
    [InlineData(" ")]
    public void VincularEntraOid_Rechaza_Oid_No_Definitivo(string oid)
    {
        var usuario = Crear("pending:ana.lopez@millet.mx", EstadoAcceso.ProvisionandoCuenta);

        var act = () => usuario.VincularEntraOid(oid);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("USUARIO_OID_INVALIDO");
    }

    [Fact]
    public void VincularEntraOid_Usuario_Historico_DevEmail_Ya_Activo_Conserva_Activo()
    {
        var usuario = Crear("dev-ana.lopez@millet.mx", EstadoAcceso.Activo);

        usuario.VincularEntraOid(OidReal);

        usuario.EstadoAcceso.Should().Be(EstadoAcceso.Activo);
    }

    [Fact]
    public void MarcarErrorProvision_Guarda_Motivo_Truncado()
    {
        var usuario = Crear("pending:ana.lopez@millet.mx", EstadoAcceso.ProvisionandoCuenta);

        usuario.MarcarErrorProvision(new string('x', Usuario.MotivoErrorProvisionMaxLength + 50));

        usuario.EstadoAcceso.Should().Be(EstadoAcceso.ErrorProvision);
        usuario.MotivoErrorProvision.Should().HaveLength(Usuario.MotivoErrorProvisionMaxLength);
    }

    [Fact]
    public void MarcarErrorProvision_Fuera_De_Provision_Falla()
    {
        var usuario = Crear(OidReal, EstadoAcceso.PendientePrimerAcceso);

        var act = () => usuario.MarcarErrorProvision("x");

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("USUARIO_NO_EN_PROVISION");
    }

    [Fact]
    public void ReintentarProvision_Vuelve_A_Provisionando()
    {
        var usuario = Crear("pending:ana.lopez@millet.mx", EstadoAcceso.ProvisionandoCuenta);
        usuario.MarcarErrorProvision("UPN tomado");

        usuario.ReintentarProvision();

        usuario.EstadoAcceso.Should().Be(EstadoAcceso.ProvisionandoCuenta);
        usuario.MotivoErrorProvision.Should().BeNull();
    }

    [Fact]
    public void ReintentarProvision_Sin_Error_Falla()
    {
        var usuario = Crear("pending:ana.lopez@millet.mx", EstadoAcceso.ProvisionandoCuenta);

        var act = () => usuario.ReintentarProvision();

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("USUARIO_NO_EN_ERROR_PROVISION");
    }

    [Fact]
    public void RegistrarAcceso_Primera_Vez_Activa_Y_Fija_Fecha_Solo_Una_Vez()
    {
        var usuario = Crear(OidReal, EstadoAcceso.PendientePrimerAcceso);
        var primero = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

        usuario.RegistrarAcceso(primero);
        usuario.RegistrarAcceso(primero.AddDays(1));

        usuario.EstadoAcceso.Should().Be(EstadoAcceso.Activo);
        usuario.PrimerAccesoEn.Should().Be(primero);
    }

    [Fact]
    public void RegistrarAcceso_Con_Oid_Pendiente_Falla()
    {
        var usuario = Crear("pending:ana.lopez@millet.mx", EstadoAcceso.ProvisionandoCuenta);

        var act = () => usuario.RegistrarAcceso(DateTimeOffset.UtcNow);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("USUARIO_OID_PENDIENTE");
    }

    [Fact]
    public void CuentaTecnica_Se_Marca_Y_Desmarca()
    {
        var usuario = Crear(esCuentaTecnica: true);
        usuario.EsCuentaTecnica.Should().BeTrue();

        usuario.DesmarcarCuentaTecnica();
        usuario.EsCuentaTecnica.Should().BeFalse();

        usuario.MarcarComoCuentaTecnica();
        usuario.EsCuentaTecnica.Should().BeTrue();
    }
}
