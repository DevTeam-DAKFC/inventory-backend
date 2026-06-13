# Inventory Backend

Backend REST para el sistema de gestión de inventario.

Este repositorio contiene la API ASP.NET Core que sirve como capa de negocio y persistencia del sistema. La aplicación móvil Flutter vive en el repositorio separado `inventory-mobile-project` y consume esta API mediante Dio / HttpClient.

## Tech stack

- ASP.NET Core sobre .NET 10
- SQL Server 2022 (vía Docker Compose para desarrollo local)
- Docker / Docker Compose
- Entity Framework Core (EF Core) para acceso a datos y migraciones
- Scalar API Reference + OpenAPI built-in (`Microsoft.AspNetCore.OpenApi`) para documentación interactiva

## Prerrequisitos

- .NET 10 SDK
- Docker Desktop (con motor Linux)
- PowerShell (los comandos de esta guía asumen sintaxis PowerShell en Windows)
- Opcional: DBeaver o SQL Server Management Studio para inspeccionar la base de datos local

## 1. Configuración del entorno

El repositorio incluye un archivo `.env.example` con la variable necesaria para levantar SQL Server. Crear el archivo `.env` local copiándolo:

```powershell
Copy-Item .env.example .env
```

Editar `.env` y reemplazar `MSSQL_SA_PASSWORD` por una contraseña local fuerte. Este valor se utiliza únicamente para el contenedor de SQL Server en el entorno de desarrollo del equipo y no debe reutilizarse en otros entornos. `.env` está ignorado por Git; solo `.env.example` se versiona.

## 2. Levantar SQL Server

Desde la raíz del repositorio:

```powershell
docker compose up -d sqlserver
docker compose ps
```

`docker compose ps` debe mostrar el contenedor `inventory-sqlserver` en estado `Up`, escuchando en `0.0.0.0:1433`.

## 3. Configurar la cadena de conexión

La API lee la cadena de conexión desde la configuración estándar de ASP.NET Core en `ConnectionStrings:DefaultConnection`. Para desarrollo local, definir una variable de entorno temporal en la misma sesión de PowerShell donde se ejecuten los comandos siguientes:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost,1433;Database=InventoryDb;User Id=sa;Password=<your-local-password>;Encrypt=True;TrustServerCertificate=True;"
```

Reemplazar `<your-local-password>` por el valor configurado en `.env`. Los archivos `appsettings.json` / `appsettings.Development.json` contienen un placeholder vacío y no almacenan contraseñas.

## 4. Aplicar migraciones

Con SQL Server arriba y la cadena de conexión definida, aplicar la migración inicial:

```powershell
dotnet ef database update --project .\src\Inventory.Api\Inventory.Api.csproj --startup-project .\src\Inventory.Api\Inventory.Api.csproj
```

Esto crea la base de datos `InventoryDb` y todas las tablas mapeadas (`users`, `branches`, `products`, `stocks`, `inventory_movements`, `notification_tokens`, `import_batches`, `import_batch_errors`) junto con la tabla `__EFMigrationsHistory`.

Si `dotnet ef` no está disponible, instalarlo como herramienta global:

```powershell
dotnet tool install --global dotnet-ef
```

## 5. Compilar y ejecutar la API

```powershell
dotnet restore .\Inventory.Backend.sln
dotnet build .\Inventory.Backend.sln
dotnet run --project .\src\Inventory.Api\Inventory.Api.csproj
```

Por defecto la API escucha en `http://localhost:5225` (perfil `http` definido en `launchSettings.json`).

## 6. Endpoints de validación

Con la API corriendo en `http://localhost:5225` se pueden validar las siguientes rutas:

- `GET http://localhost:5225/health` — health check; debe responder `200 OK` con `{"status":"ok","service":"Inventory.Api"}`.
- `http://localhost:5225/openapi/v1.json` — documento OpenAPI generado automáticamente (solo en Development).
- `http://localhost:5225/scalar/v1` — referencia interactiva de la API renderizada con Scalar (solo en Development).

## Estado actual del backend foundation

Ya implementado en esta rama:

- Scaffold de ASP.NET Core Web API con controllers habilitados.
- Endpoint `GET /health`.
- Setup de SQL Server vía `docker-compose.yml` y `.env.example`.
- Paquetes de EF Core e `InventoryDbContext` registrado en DI.
- Entidades y mapeos EF Core iniciales (`users`, `branches`, `products`, `stocks`, `inventory_movements`, `notification_tokens`, `import_batches`, `import_batch_errors`).
- Migración inicial `InitialCreate` creada y aplicada localmente.
- Documentación interactiva con Scalar en `/scalar/v1` y documento OpenAPI en `/openapi/v1.json`.

## Imágenes de productos

El backend recibe imágenes mediante `POST /products/{productId}/image` como
`multipart/form-data`, usando el campo `file`. Se aceptan únicamente
`image/jpeg`, `image/png` e `image/webp`, con un límite de 5 MB.

Los archivos se guardan en `wwwroot/uploads/products/`. `Product.ImageUrl`
almacena únicamente una ruta pública relativa con formato
`/uploads/products/{fileName}`, servida por ASP.NET Core como archivo estático.
Este flujo reemplaza los endpoints futuros `image-upload-url` e
`image-upload-complete`.

## Notification tokens

Los endpoints autenticados `POST /notification-tokens` y
`DELETE /notification-tokens/{tokenId}` permiten registrar, actualizar y
eliminar tokens FCM asociados al usuario actual. El backend evita duplicados
por token; todavía no identifica instalaciones mediante `deviceId`.

El envío real mediante Firebase Admin SDK y las notificaciones automáticas de
low stock quedan fuera del MVP actual.

## Pendiente (fuera del alcance de la base actual)

Los siguientes elementos aún no están implementados y se irán habilitando en bloques posteriores:

- Autenticación y autorización (login, tokens, roles).
- Endpoints de negocio (productos, sucursales, stock, movimientos, importación CSV, notificaciones).
- Capa de repositorios y servicios.
- Envío de notificaciones mediante Firebase Cloud Messaging.
- Pipelines de CI/CD.
- Despliegue en entornos remotos (este repositorio cubre por ahora únicamente el flujo local de desarrollo).
