using Millet.Compras.Application.Oc.RespuestaProveedor;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.UnitTests.Oc.Application;

public sealed class RegistrarRespuestaProveedorValidatorTests
{
    private static readonly RegistrarRespuestaProveedorValidator Validator = new();
    private static readonly DateOnly F = new(2026, 10, 20);

    private static CompromisoEntregaInput Comp(decimal cantidad = 10) => new(Guid.NewGuid(), cantidad, F);

    private static RegistrarRespuestaProveedorCommand Cmd(TipoRespuestaProveedorOc? tipo = TipoRespuestaProveedorOc.Confirmacion,
        string evidencia = "Correo P-881", string? motivo = null, params CompromisoEntregaInput[] compromisos) =>
        new(Guid.NewGuid(), tipo, F, evidencia, null, motivo, compromisos,
            new MemoryStream("%PDF-1.4 DEMO ficticio"u8.ToArray()), "DEMO.pdf", "application/pdf");

    [Fact]
    public void Solo_archivo_de_evidencia_es_valido()
    {
        using var archivo = new MemoryStream("%PDF-1.4 DEMO ficticio"u8.ToArray());
        var cmd = new RegistrarRespuestaProveedorCommand(Guid.NewGuid(), null, null, null, null, null, [],
            archivo, "DEMO.pdf", "application/pdf");
        Assert.True(Validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void Sin_archivo_rechaza_con_mensaje_claro()
    {
        var result = Validator.Validate(Cmd(compromisos: Comp()) with { Archivo = null });
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Adjunta el archivo de evidencia de la respuesta del proveedor.");
    }

    [Fact]
    public void Confirmacion_completa_es_valida() =>
        Assert.True(Validator.Validate(Cmd(compromisos: Comp())).IsValid);

    [Fact]
    public void Rechazo_con_motivo_es_valido() =>
        Assert.True(Validator.Validate(Cmd(TipoRespuestaProveedorOc.Rechazo, motivo: "Sin stock")).IsValid);

    [Fact]
    public void Rechazo_sin_motivo_es_valido() =>
        Assert.True(Validator.Validate(Cmd(TipoRespuestaProveedorOc.Rechazo)).IsValid);

    [Fact]
    public void Sin_clasificar_sin_motivo_es_valido() =>
        Assert.True(Validator.Validate(Cmd(null)).IsValid);

    [Fact]
    public void Rechazo_con_compromisos_es_invalido() =>
        Assert.False(Validator.Validate(Cmd(TipoRespuestaProveedorOc.Rechazo, motivo: "x", compromisos: Comp())).IsValid);

    [Fact]
    public void Confirmacion_sin_compromisos_es_valida() =>
        Assert.True(Validator.Validate(Cmd()).IsValid);

    [Fact]
    public void Cantidad_cero_es_invalida() =>
        Assert.False(Validator.Validate(Cmd(compromisos: Comp(0))).IsValid);

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void Nota_de_evidencia_en_blanco_es_valida_con_archivo(string evidencia) =>
        Assert.True(Validator.Validate(Cmd(evidencia: evidencia, compromisos: Comp())).IsValid);

    [Fact]
    public void Evidencia_de_301_caracteres_es_invalida() =>
        Assert.False(Validator.Validate(Cmd(evidencia: new string('x', 301), compromisos: Comp())).IsValid);

    [Fact]
    public void Mas_de_200_compromisos_es_invalido() =>
        Assert.False(Validator.Validate(Cmd(compromisos: Enumerable.Range(0, 201).Select(_ => Comp()).ToArray())).IsValid);
}
