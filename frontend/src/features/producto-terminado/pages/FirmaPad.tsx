import { useEffect, useRef } from 'react';
import { Eraser } from 'lucide-react';
import { Button } from '@/components/ui/button';

/** Firma con el dedo o el lápiz (PT5.6). Devuelve un PNG (data URL) o null si se borra. La foto de evidencia es la alternativa para quien no puede firmar en pantalla. */
export function FirmaPad({ valor, onChange }: { valor: string | null; onChange: (png: string | null) => void }) {
  const lienzo = useRef<HTMLCanvasElement>(null);
  const trazando = useRef(false);

  useEffect(() => {
    const c = lienzo.current;
    if (c && valor === null) c.getContext('2d')?.clearRect(0, 0, c.width, c.height);
  }, [valor]);

  const punto = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const c = lienzo.current!;
    const r = c.getBoundingClientRect();
    return { x: ((e.clientX - r.left) * c.width) / r.width, y: ((e.clientY - r.top) * c.height) / r.height };
  };
  const empezar = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const ctx = lienzo.current!.getContext('2d')!;
    e.currentTarget.setPointerCapture?.(e.pointerId);
    trazando.current = true;
    const { x, y } = punto(e);
    ctx.lineWidth = 3;
    ctx.lineCap = 'round';
    ctx.strokeStyle = getComputedStyle(lienzo.current!).color;
    ctx.beginPath();
    ctx.moveTo(x, y);
  };
  const mover = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!trazando.current) return;
    const ctx = lienzo.current!.getContext('2d')!;
    const { x, y } = punto(e);
    ctx.lineTo(x, y);
    ctx.stroke();
  };
  const terminar = () => {
    if (!trazando.current) return;
    trazando.current = false;
    onChange(lienzo.current!.toDataURL('image/png'));
  };

  return (
    <div className="flex flex-col gap-2">
      <canvas
        ref={lienzo}
        role="img"
        aria-label="Firma de quien recibe"
        width={600}
        height={240}
        className="h-40 w-full touch-none rounded-md border border-line bg-surface-card text-ink sm:h-48"
        onPointerDown={empezar}
        onPointerMove={mover}
        onPointerUp={terminar}
        onPointerLeave={terminar}
      />
      <div className="flex items-center justify-between gap-2">
        <p className="text-xs text-ink-muted">{valor ? 'Firma capturada.' : 'Pide que firme con el dedo dentro del recuadro.'}</p>
        <Button type="button" variant="outline" size="sm" className="min-h-11" disabled={!valor} title={!valor ? 'Captura una firma para poder borrarla.' : undefined} onClick={() => onChange(null)}>
          <Eraser className="size-4" aria-hidden="true" /> Borrar firma
        </Button>
      </div>
    </div>
  );
}
