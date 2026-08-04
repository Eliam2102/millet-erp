namespace Millet.Api.Hubs;

/// <summary>
/// Modo de presencia que un usuario declara sobre un recurso colaborativo
/// (CollaborationHub Sprint 2, ADR-0012 Capa 2). Es <em>soft</em>: el
/// backend no impide que dos usuarios <see cref="Editing"/> el mismo
/// recurso — la protección final la da Capa 1
/// (<c>Version</c> / <c>IsConcurrencyToken</c>). Los modos sirven para
/// que el FE muestre awareness útil:
///
/// <list type="bullet">
///   <item><c>Viewing</c> — abrió la pantalla pero no está editando.</item>
///   <item><c>Editing</c> — empezó a tipear / enfocó un input. UI peer-side
///         pinta el indicador en color "cuidado" para evitar overwrites.</item>
/// </list>
/// </summary>
public enum SoftLockModo
{
    Viewing = 0,
    Editing = 1,
}
