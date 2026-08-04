import { useState } from 'react';
import { toast } from 'sonner';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useCargarCfdiManual } from '@/features/cxp/api/useCfdis';
import type { IngresarCfdiResponse } from '@/features/cxp/api/types';

/**
 * <c>&lt;CargarCfdiSheet/&gt;</c> — carga manual de respaldo de un CFDI
 * 4.0 (canal <c>CargaManual</c>) cuando los canales automáticos
 * (descarga SAT vía FiscalAPI, mailbox) no aplican: proveedor extranjero
 * sin timbrado en el buzón, XML recuperado a mano del portal del SAT, etc.
 *
 * <para>Multipart contra <c>POST /cfdis/cargar</c>: campo <c>xml</c>
 * obligatorio + <c>pdf</c> opcional. El backend parsea, dedupe por UUID
 * (duplicado → Problem Details con <c>CFDI_DUPLICADO</c>) y deja el
 * CFDI en <c>PorProcesar</c>, igual que los canales automáticos.</para>
 */
export interface CargarCfdiSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /**
   * Notifica al caller el CFDI recién ingresado (antes de cerrar el
   * sheet). Lo usa la recepción de Almacén para auto-vincular el CFDI
   * cargado; la bandeja de CxP no lo necesita (la invalidación de
   * queries ya refresca la lista).
   */
  onCargado?: (response: IngresarCfdiResponse) => void;
}

export function CargarCfdiSheet({
  open,
  onOpenChange,
  onCargado,
}: CargarCfdiSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Cargar CFDI manualmente</SheetTitle>
          <SheetDescription>
            Canal de respaldo cuando la descarga SAT o el mailbox no
            aplican. El XML se valida y dedupe por UUID igual que los
            canales automáticos.
          </SheetDescription>
        </SheetHeader>
        {open && (
          <Form onClose={() => onOpenChange(false)} onCargado={onCargado} />
        )}
      </SheetContent>
    </Sheet>
  );
}

function Form({
  onClose,
  onCargado,
}: {
  onClose: () => void;
  onCargado?: (response: IngresarCfdiResponse) => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const cargar = useCargarCfdiManual();

  const [xml, setXml] = useState<File | null>(null);
  const [pdf, setPdf] = useState<File | null>(null);
  const [xmlError, setXmlError] = useState<string | null>(null);
  const [pdfError, setPdfError] = useState<string | null>(null);

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    let valido = true;
    if (!xml) {
      setXmlError('Selecciona el XML del CFDI.');
      valido = false;
    } else if (!xml.name.toLowerCase().endsWith('.xml')) {
      setXmlError('El archivo debe tener extensión .xml.');
      valido = false;
    }
    if (pdf && !pdf.name.toLowerCase().endsWith('.pdf')) {
      setPdfError('El archivo debe tener extensión .pdf.');
      valido = false;
    }
    if (!valido || !xml) return;

    cargar.mutate(
      { xml, pdf, idempotencyKey },
      {
        onSuccess: (response) => {
          toast.success('CFDI cargado', {
            description: `UUID ${response.uuidCfdi} quedó en Por procesar (canal Carga manual).`,
          });
          onCargado?.(response);
          onClose();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.status === 403) {
              // El 403 de política llega sin body útil; nombrar el permiso
              // aquí evita el toast críptico "HTTP 403".
              toast.error('Sin permiso para cargar CFDIs', {
                description:
                  "Requiere el permiso 'cuentas_por_pagar.cfdis.cargar-manual'; pídelo al administrador.",
              });
              return;
            }
            toast.error(error.problem.title, {
              description: error.problem.detail ?? undefined,
            });
            return;
          }
          toast.error('Error inesperado al cargar el CFDI.');
        },
      },
    );
  }

  return (
    <form onSubmit={onSubmit} className="space-y-4 px-4">
      <div className="space-y-1">
        <Label htmlFor="cfdi-xml">
          XML del CFDI <span className="text-destructive">*</span>
        </Label>
        <Input
          id="cfdi-xml"
          type="file"
          accept=".xml,text/xml,application/xml"
          aria-invalid={xmlError != null}
          aria-describedby={xmlError ? 'cfdi-xml-error' : undefined}
          onChange={(e) => {
            setXml(e.target.files?.[0] ?? null);
            setXmlError(null);
          }}
        />
        {xmlError && (
          <p id="cfdi-xml-error" className="text-xs text-destructive">
            {xmlError}
          </p>
        )}
        <p className="text-xs text-muted-foreground">
          CFDI 4.0 timbrado. El UUID, RFC emisor y totales se leen del XML.
        </p>
      </div>

      <div className="space-y-1">
        <Label htmlFor="cfdi-pdf">Representación impresa (PDF, opcional)</Label>
        <Input
          id="cfdi-pdf"
          type="file"
          accept=".pdf,application/pdf"
          aria-invalid={pdfError != null}
          aria-describedby={pdfError ? 'cfdi-pdf-error' : undefined}
          onChange={(e) => {
            setPdf(e.target.files?.[0] ?? null);
            setPdfError(null);
          }}
        />
        {pdfError && (
          <p id="cfdi-pdf-error" className="text-xs text-destructive">
            {pdfError}
          </p>
        )}
      </div>

      <SheetFooter className="px-0">
        <Button
          type="button"
          variant="ghost"
          onClick={onClose}
          disabled={cargar.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={cargar.isPending}>
          {cargar.isPending ? 'Cargando…' : 'Cargar CFDI'}
        </Button>
      </SheetFooter>
    </form>
  );
}
