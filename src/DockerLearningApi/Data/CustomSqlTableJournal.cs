using DbUp.Engine;
using DbUp.SqlServer;
using Microsoft.Data.SqlClient;
using System.Data;

namespace DockerLearningApi.Data;

/// <summary>
/// A customized SQL Server table journal that handles pre-existing journal tables
/// and avoids the "PK_SchemaVersions_Id already exists" error during tests
/// </summary>
public class CustomSqlTableJournal : SqlTableJournal
{
    public CustomSqlTableJournal(
        Func<SqlConnectionManager> connectionManager, 
        Func<DbUp.Engine.Output.IUpgradeLog> logger, 
        string schema, 
        string table) 
        : base(connectionManager, logger, schema, table)
    {
    }
    
    public override void EnsureTableExistsAndIsLatestVersion(Func<IDbCommand> dbCommandFactory)
    {
        try
        {
            // Call the base implementation, but catch any constraint violation errors
            base.EnsureTableExistsAndIsLatestVersion(dbCommandFactory);
        }
        catch (SqlException ex) when (ex.Number == 2714) // Error 2714 is "There is already an object named X"
        {
            // Suppressing the error - if the constraint already exists, that's fine
            // No need to log anything - we just want to prevent the exception from bubbling up
            // and causing the tests to fail unnecessarily
        }
    }
}