using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace ElegidoManager.Data
{
    /// <summary>Genera Ids numéricos con un contador atómico en MongoDB (igual que el IDENTITY de SQL Server).</summary>
    public class GeneradorIdMongo : ValueGenerator<int>
    {
        public override bool GeneratesTemporaryValues => false;

        public override int Next(EntityEntry entry)
        {
            var ctx = (AppDbContext)entry.Context;
            if (ctx.Mongo == null) throw new InvalidOperationException("MongoDB no está configurado.");
            return ctx.Mongo.SiguienteId(entry.Metadata.ClrType.Name);
        }
    }
}
