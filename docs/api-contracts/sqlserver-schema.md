# Contrato de esquema SQL Server

## 1. Propósito

Este documento define el esquema de persistencia planificado en SQL Server para el backend ASP.NET Core Web API del sistema de inventario.

SQL Server será la capa principal de persistencia detrás del backend. El backend todavía no está implementado, por lo que este documento funciona como una guía para futuras entidades de EF Core, migraciones y repositorios.

La aplicación Flutter no se conectará directamente a SQL Server. Flutter consumirá la API REST definida para el backend, usando Dio / HttpClient desde la aplicación móvil.

## 2. Relación con otros contratos

- `openapi.inventory-api.yaml` define el contrato REST esperado por la aplicación móvil.
- `sqlserver-schema.md` define el esquema de persistencia planificado para SQL Server.
- `mock-data.md` contiene datos semilla para demos, pruebas y desarrollo.

## 3. Convenciones de nombres

- Usar nombres `snake_case` para tablas y columnas.
- Usar nombres plurales para tablas.
- Usar `uniqueidentifier` como llave primaria.
- Usar `datetime2` para marcas de tiempo.
- Usar índices para consultas de búsqueda, filtros y relaciones frecuentes.
- Mantener restricciones `check` para reglas básicas de cantidades y estados.

## 4. Tablas planificadas

### 4.1 users

Propósito: almacenar usuarios, credenciales y rol de acceso.

Columnas:

| Columna | Tipo | Nulo | Nota |
|---|---|---:|---|
| id | uniqueidentifier | No | Identificador del usuario |
| name | nvarchar(150) | No | Nombre visible |
| email | nvarchar(255) | No | Correo para autenticación |
| password_hash | nvarchar(max) | No | Hash de contraseña |
| role | nvarchar(50) | No | Rol del usuario |
| is_active | bit | No | Estado activo/inactivo |
| created_at | datetime2 | No | Fecha de creación |
| updated_at | datetime2 | Sí | Fecha de última actualización |

Llave primaria:

- `PK_users` sobre `id`.

Llaves foráneas:

- No aplica.

Restricciones importantes:

- `email` debe ser único.
- `role` debe limitarse a valores soportados por el backend.

Índices sugeridos:

- `UX_users_email` único sobre `email`.
- `IX_users_role` sobre `role`.
- `IX_users_is_active` sobre `is_active`.

### 4.2 branches

Propósito: almacenar sucursales donde se administra inventario.

Columnas:

| Columna | Tipo | Nulo | Nota |
|---|---|---:|---|
| id | uniqueidentifier | No | Identificador de la sucursal |
| name | nvarchar(150) | No | Nombre de la sucursal |
| address | nvarchar(300) | Sí | Dirección descriptiva |
| is_active | bit | No | Estado activo/inactivo |
| created_at | datetime2 | No | Fecha de creación |
| updated_at | datetime2 | Sí | Fecha de última actualización |

Llave primaria:

- `PK_branches` sobre `id`.

Llaves foráneas:

- No aplica.

Restricciones importantes:

- `name` no debe estar vacío.

Índices sugeridos:

- `IX_branches_name` sobre `name`.
- `IX_branches_is_active` sobre `is_active`.

### 4.3 products

Propósito: almacenar el catálogo de productos. El stock no se guarda en esta tabla.

Columnas:

| Columna | Tipo | Nulo | Nota |
|---|---|---:|---|
| id | uniqueidentifier | No | Identificador del producto |
| name | nvarchar(150) | No | Nombre del producto |
| sku | nvarchar(100) | No | Código interno único |
| barcode | nvarchar(32) | Sí | Código de barras opcional |
| category | nvarchar(100) | No | Categoría del producto |
| description | nvarchar(500) | Sí | Descripción breve |
| image_url | nvarchar(1000) | Sí | URL de imagen, posiblemente desde Storage |
| min_stock | int | No | Stock mínimo sugerido |
| is_active | bit | No | Estado activo/inactivo |
| created_at | datetime2 | No | Fecha de creación |
| updated_at | datetime2 | Sí | Fecha de última actualización |

