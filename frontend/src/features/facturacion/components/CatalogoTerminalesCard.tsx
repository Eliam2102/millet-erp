import { useState } from 'react';
import { toast } from 'sonner';
import { MapPin } from 'lucide-react';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useSucursales } from '@/features/catalogos/api';
import {
  useDesactivarTerminalAfiliacion,
  useGuardarTerminalAfiliacion,
  useTerminalesAfiliacion,
  useTerminalesSinCatalogar,
} from '@/features/facturacion/api/useTerminalTarjeta';
import { esApiError } from '@/lib/api';

function mensaje(e: unknown, defecto: string): string {
  return esApiError(e) ? (e.problem.detail ?? e.problem.title) : defecto;
}

/**
 * M-111: catálogo afiliación + terminal → sucursal. El emparejamiento de las
 * ventas de la terminal solo considera los cobros de la sucursal dada de alta
 * aquí; las terminales que traen los archivos y aún no están en el catálogo se
 * listan arriba para asignarles sucursal con un clic.
 */
export function CatalogoTerminalesCard() {
  const catalogo = useTerminalesAfiliacion();
  const sinCatalogar = useTerminalesSinCatalogar();
  const sucursales = useSucursales();
  const guardar = useGuardarTerminalAfiliacion();
  const desactivar = useDesactivarTerminalAfiliacion();
  const [afiliacion, setAfiliacion] = useState('');
  const [codTerminal, setCodTerminal] = useState('');
  const [descripcion, setDescripcion] = useState('');
  const [sucursalId, setSucursalId] = useState<string | null>(null);

  const nombreSucursal = (id: string) => {
    const s = sucursales.data?.items.find((x) => x.id === id);
    return s ? `${s.clave} · ${s.nombre}` : id;
  };

  function onGuardar() {
    if (sucursalId == null) return;
    guardar.mutate(
      { afiliacion: afiliacion.trim(), codTerminal: codTerminal.trim(), sucursalId, descripcion: descripcion.trim() || null },
      {
        onSuccess: () => {
          toast.success('Terminal guardada en el catálogo.');
          setAfiliacion('');
          setCodTerminal('');
          setDescripcion('');
        },
        onError: (e) => toast.error(mensaje(e, 'No se pudo guardar la terminal.')),
      },
    );
  }

  function onDesactivar(id: string) {
    desactivar.mutate(id, {
      onSuccess: () => toast.success('Terminal desactivada.'),
      onError: (e) => toast.error(mensaje(e, 'No se pudo desactivar la terminal.')),
    });
  }

  const pendientes = sinCatalogar.data ?? [];
  const filas = catalogo.data ?? [];
  const listo = afiliacion.trim() !== '' && codTerminal.trim() !== '' && sucursalId != null;

  return (
    <section className="space-y-3 rounded-md border border-line bg-surface-card p-4" aria-labelledby="catalogo-terminales-titulo">
      <div>
        <h2 id="catalogo-terminales-titulo" className="flex items-center gap-2 font-medium">
          <MapPin className="h-4 w-4" aria-hidden="true" />
          Terminales por sucursal
        </h2>
        <p className="text-xs text-ink-muted">
          Cada afiliación y terminal del banco pertenece a una sucursal. Sin catálogo, las ventas se emparejan con
          los cobros de cualquier sucursal.
        </p>
      </div>

      {pendientes.length > 0 && (
        <div className="space-y-1" role="status">
          <p className="text-sm font-medium">Sin sucursal asignada ({pendientes.length})</p>
          <ul className="flex flex-wrap gap-2">
            {pendientes.map((p) => (
              <li key={`${p.afiliacion}/${p.codTerminal}`}>
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => {
                    setAfiliacion(p.afiliacion);
                    setCodTerminal(p.codTerminal);
                  }}
                >
                  {p.afiliacion} / {p.codTerminal} · {p.ventas} venta(s)
                </Button>
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <Label htmlFor="cat-afiliacion">Afiliación</Label>
          <Input id="cat-afiliacion" value={afiliacion} maxLength={20} onChange={(e) => setAfiliacion(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="cat-terminal">Terminal</Label>
          <Input id="cat-terminal" value={codTerminal} maxLength={20} onChange={(e) => setCodTerminal(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="cat-sucursal">Sucursal</Label>
          <SucursalSelector value={sucursalId} onChange={setSucursalId} triggerProps={{ id: 'cat-sucursal' }} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="cat-descripcion">Descripción</Label>
          <Input id="cat-descripcion" value={descripcion} maxLength={200} onChange={(e) => setDescripcion(e.target.value)} />
        </div>
        <Button size="sm" onClick={onGuardar} disabled={!listo || guardar.isPending}>
          Guardar terminal
        </Button>
      </div>

      {filas.length === 0 ? (
        <p className="text-sm text-ink-muted">Aún no hay terminales en el catálogo.</p>
      ) : (
        <div className="overflow-x-auto rounded-md border border-line">
          <Table className="w-full text-sm">
            <TableHeader>
              <TableRow>
                <TableHead>Afiliación</TableHead>
                <TableHead>Terminal</TableHead>
                <TableHead>Sucursal</TableHead>
                <TableHead>Descripción</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filas.map((t) => (
                <TableRow key={t.id}>
                  <TableCell className="font-mono text-xs">{t.afiliacion}</TableCell>
                  <TableCell className="font-mono text-xs">{t.codTerminal}</TableCell>
                  <TableCell>{nombreSucursal(t.sucursalId)}</TableCell>
                  <TableCell className="text-xs">{t.descripcion ?? '—'}</TableCell>
                  <TableCell className="text-right">
                    <Button
                      size="sm"
                      variant="ghost"
                      disabled={desactivar.isPending}
                      onClick={() => onDesactivar(t.id)}
                      aria-label={`Desactivar terminal ${t.afiliacion} ${t.codTerminal}`}
                    >
                      Desactivar
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  );
}
