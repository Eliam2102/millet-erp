import { useState } from 'react';
import { Download } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { ReporteShell } from '@/components/erp';
import { useReporteDiot } from '@/features/cxp/api/useReportes';
import { backendToShellShape } from '@/features/cxp/lib/reportes-adapter';
import { descargarArchivo } from '@/features/facturacion/lib/descargas';
import { esApiError } from '@/lib/api';
import { mensajeErrorAccion } from '@/lib/api/error';
import { mesAnteriorIso } from '@/features/cxp/lib/diot';

/**
 * F1-CXP-10 · DIOT del mes por flujo de efectivo (pagos del mes) y archivo
 * TXT de carga masiva del SAT (54 campos). La vista y el Excel muestran
 * también el IVA pagado y el no acreditable por tercero.
 */
export function ReporteDiotPage() {
  const [periodo, setPeriodo] = useState<string>(mesAnteriorIso());
  const [submitted, setSubmitted] = useState(false);
  const [descargando, setDescargando] = useState(false);
  const [anio, mes] = periodo.split('-').map(Number);
  const valido = Number.isInteger(anio) && Number.isInteger(mes) && mes >= 1 && mes <= 12;

  const query = useReporteDiot({ anio, mes }, submitted && valido);

  const descargarTxt = async () => {
    setDescargando(true);
    try {
      await descargarArchivo(
        `/api/v1/cuentas-por-pagar/reportes/diot/archivo?anio=${anio}&mes=${mes}`,
        `DIOT_${anio}${String(mes).padStart(2, '0')}.txt`,
      );
    } catch (e) {
      toast.error(mensajeErrorAccion(e));
    } finally {
      setDescargando(false);
    }
  };

  return (
    <ReporteShell
      reporte={query.data ? backendToShellShape(query.data) : undefined}
      isLoading={submitted && query.isLoading}
      error={esApiError(query.error) ? query.error : undefined}
      onRetry={() => query.refetch()}
      filtrosUi={
        <div className="space-y-3">
          <div className="flex flex-wrap items-end gap-3">
            <div className="space-y-1">
              <Label htmlFor="periodoDiot" className="text-xs">
                Mes
              </Label>
              <Input
                id="periodoDiot"
                type="month"
                value={periodo}
                onChange={(e) => {
                  setPeriodo(e.target.value);
                  setSubmitted(false);
                }}
                className="w-44"
              />
            </div>
            <Button onClick={() => setSubmitted(true)} disabled={!valido}>
              Ejecutar
            </Button>
            <Button
              variant="outline"
              onClick={descargarTxt}
              disabled={!valido || descargando}
            >
              <Download className="mr-2 size-4" aria-hidden />
              {descargando ? 'Descargando…' : 'Archivo para el SAT (TXT)'}
            </Button>
          </div>
          <Alert>
            <AlertDescription className="text-xs">
              Base flujo de efectivo: pagos del mes, en pesos con el tipo de cambio del pago. Los
              proveedores extranjeros se muestran pero no van en el archivo: se declaran por la
              importación (pedimento), que llega en la siguiente entrega. Caja chica y viáticos aún
              no se incluyen.
            </AlertDescription>
          </Alert>
        </div>
      }
    />
  );
}
