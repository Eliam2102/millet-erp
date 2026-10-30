using Millet.DatosMaestros.Domain;

namespace Millet.SharedKernel.UnitTests.Domain;

public class EquivalenciaClientesSapTests
{
    private static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7(), Principal = Guid.CreateVersion7();

    private static readonly ClienteErpRef[] Erp =
    [
        new(A, "1001", "Cristales Demo SA de CV", "AAA010101AAA"),
        new(B, "1002", "Otra Persona SA de CV", "BBB020202BBB"),
        new(Principal, "1003", "Grupo Demo SA de CV", "CCC030303CCC"),
    ];

    [Fact]
    public void Clasifica_UnCasoDeCadaLista()
    {
        var r = EquivalenciaClientesSap.Clasificar(
        [
            new("1001", "otro nombre", "aaa010101aaa"),                // misma clave + RFC => ligado
            new("1002", "Nombre Distinto", "ZZZ990101ZZZ"),           // clave existe, nada coincide => revisión
            new("9999", "No Existe", "DDD040404DDD"),                  // clave inexistente => sin cliente
        ], Erp);

        Assert.Equal(A, Assert.Single(r.Ligados).ClienteId);
        Assert.Equal(B, Assert.Single(r.ARevision).ClienteId);
        Assert.Equal("9999", Assert.Single(r.SinCliente).CardCode);
    }

    [Fact]
    public void RfcGenerico_NoLiga_PeroNombreIgualSi()
    {
        var erp = new[] { new ClienteErpRef(A, "7", "Público en General, S.A.", "XAXX010101000") };
        var r = EquivalenciaClientesSap.Clasificar(
        [
            new("7", "Otro Nombre", "XAXX010101000"),
            new("7", "PUBLICO EN GENERAL SA", "XAXX010101000"),
        ], erp);
        Assert.Single(r.ARevision);
        Assert.Single(r.Ligados);
    }

    [Fact]
    public void SaldoUsd_SeQuedaEnSuClienteOriginal_NoEnElPrincipalDelGrupo()
    {
        // 1001 está unificado/agrupado en el principal 1003; el ligado debe seguir apuntando a 1001 con su moneda.
        var r = EquivalenciaClientesSap.Clasificar([new("1001", "Cristales Demo SA de CV", "AAA010101AAA", "USD", 250m)], Erp);

        var l = Assert.Single(r.Ligados);
        Assert.Equal(A, l.ClienteId);
        Assert.NotEqual(Principal, l.ClienteId);
        Assert.Equal(("USD", 250m), (l.Sap.Moneda, l.Sap.Saldo));
    }
}
