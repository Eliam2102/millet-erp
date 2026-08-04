import { Link } from '@tanstack/react-router';
import { BarChart3, History, Package } from 'lucide-react';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { cn } from '@/lib/utils';

/**
 * <c>P13 — Hub de reportes</c> (doc 07 §FE-F6-PR1). Lista las
 * pantallas de reportes operativos disponibles según permisos.
 * Cada reporte concreto vive en su propia ruta y usa
 * <c>&lt;ReporteShell/&gt;</c>.
 */
export function ReportesPage() {
  const puedeAlfak = useHasPermission(PermisosCanonicos.AlmacenReportesAlfak);
  const puedeMpCnk = useHasPermission(PermisosCanonicos.AlmacenReportesMpCnk);

  return (
    <div className="mx-auto max-w-5xl space-y-6 px-4 py-6">
      <header className="space-y-1">
        <h1 className="flex items-center gap-2 text-2xl font-semibold tracking-tight">
          <BarChart3 className="h-6 w-6 text-primary" />
          Reportes de Almacén
        </h1>
        <p className="text-sm text-muted-foreground">
          Reportes operativos basados en saldos y movimientos del módulo.
          Cada reporte se exporta a PDF e imprime desde su pantalla.
        </p>
      </header>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {puedeAlfak && (
          <ReporteCard
            to="/almacen/reportes/alfak-historial"
            titulo="ALFAK-HISTORIAL-ALMACEN"
            descripcion="Movimientos del periodo agrupados por sub-almacén / artículo: saldo inicial, entradas, salidas, ajustes, saldo final, CPP y valor."
            icon={History}
          />
        )}
        {puedeMpCnk && (
          <ReporteCard
            to="/almacen/reportes/mp-cnk"
            titulo="SAP-REPORTE-EXISTENCIA-MP-CNK"
            descripcion="Inventario diario de materiales directos no-vidrio (interlayer, silicones, pinturas, sellantes)."
            icon={Package}
          />
        )}
        {!puedeAlfak && !puedeMpCnk && (
          <div className="col-span-full rounded-md border bg-muted/30 px-4 py-6 text-center text-sm text-muted-foreground">
            Sin reportes disponibles para tu rol.
          </div>
        )}
      </div>
    </div>
  );
}

function ReporteCard({
  to,
  titulo,
  descripcion,
  icon: Icon,
}: {
  to: '/almacen/reportes/alfak-historial' | '/almacen/reportes/mp-cnk';
  titulo: string;
  descripcion: string;
  icon: typeof BarChart3;
}) {
  return (
    <Link
      to={to}
      className={cn(
        'group rounded-md border bg-card p-4 transition-colors hover:border-primary hover:bg-primary/5',
      )}
    >
      <div className="flex items-start gap-3">
        <Icon className="mt-0.5 h-5 w-5 shrink-0 text-primary" />
        <div className="min-w-0">
          <h3 className="text-sm font-semibold font-mono">{titulo}</h3>
          <p className="mt-1 text-xs text-muted-foreground">{descripcion}</p>
        </div>
      </div>
    </Link>
  );
}
