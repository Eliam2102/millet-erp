using Millet.Compras.Application.Umbrales;
using Millet.Compras.Domain;

namespace Millet.Compras.UnitTests.Umbrales;

public class UpsertUmbralValidatorTests
{
    private readonly UpsertUmbralValidator _validator = new();

    [Fact]
    public void Comando_Valido_Pasa()
    {
        var cmd = new UpsertUmbralCommand(Guid.CreateVersion7(), 1_000m, 10_000m);

        Assert.True(_validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void N1_Igual_A_N2_Falla()
    {
        var cmd = new UpsertUmbralCommand(Guid.CreateVersion7(), 10_000m, 10_000m);

        var result = _validator.Validate(cmd);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Departamento_Empty_Es_Umbral_General_Y_Pasa()
    {
        var cmd = new UpsertUmbralCommand(Guid.Empty, 1_000m, 10_000m);

        Assert.True(_validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void Moneda_Distinta_De_MXN_Falla()
    {
        var cmd = new UpsertUmbralCommand(Guid.CreateVersion7(), null, 10_000m, "USD");

        Assert.False(_validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void Minimo_Negativo_Falla()
    {
        var cmd = new UpsertUmbralCommand(Guid.CreateVersion7(), null, 10_000m, MontoMinimoDisponibilidad: -1m);

        Assert.False(_validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void Modo_Fuera_Del_Enum_Falla()
    {
        var cmd = new UpsertUmbralCommand(Guid.CreateVersion7(), null, 10_000m, ModoNivel2: (ModoNivel2)99);

        Assert.False(_validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void Modo_Omitido_Es_Valido()
    {
        var cmd = new UpsertUmbralCommand(Guid.CreateVersion7(), null, 10_000m);

        Assert.Equal(ModoNivel2.Ambos, cmd.ModoNivel2);
        Assert.True(_validator.Validate(cmd).IsValid);
    }
}
