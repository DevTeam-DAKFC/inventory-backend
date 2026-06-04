using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{   
    public partial class InitialCreate : Migration
    {        
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    barcode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    image_url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    min_stock = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                    table.CheckConstraint("CK_products_min_stock", "[min_stock] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    total_rows = table.Column<int>(type: "int", nullable: false),
                    valid_rows = table.Column<int>(type: "int", nullable: false),
                    invalid_rows = table.Column<int>(type: "int", nullable: false),
                    imported_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_batches", x => x.id);
                    table.CheckConstraint("CK_import_batches_invalid_rows", "[invalid_rows] >= 0");
                    table.CheckConstraint("CK_import_batches_total_rows", "[total_rows] >= 0");
                    table.CheckConstraint("CK_import_batches_valid_rows", "[valid_rows] >= 0");
                    table.ForeignKey(
                        name: "FK_import_batches_users_imported_by",
                        column: x => x.imported_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    branch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    previous_stock = table.Column<int>(type: "int", nullable: false),
                    resulting_stock = table.Column<int>(type: "int", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_movements", x => x.id);
                    table.CheckConstraint("CK_inventory_movements_previous_stock", "[previous_stock] >= 0");
                    table.CheckConstraint("CK_inventory_movements_quantity", "[quantity] > 0");
                    table.CheckConstraint("CK_inventory_movements_resulting_stock", "[resulting_stock] >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_movements_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_movements_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_movements_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_notification_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_batch_errors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    row_number = table.Column<int>(type: "int", nullable: false),
                    field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_batch_errors", x => x.id);
                    table.CheckConstraint("CK_import_batch_errors_row_number", "[row_number] > 0");
                    table.ForeignKey(
                        name: "FK_import_batch_errors_import_batches_import_batch_id",
                        column: x => x.import_batch_id,
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    branch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    available_quantity = table.Column<int>(type: "int", nullable: false),
                    min_stock = table.Column<int>(type: "int", nullable: false),
                    last_movement_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_movement_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stocks", x => x.id);
                    table.CheckConstraint("CK_stocks_available_quantity", "[available_quantity] >= 0");
                    table.CheckConstraint("CK_stocks_min_stock", "[min_stock] >= 0");
                    table.ForeignKey(
                        name: "FK_stocks_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stocks_inventory_movements_last_movement_id",
                        column: x => x.last_movement_id,
                        principalTable: "inventory_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stocks_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_batch_errors_import_batch_id",
                table: "import_batch_errors",
                column: "import_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_imported_by",
                table: "import_batches",
                column: "imported_by");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_branch_id",
                table: "inventory_movements",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_created_at",
                table: "inventory_movements",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_product_id",
                table: "inventory_movements",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_user_id",
                table: "inventory_movements",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_notification_tokens_user_id",
                table: "notification_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "UX_notification_tokens_token",
                table: "notification_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_products_barcode_filtered",
                table: "products",
                column: "barcode",
                unique: true,
                filter: "[barcode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_products_sku",
                table: "products",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stocks_branch_id",
                table: "stocks",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_stocks_last_movement_id",
                table: "stocks",
                column: "last_movement_id");

            migrationBuilder.CreateIndex(
                name: "UX_stocks_product_id_branch_id",
                table: "stocks",
                columns: new[] { "product_id", "branch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_users_email",
                table: "users",
                column: "email",
                unique: true);
        }
        
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_batch_errors");

            migrationBuilder.DropTable(
                name: "notification_tokens");

            migrationBuilder.DropTable(
                name: "stocks");

            migrationBuilder.DropTable(
                name: "import_batches");

            migrationBuilder.DropTable(
                name: "inventory_movements");

            migrationBuilder.DropTable(
                name: "branches");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
