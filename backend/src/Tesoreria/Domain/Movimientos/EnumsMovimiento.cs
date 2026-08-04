namespace Millet.Tesoreria.Domain.Movimientos;

/// <summary>Sentido del movimiento bancario (§5 DDL: 1=Ingreso 2=Egreso).</summary>
public enum SentidoMovimiento : short
{
    Ingreso = 1,
    Egreso = 2,
}

/// <summary>
/// Estado de aplicación contra documentos — pasivos o confirmaciones de
/// depósito (§4.2 del levantamiento; se deriva de la suma de aplicaciones:
/// sin aplicaciones = NoAplicado, suma parcial = AplicadoParcial, suma
/// completa = Aplicado).
/// </summary>
public enum EstadoAplicacionMovimiento : short
{
    NoAplicado = 1,
    AplicadoParcial = 2,
    Aplicado = 3,
}

/// <summary>Estado de conciliación contra extracto (ortogonal al de aplicación).</summary>
public enum EstadoConciliacionMovimiento : short
{
    NoConciliado = 1,
    Conciliado = 2,
}

/// <summary>Tipo del beneficiario del movimiento (§5 DDL: 1=Proveedor 2=Cliente 3=Otro).</summary>
public enum BeneficiarioTipo : short
{
    Proveedor = 1,
    Cliente = 2,
    Otro = 3,
}