Llave primaria:

- `PK_products` sobre `id`.

Llaves foráneas:

- No aplica.

Restricciones importantes:

- `sku` debe ser único.
- `barcode` debe ser único cuando tenga valor.
- `min_stock >= 0`.

Índices sugeridos:

- `UX_products_sku` único sobre `sku`.
- `UX_products_barcode_filtered` único filtrado sobre `barcode` cuando `barcode IS NOT NULL`.
- `IX_products_name` sobre `name`.
- `IX_products_category` sobre `category`.
- `IX_products_is_active` sobre `is_active`.

### 4.4 stocks

Propósito: almacenar la cantidad disponible de cada producto por sucursal.

Columnas:

| Columna | Tipo | Nulo | Nota |
|---|---|---:|---|
| id | uniqueidentifier | No | Identificador del registro de stock |
| product_id | uniqueidentifier | No | Producto asociado |
| branch_id | uniqueidentifier | No | Sucursal asociada |
| available_quantity | int | No | Cantidad disponible |
| min_stock | int | No | Mínimo aplicable por producto y sucursal |
| last_movement_id | uniqueidentifier | Sí | Último movimiento aplicado |
| last_movement_at | datetime2 | Sí | Fecha del último movimiento |
| updated_at | datetime2 | Sí | Fecha de última actualización |

Llave primaria:

- `PK_stocks` sobre `id`.

Llaves foráneas:

- `FK_stocks_products_product_id` hacia `products(id)`.
- `FK_stocks_branches_branch_id` hacia `branches(id)`.
- `FK_stocks_inventory_movements_last_movement_id` hacia `inventory_movements(id)`, nullable.

Restricciones importantes:

- `unique(product_id, branch_id)`.
- `available_quantity >= 0`.
- `min_stock >= 0`.

Índices sugeridos:

- `UX_stocks_product_id_branch_id` único sobre `product_id, branch_id`.
- `IX_stocks_branch_id` sobre `branch_id`.
- `IX_stocks_product_id` sobre `product_id`.
- `IX_stocks_available_quantity` sobre `available_quantity`.

### 4.5 inventory_movements

Propósito: registrar entradas y salidas de inventario.

Columnas:

| Columna | Tipo | Nulo | Nota |
|---|---|---:|---|
| id | uniqueidentifier | No | Identificador del movimiento |
| product_id | uniqueidentifier | No | Producto afectado |
| branch_id | uniqueidentifier | No | Sucursal afectada |
| user_id | uniqueidentifier | No | Usuario que registró el movimiento |
| type | nvarchar(50) | No | Tipo de movimiento |
| quantity | int | No | Cantidad movida |
| previous_stock | int | No | Stock antes del movimiento |
| resulting_stock | int | No | Stock después del movimiento |
| reason | nvarchar(200) | No | Motivo del movimiento |
| notes | nvarchar(500) | Sí | Notas opcionales |
| created_at | datetime2 | No | Fecha de creación |

Llave primaria:

- `PK_inventory_movements` sobre `id`.

Llaves foráneas:

- `FK_inventory_movements_products_product_id` hacia `products(id)`.
- `FK_inventory_movements_branches_branch_id` hacia `branches(id)`.
- `FK_inventory_movements_users_user_id` hacia `users(id)`.

Restricciones importantes:

- `quantity > 0`.
- `previous_stock >= 0`.
- `resulting_stock >= 0`.
- `type` debe limitarse a valores soportados por el backend, como `incoming` y `outgoing`.

Índices sugeridos:

- `IX_inventory_movements_product_id` sobre `product_id`.
- `IX_inventory_movements_branch_id` sobre `branch_id`.
- `IX_inventory_movements_user_id` sobre `user_id`.
- `IX_inventory_movements_type` sobre `type`.
- `IX_inventory_movements_created_at` sobre `created_at`.

### 4.6 notification_tokens

Propósito: almacenar tokens de dispositivos para Firebase Cloud Messaging.

Columnas:

