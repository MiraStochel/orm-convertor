using System.Data.Entity;
using System.Data.Entity.Infrastructure;

namespace EF6Entities;

public class WWIDbConfiguration : DbConfiguration
{
    public WWIDbConfiguration()
    {
        SetExecutionStrategy("System.Data.SqlClient", () => new DefaultExecutionStrategy());
        SetDatabaseInitializer(new NullDatabaseInitializer<WWIContext>());
    }
}
