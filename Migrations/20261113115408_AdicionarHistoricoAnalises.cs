using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RequisitaPlus.Web.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarHistoricoAnalises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalisesRequisitos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequisitoId = table.Column<int>(type: "INTEGER", nullable: false),
                    DataAnalise = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResultadoJson = table.Column<string>(type: "TEXT", nullable: false),
                    TokensEntrada = table.Column<int>(type: "INTEGER", nullable: true),
                    TokensSaida = table.Column<int>(type: "INTEGER", nullable: true),
                    TokensTotal = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalisesRequisitos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalisesRequisitos_Requisitos_RequisitoId",
                        column: x => x.RequisitoId,
                        principalTable: "Requisitos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalisesRequisitos_RequisitoId_DataAnalise",
                table: "AnalisesRequisitos",
                columns: new[] { "RequisitoId", "DataAnalise" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalisesRequisitos");
        }
    }
}
