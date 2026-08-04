using Millet.Compras.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Domain;

public class AprobadorDepartamentoTests
{
    private static AprobadorDepartamento NuevoValido(
        Guid? id = null,
        DateTimeOffset? vigenteDesde = null,
        string? motivo = null)
    {
        return new AprobadorDepartamento(
            id: id ?? Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            departamentoId: Guid.CreateVersion7(),
            rol: RolAprobador.JefeDpto,
            usuarioId: Guid.CreateVersion7(),
            vigenteDesde: vigenteDesde ?? DateTimeOffset.UtcNow,
            designadoPor: Guid.CreateVersion7(),
            motivo: motivo,
            createdAt: DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Constructor_Inicializa_Todos_Los_Campos_Y_VigenteHasta_Es_Null()
    {
        var ahora = DateTimeOffset.UtcNow;
        var a = NuevoValido(vigenteDesde: ahora);

        Assert.Equal(ahora, a.VigenteDesde);
        Assert.Null(a.VigenteHasta);
        Assert.Null(a.Motivo);
    }

    [Theory]
    [InlineData(RolAprobador.JefeDpto)]
    [InlineData(RolAprobador.JefeAlmacen)]
    [InlineData(RolAprobador.AutorizadorN2)]
    public void Constructor_Acepta_Los_3_Roles(RolAprobador rol)
    {
        var a = new AprobadorDepartamento(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            departamentoId: Guid.CreateVersion7(),
            rol: rol,
            usuarioId: Guid.CreateVersion7(),
            vigenteDesde: DateTimeOffset.UtcNow,
            designadoPor: Guid.CreateVersion7(),
            motivo: null,
            createdAt: DateTimeOffset.UtcNow);
        Assert.Equal(rol, a.Rol);
    }

    [Fact]
    public void Constructor_Id_Empty_Lanza_BusinessRuleException()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NuevoValido(id: Guid.Empty));
        Assert.Equal("APROBADOR_ID_INVALIDO", ex.Code);
    }

    [Fact]
    public void Constructor_EmpresaId_Empty_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            new AprobadorDepartamento(
                Guid.CreateVersion7(), Guid.Empty, Guid.CreateVersion7(),
                RolAprobador.JefeDpto, Guid.CreateVersion7(),
                DateTimeOffset.UtcNow, Guid.CreateVersion7(), null,
                DateTimeOffset.UtcNow));
        Assert.Equal("APROBADOR_EMPRESA_INVALIDA", ex.Code);
    }

    [Fact]
    public void Constructor_DesignadoPor_Empty_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            new AprobadorDepartamento(
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                RolAprobador.JefeDpto, Guid.CreateVersion7(),
                DateTimeOffset.UtcNow, Guid.Empty, null,
                DateTimeOffset.UtcNow));
        Assert.Equal("APROBADOR_DESIGNADO_POR_INVALIDO", ex.Code);
    }

    [Fact]
    public void Constructor_Motivo_Demasiado_Largo_Lanza_422()
    {
        var motivo = new string('x', 501);
        var ex = Assert.Throws<BusinessRuleException>(() => NuevoValido(motivo: motivo));
        Assert.Equal("APROBADOR_MOTIVO_DEMASIADO_LARGO", ex.Code);
    }

    [Fact]
    public void Constructor_Motivo_Whitespace_Se_Normaliza_A_Null()
    {
        var a = NuevoValido(motivo: "   ");
        Assert.Null(a.Motivo);
    }

    [Fact]
    public void Constructor_Motivo_Trim_Se_Aplica()
    {
        var a = NuevoValido(motivo: "  promovido  ");
        Assert.Equal("promovido", a.Motivo);
    }

    [Fact]
    public void Cerrar_Setea_VigenteHasta()
    {
        var desde = DateTimeOffset.UtcNow.AddDays(-30);
        var hasta = DateTimeOffset.UtcNow;
        var a = NuevoValido(vigenteDesde: desde);

        a.Cerrar(hasta);

        Assert.Equal(hasta, a.VigenteHasta);
    }

    [Fact]
    public void Cerrar_Idempotente_Cuando_Ya_Esta_Cerrado()
    {
        var desde = DateTimeOffset.UtcNow.AddDays(-30);
        var primer = DateTimeOffset.UtcNow.AddDays(-1);
        var segundo = DateTimeOffset.UtcNow;
        var a = NuevoValido(vigenteDesde: desde);

        a.Cerrar(primer);
        a.Cerrar(segundo);   // No-op porque ya está cerrado.

        Assert.Equal(primer, a.VigenteHasta);
    }

    [Fact]
    public void Cerrar_Antes_De_VigenteDesde_Lanza_422()
    {
        var desde = DateTimeOffset.UtcNow;
        var hasta = desde.AddDays(-1);   // Anterior a desde.
        var a = NuevoValido(vigenteDesde: desde);

        var ex = Assert.Throws<BusinessRuleException>(() => a.Cerrar(hasta));
        Assert.Equal("APROBADOR_CIERRE_INVALIDO", ex.Code);
    }
}
