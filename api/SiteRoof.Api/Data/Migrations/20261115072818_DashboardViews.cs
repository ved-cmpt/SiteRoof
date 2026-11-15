using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiteRoof.Api.Data.Migrations
{
    /// <summary>
    /// Creates the T-SQL views behind the dashboards. The SQL lives in Data/Sql/*.sql (embedded resources)
    /// so it can be read, reviewed and run on its own; see docs/DATABASE.md.
    /// </summary>
    public partial class DashboardViews : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SqlScripts.Read("vw_ProjectWorkOrderRollup.sql"));
            migrationBuilder.Sql(SqlScripts.Read("vw_WorkPackageCompletion.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_WorkPackageCompletion;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_ProjectWorkOrderRollup;");
        }
    }
}
