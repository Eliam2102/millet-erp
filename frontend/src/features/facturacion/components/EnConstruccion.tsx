import { Hammer } from 'lucide-react';

/**
 * <c>&lt;EnConstruccion/&gt;</c> — panel neutro para las rutas del módulo
 * Facturación cuya pantalla real aún no llega. FE-F0 monta el shell, las
 * rutas y los guards por permiso; cada pantalla se entrega en su fase FE
 * (ver <c>07-frontend-pr-breakdown.md</c>). Hasta entonces, la ruta
 * existe (navegable, gateada) y renderiza este aviso indicando qué fase
 * la entrega — evita el 404 y comunica el estado al usuario.
 */
export function EnConstruccion({
  titulo,
  descripcion,
  fase,
}: {
  titulo: string;
  descripcion: string;
  fase: string;
}) {
  return (
    <div className="mx-auto max-w-3xl px-4 py-8">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold">{titulo}</h1>
        <p className="text-sm text-muted-foreground">{descripcion}</p>
      </header>

      <div className="mt-6 flex items-start gap-3 rounded-md border border-dashed border-border bg-muted/40 px-4 py-6">
        <Hammer
          className="mt-0.5 h-5 w-5 shrink-0 text-muted-foreground"
          aria-hidden="true"
        />
        <div className="space-y-1 text-sm">
          <p className="font-medium">Pantalla en construcción</p>
          <p className="text-muted-foreground">
            Esta sección se habilita en la fase{' '}
            <span className="font-mono">{fase}</span> del módulo
            Facturación. La ruta y los permisos ya están activos.
          </p>
        </div>
      </div>
    </div>
  );
}
