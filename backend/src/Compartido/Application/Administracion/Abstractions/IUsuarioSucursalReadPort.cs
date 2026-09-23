namespace Millet.Administracion.Application.Abstractions;

/// <summary>
/// Puerto de lectura cross-módulo para consultar la asociación
/// Usuario ↔ Sucursal (F1-ADM-01 Fase 2). El agregado
/// <c>UsuarioSucursal</c> vive en el módulo Identidad (tabla
/// <c>identidad.usuario_sucursales</c>); como Compartido NO referencia
/// Identidad (evita dependencia circular — Identidad ya referencia
/// Compartido), el puerto se declara aquí (consumidor) y lo implementa
/// un adapter en <c>Identidad.Infrastructure.PublicAdapters</c>
/// (proveedor), registrado en el composition root (<c>Api/Program.cs</c>).
/// Mismo patrón invertido que <c>ISucursalReadPort</c> (Almacen/CxP →
/// Compartido), pero aquí el flujo de datos es al revés: Compartido →
/// Identidad.
///
/// <para>
/// Único consumidor por ahora: <see cref="SucursalScopeGuard"/>, usado
/// por los handlers de "listar X de una sucursal" para el guard 403 de
/// pertenencia.
/// </para>
/// </summary>
public interface IUsuarioSucursalReadPort
{
    /// <summary>
    /// True si existe una asignación Activa entre <paramref name="usuarioId"/>
    /// y <paramref name="sucursalId"/>.
    /// </summary>
    Task<bool> EstaAsociadoAsync(
        Guid usuarioId, Guid sucursalId, CancellationToken cancellationToken);
}
