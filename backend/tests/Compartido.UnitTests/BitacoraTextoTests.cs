using Millet.Administracion.Application.Auditoria;
using Xunit;

namespace Millet.Compartido.UnitTests;

public sealed class BitacoraTextoTests
{
    [Theory]
    [InlineData("Creó CuentaBancariaProveedor CuentaBancariaProveedor 01a18f7b", "CuentaBancariaProveedor", "Creó Cuenta bancaria proveedor 01a18f7b")]
    [InlineData("Modificó Proveedor PRV-0901 — Estatus: EnRevision → Activo", "Proveedor", "Modificó Proveedor PRV-0901 — Estatus: En revisión → Activo")]
    [InlineData("ProductoAw 01a12c5c", null, "Producto A+W 01a12c5c")]
    [InlineData("Creó Empleado EMP-0012 · Juana Pérez", "Empleado", "Creó Empleado EMP-0012 · Juana Pérez")]
    public void Legible_quita_nombres_tecnicos(string texto, string? entidad, string esperado) =>
        Assert.Equal(esperado, BitacoraTexto.Legible(texto, entidad));
}
