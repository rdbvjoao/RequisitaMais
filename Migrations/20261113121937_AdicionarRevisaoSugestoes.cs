using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RequisitaPlus.Web.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarRevisaoSugestoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DecisoesSugestoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AnaliseRequisitoId = table.Column<int>(type: "INTEGER", nullable: false),
                    IndiceSugestao = table.Column<int>(type: "INTEGER", nullable: false),
                    Decisao = table.Column<int>(type: "INTEGER", nullable: false),
                    DescricaoAnterior = table.Column<string>(type: "TEXT", nullable: false),
                    RedacaoFinal = table.Column<string>(type: "TEXT", nullable: true),
                    UsuarioId = table.Column<string>(type: "TEXT", nullable: false),
                    DataDecisao = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisoesSugestoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DecisoesSugestoes_AnalisesRequisitos_AnaliseRequisitoId",
                        column: x => x.AnaliseRequisitoId,
                        principalTable: "AnalisesRequisitos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisoesSugestoes_AnaliseRequisitoId_IndiceSugestao",
                table: "DecisoesSugestoes",
                columns: new[] { "AnaliseRequisitoId", "IndiceSugestao" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisoesSugestoes");
        }
    }
}
