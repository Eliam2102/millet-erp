using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Movimientos;
using Millet.Tesoreria.Application.PublicPorts;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Movimientos;

/// <summary>
/// 86e3tfdve (opción A): la cuenta ordenante del cliente se captura una vez al registrar el
/// ingreso, solo con el patrón general del SAT (la forma de pago aún no se conoce), se muestra
/// enmascarada y Facturación la lee por puerto. Cuentas ficticias.
/// </summary>
public sealed class MovimientoCuentaOrdenanteTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly CuentaBancaria Cuenta = new(Empresa, "Banco ficticio", "1234567890", null, "MXN");

    private static TesoreriaDbContext Db() => new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());

    private static MovimientoBancario Ingreso(string? ordenante) =>
        MovimientoBancario.RegistrarIngreso(Empresa, Cuenta, 100m, new DateOnly(2026, 10, 28), null, null, null, null, Usuario, Ahora,
            cuentaOrdenante: ordenante);

    [Theory]
    [InlineData("0000000010")]                 // 10 dígitos
    [InlineData("00000000011")]                // 11: cumple el general aunque no el de 03
    [InlineData("0000-0000 0000-0016")]        // separadores de captura
    [InlineData("000000000000000018")]         // CLABE
    [InlineData("cuenta_ficticia_01")]         // alfanumérico 10–50
    public void Ingreso_acepta_ordenante_con_el_patron_general_y_la_normaliza(string captura)
    {
        var m = Ingreso(captura);
        m.CuentaOrdenante.Should().NotBeNull().And.NotContain(" ").And.NotContain("-");
        m.CuentaOrdenante.Should().Be(m.CuentaOrdenante!.ToUpperInvariant());
    }

    [Theory]
    [InlineData("000000012")]                  // 9 caracteres
    [InlineData("CTA.FICTICIA1")]              // carácter fuera del patrón
    [InlineData("CUENTA*FICTICIA")]            // carácter fuera del patrón
    [InlineData("CUENTAFICTICIACUENTAFICTICIACUENTAFICTICIACUENTAFI1")] // 51 caracteres
    // Nota: 12 o 19 dígitos SÍ cumplen el patrón general del SAT ([A-Z0-9_]{10,50});
    // se rechazan después, con la forma de pago real, al revisar o emitir el REP.
    public void Ingreso_rechaza_ordenante_fuera_del_patron_general_sin_exponerla(string captura)
    {
        var act = () => Ingreso(captura);
        var ex = act.Should().Throw<BusinessRuleException>().Which;
        ex.Code.Should().Be("MOV_CUENTA_ORDENANTE_INVALIDA");
        ex.Message.Should().NotContain(captura);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Ingreso_sin_ordenante_es_valido(string? captura) => Ingreso(captura).CuentaOrdenante.Should().BeNull();

    [Fact]
    public void Validador_limita_la_ordenante_a_50_caracteres()
    {
        var v = new RegistrarMovimientoIngresoValidator();
        var cmd = new RegistrarMovimientoIngresoCommand(Guid.NewGuid(), 100m, new DateOnly(2026, 10, 28), CuentaOrdenante: new string('A', 51));
        v.Validate(cmd).Errors.Should().Contain(e => e.PropertyName == nameof(RegistrarMovimientoIngresoCommand.CuentaOrdenante));
        v.Validate(cmd with { CuentaOrdenante = new string('A', 50) }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, "•••• 0016")]
    [InlineData(true, "0000000000000016")]
    public void Respuesta_enmascara_la_ordenante_salvo_con_permiso_de_cuenta_completa(bool verCompleta, string esperado) =>
        MovimientoBancarioMapper.ToResponse(Ingreso("0000000000000016"), verCuentaCompleta: verCompleta)
            .CuentaOrdenante.Should().Be(esperado);

    [Fact]
    public async Task Puerto_publico_devuelve_la_ordenante_del_movimiento()
    {
        await using var db = Db();
        var con = Ingreso("0000000000000016");
        var sin = Ingreso(null);
        db.CuentasBancarias.Add(Cuenta);
        db.MovimientosBancarios.AddRange(con, sin);
        await db.SaveChangesAsync();
        var handler = new ConsultarCuentaOrdenanteMovimientoHandler(db);

        (await handler.Handle(new(con.Id), default)).Should().Be("0000000000000016");
        (await handler.Handle(new(sin.Id), default)).Should().BeNull();
        (await handler.Handle(new(Guid.NewGuid()), default)).Should().BeNull();
    }

    private sealed class EmpresaContext : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
}
