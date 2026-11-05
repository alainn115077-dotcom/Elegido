# ELEGIDO MANAGER
Sistema web de administración para **Grupo Elegido Sierreño**: contrataciones, agenda, clientes, pagos, contratos PDF, consultas rápidas y organización interna del grupo.

**Tecnologías:** ASP.NET Core MVC 8 · C# · Entity Framework Core 8 · SQL Server · Bootstrap 5.3 · JavaScript · HTML/CSS · QuestPDF (PDF) · FullCalendar (calendario).
Sin inteligencia artificial ni APIs externas de IA: todas las respuestas salen de consultas reales a SQL Server.

---

## 1. Cómo ejecutarlo

### Requisitos
- Visual Studio 2022 (17.8 o superior) con la carga de trabajo **ASP.NET y desarrollo web**, o el **SDK de .NET 8**.
- **SQL Server** (Express, Developer o LocalDB).

### Pasos
1. Abre la carpeta `ElegidoManager` (o el archivo `ElegidoManager.csproj`) en Visual Studio.
2. Revisa la cadena de conexión en `appsettings.json`:
   ```json
   "ElegidoManager": "Server=localhost;Database=ElegidoManagerDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
   ```
   - SQL Server Express: `Server=localhost\\SQLEXPRESS;...`
   - LocalDB: `Server=(localdb)\\MSSQLLocalDB;...`
   - Con usuario y contraseña: `Server=localhost;Database=ElegidoManagerDB;User Id=sa;Password=TU_CLAVE;TrustServerCertificate=True;`
3. Ejecuta con **F5** (o `dotnet run`). Visual Studio descargará los paquetes NuGet la primera vez.
4. Al arrancar, el sistema **crea la base de datos y todas las tablas automáticamente**.
5. La primera vez verás la pantalla **Configuración inicial**: crea ahí la cuenta del administrador (no hay usuarios ni datos de demostración precargados).
6. Edita la sección `"Grupo"` de `appsettings.json` (representante, teléfono, ciudad, correo): esos datos aparecen en los contratos y recibos PDF.

> Si prefieres crear la base manualmente, ejecuta `Database/ElegidoManager.sql` en SSMS antes de arrancar. Crea exactamente la misma estructura.

---

## 2. Estructura del proyecto

```
ElegidoManager/
├── Controllers/
│   ├── CuentaController.cs          Login real (cookies + contraseñas con hash), configuración inicial, cambio de contraseña
│   ├── HomeController.cs            Dashboard
│   ├── AgendaController.cs          Calendario (JSON), disponibilidad en vivo, eventos anteriores
│   ├── ContratacionesController.cs  Alta/edición/detalle, cambio de estado, validación de empalmes
│   ├── ClientesController.cs        Clientes e historial
│   ├── PagosController.cs           Anticipos, abonos y liquidaciones
│   ├── FinanzasController.cs        Resumen mensual, ingresos por evento, gastos
│   ├── DocumentosController.cs      Generar / ver / descargar PDF
│   ├── ConsultasController.cs       Chat de consultas rápidas
│   ├── GrupoControllers.cs          Logística, Integrantes, Ensayos/Reuniones, Repertorio, Servicios
│   └── UsuariosController.cs        Administración de usuarios (solo administradores)
├── Data/AppDbContext.cs             Modelo EF Core: tablas, relaciones, índices y restricciones
├── Models/                          Entidades y catálogos
├── Services/
│   ├── AgendaService.cs             Cálculo de horarios y detección de empalmes
│   ├── ConsultasService.cs          Preguntas predefinidas → consultas LINQ/SQL
│   ├── DocumentosService.cs         Diseño de los PDF y monto en letra
│   └── WhatsAppService.cs           Enlaces wa.me con mensajes preparados
├── ViewModels/                      Modelos de formularios y pantallas
├── Views/                           Vistas Razor por módulo
├── wwwroot/css/site.css             Tema negro · dorado · blanco
└── wwwroot/js/                      site.js, agenda.js, consultas.js, contratacion-form.js
Database/ElegidoManager.sql          Script equivalente para SQL Server
```

---

## 3. Base de datos

| Tabla | Propósito | Relaciones |
|---|---|---|
| **Usuarios** | Acceso al sistema (hash PBKDF2, rol, activo) | — |
| **Clientes** | Nombre, teléfono (único lógico), ciudad | 1 → N Contrataciones |
| **Servicios** | Paquetes con precio base y duración | 1 → N Contrataciones (SET NULL) |
| **Contrataciones** | Tipo de evento, estado, precio acordado, observaciones | N → 1 Cliente; 1 → 1 Evento; 1 → N Pagos/Documentos/Gastos |
| **Eventos** | Inicio, fin, lugar, dirección, ciudad, punto/hora de reunión y salida | 1 → 1 Contratación (CASCADE) |
| **Pagos** | Anticipo / Abono / Liquidación, método, referencia | N → 1 Contratación |
| **Gastos** | Concepto, categoría, monto, evento opcional | N → 1 Contratación (SET NULL) |
| **Integrantes** | Músicos, instrumento, teléfono | — |
| **Reuniones** | Ensayos, juntas, grabaciones | — |
| **Canciones** | Repertorio musical | — |
| **Documentos** | PDF generados (folio único, contenido binario) | N → 1 Contratación; N → 1 Pago |

