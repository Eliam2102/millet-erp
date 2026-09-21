import { AlertCircle } from 'lucide-react';
import { Alert, AlertTitle, AlertDescription } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';

export interface AuthErrorAlertProps {
  error: string;
  className?: string;
}

interface ParsedAuthError {
  title: string;
  detail?: string;
  code?: string;
  traceId?: string;
}

function parseAuthError(raw: string): ParsedAuthError {
  const jsonMatch = raw.match(/\{[\s\S]*\}/);
  if (jsonMatch) {
    try {
      const parsed = JSON.parse(jsonMatch[0]);
      if (parsed && typeof parsed === 'object') {
        const title =
          parsed.code === 'USUARIO_INACTIVO'
            ? 'Cuenta inactiva'
            : parsed.title || 'Error al iniciar sesión';
        const detail =
          parsed.detail && parsed.detail !== title
            ? parsed.detail
            : parsed.title !== title
              ? parsed.title
              : undefined;
        return {
          title,
          detail,
          code: parsed.code,
          traceId: parsed.traceId,
        };
      }
    } catch {
      // Fallback si no es JSON válido
    }
  }

  return {
    title: 'Error al iniciar sesión',
    detail: raw,
  };
}

/**
 * Componente de alerta para errores de autenticación.
 * Parsea el formato ProblemDetails (RFC 7807) del backend y lo presenta
 * usando los componentes oficiales Alert y Badge del Design System.
 */
export function AuthErrorAlert({ error, className }: AuthErrorAlertProps) {
  const parsed = parseAuthError(error);

  return (
    <Alert variant="destructive" className={className}>
      <AlertCircle className="h-4 w-4" />
      <div className="flex flex-wrap items-center gap-2">
        <AlertTitle className="mb-0 font-semibold text-sm">
          {parsed.title}
        </AlertTitle>
        {parsed.code && (
          <Badge
            variant="destructive"
            className="text-[10px] uppercase font-mono tracking-wider px-1.5 py-0 border border-destructive-foreground/20"
          >
            {parsed.code}
          </Badge>
        )}
      </div>
      {parsed.detail && (
        <AlertDescription className="mt-1.5 text-xs leading-relaxed opacity-90">
          {parsed.detail}
        </AlertDescription>
      )}
      {parsed.traceId && (
        <p className="mt-2 text-[10px] font-mono opacity-60">
          Rastreo: {parsed.traceId}
        </p>
      )}
    </Alert>
  );
}
