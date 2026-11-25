using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RequisitaPlus.Web.Migrations
{
    /// <inheritdoc />
    public partial class TornarPropriedadeObrigatoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Falha antes da reconstrução da tabela se a associação explícita estiver incompleta.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "ValidacaoProprietarios" ("Pendentes" INTEGER NOT NULL CHECK ("Pendentes" = 0));
                INSERT INTO "ValidacaoProprietarios" SELECT COUNT(*) FROM "Requisitos"
                    WHERE "ProprietarioId" IS NULL OR trim("ProprietarioId") = ''
                    OR NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = "Requisitos"."ProprietarioId");
                DROP TABLE "ValidacaoProprietarios";
                """);
            migrationBuilder.AlterColumn<string>(
                name: "ProprietarioId",
                table: "Requisitos",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ProprietarioId",
                table: "Requisitos",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }
    }
}