**Integridad y no duplicación**
- El **saldo pendiente no se guarda**: siempre se calcula como `PrecioAcordado − SUM(Pagos)`, así nunca queda desactualizado (también disponible en la vista `vw_SaldosContrataciones`).
- La **duración** se calcula de `Inicio`/`Fin`; si la hora final es menor a la inicial, el evento termina al día siguiente.
- Restricciones `CHECK`: precio ≥ 0, montos > 0, `Fin > Inicio`.
- No se pueden borrar clientes con contrataciones ni contrataciones con pagos o documentos (se cancelan para conservar el historial).
- Un cliente nuevo cuyo teléfono ya existe se reutiliza en lugar de duplicarse.

---

## 4. Módulos

| Módulo | Qué hace |
|---|---|
| **Dashboard** | Presentaciones del mes, próximas, fechas disponibles, ingresos, anticipos, pendiente de cobro, confirmadas/pendientes, próximos eventos, fines de semana libres y gráfica de 6 meses. |
| **Agenda** | Vista mensual, semanal, diaria y lista. Verde = confirmado, amarillo = pendiente, rojo = cancelado. Al tocar un día muestra su disponibilidad y permite registrar una contratación. Historial de eventos anteriores. |
| **Contrataciones** | Formulario en un paso (cliente nuevo o registrado, evento, horario, lugar, precio, anticipo, logística). Verifica en vivo la disponibilidad y **bloquea empalmes** (con margen de traslado configurable en `Agenda:MargenTrasladoMinutos`). |
| **Clientes** | Alta, edición, búsqueda e historial completo de contrataciones y saldos. |
| **Finanzas** | Pagos con tipo automático (anticipo / abono / liquidación), no permite pagar de más, saldos automáticos, ingresos por mes y por evento, gastos por categoría, ganancia e historial de movimientos. |
| **Documentos** | Contrato, comprobante de anticipo/abono y recibo de liquidación en PDF con folio (`CTR-2026-0001`), monto en letra y firmas. Se guardan en la base para consultarlos y descargarlos después. |
| **Consultas rápidas** | Chat con 15 preguntas predefinidas (agenda, finanzas, contrataciones). Las que requieren fecha o mes muestran un selector. Cada respuesta se arma con consultas reales a SQL Server, con tarjetas, mini calendario y accesos directos. |
| **Grupo** | Integrantes y contactos, ensayos/reuniones, repertorio, y logística de salidas (punto y hora de reunión, hora de salida, mapa). |
| **WhatsApp** | Botones en cada contratación con mensajes editables: confirmación, recordatorio de anticipo, saldo pendiente, confirmación de horario y agradecimiento. Usa enlaces `wa.me` (sin API). |
| **Ajustes** | Servicios/paquetes y usuarios (roles Administrador y Colaborador). |

---

## 5. Notas
- **QuestPDF** se usa con la licencia *Community* (gratuita para personas y empresas pequeñas).
- Bootstrap, iconos, fuentes y FullCalendar se cargan desde CDN; el equipo necesita internet para verlos con estilo.
- La base se crea con `EnsureCreated()`. Si después quieres usar migraciones de EF Core, borra la base de pruebas, genera la migración inicial con `Add-Migration Inicial` y cambia `EnsureCreated()` por `Migrate()` en `Program.cs`.


## Versión 2: nube, MongoDB, escritorio y seguridad

### Base de datos
- **MongoDB Atlas**: si existe `ConnectionStrings:MongoDB` (o la variable `MONGODB_URI`), la app usa MongoDB.
  Cada colección se llama como la entidad (Usuario, Cliente, Contratacion…); los PDF van en `Archivos`.
- **SQL Server**: si no hay cadena de MongoDB, usa `ConnectionStrings:ElegidoManager` (LocalDB / Azure SQL) como antes.
- En la PC, la cadena va en `ElegidoManager/appsettings.Local.json` (no se sube a GitHub). Ver `appsettings.Local.ejemplo.json`.
- En Vercel: variable de entorno `ConnectionStrings__MongoDB` (o `MONGODB_URI` de la integración de MongoDB Atlas).

### App de escritorio (ElegidoManager.Escritorio)
- Programa de Windows (WebView2) que abre la app publicada en la nube: siempre actualizado, mismos datos.
- Crear el .exe: doble clic en `CrearEjecutable.bat` → `AppEscritorio/ElegidoManager.exe`.
- La primera vez pide la dirección (ej. https://tu-app.vercel.app). Para cambiarla: abrir con **Shift** presionado.

### Seguridad
- Bloqueo de la cuenta 15 min tras 5 contraseñas incorrectas; máximo 10 intentos de login por minuto por IP.
- Contraseñas: mínimo 8 caracteres con letras y números.
- Al cambiar contraseña o desactivar un usuario se cierran sus sesiones abiertas.
- Cookies HttpOnly/SameSite, encabezados de seguridad (CSP, anti-clickjacking, nosniff), HTTPS en la nube.
- Bitácora (solo administrador): inicios de sesión, intentos fallidos, bloqueos y cambios.

### Extras
- **Calendario del celular**: botón "Calendario" en Inicio / "A mi calendario" en cada contratación, y en Agenda
  "Pasar próximas presentaciones a mi calendario" (archivo .ics con aviso 2 h antes). Lo pueden usar los integrantes.
- **Excel**: Contrataciones → botón Excel (CSV con fecha, cliente, lugar, precio, pagado y saldo).
- **Respaldo**: Ajustes → Descargar respaldo (JSON de toda la información, sin contraseñas ni PDF). Queda en la bitácora.
- **App instalable**: service worker con carga rápida de estilos/imágenes y pantalla "Sin conexión".
