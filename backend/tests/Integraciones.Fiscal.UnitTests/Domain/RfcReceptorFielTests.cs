using Millet.Integraciones.Fiscal.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.UnitTests.Domain;

/// <summary>
/// Tests del comportamiento de FIEL agregado en PR-10:
/// <see cref="RfcReceptor.AsignarPersonExterno"/>,
/// <see cref="RfcReceptor.AsignarFiel"/> y
/// <see cref="RfcReceptor.TieneFielVigente"/>.
/// </summary>
public sealed class RfcReceptorFielTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Ahora = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AsignarPersonExterno_setea_el_id_externo()
    {
        var rfc = NewRfc();
        rfc.AsignarPersonExterno("ce5008a1-4158-4c98-b8bd-21c1b89d39bc");
        rfc.PersonIdExterno.Should().Be("ce5008a1-4158-4c98-b8bd-21c1b89d39bc");
    }

    [Fact]
    public void AsignarPersonExterno_rechaza_id_vacio()
    {
        var rfc = NewRfc();
        var act = () => rfc.AsignarPersonExterno("");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "RFC_RECEPTOR_PERSON_EXTERNO_INVALIDO");
    }

    [Fact]
    public void AsignarFiel_sin_person_falla()
    {
        var rfc = NewRfc();
        var act = () => rfc.AsignarFiel("cer-id", "key-id",
            validFrom: Ahora.AddYears(-1),
            validTo:   Ahora.AddYears(3),
            ahora:     Ahora);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "RFC_RECEPTOR_FIEL_SIN_PERSON");
    }

    [Fact]
    public void AsignarFiel_con_vigencia_invertida_falla()
    {
        var rfc = NewRfc();
        rfc.AsignarPersonExterno("p1");
        var act = () => rfc.AsignarFiel("cer-id", "key-id",
            validFrom: Ahora.AddYears(2),
            validTo:   Ahora.AddYears(1),
            ahora:     Ahora);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "RFC_RECEPTOR_FIEL_VIGENCIA_INVALIDA");
    }

    [Fact]
    public void AsignarFiel_rechaza_cert_ya_vencido()
    {
        var rfc = NewRfc();
        rfc.AsignarPersonExterno("p1");
        var act = () => rfc.AsignarFiel("cer-id", "key-id",
            validFrom: Ahora.AddYears(-5),
            validTo:   Ahora.AddDays(-1),
            ahora:     Ahora);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "RFC_RECEPTOR_FIEL_EXPIRADA");
    }

    [Fact]
    public void AsignarFiel_correcta_setea_todos_los_campos()
    {
        var rfc = NewRfc();
        rfc.AsignarPersonExterno("p1");
        var from = Ahora.AddYears(-1);
        var to   = Ahora.AddYears(3);

        rfc.AsignarFiel("cer-id-x", "key-id-y", from, to, Ahora);

        rfc.CerFileIdExterno.Should().Be("cer-id-x");
        rfc.KeyFileIdExterno.Should().Be("key-id-y");
        rfc.FielValidFrom.Should().Be(from);
        rfc.FielValidTo.Should().Be(to);
        rfc.FielSubidaAt.Should().Be(Ahora);
    }

    [Fact]
    public void TieneFielVigente_false_si_no_hay_fiel()
    {
        var rfc = NewRfc();
        rfc.TieneFielVigente(Ahora).Should().BeFalse();
    }

    [Fact]
    public void TieneFielVigente_true_dentro_del_rango()
    {
        var rfc = NewRfc();
        rfc.AsignarPersonExterno("p1");
        rfc.AsignarFiel("c", "k", Ahora.AddYears(-1), Ahora.AddYears(3), Ahora);

        rfc.TieneFielVigente(Ahora).Should().BeTrue();
        rfc.TieneFielVigente(Ahora.AddYears(2)).Should().BeTrue();
    }

    [Fact]
    public void TieneFielVigente_false_despues_de_validTo()
    {
        var rfc = NewRfc();
        rfc.AsignarPersonExterno("p1");
        rfc.AsignarFiel("c", "k", Ahora.AddYears(-1), Ahora.AddDays(30), Ahora);

        rfc.TieneFielVigente(Ahora.AddDays(31)).Should().BeFalse();
    }

    [Fact]
    public void TieneFielVigente_false_antes_de_validFrom()
    {
        var rfc = NewRfc();
        rfc.AsignarPersonExterno("p1");
        rfc.AsignarFiel("c", "k", Ahora.AddDays(30), Ahora.AddYears(4), Ahora);

        rfc.TieneFielVigente(Ahora).Should().BeFalse();
    }

    [Fact]
    public void Reasignar_fiel_sobreescribe_los_ids_y_vigencia_previos()
    {
        // Renovación: el admin sube FIEL nueva antes de que venza la actual.
        var rfc = NewRfc();
        rfc.AsignarPersonExterno("p1");
        rfc.AsignarFiel("cer-vieja", "key-vieja", Ahora.AddYears(-3), Ahora.AddYears(1), Ahora);

        var ahoraMasTarde = Ahora.AddYears(1).AddMonths(-1);
        rfc.AsignarFiel("cer-nueva", "key-nueva", ahoraMasTarde, ahoraMasTarde.AddYears(4), ahoraMasTarde);

        rfc.CerFileIdExterno.Should().Be("cer-nueva");
        rfc.KeyFileIdExterno.Should().Be("key-nueva");
        rfc.FielSubidaAt.Should().Be(ahoraMasTarde);
    }

    private static RfcReceptor NewRfc() =>
        new(Guid.NewGuid(), EmpresaId, "MIL010101AAA");
}
