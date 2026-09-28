import './glass-loader.css';

interface GlassLoaderProps {
  /** Texto de estado para lectores de pantalla y para mostrar bajo los vidrios. */
  mensaje?: string;
}

/**
 * Pantalla de carga de arranque: los mismos tres paneles de vidrio del login
 * con un reflejo de luz que los recorre en secuencia. Sin logotipo ni
 * spinner genérico; la identidad la da el vidrio.
 */
export function GlassLoader({ mensaje = 'Preparando tu espacio de trabajo' }: GlassLoaderProps) {
  return (
    <div className="glass-loader" role="status" aria-live="polite">
      <div className="glass-loader__panes" aria-hidden="true">
        <span />
        <span />
        <span />
      </div>
      <p className="glass-loader__text">{mensaje}</p>
    </div>
  );
}