| Columna | Tipo | Nulo | Nota |
|---|---|---:|---|
| id | uniqueidentifier | No | Identificador del token |
| user_id | uniqueidentifier | No | Usuario propietario del token |
| token | nvarchar(500) | No | Token FCM del dispositivo |
| platform | nvarchar(50) | No | Plataforma reportada |
| created_at | datetime2 | No | Fecha de creación |
| updated_at | datetime2 | Sí | Fecha de última actualización |

Llave primaria:

- `PK_notification_tokens` sobre `id`.

Llaves foráneas:

- `FK_notification_tokens_users_user_id` hacia `users(id)`.

Restricciones importantes:

- `token` puede ser único si el backend decide evitar duplicados globales.
- `platform` debe limitarse a valores soportados por el backend.

Índices sugeridos:

- `IX_notification_tokens_user_id` sobre `user_id`.
- `UX_notification_tokens_token` único sobre `token`, si corresponde.
- `IX_notification_tokens_platform` sobre `platform`.

### 4.7 import_batches

Propósito: registrar lotes de importación CSV cuando la funcionalidad esté disponible.

Columnas:

| Columna | Tipo | Nulo | Nota |
|---|---|---:|---|
| id | uniqueidentifier | No | Identificador del lote |
| file_name | nvarchar(255) | No | Nombre del archivo |
| status | nvarchar(50) | No | Estado del lote |
| total_rows | int | No | Total de filas |
| valid_rows | int | No | Filas válidas |
| invalid_rows | int | No | Filas inválidas |
| imported_by | uniqueidentifier | No | Usuario que inició la importación |
| created_at | datetime2 | No | Fecha de creación |
| completed_at | datetime2 | Sí | Fecha de finalización |

Llave primaria:

- `PK_import_batches` sobre `id`.

Llaves foráneas:

- `FK_import_batches_users_imported_by` hacia `users(id)`.

Restricciones importantes:

- `total_rows >= 0`.
- `valid_rows >= 0`.
- `invalid_rows >= 0`.
- `status` debe limitarse a valores soportados por el backend.

Índices sugeridos:

- `IX_import_batches_imported_by` sobre `imported_by`.
- `IX_import_batches_status` sobre `status`.
- `IX_import_batches_created_at` sobre `created_at`.

### 4.8 import_batch_errors

Propósito: almacenar errores por fila y campo dentro de un lote de importación.

Columnas:

| Columna | Tipo | Nulo | Nota |
|---|---|---:|---|
| id | uniqueidentifier | No | Identificador del error |
| import_batch_id | uniqueidentifier | No | Lote asociado |
| row_number | int | No | Número de fila |
| field | nvarchar(100) | No | Campo con error |
| message | nvarchar(500) | No | Mensaje del error |

Llave primaria:

- `PK_import_batch_errors` sobre `id`.

Llaves foráneas:

- `FK_import_batch_errors_import_batches_import_batch_id` hacia `import_batches(id)`.

Restricciones importantes:

- `row_number > 0`.
- `field` no debe estar vacío.
- `message` no debe estar vacío.

Índices sugeridos:

- `IX_import_batch_errors_import_batch_id` sobre `import_batch_id`.
- `IX_import_batch_errors_row_number` sobre `row_number`.

## 5. Reglas de transacción (transaction) para movimientos de inventario

- El stock no debe actualizarse directamente desde Flutter.
- Flutter debe llamar a `POST /inventory-movements`.
- El backend debe validar producto, sucursal, usuario y stock disponible.
- El backend debe insertar una fila en `inventory_movements`.
- El backend debe actualizar la fila correspondiente en `stocks`.
- La inserción del movimiento y la actualización de stock deben ejecutarse atómicamente en una sola transacción SQL.
- Los movimientos de salida deben fallar si `available_quantity` es insuficiente.
- `resulting_stock` debe coincidir con el valor final de `stocks.available_quantity`.

## 6. Alcance fuera de este documento

- Este documento no crea migraciones.
- Este documento no crea código backend.
- Este documento no crea `docker-compose.yml`.
- Este documento no indica que SQL Server, Docker Compose o el backend ya existan.
