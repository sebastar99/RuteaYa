
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuteaYa.Entidades.Migrations
{
    public partial class CrearTablaEnvios : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Envios",
                columns: table => new
                {
                    Id = table.Column<int>(
                        type: "int",
                        nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),

                    UsuarioId = table.Column<int>(
                        type: "int",
                        nullable: false),

                    OrigenDir = table.Column<string>(
                        type: "nvarchar(200)",
                        maxLength: 200,
                        nullable: false),

                    OrigenLat = table.Column<decimal>(
                        type: "decimal(18,6)",
                        nullable: false),

                    OrigenLng = table.Column<decimal>(
                        type: "decimal(18,6)",
                        nullable: false),

                    DestinoDir = table.Column<string>(
                        type: "nvarchar(200)",
                        maxLength: 200,
                        nullable: false),

                    DestinoLat = table.Column<decimal>(
                        type: "decimal(18,6)",
                        nullable: false),

                    DestinoLng = table.Column<decimal>(
                        type: "decimal(18,6)",
                        nullable: false),

                    FechaLimite = table.Column<DateTime>(
                        type: "datetime2",
                        nullable: false),

                    PesoKg = table.Column<decimal>(
                        type: "decimal(10,2)",
                        nullable: false),

                    Tamano = table.Column<byte>(
                        type: "tinyint",
                        nullable: false),

                    Descripcion = table.Column<string>(
                        type: "nvarchar(500)",
                        maxLength: 500,
                        nullable: false),

                    Monto = table.Column<decimal>(
                        type: "decimal(10,2)",
                        nullable: false),

                    Estado = table.Column<byte>(
                        type: "tinyint",
                        nullable: false),

                    CreadoEn = table.Column<DateTime>(
                        type: "datetime2",
                        nullable: false),

                    RowVersion = table.Column<byte[]>(
                        type: "rowversion",
                        rowVersion: true,
                        nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Envios", x => x.Id);

                    table.ForeignKey(
                        name: "FK_Envios_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Envios_UsuarioId",
                table: "Envios",
                column: "UsuarioId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Envios");
        }
    }
}
