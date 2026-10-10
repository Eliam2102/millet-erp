# Prueba aislada de layout INI-ROL

Renderiza el `PanelInicio` real, sus tokens y componentes. Usa permisos en el store y datos **ficticios**, sin iniciar sesión ni llamar al ERP. No prueba integración ni aceptación visual del cliente.

Desde `frontend`, en un entorno que permita Chromium:

```sh
npx vite build --config tools/ini-rol-layout/vite.config.mjs --outDir /tmp/ini-rol-layout-dist
node tools/ini-rol-layout/verificar.cjs
```

Necesita Playwright disponible. Si está instalado en otra ubicación, indicar la carpeta del paquete con `INI_ROL_PLAYWRIGHT`. No se agregó una dependencia al proyecto. `INI_ROL_LAYOUT_DIST` permite cambiar la carpeta del build.

El navegador intercepta todas las solicitudes; sirve el build y fixtures en memoria, sin un servidor ni puertos abiertos. Recorre Compras, Almacén, Cobranza, Tesorería y Administrador a 1440×900, 1280×800 y 390×844. Falla ante scroll de página, desbordamiento horizontal o recorte de indicadores/visuales en escritorio. Genera `/tmp/ini-rol-layout.json` y una captura del Administrador a 1440×900.

**Ejecutada el 10-oct-2026 en la Mac mini:** los 5 perfiles a 1440×900 y 1280×800 caben sin scroll de página, sin desbordamiento horizontal y sin tarjetas recortadas. En 390×844 la página se desplaza, como pide la plantilla móvil de DESIGN §5.5. La ejecución detectó y corrigió dos fallas de color: las barras de «Flujo de la semana» y los puntos de urgencia usaban clases `*-solid` que no existen en los tokens.

Pendiente: comparar con la propuesta aprobada y revisar el shell con una sesión real autorizada.
